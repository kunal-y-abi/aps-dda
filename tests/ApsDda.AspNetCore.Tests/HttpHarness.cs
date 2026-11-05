using System.Net.Http.Headers;
using System.Security.Cryptography;
using ApsDda.Api;
using ApsDda.AspNetCore;
using ApsDda.Core;
using ApsDda.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApsDda.AspNetCore.Tests;

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.Parse("2026-11-03T14:00:00Z");
    public override DateTimeOffset GetUtcNow() => Now;
}
internal sealed class LostReplyAdapter(ISourceAdapter inner) : ISourceAdapter
{
    public SourceRegistration Registration => inner.Registration;
    public int InvokeCount { get; private set; }
    public bool HideLookupResults { get; set; }
    public long MeasureQuantity(System.Text.Json.Nodes.JsonObject normalizedPayload) => inner.MeasureQuantity(normalizedPayload);
    public SourceResult Invoke(SourceDispatch dispatch)
    {
        InvokeCount++; _ = inner.Invoke(dispatch);
        throw new IOException("Private transport diagnostic must not be serialized.");
    }
    public SourceResult? Lookup(OperationIdentity identity, AuthenticatedPrincipal caller) => HideLookupResults ? null : inner.Lookup(identity, caller);
    public void VerifyPostcondition(SourceResult result, Artifact invocation) => inner.VerifyPostcondition(result, invocation);
}
internal sealed class HttpHarness : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "aps-dda-http-tests-" + Guid.NewGuid().ToString("N"));
    public TestClock Clock { get; } = new();
    public SyntheticApiScenario Scenario { get; }
    public ApsDdaHttpBinding Binding { get; }
    public Dictionary<string, string> Tokens { get; } = new(StringComparer.Ordinal);
    public WebApplication App { get; }
    public LostReplyAdapter? LostReply { get; }
    public HttpHarness(string environment = "Development", bool allowHttp = true, bool loseReply = false, int maximumRequestBytes = 262144, int maximumPlanSteps = 32)
    {
        LostReplyAdapter? decorated = null;
        Scenario = new SyntheticApiScenario(Path.Combine(_directory, "runtime.sqlite"), Clock, loseReply ? source => decorated = new LostReplyAdapter(source) : null);
        LostReply = decorated;
        var principalDirectory = Scenario.Runtime.Principals;
        var extra = new[] { "wrong-tenant", "wrong-purpose", "wrong-role", "wrong-resource", "wrong-audience", "wrong-capability", "same-subject", "other-subject" };
        foreach (var id in extra)
            principalDirectory.Register(new(id, id == "wrong-tenant" ? "another-tenant" : SyntheticApiScenario.Tenant,
                id == "same-subject" ? Scenario.Executor.SubjectId : id + "-subject", "workload-" + id, "executor-" + id,
                id == "same-subject" ? ["Approver"] : id == "wrong-role" ? ["PolicyGranter"] : ["Executor"],
                id == "wrong-purpose" ? ["another-purpose"] : [SyntheticApiScenario.Purpose],
                id == "wrong-audience" ? ["https://another.example.invalid"] : [SyntheticApiScenario.Audience],
                id == "wrong-resource" ? ["https://another.example.invalid/resource"] : [SyntheticApiScenario.Target],
                id == "wrong-capability" ? [] : [Scenario.Capability.Artifact.Header.ContentDigest]));
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in extra.Concat([Scenario.Executor.Identity, Scenario.Approver.Identity, Scenario.Reader.Identity]))
        { var token = Jcs.Base64Url(RandomNumberGenerator.GetBytes(32)); Tokens[id] = token; map[token] = id; }
        Binding = new ApsDdaHttpBinding(Scenario.Runtime, SyntheticApiScenario.Tenant, SyntheticApiScenario.Purpose, Scenario.Manifest,
            [Scenario.Resource], map, Scenario, allowHttp, maximumRequestBytes, maximumPlanSteps);
        App = ApsDdaApplication.Create([], Binding, builder =>
        {
            builder.Environment.EnvironmentName = environment;
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
        });
    }
    public Task StartAsync() => App.StartAsync();
    public HttpClient Http(string? identity = null, string scheme = "http", string host = "localhost")
    {
        var client = App.GetTestClient(); client.BaseAddress = new Uri($"{scheme}://{host}");
        if (identity is not null) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Tokens[identity]);
        return client;
    }
    public ApsDdaClient Client(string? identity = null) => new(Http(), Tokens[identity ?? Scenario.Executor.Identity], true);
    public async Task<WorkflowSnapshot> StoreAsync() => await Client().StorePlanAsync(await Client().PreparePlanAsync(new(HttpBinding.Version, "north", "2", "1")));
    public async Task<WorkflowSnapshot> AuthorizeAsync()
    {
        var workflow = await StoreAsync();
        var reviewer = Client(Scenario.Approver.Identity); var review = await reviewer.GetWorkflowAsync(workflow.WorkflowId);
        var approval = await reviewer.ApproveAsync(workflow.WorkflowId, new(HttpBinding.Version, review.ReviewDigest, Clock.Now.AddMinutes(4), ["reserve"]));
        return await Client().AuthorizeAsync(workflow.WorkflowId, new(HttpBinding.Version, approval.Artifact.Reference));
    }
    public async ValueTask DisposeAsync()
    {
        await App.DisposeAsync(); Scenario.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
