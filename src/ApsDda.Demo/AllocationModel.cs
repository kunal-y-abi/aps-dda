using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Runtime;

namespace ApsDda.Demo;

public sealed record RegionInput(string Region, long Open, long ExpectedAdded, long UpperAdded,
    long BaseTeams, long AvailableTeams, long EmergencyReserveTeams, long MinimumAdditionalTeams, long MinimumServedItems, long CostPerTeam);
public sealed record AllocationPlan(string Name, IReadOnlyList<long> AdditionalTeams, IReadOnlyList<long> ExpectedBacklog,
    IReadOnlyList<long> UpperBacklog, long Cost)
{
    public JsonObject ToJson() => new()
    {
        ["name"] = Name, ["additionalTeams"] = Numbers(AdditionalTeams), ["expectedBacklog"] = Numbers(ExpectedBacklog),
        ["upperBacklog"] = Numbers(UpperBacklog), ["cost"] = CanonicalQuantity.Format(Cost)
    };
    private static JsonArray Numbers(IEnumerable<long> values) => ArtifactFactory.Strings(values.Select(CanonicalQuantity.Format));
}
public sealed record StructuredDecisionSpec(string ModelVersion, string SelectedAlternative, int EnumeratedCandidates,
    int FeasibleCandidates, IReadOnlyList<AllocationPlan> Alternatives, JsonObject Parameters)
{
    public JsonObject ToJson() => new()
    {
        ["version"] = "1.0.0", ["critical"] = true, ["modelVersion"] = ModelVersion,
        ["selectedAlternative"] = SelectedAlternative, ["enumeratedCandidates"] = EnumeratedCandidates,
        ["feasibleCandidates"] = FeasibleCandidates, ["alternatives"] = new JsonArray(Alternatives.Select(p => (JsonNode)p.ToJson()).ToArray()),
        ["parameters"] = Parameters.DeepClone()
    };
}

