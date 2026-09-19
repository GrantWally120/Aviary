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
/// engine type (Task Manager's column works the same way), summed over that type's engines.
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

    public bool Available { get; }

    public GpuCounters()
    {
        try
        {
            if (PdhOpenQueryW(null, IntPtr.Zero, out _query) != 0)
            {
                return;
            }

            bool engine = PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", IntPtr.Zero, out _engine) == 0;
            PdhAddEnglishCounterW(_query, @"\GPU Process Memory(*)\Dedicated Usage", IntPtr.Zero, out _memory);

            // Rates need two samples; the first is taken now so the page's first second has one.
            PdhCollectQueryData(_query);
            Available = engine;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "performance: open GPU counters");
        }
    }

    /// <summary>One reading: GPU busy % and dedicated memory (bytes), per process id.</summary>
    public (Dictionary<int, double> Busy, Dictionary<int, long> Dedicated) Sample()
    {
        Dictionary<int, double> busy = new();
        Dictionary<int, long> dedicated = new();
        if (!Available)
        {
            return (busy, dedicated);
        }

        try
        {
            if (PdhCollectQueryData(_query) != 0)
            {
                return (busy, dedicated);
            }

            // pid → engine type → summed utilisation of that type's engines.
            Dictionary<int, Dictionary<string, double>> byType = new();
            foreach ((string name, double value) in Read(_engine, PdhFmtDouble | PdhFmtNoCap100))
            {
                if (!TryPid(name, out int pid))
                {
                    continue;
                }

                int at = name.IndexOf("engtype_", StringComparison.Ordinal);
                string type = at >= 0 ? name.Substring(at + 8) : "other";
                if (!byType.TryGetValue(pid, out Dictionary<string, double>? types))
                {
                    byType[pid] = types = new Dictionary<string, double>(StringComparer.Ordinal);
                }

                types[type] = (types.TryGetValue(type, out double sum) ? sum : 0) + value;
            }

            foreach ((int pid, Dictionary<string, double> types) in byType)
            {
                double max = 0;
                foreach (double v in types.Values)
                {
                    max = Math.Max(max, v);
                }

                busy[pid] = Math.Min(100, max);
            }

            if (_memory != IntPtr.Zero)
            {
                foreach ((string name, double value) in Read(_memory, PdhFmtDouble))
                {
                    if (TryPid(name, out int pid))
                    {
                        dedicated[pid] = (dedicated.TryGetValue(pid, out long b) ? b : 0) + (long)value;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "performance: read GPU counters");
        }

        return (busy, dedicated);
    }

    /// <summary>
    /// Also: every process's figures summed by engine type, so the page can say how busy the
    /// graphics card is for Aviary as a whole — the busiest engine type across Aviary's processes.
    /// </summary>
    public static double Combined(IEnumerable<int> pids, Dictionary<int, double> busy) =>
        Math.Min(100, System.Linq.Enumerable.Sum(pids, p => busy.TryGetValue(p, out double v) ? v : 0));

    private static List<(string Name, double Value)> Read(IntPtr counter, uint format)
    {
        List<(string, double)> items = new();
        uint size = 0;
        int status = PdhGetFormattedCounterArrayW(counter, format, ref size, out uint count, IntPtr.Zero);
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

            // PDH_FMT_COUNTERVALUE_ITEM_W: a name pointer, then PDH_FMT_COUNTERVALUE
            // { DWORD CStatus; (4 bytes padding) double }. 24 bytes a row on x64.
            int stride = IntPtr.Size + 16;
            for (int i = 0; i < count; i++)
            {
                IntPtr row = buffer + i * stride;
                string? name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(row));
                uint cstatus = (uint)Marshal.ReadInt32(row + IntPtr.Size);
                if (name is null || cstatus > 1)
                {
                    continue;
                }

                double value = BitConverter.Int64BitsToDouble(Marshal.ReadInt64(row + IntPtr.Size + 8));
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
