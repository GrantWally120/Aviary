using System.Collections.Concurrent;
using System.Diagnostics;
using Aviary;

int passed = 0;
void Check(bool ok, string why)
{
    if (!ok) { throw new InvalidOperationException(why); }
    passed++;
    Console.WriteLine("PASS " + why);
}

string Engine(int pid, string adapter, int engine, string type) => $"pid_{pid}_luid_{adapter}_phys_0_eng_{engine}_engtype_{type}";
HashSet<int> included = new() { 10, 20 };
var g = GpuCounters.AggregateEngines(new[]
{
    (Engine(10, "a", 0, "3D"), 40.0),
    (Engine(20, "a", 0, "3D"), 50.0),
    (Engine(10, "a", 1, "Copy"), 60.0),
    (Engine(20, "a", 1, "Copy"), 5.0),
    (Engine(99, "a", 0, "3D"), 99.0),
    (Engine(99, "a", 2, "VideoDecode"), 95.0),
}, included);
Check(g.Combined == 90, "GPU total combines the same physical engine before choosing the busiest");
Check(g.Busy[10] == 60 && g.Busy[20] == 50, "process GPU chooses its busiest engine instead of summing parallel work");
Check(g.ByType["Copy"] == 65 && g.ByType["3D"] == 90, "GPU engine detail includes only Aviary processes");
Check(!g.Busy.ContainsKey(99) && !g.ByType.ContainsKey("VideoDecode"), "unrelated GPU activity never appears in Aviary details");
g = GpuCounters.AggregateEngines(new[]
{
    (Engine(10, "a", 0, "3D"), 40.0),
    (Engine(10, "a", 1, "3D"), 60.0),
    (Engine(20, "b", 0, "3D"), 70.0),
}, included);
Check(g.Combined == 70 && g.ByType["3D"] == 70 && g.Busy[10] == 60, "distinct adapters and same-type engines remain independent");
g = GpuCounters.AggregateEngines(new[]
{
    (Engine(10, "a", 0, "3D"), 80.0),
    (Engine(20, "a", 0, "3D"), 80.0),
    (Engine(10, "a", 1, "Copy"), double.NaN),
    (Engine(10, "a", 2, "VideoDecode"), -5.0),
    ("not_a_process", 90.0),
}, included);
Check(g.Combined == 100 && g.ByType.Count == 1, "GPU caps a physical engine and rejects invalid data");
Check(GpuCounters.AggregateEngines(Array.Empty<(string, double)>(), included).Combined == 0, "valid idle GPU input remains zero");
Check(LiveStats.CpuShare(10_000_000, 10_000_000, 16) == 6.25, "one busy logical processor is 1/16 of the whole CPU");
Check(LiveStats.CpuShare(20_000_000, 20_000_000, 16) == 6.25, "CPU normalization uses the actual two-second interval");
Check(LiveStats.CpuShare(-1, 10, 16) == 0 && LiveStats.CpuShare(1000, 1, 16) == 100, "CPU rejects negative deltas and clamps the physical limit");
Check(LiveStats.CpuShare(1, 0, 16) == 0 && LiveStats.CpuShare(1, 10, 0) == 0, "CPU handles missing intervals and invalid processor counts");

// Fixed-cost requests make every possible snapshot independently checkable. Readers race
// writers at the real window boundary; no request may leave its count and elapsed ticks in
// different windows, and snapshots taken together must retain every completed request.
var blocker = new LiveStats.BlockerWindow();
long observedChecks = 0, observedTicks = 0;
bool consistent = true;
int producersLeft = 8;
using (ManualResetEventSlim go = new(false))
{
    var producers = Enumerable.Range(0, producersLeft).Select(_ => Task.Run(() =>
    {
        go.Wait();
        for (int i = 0; i < 25_000; i++) { blocker.Add(37); }
        Interlocked.Decrement(ref producersLeft);
    })).ToArray();
    go.Set();
    do
    {
        var window = blocker.Take();
        consistent &= window.Ticks == window.Checks * 37 && window.Worst == (window.Checks == 0 ? 0 : 37);
        observedChecks += window.Checks;
        observedTicks += window.Ticks;
        Thread.Yield();
    } while (Volatile.Read(ref producersLeft) != 0);
    Task.WaitAll(producers);
}

var remainder = blocker.Take();
consistent &= remainder.Ticks == remainder.Checks * 37 && remainder.Worst == (remainder.Checks == 0 ? 0 : 37);
observedChecks += remainder.Checks;
observedTicks += remainder.Ticks;
Check(consistent && observedChecks == 200_000 && observedTicks == 200_000 * 37,
    "concurrent Shields snapshots retain every request's count, time and maximum in one window");
