using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Aviary;

/// <summary>
/// Native crashes, recorded (bug 50, 14 Sep 2026).
///
/// A C++ exception that nothing catches inside WinUI or the engine — minimising the window while
/// its first tab builds is one — ends in the C runtime's abort(): a fail-fast (0xc0000409 in
/// ucrtbase.dll, at abort+0x4e) that no .NET handler ever sees. Aviary simply vanished, with no
/// crash report and nothing in crash.log; Windows' own event log was the only witness.
///
/// abort() calls a SIGABRT handler first if one is set, so Aviary sets one. On the dying thread,
/// moments before the fail-fast, it records:
///  - the native stack, as module+offset — addresses only, never memory contents, so no secrets;
///  - the C++ exception being handled, found on that same stack: its type and, for the two kinds
///    WinUI and C++/WinRT throw, its HRESULT, message, source file and line;
///  - the managed stack, when Aviary's own code is on that thread.
/// It goes to crash.log (always — knowing that Aviary crashed never depends on opting in) and to
/// crashes/ when collection is on. Then the handler returns and abort() ends the process exactly
/// as it would have: this changes what is known about a crash, never what happens.
///
/// No unsafe code: every read is a Marshal.Read* of memory first proven readable.
/// </summary>
internal static class NativeCrash
{
    private const int SIGABRT = 22;
    private static readonly IntPtr SigErr = new(-1);

