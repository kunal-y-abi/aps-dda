using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Runtime;
using Xunit;

namespace ApsDda.Demo.Tests;

public sealed class BoundaryTests
{
    [Fact]
    public void ChangingSyntheticBudgetReallyChangesFeasibleSetAndAllocation()
    {
        var data = AllocationModel.LoadDataset(); var original = new AllocationModel(data).Compare(); data.Object("finance")["budget"] = "300";
        var changedModel = new AllocationModel(data); var changed = changedModel.Compare();
        Assert.True(changed.FeasibleCandidates < original.FeasibleCandidates);
        Assert.NotEqual(original.Alternatives[1].AdditionalTeams.ToArray(), changed.Alternatives[1].AdditionalTeams.ToArray());
        Assert.All(changed.Alternatives, p => Assert.True(p.Cost <= changedModel.Budget));
        Assert.NotEqual(Jcs.Sha256Digest(original.Parameters), Jcs.Sha256Digest(changed.Parameters));
    }
    [Fact]
    public void ChangingServiceFloorReallyConstrainsBothPlans()
    {
        var data = AllocationModel.LoadDataset(); data.Object("capacity").Array("regions")[2]!.AsObject()["minimumAdditionalTeams"] = "2";
        var changed = new AllocationModel(data).Compare(); Assert.All(changed.Alternatives, p => Assert.True(p.AdditionalTeams[2] >= 2));
    }
    [Fact]
    public void ChangingMinimumServedItemsReallyConstrainsBothPlans()
    {
        var data = AllocationModel.LoadDataset(); data.Object("capacity").Array("regions")[2]!.AsObject()["minimumServedItems"] = "70";
        var model = new AllocationModel(data); var changed = model.Compare();
        Assert.All(changed.Alternatives, p => Assert.True(Math.Min(model.Regions[2].Open + model.Regions[2].ExpectedAdded, (model.Regions[2].BaseTeams + p.AdditionalTeams[2]) * model.ItemsPerTeam) >= model.Regions[2].MinimumServedItems));
    }
    [Fact]
    public void CompilerRejectsAnUnreviewedCapabilityEvenWithFreshPlanDigestsAndSignature()
    {
        using var s = new InsuranceSession(); var body = s.Plan.Artifact.Body; var step = body.Array("steps")[0]!.AsObject();
        step["capabilityRef"] = s.DraftCapability.Artifact.Reference.ToJson();
        var changed = s.Orchestrator.Issue(ArtifactType.ExecutionPlan, body, s.Clock.Now); changed.Artifact.RequireIntegrity();
        Assert.Throws<ApsDdaException>(() => s.Compiler.Compile(s.Decision, changed, s.Evidence, s.Payloads));
    }
    [Fact]
    public void UnsupportedExtensionIsNotAcceptedAsApplicationInstructions()
    {
        using var s = new InsuranceSession(); var body = s.Decision.Artifact.Body;
        body.Object("extensions")["https://unreviewed.example.invalid/instructions"] = new JsonObject { ["version"] = "1.0.0", ["critical"] = false, ["code"] = "never-run-this" };
        var changed = s.Orchestrator.Issue(ArtifactType.DecisionSpec, body, s.Clock.Now);
        Assert.Equal("UNKNOWN_CRITICAL_EXTENSION", Assert.Throws<ApsDdaException>(() => s.Compiler.Compile(changed, s.Plan, s.Evidence, s.Payloads)).Code);
    }
    [Fact]
    public void ExportedPublicPinsVerifyActualNativeSignaturesAndValuesHaveTheirDigest()
    {
        using var s = new InsuranceSession(); var report = ScenarioRunner.Run(s, "happy"); var directory = Path.Combine(Path.GetTempPath(), "aps-dda-export-verify-" + Guid.NewGuid());
        try
        {
            ScenarioRunner.Export(s, report, directory); var bindings = StrictJson.ParseObject(File.ReadAllText(Path.Combine(directory, "bindings.json")));
            foreach (var n in StrictJson.Parse(File.ReadAllText(Path.Combine(directory, "artifact-chain.json"))).AsArray())
            {
                var signed = SignedArtifact.Parse(n!.ToJsonString());
                var pin = bindings.Array("publicTrustPins").Single(k => k!.AsObject().String("issuer") == signed.Signature.Issuer)!.AsObject();
                Assert.True(DetachedSigning.VerifyCryptographic(signed, Convert.FromBase64String(pin.String("publicKeySpkiBase64"))));
            }
            foreach (var value in bindings.Object("evidenceValues")) Assert.Equal(value.Key, "urn:aps-dda:value:" + Jcs.Sha256Digest(value.Value!));
            Assert.DoesNotContain("PRIVATE KEY", bindings.ToJsonString());
        }
        finally { Directory.Delete(directory, true); }
    }
}
