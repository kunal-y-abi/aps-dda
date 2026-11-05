using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using ApsDda.Core;
using Org.Webpki.Es6NumberSerialization;
using Xunit;
using UpstreamJcs = Org.Webpki.JsonCanonicalizer.JsonCanonicalizer;

namespace ApsDda.Core.Tests;

public class CanonicalizationTests
{
    [Theory]
    [InlineData("arrays")][InlineData("french")][InlineData("structures")]
    [InlineData("unicode")][InlineData("values")][InlineData("weird")]
    public void PinnedReferenceCorpusMatchesBytes(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "JcsVectors");
        var input = File.ReadAllBytes(Path.Combine(path, "input", name + ".json"));
        var expected = File.ReadAllBytes(Path.Combine(path, "output", name + ".json"));
        Assert.Equal(expected, new UpstreamJcs(input).GetEncodedUTF8());
        // RFC values corpus includes 1e30, intentionally refused by SDK safe-number profile.
        if (name != "values") Assert.Equal(expected, Jcs.Canonicalize(input));
        else Assert.Throws<ApsDdaException>(() => Jcs.Canonicalize(input));
    }
    public static TheoryData<string, string> NumberVectors => new()
    {
        {"0000000000000000", "0"}, {"8000000000000000", "0"}, {"0000000000000001", "5e-324"}, {"8000000000000001", "-5e-324"},
        {"7fefffffffffffff", "1.7976931348623157e+308"}, {"ffefffffffffffff", "-1.7976931348623157e+308"},
        {"4340000000000000", "9007199254740992"}, {"c340000000000000", "-9007199254740992"}, {"4430000000000000", "295147905179352830000"},
        {"44b52d02c7e14af5", "9.999999999999997e+22"}, {"44b52d02c7e14af6", "1e+23"}, {"44b52d02c7e14af7", "1.0000000000000001e+23"},
        {"444b1ae4d6e2ef4e", "999999999999999700000"}, {"444b1ae4d6e2ef4f", "999999999999999900000"}, {"444b1ae4d6e2ef50", "1e+21"},
        {"3eb0c6f7a0b5ed8c", "9.999999999999997e-7"}, {"3eb0c6f7a0b5ed8d", "0.000001"},
        {"41b3de4355555553", "333333333.3333332"}, {"41b3de4355555554", "333333333.33333325"}, {"41b3de4355555555", "333333333.3333333"},
        {"41b3de4355555556", "333333333.3333334"}, {"41b3de4355555557", "333333333.33333343"},
        {"becbf647612f3696", "-0.0000033333333333333333"}, {"43143ff3c1cb0959", "1424953923781206.2"}
    };
    [Theory][MemberData(nameof(NumberVectors))]
    public void RfcAppendixBBinary64Vectors(string bits, string expected)
    {
        var number = BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64(bits, 16)));
        Assert.Equal(expected, NumberToJson.SerializeNumber(number));
        if (Math.Abs(number) <= StrictJson.MaximumSafeInteger)
            Assert.Equal("[" + expected + "]", Encoding.UTF8.GetString(Jcs.Canonicalize("[" + number.ToString("R", CultureInfo.InvariantCulture) + "]")));
    }
    [Fact]
    public void ReferenceFormatterRejectsNonFinite()
    {
        Assert.Throws<ArgumentException>(() => NumberToJson.SerializeNumber(double.NaN));
        Assert.Throws<ArgumentException>(() => NumberToJson.SerializeNumber(double.PositiveInfinity));
    }
    [Fact]
    public void TenThousandPinnedV8NumberVectorsMatchReferenceFormatter()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "JcsVectors", "es6testfile10000.txt"));
        Assert.Equal("b9f7a8e75ef22a835685a52ccba7f7d6bdc99e34b010992cbc5864cd12be6892", Convert.ToHexStringLower(SHA256.HashData(bytes)));
        var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(10000, lines.Length);
        foreach (var line in lines)
        {
            var fields = line.Split(',');
            var number = BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64(fields[0], 16)));
            Assert.Equal(fields[1], NumberToJson.SerializeNumber(number));
        }
    }
    [Fact]
    public void SdkCanonicalizationIsLocaleInvariantAndRestoresCallerCulture()
    {
        var prior = CultureInfo.CurrentCulture;
        var strange = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        strange.NumberFormat.NegativeSign = "NEG";
        try
        {
            CultureInfo.CurrentCulture = strange;
            foreach (var pair in NumberVectors)
            {
                var number = BitConverter.Int64BitsToDouble(unchecked((long)Convert.ToUInt64((string)pair[0], 16)));
                if (Math.Abs(number) <= StrictJson.MaximumSafeInteger)
                    Assert.Equal("[" + pair[1] + "]", Encoding.UTF8.GetString(Jcs.Canonicalize("[" + number.ToString("R", CultureInfo.InvariantCulture) + "]")));
            }
            Assert.Same(strange, CultureInfo.CurrentCulture);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }
    [Theory]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("{\"x\":{\"a\":1,\"\\u0061\":2}}")]
    [InlineData("{\"x\":NaN}")][InlineData("[Infinity]")][InlineData("[1e400]")]
    [InlineData("[9007199254740992]")][InlineData("[9007199254740993]")][InlineData("[-9007199254740992]")]
    [InlineData("[9007199254740992.0]")][InlineData("[9.007199254740993e15]")][InlineData("[1e30]")]
    [InlineData("[9007199254740991.1]")][InlineData("[-9007199254740991.1]")]
    [InlineData("[1e-999]")][InlineData("[01]")][InlineData("[true,]")]
    [InlineData("[\"\\ud800\"]")][InlineData("{\"\\udc00\":1}")][InlineData("[\"\\ud800x\"]")]
    [InlineData("[1]/*comment*/")][InlineData("{} {}")]
    public void UnsafeOrAmbiguousInputFailsClosed(string json) => Assert.Throws<ApsDdaException>(() => Jcs.Canonicalize(json));
    [Fact]
    public void BadUtf8AndRawSurrogatesAreRejected()
    {
        Assert.Throws<ApsDdaException>(() => Jcs.Canonicalize(new byte[] { 91, 34, 0xc0, 0xaf, 34, 93 }));
        Assert.Throws<ApsDdaException>(() => Jcs.Canonicalize("[\"" + '\ud800' + "\"]"));
        Assert.Throws<ApsDdaException>(() => Jcs.Canonicalize(new JsonObject { ["s"] = "x" + '\ud800' }));
        Assert.Throws<ApsDdaException>(() => Jcs.Canonicalize(new JsonObject { ["x" + '\udc00'] = 1 }));
    }
    [Fact]
    public void SafeBoundaryNegativeZeroAndSubnormalAreAccepted()
    {
        Assert.Equal("[9007199254740991,-9007199254740991,0,5e-324]", Encoding.UTF8.GetString(Jcs.Canonicalize("[9007199254740991,-9007199254740991,-0.0,5e-324]")));
    }
    [Fact]
    public void Utf16SortNotUtf8OrLocaleAndNoUnicodeNormalization()
    {
        Assert.Equal("{\"a\":2,\"é\":4,\"€\":3,\"😀\":1,\"דּ\":5}", Encoding.UTF8.GetString(Jcs.Canonicalize("{\"😀\":1,\"€\":3,\"é\":4,\"דּ\":5,\"a\":2}")));
        Assert.NotEqual(Jcs.Sha256Digest(new JsonObject { ["v"] = "é" }), Jcs.Sha256Digest(new JsonObject { ["v"] = "e\u0301" }));
    }
    [Fact]
    public void NumericAndEscapingVariantsCanonicalizeIdentically()
    {
        Assert.Equal(Jcs.Canonicalize("{\"b\":4.5,\"a\":\"é/\"}"), Jcs.Canonicalize("{\"a\":\"\\u00e9\\/\",\"b\":4.500e0}"));
        Assert.NotEqual(Jcs.Sha256Digest(JsonNode.Parse("[1,2]")!), Jcs.Sha256Digest(JsonNode.Parse("[2,1]")!));
    }
    [Fact]
    public void SizeAndDepthLimitsAndBase64UrlCanonicality()
    {
        Assert.Throws<ApsDdaException>(() => StrictJson.Parse(new string('[', 65) + "0" + new string(']', 65)));
        JsonNode nested = new JsonArray(0);
        for (var i = 0; i < 100; i++) nested = new JsonArray(nested);
        Assert.Throws<ApsDdaException>(() => Jcs.Canonicalize(nested));
        Assert.Throws<ApsDdaException>(() => StrictJson.Parse("[\"" + new string('x', StrictJson.MaximumBytes) + "\"]"));
        Assert.Throws<ApsDdaException>(() => Jcs.DecodeBase64Url("AA="));
        Assert.Throws<ApsDdaException>(() => Jcs.DecodeBase64Url("AB"));
        Assert.Equal(new byte[] { 0 }, Jcs.DecodeBase64Url("AA"));
        Assert.Throws<ApsDdaException>(() => Jcs.Canonicalize("1"));
    }
}
