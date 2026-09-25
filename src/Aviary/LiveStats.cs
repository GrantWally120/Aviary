using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aviary;

/// <summary>
/// Settings → Performance, live (Alpha Build 5 M4.5): what Aviary is using right now — memory,
/// processor, graphics card — for itself and for every process of its browser engine, once a
/// second.
///
/// <para><b>Only while someone is looking.</b> The sampler starts when the Performance pane is on
/// screen in the visible Settings tab, and stops when it is not. It records nothing: with the
/// diagnostics switch off it still works, and nothing it reads is written anywhere.</para>
///
/// <para><b>The same figures Windows shows.</b> Memory is each process's <i>private working
/// set</i> — Task Manager's "Memory" column — from GetProcessMemoryInfo's
/// PROCESS_MEMORY_COUNTERS_EX2. Processor is the share of the whole processor's time each process
/// used since the last second (GetProcessTimes). Graphics comes from Windows' own GPU counters
/// (<see cref="GpuCounters"/>). The page also shows what measuring costs, so watching cannot hide
/// its own overhead.</para>
///
/// <para>Which process is which comes from the window (on the UI thread, where WebView2 answers):
/// <see cref="SetProcesses"/> is handed the engine's process list each time it changes.</para>
/// </summary>
internal static class LiveStats
{
    /// <summary>One process of Aviary's: its id and what kind it is ("Aviary", "Browser", "Renderer", "Gpu", "Utility"…).</summary>
    public sealed record Proc(int Pid, string Kind);

    /// <summary>One process, measured.</summary>
    public sealed record Figures(int Pid, string Kind, double MemoryMB, double CpuPercent, double GpuPercent, double GpuMemoryMB, int Handles, double CommitMB, double PeakMB);

    /// <summary>One second's reading.</summary>
    public sealed record Reading(
        IReadOnlyList<Figures> Processes,
        double MemoryMB,
        double CpuPercent,
        double GpuPercent,
        double GpuMemoryMB,
        bool GpuAvailable,
        double UiNowMs,
        double UiWorstMs,
        double? BlockerAverageUs,
        double? BlockerWorstUs,
        long BlockerChecks,
        double SamplerMs,

        // M4.6: memory Aviary has ASKED for (commit) and the most it has ever held (peak), and
        // what each kind of GPU engine was doing.
        double CommitMB,
        double PeakMB,
        IReadOnlyDictionary<string, double> GpuByEngine);

    private static readonly object Gate = new();
    private static volatile IReadOnlyList<Proc> _procs = Array.Empty<Proc>();
    private static volatile bool _watching;
    private static Timer? _timer;
    private static GpuCounters? _gpu;
    // M6: more than one may listen - the Performance pane and the performance overlay - to ONE sampler.
    private static readonly Dictionary<string, Action<Reading>> Listeners = new(StringComparer.Ordinal);

    private static readonly Dictionary<int, (IntPtr Handle, long Cpu, long At)> Seen = new();

    public static bool Watching => _watching;

    public static void SetProcesses(IReadOnlyList<Proc> procs) => _procs = procs;

    public static void Start(string who, Action<Reading> deliver)
    {
        lock (Gate)
        {
            Listeners[who] = deliver;
            if (_timer is not null)
            {
                // M6: someone else is already listening (the corner readout keeps the sampler going
                // after the Performance page is left) - so a new listener still gets today's rate.
                RetimeLocked();
                return;
            }

            _watching = true;
            ResetBlocker();
            _uiWorst.Clear();
            _everyMs = SettingMs();
            _timer = new Timer(_ => Tick(), null, 0, _everyMs);
        }
    }

    /// <summary>
    /// The rate actually in use, in milliseconds — what the Performance page says it measures at.
    /// Not the setting: before M6 the page read the setting, and could say "every 5 seconds" while
    /// the sampler went on at one (bug 92).
    /// </summary>
    public static int EveryMs => _everyMs;

    private static int _everyMs = 1000;

    /// <summary>"How often to measure" changed: a running sampler takes the new rate at once.</summary>
    public static void Retime()
    {
        lock (Gate)
        {
            RetimeLocked();
        }
    }

    private static void RetimeLocked()
    {
        int every = SettingMs();
        if (_timer is not null && every != _everyMs)
        {
            _everyMs = every;
            _timer.Change(every, every);
        }
    }

