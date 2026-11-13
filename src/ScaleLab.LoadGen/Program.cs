using System.Globalization;
using System.Text.Json;
using ScaleLab.Core.Measurement;
using ScaleLab.Core.Reporting;
using ScaleLab.Core.Scenarios;
using ScaleLab.LoadGen;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var command = args.Length > 0 ? args[0] : "demo";
var options = CommandLine.Parse(args.Skip(1));

try
{
    return command switch
    {
        "demo" => await DemoRunner.RunAsync(CommandLine.Target(options), CommandLine.Int(options, "connections", 300), Console.Out, cts.Token),
        "bench" => await Bench(options, cts.Token),
        "report" => Report(options),
        _ => Usage(),
    };
}
catch (Exception ex) when (ex is ArgumentException or FormatException or HttpRequestException or TimeoutException)
{
    await Console.Error.WriteLineAsync($"error: {ex.Message}");
    return 2;
}

static async Task<int> Bench(IReadOnlyDictionary<string, string> options, CancellationToken ct)
{
    var bench = new BenchOptions(
        ScenarioCatalog.Get(CommandLine.Required(options, "scenario")),
        CommandLine.Get(options, "stages", "1000,2500,5000,10000").Split(',').Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToList(),
        CommandLine.Int(options, "messages", 20),
        double.Parse(CommandLine.Get(options, "rate", "2"), CultureInfo.InvariantCulture),
        CommandLine.Int(options, "bytes", 32),
        CommandLine.Int(options, "concurrency", 100),
        TimeSpan.FromSeconds(CommandLine.Int(options, "settle", 3)),
        TimeSpan.FromSeconds(CommandLine.Int(options, "drain", 5)),
        CommandLine.Int(options, "rep", 1),
        CommandLine.Get(options, "host", Environment.GetEnvironmentVariable("LAB_HOST") ?? "unspecified"));

    var result = await BenchRunner.RunAsync(CommandLine.Target(options), bench, Console.Error, ct);
    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(LabApi.Json) { WriteIndented = true });
    if (options.TryGetValue("out", out var path))
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, json + "\n", ct);
        Console.WriteLine($"wrote {path}");
    }
    else
    {
        Console.WriteLine(json);
    }

    return 0;
}

static int Report(IReadOnlyDictionary<string, string> options)
{
    var runsDir = CommandLine.Get(options, "runs", "benchmark-results/runs");
    var runs = Directory.EnumerateFiles(runsDir, "*.json")
        .Order(StringComparer.Ordinal)
        .Select(f => JsonSerializer.Deserialize<RunResult>(File.ReadAllText(f), LabApi.Json)!)
        .ToList();
    if (runs.Count == 0)
    {
        throw new ArgumentException($"No run files in {runsDir}.");
    }

    var rows = Aggregation.Aggregate(runs);
    var markdown = CommandLine.Get(options, "markdown", "benchmark-results/summary.md");
    File.WriteAllText(markdown, ScaleLab.Core.Reporting.Report.Markdown(rows));

    var images = CommandLine.Get(options, "images", "docs/images");
    Directory.CreateDirectory(images);
    foreach (var (name, svg) in ScaleLab.Core.Reporting.Report.Charts(rows, CommandLine.Get(options, "env", string.Empty)))
    {
        File.WriteAllText(Path.Combine(images, name), svg);
    }

    Console.WriteLine($"{runs.Count} runs -> {rows.Count} rows: {markdown}, charts in {images}");
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("""
        usage:
          loadgen demo   [--connections 300]
          loadgen bench  --scenario <single|sticky-only|sticky-redis|rr-negotiate|rr-skip> [--stages 1000,2500,5000,10000]
                         [--messages 20] [--rate 2] [--bytes 32] [--concurrency 100] [--settle 3] [--drain 5]
                         [--rep 1] [--out file.json] [--host "description"]
          loadgen report [--runs benchmark-results/runs] [--images docs/images] [--markdown benchmark-results/summary.md] [--env "text"]
        common: --direct http://hub-1:8080 --proxy http://proxy:8080 --servers http://hub-1:8080,http://hub-2:8080,http://hub-3:8080 --key <lab key>
        (defaults also from LAB_DIRECT, LAB_PROXY, LAB_SERVERS, LAB_KEY)
        """);
    return 2;
}
