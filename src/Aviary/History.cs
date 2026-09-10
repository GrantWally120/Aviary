using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>One site you have been to, collapsed across every visit.</summary>
internal sealed class HistoryEntry
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset LastVisitUtc { get; set; }
    public int VisitCount { get; set; }

    [JsonIgnore]
    public string Host => BrowserTab.HostOf(Url);

    [JsonIgnore]
    public string IconKey => Favicons.KeyFor(Url);
}

/// <summary>
/// Where you have been, in %LOCALAPPDATA%\Aviary\history.json.
///
/// One row per URL rather than one per visit. A visit log is what a real browser keeps and
/// it is the right shape for a "what did I look at on Tuesday" view — but it is the wrong
/// shape for the three things Phase 4 actually needs it for: Frequently Visited on the Start
/// Page, ranking in the Smart Search Field, and a history list you can scan. All three want
/// "this site, this many times, most recently then". Collapsing on write keeps the file
/// small enough to load synchronously and makes every read a dictionary probe.
///
/// The cost is that the history page groups by LAST visit, so revisiting a page moves it
/// rather than adding a second entry. That is what Safari's own list looks like in practice
/// and it is the honest trade for not carrying a database.
/// </summary>
internal static class History
{
    private const int MaxEntries = 6000;
    private const int TrimTo = 5000;

    private static readonly object Gate = new();
    private static Dictionary<string, HistoryEntry>? _items;
    private static int _saveScheduled;

    public static event Action? Changed;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aviary", "history.json");

    private static Dictionary<string, HistoryEntry> Items
    {
        get
        {
            lock (Gate)
            {
                if (_items is not null)
                {
                    return _items;
                }

                _items = new Dictionary<string, HistoryEntry>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    if (File.Exists(FilePath))
                    {
                        List<HistoryEntry>? rows =
                            JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(FilePath));

                        if (rows is not null)
                        {
                            foreach (HistoryEntry row in rows)
                            {
                                if (!string.IsNullOrWhiteSpace(row.Url))
                                {
                                    _items[row.Url] = row;
                                }
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // A corrupt history file costs you history, not your browser.
                    _items = new Dictionary<string, HistoryEntry>(StringComparer.OrdinalIgnoreCase);
                }

                return _items;
            }
        }
    }

    /// <summary>
    /// Records a visit. Internal pages, blank tabs and non-web schemes are not history —
    /// a list peppered with "Settings" is noise, and Safari does not record its own pages
    /// either.
    /// </summary>
    public static void Record(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url) || Favicons.KeyFor(url).Length == 0)
        {
            return;
        }

        // Never record while private browsing would be on. Phase 5 adds the mode; the
        // check lives here from the start so the call sites never have to remember it.
        if (AppSettings.GetBool("privateMode"))
        {
            return;
        }

        lock (Gate)
        {
            Dictionary<string, HistoryEntry> items = Items;

            if (items.TryGetValue(url, out HistoryEntry? existing))
            {
                existing.VisitCount++;
                existing.LastVisitUtc = DateTimeOffset.UtcNow;

                // A title arriving late must not blank one we already had.
                if (!string.IsNullOrWhiteSpace(title))
                {
                    existing.Title = title;
                }
            }
            else
            {
                items[url] = new HistoryEntry
                {
                    Url = url,
                    Title = string.IsNullOrWhiteSpace(title) ? BrowserTab.HostOf(url) : title,
                    LastVisitUtc = DateTimeOffset.UtcNow,
                    VisitCount = 1,
                };
            }
        }

