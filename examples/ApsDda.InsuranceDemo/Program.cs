using System.Text.Json.Nodes;
using ApsDda.Core;
using ApsDda.Demo;

// AI-assisted source development. Synthetic in-process fixture, not a production insurer or identity provider.
string Option(string name, string fallback)
{
    var index = Array.IndexOf(args, name); return index < 0 ? fallback : index + 1 < args.Length ? args[index + 1] : throw new ArgumentException("Missing option value.");
}
try
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] is "--help" or "--all" or "--interactive") continue;
        if (args[i] is not ("--scenario" or "--plan" or "--export") || ++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Unknown option or missing value.");
    }
    if (args.Contains("--help"))
    {
        Console.WriteLine("dotnet run --project examples/ApsDda.InsuranceDemo -- [--scenario NAME|--all] [--plan balanced|min-backlog] [--interactive] [--export DIRECTORY]");
        Console.WriteLine("Scenarios: " + string.Join(", ", ScenarioRunner.Supported));
        Console.WriteLine("No keys/services needed. Default human path uses an explicitly configured synthetic manager fixture; --interactive requires a person to type the exact plan digest.");
        return 0;
    }
    var selected = Option("--plan", "balanced"); var scenario = Option("--scenario", "happy");
    var scenarios = args.Contains("--all") ? ScenarioRunner.Supported : new[] { scenario };
    if (args.Contains("--interactive") && (args.Contains("--all") || scenario != "happy")) throw new ArgumentException("Interactive review supports one happy human workflow only.");
    foreach (var name in scenarios)
    {
        using var session = ScenarioRunner.Create(name, selected);
        if (args.Contains("--interactive"))
        {
            Console.WriteLine("SYNTHETIC HUMAN REVIEW: selected=" + selected + "; analytical alternatives:");
            Console.WriteLine(session.Analysis.ToJson().ToJsonString(new() { WriteIndented = true }));
            Console.WriteLine("Exact execution plan:"); Console.WriteLine(session.Plan.Artifact.ToJsonString());
            Console.WriteLine("Exact normalized payload map:");
            Console.WriteLine(new JsonObject(session.Payloads.Select(p => KeyValuePair.Create<string, JsonNode?>(p.Key, p.Value.DeepClone()))).ToJsonString(new() { WriteIndented = true }));
            Console.WriteLine("Type this exact plan digest to approve as the locally configured synthetic manager (anything else cancels): " + session.Plan.Artifact.PlanDigest);
            if (!string.Equals(Console.ReadLine(), session.Plan.Artifact.PlanDigest, StringComparison.Ordinal))
            {
                Console.WriteLine("Approval not issued. No source operation performed."); return 3;
            }
        }
        var report = ScenarioRunner.Run(session, name);
        Console.WriteLine(report.ToJson().ToJsonString(new() { WriteIndented = true }));
        var export = Option("--export", ""); if (export.Length > 0) ScenarioRunner.Export(session, report, args.Contains("--all") ? Path.Combine(export, name) : export);
    }
    return 0; // Fault scenarios are successful demonstrations of actual refusals/unknown/manual states, not business success.
}
catch (ApsDdaException e) { Console.Error.WriteLine(e.Code); return 2; }
catch (ArgumentException) { Console.Error.WriteLine("Invalid command-line options. Use --help."); return 2; }
