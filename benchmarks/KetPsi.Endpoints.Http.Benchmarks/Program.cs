using BenchmarkDotNet.Running;

namespace KetPsi.Endpoints.Http.Benchmarks;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args[0] == ThroughputProbe.LoadClientSwitch)
            return await ThroughputProbe.RunLoadClientAsync(args);

        if (args.Contains("--throughput", StringComparer.OrdinalIgnoreCase))
            return await ThroughputProbe.RunAsync(args);

        if (args.Contains("--serve", StringComparer.OrdinalIgnoreCase))
            return await ThroughputProbe.ServeAsync(args);

        // Always verify equivalence first so the numbers compare the same work.
        Console.WriteLine("Equivalence check (endpoint invoked in-memory for every host):");
        var equivalent = await EquivalenceCheck.RunAllAsync(Console.Out);
        if (args.Contains("--verify", StringComparer.OrdinalIgnoreCase))
            return equivalent ? 0 : 1;

        if (!equivalent)
        {
            Console.Error.WriteLine(
                "WARNING: some scenarios are not equivalent across hosts. Their [GlobalSetup] throws, so BenchmarkDotNet " +
                "reports them as failed instead of comparing different work. The remaining scenarios still run.");
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        return 0;
    }
}
