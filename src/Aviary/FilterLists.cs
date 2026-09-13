using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Aviary;

/// <summary>
/// The filter lists themselves (M3): which copy is used, loading them into a
/// <see cref="FilterEngine"/> off the UI thread, and fetching new copies.
///
/// <para><b>Two copies of each list.</b> The one Aviary ships with (<c>&lt;exe&gt;\Filters\</c>,
/// exactly as easylist.to published it — never edited, never merged into anything of ours,
/// which is what keeps their CC BY-SA / GPL licence a matter of attribution) and, once "Update
/// now" or the weekly refresh has run, a newer one in the profile (<c>filters\</c>). Whichever
/// says it was modified later is the one loaded.</para>
///
/// <para><b>Nothing here goes online by itself</b> unless "Keep the lists up to date" is on — off by
/// default. A list download sends nothing about the person: it is the same file for everyone,
/// fetched with no cookies. A download that does not look like the list it claims to be is
/// thrown away, and the lists already loaded keep working.</para>
/// </summary>
internal static class FilterLists
{
    /// <param name="SettingKey">The Shields switch that turns this list on or off — the plan's "per-category switches".</param>
    public sealed record ListInfo(string Id, string Name, string What, string Url, string File, string SettingKey);

    /// <summary>The lists Grant approved (13 Sep 2026). Adding one needs his yes first.</summary>
    public static readonly ListInfo[] Lists =
    {
        new("easylist", "EasyList", "Ads", "https://easylist.to/easylist/easylist.txt", "easylist.txt", "listAds"),
        new("easyprivacy", "EasyPrivacy", "Trackers", "https://easylist.to/easylist/easyprivacy.txt", "easyprivacy.txt", "listTrackers"),
    };

    private static bool IsOn(ListInfo info) => !string.Equals(AppSettings.Get(info.SettingKey), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A list switched on or off in Settings: rebuild the engine from the lists that are on, off
    /// the UI thread. Every tab picks the new engine up through <see cref="EngineChanged"/>.
    /// </summary>
    public static Task Reload(string why) => Task.Run(() => Load(why));

    /// <summary>One list as currently loaded.</summary>
    public sealed class Loaded
    {
        public required ListInfo Info;
        public string Version = string.Empty;
        public DateTimeOffset? Modified;
        public bool FromUpdate;
        public long Bytes;
        public int Lines;
    }

    private static readonly object Gate = new();
    private static volatile FilterEngine? _engine;
    private static Task? _loading;
    private static int _updating;
    private static Timer? _weekly;

    /// <summary>The engine every tab matches against, or null until the first load finishes.</summary>
    public static FilterEngine? Engine => _engine;

    /// <summary>The first load. Requests arriving before it finishes wait on it (briefly).</summary>
    public static Task Loading => _loading ?? Task.CompletedTask;

    public static IReadOnlyList<Loaded> Current { get; private set; } = Array.Empty<Loaded>();

    /// <summary>Raised on a background thread whenever a new engine is in place.</summary>
    public static event Action? EngineChanged;

    public static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "Filters");

    public static string UpdatedFolder => AppPaths.Of("filters");

    private static string StatePath => Path.Combine(UpdatedFolder, "state.json");

    /// <summary>Starts the first load on a background thread. Idempotent.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_loading is not null)
            {
                return;
            }

            _loading = Task.Run(() =>
            {
                Load("launch");
                MaybeAutoUpdate();
            });

