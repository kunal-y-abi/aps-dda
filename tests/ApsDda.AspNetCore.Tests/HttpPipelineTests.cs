using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApsDda.Api;
using ApsDda.AspNetCore;
using ApsDda.Core;
using ApsDda.Runtime;
using Xunit;

namespace ApsDda.AspNetCore.Tests;

public sealed class HttpPipelineTests
{
    private static readonly JsonSerializerOptions Json = HttpBinding.CreateJsonOptions();
    private static async Task<HttpResponseMessage> Raw(HttpClient http, string path, string body) =>
        await http.PostAsync(HttpBinding.Prefix + path, new StringContent(body, Encoding.UTF8, "application/json"));
    private static async Task AssertCode(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        var error = StrictJson.ParseObject(text).Deserialize<HttpError>(Json);
        Assert.Equal(code, error!.Code); Assert.Equal(HttpBinding.Version, error.Binding);
        Assert.DoesNotContain("sample-", text); Assert.DoesNotContain("Exception", text); Assert.DoesNotContain("Bearer", text);
    }
    [Fact]
    public async Task Typed_client_traverses_real_pipeline_exact_approval_effect_and_original_receipt()
    {
        await using var h = new HttpHarness(); await h.StartAsync();
        var client = h.Client(); var manifest = await client.GetManifestAsync();
        manifest.Artifact.RequireIntegrity(); Assert.Equal(ArtifactType.CapabilityManifest, manifest.Artifact.Type);
        var before = await client.ReadResourceAsync("north"); Assert.Equal("20", before.Resource.State.String("available"));
        var workflow = await h.AuthorizeAsync(); Assert.Equal(WorkflowState.AUTHORIZED, workflow.State);
        var completed = await client.ExecuteNextAsync(workflow.WorkflowId); Assert.Equal(WorkflowState.COMPLETED, completed.State);
        var after = await client.ReadResourceAsync("north"); Assert.Equal("18", after.Resource.State.String("available")); Assert.Equal("2", after.Resource.State.String("reserved"));
        var operation = await client.LookupOperationAsync(workflow.WorkflowId, "reserve"); Assert.True(operation.Found);
        operation.Result!.Receipt.Artifact.RequireIntegrity(); operation.Result.PostconditionEvidence.Artifact.RequireIntegrity();
        Assert.Equal(completed.Steps[0].InvocationRef, operation.Result.Receipt.Artifact.Body.Ref("invocationRef"));
        Assert.Equal(completed.Steps[0].ReceiptRef, operation.Result.Receipt.Artifact.Reference);
        var again = await client.ExecuteNextAsync(workflow.WorkflowId); Assert.Equal(completed.Steps[0].InvocationRef, again.Steps[0].InvocationRef);
        Assert.Equal(1, h.Scenario.Runtime.Store.OperationCount); Assert.Equal(1, h.Scenario.Runtime.Store.AuthorityUseCount);
        var reopened = new SqliteRuntimeStore(h.Scenario.Runtime.Store.DatabasePath);
        Assert.Equal(completed.Steps[0].InvocationRef, reopened.GetWorkflow(workflow.WorkflowId).Steps[0].InvocationRef);
        Assert.Equal(1, reopened.OperationCount);
    }
    [Theory]
    [InlineData("GET", "/manifest")]
    [InlineData("GET", "/resources/north")]
    [InlineData("GET", "/workflows/00000000-0000-0000-0000-000000000001")]
    [InlineData("GET", "/workflows/00000000-0000-0000-0000-000000000001/steps/reserve/operation")]
    [InlineData("POST", "/plans/prepare")]
    [InlineData("POST", "/plans")]
    [InlineData("POST", "/workflows/00000000-0000-0000-0000-000000000001/approve")]
    [InlineData("POST", "/workflows/00000000-0000-0000-0000-000000000001/authorize")]
    [InlineData("POST", "/workflows/00000000-0000-0000-0000-000000000001/execute")]
    [InlineData("POST", "/workflows/00000000-0000-0000-0000-000000000001/reconcile")]
    public async Task Every_supported_endpoint_denies_unauthenticated_requests(string method, string path)
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http();
        http.DefaultRequestHeaders.Add("X-Identity", h.Scenario.Executor.Identity); http.DefaultRequestHeaders.Add("X-Role", "Approver");
        using var request = new HttpRequestMessage(new HttpMethod(method), HttpBinding.Prefix + path) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        await AssertCode(await http.SendAsync(request), HttpStatusCode.Unauthorized, "AUTH_DENIED");
        Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount);
    }
    [Theory]
    [InlineData("wrong-tenant", "/manifest")]
    [InlineData("wrong-purpose", "/manifest")]
    [InlineData("wrong-role", "/manifest")]
    [InlineData("wrong-tenant", "/resources/north")]
    [InlineData("wrong-purpose", "/resources/north")]
    [InlineData("wrong-role", "/resources/north")]
    [InlineData("wrong-resource", "/resources/north")]
    [InlineData("wrong-audience", "/resources/north")]
    [InlineData("wrong-capability", "/resources/north")]
    public async Task Reads_and_metadata_intersect_server_tenant_purpose_role_and_source_scope(string identity, string path)
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http(identity);
        await AssertCode(await http.GetAsync(HttpBinding.Prefix + path), HttpStatusCode.Forbidden, "AUTH_DENIED");
    }
    [Theory]
    [InlineData("wrong-tenant")]
    [InlineData("wrong-purpose")]
    [InlineData("wrong-role")]
    [InlineData("wrong-resource")]
    [InlineData("wrong-audience")]
    [InlineData("wrong-capability")]
    [InlineData("other-subject")]
    public async Task Workflow_reads_and_effects_do_not_allow_cross_context_identity(string identity)
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var workflow = await h.StoreAsync(); using var http = h.Http(identity);
        await AssertCode(await http.GetAsync(HttpBinding.Prefix + "/workflows/" + workflow.WorkflowId), HttpStatusCode.Forbidden, "AUTH_DENIED");
        await AssertCode(await Raw(http, "/workflows/" + workflow.WorkflowId + "/execute", JsonSerializer.Serialize(new ActionRequest(HttpBinding.Version), Json)), HttpStatusCode.Forbidden, "AUTH_DENIED");
        Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount);
    }
    [Fact]
    public async Task Reader_can_read_one_configured_resource_but_cannot_plan_or_review_or_approve()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http(h.Scenario.Reader.Identity);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(HttpBinding.Prefix + "/resources/north")).StatusCode);
        var workflow = await h.StoreAsync();
        await AssertCode(await http.GetAsync(HttpBinding.Prefix + "/workflows/" + workflow.WorkflowId), HttpStatusCode.Forbidden, "AUTH_DENIED");
        await AssertCode(await Raw(http, "/plans/prepare", "{}"), HttpStatusCode.Forbidden, "AUTH_DENIED");
        await AssertCode(await Raw(http, "/workflows/" + workflow.WorkflowId + "/approve", "{}"), HttpStatusCode.Forbidden, "AUTH_DENIED");
    }
    [Fact]
    public async Task Independent_reviewer_sees_exact_payload_and_review_digest_is_required()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var workflow = await h.StoreAsync();
        var reviewer = h.Client(h.Scenario.Approver.Identity); var review = await reviewer.GetWorkflowAsync(workflow.WorkflowId);
        Assert.Equal("2", review.Payloads["reserve"].String("quantity")); Assert.Equal(workflow.PlanRef, review.Plan.Artifact.Reference);
        var error = await Assert.ThrowsAsync<ApsDdaHttpException>(() => reviewer.ApproveAsync(workflow.WorkflowId, new(HttpBinding.Version, "sha256:" + new string('0', 64), h.Clock.Now.AddMinutes(4), ["reserve"])));
        Assert.Equal("INVALID_ARTIFACT", error.Code); Assert.Equal(0, h.Scenario.Runtime.Store.AuthorityUseCount);
        using var self = h.Http("same-subject");
        await AssertCode(await Raw(self, "/workflows/" + workflow.WorkflowId + "/approve", JsonSerializer.Serialize(new ApprovalRequest(HttpBinding.Version, review.ReviewDigest, h.Clock.Now.AddMinutes(4), ["reserve"]), Json)), HttpStatusCode.Forbidden, "AUTH_DENIED");
        using var executor = h.Http(h.Scenario.Executor.Identity);
        await AssertCode(await Raw(executor, "/workflows/" + workflow.WorkflowId + "/approve", "{}"), HttpStatusCode.Forbidden, "AUTH_DENIED");
    }
    [Fact]
    public async Task Request_cannot_choose_approver_role_identity_trust_or_execution_context()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http(h.Scenario.Executor.Identity);
        var body = "{\"binding\":\"" + HttpBinding.Version + "\",\"resourceAlias\":\"north\",\"quantity\":\"2\",\"expectedVersion\":\"1\",\"context\":{\"subjectId\":\"admin\"},\"roles\":[\"Approver\"],\"trust\":{}}";
        await AssertCode(await Raw(http, "/plans/prepare", body), HttpStatusCode.BadRequest, "INVALID_ARTIFACT");
        var workflow = await h.StoreAsync(); using var reviewer = h.Http(h.Scenario.Approver.Identity);
        await AssertCode(await Raw(reviewer, "/workflows/" + workflow.WorkflowId + "/approve", "{\"binding\":\"" + HttpBinding.Version + "\",\"approverId\":\"admin\"}"), HttpStatusCode.BadRequest, "INVALID_ARTIFACT");
    }
    [Theory]
    [InlineData("{\"binding\":\"aps-dda.experimental/http/1\",\"binding\":\"aps-dda.experimental/http/1\",\"resourceAlias\":\"north\",\"quantity\":\"2\",\"expectedVersion\":\"1\"}")]
    [InlineData("{\"binding\":\"aps-dda.experimental/http/1\",\"resourceAlias\":\"north\",\"quantity\":\"2\",\"quanti\\u0074y\":\"3\",\"expectedVersion\":\"1\"}")]
    [InlineData("{\"binding\":\"aps-dda.experimental/http/1\",\"quantity\":9007199254740992}")]
    [InlineData("{\"binding\":\"aps-dda.experimental/http/1\",\"quantity\":NaN}")]
    [InlineData("{\"binding\":\"aps-dda.experimental/http/1\",\"quantity\":\"\\ud800\"}")]
    [InlineData("{\"binding\":\"aps-dda.experimental/http/1\"}")]
    [InlineData("{\"binding\":\"aps-dda.experimental/http/1\",\"resourceAlias\":\"north\",\"quantity\":\"2\",\"expectedVersion\":\"1\",}")]
    public async Task Critical_input_rejects_duplicate_decoded_names_unsafe_numbers_bad_unicode_and_missing_fields(string body)
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http(h.Scenario.Executor.Identity);
        await AssertCode(await Raw(http, "/plans/prepare", body), HttpStatusCode.BadRequest, "INVALID_ARTIFACT");
        Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount);
    }
    [Theory]
    [InlineData("payload")]
    [InlineData("envelope")]
    [InlineData("signature")]
    public async Task Complete_signed_plan_envelope_is_duplicate_checked_before_normal_deserialization(string location)
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var client = h.Client();
        var plan = await client.PreparePlanAsync(new(HttpBinding.Version, "north", "2", "1"));
        var serialized = JsonSerializer.Serialize(plan, Json);
        var raw = location switch
        {
            "payload" => serialized.Replace("\"quantity\":\"2\"", "\"quantity\":\"2\",\"quantity\":\"3\"", StringComparison.Ordinal),
            "envelope" => serialized.Insert(1, "\"binding\":\"" + HttpBinding.Version + "\","),
            _ => serialized.Replace("\"algorithm\":", "\"keyId\":\"https://attacker.example.invalid/key\",\"algorithm\":", StringComparison.Ordinal)
        };
        using var http = h.Http(h.Scenario.Executor.Identity);
        await AssertCode(await Raw(http, "/plans", raw), HttpStatusCode.BadRequest, "INVALID_ARTIFACT");
    }
    [Fact]
    public async Task Payload_tampering_and_plan_context_mismatch_are_refused_without_effect()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var client = h.Client();
        var plan = await client.PreparePlanAsync(new(HttpBinding.Version, "north", "2", "1")); plan.Payloads["reserve"]["quantity"] = "3";
        Assert.Equal("INVALID_ARTIFACT", (await Assert.ThrowsAsync<ApsDdaHttpException>(() => client.StorePlanAsync(plan))).Code);
        plan = await client.PreparePlanAsync(new(HttpBinding.Version, "north", "2", "1"));
        var body = plan.Plan.Artifact.Body; body.Object("context")["purpose"] = "another-purpose";
        var mismatched = h.Scenario.Runtime.OrchestratorSigner.Issue(ArtifactType.ExecutionPlan, body, h.Clock.Now);
        Assert.Equal("AUTH_DENIED", (await Assert.ThrowsAsync<ApsDdaHttpException>(() => client.StorePlanAsync(plan with { Plan = mismatched }))).Code);
        Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount);
    }
    [Fact]
    public async Task Lost_reply_after_real_commit_queries_original_operation_and_reconciles_without_resend()
    {
        await using var h = new HttpHarness(loseReply: true); await h.StartAsync(); var workflow = await h.AuthorizeAsync();
        var client = h.Client(); var unknown = await client.ExecuteNextAsync(workflow.WorkflowId);
        Assert.Equal(WorkflowState.UNKNOWN_OUTCOME, unknown.State); Assert.Equal("OPERATION_UNKNOWN", unknown.SafeError);
        Assert.Equal(1, h.Scenario.Runtime.Store.OperationCount); Assert.Equal(1, h.LostReply!.InvokeCount);
        var lookup = await client.LookupOperationAsync(workflow.WorkflowId, "reserve"); Assert.True(lookup.Found);
        Assert.Equal(unknown.Steps[0].InvocationRef, lookup.Result!.Receipt.Artifact.Body.Ref("invocationRef"));
        var complete = await client.ReconcileAsync(workflow.WorkflowId); Assert.Equal(WorkflowState.COMPLETED, complete.State);
        var duplicate = await client.ExecuteNextAsync(workflow.WorkflowId);
        Assert.Equal(complete.Steps[0].ReceiptRef, duplicate.Steps[0].ReceiptRef);
        Assert.Equal(1, h.LostReply.InvokeCount); Assert.Equal(1, h.Scenario.Runtime.Store.OperationCount);
        Assert.Equal("2", (await client.ReadResourceAsync("north")).Resource.State.String("reserved"));
    }
    [Fact]
    public async Task Revoked_identity_is_rechecked_at_authentication()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); h.Scenario.Runtime.Principals.Revoke(h.Scenario.Executor.Identity);
        using var http = h.Http(h.Scenario.Executor.Identity);
        await AssertCode(await http.GetAsync(HttpBinding.Prefix + "/manifest"), HttpStatusCode.Unauthorized, "AUTH_DENIED");
    }
    [Fact]
    public async Task No_approval_means_no_effect_and_operation_lookup_is_bounded_by_original_workflow()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var workflow = await h.StoreAsync();
        var error = await Assert.ThrowsAsync<ApsDdaHttpException>(() => h.Client().ExecuteNextAsync(workflow.WorkflowId)); Assert.Equal("APPROVAL_REQUIRED", error.Code);
        Assert.False((await h.Client().LookupOperationAsync(workflow.WorkflowId, "reserve")).Found);
        Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount);
    }
    [Fact]
    public async Task Expired_authenticated_authority_refuses_new_source_effect()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var workflow = await h.AuthorizeAsync(); h.Clock.Now = h.Clock.Now.AddMinutes(4).AddSeconds(1);
        var rejected = await h.Client().ExecuteNextAsync(workflow.WorkflowId);
        Assert.Equal(WorkflowState.REJECTED, rejected.State); Assert.Equal("AUTHORITY_EXPIRED", rejected.SafeError);
        Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount);
    }
    [Fact]
    public async Task Two_concurrent_plans_for_same_version_do_not_override_source_version_guard()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); var a = await h.AuthorizeAsync(); var b = await h.AuthorizeAsync();
        Assert.Equal(WorkflowState.COMPLETED, (await h.Client().ExecuteNextAsync(a.WorkflowId)).State);
        var rejected = await h.Client().ExecuteNextAsync(b.WorkflowId); Assert.Equal(WorkflowState.REJECTED, rejected.State); Assert.Equal("STALE_PRECONDITION", rejected.SafeError);
        Assert.Equal(1, h.Scenario.Runtime.Store.OperationCount); Assert.Equal("18", (await h.Client().ReadResourceAsync("north")).Resource.State.String("available"));
    }
    [Theory]
    [InlineData("/reset")]
    [InlineData("/fault")]
    [InlineData("/admin")]
    [InlineData("/sources/write")]
    [InlineData("/policy")]
    [InlineData("/revocations")]
    public async Task No_reset_fault_admin_arbitrary_source_write_or_trust_provisioning_route_exists(string path)
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http(h.Scenario.Executor.Identity);
        Assert.Equal(HttpStatusCode.NotFound, (await Raw(http, path, "{}")).StatusCode);
        Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount);
    }
}