/// <summary>Synthetic one-day fluid backlog model, deliberately not an actuarial/customer decision model.</summary>
public sealed class AllocationModel
{
    public const string ExtensionUri = "https://profiles.example.invalid/insurance/allocation-v1";
    public static readonly string[] RegionNames = ["north", "central", "south"];
    public JsonObject Dataset { get; }
    public IReadOnlyList<RegionInput> Regions { get; }
    public long ItemsPerTeam { get; }
    public long Budget { get; }
    public long DeployablePool { get; }
    public AllocationModel(JsonObject dataset)
    {
        Dataset = StrictJson.ParseObject(dataset.ToJsonString());
        if (Dataset.String("profile") != "aps-dda.synthetic-insurance/1") Refuse();
        foreach (var source in new[] { "claims", "forecast", "capacity", "finance" })
            if (!ValidateSource(source, Dataset.Object(source))) Refuse();
        ItemsPerTeam = Q(Dataset.Object("capacity"), "itemsPerTeam"); Budget = Q(Dataset.Object("finance"), "budget");
        DeployablePool = checked(Q(Dataset.Object("capacity"), "poolTeams") - Q(Dataset.Object("capacity"), "reservedPoolTeams"));
        if (ItemsPerTeam <= 0 || DeployablePool <= 0) Refuse();
        Regions = RegionNames.Select((r, i) => new RegionInput(r, Q(Row("claims", i), "open"), Q(Row("forecast", i), "expected"), Q(Row("forecast", i), "upper"),
            Q(Row("capacity", i), "baseTeams"), Q(Row("capacity", i), "availableTeams"), Q(Row("capacity", i), "emergencyReserveTeams"),
            Q(Row("capacity", i), "minimumAdditionalTeams"), Q(Row("capacity", i), "minimumServedItems"), Q(Row("finance", i), "costPerTeam"))).ToArray();
        if (Regions.Any(r => r.UpperAdded < r.ExpectedAdded || r.CostPerTeam <= 0 || r.MinimumAdditionalTeams < 1 || r.AvailableTeams < r.EmergencyReserveTeams)) Refuse();
        // Bound the finite teaching example; do not permit untrusted unbounded enumeration.
        if (Regions.Any(r => r.AvailableTeams > 20 || r.BaseTeams > 20 || r.CostPerTeam > 100000 || r.MinimumServedItems > 100000) || Budget > 100000 || ItemsPerTeam > 10000 || Regions.Any(r => r.Open > 100000 || r.UpperAdded > 100000)) Refuse();
    }
    public static JsonObject LoadDataset()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ApsDda.Demo.synthetic-data.json")!;
        using var reader = new StreamReader(stream); return StrictJson.ParseObject(reader.ReadToEnd());
    }
    private JsonObject Row(string source, int i) => Dataset.Object(source).Array("regions")[i]!.AsObject();
    private static long Q(JsonObject o, string k) => CanonicalQuantity.Parse(o.String(k), allowZero: true);
    private static void Refuse() => throw new ApsDdaException("INVALID_ARTIFACT", "Synthetic data does not satisfy the reviewed unit, region or period binding.");
    public static bool ValidateSource(string source, JsonObject value)
    {
        try
        {
            var unit = source switch { "claims" => "aggregate-open-items", "forecast" => "aggregate-added-items", "capacity" => "internal-team-day", "finance" => "synthetic-budget-credit", _ => "" };
            var top = source switch { "claims" => new[] { "unit", "period", "regions", "retrievedText" }, "capacity" => ["unit", "period", "regions", "itemsPerTeam", "poolTeams", "reservedPoolTeams"], "finance" => ["unit", "period", "regions", "budget"], _ => ["unit", "period", "regions"] };
            if (unit == "" || value.String("unit") != unit || value.String("period") != "synthetic-day-1" || value.Any(p => !top.Contains(p.Key))) return false;
            var rows = value.Array("regions"); if (rows.Count != 3) return false;
            var fields = source switch { "claims" => new[] { "open" }, "forecast" => ["expected", "upper"], "capacity" => ["baseTeams", "availableTeams", "emergencyReserveTeams", "minimumAdditionalTeams", "minimumServedItems"], _ => ["costPerTeam"] };
            for (var i = 0; i < 3; i++)
            {
                var row = rows[i]!.AsObject(); if (row.String("region") != RegionNames[i] || row.Count != fields.Length + 1) return false;
                foreach (var field in fields) _ = Q(row, field);
            }
            foreach (var key in top.Where(k => k != "unit" && k != "period" && k != "regions" && k != "retrievedText")) _ = Q(value, key);
            if (value["retrievedText"] is not null && value.String("retrievedText").Length > 4096) return false;
            return true;
        }
        catch { return false; }
    }
    /// <summary>All candidate allocations, including service-floor failures, are actually visited.</summary>
    public StructuredDecisionSpec Compare(string selected = "balanced")
    {
        if (selected is not ("balanced" or "min-backlog")) throw new ApsDdaException("INVALID_ARTIFACT", "Unknown reviewed allocation alternative.");
        var feasible = new List<AllocationPlan>(); var enumerated = 0;
        for (long n = 0; n <= Regions[0].AvailableTeams - Regions[0].EmergencyReserveTeams; n++)
        for (long c = 0; c <= Regions[1].AvailableTeams - Regions[1].EmergencyReserveTeams; c++)
        for (long s = 0; s <= Regions[2].AvailableTeams - Regions[2].EmergencyReserveTeams; s++)
        {
            enumerated++; long[] x = [n, c, s]; var cost = Regions.Select((r, i) => checked(x[i] * r.CostPerTeam)).Sum();
            if (x.Sum() > DeployablePool || cost > Budget || Regions.Where((r, i) => x[i] < r.MinimumAdditionalTeams).Any()) continue;
            if (Regions.Where((r, i) => Math.Min(r.Open + r.ExpectedAdded, checked((r.BaseTeams + x[i]) * ItemsPerTeam)) < r.MinimumServedItems).Any()) continue;
            var expected = Regions.Select((r, i) => Math.Max(0, checked(r.Open + r.ExpectedAdded - (r.BaseTeams + x[i]) * ItemsPerTeam))).ToArray();
            var upper = Regions.Select((r, i) => Math.Max(0, checked(r.Open + r.UpperAdded - (r.BaseTeams + x[i]) * ItemsPerTeam))).ToArray();
            feasible.Add(new("candidate", Array.AsReadOnly(x), Array.AsReadOnly(expected), Array.AsReadOnly(upper), cost));
        }
        if (feasible.Count == 0) throw new ApsDdaException("POLICY_EVALUATION_FAILED", "No feasible synthetic allocation; manual planning required.");
        IOrderedEnumerable<AllocationPlan> Tie(IOrderedEnumerable<AllocationPlan> q) => q.ThenBy(p => p.Cost).ThenBy(p => p.AdditionalTeams[0]).ThenBy(p => p.AdditionalTeams[1]).ThenBy(p => p.AdditionalTeams[2]);
        var cheap = Tie(feasible.OrderBy(p => p.ExpectedBacklog.Sum())).First() with { Name = "min-backlog" };
        var balanced = Tie(feasible.OrderBy(p => p.UpperBacklog.Max()).ThenBy(p => p.UpperBacklog.Sum())).First() with { Name = "balanced" };
        var parameters = new JsonObject
        {
            ["itemsPerTeam"] = CanonicalQuantity.Format(ItemsPerTeam), ["budget"] = CanonicalQuantity.Format(Budget), ["deployablePool"] = CanonicalQuantity.Format(DeployablePool),
            ["regionOrder"] = ArtifactFactory.Strings(RegionNames), ["sourceValueDigests"] = new JsonObject(new[] { "claims", "forecast", "capacity", "finance" }.Select(k => KeyValuePair.Create<string, JsonNode?>(k, JsonValue.Create(Jcs.Sha256Digest(NumericalProjection(k))))))
        };
        return new("1.0.0", selected, enumerated, feasible.Count, Array.AsReadOnly(new[] { cheap, balanced }), parameters);
    }
    public JsonObject NumericalProjection(string source)
    {
        var projection = (JsonObject)Dataset.Object(source).DeepClone(); projection.Remove("retrievedText"); return projection;
    }
}
