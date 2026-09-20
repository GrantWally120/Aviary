using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// What the folder says about itself — manifest.json, session.json, summary.md — plus the
/// two things that watch Aviary while it runs: the once-a-minute performance sample and the
/// UI-stall watchdog.
/// </summary>
internal static partial class Diagnostics
{
    // ══ THE 16 CATEGORIES — ONE LIST ════════════════════════════════════════
    //
    // The single source for manifest.json, summary.md AND the Settings page's "What Aviary
    // collects" list, which receives it as JSON. Three copies of this would drift within a
    // phase; there is one.

    internal sealed record Category(int Number, string Name, string Plain, string File);

    internal static readonly Category[] Categories =
    {
        new(1, "Browsing", "Every web address you visit, in full, with the page's title, where you came from, any redirects, whether it loaded and how long it took.", "browsing.jsonl"),
        new(2, "Searches", "What you type into the address bar, which search engine it went to, and which suggestion you picked.", "browsing.jsonl"),
        new(3, "Network", "Every request pages make: the address, what kind of file, whether it worked, its size, how long it took, and whether it came from the site you are on or somewhere else.", "network.jsonl"),
        new(4, "Blocking", "Every ad or tracker Aviary stopped: its full address, the rule that stopped it and the page you were on, plus any change to what gets blocked.", "blocking.jsonl"),
        new(5, "What you do in Aviary", "Buttons, menus and keyboard shortcuts; opening, closing, switching, moving and pinning tabs; the sidebar; looks and themes; the words you look for with Find; zoom.", "events.jsonl"),
        new(6, "Settings", "Every change — what it was and what it became — and a copy of all your settings each time collection starts.", "events.jsonl"),
        new(7, "Experiments", "Every experimental feature you switch on or off, and which were on when something went wrong.", "events.jsonl"),
        new(8, "Website errors", "Script errors and files that fail to load on the pages you visit, so a broken website can be told apart from a broken browser.", "pages.jsonl"),
        new(9, "Engine failures", "When a part of the browser engine — a page, the graphics process, the engine itself — crashes, hangs or restarts.", "issues.jsonl"),
        new(10, "Hidden errors", "Problems Aviary used to recover from silently, with exactly where in Aviary they happened.", "issues.jsonl"),
        new(11, "Things that look wrong", "Settings that do not come back the way they were saved, site icons that will not load, tabs that fail to start, and moments the window stops responding for more than a twentieth of a second.", "issues.jsonl"),
        new(12, "Crashes", "What went wrong, the last hundred or so things that happened before it, and the address of every open tab.", "crashes/"),
        new(13, "Speed", "How long starting up, new tabs and pages take; how hard the ad blocker works; how much memory and processor each part of Aviary uses; how quickly the window responds.", "perf.jsonl"),
        new(14, "Features", "Which features you use — Reader, the sidebar, pinned tabs, Start Page sections — and whether Reader could read each page.", "events.jsonl"),
        new(15, "Downloads", "Each file's name, where it came from, its size, and whether it finished.", "events.jsonl"),
        new(16, "Your computer", "Windows version, browser engine version, processor, memory, graphics, every screen's size, scaling and refresh rate, the window's size and how many tabs are open.", "session.json"),
    };

    internal static readonly string[] NeverCollected =
    {
        "Passwords, and anything typed into a password box.",
        "What you type into forms on websites.",
        "Cookie values, login tokens and other session secrets.",
        "Card numbers.",
        "Anything on your clipboard.",
    };

    internal const string MaskingNote =
        "Web addresses that carry a login code, token or password keep the name of that part, with its value replaced by •••, so the address still says what it was without being a working key.";

    internal static readonly string StaysNote =
        "Everything stays on this computer. This feature contains no code that can send anything anywhere. Kept for "
        + RetentionDays + " days and never more than " + CapMB + " MB — the oldest is removed first. Private browsing is never recorded.";

    // ══ SESSION COUNTERS — for summary.md ═══════════════════════════════════

    private static long _navigations;
    private static long _blocked;
    private static long _pageErrors;
    private static long _engineFailures;
    private static long _crashes;
    private static long _requests;
    private static long _requestBytes;
    private static long _thirdParty;
    private static long _stalls;
    private static double _longestStall;
    private static string _longestStallDuring = string.Empty;

    private static readonly ConcurrentDictionary<string, int> RuleCounts = new();
    private static readonly ConcurrentDictionary<string, int> FeatureCounts = new();

    private sealed record Slow(string Op, double Ms, string Detail);

    private static readonly List<Slow> Slowest = new();

    private static void ResetSessionCounters()
    {
        _navigations = _blocked = _pageErrors = _engineFailures = _crashes = 0;
        _requests = _requestBytes = _thirdParty = _stalls = 0;
        _longestStall = 0;
        _longestStallDuring = string.Empty;
        RuleCounts.Clear();
        FeatureCounts.Clear();
        SwallowedCounts.Clear();
        SwallowedSinceSample.Clear();
        AnomalyCounts.Clear();
        lock (Slowest) { Slowest.Clear(); }
    }

