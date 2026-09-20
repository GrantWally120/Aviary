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
    public sealed record ListInfo(string Id, string Name, string What, string Url, string File, string SettingKey, string By, string Licence, string Titled = "")
    {
        /// <summary>What the file calls itself in its "! Title:" line, when that differs from Name.</summary>
        public string HeaderTitle => Titled.Length > 0 ? Titled : Name;
    }

    /// <summary>
    /// The lists Grant approved: EasyList and EasyPrivacy (13 Sep 2026), then the cookie-notice and
    /// social lists (19 Sep 2026, M4.5) — those two are OFF until switched on. Adding one needs his
    /// yes first, and so does downloading it.
    /// </summary>
    public static readonly ListInfo[] Lists =
    {
        new("easylist", "EasyList", "Ads", "https://easylist.to/easylist/easylist.txt", "easylist.txt", "listAds",
            "the EasyList authors", "Creative Commons Attribution-ShareAlike 3.0, or GPL v3"),
        new("easyprivacy", "EasyPrivacy", "Trackers", "https://easylist.to/easylist/easyprivacy.txt", "easyprivacy.txt", "listTrackers",
            "the EasyList authors", "Creative Commons Attribution-ShareAlike 3.0, or GPL v3"),
        new("cookies", "EasyList Cookie List", "Cookie notices", "https://secure.fanboy.co.nz/fanboy-cookiemonster.txt", "fanboy-cookiemonster.txt", "listCookies",
            "the EasyList authors", "Creative Commons Attribution 3.0"),
        new("social", "Fanboy’s Social", "Social widgets", "https://easylist.to/easylist/fanboy-social.txt", "fanboy-social.txt", "listSocial",
            "the EasyList authors", "Creative Commons Attribution-ShareAlike 3.0, or GPL v3", "Fanboy's Social Blocking List"),
    };

    private static bool IsOn(ListInfo info) => !string.Equals(AppSettings.Get(info.SettingKey), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>The rules written by hand in Settings, as they were typed (M4.5).</summary>
    public static string Mine => AppSettings.Get("customFilters");

    /// <summary>Lines in "My filters" Aviary cannot use, each with the reason. Never more than 50.</summary>
    public static List<(int Line, string Text, string Why)> MineProblems()
    {
        List<(int Line, string Text, string Why)> problems = new();
        string text = Mine;
        if (text.Length == 0)
        {
            return problems;
        }

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length && problems.Count < 50; i++)
        {
            if (FilterEngine.Problem(lines[i]) is { } why)
            {
                problems.Add((i + 1, lines[i].Trim(), why));
            }
        }

        return problems;
    }

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

            // Hand-written rules go in last, so a person's own rule is read after the lists'.
            string mine = Mine;
            if (mine.Trim().Length > 0)
            {
                sources.Add(new FilterEngine.Source(FilterEngine.MineName, mine));
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
            double warmMs = Warm(engine);
            Current = loaded;
            _engine = engine;

            FilterEngine.ListStats s = engine.Stats;
            Diagnostics.Perf("filter-engine", new
            {
                why,
                ms = Math.Round(engine.BuildMs, 1),
                warmMs = Math.Round(warmMs, 1),
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
                titled = string.Equals(line.Substring(8).Trim(), info.HeaderTitle, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// "13 Sep 2026 05:50 UTC", and the other way the same authors write it in the same folder:
    /// "2026-09-19 08:25 UTC" (the Cookie List). A date that will not parse is not a broken list,
    /// only a list Aviary cannot date - see how Describe reports it.
    /// </summary>
    private static DateTimeOffset? ParseModified(string value) =>
        DateTimeOffset.TryParseExact(value.Replace(" UTC", string.Empty),
            new[] { "d MMM yyyy HH:mm", "dd MMM yyyy HH:mm", "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ssZ" },
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

    /// <summary>
    /// How often to fetch the lists by itself, or null for never. "Weekly"/"Daily" since M4.5; a
    /// saved true/false from before then still reads as weekly/never.
    /// </summary>
    public static TimeSpan? AutoUpdateEvery => AppSettings.Get("listsAutoUpdate").ToLowerInvariant() switch
    {
        "daily" => TimeSpan.FromDays(1),
        "weekly" or "true" => TimeSpan.FromDays(7),
        _ => null,
    };

    private static void MaybeAutoUpdate()
    {
        try
        {
            if (AutoUpdateEvery is not { } every)
            {
                return;
            }

            if (LastCheck.Checked is { } last && DateTimeOffset.UtcNow - last < every)
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
                // A list that is switched off is not downloaded: nothing is fetched for a list
                // nobody is using.
                if (!IsOn(info))
                {
                    continue;
                }

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
                    : "Updated " + Listed(updated) + (failed.Count > 0 ? "; " + string.Join(", ", failed) + " failed." : ".");

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
    /// <summary>
    /// Runs every match path once, here on the loading thread, before any tab gets the engine.
    ///
    /// The hot methods are [AggressiveOptimization] (FilterEngine.cs, TrackerBlocker.cs): compiled
    /// fully optimised on their first call, which is this one, instead of quickly first and then
    /// again mid-loop on the UI thread — measured 19 Sep as 3.4-7.4 ms of UI-thread CPU on the first
    /// long YouTube video request of a session.
    ///
    /// Grant's diagnostics (M4.5): the blocker's worst single check was 3.3–3.6 ms — and in three of
    /// six sessions it was the FIRST minute's worst, while every later minute's median worst was
    /// under 9 µs. A first call pays for compiling its code and warming its caches, and the first
    /// requests of a session were paying it on the UI thread, inside a page load. Paid here instead.
    /// </summary>
    private static double Warm(FilterEngine engine)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            string[] requests =
            {
                "https://www.example.com/",
                "https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js",
                "https://www.google-analytics.com/analytics.js",
                "https://cdn.example.net/assets/app.js?v=1",
                "https://static.example.org/img/logo.png",
                "https://ads.example.com/banner.gif?utm_source=x&id=1",
                "https://example.com/api/track?event=view",
                "https://connect.facebook.net/en_US/fbevents.js",
                // Long hosts (32+ characters take the framework's widest vector paths) and a long address.
                "https://rr1---sn-q4flrnlz-abcdefghijklmn.googlevideo.example-cdn-network.com/x.js?a=1",
                // A long one: the loops in the match code run long enough to be recompiled mid-loop.
                "https://rr3---sn-abcdefg.googlevideo.com/videoplayback?expire=1789566927&ei=b0uqat7WGuqMvcAPi42BmQI&ip=203.0.113.4&id=o-AAvR-jejr0lG684UaJjhEiCN0zXUvYQWhHYmjF7ffqRi&itag=243&aitags=133%2C134%2C135%2C136%2C160%2C242&source=youtube&requiressl=yes&mh=Qd&mm=31%2C29&mn=sn-abcdefg&ms=au%2Crdu&mv=m&mvi=3&pl=24&initcwndbps=2360000&vprv=1&mime=video%2Fwebm&rqh=1&gir=yes&clen=1234567&dur=123.456&lmt=1757830000000000&mt=1757830653&fvip=3&keepalive=yes&c=WEB&txp=4535434&sparams=expire%2Cei%2Cip%2Cid%2Caitags%2Csource%2Crequiressl&sig=AJfQdSswRQIgVx7ZmL0p2N3o4Q5R6S7T8U9V0W1X2Y3Z&cver=2.20260915.01.00&range=0-123456&rn=1&rbuf=0",
            };
            ReqType[] types = { ReqType.Script, ReqType.Image, ReqType.Xhr, ReqType.Subdocument, ReqType.Stylesheet, ReqType.Media, ReqType.Ping, ReqType.Other };

            foreach (string pageUrl in new[] { "https://news.example.com/2026/article", "https://www.youtube.com/" })
            {
                engine.MatchPage(pageUrl);
                FilterEngine.Page page = engine.PageFor(pageUrl);
                foreach (string url in requests)
                {
                    TrackerBlocker.Match(url);
                    FilterEngine.IsSameSite(url, page);
                    foreach (ReqType type in types)
                    {
                        engine.Match(url, type, page, thirdPartyOnly: true);
                        engine.Match(url, type, page, thirdPartyOnly: false);
                    }
                }
            }

            Shields.SiteOf("www.example.co.uk");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "filters: warm up");
        }

        return System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    /// <summary>
    /// Back to the lists Aviary shipped with (M4.5): the downloaded copies are deleted and the
    /// engine is rebuilt from the ones beside Aviary.exe. Runs off the UI thread.
    /// </summary>
    public static void RestoreBundled()
    {
        int removed = 0;
        try
        {
            foreach (ListInfo info in Lists)
            {
                string path = Path.Combine(UpdatedFolder, info.File);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    removed++;
                }
            }

            LastCheck = new State { Checked = null, Result = removed == 0 ? "Already the shipped lists." : "Back to the lists Aviary shipped with." };
            SaveState();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "filter lists: restore bundled");
        }

        Diagnostics.Shields("lists-restored", new { removed });
        Load("restore");
    }

    /// <summary>"EasyList", "EasyList and EasyPrivacy", "A, B and C" - four lists read badly otherwise.</summary>
    private static string Listed(List<string> names) => names.Count switch
    {
        0 => string.Empty,
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };

    public static object Describe(bool? updating = null)
    {
        FilterEngine? engine = Engine;
        FilterEngine.ListStats? s = engine?.Stats;
        List<object> lists = new();
        string mine = Mine;
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
                // M4.5: what this list actually brought, and whose it is.
                rules = s?.RulesByList.GetValueOrDefault(info.Name) ?? 0,
                skipped = s?.SkippedByList.GetValueOrDefault(info.Name) ?? 0,
                url = info.Url,
                by = info.By,
                licence = info.Licence,
                setting = info.SettingKey,
            });
        }

        if (mine.Trim().Length > 0)
        {
            lists.Add(new
            {
                name = FilterEngine.MineName,
                what = "Your own rules",
                version = string.Empty,
                modified = string.Empty,
                on = true,
                source = "written here",
                lines = mine.Replace("\r\n", "\n").Split('\n').Length,
                rules = s?.RulesByList.GetValueOrDefault(FilterEngine.MineName) ?? 0,
                skipped = s?.SkippedByList.GetValueOrDefault(FilterEngine.MineName) ?? 0,
                url = string.Empty,
                by = "you",
                licence = string.Empty,
                setting = "customFilters",
            });
        }

        return new
        {
            ready = engine is not null,
            lists,
            mine = MineProblems().Select(p => new { line = p.Line, text = p.Text, why = p.Why }),
            network = (s?.Network ?? 0) + (s?.NetworkExceptions ?? 0),
            cosmetic = (s?.CosmeticGeneric ?? 0) + (s?.CosmeticSpecific ?? 0) + (s?.CosmeticExceptions ?? 0),
            skipped = s?.Skipped ?? 0,
            // Why lines were skipped, biggest reasons first (M4.5).
            skippedBy = (s?.SkippedBy ?? new Dictionary<string, int>()).OrderByDescending(p => p.Value).Take(6).Select(p => new { why = p.Key, count = p.Value }),
            autoUpdate = AppSettings.Get("listsAutoUpdate"),
            checkedAt = LastCheck.Checked?.ToLocalTime().ToString("d MMM yyyy, HH:mm", CultureInfo.InvariantCulture) ?? "",
            result = LastCheck.Result ?? "",
            updating = updating ?? Updating,
        };
    }
}
