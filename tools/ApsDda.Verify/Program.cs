using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using ApsDda.AspNetCore;
using ApsDda.Core;

// AI-assisted development. Finite local verifier; it creates no public service or Git operation.
if (args.Length != 2) { Console.Error.WriteLine("Usage: dotnet run --project tools/ApsDda.Verify -c Release --no-build -- <repo-root> <local-package-directory>"); return 2; }
var repo = Path.GetFullPath(args[0]); var packages = Path.GetFullPath(args[1]);
var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
var temporary = Path.Combine(Path.GetTempPath(), "aps-dda-verification-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    Console.WriteLine("Checking actual local nupkg contents...");
    foreach (var name in new[] { "Core", "Runtime", "AspNetCore" })
    {
        var path = Path.Combine(packages, $"ApsDda.{name}.0.1.0-preview.1.nupkg");
        using var archive = ZipFile.OpenRead(path);
        Require(archive.GetEntry($"lib/net10.0/ApsDda.{name}.dll") is not null, "Package DLL missing.");
        Require(archive.GetEntry("API_CONTRACT.md") is not null, "Package contract missing.");
        if (name != "Core") continue;
        Require(archive.GetEntry("schema/aps-dda.schema.json") is not null, "Copied original schema missing.");
        var source = archive.Entries.Count(e => e.FullName.StartsWith("third-party/jcs/", StringComparison.Ordinal) && e.FullName.EndsWith(".cs", StringComparison.Ordinal));
        Require(source == 8, "Covered upstream sources missing.");
        foreach (var notice in new[] { "PROVENANCE.md", "LICENSE.upstream", "LICENSE.Apache-2.0.txt", "LICENSE.MPL-2.0.txt", "NOTICE.V8.txt", "NOTICE.Lucent.txt" })
            Require(archive.GetEntry("third-party/jcs/" + notice) is not null, "Upstream notice missing.");
    }
    await Kestrel();
    Console.WriteLine("Creating fresh package-only project outside checkout with isolated NuGet cache...");
    var consumer = Path.Combine(temporary, "consumer"); Directory.CreateDirectory(consumer);
    foreach (var (resource, target) in new[] { ("PackageSmoke.Program.cs", "Program.cs"), ("PackageSmoke.csproj", "PackageSmoke.csproj") })
    {
        using var input = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource) ?? throw new Exception("Consumer template missing.");
        using var output = File.Create(Path.Combine(consumer, target)); input.CopyTo(output);
    }
    File.WriteAllText(Path.Combine(consumer, "global.json"), File.ReadAllText(Path.Combine(repo, "global.json")));
    var config = $"<configuration><packageSources><clear/><add key=\"local\" value=\"{System.Security.SecurityElement.Escape(packages)}\"/><add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\"/></packageSources><packageSourceMapping><packageSource key=\"local\"><package pattern=\"ApsDda.*\"/></packageSource><packageSource key=\"nuget.org\"><package pattern=\"*\"/></packageSource></packageSourceMapping></configuration>\n";
    File.WriteAllText(Path.Combine(consumer, "NuGet.Config"), config);
    var env = new Dictionary<string, string> { ["NUGET_PACKAGES"] = Path.Combine(temporary, "nuget-cache") };
    await Run(["restore", "PackageSmoke.csproj", "--configfile", "NuGet.Config"], consumer, env);
    await Run(["restore", "PackageSmoke.csproj", "--locked-mode", "--configfile", "NuGet.Config"], consumer, env);
    await Run(["build", "PackageSmoke.csproj", "-c", "Release", "--no-restore", "-warnaserror"], consumer, env);
    await Run(["run", "--project", "PackageSmoke.csproj", "-c", "Release", "--no-build", "--no-restore"], consumer, env);
    Console.WriteLine("PASS live Kestrel and outside-checkout installed-package verification; temporary credentials/cache/data removed.");
    return 0;
}
catch (Exception e) { Console.Error.WriteLine("FAIL verification: " + e.Message); return 1; }
finally { Directory.Delete(temporary, true); }

