using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>
/// The seven-day rolling record of what the blocker stopped, in
/// %LOCALAPPDATA%\Aviary\privacy.json.
///
/// TrackerBlocker only ever had a session counter, which resets the moment you quit — so
/// the number could never say anything more interesting than "since you opened the app".
/// Safari's line is "In the last seven days, Safari has prevented N trackers from profiling
/// you", and seven days is the point: it is long enough to be a real number and short
/// enough to still be about you now.
///
/// What is stored is deliberately thin. A count per day, a count per tracker domain, and a
/// count per site you visited — never a pairing of the two. Recording "site X contacted
/// tracker Y at time T" would build exactly the browsing profile the blocker exists to
/// prevent, which would be an absurd thing for a privacy feature to keep on disk.
/// </summary>
internal static class PrivacyReport
{
    private const int Days = 7;

    private sealed class Model
    {
        /// <summary>Local date (yyyy-MM-dd) to blocked count.</summary>
        public Dictionary<string, int> ByDay { get; set; } = new();

        /// <summary>Tracker domain to blocked count.</summary>
        public Dictionary<string, int> ByTracker { get; set; } = new();

        /// <summary>Site you were on to blocked count.</summary>
        public Dictionary<string, int> BySite { get; set; } = new();
    }

    private static readonly object Gate = new();
    private static Model? _model;
    private static int _saveScheduled;
    private static int _sinceSave;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aviary", "privacy.json");

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
                catch (Exception)
                {
                    _model = new Model();
                }

                Prune(_model);
                return _model;
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

    /// <summary>Called for every request the blocker stops.</summary>
    public static void Record(string trackerHost, string siteHost)
    {
        if (string.IsNullOrEmpty(trackerHost))
        {
            return;
        }

        string today = DateTime.Now.Date.ToString("yyyy-MM-dd");
        string tracker = PublicSuffix.RegistrableDomain(trackerHost);

        lock (Gate)
        {
            Model model = Data;
            model.ByDay[today] = model.ByDay.GetValueOrDefault(today) + 1;
            model.ByTracker[tracker] = model.ByTracker.GetValueOrDefault(tracker) + 1;

            if (!string.IsNullOrEmpty(siteHost))
            {
                string site = PublicSuffix.RegistrableDomain(siteHost);
                model.BySite[site] = model.BySite.GetValueOrDefault(site) + 1;
            }

            _sinceSave++;
        }

        // Every block schedules a save; the debounce inside ScheduleSave coalesces a burst
        // of them into one write a few seconds later.
        //
        // This used to save only every HUNDREDTH block, with nothing saving on exit — so an
        // ordinary session never reached the threshold and the whole "seven-day" report reset
        // to zero on every launch. Found in Phase 5 M0: 23 trackers blocked on The Verge,
        // shown on the Start Page, and privacy.json did not exist.
        _sinceSave = 0;
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
        catch (Exception)
        {
        }
    }
}
