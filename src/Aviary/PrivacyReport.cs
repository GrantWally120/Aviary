using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// What the blocker stopped, in %LOCALAPPDATA%\Aviary\privacy.json — the last seven days day
/// by day, and a running all-time total.
///
/// TrackerBlocker only ever had a session counter, which resets the moment you quit — so
/// the number could never say anything more interesting than "since you opened the app".
/// Safari's line is "In the last seven days, Safari has prevented N trackers from profiling
/// you", and seven days is the point: it is long enough to be a real number and short
/// enough to still be about you now. Grant asked (12 Sep) for an all-time view beside it,
/// chosen in Settings, and for Brave's three figures — blocked, bandwidth saved, time saved.
///
/// What is stored is deliberately thin. A count per day, a count per tracker domain, and a
/// count per site you visited — never a pairing of the two. Recording "site X contacted
/// tracker Y at time T" would build exactly the browsing profile the blocker exists to
/// prevent, which would be an absurd thing for a privacy feature to keep on disk.
///
/// Bandwidth and time are ESTIMATES and say so everywhere they appear: a request that was
/// never made has no real size. Each block is counted at a typical size for its kind (see
/// <see cref="EstimateBytes"/>) and at <see cref="MsPerBlock"/> of loading time.
/// </summary>
internal static class PrivacyReport
{
    private const int Days = 7;

    /// <summary>The loading time one blocked request is estimated to save.</summary>
    public const int MsPerBlock = 50;

    private sealed class Model
    {
        /// <summary>Local date (yyyy-MM-dd) to blocked count.</summary>
        public Dictionary<string, int> ByDay { get; set; } = new();

        /// <summary>Local date to estimated bytes not downloaded.</summary>
        public Dictionary<string, long> BytesByDay { get; set; } = new();

        /// <summary>Tracker domain to blocked count.</summary>
        public Dictionary<string, int> ByTracker { get; set; } = new();

        /// <summary>Site you were on to blocked count.</summary>
        public Dictionary<string, int> BySite { get; set; } = new();

        /// <summary>Every request blocked since <see cref="Since"/>.</summary>
        public long Total { get; set; }

        /// <summary>Estimated bytes not downloaded since <see cref="Since"/>.</summary>
        public long TotalBytes { get; set; }

        /// <summary>The first day anything was recorded (yyyy-MM-dd), or empty.</summary>
        public string Since { get; set; } = string.Empty;
    }

    private static readonly object Gate = new();
    private static Model? _model;
    private static int _saveScheduled;

    private static string FilePath => AppPaths.Of("privacy.json");

