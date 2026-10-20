using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Settings → Performance, the window's half (Alpha Build 5 M4.5). <see cref="LiveStats"/> measures;
/// this says which engine process is which tab, answers the page's two commands, and hands each
/// second's reading to the Settings tab that asked — only that tab, and only while its Performance
/// pane is on screen (the page itself says when: its pane is showing and the document is visible).
/// </summary>
public sealed partial class MainWindow
{
    private CoreWebView2? _perfPage;
    private CoreWebView2Environment? _perfEnvironment;

    /// <summary>Engine process → the tabs (any window's) whose main frame it draws.</summary>
    private Dictionary<int, List<BrowserTab>> _perfTabsByPid = new();

    private async Task PerfWatchAsync(CoreWebView2 page, bool on)
    {
        if (!on)
        {
            // Bug 92: by TAB, never by reference. The page's "off" arrives with its own WinRT wrapper
            // for the same WebView (bug 70's rule), so a reference test never matched: measuring
            // went on after you left the pane, switched tab or minimised, until the tab itself closed
            // - and a new "How often to measure" never took effect until then either.
            if (_perfPage is not null && TabFor(page) is { } leaving && ReferenceEquals(TabFor(_perfPage), leaving))
            {
                StopPerf();
            }

            return;
        }

        _perfPage = page;

        // M4.6: count what each tab loads, but only while somebody is looking at the figures.
        foreach (MainWindow window in OpenWindows)
        {
            foreach (BrowserTab t in window._tabs)
            {
                t.WatchData(true);
            }
        }
        try
        {
            if (_perfEnvironment is null)
            {
                _perfEnvironment = await WebViewEnvironment.GetAsync();
                _perfEnvironment.ProcessInfosChanged += OnPerfProcessesChanged;
            }

            await RefreshPerfProcessesAsync();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "performance: engine processes");
        }

