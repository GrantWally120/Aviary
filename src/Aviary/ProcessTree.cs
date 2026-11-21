using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Aviary;

/// <summary>
/// Beta Prep 2 — which processes are REALLY Aviary's, and what each one is (Grant, 21 Nov 2026:
/// make Performance "more accurate").
///
/// <para><b>What was wrong, measured on 21 Nov.</b> Performance took its process list from the
/// engine (WebView2's GetProcessExtendedInfos), and the engine leaves one of its own out: the
/// crash reporter (<c>--type=crashpad-handler</c>), a small process that runs for as long as
/// Aviary does. Windows counted nine processes where the page said "8 of 8", so "All of Aviary"
/// read a few megabytes low. And every helper the engine calls a Utility was drawn as the same
/// "Network, storage and sound", though each is one particular service.</para>
///
/// <para><b>What this does.</b> Walks Windows' own process list (a Toolhelp snapshot) for the
/// engine's processes that descend from Aviary, so nothing of Aviary's is left out — and reads
/// each helper's command line (the engine names the service on it) so a row says "Network
/// service" or "Storage service" and means it. Only <c>msedgewebview2.exe</c> descendants count:
/// a program you opened from the Downloads list is Aviary's child too, and is not Aviary.</para>
///
/// <para>Called when the process list changes, not on every sample: the snapshot and the command
/// lines cost a millisecond or two, and nothing here is recorded.</para>
/// </summary>
internal static class ProcessTree
{
    public const string CrashReporter = "Crashpad";
    public const string EngineHelper = "Engine";

    private const string EngineExe = "msedgewebview2.exe";

    /// <summary>
    /// The engine processes descended from this one that <paramref name="known"/> does not hold,
    /// each with its kind; and a more exact kind for each of <paramref name="utilities"/>.
    /// </summary>
    public static (List<LiveStats.Proc> Missing, Dictionary<int, string> Exact) Inspect(
        IReadOnlyCollection<int> known, IReadOnlyCollection<int> utilities)
    {
        List<LiveStats.Proc> missing = new();
        Dictionary<int, string> exact = new();

        try
        {
            HashSet<int> have = new(known);
            foreach ((int pid, string exe) in Descendants(Environment.ProcessId))
            {
                if (have.Contains(pid) || !exe.Equals(EngineExe, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                missing.Add(new LiveStats.Proc(pid, KindOf(CommandLine(pid))));
            }

            foreach (int pid in utilities)
            {
                string kind = KindOf(CommandLine(pid));
                if (kind.StartsWith("Utility:", StringComparison.Ordinal))
                {
                    exact[pid] = kind;
                }
            }
        }
        catch (Exception ex)
        {
            // The engine's own list still stands; the page only loses the extra precision.
            Diagnostics.Swallowed(ex, "performance: walk Aviary's processes");
        }

        return (missing, exact);
    }

    /// <summary>What an engine process is, from its command line: the engine says so itself.</summary>
    internal static string KindOf(string? commandLine)
    {
        if (string.IsNullOrEmpty(commandLine))
        {
            return EngineHelper;
        }

        string type = Argument(commandLine, "--type=");
        if (type == "crashpad-handler")
        {
            return CrashReporter;
        }

        if (type == "utility")
        {
            string service = Argument(commandLine, "--utility-sub-type=");
            return "Utility:" + (service.Contains("NetworkService", StringComparison.OrdinalIgnoreCase) ? "Network"
                : service.Contains("StorageService", StringComparison.OrdinalIgnoreCase) ? "Storage"
                : service.Contains("AudioService", StringComparison.OrdinalIgnoreCase) ? "Audio"
                : service.Contains("VideoCapture", StringComparison.OrdinalIgnoreCase) ? "Camera"
                : service.Contains("DataDecoder", StringComparison.OrdinalIgnoreCase) ? "Decoder"
                : "Other");
        }

        return type switch
        {
            "renderer" => "Renderer",
            "gpu-process" => "Gpu",
            "" => "Browser",
            _ => EngineHelper,
        };
    }

    /// <summary>The value after <paramref name="name"/> on a command line, up to the next space or quote.</summary>
    private static string Argument(string commandLine, string name)
    {
        int at = commandLine.IndexOf(name, StringComparison.Ordinal);
        if (at < 0)
        {
            return string.Empty;
        }

        at += name.Length;
        int end = commandLine.IndexOfAny(new[] { ' ', '"' }, at);
        return end < 0 ? commandLine.Substring(at) : commandLine.Substring(at, end - at);
    }

    /// <summary>Every process that descends from <paramref name="root"/>, with its program's file name.</summary>
    private static List<(int Pid, string Exe)> Descendants(int root)
    {
        List<(int Pid, int Parent, string Exe)> all = new();
        IntPtr snapshot = CreateToolhelp32Snapshot(SnapProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == InvalidHandle)
        {
            return new List<(int, string)>();
        }

        try
        {
            ProcessEntry entry = new() { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    all.Add(((int)entry.ProcessId, (int)entry.ParentProcessId, entry.ExeFile ?? string.Empty));
                }
                while (Process32NextW(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        // Breadth-first from Aviary. A process id is reused only after its process has gone, and a
        // parent id can outlive its parent, so a child is only followed from a parent found here.
        List<(int Pid, string Exe)> found = new();
        HashSet<int> seen = new() { root };
        Queue<int> queue = new();
        queue.Enqueue(root);
        while (queue.Count > 0 && found.Count < 512)
        {
            int parent = queue.Dequeue();
            foreach ((int pid, int parentId, string exe) in all)
            {
                if (parentId == parent && seen.Add(pid))
                {
                    found.Add((pid, exe));
                    queue.Enqueue(pid);
                }
            }
        }

        return found;
    }

    /// <summary>A process's command line, asked of Windows; null when it cannot be read.</summary>
    private static string? CommandLine(int pid)
    {
        IntPtr process = OpenProcess(QueryLimited, false, pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        IntPtr buffer = IntPtr.Zero;
        try
        {
            // Asked once for the size, then for the text: a UNICODE_STRING followed by its characters.
            NtQueryInformationProcess(process, CommandLineInformation, IntPtr.Zero, 0, out int needed);
            if (needed <= 0 || needed > 1 << 20)
            {
                return null;
            }

            buffer = Marshal.AllocHGlobal(needed);
            if (NtQueryInformationProcess(process, CommandLineInformation, buffer, needed, out _) != 0)
            {
                return null;
            }

            UnicodeString text = Marshal.PtrToStructure<UnicodeString>(buffer);
            return text.Buffer == IntPtr.Zero || text.Length == 0
                ? null
                : Marshal.PtrToStringUni(text.Buffer, text.Length / 2);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "performance: read a process command line");
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }

            CloseHandle(process);
        }
    }

    private const uint SnapProcess = 0x2;
    private const uint QueryLimited = 0x1000;
    private const int CommandLineInformation = 60;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? ExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass, IntPtr buffer, int size, out int returned);
}
