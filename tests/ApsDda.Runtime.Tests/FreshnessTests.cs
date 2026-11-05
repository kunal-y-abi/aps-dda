using ApsDda.Core;
using ApsDda.Runtime;
using Xunit;

namespace ApsDda.Runtime.Tests;

public sealed class FreshnessTests
{
    [Fact]
    public void CurrentApprovalAndPolicyCannotOverrideExpiredDecisionEvidenceAtDispatch()
    {
        using var h = new Harness(); h.Submit(); var a = h.Runtime.Approve(h.WorkflowId, h.Approver, h.Clock.Now.AddMinutes(9));
        h.Runtime.Authorize(h.WorkflowId, a.Artifact.Reference, h.Caller); h.Clock.Now = h.Clock.Now.AddMinutes(6);
        var runtime = new WorkflowRuntime(h.Store, h.Trust, h.Principals, h.Orchestrator, h.Authority, h.Policy, h.Clock, TimeSpan.FromMinutes(10));
        runtime.RegisterSource(new SqliteSourceAdapter(runtime, h.SourceConfig, new ReserveCapacityCommand()));
        var w = runtime.ExecuteNext(h.WorkflowId, h.Caller); Assert.Equal(WorkflowState.REJECTED, w.State); Assert.Equal("EVIDENCE_STALE", w.SafeError); Assert.Equal(0, h.Store.OperationCount);
    }
    [Fact]
    public void ExpiryOfOriginalAuthorityDoesNotEraseAnAlreadyCommittedOperation()
    {
        using var h = new Harness(decorate: inner => new DecoratedAdapter(inner, invoke: d => { _ = inner.Invoke(d); throw new TimeoutException(); }));
        h.AuthorizeHuman(); var unknown = h.Runtime.ExecuteNext(h.WorkflowId, h.Caller); Assert.Equal(WorkflowState.UNKNOWN_OUTCOME, unknown.State);
        h.Clock.Now = h.Clock.Now.AddMinutes(11); h.Store.RevokeArtifact(h.Approval!.Artifact.Reference, h.Clock.Now); h.Reopen();
        var w = h.Runtime.Reconcile(h.WorkflowId, h.Caller); Assert.Equal(WorkflowState.COMPLETED, w.State); Assert.Equal(unknown.Steps[0].InvocationRef, w.Steps[0].InvocationRef); Assert.Equal(1, h.Store.OperationCount);
    }
}
