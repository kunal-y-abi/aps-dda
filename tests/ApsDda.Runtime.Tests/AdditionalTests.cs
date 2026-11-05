using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Runtime;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ApsDda.Runtime.Tests;

public sealed class AdditionalTests
{
    [Fact]
    public void OperationInsertFailureRollsBackActualResourceMutationAndGeneratedReceipt()
    {
        using var h = new Harness(); h.AuthorizeHuman();
        using (var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = h.Store.DatabasePath, Pooling = false }.ToString()))
        {
            c.Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TRIGGER reject_operation BEFORE INSERT ON source_operations BEGIN SELECT RAISE(ABORT,'in-process-test-fault'); END"; cmd.ExecuteNonQuery();
        }
        var w = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller);
        Assert.Equal(WorkflowState.UNKNOWN_OUTCOME, w.State); // Transport-level DB failure isn't advertised as a no-effect receipt.
        Assert.Equal(0, h.Store.OperationCount); var state = h.Store.GetResource(h.Source.Issuer, Harness.Resource); Assert.Equal("1", state.Version); Assert.Equal("0", state.State.String("reserved"));
        using var verify = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = h.Store.DatabasePath, Pooling = false }.ToString()); verify.Open(); using var count = verify.CreateCommand();
        count.CommandText = "SELECT count(*) FROM artifacts WHERE artifact_type IN ('OperationReceipt')"; Assert.Equal(0L, count.ExecuteScalar());
    }
    [Fact]
    public void CrashIntentWithoutCommittedOperationNeverCreatesNewInvocationOnRecovery()
    {
        var calls = 0;
        using var h = new Harness(decorate: inner => new DecoratedAdapter(inner, invoke: _ => { calls++; throw new IOException(); }));
        h.AuthorizeHuman(); var pending = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller);
        h.Reopen(); var recovered = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller);
        Assert.Equal(WorkflowState.UNKNOWN_OUTCOME, recovered.State); Assert.Equal(pending.Steps[0].InvocationRef, recovered.Steps[0].InvocationRef); Assert.Equal(0, h.Store.OperationCount); Assert.Equal(1, calls);
    }
    [Fact]
    public void CancelDurablyPreventsNewEffectForPreviouslyUnknownUncommittedInvocation()
    {
        using var h = new Harness(decorate: inner => new DecoratedAdapter(inner, invoke: _ => throw new TimeoutException()));
        h.AuthorizeHuman(); var pending = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller); h.Runtime.Cancel(h.WorkflowId, h.Caller); h.Reopen();
        var e = Assert.Throws<SourceRefusedException>(() => h.Adapter.Invoke(new(pending.Steps[0].InvocationRef!, h.Caller)));
        Assert.Equal("CANCEL_NOT_POSSIBLE", e.Code); Assert.Equal(0, h.Store.OperationCount);
    }
    [Fact]
    public void SingleStepCancelAfterLostCommittedReplyIsManualNotCleanCancellation()
    {
        using var h = new Harness(decorate: inner => new DecoratedAdapter(inner, invoke: d => { _ = inner.Invoke(d); throw new TimeoutException(); }));
        h.AuthorizeHuman(); h.Runtime.ExecuteNext(h.WorkflowId, h.Caller); Assert.Equal(WorkflowState.UNKNOWN_OUTCOME, h.Runtime.Cancel(h.WorkflowId, h.Caller).State);
        h.Reopen(); Assert.Equal(WorkflowState.MANUAL_RESOLUTION_REQUIRED, h.Runtime.Reconcile(h.WorkflowId, h.Caller).State); Assert.Equal(1, h.Store.OperationCount);
    }
    [Fact]
    public void ApprovalAllowsOnlyExactPermittedSteps()
    {
        using var h = new Harness(stepCount: 2); h.Submit(); var approval = h.Runtime.Approve(h.WorkflowId, h.Approver, h.Clock.Now.AddMinutes(4), ["step-1"]);
        h.Runtime.Authorize(h.WorkflowId, approval.Artifact.Reference, h.Caller); var w = h.Runtime.ExecuteAll(h.WorkflowId, h.Caller);
        Assert.Equal(WorkflowState.MANUAL_RESOLUTION_REQUIRED, w.State); Assert.Equal("APPROVAL_REQUIRED", w.SafeError); Assert.Equal(1, h.Store.OperationCount);
    }
    [Fact]
    public void InvalidApproverEffectSummaryIsRefusedEvenWithValidSignatureAndDigest()
    {
        using var h = new Harness(); h.Submit(); var b = ArtifactFactory.Approval(h.Authority, h.Plan.Artifact, h.Approver, ["step-1"], h.Clock.Now.AddMinutes(4), h.Clock.Now).Artifact.Body;
        b["effectSummary"] = "Do something unrelated";
        var approval = h.Authority.Issue(ArtifactType.Approval, b, h.Clock.Now);
        Assert.Equal("INVALID_ARTIFACT", Assert.Throws<ApsDdaException>(() => h.Runtime.AdmitAuthority(approval, h.Approver)).Code);
    }
    [Fact]
    public void SourceReceiptsFromWrongIssuerCannotBecomeCompleted()
    {
        using var h = new Harness(decorate: inner => new DecoratedAdapter(inner, invoke: d =>
        {
            var result = inner.Invoke(d);
            return result with { Receipt = result.Receipt with { Signature = result.Receipt.Signature with { Issuer = "https://wrong-source.example.invalid" } } };
        }));
        h.AuthorizeHuman(); var w = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller); Assert.Equal(WorkflowState.UNKNOWN_OUTCOME, w.State); Assert.Equal(1, h.Store.OperationCount);
    }
    [Fact]
    public void WrongPlanReceiptReferenceCannotBeVerifiedAsOriginal()
    {
        using var h = new Harness(); h.AuthorizeHuman(); var w = h.Runtime.ExecuteAll(h.WorkflowId, h.Caller);
        var inv = h.Store.GetArtifact(w.Steps[0].InvocationRef!).Artifact;
        var i = new OperationIdentity(Harness.Tenant, h.Source.Issuer, inv.Body.String("idempotencyDomain"), inv.Body.String("idempotencyKey"), inv.Body.String("invocationFingerprint"));
        var original = h.Adapter.Lookup(i, h.Caller)!; var b = original.Receipt.Artifact.Body;
        b["resourceVersion"] = "999"; var wrong = h.Source.Issue(ArtifactType.OperationReceipt, b, h.Clock.Now);
        Assert.Throws<ApsDdaException>(() => h.Adapter.VerifyPostcondition(new(wrong, original.PostconditionEvidence), inv));
    }
    [Fact]
    public void SourceChecksFreshEvidenceAgainBeforeCommitNotOnlyAtSubmit()
    {
        Harness? harness = null;
        using var h = new Harness(decorate: inner => new DecoratedAdapter(inner, invoke: d =>
        { harness!.Clock.Now = harness.Clock.Now.AddMinutes(6); return inner.Invoke(d); })); harness = h;
        h.AuthorizeHuman(); var w = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller); Assert.Equal(WorkflowState.REJECTED, w.State); Assert.Equal(0, h.Store.OperationCount);
    }
    [Fact]
    public void SourceRevalidationAfterMutationRollsBackWhenPrincipalRevokedBeforeCommit()
    {
        using var h = new Harness(); h.AuthorizeHuman();
        var revokingCommand = new RevokeDuringApplyCommand(h.Principals, h.Approver.Identity);
        var source = new SqliteSourceAdapter(h.Runtime, h.SourceConfig, revokingCommand);
        h.Reopen(inner => new DecoratedAdapter(inner, invoke: source.Invoke));
        var w = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller);
        Assert.Equal(WorkflowState.REJECTED, w.State); Assert.Equal("AUTH_DENIED", w.SafeError); Assert.Equal(0, h.Store.OperationCount);
        Assert.Equal("1", h.Store.GetResource(h.Source.Issuer, Harness.Resource).Version);
    }
    [Theory]
    [InlineData("0")]
    [InlineData("01")]
    [InlineData("+2")]
    [InlineData("2.0")]
    [InlineData("2e0")]
    [InlineData("-1")]
    [InlineData("9223372036854775808")]
    public void QuantityProfileRejectsAmbiguousOrUnsupportedExactString(string quantity)
    { Assert.Throws<ApsDdaException>(() => CanonicalQuantity.Parse(quantity)); }
    [Fact]
    public void NotificationDraftCommandHasActualDeterministicSourceStateNotCustomerContact()
    {
        var command = new PrepareNotificationDraftCommand(); var payload = new JsonObject { ["transition"] = command.Transition, ["quantity"] = "1" };
        var after = command.Apply(new JsonObject { ["draftCount"] = "0" }, payload);
        Assert.Equal("1", after.String("draftCount")); Assert.True(command.Verify(after, payload));
    }
    private sealed class RevokeDuringApplyCommand(PrincipalDirectory principals, string identity) : ILocalSourceCommand
    {
        private readonly ReserveCapacityCommand _inner = new();
        public string Transition => _inner.Transition;
        public long Quantity(JsonObject payload) => _inner.Quantity(payload);
        public JsonObject Apply(JsonObject current, JsonObject payload) { var after = _inner.Apply(current, payload); principals.Revoke(identity); return after; }
        public bool Verify(JsonObject postcondition, JsonObject payload) => _inner.Verify(postcondition, payload);
    }
}