    private const int ContextSize = 0x4D0;  // x64 CONTEXT
    private const int ContextRsp = 0x98;
    private const int ContextRip = 0xF8;
    private const uint CppExceptionCode = 0xE06D7363;  // 'msc' | 0xE0000000
    private const uint WinRtDebugMagic = 0xAABBCCDD;   // winrt::hresult_error marks its HRESULT with this

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void SignalHandler(int signal);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetErrorDetails(IntPtr self, out IntPtr description, out int error,
        out IntPtr restrictedDescription, out IntPtr capabilitySid);

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr signal(int sig, IntPtr handler);

    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "abort")]
    private static extern void CrtAbort();

    [DllImport("kernel32.dll")]
    private static extern IntPtr RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, IntPtr historyTable);

    [DllImport("kernel32.dll")]
    private static extern IntPtr RtlVirtualUnwind(uint handlerType, ulong imageBase, ulong controlPc,
        IntPtr functionEntry, IntPtr context, out IntPtr handlerData, out ulong establisherFrame, IntPtr contextPointers);

    [DllImport("kernel32.dll")]
    private static extern void RtlCaptureContext(IntPtr context);

    [DllImport("kernel32.dll")]
    private static extern void GetCurrentThreadStackLimits(out IntPtr low, out IntPtr high);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetModuleHandleExW(uint flags, IntPtr address, out IntPtr module);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetModuleFileNameW(IntPtr module, [Out] char[] name, uint size);

    [DllImport("kernel32.dll")]
    private static extern UIntPtr VirtualQuery(IntPtr address, out MemoryInfo info, UIntPtr length);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryInfo
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    private const uint FromAddress = 0x4, UnchangedRefCount = 0x2;
    private const uint MemCommit = 0x1000;
    private const uint PageGuard = 0x100, PageNoAccess = 0x01;

    /// <summary>Kept for the life of the process: the C runtime holds a pointer to it.</summary>
    private static readonly SignalHandler Handler = OnAbort;

    private static IntPtr _handlerPointer;
    private static IntPtr _previous;
    private static int _uiThread = -1;
    private static int _entered;

    /// <summary>Why the handler could not be set, for diagnostics to report once it runs.</summary>
    internal static Exception? InstallFailure { get; private set; }

    /// <summary>First thing in Main, on the thread that becomes the UI thread.</summary>
    internal static void Install()
    {
        try
        {
            _uiThread = Environment.CurrentManagedThreadId;
            _handlerPointer = Marshal.GetFunctionPointerForDelegate(Handler);
            IntPtr previous = signal(SIGABRT, _handlerPointer);
            if (previous == SigErr)
            {
                throw new InvalidOperationException("signal(SIGABRT) refused the handler.");
            }

            _previous = previous;
        }
        catch (Exception ex)
        {
            InstallFailure = ex;
        }
    }

    /// <summary>
    /// After the first tab: the engine's own code has loaded by then. If anything in the process
    /// replaced Aviary's handler, take it back and hand on to theirs afterwards, and say so.
    /// </summary>
    internal static void CheckStillInstalled()
    {
        if (_handlerPointer == IntPtr.Zero)
        {
            return;
        }

        try
        {
            IntPtr current = signal(SIGABRT, _handlerPointer);
            if (current != _handlerPointer && current != SigErr)
            {
                _previous = current;
                Diagnostics.Anomaly("native-crash-handler", "something replaced Aviary's SIGABRT handler",
                    new { replacedBy = Where(current) });
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "native crash: check handler");
        }
    }

    /// <summary>AVIARY_CRASH_TEST=native only (MainWindow.ArmCrashTest): a real abort().</summary>
    internal static void AbortForTest() => CrtAbort();

    private static void OnAbort(int signalNumber)
    {
        // raise() has already put SIGABRT back to the default, so a second abort from inside
        // here ends the process at once; this only keeps two threads from both writing.
        if (Interlocked.Exchange(ref _entered, 1) == 1)
        {
            return;
        }

        try
        {
            Record();
        }
        catch (Exception)
        {
            // Nothing left to record it in. abort() carries on either way.
        }

        IntPtr previous = _previous;
        if (previous != IntPtr.Zero && previous != new IntPtr(1) && previous != _handlerPointer)
        {
            try
            {
                Marshal.GetDelegateForFunctionPointer<SignalHandler>(previous)(signalNumber);
            }
            catch (Exception)
            {
                // Theirs failed; abort() still ends the process.
            }
        }
    }

    private static void Record()
    {
        List<string> stack = NativeStack();
        List<object> exceptions = CppExceptions(out string type, out string message);

        int thread = Environment.CurrentManagedThreadId;
        string threadName = thread == _uiThread ? "UI thread" : (Thread.CurrentThread.Name ?? "thread " + thread);

        string managed = ManagedFramesBelow(Environment.StackTrace);

        if (exceptions.Count == 0)
        {
            type = "abort()";
            message = "The C runtime's abort() was called with no C++ exception in flight.";
        }

        var details = new
        {
            kind = "native abort",
            what = "A C++ exception nothing caught inside WinUI or the engine (or a direct abort()). Before bug 50's fix this left no trace at all.",
            thread = threadName,
            nativeThreadId = GetCurrentThreadId(),
            cppExceptions = exceptions,
            nativeStack = stack,
            managedStack = managed,
        };

        const string Source = "native abort (Aviary ended)";
        Diagnostics.CrashNative(Source, details, type, message);

        StringBuilder log = new();
        log.AppendLine(DateTimeOffset.Now.ToString("u", CultureInfo.InvariantCulture));
        log.AppendLine("NATIVE ABORT on the " + threadName + " — " + type + ": " + message);
        foreach (object e in exceptions)
        {
            log.AppendLine("  exception: " + System.Text.Json.JsonSerializer.Serialize(e));
        }

        log.AppendLine("  native stack:");
        foreach (string frame in stack)
        {
            log.AppendLine("    " + frame);
        }

        if (managed.Length > 0)
        {
            log.AppendLine("  managed stack:");
            log.AppendLine(managed);
        }

        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            File.AppendAllText(Path.Combine(AppPaths.Root, "crash.log"), log + Environment.NewLine);
        }
        catch (Exception)
        {
            // Diagnostics' copy, if collection is on, is all there is.
        }
    }

    /// <summary>The managed frames that led into native code, without this class's own.</summary>
    private static string ManagedFramesBelow(string trace)
    {
        StringBuilder kept = new();
        foreach (string line in trace.Split('\n'))
        {
            string frame = line.TrimEnd('\r');
            if (frame.Contains("Aviary.NativeCrash.", StringComparison.Ordinal)
                || frame.Contains("System.Environment.get_StackTrace", StringComparison.Ordinal)
                || frame.Contains("ILStubClass", StringComparison.Ordinal)
                || frame.Trim().Length == 0)
            {
                continue;
            }

            kept.AppendLine(frame);
        }

        return kept.ToString().TrimEnd();
    }

    // ══ THE NATIVE STACK ════════════════════════════════════════════════════

    /// <summary>
    /// Walked by hand: RtlCaptureStackBackTrace stops at the first frame of .NET code (it does
    /// not consult the function tables the JIT registers — measured: one frame, 14 Sep), while
    /// RtlLookupFunctionEntry does, so this walks through Aviary's own frames and the exception
    /// dispatcher's alike. Runs of .NET frames are shown as one line; the managed stack says
    /// what they were.
    /// </summary>
    private static List<string> NativeStack()
    {
        List<string> described = new();
        IntPtr buffer = Marshal.AllocHGlobal(ContextSize + 16);
        try
        {
            IntPtr context = new((buffer.ToInt64() + 15) & ~15L);
            for (int i = 0; i < ContextSize; i += 8)
            {
                Marshal.WriteInt64(context, i, 0);
            }

            RtlCaptureContext(context);
            GetCurrentThreadStackLimits(out IntPtr low, out IntPtr high);

            int jitRun = 0;
            long lastSp = 0;
            for (int frame = 0; frame < 160; frame++)
            {
                long rip = Marshal.ReadInt64(context, ContextRip);
                long rsp = Marshal.ReadInt64(context, ContextRsp);
                if (rip == 0 || rsp < low.ToInt64() || rsp >= high.ToInt64() || (frame > 0 && rsp < lastSp))
                {
                    break;
                }

                lastSp = rsp;
                string where = Where(new IntPtr(rip));
                if (where.StartsWith("jit ", StringComparison.Ordinal))
                {
                    jitRun++;
                }
                else
                {
                    if (jitRun > 0)
                    {
                        described.Add("[.NET code ×" + jitRun + "]");
                        jitRun = 0;
                    }

                    described.Add(where);
                }

                IntPtr entry = RtlLookupFunctionEntry((ulong)rip, out ulong imageBase, IntPtr.Zero);
                if (entry == IntPtr.Zero)
                {
                    // A leaf function: its return address is on top of the stack.
                    if (!Readable(new IntPtr(rsp), 8))
                    {
                        break;
                    }

                    Marshal.WriteInt64(context, ContextRip, Marshal.ReadInt64(new IntPtr(rsp)));
                    Marshal.WriteInt64(context, ContextRsp, rsp + 8);
                }
                else
                {
                    RtlVirtualUnwind(0, imageBase, (ulong)rip, entry, context, out _, out _, IntPtr.Zero);
                }
            }

            if (jitRun > 0)
            {
                described.Add("[.NET code ×" + jitRun + "]");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return described;
    }

    /// <summary>"Module.dll+0x1234", with the nearest export when one is close; "jit 0x…" for .NET code.</summary>
    private static string Where(IntPtr address)
    {
        if (!GetModuleHandleExW(FromAddress | UnchangedRefCount, address, out IntPtr module) || module == IntPtr.Zero)
        {
            return "jit 0x" + address.ToString("x", CultureInfo.InvariantCulture);
        }

        string name = ModuleName(module);
        long offset = address.ToInt64() - module.ToInt64();
        string where = name + "+0x" + offset.ToString("x", CultureInfo.InvariantCulture);

        (string Export, long Distance)? near = NearestExport(module, offset);
        if (near is { } n && n.Distance <= 0x400)
        {
            where += "  (" + n.Export + "+0x" + n.Distance.ToString("x", CultureInfo.InvariantCulture) + ")";
        }

        return where;
    }

    private static string ModuleName(IntPtr module)
    {
        char[] buffer = new char[520];
        uint length = GetModuleFileNameW(module, buffer, (uint)buffer.Length);
        return length == 0 ? "module@0x" + module.ToString("x", CultureInfo.InvariantCulture) : Path.GetFileName(new string(buffer, 0, (int)length));
    }

    private static readonly Dictionary<IntPtr, List<(long Rva, string Name)>> ExportCache = new();

    /// <summary>
    /// The exported function at or just below <paramref name="rva"/>, read from the module as it
    /// is mapped (sections sit at their RVAs, so no file offsets). Only a hint: for a module whose
    /// own functions are not exported (WinUI), the nearest export is usually far away and the
    /// caller leaves it out.
    /// </summary>
    private static (string Export, long Distance)? NearestExport(IntPtr module, long rva)
    {
        try
        {
            if (!ExportCache.TryGetValue(module, out List<(long Rva, string Name)>? exports))
            {
                exports = ReadExports(module);
                ExportCache[module] = exports;
            }

            (long Rva, string Name) best = default;
            foreach ((long Rva, string Name) e in exports)
            {
                if (e.Rva <= rva && e.Rva > best.Rva)
                {
                    best = e;
                }
            }

            return best.Name is null ? null : (best.Name, rva - best.Rva);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<(long Rva, string Name)> ReadExports(IntPtr module)
    {
        List<(long, string)> list = new();
        if (!Readable(module, 0x40))
        {
            return list;
        }

        int peOffset = Marshal.ReadInt32(module, 0x3c);
        IntPtr pe = module + peOffset;
        if (!Readable(pe, 0x100) || Marshal.ReadInt32(pe) != 0x4550 || Marshal.ReadInt16(pe, 24) != 0x20b)
        {
            return list;
        }

        int exportRva = Marshal.ReadInt32(pe, 24 + 112);
        if (exportRva == 0)
        {
            return list;
        }

        IntPtr dir = module + exportRva;
        if (!Readable(dir, 40))
        {
            return list;
        }

        int names = Marshal.ReadInt32(dir, 24);
        IntPtr functions = module + Marshal.ReadInt32(dir, 28);
        IntPtr nameTable = module + Marshal.ReadInt32(dir, 32);
        IntPtr ordinals = module + Marshal.ReadInt32(dir, 36);

        for (int i = 0; i < names; i++)
        {
            int nameRva = Marshal.ReadInt32(nameTable, 4 * i);
            int ordinal = (ushort)Marshal.ReadInt16(ordinals, 2 * i);
            int functionRva = Marshal.ReadInt32(functions, 4 * ordinal);
            string? name = Marshal.PtrToStringAnsi(module + nameRva);
            if (name is not null)
            {
                list.Add((functionRva, name));
            }
        }

        return list;
    }

    // ══ THE C++ EXCEPTION, FOUND ON THE STACK ════════════════════════════════

    /// <summary>
    /// WinUI links its C++ runtime statically, so the exception being handled cannot be asked for
    /// through vcruntime's exports. But on x64 a catch block — and the terminate() it leads to —
    /// runs with the exception's dispatch frames still on the stack, and those hold its
    /// EXCEPTION_RECORD: code 0xE06D7363, four parameters, the first the C++ EH magic number,
    /// then the thrown object, its ThrowInfo and the image base the ThrowInfo's RVAs are
    /// relative to. Only live stack — from the current stack pointer up — is searched.
    /// </summary>
    private static List<object> CppExceptions(out string type, out string message)
    {
        type = string.Empty;
        message = string.Empty;
        List<object> found = new();
        HashSet<(long, long)> seen = new();

        GetCurrentThreadStackLimits(out IntPtr low, out IntPtr high);
        long sp = Math.Max(StackPointer().ToInt64(), low.ToInt64());
        long end = high.ToInt64();

        // Region by region: the live stack is committed from the stack pointer up, but a region
        // that is not readable is stepped over rather than read.
        long regionStart = (sp + 7) & ~7L;
        while (regionStart < end)
        {
            long regionEnd = Math.Min(NextRegion(regionStart), end);
            if (Readable(new IntPtr(regionStart), 8))
            {
                ScanForRecords(regionStart, regionEnd, found, seen, ref type, ref message);
            }

            regionStart = regionEnd;
        }

        return found;
    }

    private static void ScanForRecords(long from, long to, List<object> found, HashSet<(long, long)> seen,
        ref string type, ref string message)
    {
        for (long a = from; a + 0x40 <= to; a += 8)
        {
            IntPtr record = new(a);
            if ((uint)Marshal.ReadInt32(record) != CppExceptionCode
                || (Marshal.ReadInt32(record, 4) & 1) == 0
                || Marshal.ReadInt32(record, 0x18) != 4)
            {
                continue;
            }

            long magic = Marshal.ReadInt64(record, 0x20);
            if (magic is not (0x19930520 or 0x19930521 or 0x19930522 or 0x01994000))
            {
                continue;
            }

            IntPtr thrown = Marshal.ReadIntPtr(record, 0x28);
            IntPtr throwInfo = Marshal.ReadIntPtr(record, 0x30);
            IntPtr imageBase = Marshal.ReadIntPtr(record, 0x38);
            if (!seen.Add((thrown.ToInt64(), throwInfo.ToInt64())))
            {
                continue;
            }

            object described = DescribeCppException(thrown, throwInfo, imageBase, out string oneType, out string oneMessage);
            found.Add(described);

            if (type.Length == 0)
            {
                // The innermost — the one terminate() is about.
                type = oneType;
                message = oneMessage;
            }
        }
    }

    private static object DescribeCppException(IntPtr thrown, IntPtr throwInfo, IntPtr imageBase,
        out string type, out string message)
    {
        List<string> types = CatchableTypes(throwInfo, imageBase);
        type = types.Count > 0 ? types[0] : "C++ exception";
        message = string.Empty;

        string thrower = GetModuleHandleExW(FromAddress | UnchangedRefCount, throwInfo, out IntPtr module) && module != IntPtr.Zero
            ? ModuleName(module)
            : "?";

        string? hresult = null, file = null, function = null, code = null, text = null;
        int? line = null;

        if (Readable(thrown, 0x100))
        {
            if (types.Any(t => t.Contains("hresult_error", StringComparison.Ordinal)))
            {
                ReadWinRtError(thrown, out hresult, out text);
            }
            else if (types.Any(t => t.Contains("ResultException", StringComparison.Ordinal) && t.Contains("wil", StringComparison.Ordinal)))
            {
                ReadWilFailure(thrown, out hresult, out file, out line, out function, out code, out text);
            }
        }

        message = (hresult is null ? string.Empty : hresult + " ") + (text ?? string.Empty);
        if (file is not null)
        {
            message += " (" + file + ":" + line + ")";
        }

        message = message.Trim();

        return new
        {
            type,
            types,
            thrownBy = thrower,
            hresult,
            message = text,
            file,
            line,
            function,
            code,
            objectHead = Environment.GetEnvironmentVariable("AVIARY_TEST_NATIVE_DETAIL") == "1" ? Hex(thrown, 0x80) : null,
        };
    }

    /// <summary>The thrown type and every type it can be caught as, most derived first, demangled.</summary>
    private static List<string> CatchableTypes(IntPtr throwInfo, IntPtr imageBase)
    {
        List<string> names = new();
        if (!Readable(throwInfo, 16) || !Readable(imageBase, 0x40))
        {
            return names;
        }

        IntPtr array = imageBase + Marshal.ReadInt32(throwInfo, 12);
        if (!Readable(array, 4))
        {
            return names;
        }

        int count = Math.Min(Marshal.ReadInt32(array), 16);
        for (int i = 0; i < count; i++)
        {
            IntPtr catchable = imageBase + Marshal.ReadInt32(array, 4 + 4 * i);
            if (!Readable(catchable, 8))
            {
                break;
            }

            IntPtr descriptor = imageBase + Marshal.ReadInt32(catchable, 4);
            if (!Readable(descriptor, 24))
            {
                break;
            }

            string? decorated = Marshal.PtrToStringAnsi(descriptor + 16);
            if (!string.IsNullOrEmpty(decorated))
            {
                names.Add(Demangle(decorated));
            }
        }

        return names;
    }

    /// <summary>".?AUhresult_error@winrt@@" → "winrt::hresult_error". Templates stay decorated.</summary>
    private static string Demangle(string decorated)
    {
        if (!decorated.StartsWith(".?A", StringComparison.Ordinal) || decorated.Length < 5 || decorated.Contains("?$", StringComparison.Ordinal))
        {
            return decorated;
        }

        string body = decorated.Substring(4).TrimEnd('@');
        string[] parts = body.Split('@', StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(parts);
        return string.Join("::", parts);
    }

    /// <summary>
    /// C++/WinRT's hresult_error keeps its HRESULT right after a marker, 0xAABBCCDD, put there
    /// for debuggers; after the HRESULT comes its IRestrictedErrorInfo, which holds the message.
    /// </summary>
    private static void ReadWinRtError(IntPtr thrown, out string? hresult, out string? text)
    {
        hresult = null;
        text = null;

        for (int offset = 0; offset + 16 <= 0x40; offset += 4)
        {
            if ((uint)Marshal.ReadInt32(thrown, offset) != WinRtDebugMagic)
            {
                continue;
            }

            int hr = Marshal.ReadInt32(thrown, offset + 4);
            hresult = "0x" + hr.ToString("X8", CultureInfo.InvariantCulture);

            int infoOffset = (offset + 8 + 7) & ~7;
            IntPtr info = Marshal.ReadIntPtr(thrown, infoOffset);
            text = RestrictedErrorText(info);
            return;
        }
    }

    private static string? RestrictedErrorText(IntPtr info)
    {
        if (info == IntPtr.Zero || !Readable(info, 8))
        {
            return null;
        }

        IntPtr vtable = Marshal.ReadIntPtr(info);
        if (!Readable(vtable, 5 * IntPtr.Size))
        {
            return null;
        }

        IntPtr getDetails = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        if (!GetModuleHandleExW(FromAddress | UnchangedRefCount, getDetails, out IntPtr module) || module == IntPtr.Zero)
        {
            return null;
        }

        int result = Marshal.GetDelegateForFunctionPointer<GetErrorDetails>(getDetails)(
            info, out IntPtr description, out int _, out IntPtr restricted, out IntPtr capability);
        try
        {
            if (result < 0)
            {
                return null;
            }

            string? plain = description == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(description);
            string? detail = restricted == IntPtr.Zero ? null : Marshal.PtrToStringBSTR(restricted);
            return string.Join(" — ", new[] { plain?.Trim(), detail?.Trim() }
                .Where(s => !string.IsNullOrEmpty(s)).Distinct());
        }
        finally
        {
            if (description != IntPtr.Zero) Marshal.FreeBSTR(description);
            if (restricted != IntPtr.Zero) Marshal.FreeBSTR(restricted);
            if (capability != IntPtr.Zero) Marshal.FreeBSTR(capability);
        }
    }

    /// <summary>
    /// WIL's ResultException: std::exception's 24 bytes, then a FailureInfo — type, flags, HRESULT,
    /// status, id, message, thread, then the code, function and file strings and the line. Every
    /// pointer is checked before it is followed; a string that does not look like one is dropped.
    /// </summary>
    private static void ReadWilFailure(IntPtr thrown, out string? hresult, out string? file, out int? line,
        out string? function, out string? code, out string? text)
    {
        const int Info = 24;
        int hr = Marshal.ReadInt32(thrown, Info + 8);
        hresult = "0x" + hr.ToString("X8", CultureInfo.InvariantCulture);
        text = WideText(Marshal.ReadIntPtr(thrown, Info + 24));
        code = AnsiInModule(Marshal.ReadIntPtr(thrown, Info + 40));
        function = AnsiInModule(Marshal.ReadIntPtr(thrown, Info + 48));
        file = AnsiInModule(Marshal.ReadIntPtr(thrown, Info + 56));
        line = file is null ? null : Marshal.ReadInt32(thrown, Info + 64);
    }

    private static string? AnsiInModule(IntPtr text)
    {
        if (text == IntPtr.Zero
            || !GetModuleHandleExW(FromAddress | UnchangedRefCount, text, out IntPtr module) || module == IntPtr.Zero
            || !Readable(text, 1))
        {
            return null;
        }

        string? s = Marshal.PtrToStringAnsi(text);
        return s is { Length: > 0 and < 400 } && s.All(c => c >= ' ' && c < 0x7f) ? s : null;
    }

    private static string? WideText(IntPtr text)
    {
        if (text == IntPtr.Zero || !Readable(text, 2))
        {
            return null;
        }

        string? s = Marshal.PtrToStringUni(text);
        return s is { Length: > 0 and < 2000 } && s.All(c => c >= ' ' || c is '\r' or '\n' or '\t') ? s.Trim() : null;
    }

    // ══ MEMORY, CAREFULLY ═══════════════════════════════════════════════════

    private static IntPtr StackPointer()
    {
        IntPtr buffer = Marshal.AllocHGlobal(ContextSize + 16);
        try
        {
            IntPtr context = new((buffer.ToInt64() + 15) & ~15L);
            for (int i = 0; i < ContextSize; i += 8)
            {
                Marshal.WriteInt64(context, i, 0);
            }

            RtlCaptureContext(context);
            return Marshal.ReadIntPtr(context, ContextRsp);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>True when [address, address+length) is committed, readable, and not a guard page.</summary>
    private static bool Readable(IntPtr address, int length)
    {
        long at = address.ToInt64();
        long end = at + length;
        if (at <= 0x10000)
        {
            return false;
        }

        while (at < end)
        {
            if (VirtualQuery(new IntPtr(at), out MemoryInfo info, (UIntPtr)Marshal.SizeOf<MemoryInfo>()) == UIntPtr.Zero
                || info.State != MemCommit
                || (info.Protect & (PageGuard | PageNoAccess)) != 0
                || info.Protect == 0)
            {
                return false;
            }

            at = info.BaseAddress.ToInt64() + info.RegionSize.ToInt64();
        }

        return true;
    }

    private static long NextRegion(long address)
    {
        return VirtualQuery(new IntPtr(address), out MemoryInfo info, (UIntPtr)Marshal.SizeOf<MemoryInfo>()) == UIntPtr.Zero
            ? address + 0x1000
            : Math.Max(address + 8, info.BaseAddress.ToInt64() + info.RegionSize.ToInt64());
    }

    private static string Hex(IntPtr address, int length)
    {
        if (!Readable(address, length))
        {
            return string.Empty;
        }

        StringBuilder hex = new(length * 3);
        for (int i = 0; i < length; i++)
        {
            hex.Append(Marshal.ReadByte(address, i).ToString("x2", CultureInfo.InvariantCulture));
            if (i % 8 == 7)
            {
                hex.Append(' ');
            }
        }

        return hex.ToString().TrimEnd();
    }
}