            // The weekly refresh checks a few times a day while Aviary is open; it only
            // downloads when the switch is on and a week has passed.
            _weekly = new Timer(_ => MaybeAutoUpdate(), null, TimeSpan.FromHours(6), TimeSpan.FromHours(6));
        }
    }

    private static readonly object LoadGate = new();

    private static void Load(string why)
    {
        // One build at a time: a switch flicked twice must not let the older build land last.
        lock (LoadGate)
        {
            LoadLocked(why);
        }
    }

    private static void LoadLocked(string why)
    {
        try
        {
            List<FilterEngine.Source> sources = new();
            List<Loaded> loaded = new();
            int switchedOff = 0;
            foreach (ListInfo info in Lists)
            {
                if (!IsOn(info))
                {
                    switchedOff++;
                    continue;
                }

                if (Pick(info) is { } picked)
                {
                    sources.Add(new FilterEngine.Source(info.Name, picked.Text));
                    loaded.Add(picked.Meta);
                }
            }

            if (sources.Count == 0)
            {
                // Both lists switched off is a choice, not a fault: only Aviary's own list blocks,
                // and nothing is hidden. Missing files ARE a fault.
                if (switchedOff < Lists.Length)
                {
                    Diagnostics.Anomaly("filter-lists", "No filter lists found beside Aviary.exe or in the profile — only Aviary's own list is blocking.",
                        new { bundled = BundledFolder });
                }

                Current = loaded;
                _engine = null;
                EngineChanged?.Invoke();
                return;
            }

            FilterEngine engine = FilterEngine.Build(sources);
            Current = loaded;
            _engine = engine;

            FilterEngine.ListStats s = engine.Stats;
            Diagnostics.Perf("filter-engine", new
            {
                why,
                ms = Math.Round(engine.BuildMs, 1),
                lists = loaded.ConvertAll(l => new { l.Info.Name, l.Version, l.FromUpdate, l.Lines }),
                network = s.Network,
                networkExceptions = s.NetworkExceptions,
                hostOnly = s.HostOnly,
                cosmeticGeneric = s.CosmeticGeneric,
                cosmeticSpecific = s.CosmeticSpecific,
                cosmeticExceptions = s.CosmeticExceptions,
                pageExceptions = s.PageExceptions,
                skipped = s.Skipped,
                skippedBy = s.SkippedBy,
            });

            EngineChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "filter lists: build engine");
        }
    }

    private sealed record Picked(string Text, Loaded Meta);

    /// <summary>The newer of the shipped copy and an updated one, by the list's own "Last modified".</summary>
    private static Picked? Pick(ListInfo info)
    {
        Picked? bundled = ReadCopy(info, Path.Combine(BundledFolder, info.File), fromUpdate: false);
        Picked? updated = ReadCopy(info, Path.Combine(UpdatedFolder, info.File), fromUpdate: true);

        if (updated is null) { return bundled; }
        if (bundled is null) { return updated; }

        DateTimeOffset b = bundled.Meta.Modified ?? DateTimeOffset.MinValue;
        DateTimeOffset u = updated.Meta.Modified ?? DateTimeOffset.MinValue;
        return u >= b ? updated : bundled;
    }

    private static Picked? ReadCopy(ListInfo info, string path, bool fromUpdate)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string text = File.ReadAllText(path);
            if (!LooksLike(info, text, out Loaded meta))
            {
                Diagnostics.Anomaly("filter-lists", info.Name + " at " + path + " is not a readable filter list; it was skipped.", new { path });
                return null;
            }

            meta.FromUpdate = fromUpdate;
            return new Picked(text, meta);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "filter lists: read " + info.File);
            return null;
        }
    }

    /// <summary>
    /// A filter list is text that starts with its "[Adblock Plus …]" header and names itself in a
    /// "! Title:" line. Anything else — an error page, a captive portal, a truncated download — is
    /// refused before it can replace a list that works.
    /// </summary>
    private static bool LooksLike(ListInfo info, string text, out Loaded meta)
    {
        meta = new Loaded { Info = info, Bytes = text.Length };
        if (text.Length < 20_000 || text.Length > 40_000_000 || text.IndexOf('\0') >= 0)
        {
            return false;
        }

        if (!text.TrimStart('﻿', ' ', '\r', '\n').StartsWith("[Adblock Plus", StringComparison.Ordinal))
        {
            return false;
        }

        bool titled = false;
        using StringReader reader = new(text);
        string? line;
        int lines = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lines++;
            if (lines > 60 && titled)
            {
                continue;
            }

            if (line.StartsWith("! Title:", StringComparison.Ordinal))
            {
                titled = string.Equals(line.Substring(8).Trim(), info.Name, StringComparison.OrdinalIgnoreCase);
            }
            else if (line.StartsWith("! Version:", StringComparison.Ordinal))
            {
                meta.Version = line.Substring(10).Trim();
            }
            else if (line.StartsWith("! Last modified:", StringComparison.Ordinal))
            {
                meta.Modified = ParseModified(line.Substring(16).Trim());
            }
        }

        meta.Lines = lines;
        return titled && lines > 1000;
    }

    /// <summary>"13 Sep 2026 05:50 UTC".</summary>
    private static DateTimeOffset? ParseModified(string value) =>
        DateTimeOffset.TryParseExact(value.Replace(" UTC", string.Empty), new[] { "d MMM yyyy HH:mm", "dd MMM yyyy HH:mm" },
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset when)
            ? when
            : null;

    // ══ UPDATING ════════════════════════════════════════════════════════════

    public sealed record UpdateResult(bool Ok, bool Changed, string Message);

    /// <summary>When the lists were last checked, and what happened — shown in Settings.</summary>
    public sealed class State
    {
        public DateTimeOffset? Checked { get; set; }
        public string? Result { get; set; }
    }

    public static State LastCheck { get; private set; } = ReadState();

    public static bool Updating => Volatile.Read(ref _updating) == 1;

    /// <summary>
    /// For test instances only, like AVIARY_PROFILE_DIR: where to fetch the lists from instead of
    /// easylist.to, so a test of "Update now" never downloads from the real site. Nothing a page,
    /// a setting or a menu can reach sets a process's environment.
    /// </summary>
    private static string? TestBase => Environment.GetEnvironmentVariable("AVIARY_TEST_FILTER_BASE");

    private static void MaybeAutoUpdate()
    {
        try
        {
            if (!AppSettings.GetBool("listsAutoUpdate"))
            {
                return;
            }

            if (LastCheck.Checked is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromDays(7))
            {
                return;
            }

            _ = UpdateAsync("weekly");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "filter lists: weekly check");
        }
    }

    /// <summary>
    /// Fetches every list, keeps the ones that are genuinely newer, and swaps in a new engine if
    /// anything changed. Safe to call from any thread; a second call while one runs is refused.
    /// </summary>
    public static async Task<UpdateResult> UpdateAsync(string why)
    {
        if (Interlocked.Exchange(ref _updating, 1) == 1)
        {
            return new UpdateResult(false, false, "An update is already running.");
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        List<string> updated = new(), failed = new();
        try
        {
            await Loading.ConfigureAwait(false);

            using HttpClient http = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = true })
            {
                Timeout = TimeSpan.FromSeconds(60),
            };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Aviary/" + BuildInfo.Version);

            Directory.CreateDirectory(UpdatedFolder);
            foreach (ListInfo info in Lists)
            {
                string url = TestBase is { Length: > 0 } b ? b.TrimEnd('/') + "/" + info.File : info.Url;
                try
                {
                    string text = await http.GetStringAsync(url).ConfigureAwait(false);
                    if (!LooksLike(info, text, out Loaded fresh))
                    {
                        failed.Add(info.Name + " (not a filter list)");
                        continue;
                    }

                    Loaded? current = Current.FirstOrDefaultOf(info.Id);
                    if (current is not null && fresh.Modified is { } f && current.Modified is { } c && f <= c)
                    {
                        continue;
                    }

                    string target = Path.Combine(UpdatedFolder, info.File);
                    string temp = target + ".tmp";
                    await File.WriteAllTextAsync(temp, text).ConfigureAwait(false);
                    File.Move(temp, target, overwrite: true);
                    updated.Add(info.Name);
                }
                catch (Exception ex)
                {
                    failed.Add(info.Name + " (" + ex.GetType().Name + ")");
                    Diagnostics.Anomaly("filter-lists", "Could not update " + info.Name + ": " + ex.Message, new { url, why });
                }
            }

            if (updated.Count > 0)
            {
                Load(why);
            }

            string message = failed.Count == Lists.Length
                ? "Couldn't reach easylist.to — the lists you have keep working."
                : updated.Count == 0
                    ? (failed.Count == 0 ? "Already up to date." : "Up to date, except " + string.Join(", ", failed) + ".")
                    : "Updated " + string.Join(" and ", updated) + (failed.Count > 0 ? "; " + string.Join(", ", failed) + " failed." : ".");

            LastCheck = new State { Checked = DateTimeOffset.UtcNow, Result = message };
            SaveState();

            Diagnostics.Shields("lists-checked", new
            {
                why,
                updated,
                failed,
                ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds),
            });

            return new UpdateResult(failed.Count < Lists.Length, updated.Count > 0, message);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "filter lists: update");
            return new UpdateResult(false, false, "The update didn't finish — the lists you have keep working.");
        }
        finally
        {
            Volatile.Write(ref _updating, 0);
        }
    }

    private static Loaded? FirstOrDefaultOf(this IReadOnlyList<Loaded> list, string id)
    {
        foreach (Loaded l in list)
        {
            if (l.Info.Id == id)
            {
                return l;
            }
        }

        return null;
    }

    private static State ReadState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                return JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State();
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "filter lists: read state.json");
        }

        return new State();
    }

    private static void SaveState()
    {
        try
        {
            Directory.CreateDirectory(UpdatedFolder);
            string temp = StatePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(LastCheck));
            File.Move(temp, StatePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "filter lists: save state.json");
        }
    }

    /// <summary>What Settings shows about the lists: each one's version and date, the counts, the last check.</summary>
    public static object Describe(bool? updating = null)
    {
        FilterEngine? engine = Engine;
        FilterEngine.ListStats? s = engine?.Stats;
        List<object> lists = new();
        foreach (ListInfo info in Lists)
        {
            Loaded? l = Current.FirstOrDefaultOf(info.Id);
            lists.Add(new
            {
                name = info.Name,
                what = info.What,
                version = l?.Version ?? "",
                modified = l?.Modified?.ToLocalTime().ToString("d MMM yyyy", CultureInfo.InvariantCulture) ?? "",
                on = IsOn(info),
                source = !IsOn(info) ? "switched off" : l is null ? "missing" : l.FromUpdate ? "updated" : "shipped with Aviary",
                lines = l?.Lines ?? 0,
            });
        }

        return new
        {
            ready = engine is not null,
            lists,
            network = (s?.Network ?? 0) + (s?.NetworkExceptions ?? 0),
            cosmetic = (s?.CosmeticGeneric ?? 0) + (s?.CosmeticSpecific ?? 0) + (s?.CosmeticExceptions ?? 0),
            skipped = s?.Skipped ?? 0,
            checkedAt = LastCheck.Checked?.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) ?? "",
            result = LastCheck.Result ?? "",
            updating = updating ?? Updating,
        };
    }
}
