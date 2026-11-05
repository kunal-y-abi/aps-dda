using System.Text.Json;
using System.Text.Json.Nodes;
using ApsDda.AspNetCore;
using ApsDda.Core;
using ApsDda.Demo;
using ApsDda.Runtime;
using ApsDda.Runtime.Tests;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ApsDda.Acceptance.Tests;

// AI-assisted source development. Real file-backed synthetic source effects, not production integration.
public sealed class AcceptanceTests
{
    public static IEnumerable<object[]> Scenarios => ScenarioRunner.Supported.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void EveryConsoleScenarioHasMeasuredEffectsAndTruthfulAuthority(string scenario)
    {
        using var session = ScenarioRunner.Create(scenario);
        var report = ScenarioRunner.Run(session, scenario);
        var operations = session.Store.OperationCount;
        var reserved = AllocationModel.RegionNames.Sum(r => CanonicalQuantity.Parse(
            session.Store.GetResource(session.CapacitySigner.Issuer, InsuranceSession.CapacityTarget(r)).State.String("reserved"), allowZero: true));
        var drafts = CanonicalQuantity.Parse(session.Store.GetResource(session.DraftSigner.Issuer, InsuranceSession.DraftTarget).State.String("draftCount"), allowZero: true);
        Assert.Equal(operations, report.After["operations"]!.GetValue<long>());
        switch (scenario)
        {
            case "happy": case "maliciousdataignored": case "lostresponse-reconcile":
                Assert.Equal(WorkflowState.COMPLETED, report.Workflow!.State);
                Assert.Equal(3, operations); Assert.Equal(6, reserved); Assert.Equal(0, drafts);
                Assert.All(report.Workflow.Steps, s => Assert.NotNull(s.ReceiptRef));
                break;
            case "boundedpolicy":
                Assert.Equal(WorkflowState.COMPLETED, report.Workflow!.State);
                Assert.Equal(1, operations); Assert.Equal(0, reserved); Assert.Equal(1, drafts);
                Assert.Equal(ArtifactType.Authority, session.AuthorityArtifact!.Artifact.Type);
                break;
            case "partial-manual": case "partial-approval":
                Assert.Equal(WorkflowState.MANUAL_RESOLUTION_REQUIRED, report.Workflow!.State);
                Assert.Equal(1, operations); Assert.Equal(3, reserved); Assert.Equal(0, drafts);
                Assert.Equal("synthetic-capacity-owner", report.Workflow.Steps[1].ManualRecoveryOwner);
                break;
            case "stale": case "expired": case "revoked":
                Assert.Equal(WorkflowState.REJECTED, report.Workflow!.State);
                Assert.Equal(0, operations); Assert.Equal(0, reserved); Assert.Equal(0, drafts);
                break;
            default:
                Assert.Equal("NOT_ADMITTED", report.Admission); Assert.Null(report.Workflow);
                Assert.Equal(0, session.Store.AuthorityUseCount);
                Assert.Equal(0, operations); Assert.Equal(0, reserved); Assert.Equal(0, drafts);
                break;
        }
        if (scenario == "lostresponse-reconcile")
        {
            Assert.Equal(WorkflowState.UNKNOWN_OUTCOME, report.History[1].State);
            Assert.Equal(report.History[1].Steps[0].InvocationRef, report.Workflow!.Steps[0].InvocationRef);
            Assert.Equal(3, session.InvokeCalls); Assert.Equal(1, session.LookupCalls);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectSourceCallCannotBypassAuthenticatedAdmissionOrConsumption(bool admit)
    {
        using var h = new Harness(); h.Submit();
        var approval = ArtifactFactory.Approval(h.Authority, h.Plan.Artifact, h.Approver, ["step-1"], h.Clock.Now.AddMinutes(3), h.Clock.Now);
        if (admit) h.Runtime.AdmitAuthority(approval, h.Approver); else h.Store.Retain(approval);
        var invocation = ArtifactFactory.Invocation(h.Orchestrator, h.Plan.Artifact, "step-1", approval.Artifact, Harness.Audience + "/receipts", h.Clock.Now);
        h.Store.Retain(invocation);
        Assert.Throws<SourceRefusedException>(() => h.Adapter.Invoke(new(invocation.Artifact.Reference, h.Caller)));
        Assert.Equal(0, h.Store.AuthorityUseCount); AssertNoSourceEffect(h);
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("revocation")]
    [InlineData("stale")]
    public void SourceIndependentlyRejectsChangesAfterCoordinatorDispatch(string mutation)
    {
        using var h = new Harness(decorate: inner => new DecoratedAdapter(inner, invoke: _ => throw new TimeoutException()));
        h.AuthorizeHuman(); var pending = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller);
        if (mutation == "revocation") h.Store.RevokeArtifact(h.Approval!.Artifact.Reference, h.Clock.Now);
        else Sql(h, mutation == "payload" ? "UPDATE workflow_steps SET payload=$value" : "UPDATE resources SET version=2",
            mutation == "payload" ? "{\"quantity\":\"3\",\"transition\":\"reserve-internal-capacity\"}" : null);
        h.Reopen();
        Assert.Throws<SourceRefusedException>(() => h.Adapter.Invoke(new(pending.Steps[0].InvocationRef!, h.Caller)));
        Assert.Equal(1, h.Store.AuthorityUseCount); AssertNoSourceEffect(h);
    }

    [Fact]
    public void CorruptedActualRetainedPostconditionBlocksCompletionUntilOriginalValueRestored()
    {
        using var h = new Harness(decorate: inner => new DecoratedAdapter(inner, invoke: d => { inner.Invoke(d); throw new IOException(); }));
        h.AuthorizeHuman(); var pending = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller);
        var invocation = h.Store.GetArtifact(pending.Steps[0].InvocationRef!).Artifact;
        var identity = Identity(h, invocation); var original = h.Adapter.Lookup(identity, h.Caller)!;
        var valueRef = original.PostconditionEvidence.Artifact.Body.Array("items")[0]!.AsObject().String("valueRef");
        var value = h.Store.GetEvidenceValue(valueRef); var changed = (JsonObject)value.DeepClone(); changed.Object("state")["reserved"] = "999";
        Sql(h, "UPDATE evidence_values SET value=$value WHERE valueref='" + valueRef + "'", changed.ToJsonString());
        h.Reopen();
        Assert.Equal(WorkflowState.UNKNOWN_OUTCOME, h.Runtime.Reconcile(h.WorkflowId, h.Caller).State);
        Assert.Null(h.Store.GetWorkflow(h.WorkflowId).Steps[0].ReceiptRef);
        Assert.Equal(1, h.Store.OperationCount); Assert.Equal("2", h.Store.GetResource(h.Source.Issuer, Harness.Resource).State.String("reserved"));
        Sql(h, "UPDATE evidence_values SET value=$value WHERE valueref='" + valueRef + "'", value.ToJsonString());
        Assert.Equal(WorkflowState.COMPLETED, h.Runtime.Reconcile(h.WorkflowId, h.Caller).State);
        Assert.Equal(invocation.Reference, h.Store.GetWorkflow(h.WorkflowId).Steps[0].InvocationRef);
        var duplicate = h.Adapter.Invoke(new(invocation.Reference, h.Caller));
        Assert.Equal(original.Receipt.Artifact.Reference, duplicate.Receipt.Artifact.Reference); Assert.Equal(1, h.Store.OperationCount);
    }