    // M4.6: 1, 2 or 5 seconds, from Settings. Slower costs less and smooths the lines; the page says
    // which rate it is showing and what the sampling itself costs. Processor use is worked out from
    // the real time between two samples (Measure), so the rate can change between them.
    private static int SettingMs() => AppSettings.Get("perfInterval") switch
    {
        "Every 2 seconds" => 2000,
        "Every 5 seconds" => 5000,
        _ => 1000,
    };

    public static void Stop(string who)
    {
        lock (Gate)
        {
            Listeners.Remove(who);
            if (Listeners.Count > 0)
            {
                return;
            }

            _watching = false;
            _timer?.Dispose();
            _timer = null;
            _gpu?.Dispose();
            _gpu = null;
            foreach ((IntPtr handle, _, _) in Seen.Values)
            {
                CloseHandle(handle);
            }

            Seen.Clear();
        }
    }

    // ── What the window reports in ─────────────────────────────────────────────

    private static double _uiNow;
    private static readonly Queue<(long At, double Ms)> _uiWorst = new();

    /// <summary>How long the UI thread took to answer the window's once-a-second ping.</summary>
    public static void NoteUiLatency(double ms)
    {
        lock (_uiWorst)
        {
            _uiNow = ms;
            long now = Environment.TickCount64;
            _uiWorst.Enqueue((now, ms));
            while (_uiWorst.Count > 0 && now - _uiWorst.Peek().At > 60_000)
            {
                _uiWorst.Dequeue();
            }
        }
    }

    private static long _blockerChecks;
    private static long _blockerTicks;
    private static long _blockerMax;

    /// <summary>From every request's blocker check (BrowserTab.Decide) — a volatile read when not watched.</summary>
    public static void NoteBlocker(long ticks)
    {
        if (!_watching)
        {
            return;
        }

        Interlocked.Increment(ref _blockerChecks);
        Interlocked.Add(ref _blockerTicks, ticks);
        long max;
        while (ticks > (max = Interlocked.Read(ref _blockerMax)))
        {
            if (Interlocked.CompareExchange(ref _blockerMax, ticks, max) == max)
            {
                break;
            }
        }
    }

    private static void ResetBlocker()
    {
        Interlocked.Exchange(ref _blockerChecks, 0);
        Interlocked.Exchange(ref _blockerTicks, 0);
        Interlocked.Exchange(ref _blockerMax, 0);
    }

    // ── The sample ──────────────────────────────────────────────────────────────

