#nullable enable
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ApsDda.Core;

public enum ArtifactType
{
    CapabilityManifest, DataProduct, ProcessCapability, Task, Negotiation, EvidenceBundle,
    DecisionSpec, ExecutionPlan, PolicyDecision, Authority, Approval, CommandInvocation,
    OperationReceipt, ApplicationSpec, BuilderPrompt
}

public sealed record ArtifactHeader(string Id, string ArtifactVersion, string SpecVersion, string Issuer, DateTimeOffset IssuedAt, string ContentDigest);

/// <summary>Exact wire reference. Identifier strings are not normalized to establish authorization equivalence.</summary>
public sealed record ArtifactRef(
    [property: JsonPropertyName("artifactType")] string ArtifactType,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("artifactVersion")] string ArtifactVersion,
    [property: JsonPropertyName("contentDigest")] string ContentDigest,
    [property: JsonPropertyName("uri"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Uri = null)
{
    public JsonObject ToJson()
    {
        var obj = new JsonObject { ["artifactType"] = ArtifactType, ["id"] = Id, ["artifactVersion"] = ArtifactVersion, ["contentDigest"] = ContentDigest };
        if (Uri is not null) obj["uri"] = Uri;
        ArtifactSchema.Default.RequireDefinition(obj, "ArtifactRef");
        return obj;
    }
    public static ArtifactRef FromJson(JsonNode node)
    {
        ArtifactSchema.Default.RequireDefinition(node, "ArtifactRef");
        var o = node.AsObject();
        return new(o.String("artifactType"), o.String("id"), o.String("artifactVersion"), o.String("contentDigest"), o["uri"]?.GetValue<string>());
    }
    public void RequireMatches(Artifact artifact, ArtifactType? expectedType = null)
    {
        _ = ToJson();
        if (expectedType is not null && artifact.Type != expectedType.Value) throw StrictJson.Invalid("Reference has the wrong artifact type.");
        var actual = artifact.Reference;
        if (ArtifactType != actual.ArtifactType || Id != actual.Id || ArtifactVersion != actual.ArtifactVersion || ContentDigest != actual.ContentDigest)
            throw StrictJson.Invalid("Pinned artifact reference does not match type/id/version/digest.");
        artifact.RequireIntegrity();
        // Optional URI is a location hint, not a trust claim, identity substitute or fetch instruction.
    }
}

/// <summary>A defensively copied original-schema artifact. Parsing validates shape, not trust or integrity.</summary>
[JsonConverter(typeof(ArtifactJsonConverter))]
public sealed class Artifact
{
    public const string SpecVersion = "APS-DDA/0.1";
    private readonly JsonObject _document;
    private Artifact(JsonObject document)
    {
        _document = (JsonObject)document.DeepClone();
        Type = Enum.Parse<ArtifactType>(_document.String("artifactType"));
        var h = _document.Object("header");
        Header = new(h.String("id"), h.String("artifactVersion"), h.String("specVersion"), h.String("issuer"), h.Time("issuedAt"), h.String("contentDigest"));
    }
    public ArtifactType Type { get; }
    public ArtifactHeader Header { get; }
    public JsonObject Body => (JsonObject)_document["body"]!.DeepClone();
    public ArtifactRef Reference => new(Type.ToString(), Header.Id, Header.ArtifactVersion, Header.ContentDigest);
    public JsonObject ToJson() => (JsonObject)_document.DeepClone();
    public string ToJsonString() => _document.ToJsonString();
    public byte[] ToCanonicalBytes() => Jcs.Canonicalize(_document);

    public static Artifact Parse(string json) => FromJson(StrictJson.ParseObject(json));
    public static Artifact Parse(ReadOnlySpan<byte> utf8) => FromJson(StrictJson.ParseObject(utf8));
    public static Artifact FromJson(JsonObject document)
    {
        ArtifactSchema.Default.RequireValid(document);
        return new(document);
    }
    /// <summary>Issue new schema-shaped bytes, calculate plan digest first if applicable, then content digest.</summary>
    public static Artifact Create(ArtifactType type, string issuer, JsonObject body, DateTimeOffset issuedAt,
        string? id = null, string artifactVersion = "1.0.0")
    {
        StrictJson.ValidateUnicode(issuer);
        StrictJson.ValidateNode(body);
        var document = new JsonObject
        {
            ["artifactType"] = type.ToString(),
            ["header"] = new JsonObject
            {
                ["id"] = id ?? Guid.NewGuid().ToString("D"), ["artifactVersion"] = artifactVersion,
                ["specVersion"] = SpecVersion, ["issuer"] = issuer,
                ["issuedAt"] = issuedAt.ToUniversalTime().ToString("O"), ["contentDigest"] = "sha256:" + new string('0', 64)
            },
            ["body"] = body.DeepClone()
        };
        if (type == ArtifactType.ExecutionPlan) document.Object("body")["planDigest"] = ComputePlanDigest(document);
        document.Object("header")["contentDigest"] = ComputeContentDigest(document);
        return FromJson(document);
    }
    public static JsonObject ArtifactCore(JsonObject completeArtifact)
    {
        var clone = (JsonObject)completeArtifact.DeepClone();
        clone.Object("header").Remove("contentDigest");
        return clone;
    }
    public static JsonObject PlanCore(JsonObject completePlan)
    {
        if (completePlan.String("artifactType") != nameof(ArtifactType.ExecutionPlan)) throw StrictJson.Invalid("A plan projection requires ExecutionPlan.");
        var clone = ArtifactCore(completePlan);
        clone.Object("body").Remove("planDigest");
        return clone;
    }
    public static string ComputeContentDigest(JsonObject completeArtifact) => Jcs.Sha256Digest(ArtifactCore(completeArtifact));
    public static string ComputePlanDigest(JsonObject completePlan) => Jcs.Base64Url(SHA256.HashData(Jcs.Canonicalize(PlanCore(completePlan))));
    public string? PlanDigest => Type == ArtifactType.ExecutionPlan ? _document.Object("body").String("planDigest") : null;
    public void RequireIntegrity()
    {
        if (!string.Equals(Header.ContentDigest, ComputeContentDigest(_document), StringComparison.Ordinal)) throw StrictJson.Invalid("Artifact content digest mismatch.");
        if (Type == ArtifactType.ExecutionPlan && !string.Equals(PlanDigest, ComputePlanDigest(_document), StringComparison.Ordinal))
            throw StrictJson.Invalid("Execution plan digest mismatch.");
    }
    public void RequireType(ArtifactType expected)
    {
        if (Type != expected) throw StrictJson.Invalid("Unexpected artifact type.");
    }
}

public sealed class ArtifactJsonConverter : JsonConverter<Artifact>
{
    public override Artifact Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return Artifact.Parse(document.RootElement.GetRawText());
    }
    public override void Write(Utf8JsonWriter writer, Artifact value, JsonSerializerOptions options) => value.ToJson().WriteTo(writer, options);
}

