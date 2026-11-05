#nullable enable
using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace ApsDda.Core;

public sealed class TrustedKey
{
    private readonly byte[] _spki;
    public string KeyId { get; }
    public DateTimeOffset NotBefore { get; }
    public DateTimeOffset ExpiresAt { get; }
    public byte[] ExportPublicKey() => (byte[])_spki.Clone();
    public TrustedKey(string keyId, byte[] publicKeySpki, DateTimeOffset? notBefore = null, DateTimeOffset? expiresAt = null)
    {
        if (!Uri.TryCreate(keyId, UriKind.Absolute, out _)) throw new ArgumentException("Key ID must be an absolute URI.", nameof(keyId));
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKeySpki, out var read);
        if (read != publicKeySpki.Length) throw new CryptographicException("Trailing SPKI bytes.");
        DetachedSigning.RequireP256(key);
        KeyId = keyId; _spki = (byte[])publicKeySpki.Clone();
        NotBefore = notBefore ?? DateTimeOffset.MinValue; ExpiresAt = expiresAt ?? DateTimeOffset.MaxValue;
        if (NotBefore >= ExpiresAt) throw new ArgumentException("Key validity interval is empty.");
    }
}

/// <summary>Administrator-configured pins, never populated automatically from discovery or artifacts.</summary>
public sealed class TrustedParticipant
{
    public string Issuer { get; }
    public FrozenSet<ArtifactType> ArtifactTypes { get; }
    public FrozenSet<string> Roles { get; }
    public FrozenSet<string> Profiles { get; }
    public FrozenSet<string> ArtifactVersions { get; }
    public FrozenSet<string> Bindings { get; }
    public FrozenSet<string> Audiences { get; }
    public FrozenSet<string> Endpoints { get; }
    public FrozenSet<string> SemanticNamespaces { get; }
    public FrozenDictionary<string, TrustedKey> Keys { get; }
    public TrustedParticipant(string issuer, IEnumerable<TrustedKey> keys, IEnumerable<ArtifactType> artifactTypes,
        IEnumerable<string> roles, IEnumerable<string> profiles, IEnumerable<string> artifactVersions,
        IEnumerable<string>? bindings = null, IEnumerable<string>? audiences = null,
        IEnumerable<string>? endpoints = null, IEnumerable<string>? semanticNamespaces = null)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out _)) throw new ArgumentException("Issuer must be an absolute URI.", nameof(issuer));
        Issuer = issuer; Keys = keys.ToFrozenDictionary(k => k.KeyId, StringComparer.Ordinal);
        ArtifactTypes = artifactTypes.ToFrozenSet(); Roles = roles.ToFrozenSet(StringComparer.Ordinal);
        Profiles = profiles.ToFrozenSet(StringComparer.Ordinal); ArtifactVersions = artifactVersions.ToFrozenSet(StringComparer.Ordinal);
        Bindings = (bindings ?? []).ToFrozenSet(StringComparer.Ordinal); Audiences = (audiences ?? []).ToFrozenSet(StringComparer.Ordinal);
        Endpoints = (endpoints ?? []).ToFrozenSet(StringComparer.Ordinal); SemanticNamespaces = (semanticNamespaces ?? []).ToFrozenSet(StringComparer.Ordinal);
        if (Keys.Count == 0 || ArtifactTypes.Count == 0 || Roles.Count == 0 || Profiles.Count == 0 || ArtifactVersions.Count == 0)
            throw new ArgumentException("Trust configuration must explicitly pin keys, types, roles, profiles and artifact versions.");
    }
}

/// <summary>Only validated supported extension versions may pass; no unknown extension is ignored.</summary>
public sealed record ExtensionSupport(string Uri, string Version, Func<JsonNode, bool> Validate);

/// <summary>Caller-owned requirements; roles/issuer/audience must come from trusted configuration, not input claims.</summary>
public sealed record TrustRequirement(ArtifactType ExpectedType, string RequiredRole, string? ExpectedIssuer = null,
    string Profile = Artifact.SpecVersion, string? Binding = null, string? Audience = null, bool RequirePinnedArtifact = false);