        LiveStats.Start("performance", reading =>
        {
            // On the sampler's thread. How long the UI thread takes to get to this is also the
            // page's "how quickly Aviary answers" figure.
            long queued = System.Diagnostics.Stopwatch.GetTimestamp();
            DispatcherQueue.TryEnqueue(() =>
            {
                LiveStats.NoteUiLatency(System.Diagnostics.Stopwatch.GetElapsedTime(queued).TotalMilliseconds);
                PushPerf(reading);
            });
        });
    }

    /// <summary>Windows' engine names, in Aviary's words: what that part of the card was doing.</summary>
    private static string EngineName(string engine) => engine switch
    {
        "3D" => "Drawing",
        "VideoDecode" => "Playing video",
        "VideoEncode" => "Recording video",
        "VideoProcessing" => "Video",
        "Copy" => "Moving data",
        "Compute" => "Computing",
        _ => engine,
    };

    private void StopPerf()
    {
        _perfPage = null;
        LiveStats.Stop("performance");

        foreach (MainWindow window in OpenWindows)
        {
            foreach (BrowserTab t in window._tabs)
            {
                t.WatchData(false);
            }
        }
        if (_perfEnvironment is not null)
        {
            _perfEnvironment.ProcessInfosChanged -= OnPerfProcessesChanged;
            _perfEnvironment = null;
        }
    }

    private void OnPerfProcessesChanged(CoreWebView2Environment sender, object args) =>
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                await RefreshPerfProcessesAsync();
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "performance: engine processes changed");
            }
        });

    /// <summary>UI thread: the engine's processes, and which tab each page process is drawing.</summary>
    private async Task RefreshPerfProcessesAsync()
    {
        if (_perfEnvironment is null)
        {
            return;
        }

        _perfTabsByPid = await RefreshEngineProcessesAsync(_perfEnvironment);
    }

    /// <summary>
    /// UI thread: hands <see cref="LiveStats"/> the engine's processes and returns which tabs each
    /// page process draws. Shared by the Performance pane and (M6) the performance overlay.
    /// </summary>
    private static async Task<Dictionary<int, List<BrowserTab>>> RefreshEngineProcessesAsync(CoreWebView2Environment environment)
    {
        IReadOnlyList<CoreWebView2ProcessExtendedInfo> infos = await environment.GetProcessExtendedInfosAsync();

        // Every window's tabs, by the id of the frame that is their page - and Quik's ready tab, whose memory is
        // what its row says it costs.
        Dictionary<uint, BrowserTab> byFrame = new();
        foreach (MainWindow window in OpenWindows)
        {
            foreach (BrowserTab tab in window.TabsAndReady())
            {
                try
                {
                    if (tab.View.CoreWebView2 is CoreWebView2 core)
                    {
                        byFrame[core.FrameId] = tab;
                    }
                }
                catch (Exception)
                {
                    // A tab mid-teardown has no frame to find.
                }
            }
        }

        List<LiveStats.Proc> procs = new();
        Dictionary<int, List<BrowserTab>> tabsByPid = new();
        foreach (CoreWebView2ProcessExtendedInfo info in infos)
        {
            int pid = info.ProcessInfo.ProcessId;
            procs.Add(new LiveStats.Proc(pid, info.ProcessInfo.Kind.ToString()));

            foreach (CoreWebView2FrameInfo frame in info.AssociatedFrameInfos ?? (IReadOnlyList<CoreWebView2FrameInfo>)Array.Empty<CoreWebView2FrameInfo>())
            {
                if (frame.FrameKind == CoreWebView2FrameKind.MainFrame && byFrame.TryGetValue(frame.FrameId, out BrowserTab? tab))
                {
                    if (!tabsByPid.TryGetValue(pid, out List<BrowserTab>? list))
                    {
                        tabsByPid[pid] = list = new List<BrowserTab>();
                    }

                    if (!list.Contains(tab))
                    {
                        list.Add(tab);
                    }
                }
            }
        }

        LiveStats.SetProcesses(procs);
        return tabsByPid;
    }

    /// <summary>UI thread: one reading, turned into what the page draws, sent to the page that asked.</summary>
    private void PushPerf(LiveStats.Reading r)
    {
        CoreWebView2? page = _perfPage;
        BrowserTab? tab = page is null ? null : TabFor(page);

        // Only Aviary's own Settings page ever hears this (bug 62: a host push must never reach a website).
        if (page is null || tab is null || !tab.IsInternalPage || tab.Title != "Settings")
        {
            StopPerf();
            return;
        }

        Dictionary<int, LiveStats.Figures> byPid = r.Processes.ToDictionary(p => p.Pid);
        string KindName(string kind) => kind switch
        {
            "Aviary" => "Aviary itself",
            "Browser" => "Browser engine",
            "Gpu" => "Graphics",
            "Renderer" => "Pages",
            "Utility" => "Network, storage and sound",
            "SlimRenderer" => "Pages",
            _ => kind,
        };

        List<object> tabs = new();
        foreach (MainWindow window in OpenWindows)
        {
            foreach (BrowserTab t in window.TabsAndReady())
            {
                bool ready = window.IsQuikReadyTab(t);
                int pid = _perfTabsByPid.FirstOrDefault(p => p.Value.Contains(t)).Key;
                byPid.TryGetValue(pid, out LiveStats.Figures? f);
                int sharing = pid == 0 ? 0 : _perfTabsByPid[pid].Count;
                bool asleep = false;
                try
                {
                    asleep = t.View.CoreWebView2?.IsSuspended ?? false;
                }
                catch (Exception)
                {
                    // Not started yet.
                }

                bool active = ReferenceEquals(window._active, t);
                tabs.Add(new
                {
                    id = t.Id,
                    // Never a private tab's title or address — the page shows "Private tab".
                    title = t.IsPrivate ? null : ShownTitle(t),
                    isPrivate = t.IsPrivate,
                    audio = t.IsPlayingAudio,
                    muted = t.IsMuted,
                    own = t.IsInternalPage,
                    // M7 Quik: not a tab yet - the next new tab, made in advance. Nothing to sleep or close.
                    ready,
                    state = ready ? "ready" : asleep ? "asleep" : active ? "active" : Saver != Mode.Off ? "saver" : "background",
                    pid = pid == 0 ? (int?)null : pid,
                    memory = f?.MemoryMB,
                    cpu = f?.CpuPercent,
                    gpu = f?.GpuPercent,
                    shared = sharing > 1 ? sharing : 0,
                    blocked = t.ShieldsApply ? t.BlockedOnPage : 0,

                    // M4.6 — what this tab has loaded since the pane was opened, as the sites
                    // declared it, and how many responses never said.
                    data = t.DataBytes,
                    dataUnknown = t.DataUnknown,
                    canSleep = !ready && !active && !asleep && !t.IsInternalPage,
                });
            }
        }

        var payload = new
        {
            memory = r.MemoryMB,
            cpu = r.CpuPercent,
            gpu = r.GpuAvailable ? r.GpuPercent : (double?)null,
            gpuMemory = r.GpuAvailable ? r.GpuMemoryMB : (double?)null,
            ui = new { now = r.UiNowMs, worst = r.UiWorstMs },
            blocker = new { average = r.BlockerAverageUs, worst = r.BlockerWorstUs, checks = r.BlockerChecks },
            processes = r.Processes
                .OrderBy(p => p.Kind switch { "Aviary" => 0, "Browser" => 1, "Gpu" => 2, "Utility" => 3, _ => 4 })
                .ThenByDescending(p => p.MemoryMB)
                .Select(p => new
                {
                    pid = p.Pid,
                    kind = KindName(p.Kind),
                    memory = p.MemoryMB,
                    cpu = p.CpuPercent,
                    gpu = p.GpuPercent,
                    gpuMemory = p.GpuMemoryMB,
                    handles = p.Handles,
                    tabs = _perfTabsByPid.TryGetValue(p.Pid, out List<BrowserTab>? on) ? on.Count : 0,
                }),
            tabs,
            session = new
            {
                uptimeSeconds = (int)Diagnostics.Uptime.TotalSeconds,
                startupMs = Diagnostics.StartupMs,
                windows = OpenWindows.Count,
                rules = FilterLists.Engine is { } engine ? engine.Stats.Network + engine.Stats.HostOnly : 0,
            },
            samplerMs = r.SamplerMs,

            // M4.6 — memory Aviary has asked for and the most it has held, the GPU split by the
            // kind of work, and how often all of this is measured.
            commit = r.CommitMB,
            peak = r.PeakMB,
            gpuEngines = r.GpuByEngine
                .Where(p => p.Value >= 0.05)
                .OrderByDescending(p => p.Value)
                .Select(p => new { name = EngineName(p.Key), percent = p.Value }),
            // The rate the sampler is REALLY using, not the setting (bug 92: they could differ).
            everySeconds = LiveStats.EveryMs / 1000,

            // M6: while the corner readout is on, measuring does not stop when this page is left.
            overlay = PerfOverlayOn,
        };

        try
        {
            _ = page.ExecuteScriptAsync("window.__aviaryPerf && window.__aviaryPerf(" + JsonSerializer.Serialize(payload) + ")");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "performance: push");
        }
    }

    /// <summary>The page's Sleep and Close buttons: "sleep:42" / "close:42".</summary>
    private async Task PerfTabAsync(string arg)
    {
        string[] parts = arg.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], out int id))
        {
            return;
        }

        foreach (MainWindow window in OpenWindows.ToList())
        {
            BrowserTab? tab = window._tabs.Find(t => t.Id == id);
            if (tab is null)
            {
                continue;
            }

            if (parts[0] == "close")
            {
                window.CloseTab(tab);
            }
            else if (parts[0] == "sleep" && !ReferenceEquals(window._active, tab) && tab.View.CoreWebView2 is CoreWebView2 core)
            {
                try
                {
                    bool slept = await core.TrySuspendAsync();

                    // Bug 97: refused perhaps only because a new engine counted it as on screen again - out of
                    // sight once more (MainWindow.MemorySaver.cs), and asked again a moment later: asked straight
                    // after, the engine had not taken the hide in yet.
                    if (!slept && window.NeedsRehide(tab))
                    {
                        Rehide(tab);
                        await Task.Delay(250);
                        slept = window.NeedsRehide(tab) && await core.TrySuspendAsync();
                    }

                    DiagFeature("perf-sleep-tab", new { tab = id, slept });
                    if (!slept && _perfPage is CoreWebView2 page)
                    {
                        // The engine decides: a page playing sound or video, or in the middle of
                        // something, is not put to sleep. Say so rather than leave the button looking dead.
                        _ = page.ExecuteScriptAsync("window.__aviaryPerfNote && window.__aviaryPerfNote(" + id + ", " +
                            JsonSerializer.Serialize("The engine kept this tab awake — a page that is playing sound or video, or busy, is not put to sleep.") + ")");
                    }
                }
                catch (Exception ex)
                {
                    Diagnostics.Swallowed(ex, "performance: sleep a tab");
                }
            }

            return;
        }
    }
}