    private static Model Data
    {
        get
        {
            lock (Gate)
            {
                if (_model is not null)
                {
                    return _model;
                }

                _model = new Model();

                try
                {
                    if (File.Exists(FilePath))
                    {
                        _model = JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath)) ?? new Model();
                    }
                }
                catch (Exception ex)
                {
                    _model = new Model();
                    Diagnostics.Swallowed(ex, "privacy report: load privacy.json");
                }

                Migrate(_model);
                Prune(_model);
                return _model;
            }
        }
    }

    /// <summary>
    /// A report written before the all-time total existed has only its seven days: those
    /// become the start of the total, dated from the oldest of them, and their bandwidth is
    /// estimated at the average block's size since their kinds were never kept.
    /// </summary>
    private static void Migrate(Model model)
    {
        if (model.Total == 0 && model.ByDay.Count > 0)
        {
            model.Total = model.ByDay.Values.Sum();
            model.Since = model.ByDay.Keys.Min() ?? string.Empty;
        }

        foreach ((string day, int count) in model.ByDay)
        {
            if (!model.BytesByDay.ContainsKey(day))
            {
                long bytes = count * AverageBlockBytes;
                model.BytesByDay[day] = bytes;
                model.TotalBytes += bytes;
            }
        }
    }

    private static void Prune(Model model)
    {
        HashSet<string> keep = new(Enumerable.Range(0, Days)
            .Select(i => DateTime.Now.Date.AddDays(-i).ToString("yyyy-MM-dd")));

        foreach (string day in model.ByDay.Keys.Where(d => !keep.Contains(d)).ToList())
        {
            model.ByDay.Remove(day);
        }

        foreach (string day in model.BytesByDay.Keys.Where(d => !keep.Contains(d)).ToList())
        {
            model.BytesByDay.Remove(day);
        }

        // The domain tallies are not dated, so they are capped instead of pruned — the
        // alternative is dating every one of them, which is more retained data for a
        // feature whose entire purpose is retaining less.
        Trim(model.ByTracker, 200);
        Trim(model.BySite, 200);
    }

    private static void Trim(Dictionary<string, int> map, int limit)
    {
        if (map.Count <= limit)
        {
            return;
        }

        foreach (string key in map.OrderBy(p => p.Value).Take(map.Count - limit).Select(p => p.Key).ToList())
        {
            map.Remove(key);
        }
    }

    /// <summary>What an average blocked request is counted at when its kind is unknown.</summary>
    private const long AverageBlockBytes = 8 * 1024;

    /// <summary>
    /// A typical transfer size for a request of this kind — deliberately conservative. Ad and
    /// analytics scripts commonly run 20–100 KB; tracking pixels are a few bytes; an ad frame's
    /// document is the doorway to many more requests that are then never made either.
    /// </summary>
    public static long EstimateBytes(CoreWebView2WebResourceContext kind) => kind switch
    {
        CoreWebView2WebResourceContext.Script => 25 * 1024,
        CoreWebView2WebResourceContext.Document => 40 * 1024,
        CoreWebView2WebResourceContext.Media => 150 * 1024,
        CoreWebView2WebResourceContext.Font => 20 * 1024,
        CoreWebView2WebResourceContext.Stylesheet => 8 * 1024,
        CoreWebView2WebResourceContext.Image => 4 * 1024,
        _ => 1024,
    };

    /// <summary>Called for every request the blocker stops.</summary>
    public static void Record(string trackerHost, string siteHost,
        CoreWebView2WebResourceContext kind = CoreWebView2WebResourceContext.Other)
    {
        if (string.IsNullOrEmpty(trackerHost))
        {
            return;
        }

        System.Threading.Interlocked.Increment(ref _sessionBlocked);
        string today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        string tracker = PublicSuffix.RegistrableDomain(trackerHost);
        long bytes = EstimateBytes(kind);

        lock (Gate)
        {
            Model model = Data;
            model.ByDay[today] = model.ByDay.GetValueOrDefault(today) + 1;
            model.BytesByDay[today] = model.BytesByDay.GetValueOrDefault(today) + bytes;
            model.ByTracker[tracker] = model.ByTracker.GetValueOrDefault(tracker) + 1;
            model.Total++;
            model.TotalBytes += bytes;

            if (model.Since.Length == 0)
            {
                model.Since = today;
            }

            if (!string.IsNullOrEmpty(siteHost))
            {
                string site = PublicSuffix.RegistrableDomain(siteHost);
                model.BySite[site] = model.BySite.GetValueOrDefault(site) + 1;
            }
        }

        // Every block schedules a save; the debounce inside ScheduleSave coalesces a burst
        // of them into one write a few seconds later.
        //
        // This used to save only every HUNDREDTH block, with nothing saving on exit — so an
        // ordinary session never reached the threshold and the whole "seven-day" report reset
        // to zero on every launch. Found in Phase 5 M0: 23 trackers blocked on The Verge,
        // shown on the Start Page, and privacy.json did not exist.
        ScheduleSave();
    }

    /// <summary>Blocked counts for the last seven days, oldest first, today last.</summary>
    public static int[] LastWeek()
    {
        lock (Gate)
        {
            Model model = Data;
            int[] days = new int[Days];
            for (int i = 0; i < Days; i++)
            {
                string key = DateTime.Now.Date.AddDays(-(Days - 1 - i)).ToString("yyyy-MM-dd");
                days[i] = model.ByDay.GetValueOrDefault(key);
            }

            return days;
        }
    }

    public static int WeekTotal() => LastWeek().Sum();

    /// <summary>
    /// The three figures for the chosen range: requests blocked, estimated bytes not
    /// downloaded, estimated seconds not spent loading — and, for all time, since when.
    /// </summary>
    /// <summary>Blocked since Aviary started, for Shields’ own figures (M4.5). Not saved anywhere.</summary>
    public static long SessionBlocked => System.Threading.Interlocked.Read(ref _sessionBlocked);

    private static long _sessionBlocked;

    public static (long Blocked, long Bytes, double Seconds, string Since) Stats(bool allTime)
    {
        lock (Gate)
        {
            Model model = Data;
            long blocked = allTime ? model.Total : WeekTotal();
            long bytes = allTime ? model.TotalBytes : model.BytesByDay.Values.Sum();
            return (blocked, bytes, blocked * MsPerBlock / 1000.0, allTime ? model.Since : string.Empty);
        }
    }

    /// <summary>The tracker seen most often, for the "Most often:" line.</summary>
    public static string TopTracker()
    {
        lock (Gate)
        {
            return Data.ByTracker.Count == 0
                ? string.Empty
                : Data.ByTracker.OrderByDescending(p => p.Value).First().Key;
        }
    }

    /// <summary>The sites whose pages contacted the most trackers, for the full report.</summary>
    public static List<(string Site, int Count)> TopSites(int limit = 10)
    {
        lock (Gate)
        {
            return Data.BySite
                .OrderByDescending(p => p.Value)
                .Take(limit)
                .Select(p => (p.Key, p.Value))
                .ToList();
        }
    }

    public static List<(string Tracker, int Count)> TopTrackers(int limit = 10)
    {
        lock (Gate)
        {
            return Data.ByTracker
                .OrderByDescending(p => p.Value)
                .Take(limit)
                .Select(p => (p.Key, p.Value))
                .ToList();
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _model = new Model();
        }

        // "Start the count again" means every figure Shields shows, this session's included
        // (M4.5) - otherwise the page would say 0 all time and 13 since Aviary started.
        System.Threading.Interlocked.Exchange(ref _sessionBlocked, 0);

        Save();
    }

    private static void ScheduleSave()
    {
        if (Interlocked.Exchange(ref _saveScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(4000);
            Interlocked.Exchange(ref _saveScheduled, 0);
            Save();
        });
    }

    public static void Save()
    {
        try
        {
            string json;
            lock (Gate)
            {
                Prune(Data);
                json = JsonSerializer.Serialize(Data);
            }

            string folder = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(folder);

            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "privacy report: save privacy.json");
        }
    }
}