/// <summary>In-process, tenant-scoped trust directory. Revocation is local; no remote fetching or durable propagation.</summary>
public sealed class TrustedDirectory
{
    private readonly Dictionary<string, TrustedParticipant> _participants = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _pins = new(StringComparer.Ordinal);
    private readonly HashSet<string> _revokedIssuers = new(StringComparer.Ordinal);
    private readonly HashSet<(string Issuer, string KeyId)> _revokedKeys = new();
    private readonly HashSet<string> _revokedArtifacts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExtensionSupport> _extensions = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly TimeProvider _clock;
    public string TenantId { get; }
    public TrustedDirectory(string tenantId, TimeProvider? clock = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        TenantId = tenantId; _clock = clock ?? TimeProvider.System;
    }
    public void Register(TrustedParticipant participant)
    {
        lock (_gate)
            if (!_participants.TryAdd(participant.Issuer, participant)) throw new InvalidOperationException("Issuer is already pinned; rotate through an explicit administrator operation.");
    }
    public void PinArtifact(Artifact artifact)
    {
        artifact.RequireIntegrity();
        lock (_gate)
        {
            if (!_participants.TryGetValue(artifact.Header.Issuer, out var p) || !p.ArtifactTypes.Contains(artifact.Type))
                throw Untrusted("Cannot pin an artifact for an unknown issuer/type.");
            var key = ReferenceKey(artifact.Reference);
            if (_pins.TryGetValue(key, out var issuer) && issuer != artifact.Header.Issuer) throw Untrusted("Artifact pin issuer collision.");
            _pins[key] = artifact.Header.Issuer;
        }
    }
    public void RevokeIssuer(string issuer) { lock (_gate) _revokedIssuers.Add(issuer); }
    public void RevokeKey(string issuer, string keyId) { lock (_gate) _revokedKeys.Add((issuer, keyId)); }
    public void RevokeArtifact(ArtifactRef reference) { lock (_gate) _revokedArtifacts.Add(ReferenceKey(reference)); }
    public void SupportExtension(ExtensionSupport support)
    {
        if (!Uri.TryCreate(support.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrWhiteSpace(support.Version))
            throw new ArgumentException("Extension must pin an HTTPS URI and version.");
        lock (_gate) _extensions.Add(support.Uri, support);
    }
    public Artifact Validate(SignedArtifact signed, TrustRequirement requirement)
    {
        lock (_gate)
        {
            var a = signed.Artifact;
            a.RequireType(requirement.ExpectedType);
            a.RequireIntegrity();
            var issuer = a.Header.Issuer;
            if (requirement.ExpectedIssuer is not null && issuer != requirement.ExpectedIssuer) throw Untrusted("Wrong source issuer.");
            if (!_participants.TryGetValue(issuer, out var p) || _revokedIssuers.Contains(issuer)) throw Untrusted("Unknown or revoked issuer.");
            if (!p.ArtifactTypes.Contains(a.Type) || !p.Roles.Contains(requirement.RequiredRole)) throw Untrusted("Issuer is not approved for this artifact type/role.");
            if (requirement.Profile != a.Header.SpecVersion || !p.Profiles.Contains(requirement.Profile)) throw new ApsDdaException("UNSUPPORTED_PROFILE", "Unsupported trusted profile.");
            if (!p.ArtifactVersions.Contains(a.Header.ArtifactVersion)) throw new ApsDdaException("UNSUPPORTED_VERSION", "Unapproved artifact version.");
            if (requirement.Binding is not null && !p.Bindings.Contains(requirement.Binding)) throw new ApsDdaException("UNSUPPORTED_VERSION", "Unapproved transport binding.");
            if (requirement.Audience is not null && !p.Audiences.Contains(requirement.Audience)) throw Untrusted("Unapproved source audience.");
            var referenceKey = ReferenceKey(a.Reference);
            if (_revokedArtifacts.Contains(referenceKey)) throw Untrusted("Artifact is revoked.");
            if (requirement.RequirePinnedArtifact && (!_pins.TryGetValue(referenceKey, out var pinnedIssuer) || pinnedIssuer != issuer)) throw Untrusted("Artifact type/id/version/digest is not pinned.");
            if (!p.Keys.TryGetValue(signed.Signature.KeyId, out var key) || _revokedKeys.Contains((issuer, signed.Signature.KeyId))) throw Untrusted("Unknown or revoked signing key.");
            var now = _clock.GetUtcNow();
            if (now < key.NotBefore || now >= key.ExpiresAt || a.Header.IssuedAt > now) throw Untrusted("Signing key/artifact is outside its accepted time interval.");
            if (!DetachedSigning.VerifyCryptographic(signed, key.ExportPublicKey())) throw Untrusted("Invalid source signature.");
            var body = a.Body;
            if (body["context"] is JsonObject context && context.String("tenantId") != TenantId) throw new ApsDdaException("AUTH_DENIED", "Artifact is outside the trusted tenant.");
            if (a.Type == ArtifactType.CapabilityManifest)
            {
                foreach (var role in body.Array("roles")) if (!p.Roles.Contains(role!.GetValue<string>())) throw Untrusted("Unapproved manifest role claim.");
                foreach (var profile in body.Array("profileVersions")) if (!p.Profiles.Contains(profile!.GetValue<string>())) throw new ApsDdaException("UNSUPPORTED_PROFILE", "Unapproved manifest profile.");
                foreach (var binding in body.Array("bindings")) if (!p.Bindings.Contains(binding!.GetValue<string>())) throw Untrusted("Unapproved manifest binding.");
                foreach (var endpoint in body.Array("endpoints")) ValidateEndpoint(endpoint!.AsObject(), p);
            }
            if (a.Type == ArtifactType.ProcessCapability)
            {
                if (requirement.Audience is not null && body.Object("endpoint").String("audience") != requirement.Audience)
                    throw Untrusted("Capability does not target the required source audience.");
                ValidateEndpoint(body.Object("endpoint"), p);
                ValidateSemantic(body.Object("commandSemantic"), p);
            }
            if (a.Type == ArtifactType.DataProduct)
                foreach (var semantic in body.Array("semanticRefs")) ValidateSemantic(semantic!.AsObject(), p);
            ValidateExtensions(body);
            return a;
        }
    }
    public Artifact Resolve(ArtifactRef reference, IEnumerable<SignedArtifact> retainedArtifacts, TrustRequirement requirement)
    {
        var matches = retainedArtifacts.Where(s => s.Artifact.Reference.Id == reference.Id && s.Artifact.Reference.ArtifactType == reference.ArtifactType && s.Artifact.Reference.ArtifactVersion == reference.ArtifactVersion && s.Artifact.Reference.ContentDigest == reference.ContentDigest).Take(2).ToArray();
        if (matches.Length != 1) throw Untrusted("Pinned reference is missing or ambiguous in retained artifacts.");
        var candidate = matches[0];
        reference.RequireMatches(candidate.Artifact, requirement.ExpectedType);
        return Validate(candidate, requirement);
    }
    private static void ValidateEndpoint(JsonObject endpoint, TrustedParticipant participant)
    {
        if (!participant.Endpoints.Contains(endpoint.String("endpoint")) || !participant.Audiences.Contains(endpoint.String("audience"))) throw Untrusted("Endpoint/audience is not pinned.");
        if (endpoint["trustKeyRef"] is JsonValue v && !participant.Keys.ContainsKey(v.GetValue<string>())) throw Untrusted("Endpoint trust key is not pinned.");
    }
    private static void ValidateSemantic(JsonObject semantic, TrustedParticipant participant)
    {
        if (!participant.SemanticNamespaces.Contains(semantic.String("namespace"))) throw Untrusted("Semantic namespace is not approved for this publisher.");
    }
    private void ValidateExtensions(JsonObject body)
    {
        if (body["extensions"] is not JsonObject extensions) return;
        foreach (var e in extensions)
        {
            if (!_extensions.TryGetValue(e.Key, out var support) || e.Value is not JsonObject value || value["version"] is not JsonValue version ||
                !version.TryGetValue<string>(out var declared) || declared != support.Version || value["critical"] is not JsonValue critical || !critical.TryGetValue<bool>(out _))
                throw new ApsDdaException("UNKNOWN_CRITICAL_EXTENSION", "Unknown, unversioned or unsupported extension; binding fails closed.");
            bool valid;
            try { valid = support.Validate(value.DeepClone()); }
            catch (Exception) { valid = false; }
            if (!valid) throw new ApsDdaException("UNKNOWN_CRITICAL_EXTENSION", "Extension validation failed.");
        }
    }
    private static string ReferenceKey(ArtifactRef r) => r.ArtifactType + "\n" + r.Id + "\n" + r.ArtifactVersion + "\n" + r.ContentDigest;
    private static ApsDdaException Untrusted(string detail) => new("UNTRUSTED_PARTICIPANT", detail);
}
