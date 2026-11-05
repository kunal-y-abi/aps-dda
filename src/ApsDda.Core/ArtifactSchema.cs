#nullable enable
using System.Text.Json.Nodes;
using Json.Schema;

namespace ApsDda.Core;

public sealed record SchemaValidationResult(bool IsValid);

/// <summary>Shape/format validation only; original bundle loaded from an assembly resource, never network.</summary>
public sealed class ArtifactSchema
{
    public const string BundleId = "https://aps-dda.example.invalid/schema/aps-dda-0.1.bundle.schema.json";
    public const string ResourceName = "ApsDda.Core.aps-dda.schema.json";
    private readonly JsonSchema _schema;
    private readonly EvaluationOptions _options;
    private readonly Dictionary<string, JsonSchema> _definitions = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    public static ArtifactSchema Default { get; } = new();

    public ArtifactSchema()
    {
        var text = ReadOriginalSchema();
        _schema = JsonSchema.FromText(text);
        _options = new EvaluationOptions
        {
            EvaluateAs = SpecVersion.Draft202012,
            RequireFormatValidation = true,
            OutputFormat = OutputFormat.Flag
        };
        _options.SchemaRegistry.Fetch = _ => throw new ApsDdaException("INVALID_ARTIFACT", "External schema fetching is disabled.");
        _options.SchemaRegistry.Register(_schema);
        // Registry indexes this self-contained compound document and its embedded $id resources.
        var root = JsonNode.Parse(text)!.AsObject();
        foreach (var def in root["$defs"]!.AsObject())
            _definitions[def.Key] = JsonSchema.FromText("{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"$ref\":\"" + BundleId + "#/$defs/" + def.Key + "\"}");
    }

    public static string ReadOriginalSchema()
    {
        using var stream = typeof(ArtifactSchema).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("Embedded APS-DDA original schema is missing.");
        using var reader = new StreamReader(stream, StrictJson.Utf8);
        return reader.ReadToEnd();
    }
    public SchemaValidationResult Validate(JsonNode document)
    {
        StrictJson.ValidateNode(document);
        var parsed = StrictJson.Parse(document.ToJsonString());
        lock (_gate) return new(_schema.Evaluate(parsed, _options).IsValid);
    }
    public SchemaValidationResult Validate(string json) => Validate(StrictJson.Parse(json));
    public void RequireValid(JsonNode document)
    {
        if (!Validate(document).IsValid) throw StrictJson.Invalid("Artifact does not match the original APS-DDA schema.");
    }
    public void RequireDefinition(JsonNode document, string definition)
    {
        if (!_definitions.TryGetValue(definition, out var schema)) throw new ArgumentException("Unknown original schema definition.", nameof(definition));
        StrictJson.ValidateNode(document);
        var parsed = StrictJson.Parse(document.ToJsonString());
        lock (_gate)
            if (!schema.Evaluate(parsed, _options).IsValid) throw StrictJson.Invalid("Value does not match the required original schema definition.");
    }
}