Check(blocker.Take() == (0L, 0L, 0L), "Shields snapshot atomically empties the measurement window");

if (OperatingSystem.IsWindows())
{
    ConcurrentQueue<LiveStats.Reading> readings = new();
    using AutoResetEvent changed = new(false);
    void Deliver(LiveStats.Reading reading) { readings.Enqueue(reading); changed.Set(); }
    LiveStats.Reading WaitFor(Func<LiveStats.Reading, bool> predicate, int timeoutMs = 15_000)
    {
        Stopwatch wait = Stopwatch.StartNew();
        while (wait.ElapsedMilliseconds < timeoutMs)
        {
            while (readings.TryDequeue(out var reading))
            {
                if (predicate(reading)) { return reading; }
            }

            changed.WaitOne(100);
        }

        throw new TimeoutException("No matching sampler reading; errors: " + string.Join("; ", Diagnostics.Errors));
    }

    try
    {
        LiveStats.SetProcesses(Array.Empty<LiveStats.Proc>());
        LiveStats.Start("first", Deliver);
        var first = WaitFor(_ => true);
        Check(!first.CpuReady && !first.Processes.Single().CpuReady && first.SampleSeconds == 0,
            "first CPU sample is explicitly warming rather than a fabricated idle reading");
        Check(first.MemoryAvailable && first.Processes.Single().MemoryAvailable && first.MemoryMB > 0,
            "native private working set is available and positive for the live process");
        Check(first.Processes.Single().WorkingSetMB >= first.Processes.Single().MemoryMB && first.Processes.Single().CommitMB > 0,
            "private working set, total working set, and private commit have distinct native readings");
        Check(!first.GpuAvailable, "first GPU rate sample is explicitly warming");
        var warm = WaitFor(r => r.CpuReady);
        Check(warm.SampleSeconds > 0 && warm.SampledAt >= first.SampledAt && warm.ExpectedProcesses == 1,
            "warmed native reading has a real elapsed interval and timestamp");
        LiveStats.SetProcesses(new[] { new LiveStats.Proc(Environment.ProcessId, "Duplicate"), new LiveStats.Proc(Environment.ProcessId, "Duplicate") });
        var duplicate = WaitFor(r => r.SampledAt > warm.SampledAt);
        Check(duplicate.Processes.Count == 1 && duplicate.ExpectedProcesses == 1, "duplicate engine PIDs cannot double-count Aviary itself");
        LiveStats.SetProcesses(new[] { new LiveStats.Proc(int.MaxValue, "Missing") });
        var partial = WaitFor(r => r.ExpectedProcesses == 2);
        Check(!partial.MemoryAvailable && !partial.CpuReady && partial.Processes.Count == 1,
            "an unreadable listed process marks totals incomplete without a fake zero row");
        LiveStats.SetProcesses(Array.Empty<LiveStats.Proc>());
        Aviary.AppSettings.Interval = "Every 2 seconds";
        LiveStats.Retime();
        Check(LiveStats.EveryMs == 2000, "sampler adopts a changed measurement interval immediately");
        var slower = WaitFor(r => r.CpuReady && r.SampleSeconds >= 1.5);
        Check(slower.SampleSeconds >= 1.5, "published interval reflects actual time after retiming");
        LiveStats.Start("second", _ => { });
        LiveStats.Stop("first");
        Check(LiveStats.Watching, "one listener leaving cannot stop another window's sampler");
        LiveStats.Stop("second");
        Check(!LiveStats.Watching, "last listener leaving stops live sampling");
        Aviary.AppSettings.Interval = "Every second";
        for (int i = 0; i < 20; i++)
        {
            LiveStats.Start("stress", _ => { });
            Thread.Sleep(1);
            LiveStats.Stop("stress");
        }

        while (readings.TryDequeue(out _)) { }
        LiveStats.Start("final", Deliver);
        var fresh = WaitFor(_ => true);
        Check(!fresh.CpuReady && fresh.SampleSeconds == 0, "rapid stop/start disposes old state and restarts with a fresh baseline");
        WaitFor(r => r.CpuReady);
        LiveStats.Stop("final");
        Check(Diagnostics.Errors.IsEmpty, "native sampling and rapid lifecycle changes report no exceptions");
    }
    finally
    {
        foreach (string key in new[] { "first", "second", "stress", "final" }) { LiveStats.Stop(key); }
    }
}

Console.WriteLine($"{passed} performance checks passed.");

namespace Aviary
{
    internal static class AppSettings
    {
        public static string Interval = "Every second";
        public static string Get(string key) => Interval;
    }

    internal static class Diagnostics
    {
        public static ConcurrentQueue<string> Errors = new();
        public static void Swallowed(Exception ex, string name) => Errors.Enqueue(name + ": " + ex.Message);
    }
}
