using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using ApsDda.Core;
using Xunit;

namespace ApsDda.Core.Tests;

public class TrustTests
{
    private static TrustedParticipant Participant(ECDsa key, IEnumerable<ArtifactType>? types = null, string role = "APS-Orchestrator", string profile = Artifact.SpecVersion, string version = "1.0.0", DateTimeOffset? keyExpiry = null) => new(
        TestArtifacts.Issuer, [new TrustedKey(TestArtifacts.KeyId, key.ExportSubjectPublicKeyInfo(), expiresAt: keyExpiry)],
        types ?? Enum.GetValues<ArtifactType>(), [role, "APS-Action-Executor", "APS-Read-Publisher"], [profile], [version],
        ["https-json"], ["https://procurement.example.invalid"], ["https://procurement.example.invalid/aps/commands/cancel-purchase-order"],
        ["https://semantics.example.invalid/procurement"]);
    private static TrustedDirectory Directory(ECDsa key, TrustedParticipant? participant = null)
    {
        var d = new TrustedDirectory("northwind-test", new FixedClock(TestArtifacts.Now));
        d.Register(participant ?? Participant(key)); return d;
    }
    private static TrustRequirement Requirement(bool pinned = false) => new(ArtifactType.DecisionSpec, "APS-Orchestrator", TestArtifacts.Issuer, Binding: "https-json", RequirePinnedArtifact: pinned);
    [Fact]
    public void GenuineP256DetachedSignatureRoundTripAndPinnedResolution()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = new TestArtifacts().Decision;
        var signed = DetachedSigning.Sign(artifact, TestArtifacts.KeyId, key);
        Assert.Equal(64, Jcs.DecodeBase64Url(signed.Signature.Value).Length);
        Assert.True(DetachedSigning.VerifyCryptographic(signed, key.ExportSubjectPublicKeyInfo()));
        Assert.False(artifact.ToJson().ContainsKey("signature"));
        var parsed = SignedArtifact.Parse(signed.ToJsonString());
        Assert.True(DetachedSigning.VerifyCryptographic(parsed, key.ExportSubjectPublicKeyInfo()));
        var serialized = JsonSerializer.Serialize(signed);
        Assert.Equal(signed.ToJsonString(), serialized);
        Assert.True(DetachedSigning.VerifyCryptographic(JsonSerializer.Deserialize<SignedArtifact>(serialized)!, key.ExportSubjectPublicKeyInfo()));
        var d = Directory(key); d.PinArtifact(artifact);
        Assert.Same(artifact, d.Validate(signed, Requirement(true)));
        Assert.Same(artifact, d.Resolve(artifact.Reference with { Uri = "https://not-fetched.example.invalid" }, [signed], Requirement(true)));
    }
    [Fact]
    public void SignatureMetadataAndArtifactTamperingFailEvenWhenHashRecomputed()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var artifact = new TestArtifacts().Decision; var signed = DetachedSigning.Sign(artifact, TestArtifacts.KeyId, key);
        var body = artifact.Body; body["recommendation"] = "malicious changed proposal";
        var changed = Artifact.Create(ArtifactType.DecisionSpec, TestArtifacts.Issuer, body, TestArtifacts.Now, artifact.Header.Id);
        Assert.False(DetachedSigning.VerifyCryptographic(signed with { Artifact = changed }, key.ExportSubjectPublicKeyInfo()));
        Assert.False(DetachedSigning.VerifyCryptographic(signed, otherKey.ExportSubjectPublicKeyInfo()));
        foreach (var signature in new[]
        {
            signed.Signature with { Algorithm = "ES256" }, signed.Signature with { KeyId = "https://wrong.example.invalid/key" },
            signed.Signature with { Issuer = "https://wrong.example.invalid" }, signed.Signature with { ArtifactRef = changed.Reference },
            signed.Signature with { Value = Jcs.Base64Url(new byte[64]) }, signed.Signature with { Value = signed.Signature.Value + "=" }
        }) Assert.False(DetachedSigning.VerifyCryptographic(signed with { Signature = signature }, key.ExportSubjectPublicKeyInfo()));
        Assert.Throws<ApsDdaException>(() => Directory(key).Validate(signed with { Artifact = changed }, Requirement()));
    }
    [Fact]
    public void SignatureAndIntegrityAloneDoNotCreateTrust()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var a = new TestArtifacts().Decision;
        var signed = DetachedSigning.Sign(a, TestArtifacts.KeyId, key);
        var d = new TrustedDirectory("northwind-test", new FixedClock(TestArtifacts.Now));
        Assert.Throws<ApsDdaException>(() => d.Validate(signed, Requirement()));
        d.Register(Participant(key));
        Assert.Throws<ApsDdaException>(() => d.Validate(signed, Requirement(true)));
        Assert.Throws<ApsDdaException>(() => d.Validate(signed, Requirement() with { ExpectedIssuer = "https://other.example.invalid" }));
        Assert.Throws<ApsDdaException>(() => d.Resolve(a.Reference with { ContentDigest = "sha256:" + new string('a',64) }, [signed], Requirement()));
        var discovered = Participant(key); Assert.Throws<InvalidOperationException>(() => d.Register(discovered));
    }
    [Theory][InlineData("type")][InlineData("role")][InlineData("profile")][InlineData("version")][InlineData("binding")][InlineData("audience")][InlineData("key-expiry")]
    public void ExplicitWrongTrustPinFailsClosed(string failure)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var signed = DetachedSigning.Sign(new TestArtifacts().Decision, TestArtifacts.KeyId, key);
        var p = failure switch
        {
            "type" => Participant(key, [ArtifactType.Task]), "role" => Participant(key, role: "APS-Renderer"),
            "profile" => Participant(key, profile: "APS-DDA/9.9"), "version" => Participant(key, version: "9.0.0"),
            "key-expiry" => Participant(key, keyExpiry: TestArtifacts.Now), _ => Participant(key)
        };
        var requirement = Requirement();
        if (failure == "binding") requirement = requirement with { Binding = "untrusted-binding" };
        if (failure == "audience") requirement = requirement with { Audience = "https://wrong.example.invalid" };
        Assert.Throws<ApsDdaException>(() => Directory(key, p).Validate(signed, requirement));
    }
    [Theory][InlineData("issuer")][InlineData("key")][InlineData("artifact")]
    public void RevocationIsCheckedOnEveryValidation(string kind)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var a = new TestArtifacts().Decision; var s = DetachedSigning.Sign(a, TestArtifacts.KeyId, key);
        var d = Directory(key); d.PinArtifact(a); d.Validate(s, Requirement(true));
        if (kind == "issuer") d.RevokeIssuer(TestArtifacts.Issuer);
        if (kind == "key") d.RevokeKey(TestArtifacts.Issuer, TestArtifacts.KeyId);
        if (kind == "artifact") d.RevokeArtifact(a.Reference);
        Assert.Throws<ApsDdaException>(() => d.Validate(s, Requirement(true)));
    }
    [Fact]
    public void CrossTenantAndFutureArtifactDenied()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var c = new TestArtifacts(); var b = c.Decision.Body;
        b.Object("context")["tenantId"] = "other-tenant";
        var cross = Artifact.Create(ArtifactType.DecisionSpec, TestArtifacts.Issuer, b, TestArtifacts.Now);
        Assert.Equal("AUTH_DENIED", Assert.Throws<ApsDdaException>(() => Directory(key).Validate(DetachedSigning.Sign(cross, TestArtifacts.KeyId, key), Requirement())).Code);
        var future = Artifact.Create(ArtifactType.DecisionSpec, TestArtifacts.Issuer, c.Decision.Body, TestArtifacts.Now.AddSeconds(1));
        Assert.Throws<ApsDdaException>(() => Directory(key).Validate(DetachedSigning.Sign(future, TestArtifacts.KeyId, key), Requirement()));
    }
    [Fact]
    public void UnsupportedExtensionsFailClosedIncludingNonCriticalAndRequireValidatedVersion()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var c = new TestArtifacts(); var b = c.Decision.Body;
        const string uri = "https://example.invalid/analytics";
        b["extensions"] = new JsonObject { [uri] = new JsonObject { ["version"] = "1.0.0", ["critical"] = false, ["model"] = "reviewed-v1" } };
        var artifact = Artifact.Create(ArtifactType.DecisionSpec, TestArtifacts.Issuer, b, TestArtifacts.Now); var signed = DetachedSigning.Sign(artifact, TestArtifacts.KeyId, key);
        var d = Directory(key);
        Assert.Equal("UNKNOWN_CRITICAL_EXTENSION", Assert.Throws<ApsDdaException>(() => d.Validate(signed, Requirement())).Code);
        d.SupportExtension(new(uri, "1.0.0", n => n.AsObject().String("model") == "reviewed-v1"));
        d.Validate(signed, Requirement());
        b.Object("extensions").Object(uri)["version"] = "2.0.0";
        artifact = Artifact.Create(ArtifactType.DecisionSpec, TestArtifacts.Issuer, b, TestArtifacts.Now);
        Assert.Throws<ApsDdaException>(() => d.Validate(DetachedSigning.Sign(artifact, TestArtifacts.KeyId, key), Requirement()));
        b.Object("extensions").Object(uri)["version"] = "1.0.0"; b.Object("extensions").Object(uri)["model"] = "unreviewed";
        artifact = Artifact.Create(ArtifactType.DecisionSpec, TestArtifacts.Issuer, b, TestArtifacts.Now);
        Assert.Throws<ApsDdaException>(() => d.Validate(DetachedSigning.Sign(artifact, TestArtifacts.KeyId, key), Requirement()));
    }
    [Fact]
    public void OriginalSourceCapabilityRequiresEndpointAudienceAndSemanticPins()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var c = new TestArtifacts();
        var signed = DetachedSigning.Sign(c.Capability, TestArtifacts.KeyId, key);
        var requirement = new TrustRequirement(ArtifactType.ProcessCapability, "APS-Action-Executor", TestArtifacts.Issuer, RequirePinnedArtifact: true);
        var d = Directory(key); d.PinArtifact(c.Capability);
        // Use exact published source contract values, not discovery claims or guessed endpoint paths.
        var capBody = c.Capability.Body; var endpoint = capBody.Object("endpoint"); var semantic = capBody.Object("commandSemantic");
        var correct = new TrustedParticipant(TestArtifacts.Issuer, [new TrustedKey(TestArtifacts.KeyId, key.ExportSubjectPublicKeyInfo())], [ArtifactType.ProcessCapability], ["APS-Action-Executor"], [Artifact.SpecVersion], ["1.0.0"], ["https-json"], [endpoint.String("audience")], [endpoint.String("endpoint")], [semantic.String("namespace")]);
        var trusted = Directory(key, correct); trusted.PinArtifact(c.Capability); trusted.Validate(signed, requirement);
        var unpinned = new TrustedParticipant(TestArtifacts.Issuer, [new TrustedKey(TestArtifacts.KeyId, key.ExportSubjectPublicKeyInfo())], [ArtifactType.ProcessCapability], ["APS-Action-Executor"], [Artifact.SpecVersion], ["1.0.0"]);
        var bad = Directory(key, unpinned); bad.PinArtifact(c.Capability);
        Assert.Throws<ApsDdaException>(() => bad.Validate(signed, requirement));
    }
    [Fact]
    public void WrongCurveTrailingKeyBytesAndEnvelopeDuplicatesRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384); var a = new TestArtifacts().Decision;
        Assert.Throws<CryptographicException>(() => DetachedSigning.Sign(a, TestArtifacts.KeyId, key));
        Assert.Throws<CryptographicException>(() => new TrustedKey(TestArtifacts.KeyId, key.ExportSubjectPublicKeyInfo()));
        using var p256 = ECDsa.Create(ECCurve.NamedCurves.nistP256); var s = DetachedSigning.Sign(a, TestArtifacts.KeyId, p256);
        Assert.False(DetachedSigning.VerifyCryptographic(s, p256.ExportSubjectPublicKeyInfo().Concat(new byte[] {0}).ToArray()));
        var json = s.ToJsonString();
        Assert.Throws<ApsDdaException>(() => SignedArtifact.Parse(json[..^1] + ",\"artifact\":" + a.ToJsonString() + "}"));
        Assert.Throws<ApsDdaException>(() => JsonSerializer.Deserialize<SignedArtifact>(json[..^1] + ",\"artifact\":" + a.ToJsonString() + "}"));
        var envelope = s.ToJson(); envelope["admin"] = true; Assert.Throws<ApsDdaException>(() => SignedArtifact.Parse(envelope.ToJsonString()));
    }
    [Fact]
    public void PublicKeyArraysAreDefensivelyCopied()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var bytes = key.ExportSubjectPublicKeyInfo();
        var pin = new TrustedKey(TestArtifacts.KeyId, bytes); Array.Clear(bytes); var exported = pin.ExportPublicKey(); Array.Clear(exported);
        var signed = DetachedSigning.Sign(new TestArtifacts().Decision, TestArtifacts.KeyId, key);
        Assert.True(DetachedSigning.VerifyCryptographic(signed, pin.ExportPublicKey()));
    }
}