/// <summary>Original field access helpers. Missing/wrong values fail with safe errors.</summary>
public static class JsonFields
{
    public static string String(this JsonObject o, string key)
    {
        if (o[key] is JsonValue v && v.TryGetValue<string>(out var value)) return value;
        throw StrictJson.Invalid("Required string field is missing or has the wrong type.");
    }
    public static JsonObject Object(this JsonObject o, string key) => o[key] as JsonObject ?? throw StrictJson.Invalid("Required object field is missing or has the wrong type.");
    public static JsonArray Array(this JsonObject o, string key) => o[key] as JsonArray ?? throw StrictJson.Invalid("Required array field is missing or has the wrong type.");
    public static DateTimeOffset Time(this JsonObject o, string key)
    {
        if (DateTimeOffset.TryParse(o.String(key), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var value)) return value;
        throw StrictJson.Invalid("Required timestamp is invalid.");
    }
    public static ArtifactRef Ref(this JsonObject o, string key) => ArtifactRef.FromJson(o[key] ?? throw StrictJson.Invalid("Required artifact reference is missing."));
    public static IReadOnlyList<ArtifactRef> Refs(this JsonObject o, string key) => o.Array(key).Select(n => ArtifactRef.FromJson(n ?? throw StrictJson.Invalid("Null artifact reference."))).ToArray();
}
