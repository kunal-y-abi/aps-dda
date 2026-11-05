using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Runtime;

namespace ApsDda.Runtime.Tests;

internal sealed class MutableClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-03T14:00:00Z");
    public override DateTimeOffset GetUtcNow() => Now;
}

internal sealed class Harness : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aps-dda-tests-" + Guid.NewGuid());
    private readonly List<ECDsa> _keys = [];
    public const string Tenant = "synthetic-tenant";
    public const string Purpose = "internal-capacity-planning";
    public const string Resource = "https://capacity.example.invalid/resources/north";
    public const string Audience = "https://capacity.example.invalid";
    public MutableClock Clock { get; } = new();
    public SqliteRuntimeStore Store { get; private set; }
    public TrustedDirectory Trust { get; }
    public PrincipalDirectory Principals { get; } = new();
    public AuthenticatedPrincipal Caller { get; private set; } = null!;
    public AuthenticatedPrincipal Approver { get; private set; } = null!;
    public AuthenticatedPrincipal Granter { get; private set; } = null!;
    public ArtifactSigner Orchestrator { get; }
    public ArtifactSigner Authority { get; }
    public ArtifactSigner Source { get; }
    public SignedArtifact Product { get; }
    public SignedArtifact Capability { get; }
    public JsonArray Semantics { get; }
    public SourceRegistration SourceConfig { get; private set; } = null!;
    public IRuntimePolicy Policy { get; }
    public WorkflowRuntime Runtime { get; private set; } = null!;
    public SqliteSourceAdapter Adapter { get; private set; } = null!;
    public SignedArtifact Evidence { get; private set; } = null!;
    public SignedArtifact Decision { get; private set; } = null!;
    public SignedArtifact PolicyDecision { get; private set; } = null!;
    public SignedArtifact Plan { get; private set; } = null!;
    public SignedArtifact? Approval { get; private set; }
    public JsonObject Payload { get; } = new() { ["quantity"] = "2", ["transition"] = "reserve-internal-capacity" };
    public string WorkflowId => Plan.Artifact.Body.Object("context").String("correlationId");
    public Dictionary<string, JsonObject> Payloads { get; } = new(StringComparer.Ordinal);
    public Harness(bool boundedPolicy = false, int stepCount = 1, bool externalInvariant = false, Func<JsonObject, bool>? evidenceValidator = null,
        Func<ISourceAdapter, ISourceAdapter>? decorate = null, long available = 20)
    {
        Store = new SqliteRuntimeStore(Path.Combine(_directory, "runtime.sqlite")); Trust = new TrustedDirectory(Tenant, Clock);
        Orchestrator = Signer("https://orchestrator.example.invalid", [ArtifactType.ExecutionPlan, ArtifactType.CommandInvocation, ArtifactType.DecisionSpec], ["APS-Orchestrator"]);
        Authority = Signer("https://authority.example.invalid", [ArtifactType.Approval, ArtifactType.Authority, ArtifactType.PolicyDecision], ["APS-Authority-Service", "APS-Policy-Service"]);
        Source = Signer("https://capacity.example.invalid/issuer", [ArtifactType.ProcessCapability, ArtifactType.DataProduct, ArtifactType.EvidenceBundle, ArtifactType.OperationReceipt], ["APS-Read-Publisher", "APS-Action-Executor"], [Audience], [Audience + "/commands"], ["https://semantics.example.invalid/capacity"]);
        Policy = new EligibilityPolicy("https://policy.example.invalid/internal-capacity", "1.0.0", !boundedPolicy); Store.SetCurrentPolicy(Policy.PolicyReference);
        Semantics = new JsonArray(ArtifactFactory.Semantic("https://semantics.example.invalid/capacity", "https://semantics.example.invalid/capacity/units", "metric", "source-resource-available-units"));
        var schema = ArtifactFactory.SchemaReference("https://schema.example.invalid/internal-state", new JsonObject { ["type"] = "object" });
        Product = ArtifactFactory.DataProduct(Source, "Synthetic capacity resource", Semantics, schema, [Purpose], "local", Clock.Now);
        Capability = ArtifactFactory.ProcessCapability(Source, "ReserveInternalCapacity", ArtifactFactory.Semantic("https://semantics.example.invalid/capacity", "https://semantics.example.invalid/capacity/reserve", "command", "reserve-internal-capacity"), Audience + "/commands", Audience, schema, schema, "synthetic-capacity-v1", "exact configured resource", "capacity-operations-owner", Clock.Now, boundedPolicy, boundedPolicy ? "low" : "moderate");
        Trust.PinArtifact(Product.Artifact); Trust.PinArtifact(Capability.Artifact);
        Caller = Register("worker", "planner", "Executor", [Capability.Artifact.Header.ContentDigest]);
        Approver = Register("manager", "manager-subject", "Approver", []);
        Granter = Register("policy-service", "policy-service-subject", "PolicyGranter", []);
        SourceConfig = new SourceRegistration(Capability, Product, Source, [Resource], [Purpose], [Caller.SubjectId], [Caller.WorkloadId], ["Executor"],
            [new EvidenceRule(Product.Artifact.Reference, Source.Issuer, Semantics, TimeSpan.FromMinutes(5), evidenceValidator ?? (v => v.String("unit") == "internal-unit" && v.String("region") == "north"))],
            maximumQuantity: 50, policyGrantAllowed: boundedPolicy, maximumPolicyQuantity: 10, maximumPolicyOperationsPerMinute: 1, requiresExternalHardInvariant: externalInvariant);
        Reopen(decorate);
        Store.SeedResource(Source.Issuer, Resource, Tenant, new JsonObject { ["available"] = available.ToString(), ["reserved"] = "0" });
        var context = ArtifactFactory.Context(Caller, Purpose, Guid.NewGuid(), Policy.PolicyReference);
        var value = Store.RetainEvidenceValue(new JsonObject { ["unit"] = "internal-unit", ["region"] = "north", ["available"] = available.ToString() });
        Evidence = ArtifactFactory.Evidence(Source, context, Product.Artifact, Semantics, Jcs.Sha256Digest(new JsonObject { ["resource"] = Resource }), "1", value, Clock.Now, Clock.Now);
        Runtime.Retain(Evidence, new(ArtifactType.EvidenceBundle, "APS-Read-Publisher", Source.Issuer));
        Decision = ArtifactFactory.Decision(Orchestrator, context, "synthetic-planning-owner", "Reserve capacity?", "Reduce predicted backlog", ["source-owned available capacity"], ["reserve", "manual"], [Evidence.Artifact.Reference], "deterministic-rules", "1.0.0", new JsonObject { ["quantity"] = "2" }, ["synthetic data"], ["not externally validated"], "reserve two units", "eligible", Clock.Now);
        Runtime.Retain(Decision, new(ArtifactType.DecisionSpec, "APS-Orchestrator", Orchestrator.Issuer));
        PolicyDecision = ArtifactFactory.PolicyDecision(Authority, Decision.Artifact, Policy, Clock.Now);
        Runtime.Retain(PolicyDecision, new(ArtifactType.PolicyDecision, "APS-Policy-Service", Authority.Issuer));
        var steps = new List<JsonObject>();
        for (var i = 0; i < stepCount; i++)
        {
            var id = "step-" + (i + 1); Payloads[id] = (JsonObject)Payload.DeepClone();
            steps.Add(ArtifactFactory.PlanStep(id, i == 0 ? [] : ["step-" + i], Capability.Artifact, Resource, Payload, (i + 1).ToString(), Guid.NewGuid().ToString("N"), [Evidence.Artifact.Reference], context, Clock.Now.AddMinutes(10), "capacity-operations-owner"));
        }
        Plan = ArtifactFactory.Plan(Orchestrator, Decision.Artifact, PolicyDecision.Artifact, steps, Clock.Now, Clock.Now.AddMinutes(10), Clock.Now);
    }
    public AuthenticatedPrincipal Register(string identity, string subject, string role, IEnumerable<string> capabilities,
        string tenant = Tenant, IEnumerable<string>? purposes = null, IEnumerable<string>? audiences = null, IEnumerable<string>? resources = null)
        => Principals.Register(new(identity, tenant, subject, "workload-" + identity, "executor-" + identity, [role], purposes ?? [Purpose], audiences ?? [Audience], resources ?? [Resource], capabilities));
    private ArtifactSigner Signer(string issuer, ArtifactType[] types, string[] roles, string[]? audiences = null, string[]? endpoints = null, string[]? ns = null)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); _keys.Add(key); var keyId = issuer + "/keys/runtime";
        Trust.Register(new TrustedParticipant(issuer, [new TrustedKey(keyId, key.ExportSubjectPublicKeyInfo())], types, roles, [Artifact.SpecVersion], ["1.0.0"], audiences: audiences, endpoints: endpoints, semanticNamespaces: ns));
        return new(issuer, keyId, key);
    }
    public void Reopen(Func<ISourceAdapter, ISourceAdapter>? decorate = null)
    {
        Store = new SqliteRuntimeStore(Store.DatabasePath);
        Runtime = new(Store, Trust, Principals, Orchestrator, Authority, Policy, Clock);
        Adapter = new(Runtime, SourceConfig, new ReserveCapacityCommand()); Runtime.RegisterSource(decorate?.Invoke(Adapter) ?? Adapter);
    }
    public void Submit() => Runtime.Submit(Plan, Payloads, Caller);
    public void AuthorizeHuman()
    {
        Submit(); Approval = Runtime.Approve(WorkflowId, Approver, Clock.Now.AddMinutes(4)); Runtime.Authorize(WorkflowId, Approval.Artifact.Reference, Caller);
    }
    public void AuthorizePolicy(long maximum = 10, int rate = 1)
    {
        Submit(); var grant = ArtifactFactory.PolicyGrant(Authority, Plan.Artifact, Capability.Artifact, Resource, [Evidence.Artifact.Reference], Purpose, maximum, rate, Clock.Now.AddMinutes(4), Clock.Now, Authority.Issuer + "/status");
        Approval = grant; Runtime.AdmitAuthority(grant, Granter); Runtime.Authorize(WorkflowId, grant.Artifact.Reference, Caller);
    }
    public SignedArtifact ReplacePlan(Func<JsonObject, JsonObject> transform)
    { Plan = Orchestrator.Issue(ArtifactType.ExecutionPlan, transform(Plan.Artifact.Body), Clock.Now); return Plan; }
    public SignedArtifact ReplaceEvidence(Func<JsonObject, JsonObject> transform)
    {
        var replaced = Source.Issue(ArtifactType.EvidenceBundle, transform(Evidence.Artifact.Body), Clock.Now); Runtime.Retain(replaced, new(ArtifactType.EvidenceBundle, "APS-Read-Publisher", Source.Issuer));
        Evidence = replaced;
        var decisionBody = Decision.Artifact.Body; decisionBody["evidenceRefs"] = new JsonArray(replaced.Artifact.Reference.ToJson());
        Decision = Orchestrator.Issue(ArtifactType.DecisionSpec, decisionBody, Clock.Now); Runtime.Retain(Decision, new(ArtifactType.DecisionSpec, "APS-Orchestrator", Orchestrator.Issuer));
        PolicyDecision = ArtifactFactory.PolicyDecision(Authority, Decision.Artifact, Policy, Clock.Now); Runtime.Retain(PolicyDecision, new(ArtifactType.PolicyDecision, "APS-Policy-Service", Authority.Issuer));
        ReplacePlan(p => { p["decisionRef"] = Decision.Artifact.Reference.ToJson(); p["policyRef"] = PolicyDecision.Artifact.Reference.ToJson(); foreach (var step in p.Array("steps")) step!.AsObject()["evidenceRefs"] = new JsonArray(replaced.Artifact.Reference.ToJson()); return p; });
        return replaced;
    }
    public void Dispose()
    { foreach (var key in _keys) key.Dispose(); if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
}

internal sealed class DecoratedAdapter(ISourceAdapter inner, Func<SourceDispatch, SourceResult>? invoke = null, Func<OperationIdentity, AuthenticatedPrincipal, SourceResult?>? lookup = null,
    Action<SourceResult, Artifact>? verify = null) : ISourceAdapter
{
    public SourceRegistration Registration => inner.Registration;
    public long MeasureQuantity(JsonObject normalizedPayload) => inner.MeasureQuantity(normalizedPayload);
    public SourceResult Invoke(SourceDispatch dispatch) => invoke?.Invoke(dispatch) ?? inner.Invoke(dispatch);
    public SourceResult? Lookup(OperationIdentity identity, AuthenticatedPrincipal caller) => lookup is null ? inner.Lookup(identity, caller) : lookup(identity, caller);
    public void VerifyPostcondition(SourceResult result, Artifact invocation) { if (verify is null) inner.VerifyPostcondition(result, invocation); else verify(result, invocation); }
}
