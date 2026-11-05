using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ApsDda.Core;
using ApsDda.Runtime;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApsDda.AspNetCore;

public static class ApsDdaHttpExtensions
{
    public static IServiceCollection AddApsDdaHttp(this IServiceCollection services, ApsDdaHttpBinding binding)
    {
        services.AddSingleton(binding);
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, ConfiguredTokenHandler>(HttpBinding.AuthenticationScheme, _ => { });
        services.AddAuthorization(options => options.AddPolicy(HttpBinding.AuthenticationScheme, policy =>
            policy.AddAuthenticationSchemes(HttpBinding.AuthenticationScheme).RequireAuthenticatedUser()));
        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNameCaseInsensitive = false;
            options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(allowIntegerValues: false));
        });
        return services;
    }

    /// <summary>Call before MapApsDda. Does not accept forwarded host/proto/identity claims or enable CORS.</summary>
    public static WebApplication UseApsDdaHttp(this WebApplication app)
    {
        var binding = app.Services.GetRequiredService<ApsDdaHttpBinding>();
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments(HttpBinding.Prefix)) { await next(context); return; }
            try
            {
                if (!context.Request.IsHttps && !(app.Environment.IsDevelopment() && binding.AllowDevelopmentLoopbackHttp && IsLocal(context)))
                    throw new ApsDdaException("HTTPS_REQUIRED", "HTTPS is required.");
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                await next(context);
            }
            catch (ApsDdaException e)
            {
                var code = HttpSafety.Code(e.Code);
                var status = code switch
                {
                    "AUTH_DENIED" => 403, "NOT_FOUND" => 404, "REQUEST_TOO_LARGE" => 413,
                    "HTTPS_REQUIRED" => 400, "UNSUPPORTED_MEDIA_TYPE" => 415,
                    "AUTHORITY_CONSUMED" or "CONFLICT" => 409, _ => 400
                };
                await WriteError(context, status, code);
            }
            catch (Exception e) when (e is JsonException or FormatException or ArgumentException or InvalidOperationException)
            { await WriteError(context, 400, "INVALID_ARTIFACT"); }
            catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
            { await WriteError(context, 500, "INTERNAL_ERROR"); }
        });
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
    private static bool IsLocal(HttpContext context)
    {
        var host = context.Request.Host.Host;
        var loopbackHost = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);
        // TestServer has no remote IP. Actual Kestrel requests must come from loopback as well.
        return loopbackHost && (context.Connection.RemoteIpAddress is null || IPAddress.IsLoopback(context.Connection.RemoteIpAddress));
    }
    internal static async Task WriteError(HttpContext context, int status, string code)
    {
        if (context.Response.HasStarted) { context.Abort(); return; }
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new HttpError(HttpBinding.Version, code), cancellationToken: context.RequestAborted);
    }
    public static RouteGroupBuilder MapApsDda(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(HttpBinding.Prefix).RequireAuthorization(HttpBinding.AuthenticationScheme);
        group.MapGet("/manifest", (HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Reader", "Executor", "Approver");
            b.RequireManifestAccess(p);
            foreach (var reference in b.Manifest.Artifact.Body.Refs("capabilityRefs"))
                if (!p.CapabilityDigests.Contains(reference.ContentDigest)) throw ApsDdaHttpBinding.Denied();
            if (b.Runtime.Clock.GetUtcNow() >= b.Manifest.Artifact.Body.Time("expiresAt")) throw new ApsDdaException("AUTHORITY_EXPIRED", "Manifest expired.");
            b.Runtime.Trust.Validate(b.Manifest, new(ArtifactType.CapabilityManifest, "APS-Action-Executor", b.Manifest.Artifact.Header.Issuer,
                Binding: "https-json", RequirePinnedArtifact: true));
            return Results.Json(b.Manifest, HttpBinding.CreateJsonOptions());
        });
        group.MapGet("/resources/{alias}", (string alias, HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Reader", "Executor", "Approver");
            var resource = b.Resource(alias); b.RequireResource(p, resource.CapabilityRef, resource.Target);
            var source = b.Runtime.GetSource(resource.CapabilityRef).Registration;
            b.Runtime.Trust.Validate(source.Capability, new(ArtifactType.ProcessCapability, "APS-Action-Executor", source.Signer.Issuer, Audience: source.Audience, RequirePinnedArtifact: true));
            b.Runtime.Trust.Validate(source.Product, new(ArtifactType.DataProduct, "APS-Read-Publisher", source.Signer.Issuer, RequirePinnedArtifact: true));
            var state = b.Runtime.Store.GetResource(source.Signer.Issuer, resource.Target);
            if (state.TenantId != p.TenantId) throw ApsDdaHttpBinding.Denied();
            return new ResourceRead(HttpBinding.Version, state, source.Product, b.Runtime.Clock.GetUtcNow());
        });
        group.MapPost("/plans/prepare", async (HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Executor");
            var request = await Input<PreparePlanRequest>(ctx, b); RequireBinding(request.Binding);
            var resource = b.Resource(request.ResourceAlias); b.RequireResource(p, resource.CapabilityRef, resource.Target);
            if (b.PlanPreparer is null) throw new ApsDdaException("UNSUPPORTED_PROFILE", "No bounded plan factory is configured.");
            var prepared = b.PlanPreparer.Prepare(request, p); RequireBinding(prepared.Binding);
            b.RequirePlan(p, prepared.Plan.Artifact, false);
            return prepared;
        });
        group.MapPost("/plans", async (HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Executor");
            var request = await Input<StorePlanRequest>(ctx, b); RequireBinding(request.Binding);
            if (request.Plan is null || request.Payloads is null) throw new ApsDdaException("INVALID_ARTIFACT", "Plan and payloads required.");
            b.RequirePlan(p, request.Plan.Artifact, false);
            return HttpSafety.Snapshot(b.Runtime.Submit(request.Plan, request.Payloads, p));
        });
        group.MapGet("/workflows/{workflowId}", (string workflowId, HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Executor", "Approver");
            return b.Review(workflowId, p, p.Roles.Contains("Approver"));
        });
        group.MapPost("/workflows/{workflowId}/approve", async (string workflowId, HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Approver");
            var request = await Input<ApprovalRequest>(ctx, b); RequireBinding(request.Binding);
            var review = b.Review(workflowId, p, true);
            if (request.ReviewDigest != review.ReviewDigest || request.AllowedStepIds is null || request.AllowedStepIds.Length is < 1 or > 256)
                throw new ApsDdaException("INVALID_ARTIFACT", "Approval must bind exact retained review and bounded steps.");
            return b.Runtime.Approve(workflowId, p, request.ExpiresAt, request.AllowedStepIds);
        });
        group.MapPost("/workflows/{workflowId}/authorize", async (string workflowId, HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Executor");
            var request = await Input<AuthorizationRequest>(ctx, b); RequireBinding(request.Binding);
            _ = b.Review(workflowId, p, false);
            if (request.AuthorityRef is null) throw new ApsDdaException("INVALID_ARTIFACT", "Authority reference required.");
            return HttpSafety.Snapshot(b.Runtime.Authorize(workflowId, request.AuthorityRef, p));
        });
        group.MapPost("/workflows/{workflowId}/execute", async (string workflowId, HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Executor");
            RequireBinding((await Input<ActionRequest>(ctx, b)).Binding); _ = b.Review(workflowId, p, false);
            return HttpSafety.Snapshot(b.Runtime.ExecuteNext(workflowId, p));
        });
        group.MapPost("/workflows/{workflowId}/reconcile", async (string workflowId, HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Executor");
            RequireBinding((await Input<ActionRequest>(ctx, b)).Binding); _ = b.Review(workflowId, p, false);
            return HttpSafety.Snapshot(b.Runtime.Reconcile(workflowId, p));
        });
        group.MapGet("/workflows/{workflowId}/steps/{stepId}/operation", (string workflowId, string stepId, HttpContext ctx, ApsDdaHttpBinding b) =>
        {
            var p = Principal(ctx, b); b.RequireRole(p, "Executor"); var review = b.Review(workflowId, p, false);
            var step = review.Workflow.Steps.SingleOrDefault(x => x.StepId == stepId)
                ?? throw new ApsDdaException("NOT_FOUND", "Step not found.");
            if (step.InvocationRef is null) return new OperationLookup(HttpBinding.Version, false, null);
            var invocation = b.Runtime.Store.GetArtifact(step.InvocationRef).Artifact;
            var source = b.Runtime.GetSource(invocation.Body.Ref("capabilityRef")); var body = invocation.Body;
            var identity = new OperationIdentity(p.TenantId, source.Registration.Signer.Issuer,
                body.String("idempotencyDomain"), body.String("idempotencyKey"), body.String("invocationFingerprint"));
            var result = source.Lookup(identity, p);
            if (result is not null) source.VerifyPostcondition(result, invocation);
            return new OperationLookup(HttpBinding.Version, result is not null, result);
        });
        return group;
    }
    private static AuthenticatedPrincipal Principal(HttpContext context, ApsDdaHttpBinding binding) =>
        binding.Runtime.Principals.ResolveAuthenticated(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw ApsDdaHttpBinding.Denied());
    internal static void RequireBinding(string? binding)
    { if (binding != HttpBinding.Version) throw new ApsDdaException("UNSUPPORTED_PROFILE", "Explicit experimental HTTP binding required."); }
    private static async Task<T> Input<T>(HttpContext context, ApsDdaHttpBinding binding)
    {
        var type = context.Request.ContentType;
        if (type is null || !System.Net.Http.Headers.MediaTypeHeaderValue.TryParse(type, out var media) || media.MediaType != "application/json" ||
            (media.CharSet is not null && !media.CharSet.Trim('"').Equals("utf-8", StringComparison.OrdinalIgnoreCase)) || context.Request.Headers.ContentEncoding.Count != 0)
            throw new ApsDdaException("UNSUPPORTED_MEDIA_TYPE", "Use uncompressed application/json UTF-8.");
        if (context.Request.ContentLength > binding.MaximumRequestBytes) throw new ApsDdaException("REQUEST_TOO_LARGE", "Request exceeds byte bound.");
        using var bytes = new MemoryStream(); var buffer = new byte[8192];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer, context.RequestAborted);
            if (read == 0) break;
            if (bytes.Length + read > binding.MaximumRequestBytes) throw new ApsDdaException("REQUEST_TOO_LARGE", "Request exceeds byte bound.");
            bytes.Write(buffer, 0, read);
        }
        var strict = StrictJson.ParseObject(bytes.ToArray()); // Check duplicates in the ENTIRE envelope before model deserialization.
        return strict.Deserialize<T>(HttpBinding.CreateJsonOptions()) ?? throw new ApsDdaException("INVALID_ARTIFACT", "Request object required.");
    }
}

internal sealed class ConfiguredTokenHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, ApsDdaHttpBinding binding) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var values = Request.Headers.Authorization;
        if (values.Count != 1 || values[0] is not { } value || !value.StartsWith("Bearer ", StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.NoResult());
        try
        {
            var principal = binding.Authenticate(value[7..]);
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, principal.Identity)], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
        catch (ApsDdaException) { return Task.FromResult(AuthenticateResult.Fail("Authentication refused.")); }
    }
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    { Response.Headers.WWWAuthenticate = "Bearer"; return ApsDdaHttpExtensions.WriteError(Context, 401, "AUTH_DENIED"); }
    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) => ApsDdaHttpExtensions.WriteError(Context, 403, "AUTH_DENIED");
}

/// <summary>Actual WebApplication factory shared by the executable sample and TestServer tests.</summary>
public static class ApsDdaApplication
{
    public static WebApplication Create(string[] args, ApsDdaHttpBinding binding, Action<WebApplicationBuilder>? configureBuilder = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        configureBuilder?.Invoke(builder);
        builder.Services.AddApsDdaHttp(binding);
        var app = builder.Build();
        app.UseApsDdaHttp(); app.MapApsDda();
        return app;
    }
}
