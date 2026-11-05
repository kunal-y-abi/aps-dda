using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Runtime;

namespace ApsDda.AspNetCore;

/// <summary>Provision only from trusted host configuration. Token values are immediately hashed, not placed in artifacts.</summary>
public sealed class ApsDdaHttpBinding
{
    private readonly (byte[] Hash, string Identity)[] _tokens;
    private readonly FrozenDictionary<string, HttpResourceRegistration> _resources;
    public ApsDdaHttpBinding(WorkflowRuntime runtime, string tenantId, string purpose, SignedArtifact manifest,
        IEnumerable<HttpResourceRegistration> resources, IEnumerable<KeyValuePair<string, string>> tokenToIdentity,
        IBoundedPlanPreparer? planPreparer = null, bool allowDevelopmentLoopbackHttp = false,
        int maximumRequestBytes = 262_144, int maximumPlanSteps = 32)
    {
        if (maximumRequestBytes is < 1024 or > StrictJson.MaximumBytes || maximumPlanSteps is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(maximumRequestBytes));
        Runtime = runtime; TenantId = tenantId; Purpose = purpose; Manifest = manifest;
        PlanPreparer = planPreparer; AllowDevelopmentLoopbackHttp = allowDevelopmentLoopbackHttp;
        MaximumRequestBytes = maximumRequestBytes; MaximumPlanSteps = maximumPlanSteps;
        if (runtime.Trust.TenantId != tenantId || string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Binding must use the trusted runtime tenant and a configured purpose.");
        runtime.Trust.Validate(manifest, new(ArtifactType.CapabilityManifest, "APS-Action-Executor",
            manifest.Artifact.Header.Issuer, Binding: "https-json", RequirePinnedArtifact: true));
        var entries = resources.ToArray();
        if (entries.Length is < 1 or > 100 || entries.Any(x => !SafeAlias(x.Alias)))
            throw new ArgumentException("Configure between one and one hundred exact safe resource aliases.");
        _resources = entries.ToFrozenDictionary(x => x.Alias, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            var source = runtime.GetSource(entry.CapabilityRef).Registration;
            if (!source.Resources.Contains(entry.Target) || !source.Purposes.Contains(purpose)) throw Denied();
        }
        _tokens = tokenToIdentity.Select(x =>
        {
            RequireToken(x.Key);
            _ = runtime.Principals.ResolveAuthenticated(x.Value);
            return (SHA256.HashData(Encoding.ASCII.GetBytes(x.Key)), x.Value);
        }).ToArray();
        if (_tokens.Length is < 1 or > 100 || _tokens.Select(x => Convert.ToHexString(x.Hash)).Distinct().Count() != _tokens.Length)
            throw new ArgumentException("Configure distinct explicit tokens; no default credential is supplied.");
    }
    public WorkflowRuntime Runtime { get; }
    public string TenantId { get; }
    public string Purpose { get; }
    public SignedArtifact Manifest { get; }
    public IBoundedPlanPreparer? PlanPreparer { get; }
    public bool AllowDevelopmentLoopbackHttp { get; }
    public int MaximumRequestBytes { get; }
    public int MaximumPlanSteps { get; }
    internal static bool SafeAlias(string value) => value.Length is > 0 and <= 64 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    internal static void RequireToken(string token)
    {
        if (token.Length is < 43 or > 256 || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Use an explicit base64url token generated from at least 32 random bytes.");
    }
    internal AuthenticatedPrincipal Authenticate(string token)
    {
        if (token.Length is < 43 or > 256 || !token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) throw Denied();
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(token));
        string? identity = null;
        foreach (var item in _tokens)
            if (CryptographicOperations.FixedTimeEquals(hash, item.Hash)) identity = item.Identity;
        return identity is null ? throw Denied() : Runtime.Principals.ResolveAuthenticated(identity);
    }
    internal void RequireRole(AuthenticatedPrincipal principal, params string[] roles)
    {
        if (principal.TenantId != TenantId || !principal.Purposes.Contains(Purpose)) throw Denied();
        foreach (var role in roles)
        {
            if (!principal.Roles.Contains(role)) continue;
            Runtime.Principals.RequireCurrent(principal, role);
            return;
        }
        throw Denied();
    }
    internal HttpResourceRegistration Resource(string alias) => _resources.GetValueOrDefault(alias)
        ?? throw new ApsDdaException("NOT_FOUND", "Configured resource not found.");
    internal void RequireManifestAccess(AuthenticatedPrincipal p)
    {
        foreach (var entry in _resources.Values) RequireResource(p, entry.CapabilityRef, entry.Target);
    }
    internal void RequireResource(AuthenticatedPrincipal p, ArtifactRef capability, string target)
    {
        var source = Runtime.GetSource(capability).Registration;
        if (!_resources.Values.Any(x => x.Target == target && x.CapabilityRef == capability) ||
            !source.Resources.Contains(target) || !p.Resources.Contains(target) || !p.Audiences.Contains(source.Audience) ||
            !p.CapabilityDigests.Contains(capability.ContentDigest) || !source.Purposes.Contains(Purpose) ||
            source.Classification != p.Classification || source.Residency != p.Residency) throw Denied();
        if (p.Roles.Contains("Executor") && !p.Roles.Contains("Approver") && !p.Roles.Contains("Reader") &&
            (!source.SubjectIds.Contains(p.SubjectId) || !source.WorkloadIds.Contains(p.WorkloadId) || !source.Roles.Overlaps(p.Roles))) throw Denied();
    }
    internal void RequirePlan(AuthenticatedPrincipal p, Artifact plan, bool approver)
    {
        var context = plan.Body.Object("context");
        if (context.String("tenantId") != p.TenantId || context.String("purpose") != Purpose ||
            context["delegationRef"] is not null || context.String("classification") != p.Classification || context.String("residency") != p.Residency ||
            (!approver && (context.String("subjectId") != p.SubjectId || context.String("workloadId") != p.WorkloadId || context.String("executorId") != p.ExecutorId))) throw Denied();
        var steps = plan.Body.Array("steps");
        if (steps.Count > MaximumPlanSteps) throw new ApsDdaException("REQUEST_TOO_LARGE", "Plan step bound exceeded.");
        foreach (var step in steps) RequireResource(p, step!.AsObject().Ref("capabilityRef"), step.AsObject().String("target"));
    }
    internal WorkflowReview Review(string workflowId, AuthenticatedPrincipal p, bool approver)
    {
        if (!Guid.TryParseExact(workflowId, "D", out _)) throw new ApsDdaException("NOT_FOUND", "Workflow not found.");
        var workflow = Runtime.Store.GetWorkflow(workflowId);
        var plan = Runtime.Store.GetArtifact(workflow.PlanRef);
        Runtime.Trust.Validate(plan, new(ArtifactType.ExecutionPlan, "APS-Orchestrator", Runtime.OrchestratorSigner.Issuer));
        RequirePlan(p, plan.Artifact, approver);
        var payloads = workflow.Steps.ToDictionary(x => x.StepId, x => Runtime.Store.GetPayload(workflowId, x.StepId), StringComparer.Ordinal);
        var payloadNode = new JsonObject(payloads.Select(x => KeyValuePair.Create<string, JsonNode?>(x.Key, x.Value.DeepClone())));
        var digest = Jcs.Sha256Digest(new JsonObject { ["profile"] = "aps-dda.experimental/http-review/1",
            ["planRef"] = plan.Artifact.Reference.ToJson(), ["payloads"] = payloadNode });
        return new(HttpBinding.Version, HttpSafety.Snapshot(workflow), plan, payloads, digest);
    }
    internal static ApsDdaException Denied() => new("AUTH_DENIED", "Authenticated caller is outside the configured scope.");
}

internal static class HttpSafety
{
    private static readonly FrozenSet<string> Codes = new[] { "AUTH_DENIED", "INVALID_ARTIFACT", "NOT_FOUND", "REQUEST_TOO_LARGE", "HTTPS_REQUIRED",
        "UNSUPPORTED_MEDIA_TYPE", "UNSUPPORTED_PROFILE", "UNSUPPORTED_VERSION", "UNTRUSTED_PARTICIPANT", "UNKNOWN_CRITICAL_EXTENSION",
        "APPROVAL_REQUIRED", "AUTHORITY_CONSUMED", "AUTHORITY_EXPIRED", "POLICY_EVALUATION_FAILED", "EVIDENCE_STALE", "PRECONDITION_FAILED",
        "STALE_PRECONDITION", "QUOTA_EXCEEDED", "CONFLICT", "OPERATION_UNKNOWN", "CANCEL_NOT_POSSIBLE", "INTEGRITY_FAILED", "REQUEST_REFUSED" }.ToFrozenSet(StringComparer.Ordinal);
    internal static string Code(string code) => Codes.Contains(code) ? code : "REQUEST_REFUSED";
    internal static WorkflowSnapshot Snapshot(WorkflowSnapshot value) => value with { SafeError = value.SafeError is null ? null : Code(value.SafeError) };
}
