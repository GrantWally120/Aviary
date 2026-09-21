using System;
using Microsoft.UI.Xaml.Media;

namespace Aviary;

/// <summary>
/// Frame timing (Alpha Build 5 M4.9), and the one measurement in Aviary that is OFF by default
/// while collection is on.
///
/// <para>PHASE5-PLAN listed "frame timing" under category 13 from the start, and M1 did not build
/// it for a reason that is still true: WinUI exposes per-frame times only through
/// <see cref="CompositionTarget.Rendering"/>, and subscribing to that <b>forces the compositor to
/// redraw every frame forever</b> — it measures smoothness by destroying it. The watchdog measures
/// responsiveness instead, which is honest and free.</para>
///
/// <para>Grant asked on 21 Sep 2026 for the collection to cover everything. So it is here, and it
/// is a row of its own rather than something the improvement switch turns on: a person switching
/// on "help improve Aviary" is not agreeing to make Aviary slower. The row says the cost in the
/// same breath as the offer, and the figures carry <c>costsWhatItMeasures: true</c> so nobody
/// later compares a frame-timed session against a normal one without knowing.</para>
///
/// <para>What it records once a minute: how many frames were drawn, the median and 95th-percentile
/// gap between them, and how many gaps were longer than 33 ms (a dropped frame at 30fps) — never
/// a per-frame line, which would be tens of thousands of records a minute.</para>
/// </summary>
internal static partial class Diagnostics
{
    public const string FramesKey = "frameTiming";

    /// <summary>
    /// Seconds between reports instead of a minute, for tests. Environment only — nothing inside
    /// the browser can set it, the AVIARY_CRASH_TEST / AVIARY_SLEEP_TEST rule.
    /// </summary>
    private static readonly int FramesTestSeconds =
        int.TryParse(Environment.GetEnvironmentVariable("AVIARY_FRAMES_TEST"), out int s) && s > 0 ? s : 0;

    private static double FramesEverySeconds => FramesTestSeconds > 0 ? FramesTestSeconds : 60;

    private static bool _framesOn;
    private static long _lastFrameStamp;
    private static readonly object FrameGate = new();
    private static readonly System.Collections.Generic.List<double> FrameGaps = new();
    private static long _framesReportedAt;

    /// <summary>Starts or stops frame timing to match the setting. UI thread only.</summary>
    public static void ApplyFrameTiming()
    {
        try
        {
            bool want = _mode == 1 && AppSettings.GetBool(FramesKey);
            if (want == _framesOn)
            {
                return;
            }

            _framesOn = want;
            if (want)
            {
                _lastFrameStamp = 0;
                _framesReportedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                CompositionTarget.Rendering += OnFrame;
            }
            else
            {
                CompositionTarget.Rendering -= OnFrame;
                lock (FrameGate)
                {
                    FrameGaps.Clear();
                }
            }
        }
        catch (Exception ex)
        {
            Swallowed(ex, "diagnostics: frame timing");
        }
    }

    private static void OnFrame(object? sender, object e)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long previous = _lastFrameStamp;
        _lastFrameStamp = now;

        if (previous != 0)
        {
            double gap = System.Diagnostics.Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds;

            // A gap longer than a second is Aviary having been idle, not a stutter: the compositor
            // stops between frames when nothing moves, even with this subscribed.
            if (gap <= 1000)
            {
                lock (FrameGate)
                {
                    FrameGaps.Add(gap);
                }
            }
        }

        if (System.Diagnostics.Stopwatch.GetElapsedTime(_framesReportedAt, now).TotalSeconds < FramesEverySeconds)
        {
            return;
        }

        _framesReportedAt = now;
        ReportFrames();
    }

    private static void ReportFrames()
    {
        double[] gaps;
        lock (FrameGate)
        {
            if (FrameGaps.Count < 10)
            {
                FrameGaps.Clear();
                return;
            }

            gaps = FrameGaps.ToArray();
            FrameGaps.Clear();
        }

        Array.Sort(gaps);
        int dropped = 0;
        foreach (double g in gaps)
        {
            if (g > 33)
            {
                dropped++;
            }
        }

        Perf("frames", new
        {
            frames = gaps.Length,
            medianMs = Math.Round(gaps[gaps.Length / 2], 2),
            p95Ms = Math.Round(gaps[(int)(gaps.Length * 0.95)], 2),
            longestMs = Math.Round(gaps[^1], 2),
            droppedOver33Ms = dropped,

            // Never let this be compared with a session that was not paying for it.
            costsWhatItMeasures = true,
        });
    }
}
