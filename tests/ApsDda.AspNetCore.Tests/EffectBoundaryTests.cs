using System.Net;
using System.Text;
using System.Text.Json;
using ApsDda.AspNetCore;
using ApsDda.Core;
using Xunit;

namespace ApsDda.AspNetCore.Tests;

public sealed class EffectBoundaryTests
{
    [Theory]
    [InlineData("wrong-audience")]
    [InlineData("wrong-resource")]
    [InlineData("wrong-capability")]
    public async Task Manifest_also_denies_wrong_source_scope(string identity)
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http(identity);
        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(HttpBinding.Prefix + "/manifest")).StatusCode);
    }
    [Theory]
    [InlineData("approve")]
    [InlineData("authorize")]
    [InlineData("execute")]
    [InlineData("reconcile")]
    public async Task Every_effect_action_rejects_duplicate_json_before_runtime_action(string action)
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var workflow = await h.StoreAsync();
        var review = await h.Client(h.Scenario.Approver.Identity).GetWorkflowAsync(workflow.WorkflowId);
        object input = action switch
        {
            "approve" => new ApprovalRequest(HttpBinding.Version, review.ReviewDigest, h.Clock.Now.AddMinutes(4), ["reserve"]),
            "authorize" => new AuthorizationRequest(HttpBinding.Version, workflow.PlanRef),
            _ => new ActionRequest(HttpBinding.Version)
        };
        var serialized = JsonSerializer.Serialize(input, HttpBinding.CreateJsonOptions());
        serialized = serialized.Insert(1, "\"binding\":\"" + HttpBinding.Version + "\",");
        using var http = h.Http(action == "approve" ? h.Scenario.Approver.Identity : h.Scenario.Executor.Identity);
        var response = await http.PostAsync(HttpBinding.Prefix + "/workflows/" + workflow.WorkflowId + "/" + action,
            new StringContent(serialized, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("INVALID_ARTIFACT", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount); Assert.Equal(0, h.Scenario.Runtime.Store.AuthorityUseCount);
    }
    [Fact]
    public async Task Signature_tampering_is_refused_before_plan_retention()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var client = h.Client();
        var plan = await client.PreparePlanAsync(new(HttpBinding.Version, "north", "2", "1"));
        var bad = plan.Plan with { Signature = plan.Plan.Signature with { Value = Jcs.Base64Url(new byte[64]) } };
        var error = await Assert.ThrowsAsync<ApsDdaHttpException>(() => client.StorePlanAsync(plan with { Plan = bad }));
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode); Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount);
    }
    [Fact]
    public async Task PolicyDecision_allow_evidence_cannot_be_used_as_execution_authority()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var workflow = await h.StoreAsync();
        var review = await h.Client().GetWorkflowAsync(workflow.WorkflowId);
        var error = await Assert.ThrowsAsync<ApsDdaHttpException>(() => h.Client().AuthorizeAsync(workflow.WorkflowId,
            new(HttpBinding.Version, review.Plan.Artifact.Body.Ref("policyRef"))));
        Assert.Equal("APPROVAL_REQUIRED", error.Code); Assert.Equal(0, h.Scenario.Runtime.Store.AuthorityUseCount);
    }
    [Fact]
    public async Task Repeating_same_plan_envelope_reuses_original_workflow_not_new_payload_or_identity()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var client = h.Client();
        var plan = await client.PreparePlanAsync(new(HttpBinding.Version, "north", "2", "1"));
        var first = await client.StorePlanAsync(plan); var second = await client.StorePlanAsync(plan);
        Assert.Equal(first.WorkflowId, second.WorkflowId); Assert.Equal(first.PlanRef, second.PlanRef);
        plan.Payloads["reserve"]["quantity"] = "3";
        await Assert.ThrowsAsync<ApsDdaHttpException>(() => client.StorePlanAsync(plan));
        Assert.Equal("2", (await client.GetWorkflowAsync(first.WorkflowId)).Payloads["reserve"].String("quantity"));
    }
    [Fact]
    public async Task Review_digest_cannot_be_reused_for_another_plan_with_same_quantity()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var first = await h.StoreAsync(); var second = await h.StoreAsync();
        var reviewer = h.Client(h.Scenario.Approver.Identity); var firstReview = await reviewer.GetWorkflowAsync(first.WorkflowId);
        var secondReview = await reviewer.GetWorkflowAsync(second.WorkflowId); Assert.NotEqual(firstReview.ReviewDigest, secondReview.ReviewDigest);
        var error = await Assert.ThrowsAsync<ApsDdaHttpException>(() => reviewer.ApproveAsync(second.WorkflowId,
            new(HttpBinding.Version, firstReview.ReviewDigest, h.Clock.Now.AddMinutes(4), ["reserve"])));
        Assert.Equal("INVALID_ARTIFACT", error.Code);
    }
    [Fact]
    public async Task Experimental_binding_is_mandatory_and_original_schema_cannot_be_replaced_by_envelope()
    {
        await using var h = new HttpHarness(); await h.StartAsync();
        var error = await Assert.ThrowsAsync<ApsDdaHttpException>(() => h.Client().PreparePlanAsync(new("APS-DDA/0.1", "north", "2", "1")));
        Assert.Equal("UNSUPPORTED_PROFILE", error.Code);
    }
    [Fact]
    public async Task Historical_operation_can_be_recognized_after_authority_expiry_without_new_effect()
    {
        await using var h = new HttpHarness(loseReply: true); await h.StartAsync(); var workflow = await h.AuthorizeAsync();
        await h.Client().ExecuteNextAsync(workflow.WorkflowId);
        h.Clock.Now = h.Clock.Now.AddMinutes(11); // Both approval and plan execution windows have ended.
        Assert.True((await h.Client().LookupOperationAsync(workflow.WorkflowId, "reserve")).Found);
        Assert.Equal(ApsDda.Runtime.WorkflowState.COMPLETED, (await h.Client().ReconcileAsync(workflow.WorkflowId)).State);
        Assert.Equal(1, h.LostReply!.InvokeCount); Assert.Equal(1, h.Scenario.Runtime.Store.OperationCount);
    }
    [Fact]
    public async Task Unknown_lookup_not_found_remains_unknown_and_never_resends()
    {
        await using var h = new HttpHarness(loseReply: true); await h.StartAsync(); var workflow = await h.AuthorizeAsync();
        await h.Client().ExecuteNextAsync(workflow.WorkflowId); h.LostReply!.HideLookupResults = true;
        Assert.False((await h.Client().LookupOperationAsync(workflow.WorkflowId, "reserve")).Found);
        Assert.Equal(ApsDda.Runtime.WorkflowState.UNKNOWN_OUTCOME, (await h.Client().ReconcileAsync(workflow.WorkflowId)).State);
        Assert.Equal(ApsDda.Runtime.WorkflowState.UNKNOWN_OUTCOME, (await h.Client().ExecuteNextAsync(workflow.WorkflowId)).State);
        Assert.Equal(1, h.LostReply.InvokeCount); Assert.Equal(1, h.Scenario.Runtime.Store.OperationCount);
        Assert.Equal("2", (await h.Client().ReadResourceAsync("north")).Resource.State.String("reserved"));
    }
}