    /// <summary>Keeps the slowest few of each kind of operation for summary.md.</summary>
    public static void TrackSlow(string op, double ms, string detail)
    {
        if (_mode != 1)
        {
            return;
        }

        lock (Slowest)
        {
            Slowest.Add(new Slow(op, ms, detail));
            if (Slowest.Count > 400)
            {
                // Keep the five slowest of each kind.
                List<Slow> keep = Slowest
                    .GroupBy(s => s.Op)
                    .SelectMany(g => g.OrderByDescending(s => s.Ms).Take(5))
                    .ToList();
                Slowest.Clear();
                Slowest.AddRange(keep);
            }
        }
    }

    /// <summary>Writer thread: tallies taken from records as they are written.</summary>
    private static void CountForSummary(Entry e)
    {
        if (e.Data is NetRecord net)
        {
            Interlocked.Increment(ref _requests);
            if (net.Bytes is > 0)
            {
                Interlocked.Add(ref _requestBytes, net.Bytes.Value);
            }

            if (net.Party == "third")
            {
                Interlocked.Increment(ref _thirdParty);
            }
        }
    }

    // ══ manifest.json ═══════════════════════════════════════════════════════

    private static void WriteManifest()
    {
        var manifest = new
        {
            name = "Aviary — data collected for improvements",
            about = "This folder is written by Aviary's \"Allow data collection for improvements\" switch "
                  + "(Settings → Privacy and security, at the bottom). It is filled only while that switch is on.",
            readFirst = "summary.md — the digest, regenerated every 10 minutes and when Aviary closes.",
            stays = StaysNote,
            masking = MaskingNote,
            neverCollected = NeverCollected,
            capMB = CapBytes / (1024 * 1024),
            retentionDays = RetentionDays,
            rotation = "Each .jsonl file moves into archive/ when it reaches 16 MB; archives are deleted oldest-first to stay under the cap.",
            lineFormat = "One JSON object per line. t = local time, s = collection session, k = kind; the rest depends on the file.",
            categories = Categories.Select(c => new { number = c.Number, name = c.Name, whatItHolds = c.Plain, file = c.File }),
            files = new Dictionary<string, string>
            {
                ["summary.md"] = "The digest: what needs attention, recurring issues, slowest operations, sites with the most page errors.",
                ["session.json"] = "This session: your computer, the window, open tabs, all settings, and whether the session ended cleanly.",
                ["browsing.jsonl"] = "Categories 1-2: navigations, searches, titles, same-page route changes.",
                ["network.jsonl"] = "Category 3: every request, logged when its response arrives.",
                ["blocking.jsonl"] = "Category 4: every blocked request, and changes to blocking.",
                ["events.jsonl"] = "Categories 5, 6, 7, 14, 15: what you did, settings, experiments, features, downloads.",
                ["pages.jsonl"] = "Category 8: website script errors, rejected promises, failed resources.",
                ["issues.jsonl"] = "Categories 9-12: engine failures, hidden errors, anomalies, and a pointer to each crash report.",
                ["perf.jsonl"] = "Category 13: start-up, tab creation, page timing, the blocker's cost, memory and processor, responsiveness.",
                ["crashes/"] = "Category 12: one file per crash.",
                ["archive/"] = "Older .jsonl files, moved out of the way by size.",
            },
        };

        WriteFileAtomically(Path.Combine(Folder, "manifest.json"), SerialiseDocument(manifest));
    }

    // ══ session.json — category 16, and whether the session ended cleanly ═════

    private static object? _environment;
    private static object? _window;
    private static Func<object?>? _windowProvider;

    /// <summary>The engine's version, as the environment reports it once a tab has started.</summary>
    public static string EngineVersion { get; set; } = string.Empty;

    private static void WriteSessionFile(string? ended)
    {
        _environment ??= ProbeEnvironment();

        var session = new
        {
            session = _session,
            reason = _sessionReason,
            started = Stamp(_sessionStart),
            updated = Stamp(DateTimeOffset.Now),
            ended,
            build = new
            {
                version = BuildInfo.Version,
                channel = BuildInfo.Channel,
                phase = BuildInfo.PhaseLabel,
                engine = EngineVersion.Length > 0 ? EngineVersion : SafeAvailableEngineVersion(),
            },
            process = new
            {
                pid = Environment.ProcessId,
                started = Stamp(ProcessStart),
                uptimeMinutes = Math.Round((DateTimeOffset.Now - ProcessStart).TotalMinutes, 1),
                workingSetMB = Math.Round(Environment.WorkingSet / 1048576.0, 1),
            },
            computer = _environment,
            window = _window,
            openTabs = TabsSnapshot(),
            settings = AppSettings.SnapshotForReport(),
            experimentsOn = ActiveExperiments(),
            collection = new
            {
                folder = Folder,
                capMB = CapBytes / (1024 * 1024),
                retentionDays = RetentionDays,
                linesWrittenThisSession = Interlocked.Read(ref _linesWritten),
                droppedThisSession = Interlocked.Read(ref _dropped),
                writeErrorsThisSession = Interlocked.Read(ref _writeErrors),
            },
        };

        WriteFileAtomically(Path.Combine(Folder, "session.json"), SerialiseDocument(session));
    }

