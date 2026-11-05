#nullable enable
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Canonicalizer = Org.Webpki.JsonCanonicalizer.JsonCanonicalizer;

namespace ApsDda.Core;

/// <summary>RFC 8785 bytes, constrained by the SDK's stricter safe-number input profile.</summary>
public static class Jcs
{
    public const string UpstreamCommit = "32a48195b9b20fe67b1795e39e4509f14d860b45";
    public static byte[] Canonicalize(string json)
    {
        var node = StrictJson.Parse(json);
        if (node is not (JsonObject or JsonArray)) throw StrictJson.Invalid("JCS binding supports object/array roots only.");
        var culture = CultureInfo.CurrentCulture;
        try
        {
            // Upstream's DToA fallback appends an integer exponent using current culture.
            // Scope invariant culture synchronously without changing the unmodified source.
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            return new Canonicalizer(json).GetEncodedUTF8();
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.IO.IOException)
        { throw StrictJson.Invalid("Canonicalization failed.", e); }
        finally { CultureInfo.CurrentCulture = culture; }
    }
    public static byte[] Canonicalize(ReadOnlySpan<byte> utf8)
    {
        _ = StrictJson.Parse(utf8);
        return Canonicalize(StrictJson.Utf8.GetString(utf8));
    }
    public static byte[] Canonicalize(JsonNode node)
    {
        StrictJson.ValidateNode(node);
        return Canonicalize(node.ToJsonString());
    }
    /// <summary>For external payloads/parameters ONLY when their own profile explicitly adopts this byte rule.</summary>
    public static string Sha256Digest(JsonNode node) => "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Canonicalize(node)));
    public static string Base64Url(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static byte[] DecodeBase64Url(string text)
    {
        if (text.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw StrictJson.Invalid("Invalid base64url encoding.");
        try
        {
            var bytes = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4));
            if (!string.Equals(Base64Url(bytes), text, StringComparison.Ordinal)) throw StrictJson.Invalid("Noncanonical base64url encoding.");
            return bytes;
        }
        catch (FormatException e) { throw StrictJson.Invalid("Invalid base64url encoding.", e); }
    }
}
