using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Aviary;

/// <summary>
/// Crash protection for the Experimental menu (Alpha Build 5 M6), and deliberately NOT part of
/// diagnostics: staying safe must never depend on opting in to data collection. One tiny file in
/// the profile, <c>experiments-safety.json</c>, always kept.
///
/// <para><b>The rule.</b> If Aviary — or its browser engine — stops unexpectedly SOON after the
/// experiments were changed, the next launch starts with every experiment off and says so, with
/// Turn Them Back On / Keep Them Off. "Soon" = before Aviary has started and closed normally twice
/// with the experiments as they now are. After that, a crash is not blamed on them.</para>
///
/// <para>"Stops unexpectedly" is any of: the process ended without reaching a normal close (a
/// fatal or native crash, a kill, a power cut — the file still says <c>running</c>); a UI-thread
/// failure Aviary survived (App.xaml.cs); the engine's browser or graphics process exiting
/// unexpectedly (BrowserTab — but not while Windows is shutting down, bug 83's rule). A page's own
/// renderer crashing does not count: websites do that by themselves.</para>
///
/// <para>Most experiments change the engine's switches, which only apply at launch — so a crash
/// they cause comes in the session AFTER the one they were switched on in. That session is the one
/// the rule is built around: <c>cleanRuns</c> counts normal closes of sessions that STARTED after
/// the last change, so the session in which the change was made never counts towards trusting it.</para>
/// </summary>
internal static class SafetyMarker
{
    /// <summary>How many normal runs with the experiments as they are before a crash is no longer blamed on them.</summary>
    public const int TrustAfter = 2;

    private static readonly object Gate = new();
    private static State _state = new();
    private static List<string> _switchedOff = new();
    private static string? _why;

    private sealed class State
    {
        public bool Running { get; set; }
        public DateTimeOffset? Started { get; set; }
        public DateTimeOffset? ChangedAt { get; set; }
        public int CleanRuns { get; set; }
        public string? Problem { get; set; }
    }

    private static string FilePath => Path.Combine(AppPaths.Root, "experiments-safety.json");

    /// <summary>
    /// From App's constructor — BEFORE the engine's switches are built (the first tab), so
    /// experiments switched off here never reach the engine at all.
    /// </summary>
    public static void AtLaunch()
    {
        lock (Gate)
        {
            State before = Read() ?? new State();
            string? problem = before.Problem ?? (before.Running ? "it did not close normally" : null);

            if (problem is not null && before.CleanRuns < TrustAfter)
            {
                List<string> were = Experiments.TurnAllOff(quikToo: true);
                if (were.Count > 0)
                {
                    _switchedOff = were;
                    _why = problem;
                    Diagnostics.Feature("experiments-off-after-problem", new { problem, experiments = were });
                    before.ChangedAt = DateTimeOffset.Now;
                    before.CleanRuns = 0;
                }
            }

            _state = new State
            {
                Running = true,
                Started = DateTimeOffset.Now,
                ChangedAt = before.ChangedAt,
                CleanRuns = before.CleanRuns,
                Problem = null,
            };
            Write();
        }
    }

    /// <summary>What the launch switched off and why — handed out ONCE, to the first window that asks.</summary>
    public static (List<string> Keys, string Why)? TakeSwitchedOff()
    {
        lock (Gate)
        {
            if (_switchedOff.Count == 0)
            {
                return null;
            }

            (List<string>, string) taken = (_switchedOff, _why ?? string.Empty);
            _switchedOff = new List<string>();
            return taken;
        }
    }

    /// <summary>An experiment was switched on or off (by a person, or by "Turn every experiment off").</summary>
    public static void ExperimentChanged()
    {
        lock (Gate)
        {
            _state.ChangedAt = DateTimeOffset.Now;
            _state.CleanRuns = 0;
            Write();
        }
    }

    /// <summary>Something went wrong that Aviary survived. Kept in the file, so the next launch knows.</summary>
    public static void Problem(string what)
    {
        lock (Gate)
        {
            if (_state.Problem is not null)
            {
                return;
            }

            _state.Problem = what;
            Write();
        }
    }

    /// <summary>A normal close: the last window closed, or Windows ended the session.</summary>
    public static void CleanEnd()
    {
        lock (Gate)
        {
            if (!_state.Running)
            {
                return;
            }

            _state.Running = false;
            bool startedAfterChange = _state.ChangedAt is not { } changed || (_state.Started is { } s && s > changed);
            if (_state.Problem is null && startedAfterChange)
            {
                _state.CleanRuns++;
            }

            Write();
        }
    }

    private static State? Read()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath)) : null;
        }
        catch (Exception ex)
        {
            // A damaged marker is treated as none: the worst case is one crash not being caught.
            Diagnostics.Swallowed(ex, "experiments: read the safety marker");
            return null;
        }
    }

    // Small and synchronous on purpose: it has to be on disk before a crash that may come next.
    private static void Write()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_state));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "experiments: write the safety marker");
        }
    }
}
