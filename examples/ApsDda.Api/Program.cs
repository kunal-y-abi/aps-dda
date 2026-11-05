using System.Text.Json;
using ApsDda.Api;
using ApsDda.AspNetCore;
using ApsDda.Runtime;

static string Required(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Set {name} explicitly. No default credential is provided.");

var executorToken = Required("APS_DDA_EXECUTOR_TOKEN");
var approverToken = Required("APS_DDA_APPROVER_TOKEN");
var readerToken = Required("APS_DDA_READER_TOKEN");
if (new[] { executorToken, approverToken, readerToken }.Distinct(StringComparer.Ordinal).Count() != 3)
    throw new InvalidOperationException("Configure distinct explicit executor, approver and reader tokens.");
var development = (Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")) == "Development";
var allowHttp = development && Environment.GetEnvironmentVariable("APS_DDA_ALLOW_LOOPBACK_HTTP") == "true";
var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? (allowHttp ? "http://localhost:5127" : "https://localhost:7127");
foreach (var value in urls.Split(';'))
{
    if (!Uri.TryCreate(value, UriKind.Absolute, out var address) || !address.IsLoopback ||
        (address.Scheme != "https" && !(allowHttp && address.Scheme == "http")))
        throw new InvalidOperationException("Sample host accepts only explicit HTTPS loopback URLs, or the opted-in Development loopback HTTP exception.");
}
if (args.Contains("--exercise", StringComparer.Ordinal))
{
    if (!args.Contains("--approve-reviewed-plan", StringComparer.Ordinal))
        throw new InvalidOperationException("Synthetic replay requires --approve-reviewed-plan. It is not a human-review user interface.");
    using var executorHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = new Uri(urls.Split(';')[0]) };
    using var reviewerHttp = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = executorHttp.BaseAddress };
    var executor = new ApsDdaClient(executorHttp, executorToken, allowHttp);
    var reviewer = new ApsDdaClient(reviewerHttp, approverToken, allowHttp);
    var resource = await executor.ReadResourceAsync("north");
    var prepared = await executor.PreparePlanAsync(new PreparePlanRequest(HttpBinding.Version, "north", "2", resource.Resource.Version));
    var workflow = await executor.StorePlanAsync(prepared);
    var review = await reviewer.GetWorkflowAsync(workflow.WorkflowId);
    Console.WriteLine("Synthetic protocol replay: exact retained payloads (not a human-review UX):");
    Console.WriteLine(JsonSerializer.Serialize(review.Payloads, HttpBinding.CreateJsonOptions()));
    var expiry = prepared.Plan.Artifact.Header.IssuedAt.AddMinutes(4);
    var approval = await reviewer.ApproveAsync(workflow.WorkflowId, new ApprovalRequest(HttpBinding.Version, review.ReviewDigest, expiry, ["reserve"]));
    await executor.AuthorizeAsync(workflow.WorkflowId, new AuthorizationRequest(HttpBinding.Version, approval.Artifact.Reference));
    var completed = await executor.ExecuteNextAsync(workflow.WorkflowId);
    var original = await executor.LookupOperationAsync(workflow.WorkflowId, "reserve");
    Console.WriteLine($"workflow={completed.WorkflowId} state={completed.State} originalOperationFound={original.Found}");
    if (completed.State != WorkflowState.COMPLETED || !original.Found) Environment.ExitCode = 1;
    return;
}
var database = Environment.GetEnvironmentVariable("APS_DDA_DATABASE") ?? Path.Combine(Path.GetTempPath(), "aps-dda-http-" + Guid.NewGuid().ToString("N"), "runtime.sqlite");
using var scenario = new SyntheticApiScenario(database);
var binding = scenario.CreateBinding(new Dictionary<string, string>(StringComparer.Ordinal)
{
    [executorToken] = scenario.Executor.Identity,
    [approverToken] = scenario.Approver.Identity,
    [readerToken] = scenario.Reader.Identity
}, allowHttp);
await using var app = ApsDdaApplication.Create(args, binding, builder => builder.WebHost.UseUrls(urls));
Console.WriteLine("Experimental synthetic APS-DDA API. No production identity provider, customer action or public deployment.");
await app.RunAsync();
