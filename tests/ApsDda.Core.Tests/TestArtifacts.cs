using System.Text.Json.Nodes;
using ApsDda.Core;

namespace ApsDda.Core.Tests;

internal sealed class TestArtifacts
{
    public const string Issuer = "https://source.example.invalid/aps";
    public const string KeyId = "https://source.example.invalid/keys/p256-1";
    public const string Audience = "https://procurement.example.invalid";
    public static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-03T14:00:00Z");
    public Artifact Product { get; }
    public Artifact Capability { get; }
    public Artifact Evidence { get; }
    public Artifact Decision { get; }
    public Artifact Policy { get; }
    public Artifact Plan { get; }
    public Artifact Approval { get; }
    public Artifact Invocation { get; }
    public Artifact Receipt { get; }
    public JsonObject Payload { get; } = new() { ["resourceId"] = "PO-7841", ["quantity"] = "2", ["transition"] = "cancel-unreceived" };
    public JsonObject Context { get; }
    internal static JsonObject Fixture(string name) => StrictJson.ParseObject(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name + ".json")));
    internal static Artifact Issue(string fixture, JsonObject? body = null) => Artifact.Create(Enum.Parse<ArtifactType>(Fixture(fixture).String("artifactType")), Issuer, body ?? Fixture(fixture).Object("body"), Now);
    public TestArtifacts()
    {
        Context = Fixture("execution-plan").Object("body").Object("context");
        Context["policyRefs"] = new JsonArray(new JsonObject { ["uri"] = "https://policy.example.invalid/rules/purchase-order-cancellation", ["version"] = "1.0.0", ["digest"] = Jcs.Sha256Digest(new JsonObject { ["policy"] = "internal-test-rule-v1" }) });
        Product = Issue("data-product-purchase-orders");
        var capBody = Fixture("process-capability-cancel-po").Object("body");
        capBody.Object("endpoint")["trustKeyRef"] = KeyId;
        Capability = Issue("process-capability-cancel-po", capBody);
        var eb = Fixture("evidence-bundle-observational").Object("body");
        eb["context"] = Context.DeepClone();
        foreach (var n in eb.Array("items"))
        {
            var item = n!.AsObject(); item["productRef"] = Product.Reference.ToJson();
            item["queryDigest"] = Jcs.Sha256Digest(new JsonObject { ["id"] = "PO-7841" });
        }
        Evidence = Issue("evidence-bundle-observational", eb);
        var d = Fixture("decision-spec").Object("body"); d["context"] = Context.DeepClone();
        d["evidenceRefs"] = new JsonArray(Evidence.Reference.ToJson());
        d.Object("method")["parameterDigest"] = Jcs.Sha256Digest(new JsonObject { ["limit"] = "2" });
        Decision = Issue("decision-spec", d);
        var p = Fixture("policy-decision").Object("body"); p["context"] = Context.DeepClone();
        p["decisionDigest"] = Jcs.Sha256Digest(new JsonObject { ["decisionRef"] = Decision.Reference.ToJson(), ["outcome"] = "allow-with-human-approval" });
        Policy = Issue("policy-decision", p);
        var plan = Fixture("execution-plan").Object("body"); plan["context"] = Context.DeepClone();
        plan["decisionRef"] = Decision.Reference.ToJson(); plan["policyRef"] = Policy.Reference.ToJson();
        plan["executionWindow"] = new JsonObject { ["notBefore"] = Now.ToString("O"), ["expiresAt"] = Now.AddMinutes(10).ToString("O") };
        var step = plan.Array("steps")[0]!.AsObject();
        step["capabilityRef"] = Capability.Reference.ToJson(); step["evidenceRefs"] = new JsonArray(Evidence.Reference.ToJson());
        step["payloadDigest"] = BindingDigests.Payload(Payload);
        step["invocationFingerprint"] = BindingDigests.Invocation(Context, Capability.Reference, step.String("target"), Payload, step.String("expectedState"), step.String("idempotencyDomain"), Capability.Body.Object("endpoint").String("audience"));
        step["expiry"] = Now.AddMinutes(10).ToString("O");
        Plan = Issue("execution-plan", plan);
        var approval = Fixture("approval").Object("body"); approval["context"] = Context.DeepClone();
        approval["planRef"] = Plan.Reference.ToJson(); approval["planDigest"] = Plan.PlanDigest; approval["policyRef"] = Policy.Reference.ToJson();
        approval["issuedAt"] = Now.ToString("O"); approval["expiresAt"] = Now.AddMinutes(10).ToString("O");
        Approval = Issue("approval", approval);
        var invocation = Fixture("command-invocation").Object("body"); invocation["context"] = Context.DeepClone();
        invocation["planRef"] = Plan.Reference.ToJson(); invocation["planDigest"] = Plan.PlanDigest; invocation["capabilityRef"] = Capability.Reference.ToJson();
        invocation["authorityRefs"] = new JsonArray(Approval.Reference.ToJson());
        invocation["payloadDigest"] = step["payloadDigest"]!.DeepClone(); invocation["invocationFingerprint"] = step["invocationFingerprint"]!.DeepClone();
        Invocation = Issue("command-invocation", invocation);
        var receipt = Fixture("operation-receipt-completed").Object("body"); receipt["invocationRef"] = Invocation.Reference.ToJson();
        var post = Fixture("evidence-bundle-postcondition").Object("body"); post["context"] = Context.DeepClone();
        foreach (var n in post.Array("items")) n!.AsObject()["productRef"] = Product.Reference.ToJson();
        var postEvidence = Issue("evidence-bundle-postcondition", post);
        receipt["postconditionEvidenceRefs"] = new JsonArray(postEvidence.Reference.ToJson());
        receipt.Remove("predecessorReceiptRef");
        Receipt = Issue("operation-receipt-completed", receipt);
    }
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
