using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace Aviary;

/// <summary>
/// Memory saver (Alpha Build 5 M4.8). Grant: "memory saver improve that".
///
/// Until M4.8 the whole feature was one line: a background tab was told it MAY release caches
/// and dropped to a low memory priority (<see cref="BrowserTab.SetBackgrounded"/>). That is a
/// hint to the engine and nothing more — it never gave back the tens of megabytes a loaded page
/// actually holds, and the Performance page could show a background tab still sitting on 300 MB
/// with "memory saver" written under it, which is the "live but inert" shape this whole build
/// exists to eliminate.
///
/// Now the hint is the FLOOR and sleeping is the rest of it: a tab nobody has looked at for a
/// while is suspended outright, which really does hand the memory back, and it wakes the moment
/// it is shown again (<see cref="BrowserTab.Wake"/>).
///
/// What makes this safe to do on a timer rather than only on a button:
/// <list type="bullet">
/// <item>The ENGINE decides. <c>TrySuspendAsync</c> refuses for a page playing sound or video,
/// or one in the middle of something — so background music does not stop.</item>
/// <item>Never the tab being looked at, never a tab in a window whose tab it is, and never one
/// of Aviary's own pages (Settings measures itself; sleeping it would be measuring nothing).</item>
/// <item>Never in a private window: a private tab that reloads on waking would have to go back
/// to the site, and private browsing is meant to leave as little trace as possible.</item>
/// <item>It waits for real idleness, counted from when the tab was last the one on screen.</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Checks for tabs to put to sleep. One per window; cheap, and off when nothing can sleep.</summary>
    private DispatcherTimer? _sleepTimer;

    /// <summary>
    /// Seconds to wait instead of the setting's minutes, for tests (M4.8). Environment only —
    /// nothing inside the browser can set it, the same rule as AVIARY_CRASH_TEST and
    /// AVIARY_STALL_TEST. Without it a run of this feature takes five minutes to see once.
    /// </summary>
    private static readonly int TestSeconds =
        int.TryParse(Environment.GetEnvironmentVariable("AVIARY_SLEEP_TEST"), out int s) && s > 0 ? s : 0;

    /// <summary>
    /// How hard memory saver works (M4.9, Grant's own names and his own description of each).
    /// One row now, where M4.8 had a switch and a separate timer.
    /// </summary>
    internal enum Mode
    {
        /// <summary>Nothing at all: every tab keeps normal priority and none is ever slept.</summary>
        Off,

        /// <summary>"Slower and less offload" — the hint, and sleep only after a full hour.</summary>
        Chill,

        /// <summary>"Well balanced", and the default.</summary>
        Balanced,

        /// <summary>"Fastest and most" — five minutes, and it lets go the moment the window is minimised.</summary>
        Aggressive,
    }

    internal static Mode Saver => AppSettings.Get("memorySaver") switch
    {
        "Off" => Mode.Off,
        "Chill" => Mode.Chill,
        "Aggressive" => Mode.Aggressive,
        _ => Mode.Balanced,
    };

    /// <summary>How long a tab must be left alone, or null when sleeping is switched off.</summary>
    public const string QuikKeepAwakeKey = "quikKeepAwake";

    /// <summary>Quik's "Keep recent tabs awake" - only while Aviary runs Quik.</summary>
    private static bool KeepAwake => Quik.On && AppSettings.GetBool(QuikKeepAwakeKey);

    /// <summary>Half an hour; with AVIARY_SLEEP_TEST, four of its test "idle times" so a test can see both sides.</summary>
    private static TimeSpan KeepAwakeFor => TestSeconds > 0 ? TimeSpan.FromSeconds(TestSeconds * 4) : TimeSpan.FromMinutes(30);

    internal static TimeSpan? SleepAfter
    {
        get
        {
            Mode mode = Saver;
            if (mode == Mode.Off)
            {
                return null;
            }

            if (TestSeconds > 0)
            {
                return TimeSpan.FromSeconds(TestSeconds);
            }

            return mode switch
            {
                Mode.Chill => TimeSpan.FromHours(1),
                Mode.Aggressive => TimeSpan.FromMinutes(5),
                _ => TimeSpan.FromMinutes(30),
            };
        }
    }

    /// <summary>
    /// Starts or stops the idle check. Called at start-up and whenever the setting changes, so
    /// turning sleeping off stops the timer rather than leaving it running and doing nothing.
    /// </summary>
    private void ApplyMemorySaver()
    {
        try
        {
            if (SleepAfter is null)
            {
                _sleepTimer?.Stop();
                _sleepTimer = null;
                return;
            }

            if (_sleepTimer is not null)
            {
                return;
            }

            // A minute is far finer than any of the choices needs; the work each tick is a
            // subtraction per tab unless something is actually due.
            _sleepTimer = new DispatcherTimer
            {
                Interval = TestSeconds > 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromMinutes(1),
            };
            _sleepTimer.Tick += (_, _) => _ = SleepIdleTabsAsync();
            _sleepTimer.Start();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "memory saver: start");
        }
    }

    /// <summary>
    /// Aggressive lets go as soon as the window is minimised (M4.9): nothing on screen is being
    /// looked at, so every background tab may sleep at once rather than waiting out its five
    /// minutes. The active tab is still spared — it is what you come back to.
    /// </summary>
    internal void OnWindowMinimised()
    {
        if (Saver == Mode.Aggressive)
        {
            _ = SleepIdleTabsAsync(ignoreIdleTime: true);
        }
    }

    /// <summary>UI thread: every tab that has earned a sleep, offered to the engine one at a time.</summary>
    private async Task SleepIdleTabsAsync(bool ignoreIdleTime = false)
    {
        if (SleepAfter is not TimeSpan after)
        {
            return;
        }

        try
        {
            DateTime cutoff = ignoreIdleTime ? DateTime.UtcNow : DateTime.UtcNow - after;
            List<BrowserTab> due = new();

            foreach (BrowserTab tab in _tabs)
            {
                if (ReferenceEquals(tab, _active) || tab.IsPrivate || tab.IsInternalPage || tab.IsAsleep)
                {
                    continue;
                }

                // M7 Quik, "Keep recent tabs awake": a tab used in the last half hour is left alone, so going back to
                // it is instant - at the cost of the memory it keeps (said on the row).
                if (KeepAwake && tab.LastActiveUtc > DateTime.UtcNow - KeepAwakeFor)
                {
                    continue;
                }

                if (tab.LastActiveUtc <= cutoff)
                {
                    due.Add(tab);
                }
            }

            foreach (BrowserTab tab in due)
            {
                // The window may have closed the tab while an earlier one was being asked.
                if (!_tabs.Contains(tab) || ReferenceEquals(tab, _active))
                {
                    continue;
                }

                bool slept = await tab.TrySleepAsync();

                // Bug 97: refused - perhaps only because a new engine counted it as on screen again. Put out of sight
                // once more, and the next check asks again: not at once - asked straight after, the engine had not
                // taken the hide in yet (measured: refused; slept at the next check). Once per stay behind, since a
                // page playing sound is refused for real every time.
                if (!slept && !tab.SleepRetried && NeedsRehide(tab))
                {
                    tab.SleepRetried = true;
                    Rehide(tab);
                    Quik.Test("re-hidden after a refusal: tab " + tab.Id);
                }

                if (slept)
                {
                    DiagFeature("memory-saver-slept", new
                    {
                        tab = tab.Id,
                        idleMinutes = (int)(DateTime.UtcNow - tab.LastActiveUtc).TotalMinutes,
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "memory saver: sleep idle tabs");
        }
    }

    private static bool _rehideHooked;

    /// <summary>
    /// AVIARY_REHIDE_TEST=off: bug 97's fix left out, so tools/test/rehide-test.ps1 has its control in the SAME
    /// build; =noqueue: only the re-hides on a refusal (Memory saver, Performance's Sleep), to prove those on
    /// their own. Environment only - nothing inside the browser can set it, the rule for every test hook.
    /// </summary>
    private static readonly string RehideTest = Environment.GetEnvironmentVariable("AVIARY_REHIDE_TEST") ?? string.Empty;

    private static bool RehideOff => RehideTest == "off";

    /// <summary>
    /// <b>Bug 97</b> (found 1 Oct 2026, M7 stage 2, chasing why Quik's background tabs never slept): <b>making a
    /// tab's engine turns the page of every tab already behind back to "visible"</b> - in the engine's eyes only.
    /// Measured (tools/test, regular Aviary, the engine as shipped): a new tab, a link opened behind, Settings or
    /// Quik's ready tab - each left the tabs put behind EARLIER reading <c>document.visibilityState</c> "visible"
    /// for as long as anyone watched (90 s), drawing nothing (0 frames a second, as a hidden one), while switching
    /// between tabs that already exist never did it. A page the engine counts as on screen escapes its background
    /// throttling, and <c>TrySuspendAsync</c> refuses it: Memory saver was refused 46 times in a row for one. Only
    /// the tab put behind at the same moment as the new one is made stays "hidden" (its hide reaches the engine
    /// after); with three tabs or more, every new tab left the older ones behind it counted as on screen until
    /// each was visited again. Quik's ready tab, made again after EVERY new tab, left no tab behind able to sleep.
    ///
    /// <para>The engine is told again that each is out of sight: shown and hidden in the same turn of the
    /// window's thread - nothing is drawn (XAML never composes in between), and the page reads "hidden" again.
    /// It costs ~3-9 ms a tab, so: ONE tab per idle moment of the window's thread, never in a block (twelve tabs
    /// at once took 63 ms), and only once a second has passed since the LAST engine was made - done while the
    /// engine was still starting the new tab, one took 69 ms and the new tab came 70 ms later. Nothing waits
    /// on it: what it restores (sleep, background throttling) works in minutes. Never a tab that is ASLEEP: the
    /// engine wakes a sleeping tab the moment it is shown. And a refusal to sleep puts that one tab out of sight
    /// again (<see cref="SleepIdleTabsAsync"/>, Performance's Sleep).</para>
    /// </summary>
    private static void HookRehide()
    {
        if (_rehideHooked)
        {
            return;
        }

        _rehideHooked = true;
        BrowserTab.EngineMade += made =>
        {
            foreach (MainWindow window in OpenWindows)
            {
                window.QueueRehide(made);
            }
        };
    }

    /// <summary>Tabs waiting to be put out of sight again, one per idle moment.</summary>
    private readonly Queue<BrowserTab> _rehide = new();
    private bool _rehidePosted;

    /// <summary>A second after the last engine made: then the queue starts.</summary>
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _rehideTimer;

    private void QueueRehide(BrowserTab made)
    {
        if (RehideOff || RehideTest == "noqueue")
        {
            return;
        }

        foreach (BrowserTab tab in TabsAndReady())
        {
            if (!ReferenceEquals(tab, made) && NeedsRehide(tab) && !_rehide.Contains(tab))
            {
                _rehide.Enqueue(tab);
            }
        }

        if (_rehideTimer is null)
        {
            _rehideTimer = DispatcherQueue.CreateTimer();
            _rehideTimer.Interval = TimeSpan.FromSeconds(1);
            _rehideTimer.IsRepeating = false;
            _rehideTimer.Tick += (_, _) => PostRehide();
        }

        _rehideTimer.Stop();
        _rehideTimer.Start();
    }

    /// <summary>The window is going: nothing more to put out of sight.</summary>
    private void StopRehide()
    {
        _rehideTimer?.Stop();
        _rehide.Clear();
    }

    private void PostRehide()
    {
        if (_rehidePosted || _rehide.Count == 0)
        {
            return;
        }

        _rehidePosted = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _rehidePosted = false;
            while (_rehide.Count > 0)
            {
                BrowserTab tab = _rehide.Dequeue();

                // Checked again: since it was queued it may have come to the front, gone to sleep or closed.
                if (NeedsRehide(tab) && (_tabs.Contains(tab) || IsQuikReadyTab(tab)))
                {
                    Rehide(tab);
                    break;
                }
            }

            PostRehide();
        });
    }

    /// <summary>Behind, awake, with an engine - the only tabs a new engine turns "visible".</summary>
    private bool NeedsRehide(BrowserTab tab) =>
        !ReferenceEquals(tab, _active) && tab.IsReady && !tab.IsAsleep && tab.View.Visibility == Visibility.Collapsed;

    private static void Rehide(BrowserTab tab)
    {
        if (RehideOff)
        {
            return;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        tab.View.Visibility = Visibility.Visible;
        tab.View.Visibility = Visibility.Collapsed;
        Quik.Test("rehide tab " + tab.Id + " in " + Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3) + " ms");
    }
}
