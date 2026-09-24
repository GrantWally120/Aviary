using System;
using System.Collections.Generic;
using System.Linq;

namespace Aviary;

/// <summary>
/// Safety Check (Alpha Build 5 M4.9, one of Grant's five picks — Chrome's name for it). One pane
/// that reads the settings Aviary already holds and says, in plain words, which of them leave you
/// less protected than you could be, with a button that fixes each one.
///
/// <para><b>It invents no new state.</b> Everything it reports is a setting you can already see
/// somewhere else in Settings, or a fact Aviary already knows — which sites have Shields lowered,
/// how old the filter lists are, whether an engine switch is waiting for a restart. It only puts
/// them on one page.</para>
///
/// <para><b>Three kinds of line, and they mean different things:</b> <c>ok</c> — nothing to do;
/// <c>warn</c> — something is weaker than it could be and there is a fix; <c>info</c> — a choice
/// you made that is worth knowing about, and is NOT wrong (Shields lowered for a site you trust,
/// improvement data on). A check that calls a person's deliberate choice a problem teaches them to
/// ignore it.</para>
///
/// <para><b>The "switched on but not yet working" case</b> is the one no other page shows: third-
/// party cookie blocking, Global Privacy Control and WebRTC protection are engine switches, read
/// once when the engine starts (WebViewEnvironment). Turned on mid-session they are saved and do
/// nothing until Aviary restarts. Safety Check compares what is saved with what the engine was
/// launched with and says so.</para>
/// </summary>
internal static class SafetyCheck
{
    public sealed record Finding(string Id, string Level, string Title, string Detail, string? FixLabel, string? FixKey, string? FixValue, string? Goto);

    /// <summary>The only settings a Safety Check fix may change — each to its safer value.</summary>
    public static readonly IReadOnlyDictionary<string, string> Fixable = new Dictionary<string, string>
    {
        ["blockTrackers"] = "true",
        ["httpsOnly"] = "true",
        ["thirdPartyCookies"] = "true",
        ["gpc"] = "true",
        ["webrtcProtect"] = "true",
        ["stripTracking"] = "true",
        ["trimReferrer"] = "true",
        ["warnExecutables"] = "true",
        ["suggest"] = "false",
    };

