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
}
