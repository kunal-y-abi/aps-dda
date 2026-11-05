#nullable enable
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ApsDda.Core;

/// <summary>Experimental detached signature envelope; this is not JWS or a new APS-DDA artifact type.</summary>
public sealed record DetachedSignature(
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("keyId")] string KeyId,
    [property: JsonPropertyName("issuer")] string Issuer,
    [property: JsonPropertyName("artifactRef")] ArtifactRef ArtifactRef,
    [property: JsonPropertyName("value")] string Value);

[JsonConverter(typeof(SignedArtifactJsonConverter))]
public sealed record SignedArtifact(Artifact Artifact, DetachedSignature Signature)
{
    public JsonObject ToJson() => new()
    {
        ["artifact"] = Artifact.ToJson(), ["signature"] = JsonSerializer.SerializeToNode(Signature)
    };
    public string ToJsonString() => ToJson().ToJsonString();
    public static SignedArtifact Parse(string json) => FromJson(StrictJson.ParseObject(json));
    public static SignedArtifact Parse(ReadOnlySpan<byte> utf8) => FromJson(StrictJson.ParseObject(utf8));
    private static SignedArtifact FromJson(JsonObject o)
    {
        if (o.Count != 2 || !o.ContainsKey("artifact") || !o.ContainsKey("signature")) throw StrictJson.Invalid("Unexpected signed envelope shape.");
        var s = o.Object("signature");
        string[] fields = ["algorithm", "keyId", "issuer", "artifactRef", "value"];
        if (s.Count != fields.Length || fields.Any(k => !s.ContainsKey(k))) throw StrictJson.Invalid("Unexpected detached signature shape.");
        return new(Artifact.FromJson(o.Object("artifact")), new(s.String("algorithm"), s.String("keyId"), s.String("issuer"), s.Ref("artifactRef"), s.String("value")));
    }
}

public sealed class SignedArtifactJsonConverter : JsonConverter<SignedArtifact>
{
    public override SignedArtifact Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return SignedArtifact.Parse(document.RootElement.GetRawText());
    }
    public override void Write(Utf8JsonWriter writer, SignedArtifact value, JsonSerializerOptions options) => value.ToJson().WriteTo(writer, options);
}

/// <summary>SHA-256/ECDSA over JCS of the documented detached protected input. 64-byte IEEE P1363 signatures.</summary>
public static class DetachedSigning
{
    public const string Algorithm = "APS-DDA-ECDSA-P256-SHA256-P1363-v1";
    public const string Domain = "APS-DDA/0.1 detached artifact signature v1";
    public static SignedArtifact Sign(Artifact artifact, string keyId, ECDsa privateKey)
    {
        artifact.RequireIntegrity();
        RequireP256(privateKey);
        if (!System.Uri.TryCreate(keyId, UriKind.Absolute, out _)) throw StrictJson.Invalid("Key ID must be an absolute URI.");
        var signature = new DetachedSignature(Algorithm, keyId, artifact.Header.Issuer, artifact.Reference, "");
        var bytes = privateKey.SignData(ProtectedInput(signature), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new(artifact, signature with { Value = Jcs.Base64Url(bytes) });
    }
    /// <summary>Cryptography/integrity ONLY. Caller must independently pin issuer, key, role, profile and capability.</summary>
    public static bool VerifyCryptographic(SignedArtifact signed, ReadOnlySpan<byte> publicKeySpki)
    {
        try
        {
            signed.Artifact.RequireIntegrity();
            var s = signed.Signature;
            if (s.Algorithm != Algorithm || s.Issuer != signed.Artifact.Header.Issuer || s.ArtifactRef.Uri is not null) return false;
            s.ArtifactRef.RequireMatches(signed.Artifact);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKeySpki, out var read);
            if (read != publicKeySpki.Length) return false;
            RequireP256(key);
            var bytes = Jcs.DecodeBase64Url(s.Value);
            return bytes.Length == 64 && key.VerifyData(ProtectedInput(s), bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (Exception e) when (e is ApsDdaException or CryptographicException or ArgumentException) { return false; }
    }
    public static byte[] ProtectedInput(DetachedSignature s) => Jcs.Canonicalize(new JsonObject
    {
        ["domain"] = Domain, ["algorithm"] = s.Algorithm, ["keyId"] = s.KeyId,
        ["issuer"] = s.Issuer, ["artifactRef"] = s.ArtifactRef.ToJson()
    });
    internal static void RequireP256(ECDsa key)
    {
        var p = key.ExportParameters(false);
        if (key.KeySize != 256 || p.Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value)
            throw new CryptographicException("Only named NIST P-256 keys are supported.");
    }
}
