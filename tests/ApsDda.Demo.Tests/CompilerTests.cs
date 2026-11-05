using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Runtime;
using Xunit;

namespace ApsDda.Demo.Tests;

public sealed class CompilerTests
{
    [Fact]
    public void OriginalSchemaArtifactsHaveRealReferencesDigestsAndSignedBinding()
    {
        using var s = new InsuranceSession();
        foreach (var artifact in s.Evidence.Append(s.Decision).Append(s.PolicyDecision).Append(s.Plan).Append(s.Compiled.ApplicationSpec).Append(s.Compiled.BuilderPrompt))
        {
            artifact.Artifact.RequireIntegrity(); Assert.True(ArtifactSchema.Default.Validate(artifact.Artifact.ToJson()).IsValid);
            Assert.StartsWith("sha256:", artifact.Artifact.Header.ContentDigest); Assert.Equal(71, artifact.Artifact.Header.ContentDigest.Length);
        }
        Assert.Equal(43, s.Plan.Artifact.PlanDigest!.Length);
        s.Trust.Validate(s.Compiled.ApplicationSpec, new(ArtifactType.ApplicationSpec, "APS-Orchestrator", s.Orchestrator.Issuer));
        s.Trust.Validate(s.Compiled.BuilderPrompt, new(ArtifactType.BuilderPrompt, "APS-Orchestrator", s.Orchestrator.Issuer));
        s.Compiled.BuilderPrompt.Artifact.Body.Ref("applicationSpecRef").RequireMatches(s.Compiled.ApplicationSpec.Artifact, ArtifactType.ApplicationSpec);
        Assert.Equal(ApplicationCompiler.CompilerVersion, s.Compiled.BuilderPrompt.Artifact.Body.String("compilerVersion"));
        Assert.True(s.Compiled.BuilderPrompt.Artifact.Body["noSecretDeclaration"]!.GetValue<bool>());
    }
    [Fact]
    public void RecompilationUsesTheSameDeterministicTemplateForEquivalentInputsExceptNewApplicationIdentity()
    {
        using var s = new InsuranceSession(); var next = s.Compiler.Compile(s.Decision, s.Plan, s.Evidence, s.Payloads);
        var a = s.Compiled.BuilderPrompt.Artifact.Body.String("promptText").Replace(s.Compiled.ApplicationSpec.Artifact.Header.ContentDigest, "APPLICATION");
        var b = next.BuilderPrompt.Artifact.Body.String("promptText").Replace(next.ApplicationSpec.Artifact.Header.ContentDigest, "APPLICATION");
        Assert.Equal(a, b); Assert.Equal(s.Compiled.ApplicationSpec.Artifact.Body.ToJsonString(), next.ApplicationSpec.Artifact.Body.ToJsonString());
        Assert.Null(next.ApplicationSpec.Artifact.Body["authorityRef"]); Assert.Null(next.BuilderPrompt.Artifact.Body["authorityRef"]);
    }
    [Theory]
    [InlineData("arbitrary-html")]
    [InlineData("payment-button")]
    [InlineData("shell-executor")]
    public void UnsupportedComponentsAreActuallyRefused(string component)
    {
        using var s = new InsuranceSession(); var options = ApplicationCompiler.DefaultOptions(); options["components"] = new JsonArray(component);
        Assert.Equal("UNSUPPORTED_PROFILE", Assert.Throws<ApsDdaException>(() => s.Compiler.Compile(s.Decision, s.Plan, s.Evidence, s.Payloads, options)).Code);
    }
    [Theory]
    [InlineData("apiKey")]
    [InlineData("privateKey")]
    [InlineData("secrets")]
    [InlineData("arbitraryCode")]
    public void SecretAndUnreviewedInputFieldsNeverEnterDerivedArtifacts(string field)
    {
        using var s = new InsuranceSession(); var options = ApplicationCompiler.DefaultOptions(); options[field] = "synthetic-secret-marker-not-a-real-secret";
        Assert.Throws<ApsDdaException>(() => s.Compiler.Compile(s.Decision, s.Plan, s.Evidence, s.Payloads, options));
        Assert.DoesNotContain("synthetic-secret-marker", s.Compiled.BuilderPrompt.Artifact.ToJsonString());
    }
    [Fact]
    public void MissingEvidenceAndPayloadReferencesAreRefused()
    {
        using var s = new InsuranceSession();
        Assert.Throws<ApsDdaException>(() => s.Compiler.Compile(s.Decision, s.Plan, s.Evidence.Skip(1).ToArray(), s.Payloads));
        Assert.Throws<ApsDdaException>(() => s.Compiler.Compile(s.Decision, s.Plan, s.Evidence, new Dictionary<string, JsonObject>()));
    }
    [Fact]
    public void WrongPlanIsNotAcceptedByApplicationSpecOrAuthority()
    {
        using var s = new InsuranceSession();
        var otherPlan = s.Orchestrator.Issue(ArtifactType.ExecutionPlan, s.Plan.Artifact.Body, s.Clock.Now);
        Assert.Throws<ApsDdaException>(() => s.Compiler.ValidateApplication(s.Compiled.ApplicationSpec.Artifact, s.Decision.Artifact, otherPlan.Artifact));
        s.Authorize(); Assert.Throws<ApsDdaException>(() => ArtifactBindings.RequireApprovalBinding(s.AuthorityArtifact!.Artifact, otherPlan.Artifact, s.PolicyDecision.Artifact));
    }
    [Fact]
    public void SignedButDifferentNumericalAllocationCannotCompileForChosenDecision()
    {
        using var s = new InsuranceSession(); var b = s.Plan.Artifact.Body; var payloads = s.Payloads.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone());
        payloads["reserve-north"]["quantity"] = "2"; var step = b.Array("steps")[0]!.AsObject();
        step["payloadDigest"] = BindingDigests.Payload(payloads["reserve-north"]);
        step["invocationFingerprint"] = BindingDigests.Invocation(b.Object("context"), s.CapacityCapability.Artifact.Reference, step.String("target"), payloads["reserve-north"], "1", step.String("idempotencyDomain"), InsuranceSession.CapacityAudience);
        var changed = s.Orchestrator.Issue(ArtifactType.ExecutionPlan, b, s.Clock.Now);
        Assert.Throws<ApsDdaException>(() => s.Compiler.Compile(s.Decision, changed, s.Evidence, payloads));
    }
    [Fact]
    public void SourceProseNeverBecomesCompilerConfiguration()
    {
        using var s = new InsuranceSession(); var options = ApplicationCompiler.DefaultOptions(); options["targetBuilder"] = "https://untrusted.example.invalid/run-code";
        Assert.Throws<ApsDdaException>(() => s.Compiler.Compile(s.Decision, s.Plan, s.Evidence, s.Payloads, options));
    }
    [Fact]
    public void PolicyGrantQuantityOverConfiguredInternalBoundIsRefusedBeforeEffects()
    {
        using var s = new InsuranceSession(boundedPolicy: true); s.Submit();
        var broad = ArtifactFactory.PolicyGrant(s.Authority, s.Plan.Artifact, s.DraftCapability.Artifact, InsuranceSession.DraftTarget, s.Evidence.Select(e => e.Artifact.Reference), InsuranceSession.Purpose, 2, 1, s.Clock.Now.AddMinutes(4), s.Clock.Now, s.Authority.Issuer + "/status");
        Assert.Throws<ApsDdaException>(() => s.Runtime.AdmitAuthority(broad, s.Granter)); Assert.Equal(0, s.Store.OperationCount);
    }
    [Fact]
    public void DerivedApplicationAndPromptAreNeverExecutionAuthority()
    {
        using var s = new InsuranceSession(); s.Submit();
        foreach (var a in new[] { s.Compiled.ApplicationSpec, s.Compiled.BuilderPrompt })
            Assert.Equal("APPROVAL_REQUIRED", Assert.Throws<ApsDdaException>(() => s.Runtime.AdmitAuthority(a, s.Approver)).Code);
        Assert.Equal(0, s.Store.OperationCount);
    }
    [Fact]
    public void ExportContainsActualSignedReceiptsAndNoDatabaseOrPrivateKeys()
    {
        using var s = new InsuranceSession(); var report = ScenarioRunner.Run(s, "happy"); var directory = Path.Combine(Path.GetTempPath(), "aps-dda-export-test-" + Guid.NewGuid());
        try
        {
            ScenarioRunner.Export(s, report, directory);
            var chain = StrictJson.Parse(File.ReadAllText(Path.Combine(directory, "artifact-chain.json"))).AsArray();
            var signed = chain.Select(n => SignedArtifact.Parse(n!.ToJsonString())).ToArray();
            Assert.Equal(3, signed.Count(a => a.Artifact.Type == ArtifactType.OperationReceipt)); foreach (var artifact in signed) artifact.Artifact.RequireIntegrity();
            Assert.DoesNotContain(Directory.GetFiles(directory), f => f.EndsWith(".sqlite", StringComparison.Ordinal) || f.EndsWith(".pem", StringComparison.Ordinal));
            Assert.DoesNotContain("PRIVATE KEY", File.ReadAllText(Path.Combine(directory, "artifact-chain.json")));
        }
        finally { Directory.Delete(directory, true); }
    }
}
