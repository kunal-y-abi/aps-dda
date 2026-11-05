using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ApsDda.Api;
using ApsDda.AspNetCore;
using ApsDda.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ApsDda.AspNetCore.Tests;

public sealed class BoundaryTests
{
    [Theory]
    [InlineData("Production", true, "http", "localhost", 400)]
    [InlineData("Production", true, "https", "localhost", 200)]
    [InlineData("Development", false, "http", "localhost", 400)]
    [InlineData("Development", true, "http", "localhost", 200)]
    [InlineData("Development", true, "http", "127.0.0.1", 200)]
    [InlineData("Development", true, "http", "remote.example.invalid", 400)]
    public async Task Production_requires_https_and_development_exception_is_explicit_loopback_only(string environment, bool allow, string scheme, string host, int status)
    {
        await using var h = new HttpHarness(environment, allow); await h.StartAsync();
        using var http = h.Http(h.Scenario.Executor.Identity, scheme, host);
        http.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        http.DefaultRequestHeaders.Add("X-Forwarded-Host", "localhost");
        var response = await http.GetAsync(HttpBinding.Prefix + "/manifest"); Assert.Equal(status, (int)response.StatusCode);
        if (status == 200) Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        else Assert.Contains("HTTPS_REQUIRED", await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("application/json; charset=utf-16", "")]
    [InlineData("text/plain", "")]
    [InlineData("application/json", "gzip")]
    public async Task Only_uncompressed_utf8_json_is_admitted(string contentType, string encoding)
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http(h.Scenario.Executor.Identity);
        using var content = new ByteArrayContent(Encoding.UTF8.GetBytes("{}"));
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        if (encoding.Length != 0) content.Headers.ContentEncoding.Add(encoding);
        var response = await http.PostAsync(HttpBinding.Prefix + "/plans/prepare", content);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
    [Fact]
    public async Task Malformed_utf8_is_refused_before_artifact_or_request_materialization()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http(h.Scenario.Executor.Identity);
        using var content = new ByteArrayContent([0x7b, 0x22, 0xc3, 0x28, 0x22, 0x3a, 0x31, 0x7d]);
        content.Headers.ContentType = new("application/json");
        var response = await http.PostAsync(HttpBinding.Prefix + "/plans/prepare", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); Assert.Contains("INVALID_ARTIFACT", await response.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Body_limit_applies_to_declared_and_streamed_requests(bool chunked)
    {
        await using var h = new HttpHarness(maximumRequestBytes: 1024); await h.StartAsync(); using var http = h.Http(h.Scenario.Executor.Identity);
        var body = Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', 2048) + "\"}");
        using HttpContent content = chunked ? new UnboundedLengthContent(body) : new ByteArrayContent(body);
        content.Headers.ContentType = new("application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await http.PostAsync(HttpBinding.Prefix + "/plans/prepare", content)).StatusCode);
    }
    [Fact]
    public async Task Wrong_bearer_token_does_not_select_identity_using_extra_headers()
    {
        await using var h = new HttpHarness(); await h.StartAsync(); using var http = h.Http();
        http.DefaultRequestHeaders.Authorization = new("Bearer", Jcs.Base64Url(RandomNumberGenerator.GetBytes(32)));
        http.DefaultRequestHeaders.Add("X-Tenant", SyntheticApiScenario.Tenant); http.DefaultRequestHeaders.Add("X-Identity", h.Scenario.Executor.Identity);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(HttpBinding.Prefix + "/manifest")).StatusCode);
    }
    [Fact]
    public async Task Token_configuration_has_no_default_and_refuses_short_or_duplicate_values()
    {
        await using var h = new HttpHarness();
        Assert.Throws<ArgumentException>(() => h.Scenario.CreateBinding([]));
        Assert.Throws<ArgumentException>(() => h.Scenario.CreateBinding([KeyValuePair.Create("admin", h.Scenario.Executor.Identity)]));
        var token = Jcs.Base64Url(RandomNumberGenerator.GetBytes(32));
        Assert.Throws<ArgumentException>(() => h.Scenario.CreateBinding([KeyValuePair.Create(token, h.Scenario.Executor.Identity), KeyValuePair.Create(token, h.Scenario.Approver.Identity)]));
    }
    [Fact]
    public async Task Sample_refuses_existing_database_instead_of_resetting_or_replacing_keys()
    {
        await using var h = new HttpHarness();
        Assert.Throws<InvalidOperationException>(() => new SyntheticApiScenario(h.Scenario.Runtime.Store.DatabasePath));
    }
    [Theory]
    [InlineData("http://remote.example.invalid", true)]
    [InlineData("http://localhost", false)]
    [InlineData("https://user:secret@localhost", false)]
    public void Typed_client_refuses_insecure_remote_urls_and_url_credentials(string url, bool allow)
    {
        using var http = new HttpClient { BaseAddress = new Uri(url) };
        Assert.Throws<ArgumentException>(() => new ApsDdaClient(http, Jcs.Base64Url(RandomNumberGenerator.GetBytes(32)), allow));
    }
    [Fact]
    public void Typed_client_is_registered_through_actual_HttpClientFactory()
    {
        var services = new ServiceCollection();
        services.AddApsDdaClient(new Uri("https://localhost:7127"), Jcs.Base64Url(RandomNumberGenerator.GetBytes(32)));
        using var provider = services.BuildServiceProvider(); Assert.NotNull(provider.GetRequiredService<ApsDdaClient>());
    }
    [Theory]
    [InlineData("0", "1", "INVALID_ARTIFACT")]
    [InlineData("02", "1", "INVALID_ARTIFACT")]
    [InlineData("51", "1", "QUOTA_EXCEEDED")]
    [InlineData("2", "2", "STALE_PRECONDITION")]
    public async Task Factory_admits_only_canonical_bounded_quantity_and_actual_expected_version(string quantity, string version, string code)
    {
        await using var h = new HttpHarness(); await h.StartAsync();
        var error = await Assert.ThrowsAsync<ApsDdaHttpException>(() => h.Client().PreparePlanAsync(new(HttpBinding.Version, "north", quantity, version)));
        Assert.Equal(code, error.Code); Assert.Equal(0, h.Scenario.Runtime.Store.OperationCount);
    }
    private sealed class UnboundedLengthContent(byte[] data) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(data).AsTask();
    }
}
