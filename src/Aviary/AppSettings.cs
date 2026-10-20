using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Aviary;

/// <summary>
/// Everything the browser remembers between launches, in one small JSON file next to the
/// WebView2 profile: %LOCALAPPDATA%\Aviary\settings.json
///
/// Deliberately a flat string dictionary rather than a typed class. Settings is a
/// data-driven page whose rows change every phase; a typed model would mean editing three
/// files to add one switch, and a forgotten property means a setting that silently stops
/// being saved. Values are stored as strings because that is what the settings page sends
/// and what it needs back to render.
/// </summary>
internal static class AppSettings
{
    private static readonly object Gate = new();
    private static Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private static bool _loaded;

    private static string Folder => AppPaths.Root;

    private static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>
    /// Defaults. "Classic" is the opening look for a fresh install; after that whatever
    /// was last chosen wins, because the saved value overwrites this.
    /// </summary>
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["look"] = "Classic",
        ["theme"] = "System",
        ["searchEngine"] = "DuckDuckGo",
        ["homeButton"] = "true",
        ["fullUrl"] = "false",
        ["bookmarksBar"] = "false",
        ["blockTrackers"] = "true",
        // M4.9 — one row, four modes (Grant: "aggressive which will save and offload the tab
        // fastest and most Chill will do it slower and less offload and balanced(Default) is well
        // balanced"). It was a switch until M4.9; a saved "true" still reads as Balanced.
        ["memorySaver"] = "Balanced",
        ["startup"] = "Start Page",
        ["zoom"] = "100%",
        // Phase 5 M2 — privacy that actually works. Every one of these is live and real.
        ["gpc"] = "true",
        ["safariUa"] = "true",
        ["thirdPartyCookies"] = "true",
        ["webrtcProtect"] = "true",
        ["stripTracking"] = "true",
        ["httpsOnly"] = "true",
        ["trimReferrer"] = "true",
        ["privacyRange"] = "Last 7 days",
        ["formAutofill"] = "false",
        ["downloadHistory"] = "After one day",
        ["downloadDir"] = "",
        ["warnExecutables"] = "true",
        ["siteIcons"] = "true",
        // Phase 5 M3 — the ad blocker. Read by code before Settings has ever been opened.
        ["blockMode"] = "Standard",
        ["listAds"] = "true",
        ["listTrackers"] = "true",
        // M4.5: the two lists Grant approved on 19 Sep, off until switched on.
        ["listCookies"] = "false",
        ["listSocial"] = "false",
        // Off / Daily / Weekly since M4.5 (a saved "true" still reads as weekly).
        ["listsAutoUpdate"] = "Off",
        // Rules written by hand in Settings, compiled in as "My filters" (M4.5).
        ["customFilters"] = "",
        // The rest of Shields’ own switches (M4.5).
        ["cosmeticHiding"] = "true",
        ["shieldBadge"] = "true",
        ["strictWarning"] = "true",
        // Phase 5 M4 — search. Read on every keystroke in the address bar, Settings opened or not.
        ["suggest"] = "false",
        // Alpha Build 5 M4.5 — how Aviary moves (Motion.cs). Read by the window at start-up.
        ["animationStyle"] = "Match the look",
        // M4.6 — which of the two icons the window wears.
        ["appIcon"] = "Blue tile",
        // M4.6 — the tab choices.
        ["newTabPosition"] = "After the current tab",
        ["middleClickCloses"] = "true",
        ["tabCloseButton"] = "On hover",
        ["newTabShowsPage"] = "The Start Page",
        // M4.6 — how often Settings -> Performance measures while you watch it.
        ["perfInterval"] = "Every second",
        // M4.6 — the graphics caches, which can only go at the next launch.
        ["clearGpuCaches"] = "false",
        // M4.6 — how fast all of that moves, and where Liquid Glass puts its bar.
        ["animationSpeed"] = "Standard",
        ["windowButtons"] = "Floating",
        // M4.9 — frame timing, the one measurement that costs what it measures, so it is off even
        // while collection is on (Diagnostics.Frames.cs).
        ["frameTiming"] = "false",
        // M4.9 — Bird News (BirdsNews.cs). The Start Page section is its master switch: nothing
        // is fetched while nobody is reading it. The six feeds Grant approved on 21 Sep, each on.
        ["startBackground"] = "Glasshouse",
        ["startNews"] = "true",
        ["newsRefresh"] = "Every 15 minutes",
        ["newsBbc"] = "true",
        ["newsAljazeera"] = "true",
        ["newsGuardian"] = "true",
        ["newsInquirer"] = "true",
        ["newsRappler"] = "true",
        ["newsGma"] = "true",
        ["newsCustom"] = "",
        // Alpha Build 5 M4.5 — accessibility, and the tab rows beside it.
        ["pageTextSize"] = "Default",
        ["underlineLinks"] = "false",
        ["focusRing"] = "false",
        ["warnManyTabs"] = "true",
        ["lastTabCloses"] = "Close the window",
        // M6 stage 5 — the four rows that were badged until now. Each is read as the browser engine
        // starts (WebViewEnvironment), before Settings has ever been opened.
        ["gpu"] = "true",
        ["autoplay"] = WebViewEnvironment.AutoPlayWithSound,
        ["language"] = WebViewEnvironment.LanguageOfWindows,
        ["minFont"] = "None",
        // M7 "Super Quik" — off until switched on in Settings → Quik; read once, at launch (Quik.cs).
        ["quik"] = "false",
        // M7 stage 2 - Quik's own options, on while Quik runs (Settings -> Quik).
        ["quikReadyTab"] = "true",
        ["quikKeepAwake"] = "true",
    };

    public static void Load()
    {
        lock (Gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            _values = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);

            // The Reduce motion row says "Follow the Windows animation setting", so it had
            // better START by following it rather than by guessing. A saved value read
            // below still wins — this only decides what a fresh install inherits.
            try
            {
                _values["reduceMotion"] =
                    new Windows.UI.ViewManagement.UISettings().AnimationsEnabled ? "false" : "true";
            }
            catch (Exception ex)
            {
                _values["reduceMotion"] = "false";
                Diagnostics.Swallowed(ex, "settings: read Windows animation setting");
            }

            try
            {
                if (!File.Exists(FilePath))
                {
                    return;
                }

                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                {
                    _values[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                        _ => property.Value.ToString()
                    };
                }

                // Every install made before Phase 4 carries a junk "reset":"true" written
                // by the Reset button, which posted itself as if it were a setting and had
                // no handler on the other end. Drop it on sight; it is not a preference.
                _values.Remove("reset");

                // M2: "Send a Do Not Track request" became Global Privacy Control. DNT is dead,
                // and a rare DNT header is itself a fingerprinting bit — besides which the old
                // switch never sent anything. Whatever was chosen for it carries over once.
                if (_values.Remove("dnt", out string? dnt) && !doc.RootElement.TryGetProperty("gpc", out _))
                {
                    _values["gpc"] = dnt;
                }

                // M4.6 (bug 73, from Grant's own 20 Sep log): "Keep the lists up to date" was a
                // switch until M4.5 and is now Off / Daily / Weekly. The page drew a saved "true"
                // as Weekly and a "false" as Off, which read correctly - but the STORE still held
                // the boolean, so diagnostics' round-trip check reported the page and the store
                // disagreeing, twice, on a row that was working. Migrate the value itself: one
                // place, and nothing downstream has to know the old shape.
                if (_values.TryGetValue("listsAutoUpdate", out string? schedule))
                {
                    if (schedule.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        _values["listsAutoUpdate"] = "Weekly";
                    }
                    else if (schedule.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        _values["listsAutoUpdate"] = "Off";
                    }
                }

                // M4.9: "Memory saver" was a switch and is now Off / Chill / Balanced / Aggressive.
                // Same rule as bug 73 — migrate the VALUE here, in one place, so nothing downstream
                // has to know the old shape and the store never disagrees with the page.
                if (_values.TryGetValue("memorySaver", out string? saver))
                {
                    if (saver.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        _values["memorySaver"] = "Balanced";
                    }
                    else if (saver.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        _values["memorySaver"] = "Off";
                    }
                }

                // M4.9: sleepIdleTabs was M4.8's separate row, folded into the mode above before it
                // ever shipped. Drop it on sight so it cannot sit in the file meaning nothing.
                _values.Remove("sleepIdleTabs");

                // M6 stage 5: "Autoplay video" was a switch - badged, never live, so a saved value is
                // only the page's old default - and is now Safari's three choices. Same rule as above.
                if (_values.TryGetValue("autoplay", out string? autoplay))
                {
                    if (autoplay.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        _values["autoplay"] = WebViewEnvironment.AutoPlayAll;
                    }
                    else if (autoplay.Equals("false", StringComparison.OrdinalIgnoreCase))
                    {
                        _values["autoplay"] = WebViewEnvironment.AutoPlayWithSound;
                    }
                }

                // ...and "Preferred language" offered Cebuano, which the engine cannot ask for
                // (measured): anything it no longer offers is Windows' language again.
                if (_values.TryGetValue("language", out string? language)
                    && language != WebViewEnvironment.LanguageOfWindows
                    && !WebViewEnvironment.Languages.ContainsKey(language))
                {
                    _values["language"] = WebViewEnvironment.LanguageOfWindows;
                }
            }
            catch (Exception ex)
            {
                // A corrupt settings file should cost you your preferences, not your
                // browser. Defaults are already in place; carry on.
                Diagnostics.Swallowed(ex, "settings: load settings.json");
            }
        }
    }

    /// <summary>
    /// EVERY key the store knows — the built-in defaults, overwritten by anything ever
    /// saved. This is what the settings page is rendered from.
    ///
    /// It replaces a hand-written list of thirteen keys that lived in BuildSettingsHtml
    /// while the message handler happily persisted all twenty-two. Nine controls therefore
    /// wrote to disk and then rendered their hardcoded default on the next visit, so the
    /// page and the file disagreed and the switch looked broken. There is no list now.
    /// Saving a key is what registers it.
    /// </summary>
    /// <summary>
    /// Settings whose VALUE is the person's own writing, not a choice Aviary offered (M4.5).
    /// "My filters" can name any site they visit, so diagnostics got a count of the lines and
    /// never the lines themselves (bug 72).
    ///
    /// <para><b>EMPTY SINCE M4.9, AND DELIBERATELY SO.</b> Grant asked on 21 Sep 2026 to "update
    /// our data collection to everything … for the betterment of our browser", and when asked
    /// specifically about this one he chose to include it. It is his own writing, on his own
    /// machine, in a folder that never leaves it. <b>This is not bug 72 coming back</b> — the
    /// mechanism is intact and still honoured in both places, there is simply nothing in the
    /// list. Do not "fix" it back; if a key ever needs redacting again, adding it here is all it
    /// takes. The secrets filter (Diagnostics.Scrub) is untouched and still the real line.</para>
    /// </summary>
    public static readonly string[] PrivateKeys = Array.Empty<string>();

    /// <summary>The snapshot diagnostics may write down: every setting, with those redacted.</summary>
    public static Dictionary<string, string> SnapshotForReport()
    {
        Dictionary<string, string> all = Snapshot();
        foreach (string key in PrivateKeys)
        {
            if (all.TryGetValue(key, out string? value))
            {
                int lines = value.Trim().Length == 0 ? 0 : value.Replace("\r\n", "\n").Split('\n').Length;
                all[key] = lines + (lines == 1 ? " line (not recorded)" : " lines (not recorded)");
            }
        }

        return all;
    }

    public static Dictionary<string, string> Snapshot()
    {
        Load();
        lock (Gate)
        {
            return new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The settings page's own literal defaults, posted up once on load. Only keys never
    /// seen before are written — a seed must never overwrite a choice — and one Save runs
    /// at the end rather than one per key.
    /// </summary>
    public static void SeedDefaults(IReadOnlyDictionary<string, string> seed)
    {
        Load();
        bool dirty = false;

        lock (Gate)
        {
            foreach (KeyValuePair<string, string> pair in seed)
            {
                if (string.IsNullOrEmpty(pair.Key) || _values.ContainsKey(pair.Key))
                {
                    continue;
                }

                _values[pair.Key] = pair.Value;
                dirty = true;
            }
        }

        if (dirty)
        {
            Save();
        }
    }

    /// <summary>
    /// Puts every preference back to its default. History, bookmarks and downloaded files
    /// are not touched — this is the settings page only.
    /// </summary>
    public static void Reset()
    {
        Load();

        Diagnostics.Ui("settings-reset");

        lock (Gate)
        {
            _values = new Dictionary<string, string>(Defaults, StringComparer.OrdinalIgnoreCase);
        }

        Save();
    }

    public static string Get(string key)
    {
        Load();
        lock (Gate)
        {
            return _values.TryGetValue(key, out string? value) ? value
                 : Defaults.TryGetValue(key, out string? fallback) ? fallback
                 : string.Empty;
        }
    }

    public static bool GetBool(string key) =>
        string.Equals(Get(key), "true", StringComparison.OrdinalIgnoreCase);

    public static void Set(string key, string value)
    {
        Load();
        string? previous;
        lock (Gate)
        {
            previous = _values.TryGetValue(key, out string? old) ? old : null;
            _values[key] = value;
        }

        // Every change, old to new — diagnostics category 6. Recorded before the write so a
        // save that throws is still seen as having been attempted.
        Diagnostics.Setting(key, previous, value);
        Save();
    }

    /// <summary>
    /// Written on every change rather than at shutdown. The file is tiny, and a browser
    /// that loses your preferences because it crashed is a browser that feels unreliable.
    /// </summary>
    private static long _saveSequence;

    /// <summary>
    /// Bumped at the start of every save. The diagnostics round-trip check only compares a
    /// save that is still the newest; otherwise a quick second save (Ctrl+= held down) would
    /// make the first one look like it read back wrong.
    /// </summary>
    public static long SaveSequence => System.Threading.Interlocked.Read(ref _saveSequence);

    /// <summary>
    /// Held while settings.json is written and moved into place, and by anything else in Aviary
    /// that opens it — diagnostics' read-back. Windows will not rename a file over one that is
    /// open, whatever the reader shares (measured 19 Sep: error 5 even with FileShare.Delete), so
    /// a read-back still open when the next save arrived made that save fail — bug 66, from
    /// Grant's diagnostics: 18 Sep 21:45:35, the tab layout and the sidebar changed 2 ms apart and
    /// the second save was refused ("Access to the path is denied").
    /// </summary>
    internal static readonly object FileGate = new();

    /// <summary>The latest save did not reach the disk; the in-memory value is still right.</summary>
    private static volatile bool _unsaved;

    private static void Save()
    {
        long sequence = System.Threading.Interlocked.Increment(ref _saveSequence);

        try
        {
            Directory.CreateDirectory(Folder);

            Dictionary<string, string> snapshot;
            lock (Gate)
            {
                snapshot = new Dictionary<string, string>(_values);
            }

            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

            // Write beside the target and move into place, so an interrupted write cannot
            // leave a half-written settings file behind.
            string temp = FilePath + ".tmp";
            lock (FileGate)
            {
                File.WriteAllText(temp, json);
                MoveIntoPlace(temp, FilePath);
            }

            _unsaved = false;

            // Read back and compared on the diagnostics writer, off this thread: a key that
            // does not survive the round trip is the "saves but reverts" bug, caught live.
            Diagnostics.SettingsSaved(snapshot, FilePath, sequence);
        }
        catch (Exception ex)
        {
            // Losing a preference is survivable. Taking the browser down over it is not. The
            // next change writes everything again, and closing Aviary does if this was the last.
            _unsaved = true;
            Diagnostics.Swallowed(ex, "settings: save settings.json");
        }
    }

    /// <summary>
    /// Something outside Aviary — a virus scanner, the search indexer, Explorer's preview — can
    /// hold the file for a moment too. A few short tries (at most 90 ms) before giving up.
    /// </summary>
    private static void MoveIntoPlace(string temp, string target)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < 4 && ex is IOException or UnauthorizedAccessException)
            {
                System.Threading.Thread.Sleep(15 * attempt);
            }
        }
    }

    /// <summary>At close and when Windows ends the session: one more try if the latest save did not land.</summary>
    public static void SaveIfUnsaved()
    {
        if (_unsaved)
        {
            Save();
        }
    }
}
