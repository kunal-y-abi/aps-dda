using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ApsDda.AspNetCore;
using ApsDda.Core;
using ApsDda.Runtime;

namespace ApsDda.Api;

/// <summary>Developer-run synthetic reference host. In-memory keys/trust mean this fixture deliberately requires a NEW database per process.</summary>
public sealed class SyntheticApiScenario : IDisposable, IBoundedPlanPreparer
{
    public const string Tenant = "synthetic-tenant";
    public const string Purpose = "internal-capacity-planning";
    public const string Target = "https://capacity.example.invalid/resources/north";
    public const string Audience = "https://capacity.example.invalid";
    public const string SemanticNamespace = "https://semantics.example.invalid/capacity";
    private readonly List<ECDsa> _keys = [];
    public WorkflowRuntime Runtime { get; }
    public SignedArtifact Manifest { get; }
    public SignedArtifact Capability { get; }
    public SignedArtifact Product { get; }
    public HttpResourceRegistration Resource { get; }
    public AuthenticatedPrincipal Executor { get; }
    public AuthenticatedPrincipal Approver { get; }
    public AuthenticatedPrincipal Reader { get; }
    private readonly ArtifactSigner _source;
    private readonly ArtifactSigner _orchestrator;
    private readonly ArtifactSigner _authority;
    private readonly JsonArray _semantics;
    public SyntheticApiScenario(string newDatabasePath, TimeProvider? clock = null, Func<ISourceAdapter, ISourceAdapter>? sourceDecorator = null)
    {
        if (File.Exists(newDatabasePath)) throw new InvalidOperationException("Synthetic host requires a new database path; it never resets existing data or replaces existing trust.");
        clock ??= TimeProvider.System;
        var now = clock.GetUtcNow(); var trust = new TrustedDirectory(Tenant, clock); var principals = new PrincipalDirectory();
        ArtifactSigner Signer(string issuer, ArtifactType[] types, string[] roles, string[]? audiences = null, string[]? endpoints = null, string[]? semantics = null)
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); _keys.Add(key); var keyId = issuer + "/keys/process-generated";
            trust.Register(new TrustedParticipant(issuer, [new TrustedKey(keyId, key.ExportSubjectPublicKeyInfo())], types, roles,
                [Artifact.SpecVersion], ["1.0.0"], bindings: ["https-json"], audiences: audiences, endpoints: endpoints, semanticNamespaces: semantics));
            return new ArtifactSigner(issuer, keyId, key);
        }
        _orchestrator = Signer("https://orchestrator.example.invalid", [ArtifactType.DecisionSpec, ArtifactType.ExecutionPlan, ArtifactType.CommandInvocation], ["APS-Orchestrator"]);
        _authority = Signer("https://authority.example.invalid", [ArtifactType.PolicyDecision, ArtifactType.Approval], ["APS-Policy-Service", "APS-Authority-Service"]);
        _source = Signer(Audience + "/issuer", [ArtifactType.CapabilityManifest, ArtifactType.DataProduct, ArtifactType.ProcessCapability, ArtifactType.EvidenceBundle, ArtifactType.OperationReceipt],
            ["APS-Read-Publisher", "APS-Action-Executor"], [Audience], [Audience + "/commands"], [SemanticNamespace]);
        _semantics = new JsonArray(ArtifactFactory.Semantic(SemanticNamespace, SemanticNamespace + "/units", "metric", "source-resource-available-units"));
        var schema = ArtifactFactory.SchemaReference("https://schema.example.invalid/internal-state", new JsonObject { ["type"] = "object" });
        Product = ArtifactFactory.DataProduct(_source, "Synthetic internal capacity", _semantics, schema, [Purpose], "local", now);
        Capability = ArtifactFactory.ProcessCapability(_source, "ReserveInternalCapacity", ArtifactFactory.Semantic(SemanticNamespace, SemanticNamespace + "/reserve", "command", "reserve-internal-capacity"),
            Audience + "/commands", Audience, schema, schema, "synthetic-capacity-v1", "one configured north resource", "capacity-operations-owner", now);
        trust.PinArtifact(Product.Artifact); trust.PinArtifact(Capability.Artifact);
        Manifest = _source.Issue(ArtifactType.CapabilityManifest, new JsonObject
        {
            ["roles"] = new JsonArray("APS-Read-Publisher", "APS-Action-Executor"), ["profileVersions"] = new JsonArray(Artifact.SpecVersion),
            ["bindings"] = new JsonArray("https-json"), ["endpoints"] = new JsonArray(Capability.Artifact.Body.Object("endpoint").DeepClone()),
            ["capabilityRefs"] = new JsonArray(Capability.Artifact.Reference.ToJson()), ["expiresAt"] = now.AddHours(8).ToString("O"),
            ["limits"] = new JsonObject { ["httpRequestBytes"] = 262144, ["httpPlanSteps"] = 32, ["exactReadRows"] = 1 }, ["residency"] = new JsonArray("local")
        }, now);
        trust.PinArtifact(Manifest.Artifact);
        AuthenticatedPrincipal Register(string identity, string subject, string role) => principals.Register(new(identity, Tenant, subject,
            "workload-" + identity, "executor-" + identity, [role], [Purpose], [Audience], [Target], [Capability.Artifact.Header.ContentDigest]));
        Executor = Register("sample-worker", "sample-planner", "Executor");
        Approver = Register("sample-reviewer", "sample-independent-reviewer", "Approver");
        Reader = Register("sample-reader", "sample-read-only", "Reader");
        var store = new SqliteRuntimeStore(newDatabasePath);
        var policy = new EligibilityPolicy("https://policy.example.invalid/internal-capacity", "1.0.0", true);
        store.SetCurrentPolicy(policy.PolicyReference);
        Runtime = new WorkflowRuntime(store, trust, principals, _orchestrator, _authority, policy, clock);
        var source = new SourceRegistration(Capability, Product, _source, [Target], [Purpose], [Executor.SubjectId], [Executor.WorkloadId], ["Executor"],
            [new EvidenceRule(Product.Artifact.Reference, _source.Issuer, _semantics, TimeSpan.FromMinutes(5), value => value.String("unit") == "internal-unit" && value.String("region") == "north")], maximumQuantity: 50);
        var adapter = new SqliteSourceAdapter(Runtime, source, new ReserveCapacityCommand());
        Runtime.RegisterSource(sourceDecorator?.Invoke(adapter) ?? adapter);
        store.SeedResource(_source.Issuer, Target, Tenant, new JsonObject { ["available"] = "20", ["reserved"] = "0" });
        Resource = new HttpResourceRegistration("north", Capability.Artifact.Reference, Target);
    }
    public ApsDdaHttpBinding CreateBinding(IEnumerable<KeyValuePair<string, string>> tokenToIdentity, bool allowDevelopmentLoopbackHttp = false) =>
        new(Runtime, Tenant, Purpose, Manifest, [Resource], tokenToIdentity, this, allowDevelopmentLoopbackHttp);
    public StorePlanRequest Prepare(PreparePlanRequest request, AuthenticatedPrincipal principal)
    {
        Runtime.Principals.RequireCurrent(principal, "Executor");
        if (request.ResourceAlias != Resource.Alias || principal.TenantId != Tenant || !principal.Purposes.Contains(Purpose) ||
            principal.SubjectId != Executor.SubjectId || principal.WorkloadId != Executor.WorkloadId || principal.ExecutorId != Executor.ExecutorId)
            throw new ApsDdaException("AUTH_DENIED", "Synthetic factory requires the configured executor context.");
        var quantity = CanonicalQuantity.Parse(request.Quantity);
        if (quantity > 50) throw new ApsDdaException("QUOTA_EXCEEDED", "Configured internal quantity bound exceeded.");
        _ = CanonicalQuantity.Parse(request.ExpectedVersion);
        var state = Runtime.Store.GetResource(_source.Issuer, Target);
        if (state.Version != request.ExpectedVersion) throw new ApsDdaException("STALE_PRECONDITION", "Requested source version is not current.");
        var now = Runtime.Clock.GetUtcNow(); var context = ArtifactFactory.Context(principal, Purpose, Guid.NewGuid(), Runtime.Policy.PolicyReference);
        var valueRef = Runtime.Store.RetainEvidenceValue(new JsonObject { ["unit"] = "internal-unit", ["region"] = "north", ["available"] = state.State.String("available") });
        var evidence = ArtifactFactory.Evidence(_source, context, Product.Artifact, _semantics, Jcs.Sha256Digest(new JsonObject { ["resource"] = Target }), state.Version, valueRef, now, now);
        Runtime.Retain(evidence, new(ArtifactType.EvidenceBundle, "APS-Read-Publisher", _source.Issuer));
        var decision = ArtifactFactory.Decision(_orchestrator, context, "synthetic-planning-owner", "Reserve internal capacity?", "Prepare internal resource readiness",
            ["source-owned available capacity"], ["reserve", "manual"], [evidence.Artifact.Reference], "deterministic-rules", "1.0.0", new JsonObject { ["quantity"] = request.Quantity },
            ["synthetic data only"], ["not an actuarial forecast or external validation"], "reserve internal units", "eligible", now);
        Runtime.Retain(decision, new(ArtifactType.DecisionSpec, "APS-Orchestrator", _orchestrator.Issuer));
        var policy = ArtifactFactory.PolicyDecision(_authority, decision.Artifact, Runtime.Policy, now);
        Runtime.Retain(policy, new(ArtifactType.PolicyDecision, "APS-Policy-Service", _authority.Issuer));
        var payload = new JsonObject { ["quantity"] = request.Quantity, ["transition"] = "reserve-internal-capacity" };
        var step = ArtifactFactory.PlanStep("reserve", [], Capability.Artifact, Target, payload, state.Version,
            Guid.NewGuid().ToString("N"), [evidence.Artifact.Reference], context, now.AddMinutes(10), "capacity-operations-owner");
        var plan = ArtifactFactory.Plan(_orchestrator, decision.Artifact, policy.Artifact, [step], now, now.AddMinutes(10), now);
        return new StorePlanRequest(HttpBinding.Version, plan, new Dictionary<string, JsonObject>(StringComparer.Ordinal) { ["reserve"] = payload });
    }
    public void Dispose() { foreach (var key in _keys) key.Dispose(); }
}
