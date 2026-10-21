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
    public sealed record Figures(int Pid, string Kind, double MemoryMB, double CpuPercent, double GpuPercent, double GpuMemoryMB, int Handles, double CommitMB, double PeakMB,
        double WorkingSetMB, bool MemoryAvailable, bool CpuReady, bool HandlesAvailable);

    /// <summary>One interval's reading. MB-named fields use binary MiB (1,048,576 bytes).</summary>
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

        // Private commit, sum of current processes' separate lifetime total-working-set peaks
        // (not a simultaneous aggregate peak), and what each kind of GPU engine was doing.
        double CommitMB,
        double PeakMB,
        IReadOnlyDictionary<string, double> GpuByEngine,
        DateTimeOffset SampledAt,
        double SampleSeconds,
        bool CpuReady,
        bool MemoryAvailable,
        bool GpuMemoryAvailable,
        int ExpectedProcesses,
        bool UiAvailable);

    private static readonly object Gate = new();
    // A timer may re-enter when collecting GPU counters runs longer than its period. Stop must
    // not close a Win32 handle/PDH query while a sample uses it, or block the UI waiting for PDH.
    private static readonly object SampleGate = new();
    private static int _generation;
    private static int _sampleGeneration = -1;
    private static long _lastSample;
    private static volatile IReadOnlyList<Proc> _procs = Array.Empty<Proc>();
    private static volatile bool _watching;
    private static Timer? _timer;
    private static GpuCounters? _gpu;
    // M6: more than one may listen - the Performance pane and the performance overlay - to ONE sampler.
    private static readonly Dictionary<string, Action<Reading>> Listeners = new(StringComparer.Ordinal);

    private static readonly Dictionary<int, (IntPtr Handle, long Cpu, long At)> Seen = new();

    public static bool Watching => _watching;
    // Environment.ProcessorCount may reflect a process/job affinity limit; the UI promises a
    // share of the whole machine, so normalize against all active Windows processor groups.
    public static int LogicalProcessors { get; } = Math.Max(1, (int)GetActiveProcessorCount(0xffff));

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
            _generation++;
            ResetBlocker();
            lock (_uiWorst)
            {
                _uiWorst.Clear();
                _uiNow = 0;
            }
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
            if (!Listeners.Remove(who))
            {
                return;
            }

            if (Listeners.Count > 0)
            {
                return;
            }

            _watching = false;
            _generation++;
            _timer?.Dispose();
            _timer = null;
            // Dispose on the worker after the last in-flight sample. A new watch may already
            // have started by then; its first Tick resets the previous generation instead.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                lock (SampleGate)
                {
                    lock (Gate)
                    {
                        if (!_watching)
                        {
                            ClearSampleResources();
                        }
                    }
                }
            });
        }
    }

    // SampleGate is held whenever these handles or the GPU query are used or released.
    private static void ClearSampleResources()
    {
        _gpu?.Dispose();
        _gpu = null;
        foreach ((IntPtr handle, _, _) in Seen.Values)
        {
            CloseHandle(handle);
        }

        Seen.Clear();
        _lastSample = 0;
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

    private static readonly BlockerWindow Blocker = new();

    /// <summary>A request's count, elapsed ticks and maximum belong to the same measurement
    /// window. Separate Interlocked exchanges can split one request across two windows.</summary>
    internal sealed class BlockerWindow
    {
        private readonly object _gate = new();
        private long _checks, _ticks, _worst;

        internal void Add(long ticks)
        {
            lock (_gate)
            {
                _checks++;
                _ticks += ticks;
                _worst = Math.Max(_worst, ticks);
            }
        }

        internal (long Checks, long Ticks, long Worst) Take()
        {
            lock (_gate)
            {
                var result = (_checks, _ticks, _worst);
                _checks = _ticks = _worst = 0;
                return result;
            }
        }
    }

    /// <summary>From every request's blocker check (BrowserTab.Decide) — a volatile read when not watched.</summary>
    public static void NoteBlocker(long ticks)
    {
        if (!_watching)
        {
            return;
        }

        Blocker.Add(ticks);
    }

    private static void ResetBlocker()
    {
        _ = Blocker.Take();
    }

    // ── The sample ──────────────────────────────────────────────────────────────

    private static void Tick()
    {
        if (!Monitor.TryEnter(SampleGate))
        {
            return;
        }

        try
        {
            TickLocked();
        }
        finally
        {
            Monitor.Exit(SampleGate);
        }
    }

    private static void TickLocked()
    {
        long started = Stopwatch.GetTimestamp();
        GpuCounters gpu;
        int generation;
        lock (Gate)
        {
            if (!_watching || Listeners.Count == 0)
            {
                return;
            }

            generation = _generation;
            if (_sampleGeneration != generation)
            {
                ClearSampleResources();
                _sampleGeneration = generation;
            }

            // Only this sample (or stopped-generation cleanup) can dispose this query.
        }

        gpu = _gpu ??= new GpuCounters();

        try
        {
            List<Proc> procs = new() { new Proc(Environment.ProcessId, "Aviary") };
            procs.AddRange(_procs.Where(p => p.Pid != Environment.ProcessId).DistinctBy(p => p.Pid));

            (Dictionary<int, double> busy, Dictionary<int, long> dedicated) = gpu.Sample(procs.Select(p => p.Pid));
            long now = Stopwatch.GetTimestamp();
            double sampleSeconds = _lastSample == 0 ? 0 : Stopwatch.GetElapsedTime(_lastSample, now).TotalSeconds;
            _lastSample = now;
            List<Figures> rows = new();
            HashSet<int> alive = new();
            long memoryBytes = 0, commitBytes = 0, peakBytes = 0;
            double totalCpu = 0;

            foreach (Proc p in procs)
            {
                if (Measure(p.Pid, now) is not { } m)
                {
                    continue;
                }

                alive.Add(p.Pid);
                memoryBytes += m.PrivateBytes;
                commitBytes += m.CommitBytes;
                peakBytes += m.PeakBytes;
                totalCpu += m.Cpu;
                rows.Add(new Figures(p.Pid, p.Kind,
                    m.PrivateBytes / 1048576.0,
                    Math.Round(m.Cpu, 1),
                    Math.Round(busy.TryGetValue(p.Pid, out double g) ? g : 0, 1),
                    (dedicated.TryGetValue(p.Pid, out long d) ? d : 0) / 1048576.0,
                    m.Handles,
                    m.CommitBytes / 1048576.0,
                    m.PeakBytes / 1048576.0,
                    m.WorkingSetBytes / 1048576.0,
                    m.MemoryAvailable, m.CpuReady, m.HandlesAvailable));
            }

            lock (Gate)
            {
                foreach (int gone in Seen.Keys.Where(k => !alive.Contains(k)).ToList())
                {
                    CloseHandle(Seen[gone].Handle);
                    Seen.Remove(gone);
                }
            }

            long checks, ticks, worst;
            lock (Gate)
            {
                if (!_watching || generation != _generation)
                {
                    return;
                }

                (checks, ticks, worst) = Blocker.Take();
            }
            double Us(long t) => Math.Round(t * 1_000_000.0 / Stopwatch.Frequency, 1);

            double uiNow, uiWorst;
            bool uiAvailable;
            lock (_uiWorst)
            {
                long cutoff = Environment.TickCount64 - 60_000;
                while (_uiWorst.Count > 0 && _uiWorst.Peek().At < cutoff)
                {
                    _uiWorst.Dequeue();
                }

                uiNow = _uiNow;
                uiAvailable = _uiWorst.Count > 0;
                uiWorst = _uiWorst.Count == 0 ? 0 : _uiWorst.Max(x => x.Ms);
            }

            Reading reading = new(
                rows,
                memoryBytes / 1048576.0,
                Math.Round(Math.Min(100, totalCpu), 1),
                Math.Round(gpu.CombinedPercent, 1),
                dedicated.Values.Sum() / 1048576.0,
                gpu.Available,
                Math.Round(uiNow, 1),
                Math.Round(uiWorst, 1),
                checks == 0 ? null : Math.Round(ticks * 1_000_000.0 / Stopwatch.Frequency / checks, 1),
                checks == 0 ? null : Us(worst),
                checks,
                Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2),
                commitBytes / 1048576.0,
                peakBytes / 1048576.0,
                gpu.ByEngine.ToDictionary(p => p.Key, p => Math.Round(Math.Min(100, p.Value), 1), StringComparer.Ordinal),
                DateTimeOffset.UtcNow, Math.Round(sampleSeconds, 3),
                rows.Count == procs.Count && rows.All(p => p.CpuReady),
                rows.Count == procs.Count && rows.All(p => p.MemoryAvailable),
                gpu.MemoryAvailable, procs.Count, uiAvailable);

            Action<Reading>[] deliver;
            lock (Gate)
            {
                if (!_watching || generation != _generation)
                {
                    return;
                }

                deliver = Listeners.Values.ToArray();
            }
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

    private readonly record struct Measured(long PrivateBytes, double Cpu, int Handles, long CommitBytes, long PeakBytes,
        long WorkingSetBytes, bool MemoryAvailable, bool CpuReady, bool HandlesAvailable);

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

        if (!GetProcessTimes(handle, out _, out long exited, out long kernel, out long user) || exited != 0)
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
                percent = CpuShare(cpu - lastCpu, wall100ns, LogicalProcessors);
            }
        }

        lock (Gate)
        {
            Seen[pid] = (handle, cpu, now);
        }

        long privateBytes = 0, commit = 0, peak = 0, workingSet = 0;
        // Older Windows can succeed with only the EX prefix populated. A zero-initialized
        // EX2 tail then looks like a real 0-byte private working set. Use a sentinel and
        // check the returned layout instead of mistaking API success for EX2 support.
        MemoryCountersEx2 counters = new()
        {
            cb = (uint)Marshal.SizeOf<MemoryCountersEx2>(),
            PrivateWorkingSetSize = UIntPtr.MaxValue,
        };
        bool memoryAvailable = GetProcessMemoryInfo(handle, ref counters, counters.cb)
            && HasPrivateWorkingSet(counters.cb, counters.PrivateWorkingSetSize.ToUInt64(), counters.WorkingSetSize.ToUInt64());
        if (memoryAvailable)
        {
            privateBytes = (long)counters.PrivateWorkingSetSize;

            // M4.6: PrivateUsage is what the process has asked Windows to commit - the figure that
            // does not fall when memory is paged out - and PeakWorkingSetSize is the most it ever
            // held at once. Together they say whether a tab has really given memory back.
            commit = (long)counters.PrivateUsage;
            peak = (long)counters.PeakWorkingSetSize;
            workingSet = (long)counters.WorkingSetSize;
        }

        bool handlesAvailable = GetProcessHandleCount(handle, out uint handles);
        return new Measured(privateBytes, lastCpu >= 0 ? percent : 0, (int)handles, commit, peak,
            workingSet, memoryAvailable, lastCpu >= 0, handlesAvailable);
    }

    // ── Win32 ─────────────────────────────────────────────────────────────────

    private const uint ProcessQueryLimitedInformation = 0x1000;

    internal static double CpuShare(long cpu100ns, double elapsed100ns, int logicalProcessors) =>
        elapsed100ns <= 0 || logicalProcessors <= 0 ? 0 : Math.Clamp(cpu100ns * 100.0 / (elapsed100ns * logicalProcessors), 0, 100);

    internal static bool HasPrivateWorkingSet(uint returnedSize, ulong privateBytes, ulong workingSetBytes) =>
        returnedSize >= (uint)Marshal.SizeOf<MemoryCountersEx2>()
        && privateBytes != UIntPtr.MaxValue.ToUInt64()
        && privateBytes <= workingSetBytes && workingSetBytes <= long.MaxValue;

    /// <summary>PROCESS_MEMORY_COUNTERS_EX2 (Windows 10/11 22H2 with the September 2023 update): PrivateWorkingSetSize is Task Manager's "Memory".</summary>
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
    private static extern uint GetActiveProcessorCount(ushort groupNumber);

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