    private static void Tick()
    {
        long started = Stopwatch.GetTimestamp();
        Action<Reading>[] deliver;
        GpuCounters gpu;
        lock (Gate)
        {
            if (!_watching || Listeners.Count == 0)
            {
                return;
            }

            deliver = Listeners.Values.ToArray();
            // Held here, not re-read: Stop() may dispose the shared one while this sample runs.
            gpu = _gpu ??= new GpuCounters();
        }

        try
        {
            List<Proc> procs = new() { new Proc(Environment.ProcessId, "Aviary") };
            procs.AddRange(_procs.Where(p => p.Pid != Environment.ProcessId));

            (Dictionary<int, double> busy, Dictionary<int, long> dedicated) = gpu.Sample();
            long now = Stopwatch.GetTimestamp();
            List<Figures> rows = new();
            HashSet<int> alive = new();

            foreach (Proc p in procs)
            {
                if (Measure(p.Pid, now) is not { } m)
                {
                    continue;
                }

                alive.Add(p.Pid);
                rows.Add(new Figures(p.Pid, p.Kind,
                    Math.Round(m.PrivateBytes / 1048576.0, 1),
                    Math.Round(m.Cpu, 1),
                    Math.Round(busy.TryGetValue(p.Pid, out double g) ? g : 0, 1),
                    Math.Round((dedicated.TryGetValue(p.Pid, out long d) ? d : 0) / 1048576.0, 1),
                    m.Handles,
                    Math.Round(m.CommitBytes / 1048576.0, 1),
                    Math.Round(m.PeakBytes / 1048576.0, 1)));
            }

            lock (Gate)
            {
                foreach (int gone in Seen.Keys.Where(k => !alive.Contains(k)).ToList())
                {
                    CloseHandle(Seen[gone].Handle);
                    Seen.Remove(gone);
                }
            }

            long checks = Interlocked.Exchange(ref _blockerChecks, 0);
            long ticks = Interlocked.Exchange(ref _blockerTicks, 0);
            long worst = Interlocked.Exchange(ref _blockerMax, 0);
            double Us(long t) => Math.Round(t * 1_000_000.0 / Stopwatch.Frequency, 1);

            double uiNow, uiWorst;
            lock (_uiWorst)
            {
                uiNow = _uiNow;
                uiWorst = _uiWorst.Count == 0 ? 0 : _uiWorst.Max(x => x.Ms);
            }

            Reading reading = new(
                rows,
                Math.Round(rows.Sum(r => r.MemoryMB), 1),
                Math.Round(Math.Min(100, rows.Sum(r => r.CpuPercent)), 1),
                Math.Round(GpuCounters.Combined(rows.Select(r => r.Pid), busy), 1),
                Math.Round(rows.Sum(r => r.GpuMemoryMB), 1),
                gpu.Available,
                Math.Round(uiNow, 1),
                Math.Round(uiWorst, 1),
                checks == 0 ? null : Us(ticks / checks),
                checks == 0 ? null : Us(worst),
                checks,
                Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2),
                Math.Round(rows.Sum(r => r.CommitMB), 1),
                Math.Round(rows.Sum(r => r.PeakMB), 1),
                gpu.ByEngine.ToDictionary(p => p.Key, p => Math.Round(Math.Min(100, p.Value), 1), StringComparer.Ordinal));

            foreach (Action<Reading> d in deliver)
            {
                d(reading);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "performance: sample");
        }
    }

    private readonly record struct Measured(long PrivateBytes, double Cpu, int Handles, long CommitBytes, long PeakBytes);

    private static Measured? Measure(int pid, long now)
    {
        IntPtr handle;
        long lastCpu = -1, lastAt = 0;
        lock (Gate)
        {
            if (Seen.TryGetValue(pid, out (IntPtr Handle, long Cpu, long At) seen))
            {
                handle = seen.Handle;
                lastCpu = seen.Cpu;
                lastAt = seen.At;
            }
            else
            {
                handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
                if (handle == IntPtr.Zero)
                {
                    return null;
                }
            }
        }

        if (!GetProcessTimes(handle, out _, out _, out long kernel, out long user))
        {
            lock (Gate)
            {
                Seen.Remove(pid);
            }

            CloseHandle(handle);
            return null;
        }

        long cpu = kernel + user;
        double percent = 0;
        if (lastCpu >= 0)
        {
            double wall100ns = Stopwatch.GetElapsedTime(lastAt, now).TotalMilliseconds * 10_000;
            if (wall100ns > 0)
            {
                percent = Math.Clamp((cpu - lastCpu) * 100.0 / (wall100ns * Environment.ProcessorCount), 0, 100);
            }
        }

        lock (Gate)
        {
            Seen[pid] = (handle, cpu, now);
        }

        long privateBytes = 0, commit = 0, peak = 0;
        MemoryCountersEx2 counters = new() { cb = (uint)Marshal.SizeOf<MemoryCountersEx2>() };
        if (GetProcessMemoryInfo(handle, ref counters, counters.cb))
        {
            privateBytes = (long)counters.PrivateWorkingSetSize;

            // M4.6: PrivateUsage is what the process has asked Windows to commit - the figure that
            // does not fall when memory is paged out - and PeakWorkingSetSize is the most it ever
            // held at once. Together they say whether a tab has really given memory back.
            commit = (long)counters.PrivateUsage;
            peak = (long)counters.PeakWorkingSetSize;
        }

        GetProcessHandleCount(handle, out uint handles);
        return new Measured(privateBytes, lastCpu >= 0 ? percent : 0, (int)handles, commit, peak);
    }

    // ── Win32 ─────────────────────────────────────────────────────────────────

    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>PROCESS_MEMORY_COUNTERS_EX2 (Windows 10 21H1 and later): PrivateWorkingSetSize is Task Manager's "Memory".</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryCountersEx2
    {
        public uint cb;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
        public UIntPtr QuotaPeakPagedPoolUsage;
        public UIntPtr QuotaPagedPoolUsage;
        public UIntPtr QuotaPeakNonPagedPoolUsage;
        public UIntPtr QuotaNonPagedPoolUsage;
        public UIntPtr PagefileUsage;
        public UIntPtr PeakPagefileUsage;
        public UIntPtr PrivateUsage;
        public UIntPtr PrivateWorkingSetSize;
        public ulong SharedCommitUsage;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCountersEx2 counters, uint size);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessHandleCount(IntPtr process, out uint count);
}
