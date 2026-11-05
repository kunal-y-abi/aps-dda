using System.Security.Cryptography;
using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Runtime;
using Microsoft.Data.Sqlite;

namespace ApsDda.Demo;

public sealed class DemoClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 3, 14, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
public sealed class NumericalPolicy : IRuntimePolicy
{
    private readonly JsonObject _reference;
    private readonly JsonObject _expected;
    private readonly bool _human;
    public NumericalPolicy(StructuredDecisionSpec spec, bool human)
    {
        _expected = spec.ToJson(); _human = human;
        _reference = new() { ["uri"] = "https://policy.example.invalid/synthetic-allocation", ["version"] = "1.0.0", ["digest"] = Jcs.Sha256Digest(new JsonObject
        { ["profile"] = "aps-dda.experimental/synthetic-allocation-policy/1", ["human"] = human, ["analysis"] = _expected.DeepClone() }) };
    }
    public JsonObject PolicyReference => (JsonObject)_reference.DeepClone();
    public PolicyEvaluation Evaluate(Artifact decision)
    {
        var body = decision.Body;
        var good = body.String("actionEligibility") == "eligible" && body.Object("method").String("parameterDigest") == Jcs.Sha256Digest(_expected.Object("parameters")) &&
            Jcs.Sha256Digest(body.Object("extensions").Object(AllocationModel.ExtensionUri)) == Jcs.Sha256Digest(_expected);
        return new(good ? _human ? "allow-with-human-approval" : "allow" : "deny", []);
    }
}

