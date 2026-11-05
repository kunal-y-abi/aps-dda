using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApsDda.Core;
using Xunit;

namespace ApsDda.Core.Tests;

public class ArtifactTests
{
    public static TheoryData<string> OriginalFixtures => new()
    {
        "application-spec", "approval", "builder-prompt", "capability-manifest", "command-invocation", "data-product-purchase-orders",
        "decision-spec", "evidence-bundle-observational", "evidence-bundle-postcondition", "execution-plan",
        "operation-receipt-accepted", "operation-receipt-completed", "operation-receipt-unknown-effect", "policy-decision", "process-capability-cancel-po"
    };
    [Theory][MemberData(nameof(OriginalFixtures))]
    public void OriginalFixturesValidateShapeOnlyAndPlaceholderIntegrityIsRejected(string name)
    {
        var document = TestArtifacts.Fixture(name);
        Assert.True(new ArtifactSchema().Validate(document).IsValid);
        var a = Artifact.FromJson(document);
        Assert.Throws<ApsDdaException>(() => a.RequireIntegrity());
    }
    [Fact]
    public void EmbeddedSchemaHashMatchesOriginalUnchangedBundle()
    {
        Assert.Equal("3aa0469657fa30554d4989285105354964bdd8c76b90356b0b6e44eac5847ea4", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ArtifactSchema.ReadOriginalSchema()))));
    }
    [Fact]
    public void IndependentStringOnlyPlanGoldenDigestMatchesExactProjection()
    {
        // Independent Node/V8 JSON.stringify + recursive ordinal sorting + SHA256 calculation.
        // This particular vector contains no JSON numbers; historical nested refs remain shape-only.
        var document = TestArtifacts.Fixture("execution-plan");
        Assert.Equal("2_WRbMhE08b7N9KKfcRieCYEKiVzgHv3KtlvIa_KiGU", Artifact.ComputePlanDigest(document));
        document.Object("body")["planDigest"] = Artifact.ComputePlanDigest(document);
        Assert.Equal("sha256:05e57009adf0294acdb30842bae78d5487e7de722c7300d7245d5702153b16fe", Artifact.ComputeContentDigest(document));
    }
    [Fact]
    public void EntireRealDigestChainAndExactBindingPass()
    {
        var c = new TestArtifacts();
        foreach (var a in new[] { c.Product, c.Capability, c.Evidence, c.Decision, c.Policy, c.Plan, c.Approval, c.Invocation, c.Receipt })
        {
            a.RequireIntegrity(); a.Reference.RequireMatches(a, a.Type);
            Assert.True(ArtifactSchema.Default.Validate(a.ToJson()).IsValid);
            Assert.Equal(a.ToCanonicalBytes(), Artifact.Parse(a.ToJsonString()).ToCanonicalBytes());
            Assert.Equal(a.ToCanonicalBytes(), JsonSerializer.Deserialize<Artifact>(JsonSerializer.Serialize(a))!.ToCanonicalBytes());
        }
        Assert.Equal(43, c.Plan.PlanDigest!.Length);
        Assert.Equal(32, Jcs.DecodeBase64Url(c.Plan.PlanDigest).Length);
        ArtifactBindings.RequirePlanDag(c.Plan);
        ArtifactBindings.RequireApprovalBinding(c.Approval, c.Plan, c.Policy);
        ArtifactBindings.RequireInvocationBinding(c.Invocation, c.Plan, c.Payload, c.Capability, c.Capability.Body.Object("endpoint").String("audience"));
        ArtifactBindings.RequireReceiptBinding(c.Receipt, c.Invocation);
    }
    [Fact]
    public void ExactPlanAndArtifactProjectionsRemoveOnlyDesignatedFields()
    {
        var plan = new TestArtifacts().Plan;
        var expected = plan.ToJson(); expected.Object("header").Remove("contentDigest"); expected.Object("body").Remove("planDigest");
        Assert.True(JsonNode.DeepEquals(expected, Artifact.PlanCore(plan.ToJson())));
        var expectedArtifact = plan.ToJson(); expectedArtifact.Object("header").Remove("contentDigest");
        Assert.True(JsonNode.DeepEquals(expectedArtifact, Artifact.ArtifactCore(plan.ToJson())));
        var before = plan.PlanDigest;
        var document = plan.ToJson(); document.Object("header")["contentDigest"] = "sha256:" + new string('a', 64); document.Object("body")["planDigest"] = new string('A', 43);
        Assert.Equal(before, Artifact.ComputePlanDigest(document));
        Assert.NotEqual(plan.Header.ContentDigest, Artifact.ComputeContentDigest(document));
    }
    [Theory]
    [InlineData("tenantId")][InlineData("subjectId")][InlineData("workloadId")][InlineData("executorId")][InlineData("purpose")][InlineData("correlationId")]
    [InlineData("target")][InlineData("payloadDigest")][InlineData("expectedState")][InlineData("idempotencyKey")][InlineData("idempotencyDomain")]
    [InlineData("invocationFingerprint")][InlineData("risk")][InlineData("expiry")][InlineData("manualRecoveryOwner")]
    [InlineData("decisionRef")][InlineData("policyRef")][InlineData("capabilityRef")][InlineData("evidenceRefs")][InlineData("executionWindow")]
    [InlineData("issuer")][InlineData("issuedAt")][InlineData("artifactVersion")][InlineData("id")][InlineData("extensions")]
    public void EveryEffectBindingAndHeaderChangeChangesBothDigests(string field)
    {
        var a = new TestArtifacts().Plan; var document = a.ToJson(); var body = document.Object("body"); var step = body.Array("steps")[0]!.AsObject();
        if (body.Object("context").ContainsKey(field)) body.Object("context")[field] = field == "correlationId" ? Guid.NewGuid().ToString("D") : "changed";
        else if (step.ContainsKey(field) && step[field] is JsonValue) step[field] = "changed";
        else if (field == "capabilityRef") step.Object(field)["artifactVersion"] = "2.0.0";
        else if (field == "evidenceRefs") step.Array(field)[0]!.AsObject()["artifactVersion"] = "2.0.0";
        else if (field is "decisionRef" or "policyRef") body.Object(field)["artifactVersion"] = "2.0.0";
        else if (field == "executionWindow") body.Object(field)["expiresAt"] = TestArtifacts.Now.AddHours(1).ToString("O");
        else if (field == "extensions") body["extensions"] = new JsonObject { ["https://example.invalid/ext"] = new JsonObject { ["version"] = "1.0.0", ["critical"] = true, ["effect"] = "changed" } };
        else document.Object("header")[field] = "changed";
        Assert.NotEqual(a.PlanDigest, Artifact.ComputePlanDigest(document));
        Assert.NotEqual(a.Header.ContentDigest, Artifact.ComputeContentDigest(document));
    }
    [Fact]
    public void ArrayOrderAndSelfFieldsAndMutableCloneBehavePrecisely()
    {
        var a = new TestArtifacts().Plan;
        var body = a.Body; body.Object("context")["purpose"] = "changed";
        a.RequireIntegrity(); Assert.NotEqual("changed", a.Body.Object("context").String("purpose"));
        var doc = a.ToJson(); var steps = doc.Object("body").Array("steps"); var second = steps[0]!.DeepClone().AsObject(); second["stepId"] = "second"; steps.Add(second);
        var forward = Artifact.ComputePlanDigest(doc);
        var first = steps[0]!.DeepClone(); var last = steps[1]!.DeepClone(); steps[0] = last; steps[1] = first;
        Assert.NotEqual(forward, Artifact.ComputePlanDigest(doc));
    }
    [Theory][InlineData("type")][InlineData("id")][InlineData("version")][InlineData("digest")]
    public void ReferenceAllPinsMustMatch(string field)
    {
        var a = new TestArtifacts().Decision; var r = a.Reference;
        r = field switch { "type" => r with { ArtifactType = "Task" }, "id" => r with { Id = Guid.NewGuid().ToString("D") }, "version" => r with { ArtifactVersion = "2.0.0" }, _ => r with { ContentDigest = "sha256:" + new string('a', 64) } };
        Assert.Throws<ApsDdaException>(() => r.RequireMatches(a));
    }
    [Fact]
    public void NoMinimalCompetingWireOrUnknownMembersOrMalformedFormats()
    {
        Assert.False(ArtifactSchema.Default.Validate("{\"artifactType\":\"Task\",\"header\":{},\"body\":{}}").IsValid);
        var fixture = TestArtifacts.Fixture("approval"); fixture["extra"] = true; Assert.False(ArtifactSchema.Default.Validate(fixture).IsValid);
        fixture = TestArtifacts.Fixture("approval"); fixture.Object("body")["admin"] = true; Assert.False(ArtifactSchema.Default.Validate(fixture).IsValid);
        fixture = TestArtifacts.Fixture("approval"); fixture.Object("header")["id"] = "not-a-uuid"; Assert.False(ArtifactSchema.Default.Validate(fixture).IsValid);
        fixture = TestArtifacts.Fixture("approval"); fixture.Object("header")["issuedAt"] = "not-a-date"; Assert.False(ArtifactSchema.Default.Validate(fixture).IsValid);
        fixture = TestArtifacts.Fixture("approval"); fixture.Object("header")["issuer"] = "relative-uri"; Assert.False(ArtifactSchema.Default.Validate(fixture).IsValid);
        fixture = TestArtifacts.Fixture("execution-plan"); fixture.Object("body")["planDigest"] = "sha256:" + new string('a',64); Assert.False(ArtifactSchema.Default.Validate(fixture).IsValid);
        fixture = TestArtifacts.Fixture("operation-receipt-completed"); fixture.Object("body").Remove("postconditionEvidenceRefs"); Assert.False(ArtifactSchema.Default.Validate(fixture).IsValid);
        fixture = TestArtifacts.Fixture("execution-plan"); fixture.Object("body").Array("steps")[0]!.AsObject().Remove("manualRecoveryOwner"); Assert.False(ArtifactSchema.Default.Validate(fixture).IsValid);
    }
    [Fact]
    public void ExactReferenceResolutionDoesNotNormalizeIdsOrFetchHints()
    {
        var a = new TestArtifacts().Decision;
        (a.Reference with { Uri = "https://untrusted.example.invalid/no-fetch" }).RequireMatches(a);
        Assert.Throws<ApsDdaException>(() => (a.Reference with { Id = a.Reference.Id.ToUpperInvariant() }).RequireMatches(a));
    }
    [Fact]
    public void MissingAuthorityOrWrongDAGOrPlanBoundReferenceRejected()
    {
        var c = new TestArtifacts();
        var plan = c.Plan.Body; plan.Array("steps")[0]!.AsObject()["dependsOn"] = new JsonArray("missing");
        Assert.Throws<ApsDdaException>(() => ArtifactBindings.RequirePlanDag(Artifact.Create(ArtifactType.ExecutionPlan, TestArtifacts.Issuer, plan, TestArtifacts.Now)));
        var invocation = c.Invocation.Body; invocation["planDigest"] = new string('A',43);
        Assert.Throws<ApsDdaException>(() => ArtifactBindings.RequirePlanReference(Artifact.Create(ArtifactType.CommandInvocation, TestArtifacts.Issuer, invocation, TestArtifacts.Now), c.Plan));
        invocation = c.Invocation.Body; invocation["authorityRefs"] = new JsonArray(c.Policy.Reference.ToJson());
        Assert.Throws<ApsDdaException>(() => ArtifactBindings.RequireInvocationBinding(Artifact.Create(ArtifactType.CommandInvocation, TestArtifacts.Issuer, invocation, TestArtifacts.Now), c.Plan, c.Payload, c.Capability, c.Capability.Body.Object("endpoint").String("audience")));
    }
    [Fact]
    public void TamperedPayloadContextCapabilityAndReceiptCannotBind()
    {
        var c = new TestArtifacts(); var payload = (JsonObject)c.Payload.DeepClone(); payload["quantity"] = "200";
        Assert.Throws<ApsDdaException>(() => ArtifactBindings.RequireInvocationBinding(c.Invocation, c.Plan, payload, c.Capability, c.Capability.Body.Object("endpoint").String("audience")));
        Assert.Throws<ApsDdaException>(() => ArtifactBindings.RequireInvocationBinding(c.Invocation, c.Plan, c.Payload, c.Capability, "https://wrong.example.invalid"));
        var other = (JsonObject)c.Context.DeepClone(); other["workloadId"] = "wrong"; Assert.Throws<ApsDdaException>(() => ArtifactBindings.RequireSameContext(c.Context, other));
        Assert.Throws<ApsDdaException>(() => ArtifactBindings.RequireReceiptBinding(c.Receipt, Artifact.Create(ArtifactType.CommandInvocation, TestArtifacts.Issuer, c.Invocation.Body, TestArtifacts.Now)));
    }
}
