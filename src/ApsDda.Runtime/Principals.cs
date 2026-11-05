using System.Collections.Frozen;
using System.Text.Json.Nodes;
using ApsDda.Core;

namespace ApsDda.Runtime;

/// <summary>Administrator provisioning, not an HTTP request model. No credentials belong in artifacts.</summary>
public sealed record PrincipalRegistration(string Identity, string TenantId, string SubjectId, string WorkloadId,
    string ExecutorId, IEnumerable<string> Roles, IEnumerable<string> Purposes, IEnumerable<string> Audiences,
    IEnumerable<string> Resources, IEnumerable<string> CapabilityDigests, string Classification = "internal", string Residency = "local");

/// <summary>Opaque in-process handle obtained only from the trusted authentication boundary.</summary>
public sealed class AuthenticatedPrincipal
{
    internal AuthenticatedPrincipal(PrincipalRegistration r)
    {
        Identity = r.Identity; TenantId = r.TenantId; SubjectId = r.SubjectId; WorkloadId = r.WorkloadId; ExecutorId = r.ExecutorId;
        Roles = r.Roles.ToFrozenSet(StringComparer.Ordinal); Purposes = r.Purposes.ToFrozenSet(StringComparer.Ordinal);
        Audiences = r.Audiences.ToFrozenSet(StringComparer.Ordinal); Resources = r.Resources.ToFrozenSet(StringComparer.Ordinal);
        CapabilityDigests = r.CapabilityDigests.ToFrozenSet(StringComparer.Ordinal); Classification = r.Classification; Residency = r.Residency;
    }
    public string Identity { get; }
    public string TenantId { get; }
    public string SubjectId { get; }
    public string WorkloadId { get; }
    public string ExecutorId { get; }
    public string Classification { get; }
    public string Residency { get; }
    public FrozenSet<string> Roles { get; }
    public FrozenSet<string> Purposes { get; }
    public FrozenSet<string> Audiences { get; }
    public FrozenSet<string> Resources { get; }
    public FrozenSet<string> CapabilityDigests { get; }
}

public sealed class PrincipalDirectory
{
    private readonly Dictionary<string, AuthenticatedPrincipal> _principals = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    public AuthenticatedPrincipal Register(PrincipalRegistration registration)
    {
        var p = new AuthenticatedPrincipal(registration);
        lock (_gate) _principals.Add(p.Identity, p);
        return p;
    }
    /// <summary>Call only after an application authenticates this identity; resolving a user-supplied string is not authentication.</summary>
    public AuthenticatedPrincipal ResolveAuthenticated(string identity)
    {
        lock (_gate) return _principals.GetValueOrDefault(identity) ?? throw Errors.Denied();
    }
    public void Revoke(string identity) { lock (_gate) _principals.Remove(identity); }
    public void RequireCurrent(AuthenticatedPrincipal p, string role)
    {
        lock (_gate)
            if (!_principals.TryGetValue(p.Identity, out var current) || !ReferenceEquals(p, current) || !p.Roles.Contains(role)) throw Errors.Denied();
    }
    internal void RequireContext(AuthenticatedPrincipal p, JsonObject context, string role, bool approver = false)
    {
        RequireCurrent(p, role);
        if (p.TenantId != context.String("tenantId") || !p.Purposes.Contains(context.String("purpose")) ||
            (context["classification"] is not null && p.Classification != context.String("classification")) ||
            (context["residency"] is not null && p.Residency != context.String("residency")) || context["delegationRef"] is not null ||
            (!approver && (p.SubjectId != context.String("subjectId") || p.WorkloadId != context.String("workloadId") || p.ExecutorId != context.String("executorId"))))
            throw Errors.Denied(); // Delegation is explicitly unsupported; never silently accepted.
    }
}

internal static class Errors
{
    public static ApsDdaException Denied() => new("AUTH_DENIED", "Current authenticated principal is outside the configured scope.");
    public static void Require(bool condition, string code = "INVALID_ARTIFACT", string detail = "Binding does not satisfy the selected runtime profile.")
    { if (!condition) throw new ApsDdaException(code, detail); }
}