/// <summary>Trusted in-process fixture. Every key is ephemeral; only SQLite state exists in a private temporary directory.</summary>
public sealed class InsuranceSession : IDisposable
{
    public const string Tenant = "synthetic-insurer";
    public const string Purpose = "synthetic-response-readiness";
    public const string CapacityAudience = "https://capacity.example.invalid";
    public const string DraftAudience = "https://readiness.example.invalid";
    public const string DraftTarget = DraftAudience + "/resources/internal-draft";
    public static string CapacityTarget(string region) => CapacityAudience + "/resources/" + region;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aps-dda-insurance-" + Guid.NewGuid().ToString("N"));
    private readonly List<ECDsa> _keys = [];
    private readonly JsonArray _publicKeys = [];
    private readonly List<EvidenceRule> _rules = [];
    private readonly List<(SourceRegistration Registration, ILocalSourceCommand Command)> _registrations = [];
    private readonly bool _loseReply;
    private bool _replyLost;
    public DemoClock Clock { get; } = new();
    public SqliteRuntimeStore Store { get; private set; }
    public TrustedDirectory Trust { get; }
    public PrincipalDirectory Principals { get; } = new();
    public AllocationModel Model { get; }
    public StructuredDecisionSpec Analysis { get; }
    public IRuntimePolicy Policy { get; }
    public WorkflowRuntime Runtime { get; private set; } = null!;
    public AuthenticatedPrincipal Caller { get; }
    public AuthenticatedPrincipal Approver { get; }
    public AuthenticatedPrincipal Granter { get; }
    public ArtifactSigner Orchestrator { get; }
    public ArtifactSigner Authority { get; }
    public ArtifactSigner CapacitySigner { get; }
    public ArtifactSigner DraftSigner { get; }
    public SignedArtifact CapacityCapability { get; }
    public SignedArtifact DraftCapability { get; }
    public IReadOnlyList<SignedArtifact> Evidence { get; }
    public SignedArtifact Decision { get; }
    public SignedArtifact PolicyDecision { get; }
    public SignedArtifact Plan { get; private set; }
    public SignedArtifact? AuthorityArtifact { get; private set; }
    public Dictionary<string, JsonObject> Payloads { get; } = new(StringComparer.Ordinal);
    public CompiledApplication Compiled { get; }
    public ApplicationCompiler Compiler { get; }
    public bool BoundedPolicy { get; }
    public string WorkflowId => Plan.Artifact.Body.Object("context").String("correlationId");
    public int InvokeCalls { get; private set; }
    public int LookupCalls { get; private set; }
    public JsonArray PublicTrustPins => (JsonArray)_publicKeys.DeepClone();
    public InsuranceSession(string selected = "balanced", bool boundedPolicy = false, bool loseReply = false,
        bool semanticMismatch = false, bool staleEvidence = false, JsonObject? dataset = null)
    {
        BoundedPolicy = boundedPolicy; _loseReply = loseReply;
        Model = new(dataset ?? AllocationModel.LoadDataset()); Analysis = Model.Compare(selected);
        Store = new(Path.Combine(_directory, "runtime.sqlite")); Trust = new(Tenant, Clock);
        var expectedExtension = Analysis.ToJson();
        Trust.SupportExtension(new(AllocationModel.ExtensionUri, "1.0.0", n => Jcs.Sha256Digest(n) == Jcs.Sha256Digest(expectedExtension)));
        Orchestrator = Signer("https://planner.example.invalid", [ArtifactType.DecisionSpec, ArtifactType.ExecutionPlan, ArtifactType.CommandInvocation, ArtifactType.ApplicationSpec, ArtifactType.BuilderPrompt], ["APS-Orchestrator"]);
        Authority = Signer("https://authority.example.invalid", [ArtifactType.PolicyDecision, ArtifactType.Authority, ArtifactType.Approval], ["APS-Policy-Service", "APS-Authority-Service"]);
        var publishers = new Dictionary<string, (ArtifactSigner Signer, SignedArtifact Product, JsonArray Semantics)>();
        var schema = ArtifactFactory.SchemaReference("https://schemas.example.invalid/synthetic/aggregate-v1", new JsonObject { ["type"] = "object", ["profile"] = "reviewed-source-specific-canonical-quantity-strings/1" });
        foreach (var source in new[] { "claims", "forecast", "capacity", "finance" })
        {
            var audience = "https://" + source + ".example.invalid"; var ns = "https://semantics.example.invalid/" + source;
            var signer = Signer(audience + "/issuer", [ArtifactType.DataProduct, ArtifactType.ProcessCapability, ArtifactType.EvidenceBundle, ArtifactType.OperationReceipt], ["APS-Read-Publisher", "APS-Action-Executor"], [audience], [audience + "/commands"], [ns]);
            var semantics = new JsonArray(ArtifactFactory.Semantic(ns, ns + "/aggregate", "metric", "three-region-" + source + "-synthetic-day-1"));
            var product = ArtifactFactory.DataProduct(signer, "Synthetic aggregate " + source, semantics, schema, [Purpose], "local", Clock.Now, grain: "three named regions; one synthetic day");
            Trust.PinArtifact(product.Artifact); Store.Retain(product); publishers[source] = (signer, product, semantics);
            _rules.Add(new(product.Artifact.Reference, signer.Issuer, semantics, TimeSpan.FromMinutes(5), v =>
            {
                if (!AllocationModel.ValidateSource(source, v)) return false;
                var p = (JsonObject)v.DeepClone(); p.Remove("retrievedText"); return Jcs.Sha256Digest(p) == Jcs.Sha256Digest(Model.NumericalProjection(source));
            }));
        }
        CapacitySigner = publishers["capacity"].Signer;
        var localSchema = ArtifactFactory.SchemaReference("https://schemas.example.invalid/internal-command-v1", new JsonObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = new JsonArray("quantity", "transition"), ["properties"] = new JsonObject { ["quantity"] = new JsonObject { ["type"] = "string", ["pattern"] = "^[1-9][0-9]*$" }, ["transition"] = new JsonObject { ["enum"] = new JsonArray("reserve-internal-capacity", "prepare-notification-draft") } } });
        // Command results describe source state, not the analytical aggregate input product.
        var resultSchema = ArtifactFactory.SchemaReference("https://schemas.example.invalid/internal-postcondition-v1", new JsonObject { ["type"] = "object", ["required"] = new JsonArray("target", "version", "state", "beforeState", "invocationRef", "operationId") });
        CapacityCapability = ArtifactFactory.ProcessCapability(CapacitySigner, "ReserveSyntheticResponseTeams", ArtifactFactory.Semantic("https://semantics.example.invalid/capacity", "https://semantics.example.invalid/capacity/reserve", "command", "reserve-internal-capacity"), CapacityAudience + "/commands", CapacityAudience, localSchema, resultSchema, "synthetic-capacity/1", "exact configured regional resource", "synthetic-capacity-owner", Clock.Now);
        DraftSigner = Signer(DraftAudience + "/issuer", [ArtifactType.DataProduct, ArtifactType.ProcessCapability, ArtifactType.EvidenceBundle, ArtifactType.OperationReceipt], ["APS-Read-Publisher", "APS-Action-Executor"], [DraftAudience], [DraftAudience + "/commands"], ["https://semantics.example.invalid/readiness"]);
        var draftSemantics = new JsonArray(ArtifactFactory.Semantic("https://semantics.example.invalid/readiness", "https://semantics.example.invalid/readiness/draft-count", "metric", "internal-draft-count"));
        var draftProduct = ArtifactFactory.DataProduct(DraftSigner, "Synthetic internal draft state", draftSemantics, resultSchema, [Purpose], "local", Clock.Now);
        DraftCapability = ArtifactFactory.ProcessCapability(DraftSigner, "PrepareInternalReadinessDraft", ArtifactFactory.Semantic("https://semantics.example.invalid/readiness", "https://semantics.example.invalid/readiness/draft", "command", "prepare-notification-draft"), DraftAudience + "/commands", DraftAudience, localSchema, resultSchema, "synthetic-draft/1", DraftTarget, "synthetic-readiness-owner", Clock.Now, true, "low");
        foreach (var artifact in new[] { CapacityCapability, DraftCapability, draftProduct }) Trust.PinArtifact(artifact.Artifact);
        var resources = AllocationModel.RegionNames.Select(CapacityTarget).Append(DraftTarget).ToArray();
        AuthenticatedPrincipal Register(string identity, string subject, string role) => Principals.Register(new(identity, Tenant, subject, "workload-" + identity, "executor-" + identity, [role], [Purpose], [CapacityAudience, DraftAudience], resources, [CapacityCapability.Artifact.Header.ContentDigest, DraftCapability.Artifact.Header.ContentDigest]));
        Caller = Register("fixture-worker", "fixture-planner", "Executor"); Approver = Register("fixture-manager", "fixture-manager-subject", "Approver"); Granter = Register("fixture-policy-service", "fixture-policy-subject", "PolicyGranter");
        Policy = new NumericalPolicy(Analysis, !boundedPolicy); Store.SetCurrentPolicy(Policy.PolicyReference);
        // A separate source-owned state product is used for capacity receipts; never relabel aggregate inputs as postconditions.
        var capacityStateSemantics = new JsonArray(ArtifactFactory.Semantic("https://semantics.example.invalid/capacity", "https://semantics.example.invalid/capacity/state", "metric", "regional-internal-team-state"));
        var capacityStateProduct = ArtifactFactory.DataProduct(CapacitySigner, "Synthetic regional reservation state", capacityStateSemantics, resultSchema, [Purpose], "local", Clock.Now); Trust.PinArtifact(capacityStateProduct.Artifact);
        _registrations.Add((new(CapacityCapability, capacityStateProduct, CapacitySigner, resources.Where(r => r != DraftTarget), [Purpose], [Caller.SubjectId], [Caller.WorkloadId], ["Executor"], _rules, maximumQuantity: 20), new ReserveCapacityCommand()));
        _registrations.Add((new(DraftCapability, draftProduct, DraftSigner, [DraftTarget], [Purpose], [Caller.SubjectId], [Caller.WorkloadId], ["Executor"], _rules, maximumQuantity: 1, policyGrantAllowed: true, maximumPolicyQuantity: 1, maximumPolicyOperationsPerMinute: 1), new PrepareNotificationDraftCommand()));
        Reopen();
        foreach (var r in Model.Regions) Store.SeedResource(CapacitySigner.Issuer, CapacityTarget(r.Region), Tenant, new JsonObject { ["available"] = CanonicalQuantity.Format(r.AvailableTeams - r.EmergencyReserveTeams), ["reserved"] = "0" });
        Store.SeedResource(DraftSigner.Issuer, DraftTarget, Tenant, new JsonObject { ["draftCount"] = "0" });
        var context = ArtifactFactory.Context(Caller, Purpose, Guid.NewGuid(), Policy.PolicyReference); var evidence = new List<SignedArtifact>();
        foreach (var (source, pub) in publishers)
        {
            var value = (JsonObject)Model.Dataset.Object(source).DeepClone(); if (semanticMismatch && source == "finance") value["unit"] = "real-currency-not-supported";
            var observed = staleEvidence && source == "forecast" ? Clock.Now.AddMinutes(-6) : Clock.Now;
            var item = ArtifactFactory.Evidence(pub.Signer, context, pub.Product.Artifact, pub.Semantics, Jcs.Sha256Digest(new JsonObject { ["source"] = source, ["period"] = "synthetic-day-1" }), "1", Store.RetainEvidenceValue(value), observed, Clock.Now);
            Runtime.Retain(item, new(ArtifactType.EvidenceBundle, "APS-Read-Publisher", pub.Signer.Issuer)); evidence.Add(item);
        }
        Evidence = evidence.AsReadOnly(); var selectedPlan = Analysis.Alternatives.Single(a => a.Name == selected);
        Decision = ArtifactFactory.Decision(Orchestrator, context, "synthetic-response-planning-owner", "How should additional internal team-days be allocated across three synthetic regions?", "Compare minimum expected total backlog and minimax upper-scenario backlog", ["integer teams; each region has at least one additional team", "expected served items >= regional minimumServedItems", "model cost <= synthetic budget", "regional emergency reserve excluded; total <= deployable pool"], Analysis.Alternatives.Select(a => a.ToJson().ToJsonString()), evidence.Select(e => e.Artifact.Reference), "optimizer", "finite-integer-enumeration/1.0.0", Analysis.Parameters, ["one day; uniform productivity; no cross-region transfer delay", "observational sources, not a global snapshot", "budget is modeled credits, not permission to spend"], ["expected and upper stress inputs are synthetic deterministic scenarios, not probabilities", "not actuarially or empirically validated"], "Chosen " + selected + "; " + (boundedPolicy ? "prepare one internal draft only" : "reserve exact regional teams"), "eligible", Clock.Now, new JsonObject { [AllocationModel.ExtensionUri] = Analysis.ToJson() });
        Runtime.Retain(Decision, new(ArtifactType.DecisionSpec, "APS-Orchestrator", Orchestrator.Issuer));
        PolicyDecision = ArtifactFactory.PolicyDecision(Authority, Decision.Artifact, Policy, Clock.Now); Runtime.Retain(PolicyDecision, new(ArtifactType.PolicyDecision, "APS-Policy-Service", Authority.Issuer));
        var steps = new List<JsonObject>(); var refs = evidence.Select(e => e.Artifact.Reference).ToArray();
        if (boundedPolicy)
        {
            Payloads["draft-readiness"] = new() { ["quantity"] = "1", ["transition"] = "prepare-notification-draft" };
            steps.Add(ArtifactFactory.PlanStep("draft-readiness", [], DraftCapability.Artifact, DraftTarget, Payloads["draft-readiness"], "1", Guid.NewGuid().ToString("N"), refs, context, Clock.Now.AddMinutes(10), "synthetic-readiness-owner"));
        }
        else for (var i = 0; i < 3; i++)
        {
            var id = "reserve-" + AllocationModel.RegionNames[i]; Payloads[id] = new() { ["quantity"] = CanonicalQuantity.Format(selectedPlan.AdditionalTeams[i]), ["transition"] = "reserve-internal-capacity" };
            steps.Add(ArtifactFactory.PlanStep(id, i == 0 ? [] : ["reserve-" + AllocationModel.RegionNames[i - 1]], CapacityCapability.Artifact, CapacityTarget(AllocationModel.RegionNames[i]), Payloads[id], "1", Guid.NewGuid().ToString("N"), refs, context, Clock.Now.AddMinutes(10), "synthetic-capacity-owner"));
        }
        Plan = ArtifactFactory.Plan(Orchestrator, Decision.Artifact, PolicyDecision.Artifact, steps, Clock.Now, Clock.Now.AddMinutes(10), Clock.Now);
        Compiler = new(Trust, Orchestrator, Clock, CapacityCapability, DraftCapability); Compiled = Compiler.Compile(Decision, Plan, Evidence, Payloads);
    }
    private ArtifactSigner Signer(string issuer, ArtifactType[] types, string[] roles, string[]? audiences = null, string[]? endpoints = null, string[]? namespaces = null)
    {
        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); _keys.Add(key); var keyId = issuer + "/keys/ephemeral";
        _publicKeys.Add(new JsonObject { ["issuer"] = issuer, ["keyId"] = keyId, ["publicKeySpkiBase64"] = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) });
        Trust.Register(new(issuer, [new TrustedKey(keyId, key.ExportSubjectPublicKeyInfo())], types, roles, [Artifact.SpecVersion], ["1.0.0"], audiences: audiences, endpoints: endpoints, semanticNamespaces: namespaces));
        return new(issuer, keyId, key);
    }
    public void Reopen()
    {
        Store = new(Store.DatabasePath); Runtime = new(Store, Trust, Principals, Orchestrator, Authority, Policy, Clock);
        foreach (var rule in _rules) Runtime.RegisterEvidenceRule(rule);
        foreach (var (registration, command) in _registrations) Runtime.RegisterSource(new CountingAdapter(this, new SqliteSourceAdapter(Runtime, registration, command)));
    }
    private sealed class CountingAdapter(InsuranceSession owner, ISourceAdapter inner) : ISourceAdapter
    {
        public SourceRegistration Registration => inner.Registration;
        public long MeasureQuantity(JsonObject payload) => inner.MeasureQuantity(payload);
        public SourceResult Invoke(SourceDispatch dispatch)
        {
            owner.InvokeCalls++; var result = inner.Invoke(dispatch);
            if (owner._loseReply && !owner._replyLost) { owner._replyLost = true; throw new IOException("Synthetic reply lost after commit."); }
            return result;
        }
        public SourceResult? Lookup(OperationIdentity identity, AuthenticatedPrincipal caller) { owner.LookupCalls++; return inner.Lookup(identity, caller); }
        public void VerifyPostcondition(SourceResult result, Artifact invocation) => inner.VerifyPostcondition(result, invocation);
    }
    public WorkflowSnapshot Submit() => Runtime.Submit(Plan, Payloads, Caller);
    public void Authorize(IEnumerable<string>? approvedSteps = null)
    {
        Submit();
        AuthorityArtifact = BoundedPolicy ? ArtifactFactory.PolicyGrant(Authority, Plan.Artifact, DraftCapability.Artifact, DraftTarget, Evidence.Select(e => e.Artifact.Reference), Purpose, 1, 1, Clock.Now.AddMinutes(4), Clock.Now, Authority.Issuer + "/status") : Runtime.Approve(WorkflowId, Approver, Clock.Now.AddMinutes(4), approvedSteps);
        if (BoundedPolicy) Runtime.AdmitAuthority(AuthorityArtifact, Granter);
        Runtime.Authorize(WorkflowId, AuthorityArtifact.Artifact.Reference, Caller);
    }
    /// <summary>In-process scenario-only provisioning change, never an HTTP/admin endpoint.</summary>
    public void ChangeRegionalVersion(string region)
    {
        if (!AllocationModel.RegionNames.Contains(region)) throw new ArgumentException("Unknown synthetic region.");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Store.DatabasePath, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE resources SET version=version+1 WHERE source_issuer=$source AND target=$target AND tenant_id=$tenant";
        command.Parameters.AddWithValue("$source", CapacitySigner.Issuer); command.Parameters.AddWithValue("$target", CapacityTarget(region)); command.Parameters.AddWithValue("$tenant", Tenant);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Synthetic fixture resource not found.");
    }
    public void TamperPlanWithoutResigning()
    {
        var document = Plan.Artifact.ToJson(); document.Object("body").Array("steps")[0]!.AsObject()["target"] = CapacityTarget("south");
        Plan = new(Artifact.FromJson(document), Plan.Signature);
    }
    public JsonObject Effects() => new()
    {
        ["capacity"] = new JsonArray(AllocationModel.RegionNames.Select(r => (JsonNode)new JsonObject { ["region"] = r, ["version"] = Store.GetResource(CapacitySigner.Issuer, CapacityTarget(r)).Version, ["state"] = Store.GetResource(CapacitySigner.Issuer, CapacityTarget(r)).State }).ToArray()),
        ["draft"] = Store.GetResource(DraftSigner.Issuer, DraftTarget).State, ["operations"] = Store.OperationCount, ["authorityUses"] = Store.AuthorityUseCount,
        ["invokeCalls"] = InvokeCalls, ["lookupCalls"] = LookupCalls
    };
    public void Dispose()
    {
        foreach (var key in _keys) key.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
