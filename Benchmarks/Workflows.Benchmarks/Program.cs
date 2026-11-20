using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Workflows.Benchmarks;

var quick = args.Contains("--quick");

// BenchmarkDotNet parses argv itself, so the harness-only flags have to be removed first.
var benchmarkArgs = args.Where(a => a != "--quick").ToArray();

var config = DefaultConfig.Instance
    .AddExporter(MarkdownExporter.GitHub)
    .WithArtifactsPath(Path.Combine(AppContext.BaseDirectory, "BenchmarkDotNet.Artifacts"));

if (quick)
{
    // Small iteration counts so a CI smoke run finishes in a couple of minutes.
    config = config
        .AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance))
        .WithOptions(ConfigOptions.JoinSummary);
}

// BenchmarkDotNet throws when a filter matches no benchmark in a suite, so a --filter is resolved here
// first and only the suites it names are handed to the runner.
var suites = new List<Type> { typeof(InMemoryRunnerTickBenchmarks), typeof(SignalToCommitBenchmarks) };

var filterIndex = Array.FindIndex(benchmarkArgs, a => a == "--filter" || a.StartsWith("--filter=", StringComparison.Ordinal));
if (filterIndex >= 0)
{
    var filter = benchmarkArgs[filterIndex];
    if (filter == "--filter")
    {
        filter = filterIndex + 1 < benchmarkArgs.Length ? benchmarkArgs[filterIndex + 1] : string.Empty;
    }
    else
    {
        filter = filter["--filter=".Length..];
    }

    // The suite has already been narrowed, so BDN must not see the filter again; it throws when a
    // filter matches nothing inside the selected suite.
    var needle = filter.Trim('*');
    benchmarkArgs = benchmarkArgs
        .Where(a => a != "--filter" && !a.StartsWith("--filter=", StringComparison.Ordinal) && a != filter)
        .ToArray();
    if (!string.IsNullOrEmpty(needle))
    {
        suites = suites.Where(s => s.Name.Contains(needle, StringComparison.OrdinalIgnoreCase)).ToList();
    }
}

if (suites.Count == 0)
{
    Console.WriteLine($"No benchmark suite matched '{string.Join(' ', args)}'. Available: {string.Join(", ", typeof(Program).Assembly.GetTypes().Where(t => t.Name.EndsWith("Benchmarks", StringComparison.Ordinal)).Select(t => t.Name))}");
    return 1;
}

foreach (var suite in suites)
{
    BenchmarkRunner.Run(suite, config, benchmarkArgs);
}

return 0;