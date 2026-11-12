using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace KetPsi.Endpoints.Http.Benchmarks;

/// <summary>
/// Kestrel throughput probe. The server (this process) runs one host at a time on loopback; load comes from a
/// <b>separate</b> client process (this same executable started with <see cref="LoadClientSwitch"/>), so HttpClient
/// allocations and CPU never show up in the numbers. All metrics are sampled in the server process only:
/// <see cref="GC.GetTotalAllocatedBytes(bool)"/> (precise), process CPU time and GC collection counts, divided by the
/// number of requests the server handled inside the measurement window.
/// </summary>
public static class ThroughputProbe
{
    public const string LoadClientSwitch = "--load-client";

    private static long s_requests;

    // ───────────────────────────── orchestrated run (--throughput) ─────────────────────────────

    public static async Task<int> RunAsync(string[] args)
    {
        var duration = GetInt(args, "--duration", 5);
        var warmup = GetInt(args, "--warmup", 2);
        var connections = GetInt(args, "--connections", 32);
        var scenarios = GetEnumFilter<Scenario>(args, "--scenario");
        var hosts = GetEnumFilter<HostKind>(args, "--host");

        Console.WriteLine($"Server-side throughput: warmup={warmup}s duration={duration}s connections={connections} " +
                          $"(client = separate process; GC={(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")})");
        Console.WriteLine($"{"Scenario",-14} {"Host",-10} {"RPS",10} {"B/req",9} {"CPU us/req",11} {"Gen0",6} {"Gen1",6} {"Gen2",6} {"Client ok/err",16}");

        foreach (var scenario in scenarios)
        {
            foreach (var host in hosts)
            {
                var line = await MeasureAsync(host, scenario, warmup, duration, connections);
                Console.WriteLine(line);
            }
        }

        return 0;
    }

    private static async Task<string> MeasureAsync(HostKind host, Scenario scenario, int warmup, int duration, int connections)
    {
        await using var app = BuildCountingApp(host, port: 0);
        await app.StartAsync();
        var baseUrl = app.Urls.First();

        using var client = StartLoadClient(baseUrl, scenario, warmup, duration, connections);

        Snapshot? start = null;
        Snapshot? end = null;
        string clientSummary = "?";

        while (await client.StandardOutput.ReadLineAsync() is { } line)
        {
            if (line == "WARM")
            {
                start = Snapshot.Take();
            }
            else if (line.StartsWith("DONE", StringComparison.Ordinal))
            {
                end = Snapshot.Take();
                clientSummary = line[5..];
            }
        }

        await client.WaitForExitAsync();
        await app.StopAsync();

        if (start is null || end is null)
            return $"{scenario,-14} {host,-10} client failed (exit {client.ExitCode}): {await client.StandardError.ReadToEndAsync()}";

        var d = end.Value - start.Value;
        var requests = Math.Max(1, d.Requests);
        return string.Create(CultureInfo.InvariantCulture,
            $"{scenario,-14} {host,-10} {d.Requests / d.Elapsed.TotalSeconds,10:F0} {d.AllocatedBytes / (double)requests,9:F0} " +
            $"{d.Cpu.TotalMicroseconds / requests,11:F1} {d.Gen0,6} {d.Gen1,6} {d.Gen2,6} {clientSummary,16}");
    }

    private static Process StartLoadClient(string baseUrl, Scenario scenario, int warmup, int duration, int connections)
    {
        var processPath = Environment.ProcessPath!;
        var arguments = new List<string>();
        if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            arguments.Add(Assembly.GetEntryAssembly()!.Location);

        arguments.AddRange([LoadClientSwitch, baseUrl, scenario.ToString(), warmup.ToString(CultureInfo.InvariantCulture),
            duration.ToString(CultureInfo.InvariantCulture), connections.ToString(CultureInfo.InvariantCulture)]);

        var psi = new ProcessStartInfo(processPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var a in arguments)
            psi.ArgumentList.Add(a);

        return Process.Start(psi) ?? throw new InvalidOperationException("Could not start the load client.");
    }

    // ───────────────────────────── external load tool mode (--serve) ─────────────────────────────

    public static async Task<int> ServeAsync(string[] args)
    {
        var host = GetEnumFilter<HostKind>(args, "--host").First();
        var port = GetInt(args, "--port", 5005);
        var interval = TimeSpan.FromSeconds(GetInt(args, "--interval", 5));

        await using var app = BuildCountingApp(host, port);
        await app.StartAsync();
        var baseUrl = app.Urls.First();

        Console.WriteLine($"Serving {host} on {baseUrl} (server-side stats every {interval.TotalSeconds:F0}s; Ctrl+C to stop).");
        Console.WriteLine("Drive it from another process, e.g.:");
        foreach (var scenario in Enum.GetValues<Scenario>())
        {
            var r = Scenarios.Get(scenario);
            var headers = string.Concat(r.Headers.Select(h => $" -H \"{h.Name}: {h.Value}\""));
            var body = r.Body is null ? "" : $" -m POST -b '{System.Text.Encoding.UTF8.GetString(r.Body)}'";
            Console.WriteLine($"  bombardier -c 64 -d 10s -l{headers}{body} \"{baseUrl}{r.PathAndQuery}\"");
        }

        var stopping = app.Lifetime.ApplicationStopping;
        var previous = Snapshot.Take();
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stopping);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var now = Snapshot.Take();
            var d = now - previous;
            previous = now;
            if (d.Requests == 0)
                continue;

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"rps={d.Requests / d.Elapsed.TotalSeconds:F0} alloc/req={d.AllocatedBytes / (double)d.Requests:F0}B " +
                $"cpu/req={d.Cpu.TotalMicroseconds / d.Requests:F1}us gen0={d.Gen0} gen1={d.Gen1} gen2={d.Gen2}"));
        }

        await app.StopAsync();
        return 0;
    }

    private static WebApplication BuildCountingApp(HostKind host, int port)
    {
        var app = BenchHosts.Build(host, kestrel: true, port: port);
        // Allocation-free request counter (static lambda, no closure).
        app.Use(static (HttpContext context, RequestDelegate next) =>
        {
            Interlocked.Increment(ref s_requests);
            return next(context);
        });
        return app;
    }

    private readonly record struct Snapshot(TimeSpan Time, long Requests, long AllocatedBytes, TimeSpan Cpu, int Gen0, int Gen1, int Gen2)
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        public static Snapshot Take()
        {
            using var process = Process.GetCurrentProcess();
            return new(
                Clock.Elapsed,
                Interlocked.Read(ref s_requests),
                GC.GetTotalAllocatedBytes(precise: true),
                process.TotalProcessorTime,
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2));
        }

        public static Delta operator -(Snapshot end, Snapshot start) => new(
            end.Time - start.Time,
            end.Requests - start.Requests,
            end.AllocatedBytes - start.AllocatedBytes,
            end.Cpu - start.Cpu,
            end.Gen0 - start.Gen0,
            end.Gen1 - start.Gen1,
            end.Gen2 - start.Gen2);
    }

    private readonly record struct Delta(TimeSpan Elapsed, long Requests, long AllocatedBytes, TimeSpan Cpu, int Gen0, int Gen1, int Gen2);

    // ───────────────────────────── load client (separate process) ─────────────────────────────

    public static async Task<int> RunLoadClientAsync(string[] args)
    {
        var baseUrl = new Uri(args[1]);
        var scenario = Enum.Parse<Scenario>(args[2]);
        var warmup = TimeSpan.FromSeconds(int.Parse(args[3], CultureInfo.InvariantCulture));
        var duration = TimeSpan.FromSeconds(int.Parse(args[4], CultureInfo.InvariantCulture));
        var connections = int.Parse(args[5], CultureInfo.InvariantCulture);

        var request = Scenarios.Get(scenario);
        var uri = new Uri(baseUrl, request.PathAndQuery);
        using var handler = new SocketsHttpHandler { MaxConnectionsPerServer = connections, UseProxy = false, AllowAutoRedirect = false };
        using var http = new HttpClient(handler) { DefaultRequestVersion = HttpVersion.Version11 };

        long ok = 0, errors = 0;
        var stop = false;
        var expectedStatus = scenario == Scenario.BadRequest ? 400 : 200;

        async Task Worker()
        {
            while (!Volatile.Read(ref stop))
            {
                using var message = new HttpRequestMessage(new HttpMethod(request.Method), uri);
                foreach (var (name, value) in request.Headers)
                {
                    if (!message.Headers.TryAddWithoutValidation(name, value))
                    {
                        message.Content ??= new ByteArrayContent(request.Body ?? []);
                        message.Content.Headers.TryAddWithoutValidation(name, value);
                    }
                }

                if (request.Body is not null && message.Content is null)
                    message.Content = new ByteArrayContent(request.Body);

                try
                {
                    using var response = await http.SendAsync(message, HttpCompletionOption.ResponseContentRead);
                    if ((int)response.StatusCode == expectedStatus)
                        Interlocked.Increment(ref ok);
                    else
                        Interlocked.Increment(ref errors);
                }
                catch (HttpRequestException)
                {
                    Interlocked.Increment(ref errors);
                }
            }
        }

        var workers = Enumerable.Range(0, connections).Select(_ => Task.Run(Worker)).ToArray();

        await Task.Delay(warmup);
        Interlocked.Exchange(ref ok, 0);
        Interlocked.Exchange(ref errors, 0);
        Console.WriteLine("WARM");
        await Task.Delay(duration);
        var okCount = Interlocked.Read(ref ok);
        var errorCount = Interlocked.Read(ref errors);
        Console.WriteLine($"DONE {okCount}/{errorCount}");
        Volatile.Write(ref stop, true);
        await Task.WhenAll(workers);
        return 0;
    }

    // ───────────────────────────── args ─────────────────────────────

    private static int GetInt(string[] args, string name, int fallback)
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }

    private static TEnum[] GetEnumFilter<TEnum>(string[] args, string name) where TEnum : struct, Enum
    {
        var i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Length)
            return Enum.GetValues<TEnum>();

        return args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => Enum.Parse<TEnum>(s, ignoreCase: true))
            .ToArray();
    }
}