async Task Kestrel()
{
    // Obtain an available loopback port; host readiness is bounded and failures do not cause indefinite retry.
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
    var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var uri = new Uri($"http://127.0.0.1:{port}");
    var dll = Path.Combine(repo, "examples", "ApsDda.Api", "bin", "Release", "net10.0", "ApsDda.Api.dll");
    var env = new Dictionary<string, string>
    {
        ["APS_DDA_EXECUTOR_TOKEN"] = Jcs.Base64Url(RandomNumberGenerator.GetBytes(32)),
        ["APS_DDA_APPROVER_TOKEN"] = Jcs.Base64Url(RandomNumberGenerator.GetBytes(32)),
        ["APS_DDA_READER_TOKEN"] = Jcs.Base64Url(RandomNumberGenerator.GetBytes(32)),
        ["DOTNET_ENVIRONMENT"] = "Development", ["APS_DDA_ALLOW_LOOPBACK_HTTP"] = "true",
        ["ASPNETCORE_URLS"] = uri.AbsoluteUri, ["APS_DDA_DATABASE"] = Path.Combine(temporary, "http", "runtime.sqlite")
    };
    using var host = Process.Start(StartInfo([dll], repo, env)) ?? throw new Exception("Host did not start.");
    var stdout = host.StandardOutput.ReadToEndAsync(); var stderr = host.StandardError.ReadToEndAsync();
    try
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(2) };
        var ready = false;
        for (var i = 0; i < 60 && !host.HasExited; i++)
        {
            try { using var response = await http.GetAsync("/aps-dda/v1/manifest"); ready = response.StatusCode == HttpStatusCode.Unauthorized; }
            catch (HttpRequestException) { } catch (TaskCanceledException) { }
            if (ready) break; await Task.Delay(500);
        }
        Require(ready, "Kestrel did not become ready with unauthenticated 401.");
        await Run([dll, "--exercise", "--approve-reviewed-plan"], repo, env);
        var reader = new ApsDdaClient(http, env["APS_DDA_READER_TOKEN"], allowDevelopmentLoopbackHttp: true);
        var read = await reader.ReadResourceAsync("north");
        Require(read.Resource.Version == "2" && read.Resource.State.String("available") == "18" && read.Resource.State.String("reserved") == "2", "Actual source state differs from replay.");
        using var reset = new HttpRequestMessage(HttpMethod.Post, "/aps-dda/v1/admin/reset");
        reset.Headers.Authorization = new("Bearer", env["APS_DDA_EXECUTOR_TOKEN"]);
        reset.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        using var responseReset = await http.SendAsync(reset);
        Require(responseReset.StatusCode == HttpStatusCode.NotFound, "Unexpected HTTP admin/reset route.");
        Console.WriteLine("PASS actual loopback Kestrel: unauthenticated 401, typed-client reviewed-plan replay, source available=18/reserved=2/version=2, no reset route.");
    }
    finally
    {
        if (!host.HasExited) host.Kill(entireProcessTree: true);
        await host.WaitForExitAsync(); await stdout; await stderr;
    }
}
ProcessStartInfo StartInfo(string[] command, string cwd, IReadOnlyDictionary<string, string>? env)
{
    var info = new ProcessStartInfo(dotnet) { WorkingDirectory = cwd, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var item in command) info.ArgumentList.Add(item);
    if (env is not null) foreach (var pair in env) info.Environment[pair.Key] = pair.Value;
    return info;
}
async Task Run(string[] command, string cwd, IReadOnlyDictionary<string, string>? env = null)
{
    Console.WriteLine("dotnet " + string.Join(" ", command.Select(c => c.Contains(' ') ? "\"" + c + "\"" : c)));
    using var process = Process.Start(StartInfo(command, cwd, env)) ?? throw new Exception("dotnet did not start.");
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
    Console.Write(await stdout); Console.Error.Write(await stderr);
    Require(process.ExitCode == 0, "dotnet child command failed.");
}
static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
