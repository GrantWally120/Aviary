using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Aviary;

/// <summary>
/// Per-process GPU figures from Windows' own performance counters — the ones Task Manager reads
/// (Alpha Build 5 M4.5, Settings → Performance):
/// <list type="bullet">
/// <item><c>\GPU Engine(*)\Utilization Percentage</c> — one instance per process, adapter and
/// engine, named <c>pid_1234_luid_…_phys_0_eng_3_engtype_3D</c>.</item>
/// <item><c>\GPU Process Memory(*)\Dedicated Usage</c> — bytes of the graphics card's own memory,
/// per process and adapter.</item>
/// </list>
/// Read through pdh.dll directly, so no package is needed. A process's GPU figure is its busiest
/// physical engine across its adapters. Independent engines are never added together.
/// Only ever used on the Performance page's own background thread, and only while it is watched.
/// </summary>
internal sealed class GpuCounters : IDisposable
{
    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQueryW(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounterW(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArrayW(IntPtr counter, uint format, ref uint bufferSize, out uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr query);

    private const uint PdhFmtDouble = 0x00000200;
    private const uint PdhFmtNoCap100 = 0x00008000;
    private const int PdhMoreData = unchecked((int)0x800007D2);

    private IntPtr _query;
    private readonly IntPtr _engine;
    private readonly IntPtr _memory;

    public bool Available { get; private set; }
    public bool MemoryAvailable { get; private set; }
    private bool _sampled;

    public GpuCounters()
    {
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0)
            {
                return;
            }

            bool engine = PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _engine) == 0;
            if (PdhAddEnglishCounterW(_query, @"\GPU Process Memory(*)\Dedicated Usage", IntPtr.Zero, out _memory) != 0)
            {
                _memory = IntPtr.Zero;
            }

            // Rates need two samples; the first is taken now so the page's first second has one.
            PdhCollectQueryData(_query);
            if (!engine)
            {
                _engine = IntPtr.Zero;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "performance: open GPU counters");
        }
    }

    /// <summary>One reading: GPU busy % and dedicated memory (bytes), per process id.</summary>
    /// <summary>
    /// What each kind of GPU engine was doing for Aviary's processes, as a share of its busiest
    /// physical engine: "3D", "VideoDecode", "Copy", "Compute". Filled by the
    /// last <see cref="Sample"/>; M4.6 shows it under the headline figure.
    /// </summary>
    public IReadOnlyDictionary<string, double> ByEngine => _byEngine;

    private Dictionary<string, double> _byEngine = new(StringComparer.Ordinal);
    public double CombinedPercent { get; private set; }

    public (Dictionary<int, double> Busy, Dictionary<int, long> Dedicated) Sample(IEnumerable<int> processIds)
    {
        HashSet<int> included = new(processIds);
        Dictionary<int, double> busy = new();
        Dictionary<int, long> dedicated = new();
        Available = false;
        MemoryAvailable = false;
        CombinedPercent = 0;
        _byEngine = new(StringComparer.Ordinal);
        if (_query == IntPtr.Zero)
        {
            return (busy, dedicated);
        }

        try
        {
            if (PdhCollectQueryData(_query) != 0)
            {
                return (busy, dedicated);
            }

            bool warm = _sampled;
            _sampled = true;
            List<(string Name, double Value)> engineRows = Read(_engine, PdhFmtDouble | PdhFmtNoCap100, out bool engineRead, out HashSet<int> invalidEnginePids);
            Available = warm && engineRead && !invalidEnginePids.Overlaps(included);
            (busy, _byEngine, CombinedPercent) = AggregateEngines(engineRows, included);

            if (_memory != IntPtr.Zero)
            {
                List<(string Name, double Value)> memoryRows = Read(_memory, PdhFmtDouble, out bool memoryRead, out HashSet<int> invalidMemoryPids);
                MemoryAvailable = memoryRead && !invalidMemoryPids.Overlaps(included);
                foreach ((string name, double value) in memoryRows)
                {
                    if (TryPid(name, out int pid) && included.Contains(pid) && double.IsFinite(value) && value >= 0)
                    {
                        dedicated[pid] = (dedicated.TryGetValue(pid, out long b) ? b : 0) + (long)value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Available = false;
            MemoryAvailable = false;
            Diagnostics.Swallowed(ex, "performance: read GPU counters");
        }

        return (busy, dedicated);
    }

    /// <summary>Combine only Aviary's processes on the same physical engine before taking the
    /// busiest engine. The type detail is the busiest engine of that type, across adapters.</summary>
    internal static (Dictionary<int, double> Busy, Dictionary<string, double> ByType, double Combined) AggregateEngines(
        IEnumerable<(string Name, double Value)> rows, ISet<int> included)
    {
        Dictionary<int, double> busy = new();
        Dictionary<string, double> physicalEngines = new(StringComparer.Ordinal);
        Dictionary<string, string> engineTypes = new(StringComparer.Ordinal);
        foreach ((string name, double value) in rows)
        {
            if (!TryPid(name, out int pid) || !included.Contains(pid) || !double.IsFinite(value) || value < 0)
            {
                continue;
            }

            int at = name.IndexOf("engtype_", StringComparison.Ordinal);
            string type = at >= 0 ? name.Substring(at + 8) : "other";
            string engine = name.Substring(name.IndexOf('_', 4) + 1);
            physicalEngines[engine] = (physicalEngines.TryGetValue(engine, out double sum) ? sum : 0) + value;
            engineTypes[engine] = type;
            busy[pid] = Math.Max(busy.TryGetValue(pid, out double prior) ? prior : 0, Math.Min(100, value));
        }

        Dictionary<string, double> byType = new(StringComparer.Ordinal);
        double combined = 0;
        foreach ((string engine, double value) in physicalEngines)
        {
            string type = engineTypes[engine];
            double capped = Math.Min(100, value);
            byType[type] = Math.Max(byType.TryGetValue(type, out double had) ? had : 0, capped);
            combined = Math.Max(combined, capped);
        }

        return (busy, byType, combined);
    }

    private static List<(string Name, double Value)> Read(IntPtr counter, uint format, out bool available, out HashSet<int> invalidPids)
    {
        available = false;
        invalidPids = new();
        List<(string, double)> items = new();
        if (counter == IntPtr.Zero)
        {
            return items;
        }
        uint size = 0;
        int status = PdhGetFormattedCounterArrayW(counter, format, ref size, out uint count, IntPtr.Zero);
        if (status == 0 && size == 0)
        {
            available = count == 0;
            return items;
        }

        if (status != PdhMoreData || size == 0)
        {
            return items;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (PdhGetFormattedCounterArrayW(counter, format, ref size, out count, buffer) != 0)
            {
                return items;
            }

            available = count == 0;

            // PDH_FMT_COUNTERVALUE_ITEM_W: a name pointer, then PDH_FMT_COUNTERVALUE
            // { DWORD CStatus; (4 bytes padding) double }. 24 bytes a row on x64.
            int stride = IntPtr.Size + 16;
            for (int i = 0; i < count; i++)
            {
                IntPtr row = buffer + i * stride;
                string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(row));
                uint cstatus = (uint)Marshal.ReadInt32(row + IntPtr.Size);
                if (name is null)
                {
                    continue;
                }

                double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(row + IntPtr.Size + 8));
                if (cstatus > 1 || !double.IsFinite(value) || value < 0)
                {
                    if (TryPid(name, out int pid))
                    {
                        invalidPids.Add(pid);
                    }

                    continue;
                }

                available = true;
                items.Add((name, value));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return items;
    }

    /// <summary>"pid_1234_luid_…" → 1234.</summary>
    private static bool TryPid(string instance, out int pid)
    {
        pid = 0;
        if (!instance.StartsWith("pid_", StringComparison.Ordinal))
        {
            return false;
        }

        int end = instance.IndexOf('_', 4);
        return end > 4 && int.TryParse(instance.AsSpan(4, end - 4), NumberStyles.None, CultureInfo.InvariantCulture, out pid);
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero)
        {
            PdhCloseQuery(_query);
            _query = IntPtr.Zero;
        }
    }
}