    [Fact]
    public void DerivedBuilderArtifactCannotBeConsumedAsRuntimeAuthorityAfterHttpRoundtrip()
    {
        using var s = new InsuranceSession(); s.Submit();
        var request = new AuthorizationRequest(HttpBinding.Version, s.Compiled.BuilderPrompt.Artifact.Reference);
        var wire = JsonSerializer.Serialize(request, HttpBinding.CreateJsonOptions());
        var parsed = StrictJson.ParseObject(wire).Deserialize<AuthorizationRequest>(HttpBinding.CreateJsonOptions())!;
        Assert.Equal("APPROVAL_REQUIRED", Assert.Throws<ApsDdaException>(() => s.Runtime.Authorize(s.WorkflowId, parsed.AuthorityRef, s.Caller)).Code);
        Assert.Equal(0, s.Store.OperationCount); Assert.Equal(0, s.Store.AuthorityUseCount);
    }

    private static void AssertNoSourceEffect(Harness h)
    {
        Assert.Equal(0, h.Store.OperationCount);
        Assert.Equal("20", h.Store.GetResource(h.Source.Issuer, Harness.Resource).State.String("available"));
        Assert.Equal("0", h.Store.GetResource(h.Source.Issuer, Harness.Resource).State.String("reserved"));
    }
    private static OperationIdentity Identity(Harness h, Artifact inv) => new(Harness.Tenant, h.Source.Issuer,
        inv.Body.String("idempotencyDomain"), inv.Body.String("idempotencyKey"), inv.Body.String("invocationFingerprint"));
    private static void Sql(Harness h, string sql, string? value)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = h.Store.DatabasePath, Pooling = false }.ToString());
        c.Open(); using var command = c.CreateCommand(); command.CommandText = sql;
        if (value is not null) command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }
}
