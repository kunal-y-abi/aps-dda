#nullable enable
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ApsDda.Core;

/// <summary>A stable refusal code and safe, non-payload error detail.</summary>
public sealed class ApsDdaException(string code, string safeDetail, Exception? inner = null) : Exception(safeDetail, inner)
{
    public string Code { get; } = code;
}

/// <summary>Strict profile parser: duplicates, bad Unicode, unsafe integers and non-finite numbers are refused.</summary>
public static class StrictJson
{
    public const int MaximumBytes = 1_048_576;
    public const int MaximumDepth = 64;
    public const double MaximumSafeInteger = 9_007_199_254_740_991d;
    internal static readonly UTF8Encoding Utf8 = new(false, true);

    public static JsonNode Parse(string json)
    {
        try { return Parse(Utf8.GetBytes(json)); }
        catch (EncoderFallbackException e) { throw Invalid("Invalid Unicode input.", e); }
    }

    public static JsonNode Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length > MaximumBytes) throw Invalid("JSON exceeds the configured size limit.");
        try
        {
            _ = Utf8.GetString(utf8); // Refuse malformed UTF-8, rather than replacing it.
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
            {
                AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = MaximumDepth
            });
            var names = new Stack<HashSet<string>>();
            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject: names.Push(new(StringComparer.Ordinal)); break;
                    case JsonTokenType.EndObject: names.Pop(); break;
                    case JsonTokenType.PropertyName:
                        var name = ReadString(ref reader);
                        if (!names.Peek().Add(name)) throw Invalid("Duplicate JSON member name.");
                        break;
                    case JsonTokenType.String: _ = ReadString(ref reader); break;
                    case JsonTokenType.Number:
                        ValidateNumber(reader.ValueSpan);
                        break;
                }
            }
            return JsonNode.Parse(utf8, documentOptions: new JsonDocumentOptions { MaxDepth = MaximumDepth })
                ?? throw Invalid("Null JSON root is unsupported.");
        }
        catch (ApsDdaException) { throw; }
        catch (Exception e) when (e is JsonException or DecoderFallbackException or InvalidOperationException or FormatException)
        { throw Invalid("Malformed or non-interoperable JSON.", e); }
    }

    public static JsonObject ParseObject(string json) => Parse(json) as JsonObject ?? throw Invalid("An object root is required.");
    public static JsonObject ParseObject(ReadOnlySpan<byte> utf8) => Parse(utf8) as JsonObject ?? throw Invalid("An object root is required.");

    private static string ReadString(ref Utf8JsonReader reader)
    {
        // System.Text.Json's GetString rejects unpaired escaped surrogates. Validate the decoded
        // UTF-16 as well so no replacement characters are introduced by our encoding boundary.
        var value = reader.GetString()!;
        ValidateUnicode(value);
        return value;
    }

    public static void ValidateUnicode(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (++i >= text.Length || !char.IsLowSurrogate(text[i])) throw Invalid("Unpaired Unicode surrogate.");
            }
            else if (char.IsLowSurrogate(text[i])) throw Invalid("Unpaired Unicode surrogate.");
        }
    }

    private static void ValidateNumber(ReadOnlySpan<byte> token)
    {
        if (token.Length > 256) throw Invalid("Numeric token exceeds the profile limit; use a canonical string.");
        var raw = Encoding.ASCII.GetString(token);
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw Invalid("Non-finite JSON number.");
        // All binary64 values beyond this magnitude are integral and unsafe for exact identifiers.
        // Reject every lexical representation, including decimal and exponent disguises.
        if (Math.Abs(number) > MaximumSafeInteger) throw Invalid("Unsafe integer/magnitude; use a canonical string.");
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var exact) && Math.Abs(exact) > 9_007_199_254_740_991m)
            throw Invalid("Unsafe lexical magnitude; use a canonical string.");
        var significand = raw.Split('e', 'E')[0];
        if (number == 0 && significand.Any(c => c is >= '1' and <= '9'))
            throw Invalid("Nonzero JSON number underflows binary64; use a canonical string.");
    }

    /// <summary>Validate mutable JsonNode input before serialization can replace bad UTF-16.</summary>
    internal static void ValidateNode(JsonNode? node, int depth = 0)
    {
        if (depth > MaximumDepth) throw Invalid("JSON exceeds the configured depth limit.");
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj) { ValidateUnicode(pair.Key); ValidateNode(pair.Value, depth + 1); }
                break;
            case JsonArray array:
                foreach (var child in array) ValidateNode(child, depth + 1);
                break;
            case JsonValue value:
                if (value.TryGetValue<string>(out var s)) ValidateUnicode(s);
                else if (value.TryGetValue<JsonElement>(out var element) && element.ValueKind == JsonValueKind.String)
                    ValidateUnicode(element.GetString()!);
                else if (value.GetValueKind() is JsonValueKind.Object or JsonValueKind.Array)
                    throw Invalid("JsonValue-wrapped object/array is unsupported; use JsonObject/JsonArray.");
                break;
        }
    }

    internal static ApsDdaException Invalid(string detail, Exception? inner = null) => new("INVALID_ARTIFACT", detail, inner);
}