        ScheduleSave();
        Changed?.Invoke();
    }

    /// <summary>
    /// Corrects a title without counting a second visit.
    ///
    /// A page's real title usually arrives AFTER the navigation completes, and a
    /// single-page app rewrites it repeatedly while you sit there. Routing those through
    /// Record would turn one visit into five and quietly corrupt the Frequently Visited
    /// ranking on the Start Page.
    /// </summary>
    public static void UpdateTitle(string url, string title)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        bool changed = false;
        lock (Gate)
        {
            if (Items.TryGetValue(url, out HistoryEntry? entry) && entry.Title != title)
            {
                entry.Title = title;
                changed = true;
            }
        }

        if (changed)
        {
            ScheduleSave();
        }
    }

    /// <summary>Most recently visited first.</summary>
    public static List<HistoryEntry> Recent(int limit = 200)
    {
        lock (Gate)
        {
            return Items.Values
                .OrderByDescending(e => e.LastVisitUtc)
                .Take(limit)
                .ToList();
        }
    }

    /// <summary>
    /// The Start Page's Frequently Visited row: one tile per SITE, not per page, ranked by
    /// how much you actually use it. Visiting a site fifty times over a month should beat a
    /// page you opened three times this morning, so recency decays rather than dominating.
    /// </summary>
    public static List<HistoryEntry> TopSites(int limit, IEnumerable<string>? excludeHosts = null)
    {
        HashSet<string> excluded = new(excludeHosts ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (Gate)
        {
            return Items.Values
                .Where(e => e.Host.Length > 0 && !excluded.Contains(e.Host))
                .GroupBy(e => PublicSuffix.RegistrableDomain(e.Host), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(e => Score(e, now)).First())
                .OrderByDescending(e => Score(e, now))
                .Take(limit)
                .ToList();
        }
    }

    /// <summary>Visits, damped by how long ago the last one was. Half-life about a fortnight.</summary>
    private static double Score(HistoryEntry entry, DateTimeOffset now)
    {
        double days = Math.Max(0, (now - entry.LastVisitUtc).TotalDays);
        return entry.VisitCount * Math.Pow(0.5, days / 14.0);
    }

    /// <summary>
    /// Matches for the Smart Search Field. Ranked so that what you meant is first: a host
    /// that STARTS with what you typed beats one that merely contains it, and a site you go
    /// to constantly beats one you saw once.
    /// </summary>
    public static List<HistoryEntry> Search(string query, int limit = 8)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<HistoryEntry>();
        }

        string q = query.Trim().ToLowerInvariant();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (Gate)
        {
            return Items.Values
                .Select(e => (entry: e, rank: MatchRank(e, q)))
                .Where(x => x.rank > 0)
                .OrderByDescending(x => x.rank)
                .ThenByDescending(x => Score(x.entry, now))
                .Take(limit)
                .Select(x => x.entry)
                .ToList();
        }
    }

    private static int MatchRank(HistoryEntry entry, string q)
    {
        string host = entry.Host.ToLowerInvariant();
        string url = entry.Url.ToLowerInvariant();
        string title = (entry.Title ?? string.Empty).ToLowerInvariant();

        if (host.StartsWith(q, StringComparison.Ordinal)) { return 100; }
        if (PublicSuffix.RegistrableDomain(host).StartsWith(q, StringComparison.Ordinal)) { return 90; }
        if (title.StartsWith(q, StringComparison.Ordinal)) { return 70; }
        if (host.Contains(q, StringComparison.Ordinal)) { return 50; }
        if (title.Contains(q, StringComparison.Ordinal)) { return 30; }
        if (url.Contains(q, StringComparison.Ordinal)) { return 10; }
        return 0;
    }

    /// <summary>
    /// The completion Safari inlines into the address bar: if what you typed is the start
    /// of a host you visit, the rest of it appears selected so Enter goes straight there.
    /// Only offered for a site you have actually been to more than once — autocompleting to
    /// somewhere you glanced at once is worse than not autocompleting at all.
    /// </summary>
    public static string? InlineCompletion(string typed)
    {
        if (string.IsNullOrWhiteSpace(typed) || typed.Contains(' ') || typed.Contains('/'))
        {
            return null;
        }

        string q = typed.Trim().ToLowerInvariant();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        lock (Gate)
        {
            HistoryEntry? best = Items.Values
                .Where(e => e.VisitCount > 1 && e.Host.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => Score(e, now))
                .FirstOrDefault();

            return best?.Host;
        }
    }

    public static void Remove(string url)
    {
        lock (Gate) { Items.Remove(url); }
        ScheduleSave();
        Changed?.Invoke();
    }

    /// <summary>Clears everything newer than <paramref name="since"/>; null clears all.</summary>
    public static void Clear(DateTimeOffset? since = null)
    {
        lock (Gate)
        {
            if (since is null)
            {
                Items.Clear();
            }
            else
            {
                foreach (string url in Items.Where(p => p.Value.LastVisitUtc >= since.Value)
                                            .Select(p => p.Key).ToList())
                {
                    Items.Remove(url);
                }
            }
        }

        Save();
        Changed?.Invoke();
    }

    public static int Count
    {
        get { lock (Gate) { return Items.Count; } }
    }

    private static void ScheduleSave()
    {
        if (Interlocked.Exchange(ref _saveScheduled, 1) == 1)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(3000);
            Interlocked.Exchange(ref _saveScheduled, 0);
            Save();
        });
    }

    public static void Save()
    {
        try
        {
            List<HistoryEntry> rows;
            lock (Gate)
            {
                // Trimmed on write rather than on read, so a long-running session never
                // pays for it and the file cannot grow without bound.
                if (Items.Count > MaxEntries)
                {
                    foreach (string url in Items.Values
                                 .OrderByDescending(e => e.LastVisitUtc)
                                 .Skip(TrimTo)
                                 .Select(e => e.Url)
                                 .ToList())
                    {
                        Items.Remove(url);
                    }
                }

                rows = Items.Values.OrderByDescending(e => e.LastVisitUtc).ToList();
            }

            string folder = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(folder);

            string json = JsonSerializer.Serialize(rows);
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Losing history is survivable; taking the browser down is not.
        }
    }
}