    public static List<Finding> Run()
    {
        List<Finding> found = new();

        // ── Shields ──
        if (!AppSettings.GetBool("blockTrackers"))
        {
            found.Add(new("shields", "warn", "Shields are off everywhere",
                "No site’s ads or trackers are being stopped, so they can follow you from one site to the next.",
                "Turn Shields on", "blockTrackers", "true", null));
        }
        else
        {
            found.Add(new("shields", "ok", "Shields are on",
                (Shields.Strict ? "Strict" : "Standard") + " everywhere, with " + FilterLists.Current.Count +
                (FilterLists.Current.Count == 1 ? " list" : " lists") + " loaded.", null, null, null, null));

            int down = Shields.DownSites().Count;
            if (down > 0)
            {
                found.Add(new("shieldsDown", "info", down == 1 ? "Shields are off for 1 site" : "Shields are off for " + down + " sites",
                    "You turned them off for these yourself, usually because a site didn’t work with them. Worth a look now and then — a site that works now doesn’t need them off.",
                    "Review them", null, null, "shields"));
            }
        }

        // ── The filter lists: old lists miss new trackers ──
        DateTimeOffset? newest = FilterLists.Current.Select(l => l.Modified).Where(m => m is not null).DefaultIfEmpty(null).Max();
        if (newest is DateTimeOffset when)
        {
            int days = Math.Max(0, (int)(DateTimeOffset.UtcNow - when).TotalDays);
            bool auto = FilterLists.AutoUpdateEvery is not null;
            if (days > 30)
            {
                found.Add(new("lists", "warn", "The filter lists are " + days + " days old",
                    "New trackers appear all the time, and the lists are how Shields knows about them." +
                    (auto ? "" : " Keeping them up to date is off, in Shields."),
                    "Update them now", null, null, "cmd:listsUpdate"));
            }
            else
            {
                found.Add(new("lists", "ok", "The filter lists are up to date",
                    (days == 0 ? "Updated today" : days == 1 ? "Updated yesterday" : "Updated " + days + " days ago") +
                    (auto ? ", and kept up to date on their own." : "."), null, null, null, null));
            }
        }

        // ── Connections ──
        Switch(found, "httpsOnly", "Pages load over HTTPS first",
            "HTTPS is not required",
            "Without it, a site that offers a secure version may still be loaded over plain HTTP, where anyone on the same network can read and change it.",
            "Require HTTPS");

        // ── Tracking across sites (the three engine switches) ──
        EngineSwitch(found, "thirdPartyCookies", "Third-party cookies are blocked",
            "Third-party cookies are allowed",
            "They are how ads follow you from site to site.", "Block them");
        EngineSwitch(found, "gpc", "Sites are asked not to sell or share your data",
            "Sites are not asked to leave your data alone",
            "Global Privacy Control tells every site you don’t want your data sold or shared — and in several places the law makes them listen.", "Ask them");
        EngineSwitch(found, "webrtcProtect", "Your home network address is hidden",
            "Video-calling features can see your home network address",
            "A page can read your computer’s address on your own network through them, which helps identify you.", "Hide it");

        Switch(found, "stripTracking", "Tracking is taken off links",
            "Links keep their tracking tags",
            "fbclid, gclid, utm_ tags and their kind tell the next site exactly where you came from.",
            "Take them off");
        Switch(found, "trimReferrer", "Sites only learn which site you came from",
            "Sites can learn the exact page you came from",
            "Not just the site — the page, which can say a lot about what you were reading.",
            "Trim it");

        // ── Downloads ──
        Switch(found, "warnExecutables", "Aviary warns before opening downloaded programs",
            "Downloaded programs open without a warning",
            "A program from the web can do anything your account can.",
            "Warn me");

        // ── Things worth knowing that are not wrong ──
        if (AppSettings.GetBool("suggest"))
        {
            found.Add(new("suggest", "info", "What you type goes to your search engine as you type it",
                "Search suggestions are on, so every letter typed in the address bar is sent to " +
                AppSettings.Get("searchEngine") + " before you press Enter. Never from a private window.",
                "Turn suggestions off", "suggest", "false", null));
        }

        if (AppSettings.GetBool(Diagnostics.SettingKey))
        {
            found.Add(new("improvement", "info", "Improvement data is on",
                "Everything you do in Aviary is recorded in a folder on this computer — it never leaves it. Passwords, what you type into sites, cookies, login tokens and card numbers are never collected.",
                "See what it collects", null, null, "privacy"));
        }

        return found;
    }

    /// <summary>A setting that takes effect at once.</summary>
    private static void Switch(List<Finding> found, string key, string goodTitle, string badTitle, string why, string fix)
    {
        found.Add(AppSettings.GetBool(key)
            ? new(key, "ok", goodTitle, string.Empty, null, null, null, null)
            : new(key, "warn", badTitle, why, fix, key, "true", null));
    }

    /// <summary>
    /// An engine switch: read once, when the engine starts. Saved-and-on but launched-off is its own
    /// state — switched on, and not working until Aviary restarts.
    /// </summary>
    private static void EngineSwitch(List<Finding> found, string key, string goodTitle, string badTitle, string why, string fix)
    {
        bool saved = AppSettings.GetBool(key);
        bool running = WebViewEnvironment.LaunchedWith.TryGetValue(key, out bool launched) ? launched : saved;

        if (saved && running)
        {
            found.Add(new(key, "ok", goodTitle, string.Empty, null, null, null, null));
        }
        else if (saved)
        {
            found.Add(new(key, "warn", goodTitle + " — once Aviary restarts",
                "It is switched on, but the browser engine reads this only when it starts. Close Aviary and open it again and it is working.",
                null, null, null, null));
        }
        else
        {
            found.Add(new(key, "warn", badTitle, why + " This one takes effect the next time Aviary starts.", fix, key, "true", null));
        }
    }
}
