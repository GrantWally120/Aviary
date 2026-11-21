using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Aviary;

/// <summary>
/// Beta Prep 2 — Quik's numbers, measured on THIS computer (Grant, 21 Nov 2026: "significantly
/// improve ... Quik Mode"). Until now Settings → Quik could only quote the development PC's
/// figures from 20 October; a person switching Quik on had no way to see what it did for them.
///
/// <para><b>What is measured</b>, whether Quik is on or off, so the two can be compared:</para>
/// <list type="bullet">
/// <item><b>This launch</b> — from the process starting to the first page being on screen, the same
/// span the startup record has always used.</item>
/// <item><b>New tabs this session</b> — each one's time from being asked for to its page on screen,
/// and whether it came from Quik's ready tab or was made.</item>
/// <item><b>The last launch of each kind</b> — one number and one date for "with Quik" and one for
/// "without", kept in <c>quik-stats.json</c> in the profile so a launch can be set beside the other
/// kind. Timings only: no address, no title, nothing about what was opened. It is in the Privacy
/// Policy, never leaves this computer, and Clear browsing data's "everything" removes it with the
/// rest of the profile's small files.</item>
/// </list>
/// <para>A private window's tabs are counted like any other: a time and a yes/no say nothing about
/// what was in them.</para>
/// </summary>
internal static class QuikStats
{
    private static readonly object Gate = new();
    private static readonly List<(double Ms, bool Ready)> Opens = new();
    private static readonly long Started = Stopwatch.GetTimestamp();
    private static double _launchMs;
    private static Saved? _saved;

    private sealed class Saved
    {
        public double OnMs { get; set; }
        public string? OnAt { get; set; }
        public double OffMs { get; set; }
        public string? OffAt { get; set; }
    }

    private static string StorePath => AppPaths.Of("quik-stats.json");

    /// <summary>The process start, for the launch time: Windows' own figure, or this class's first use.</summary>
    private static double SinceProcessStartMs()
    {
        try
        {
            return (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik stats: process start time");
            return Stopwatch.GetElapsedTime(Started).TotalMilliseconds;
        }
    }

    /// <summary>The first page of this launch is on screen. Once.</summary>
    public static void FirstPage()
    {
        double ms = Math.Round(SinceProcessStartMs(), 0);
        lock (Gate)
        {
            if (_launchMs > 0 || ms <= 0 || ms > 120_000)
            {
                return;
            }

            _launchMs = ms;
            Saved saved = Load();
            string today = DateTime.Now.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
            if (Quik.On)
            {
                saved.OnMs = ms;
                saved.OnAt = today;
            }
            else
            {
                saved.OffMs = ms;
                saved.OffAt = today;
            }

            Save(saved);
        }
    }

    /// <summary>A new tab's page reached the screen: how long from the request, and whether it was a ready tab.</summary>
    public static void TabOpened(double ms, bool ready)
    {
        if (ms < 0 || ms > 60_000)
        {
            return;
        }

        lock (Gate)
        {
            Opens.Add((ms, ready));
            if (Opens.Count > 400)
            {
                Opens.RemoveRange(0, Opens.Count - 400);
            }
        }
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        values.Sort();
        int mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }

    /// <summary>What Settings → Quik draws. <paramref name="readyWaiting"/> is the asking window's pool.</summary>
    public static object Describe(int readyWaiting)
    {
        lock (Gate)
        {
            Saved saved = Load();
            List<double> ready = Opens.Where(o => o.Ready).Select(o => o.Ms).ToList();
            List<double> made = Opens.Where(o => !o.Ready).Select(o => o.Ms).ToList();
            return new
            {
                on = Quik.On,
                launchMs = _launchMs,
                lastOnMs = saved.OnMs,
                lastOnAt = saved.OnAt,
                lastOffMs = saved.OffMs,
                lastOffAt = saved.OffAt,
                tabs = Opens.Count,
                readyTabs = ready.Count,
                readyMedianMs = Math.Round(Median(ready), 1),
                madeTabs = made.Count,
                madeMedianMs = Math.Round(Median(made), 1),
                readyWaiting,
            };
        }
    }

    private static Saved Load()
    {
        if (_saved is not null)
        {
            return _saved;
        }

        try
        {
            if (File.Exists(StorePath))
            {
                _saved = JsonSerializer.Deserialize<Saved>(File.ReadAllText(StorePath));
            }
        }
        catch (Exception ex)
        {
            // Two timings are not worth a failed launch: start again from nothing.
            Diagnostics.Swallowed(ex, "quik stats: load");
        }

        return _saved ??= new Saved();
    }

    private static void Save(Saved saved)
    {
        try
        {
            string temp = StorePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(saved));
            File.Move(temp, StorePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik stats: save");
        }
    }
}
