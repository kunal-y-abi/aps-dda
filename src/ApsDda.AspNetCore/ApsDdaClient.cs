using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ApsDda.Core;
using ApsDda.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace ApsDda.AspNetCore;

public sealed class ApsDdaHttpException(HttpStatusCode statusCode, string code) : Exception("APS-DDA HTTP request was refused.")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Code { get; } = code;
}

/// <summary>Typed bounded client. Authentication is explicit client configuration, never part of any artifact/request model.</summary>
public sealed class ApsDdaClient
{
    private readonly HttpClient _http;
    private readonly string _token;
    private readonly JsonSerializerOptions _json = HttpBinding.CreateJsonOptions();
    public ApsDdaClient(HttpClient http, string token, bool allowDevelopmentLoopbackHttp = false)
    {
        ValidateAddress(http.BaseAddress, allowDevelopmentLoopbackHttp); ApsDdaHttpBinding.RequireToken(token);
        _http = http; _token = token;
    }
    internal static void ValidateAddress(Uri? address, bool allowLoopback)
    {
        if (address is null || !address.IsAbsoluteUri || address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0 ||
            (address.Scheme != "https" && !(allowLoopback && address.Scheme == "http" && address.IsLoopback)))
            throw new ArgumentException("Use an explicit HTTPS base address, or opt in to development-only loopback HTTP.");
    }
    public Task<SignedArtifact> GetManifestAsync(CancellationToken cancellationToken = default) => Send<SignedArtifact>(HttpMethod.Get, "/manifest", null, cancellationToken);
    public Task<ResourceRead> ReadResourceAsync(string alias, CancellationToken cancellationToken = default) => Send<ResourceRead>(HttpMethod.Get, "/resources/" + Segment(alias), null, cancellationToken);
    public Task<StorePlanRequest> PreparePlanAsync(PreparePlanRequest request, CancellationToken cancellationToken = default) => Send<StorePlanRequest>(HttpMethod.Post, "/plans/prepare", request, cancellationToken);
    public Task<WorkflowSnapshot> StorePlanAsync(StorePlanRequest request, CancellationToken cancellationToken = default) => Send<WorkflowSnapshot>(HttpMethod.Post, "/plans", request, cancellationToken);
    public Task<WorkflowReview> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken = default) => Send<WorkflowReview>(HttpMethod.Get, "/workflows/" + Segment(workflowId), null, cancellationToken);
    public Task<SignedArtifact> ApproveAsync(string workflowId, ApprovalRequest request, CancellationToken cancellationToken = default) => Send<SignedArtifact>(HttpMethod.Post, "/workflows/" + Segment(workflowId) + "/approve", request, cancellationToken);
    public Task<WorkflowSnapshot> AuthorizeAsync(string workflowId, AuthorizationRequest request, CancellationToken cancellationToken = default) => Send<WorkflowSnapshot>(HttpMethod.Post, "/workflows/" + Segment(workflowId) + "/authorize", request, cancellationToken);
    public Task<WorkflowSnapshot> ExecuteNextAsync(string workflowId, CancellationToken cancellationToken = default) => Send<WorkflowSnapshot>(HttpMethod.Post, "/workflows/" + Segment(workflowId) + "/execute", new ActionRequest(HttpBinding.Version), cancellationToken);
    public Task<WorkflowSnapshot> ReconcileAsync(string workflowId, CancellationToken cancellationToken = default) => Send<WorkflowSnapshot>(HttpMethod.Post, "/workflows/" + Segment(workflowId) + "/reconcile", new ActionRequest(HttpBinding.Version), cancellationToken);
    public Task<OperationLookup> LookupOperationAsync(string workflowId, string stepId, CancellationToken cancellationToken = default) => Send<OperationLookup>(HttpMethod.Get, "/workflows/" + Segment(workflowId) + "/steps/" + Segment(stepId) + "/operation", null, cancellationToken);
    private static string Segment(string text) => Uri.EscapeDataString(text);
    private async Task<T> Send<T>(HttpMethod method, string path, object? input, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, HttpBinding.Prefix + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (input is not null)
        {
            var serialized = JsonSerializer.Serialize(input, _json);
            _ = StrictJson.ParseObject(serialized);
            if (Encoding.UTF8.GetByteCount(serialized) > 262_144) throw new ApsDdaException("REQUEST_TOO_LARGE", "Typed client request bound exceeded.");
            request.Content = new StringContent(serialized, Encoding.UTF8, "application/json");
        }
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var bytes = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            if (bytes.Length + count > StrictJson.MaximumBytes) throw new ApsDdaException("REQUEST_TOO_LARGE", "HTTP response bound exceeded.");
            bytes.Write(buffer, 0, count);
        }
        var parsed = StrictJson.ParseObject(bytes.ToArray());
        if (!response.IsSuccessStatusCode)
        {
            var error = parsed.Deserialize<HttpError>(_json) ?? throw new ApsDdaException("INVALID_ARTIFACT", "Invalid HTTP error.");
            throw new ApsDdaHttpException(response.StatusCode, HttpSafety.Code(error.Code));
        }
        return parsed.Deserialize<T>(_json) ?? throw new ApsDdaException("INVALID_ARTIFACT", "Invalid HTTP response.");
    }
}

public static class ApsDdaClientExtensions
{
    public static IHttpClientBuilder AddApsDdaClient(this IServiceCollection services, Uri baseAddress, string token,
        bool allowDevelopmentLoopbackHttp = false)
    {
        ApsDdaClient.ValidateAddress(baseAddress, allowDevelopmentLoopbackHttp); ApsDdaHttpBinding.RequireToken(token);
        return services.AddHttpClient("ApsDdaConfiguredClient", client => { client.BaseAddress = baseAddress; client.Timeout = TimeSpan.FromSeconds(30); })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddTypedClient(http => new ApsDdaClient(http, token, allowDevelopmentLoopbackHttp));
    }
}
