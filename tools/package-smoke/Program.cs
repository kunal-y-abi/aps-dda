using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApsDda.AspNetCore;
using ApsDda.Core;
using ApsDda.Runtime;
using Microsoft.Extensions.DependencyInjection;

// AI-assisted source development. This template is copied outside the checkout and uses NuGet only.
const string schemaHash = "3aa0469657fa30554d4989285105354964bdd8c76b90356b0b6e44eac5847ea4";
if (Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ArtifactSchema.ReadOriginalSchema()))) != schemaHash)
    throw new Exception("Installed original embedded schema hash differs.");
using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
const string issuer = "https://synthetic-consumer.example.invalid/issuer";
const string keyId = issuer + "/keys/runtime";
const string audience = "https://synthetic-consumer.example.invalid";
const string resource = audience + "/resource";
const string ns = audience + "/semantics";
var signer = new ArtifactSigner(issuer, keyId, key);
var now = DateTimeOffset.UtcNow;
var directory = Path.Combine(Path.GetTempPath(), "aps-installed-consumer-" + Guid.NewGuid());
var path = Path.Combine(directory, "runtime.sqlite");
try
{
    var store = new SqliteRuntimeStore(path);
    var trust = new TrustedDirectory("synthetic-tenant");
    trust.Register(new TrustedParticipant(issuer, [new TrustedKey(keyId, key.ExportSubjectPublicKeyInfo())],
        [ArtifactType.DataProduct, ArtifactType.ProcessCapability, ArtifactType.EvidenceBundle, ArtifactType.DecisionSpec, ArtifactType.PolicyDecision,
            ArtifactType.ExecutionPlan, ArtifactType.Approval, ArtifactType.CommandInvocation, ArtifactType.OperationReceipt],
        ["APS-Orchestrator", "APS-Policy-Service", "APS-Authority-Service", "APS-Read-Publisher", "APS-Action-Executor"], [Artifact.SpecVersion], ["1.0.0"],
        audiences: [audience], endpoints: [audience + "/commands"], semanticNamespaces: [ns]));
    var policy = new EligibilityPolicy(audience + "/policy", "1.0.0", true); store.SetCurrentPolicy(policy.PolicyReference);
    var semantics = new JsonArray(ArtifactFactory.Semantic(ns, ns + "/units", "metric", "internal-unit"));
    var schema = ArtifactFactory.SchemaReference(audience + "/schemas/state", new JsonObject { ["type"] = "object" });
    var product = ArtifactFactory.DataProduct(signer, "Synthetic units", semantics, schema, ["internal-planning"], "local", now);
    var cap = ArtifactFactory.ProcessCapability(signer, "ReserveInternalCapacity", ArtifactFactory.Semantic(ns, ns + "/reserve", "command", "reserve-internal-capacity"),
        audience + "/commands", audience, schema, schema, "installed-consumer-v1", resource, "manual-operations-owner", now);
    trust.PinArtifact(product.Artifact); trust.PinArtifact(cap.Artifact);
    var principals = new PrincipalDirectory();
    var caller = principals.Register(new("authenticated-worker", "synthetic-tenant", "planner", "worker", "executor", ["Executor"], ["internal-planning"], [audience], [resource], [cap.Artifact.Header.ContentDigest]));
    var approver = principals.Register(new("authenticated-manager", "synthetic-tenant", "independent-manager", "manager", "approval-service", ["Approver"], ["internal-planning"], [], [], []));
    var rule = new EvidenceRule(product.Artifact.Reference, issuer, semantics, TimeSpan.FromMinutes(5), v => v.String("unit") == "internal-unit");
    var source = new SourceRegistration(cap, product, signer, [resource], ["internal-planning"], [caller.SubjectId], [caller.WorkloadId], ["Executor"], [rule]);
    var runtime = new WorkflowRuntime(store, trust, principals, signer, signer, policy);
    var adapter = new SqliteSourceAdapter(runtime, source, new ReserveCapacityCommand()); runtime.RegisterSource(adapter);
    store.SeedResource(issuer, resource, "synthetic-tenant", new JsonObject { ["available"] = "8", ["reserved"] = "0" });
    var context = ArtifactFactory.Context(caller, "internal-planning", Guid.NewGuid(), policy.PolicyReference);
    var value = store.RetainEvidenceValue(new JsonObject { ["unit"] = "internal-unit" });
    var evidence = ArtifactFactory.Evidence(signer, context, product.Artifact, semantics, Jcs.Sha256Digest(new JsonObject { ["target"] = resource }), "1", value, now, now);
    var decision = ArtifactFactory.Decision(signer, context, "manual-operations-owner", "Reserve two units?", "Internal readiness", ["local capacity"], ["reserve", "manual"],
        [evidence.Artifact.Reference], "deterministic-rules", "1.0.0", new JsonObject { ["units"] = "2" }, ["synthetic data"], [], "reserve", "eligible", now);
    var evaluated = ArtifactFactory.PolicyDecision(signer, decision.Artifact, policy, now);
    runtime.Retain(evidence, new(ArtifactType.EvidenceBundle, "APS-Read-Publisher", issuer));
    runtime.Retain(decision, new(ArtifactType.DecisionSpec, "APS-Orchestrator", issuer));
    runtime.Retain(evaluated, new(ArtifactType.PolicyDecision, "APS-Policy-Service", issuer));
    var payload = new JsonObject { ["quantity"] = "2", ["transition"] = "reserve-internal-capacity" };
    var step = ArtifactFactory.PlanStep("reserve", [], cap.Artifact, resource, payload, "1", Guid.NewGuid().ToString("N"), [evidence.Artifact.Reference], context, now.AddMinutes(3), "manual-operations-owner");
    var plan = ArtifactFactory.Plan(signer, decision.Artifact, evaluated.Artifact, [step], now, now.AddMinutes(3), now);
    plan.Artifact.RequireIntegrity();
    if (!DetachedSigning.VerifyCryptographic(plan, key.ExportSubjectPublicKeyInfo())) throw new Exception("Actual native signature failed.");
    var workflow = runtime.Submit(plan, new Dictionary<string, JsonObject> { ["reserve"] = payload }, caller);
    var approval = runtime.Approve(workflow.WorkflowId, approver, now.AddMinutes(2)); runtime.Authorize(workflow.WorkflowId, approval.Artifact.Reference, caller);
    var done = runtime.ExecuteAll(workflow.WorkflowId, caller);
    if (done.State != WorkflowState.COMPLETED || store.OperationCount != 1 || store.GetResource(issuer, resource).State.String("reserved") != "2" || store.GetResource(issuer, resource).State.String("available") != "6") throw new Exception("Installed package workflow failed.");
    var inv = store.GetArtifact(done.Steps[0].InvocationRef!).Artifact;
    var result = adapter.Lookup(new("synthetic-tenant", issuer, inv.Body.String("idempotencyDomain"), inv.Body.String("idempotencyKey"), inv.Body.String("invocationFingerprint")), caller)!;
    adapter.VerifyPostcondition(result, inv);
    var reopened = new SqliteRuntimeStore(path);
    if (reopened.GetWorkflow(workflow.WorkflowId).Steps[0].InvocationRef != done.Steps[0].InvocationRef || reopened.OperationCount != 1 || reopened.AuthorityUseCount != 1) throw new Exception("Installed package reopen failed.");
    var duplicate = adapter.Invoke(new(inv.Reference, caller));
    if (duplicate.Receipt.Artifact.Reference != done.Steps[0].ReceiptRef || store.OperationCount != 1) throw new Exception("Installed duplicate operation failed.");
    var token = Jcs.Base64Url(RandomNumberGenerator.GetBytes(32));
    var services = new ServiceCollection(); services.AddApsDdaClient(new Uri("https://localhost:7127"), token);
    using var provider = services.BuildServiceProvider();
    _ = provider.GetRequiredService<ApsDdaClient>();
    var request = new PreparePlanRequest(HttpBinding.Version, "north", "2", "1");
    if (StrictJson.ParseObject(JsonSerializer.Serialize(request, HttpBinding.CreateJsonOptions())).Deserialize<PreparePlanRequest>(HttpBinding.CreateJsonOptions()) != request) throw new Exception("Packaged HTTP wire roundtrip failed.");
    try { StrictJson.ParseObject("{\"binding\":\"one\",\"binding\":\"two\"}"); throw new Exception("Duplicate accepted."); } catch (ApsDdaException) { }
    Console.WriteLine("PASS package-only consumer: embedded original schema/hash, real digests/native signature, authenticated approval, actual SQLite effect, verified original receipt, durable reopen, duplicate no-extra-effect, typed HttpClient registration.");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