    /// <summary>
    /// Reads the PREVIOUS session.json before it is replaced. No "ended" means Aviary did not
    /// close normally — force-killed, or a crash that took the process with it.
    /// </summary>
    private static void NoteUncleanPreviousSession()
    {
        string path = Path.Combine(Folder, "session.json");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllBytes(path));
            JsonElement root = doc.RootElement;

            bool ended = root.TryGetProperty("ended", out JsonElement e) && e.ValueKind == JsonValueKind.String;
            if (ended)
            {
                return;
            }

            string Read(string name) =>
                root.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
                    ? v.GetString() ?? string.Empty
                    : string.Empty;

            WriteEntry(new Entry
            {
                File = LogFile.Issues,
                Time = DateTimeOffset.Now,
                Kind = "unclean-exit",
                Data = new
                {
                    previousSession = Read("session"),
                    started = Read("started"),
                    lastSeen = Read("updated"),
                    detail = "The previous session never reached a clean close: Aviary was force-closed, lost power, or crashed in a way that ended the process.",
                },
            });
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _writeErrors);
        }
    }

    // ══ ENVIRONMENT — category 16 ═══════════════════════════════════════════

    private static string SafeAvailableEngineVersion()
    {
        try
        {
            return CoreWebView2Environment.GetAvailableBrowserVersionString() ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static object ProbeEnvironment()
    {
        string Try(Func<string?> read)
        {
            try { return read() ?? string.Empty; }
            catch (Exception) { return string.Empty; }
        }

        string winKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

        string RegString(string key, string value) => Try(() =>
        {
            using Microsoft.Win32.RegistryKey? k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(key);
            return k?.GetValue(value)?.ToString();
        });

        MEMORYSTATUSEX memory = new() { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        bool haveMemory = false;
        try { haveMemory = GlobalMemoryStatusEx(ref memory); }
        catch (Exception) { /* reported as unknown */ }

        return new
        {
            windows = new
            {
                edition = RegString(winKey, "EditionID"),
                release = RegString(winKey, "DisplayVersion"),
                build = RegString(winKey, "CurrentBuild") + "." + RegString(winKey, "UBR"),
                version = Environment.OSVersion.Version.ToString(),
            },
            dotnet = RuntimeInformation.FrameworkDescription,
            windowsAppSdk = Try(() => Microsoft.Windows.ApplicationModel.WindowsAppRuntime.ReleaseInfo.AsString),
            windowsAppRuntimePackage = Try(() => Microsoft.Windows.ApplicationModel.WindowsAppRuntime.RuntimeInfo.AsString),
            processor = new
            {
                name = RegString(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString").Trim(),
                logicalCores = Environment.ProcessorCount,
                architecture = RuntimeInformation.OSArchitecture.ToString(),
            },
            memoryGB = haveMemory ? Math.Round(memory.ullTotalPhys / 1073741824.0, 1) : (double?)null,
            memoryAvailableGB = haveMemory ? Math.Round(memory.ullAvailPhys / 1073741824.0, 1) : (double?)null,
            graphics = GraphicsAdapters(),
            displays = Displays(),
            locale = CultureInfo.CurrentUICulture.Name,
            timeZone = TimeZoneInfo.Local.Id,
        };
    }

    private static List<string> GraphicsAdapters()
    {
        List<string> names = new();
        try
        {
            DISPLAY_DEVICE device = new() { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint i = 0; EnumDisplayDevices(null, i, ref device, 0); i++)
            {
                if (!string.IsNullOrWhiteSpace(device.DeviceString) && !names.Contains(device.DeviceString))
                {
                    names.Add(device.DeviceString);
                }

                device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            }
        }
        catch (Exception)
        {
            // Unknown graphics is still a useful session file.
        }

        return names;
    }

    private static List<object> Displays()
    {
        List<object> displays = new();

        try
        {
            MonitorEnumProc callback = (IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                try
                {
                    MONITORINFOEX info = new() { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                    if (!GetMonitorInfo(monitor, ref info))
                    {
                        return true;
                    }

                    uint dpiX = 96;
                    try { _ = GetDpiForMonitor(monitor, 0, out dpiX, out _); }
                    catch (Exception) { dpiX = 96; }

                    DEVMODE mode = new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };
                    bool haveMode = EnumDisplaySettings(info.szDevice, -1, ref mode);

                    displays.Add(new
                    {
                        device = info.szDevice,
                        primary = (info.dwFlags & 1) != 0,
                        width = haveMode ? mode.dmPelsWidth : info.rcMonitor.Right - info.rcMonitor.Left,
                        height = haveMode ? mode.dmPelsHeight : info.rcMonitor.Bottom - info.rcMonitor.Top,
                        scalePercent = (int)Math.Round(dpiX * 100.0 / 96.0),
                        refreshHz = haveMode ? mode.dmDisplayFrequency : 0,
                    });
                }
                catch (Exception)
                {
                    // One unreadable monitor should not lose the others.
                }

                return true;
            };

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);
        }
        catch (Exception)
        {
            // Reported as no displays rather than as a failure to write the session.
        }

        return displays;
    }

    // ══ TIMERS ══════════════════════════════════════════════════════════════

    private static Microsoft.UI.Dispatching.DispatcherQueue? _ui;
    private static Timer? _watchdog;
    private static Timer? _sampler;
    private static Timer? _summaryTimer;

    /// <summary>
    /// The window exists. Hands over its dispatcher (the watchdog and the process sampler must
    /// reach the UI thread) and a way to describe the window for session.json.
    /// </summary>
    public static void AttachUi(Microsoft.UI.Dispatching.DispatcherQueue ui, Func<object?> window)
    {
        _ui = ui;
        _windowProvider = window;
        CaptureUiThread();
        RefreshWindow();

        if (_mode == 1)
        {
            StartTimers();
        }
    }

    private static void StartTimers()
    {
        if (_ui is null)
        {
            return;
        }

        _watchdog ??= new Timer(WatchdogTick, null, 1000, 100);
        _sampler ??= new Timer(_ => Sample(), null, 60_000, 60_000);
        _summaryTimer ??= new Timer(_ => EnqueueCommand(PeriodicOnWriter), null, 600_000, 600_000);
    }

    private static void StopTimers()
    {
        _watchdog?.Dispose();
        _watchdog = null;
        _sampler?.Dispose();
        _sampler = null;
        _summaryTimer?.Dispose();
        _summaryTimer = null;
    }

    /// <summary>Every ten minutes: tidy, summarise, and refresh session.json.</summary>
    private static void PeriodicOnWriter()
    {
        if (!_open)
        {
            return;
        }

        Housekeep();
        BuildSummary();
        WriteSessionFile(ended: null);
    }

    private static void RefreshWindow()
    {
        try
        {
            _window = _windowProvider?.Invoke() ?? _window;
        }
        catch (Exception ex)
        {
            Swallowed(ex, "diagnostics: describe window");
        }
    }

    // ══ THE WATCHDOG — UI stalls over 50 ms ═════════════════════════════════
    //
    // Ten times a second a background timer posts one empty callback to the UI thread and
    // times how long it waits to run. An idle UI thread runs it within a millisecond; a busy
    // one runs it only when it is free, so the wait IS the stall. Only one tick is ever in
    // flight, so a 400 ms freeze reads as one 400 ms stall rather than four queued ticks.
    //
    // Honest naming: this is responsiveness, not frame timing. WinUI exposes per-frame times
    // only through CompositionTarget.Rendering, and subscribing to that forces the compositor
    // to redraw every frame forever — measuring smoothness by destroying it.

    private const double StallMs = 50;
    private static int _tickPending;
    private static readonly object TickGate = new();
    private static readonly List<double> TickLatencies = new();
    private static long _lastStallLogged;

    // ── What a stall WAS (bug 67) ────────────────────────────────────────────────
    //
    // Grant's 16 Sep diagnostics held an 861 ms and a 536 ms stall, each in a minute with
    // several more, while Aviary's own processor use was 0.0–0.1% and nothing at all had
    // happened in Aviary for 13 seconds and 56 minutes. The only label they carried was the
    // last breadcrumb — "ui tab-switch", thirteen seconds stale — which read as a cause and was
    // not one. A stall is now measured, not guessed:
    //   - the UI thread's OWN processor time across the wait says whether it was working
    //     (Aviary or XAML doing something slow) or waiting (on the disk, the GPU, a lock,
    //     another process);
    //   - the whole machine's processor time over the same span says whether other programs
    //     had it;
    //   - the watchdog's own timer says whether this process was running at all — if the
    //     timer was late by the length of the stall, the machine paused, not the UI thread;
    //   - Windows' move/size loop (dragging or resizing the window, WM_ENTERSIZEMOVE) and its
    //     menu loop are recorded, and a stall inside one is the window being held, not Aviary.
    // The breadcrumb stays, with its age, so "during" can no longer pass for a cause.

    private static IntPtr _uiThread;
    private static long _lastTimerAt;
    private static long _timerLateMaxTicks;
    private static long _postedUiCpu;
    private static long _postedSysIdle;
    private static long _postedSysTotal;
    private static int _modalLoop;           // 1 while Windows is moving/sizing the window or running a menu loop
    private static int _modalLoopsSincePost; // loops that STARTED while the tick was waiting
    private static string _modalKind = string.Empty;
    private static long _stallsWhileMoving;

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenThread(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint threadId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetThreadTimes(IntPtr thread, out long created, out long exited, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

    private const uint ThreadQueryLimitedInformation = 0x0800;

    /// <summary>Called on the UI thread: a handle the watchdog can read the thread's time through.</summary>
    private static void CaptureUiThread()
    {
        if (_uiThread == IntPtr.Zero)
        {
            _uiThread = OpenThread(ThreadQueryLimitedInformation, false, GetCurrentThreadId());
        }
    }

    /// <summary>The UI thread's processor time so far, in 100 ns units; -1 when unreadable.</summary>
    private static long UiThreadCpu() =>
        _uiThread != IntPtr.Zero && GetThreadTimes(_uiThread, out _, out _, out long k, out long u) ? k + u : -1;

    private static (long Idle, long Total) SystemCpu() =>
        GetSystemTimes(out long idle, out long kernel, out long user) ? (idle, kernel + user) : (-1, -1);

    /// <summary>
    /// Windows entered or left a modal loop on one of Aviary's windows: moving or sizing it
    /// (WM_ENTERSIZEMOVE / WM_EXITSIZEMOVE), or its system menu (WM_ENTERMENULOOP / WM_EXITMENULOOP).
    /// Called from the window's subclass procedure (MainWindow.SessionEnd.cs).
    /// </summary>
    public static void NoteModalLoop(string kind, bool entering)
    {
        if (entering)
        {
            _modalKind = kind;
            Interlocked.Exchange(ref _modalLoop, 1);
            Interlocked.Increment(ref _modalLoopsSincePost);
        }
        else
        {
            Interlocked.Exchange(ref _modalLoop, 0);
        }

        if (_mode == 1)
        {
            Crumb("window", (entering ? "start " : "end ") + kind);
        }
    }

    private static void WatchdogTick(object? state)
    {
        Microsoft.UI.Dispatching.DispatcherQueue? ui = _ui;
        if (_mode != 1 || ui is null)
        {
            return;
        }

        // How late THIS timer fired. It is a thread-pool timer every 100 ms; if it too was held
        // up for the length of a stall, the process (or the machine) was not running at all.
        long firedAt = Stopwatch.GetTimestamp();
        long previous = Interlocked.Exchange(ref _lastTimerAt, firedAt);
        if (previous != 0)
        {
            long late = firedAt - previous - Stopwatch.Frequency / 10;
            if (late > Interlocked.Read(ref _timerLateMaxTicks))
            {
                Interlocked.Exchange(ref _timerLateMaxTicks, late);
            }
        }

        if (Interlocked.CompareExchange(ref _tickPending, 1, 0) != 0)
        {
            return;
        }

        Interlocked.Exchange(ref _timerLateMaxTicks, 0);
        Interlocked.Exchange(ref _modalLoopsSincePost, 0);
        Interlocked.Exchange(ref _postedUiCpu, UiThreadCpu());
        (long idle, long total) = SystemCpu();
        Interlocked.Exchange(ref _postedSysIdle, idle);
        Interlocked.Exchange(ref _postedSysTotal, total);

        long posted = Stopwatch.GetTimestamp();
        bool queued = ui.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Normal, () =>
        {
            double ms = Stopwatch.GetElapsedTime(posted).TotalMilliseconds;
            NoteTick(ms);
            Interlocked.Exchange(ref _tickPending, 0);
        });

        if (!queued)
        {
            Interlocked.Exchange(ref _tickPending, 0);
        }
    }

    private static void NoteTick(double ms)
    {
        // Past a minute it was the machine sleeping, not Aviary stalling.
        bool stall = ms > StallMs && ms <= 60_000;
        bool moving = Volatile.Read(ref _modalLoop) == 1 || Volatile.Read(ref _modalLoopsSincePost) > 0;

        if (stall && moving)
        {
            // The window was being dragged, sized or had its system menu open. Counted in the
            // minute's sample, but it is not Aviary failing to respond, and it is not logged as
            // one — nor does it enter the latency figures, where one long drag would swamp them.
            Interlocked.Increment(ref _stallsWhileMoving);
            return;
        }

        lock (TickGate)
        {
            if (TickLatencies.Count < 2000)
            {
                TickLatencies.Add(ms);
            }
        }

        if (!stall)
        {
            return;
        }

        StallCause cause = MeasureStall(ms);

        Interlocked.Increment(ref _stalls);
        string during = LastCrumbWithAge();
        if (ms > _longestStall)
        {
            _longestStall = ms;
            _longestStallDuring = cause.Verdict + " — last action: " + during;
        }

        TrackSlow("ui-stall", ms, cause.Verdict + " — last action: " + during);

        // At most one stall line every two seconds; the rest are in the minute's sample.
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastStallLogged) >= 2000)
        {
            Interlocked.Exchange(ref _lastStallLogged, now);
            Anomaly("ui-stall", Math.Round(ms) + " ms without responding — " + cause.Verdict, new
            {
                ms = Math.Round(ms, 1),
                cause = cause.Kind,
                uiThreadCpuMs = cause.UiCpuMs,
                systemCpuPercent = cause.SystemCpuPercent,
                timerLateMs = cause.TimerLateMs,
                during,
            });
        }
    }

    private sealed record StallCause(string Kind, string Verdict, double? UiCpuMs, double? SystemCpuPercent, double TimerLateMs);

    /// <summary>Runs on the UI thread the moment a stall ends; compares against the post.</summary>
    private static StallCause MeasureStall(double ms)
    {
        long uiBefore = Interlocked.Read(ref _postedUiCpu);
        long uiAfter = UiThreadCpu();
        double? uiCpuMs = uiBefore >= 0 && uiAfter >= uiBefore ? Math.Round((uiAfter - uiBefore) / 10_000.0, 1) : null;

        long idleBefore = Interlocked.Read(ref _postedSysIdle), totalBefore = Interlocked.Read(ref _postedSysTotal);
        (long idleAfter, long totalAfter) = SystemCpu();
        double? systemPercent = null;
        if (idleBefore >= 0 && idleAfter >= 0 && totalAfter > totalBefore)
        {
            double busy = (totalAfter - totalBefore) - (idleAfter - idleBefore);
            systemPercent = Math.Round(Math.Clamp(busy * 100.0 / (totalAfter - totalBefore), 0, 100), 1);
        }

        double timerLateMs = Math.Round(Interlocked.Read(ref _timerLateMaxTicks) * 1000.0 / Stopwatch.Frequency, 1);

        if (timerLateMs >= ms * 0.5)
        {
            return new StallCause("machine-paused", "the whole of Aviary was paused, not just its window (the computer was busy, sleeping or froze)", uiCpuMs, systemPercent, timerLateMs);
        }

        if (uiCpuMs is double busyMs && busyMs >= ms * 0.5)
        {
            return new StallCause("ui-busy", "Aviary's window was busy working", uiCpuMs, systemPercent, timerLateMs);
        }

        if (systemPercent is double sys && sys >= 85)
        {
            return new StallCause("ui-waiting-system-busy", "Aviary's window was waiting while other programs had the processor", uiCpuMs, systemPercent, timerLateMs);
        }

        return new StallCause("ui-waiting", "Aviary's window was waiting on something outside it (the disk, the graphics card, or another process)", uiCpuMs, systemPercent, timerLateMs);
    }

    // ══ THE MINUTE SAMPLE — memory, processor, responsiveness, blocker cost ═══

    private static readonly Dictionary<int, (TimeSpan Cpu, long At)> CpuSeen = new();

    private static void Sample()
    {
        if (_mode != 1)
        {
            return;
        }

        List<double> ticks;
        lock (TickGate)
        {
            ticks = new List<double>(TickLatencies);
            TickLatencies.Clear();
        }

        long filterCount = Interlocked.Exchange(ref _filterCount, 0);
        long filterTicks = Interlocked.Exchange(ref _filterTicks, 0);
        long filterMax = Interlocked.Exchange(ref _filterMaxTicks, 0);

        // Repeated swallowed errors since the last sample, so a catch that fires on every
        // hover is still counted rather than silently capped.
        List<object> repeats = new();
        foreach (string key in SwallowedSinceSample.Keys.ToList())
        {
            if (SwallowedSinceSample.TryRemove(key, out int n) && n > 0)
            {
                repeats.Add(new { at = key, times = n });
            }
        }

        if (repeats.Count > 0)
        {
            Enqueue(LogFile.Issues, "swallowed-repeats", null, new { sinceLastMinute = repeats });
        }

        double ToUs(long t) => Math.Round(t * 1_000_000.0 / Stopwatch.Frequency, 1);

        long whileHeld = Interlocked.Exchange(ref _stallsWhileMoving, 0);
        var ui = ticks.Count == 0 ? null : new
        {
            samples = ticks.Count,
            medianMs = Math.Round(Percentile(ticks, 0.5), 1),
            p95Ms = Math.Round(Percentile(ticks, 0.95), 1),
            maxMs = Math.Round(ticks.Max(), 1),
            stallsOver50Ms = ticks.Count(t => t > StallMs && t <= 60_000),
            // Waits while Windows held the window (moving, sizing, its system menu) — kept out
            // of the figures above (bug 67).
            stallsWhileWindowHeld = whileHeld == 0 ? (long?)null : whileHeld,
        };

        var filter = filterCount == 0 ? null : new
        {
            matches = filterCount,
            averageUs = ToUs(filterTicks / filterCount),
            maxUs = ToUs(filterMax),
        };

        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher = _ui;
        if (dispatcher is null)
        {
            WriteSample(ui, filter, new List<(int, string, string[])>());
            return;
        }

        // The engine's process list must be read on the UI thread; the per-process numbers
        // are then gathered back off it.
        bool queued = dispatcher.TryEnqueue(async () =>
        {
            List<(int Pid, string Kind, string[] Frames)> processes = new();

            try
            {
                RefreshWindow();
                CoreWebView2Environment env = await WebViewEnvironment.GetAsync();
                IReadOnlyList<CoreWebView2ProcessExtendedInfo> infos = await env.GetProcessExtendedInfosAsync();

                // While a private window is open, its tabs' renderers are in this list too, and
                // nothing in it says which profile a process belongs to — M2 found the private
                // tabs' addresses in the minute sample. So no frame addresses at all until the
                // last private tab closes; the memory and processor figures stay.
                bool privateOpen = AnyPrivateTab;

                foreach (CoreWebView2ProcessExtendedInfo info in infos)
                {
                    string[] frames = privateOpen
                        ? Array.Empty<string>()
                        : info.AssociatedFrameInfos?
                            .Select(f => f.Source)
                            .Where(s => !string.IsNullOrEmpty(s))
                            .Take(8)
                            .ToArray() ?? Array.Empty<string>();

                    processes.Add((info.ProcessInfo.ProcessId, info.ProcessInfo.Kind.ToString(), frames));
                }
            }
            catch (Exception ex)
            {
                Swallowed(ex, "diagnostics: engine process list");
            }

            _ = Task.Run(() => WriteSample(ui, filter, processes));
        });

        if (!queued)
        {
            WriteSample(ui, filter, new List<(int, string, string[])>());
        }
    }

    private static void WriteSample(object? ui, object? filter, List<(int Pid, string Kind, string[] Frames)> engine)
    {
        try
        {
            long now = Stopwatch.GetTimestamp();
            List<object> processes = new();

            using (Process self = Process.GetCurrentProcess())
            {
                processes.Add(DescribeProcess(self, "Aviary", Array.Empty<string>(), now));
            }

            foreach ((int pid, string kind, string[] frames) in engine)
            {
                try
                {
                    using Process p = Process.GetProcessById(pid);
                    processes.Add(DescribeProcess(p, kind, frames, now));
                }
                catch (Exception)
                {
                    // Exited between listing and measuring.
                }
            }

            lock (CpuSeen)
            {
                HashSet<int> alive = engine.Select(e => e.Pid).Append(Environment.ProcessId).ToHashSet();
                foreach (int gone in CpuSeen.Keys.Where(k => !alive.Contains(k)).ToList())
                {
                    CpuSeen.Remove(gone);
                }
            }

            Perf("sample", new
            {
                tabs = Tabs.Count,
                ui,
                blocker = filter,
                managedHeapMB = Math.Round(GC.GetTotalMemory(false) / 1048576.0, 1),
                processes,
            });
        }
        catch (Exception ex)
        {
            Swallowed(ex, "diagnostics: minute sample");
        }
    }

    private static object DescribeProcess(Process p, string kind, string[] frames, long now)
    {
        p.Refresh();
        TimeSpan cpu = p.TotalProcessorTime;
        double? cpuPercent = null;

        lock (CpuSeen)
        {
            if (CpuSeen.TryGetValue(p.Id, out (TimeSpan Cpu, long At) last))
            {
                double wall = Stopwatch.GetElapsedTime(last.At, now).TotalMilliseconds;
                if (wall > 0)
                {
                    cpuPercent = Math.Round((cpu - last.Cpu).TotalMilliseconds * 100.0 / (wall * Environment.ProcessorCount), 1);
                }
            }

            CpuSeen[p.Id] = (cpu, now);
        }

        return new
        {
            pid = p.Id,
            kind,
            workingSetMB = Math.Round(p.WorkingSet64 / 1048576.0, 1),
            privateMB = Math.Round(p.PrivateMemorySize64 / 1048576.0, 1),
            cpuPercent,
            frames = frames.Length == 0 ? null : frames,
        };
    }

    private static double Percentile(List<double> values, double p)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        List<double> sorted = values.OrderBy(v => v).ToList();
        int index = (int)Math.Clamp(Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }

    // ══ SETTINGS ROUND-TRIP — category 11 ═══════════════════════════════════

    /// <summary>
    /// After settings.json is written, read it back on the writer thread and compare. A key
    /// that does not come back as it was saved is the M0 "saves but reverts" bug class,
    /// caught at the moment it happens instead of on the next launch.
    /// </summary>
    public static void SettingsSaved(IReadOnlyDictionary<string, string> expected, string path, long sequence)
    {
        if (_mode != 1)
        {
            return;
        }

        Dictionary<string, string> copy = new(expected, StringComparer.OrdinalIgnoreCase);
        EnqueueCommand(() =>
        {
            // A newer save has happened (or is happening): it carries its own check, and
            // comparing this one against its file would report a mismatch that is not real.
            if (AppSettings.SaveSequence != sequence)
            {
                return;
            }

            try
            {
                // Under AppSettings' own lock: a read still open when the next save moves its file
                // into place makes that save fail (bug 66).
                byte[] bytes;
                lock (AppSettings.FileGate)
                {
                    bytes = File.ReadAllBytes(path);
                }
                if (AppSettings.SaveSequence != sequence)
                {
                    return;
                }

                using JsonDocument doc = JsonDocument.Parse(bytes);
                Dictionary<string, string> onDisk = new(StringComparer.OrdinalIgnoreCase);
                foreach (JsonProperty p in doc.RootElement.EnumerateObject())
                {
                    onDisk[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? string.Empty : p.Value.ToString();
                }

                foreach (KeyValuePair<string, string> pair in copy)
                {
                    if (!onDisk.TryGetValue(pair.Key, out string? found))
                    {
                        Anomaly("settings-roundtrip", pair.Key + " was saved but is missing from settings.json", new { key = pair.Key, saved = pair.Value });
                    }
                    else if (!string.Equals(found, pair.Value, StringComparison.Ordinal))
                    {
                        Anomaly("settings-roundtrip", pair.Key + " was saved as \"" + pair.Value + "\" but reads back as \"" + found + "\"",
                            new { key = pair.Key, saved = pair.Value, onDisk = found });
                    }
                }
            }
            catch (IOException)
            {
                // Mid-move by a second save. The next save is checked instead.
            }
            catch (Exception ex)
            {
                Anomaly("settings-roundtrip", "settings.json could not be read back: " + ex.Message);
            }
        });
    }

    /// <summary>
    /// The Settings page reports what each control actually DREW. Anything that disagrees
    /// with the store is a row that will look broken — the exact shape of M0's nine
    /// "saves but comes back as the default" switches.
    /// </summary>
    public static void SettingsRendered(IReadOnlyDictionary<string, string> rendered)
    {
        if (_mode != 1)
        {
            return;
        }

        Dictionary<string, string> store = AppSettings.Snapshot();
        foreach (KeyValuePair<string, string> pair in rendered)
        {
            if (!store.TryGetValue(pair.Key, out string? stored)
                || Array.IndexOf(AppSettings.PrivateKeys, pair.Key) >= 0)
            {
                // A redacted setting reports a summary, not its value: there is nothing to
                // compare, and comparing would write the value into the anomaly.
                continue;
            }

            // A switch draws its truthiness, and the rest of Aviary reads a switch through
            // GetBool — so "" and "false" are the same answer, and only a real disagreement
            // about on/off counts.
            bool mismatch = pair.Value is "true" or "false"
                ? string.Equals(stored, "true", StringComparison.OrdinalIgnoreCase) != (pair.Value == "true")
                : !string.Equals(stored, pair.Value, StringComparison.OrdinalIgnoreCase);

            if (mismatch)
            {
                Anomaly("settings-roundtrip", "Settings shows " + pair.Key + " as \"" + pair.Value + "\" but the store holds \"" + stored + "\"",
                    new { key = pair.Key, shown = pair.Value, stored });
            }
        }
    }

    // ══ WHAT THE SETTINGS ROW SAYS ══════════════════════════════════════════

    /// <summary>The live status line under "Collected data", and everything the page needs.</summary>
    public static object DescribeForSettings()
    {
        (int files, long bytes) = Stats();
        string size = bytes >= 1024 * 1024
            ? (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB"
            : Math.Max(1, bytes / 1024) + " KB";

        string status = _mode == 1
            ? "Collecting now · " + (files == 0 ? "starting" : size + " in " + files + (files == 1 ? " file" : " files"))
              + " · kept " + RetentionDays + " days, never more than " + CapMB + " MB."
            : files > 0
                ? "Off · " + size + " kept from earlier. Delete removes it."
                : "Off · nothing collected.";

        return new
        {
            on = _mode == 1,
            status,
            folder = Folder,
            categories = Categories.Select(c => new { n = c.Number, name = c.Name, plain = c.Plain }),
            never = NeverCollected,
            masking = MaskingNote,
            stays = StaysNote,
        };
    }

    // ══ P/INVOKE — environment probes ═══════════════════════════════════════

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string? device, uint index, ref DISPLAY_DEVICE displayDevice, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE mode);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
