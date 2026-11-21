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
    /// <summary>
    /// Beta Prep 2: every finding also says which part of Aviary it is about (<c>Area</c>), and there
    /// is a fourth level, <c>tip</c> - a stronger setting that is OFF by choice and costs something
    /// (Strict breaks a few sites), so it is offered, never counted against the score.
    /// </summary>
    public sealed record Finding(string Id, string Level, string Title, string Detail, string? FixLabel, string? FixKey, string? FixValue, string? Goto)
    {
        public string Area { get; init; } = string.Empty;
    }

    private const string AreaShields = "Shields";
    private const string AreaConnections = "Connections";
    private const string AreaTracking = "Tracking";
    private const string AreaWebsites = "Websites";
    private const string AreaDownloads = "Downloads";
    private const string AreaData = "Your data";

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
        // Beta Prep 2.
        ["blockLinkPings"] = "true",
        ["popups"] = "true",
        [BounceTracking.SettingKey] = "true",
    };

    public static List<Finding> Run()
    {
        List<Finding> found = new();
        Run(found);
        return found;
    }

    private static void Run(List<Finding> found)
    {
        // ── Shields ──
        if (!AppSettings.GetBool("blockTrackers"))
        {
            found.Add(new("shields", "warn", "Shields are off everywhere",
                "No site’s ads or trackers are being stopped, so they can follow you from one site to the next.",
                "Turn Shields on", "blockTrackers", "true", null) { Area = AreaShields });
        }
        else
        {
            found.Add(new("shields", "ok", "Shields are on",
                (Shields.Strict ? "Strict" : "Standard") + " everywhere, with " + FilterLists.Current.Count +
                (FilterLists.Current.Count == 1 ? " list" : " lists") + " loaded.", null, null, null, null) { Area = AreaShields });

            if (!Shields.Strict)
            {
                found.Add(new("strict", "tip", "Strict Shields would stop more",
                    "Standard leaves alone the ads and trackers a site serves itself. Strict stops those too, and warns before a site that exists only to track. It can break parts of a few sites, and you can lower it for any one of them from the shield in the toolbar.",
                    "Open Shields", null, null, "shields") { Area = AreaShields });
            }

            int down = Shields.DownSites().Count;
            if (down > 0)
            {
                found.Add(new("shieldsDown", "info", down == 1 ? "Shields are off for 1 site" : "Shields are off for " + down + " sites",
                    "You turned them off for these yourself, usually because a site didn’t work with them. Worth a look now and then — a site that works now doesn’t need them off.",
                    "Review them", null, null, "shields") { Area = AreaShields });
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
                    "Update them now", null, null, "cmd:listsUpdate") { Area = AreaShields });
            }
            else
            {
                found.Add(new("lists", "ok", "The filter lists are up to date",
                    (days == 0 ? "Updated today" : days == 1 ? "Updated yesterday" : "Updated " + days + " days ago") +
                    (auto ? ", and kept up to date on their own." : "."), null, null, null, null) { Area = AreaShields });
            }

            if (!auto)
            {
                found.Add(new("listsAuto", "tip", "The filter lists are not kept up to date on their own",
                    "They stay as they are until you press Update now. Daily or Weekly fetches the newest copies from the people who publish them - the same file for everyone, with nothing about you sent.",
                    "Open Shields", null, null, "shields") { Area = AreaShields });
            }
        }

        // ── Connections ──
        Switch(found, AreaConnections, "httpsOnly", "Pages load over HTTPS first",
            "HTTPS is not required",
            "Without it, a site that offers a secure version may still be loaded over plain HTTP, where anyone on the same network can read and change it.",
            "Require HTTPS");

        // Sites you chose to open without a secure connection: a choice, remembered per site.
        int insecure = SiteSettings.SitesWith("httpAllowed").Count;
        if (insecure > 0)
        {
            found.Add(new("httpAllowed", "info",
                insecure == 1 ? "1 site may load without a secure connection" : insecure + " sites may load without a secure connection",
                "You chose “continue” when Aviary said these had no secure version, and it remembered. Anyone on the same network can read and change what an insecure page shows. Forgetting them makes Aviary ask again.",
                "Forget them", null, null, "cmd:httpForget") { Area = AreaConnections });
        }

        // Always true, so always said: it is half of what stands between a download and this computer.
        found.Add(new("smartscreen", "ok", "Microsoft Defender SmartScreen checks sites and downloads",
            "Known phishing pages and harmful downloads are stopped by Windows’ own check, which Aviary keeps switched on.",
            null, null, null, null) { Area = AreaConnections });

        // ── Tracking across sites (the three engine switches) ──
        EngineSwitch(found, AreaTracking, "thirdPartyCookies", "Third-party cookies are blocked",
            "Third-party cookies are allowed",
            "They are how ads follow you from site to site.", "Block them");
        EngineSwitch(found, AreaTracking, "gpc", "Sites are asked not to sell or share your data",
            "Sites are not asked to leave your data alone",
            "Global Privacy Control tells every site you don’t want your data sold or shared — and in several places the law makes them listen.", "Ask them");
        EngineSwitch(found, AreaTracking, "webrtcProtect", "Your home network address is hidden",
            "Video-calling features can see your home network address",
            "A page can read your computer’s address on your own network through them, which helps identify you.", "Hide it");

        Switch(found, AreaTracking, "stripTracking", "Tracking is taken off links",
            "Links keep their tracking tags",
            "fbclid, gclid, utm_ tags and their kind tell the next site exactly where you came from.",
            "Take them off");
        Switch(found, AreaTracking, "trimReferrer", "Sites only learn which site you came from",
            "Sites can learn the exact page you came from",
            "Not just the site — the page, which can say a lot about what you were reading.",
            "Trim it");

        // Beta Prep 2: the two newer link protections.
        Switch(found, AreaTracking, "blockLinkPings", "Link tracking pings are blocked",
            "Clicked links can report the click",
            "A link can quietly tell a server it was clicked, alongside taking you where it goes.",
            "Block the pings");
        Switch(found, AreaTracking, BounceTracking.SettingKey, "Tracking redirects are skipped",
            "Links go through tracking redirects",
            "Links on Facebook, Instagram, Google, YouTube and others go through the company’s own redirect page first, which notes the click. Aviary can go straight to where the link leads.",
            "Skip them");

        if (!AppSettings.GetBool("exp.fingerprint"))
        {
            found.Add(new("fingerprint", "tip", "Stronger fingerprint protection is off",
                "Scripts can recognise a browser without cookies, from how it draws and from its hardware. Aviary can blur that, per site. It is an experiment: a site that reads back its own drawings can come out a hair different.",
                "Open Experimental", null, null, "experimental") { Area = AreaTracking });
        }

        // ── What websites may do (Beta Prep 2) ──
        Switch(found, AreaWebsites, "popups", "Pop-up windows are blocked",
            "Sites can open pop-up windows",
            "A page can open windows you did not ask for - the usual way unwanted adverts and fake warnings arrive.",
            "Block pop-ups");

        List<string> blocked = SitePermissions.BlockedNames();
        found.Add(new("permissions", "ok",
            blocked.Count == 4 ? "No site can use your location, camera, microphone or notifications"
                               : "Sites must ask before using your location, camera, microphone or notifications",
            blocked.Count is > 0 and < 4
                ? "Blocked for every site, without a question: " + string.Join(", ", blocked) + ". Pages without a secure connection are always refused."
                : blocked.Count == 4 ? "All four are blocked for every site, without a question."
                : "Each site asks, and you answer for that site. Pages without a secure connection are always refused. Content can block any of them outright.",
            null, null, null, null) { Area = AreaWebsites });

        // ── Downloads ──
        Switch(found, AreaDownloads, "warnExecutables", "Aviary warns before opening downloaded programs",
            "Downloaded programs open without a warning",
            "A program from the web can do anything your account can.",
            "Warn me");

        // ── Things worth knowing that are not wrong ──
        if (AppSettings.GetBool("suggest"))
        {
            found.Add(new("suggest", "info", "What you type goes to your search engine as you type it",
                "Search suggestions are on, so every letter typed in the address bar is sent to " +
                AppSettings.Get("searchEngine") + " before you press Enter. Never from a private window.",
                "Turn suggestions off", "suggest", "false", null) { Area = AreaData });
        }

        // Beta Prep 2: two more things kept on this computer that are worth knowing about.
        if (AppSettings.GetBool("savePasswords"))
        {
            found.Add(new("passwords", "info", "Aviary offers to save passwords",
                "Saved passwords stay in this profile, encrypted with your Windows account - so anyone who can use this Windows account can use them. A private window never saves one.",
                "Open Autofill", null, null, "autofill") { Area = AreaData });
        }

        if (AppSettings.GetBool("formAutofill"))
        {
            found.Add(new("forms", "info", "Aviary remembers what you type into forms",
                "Names, addresses and search boxes you fill in are kept in this profile on this computer, to offer again. Autofill can forget them.",
                "Open Autofill", null, null, "autofill") { Area = AreaData });
        }

        if (AppSettings.GetBool(Diagnostics.SettingKey))
        {
            found.Add(new("improvement", "info", "Improvement data is on",
                "Detailed browsing and app records are stored on this computer. This feature has no upload or email sender. Secret masking is best effort; addresses, titles and errors may still contain sensitive details. Review the Privacy Policy and records before sharing.",
                "See what it collects", null, null, "privacy") { Area = AreaData });
        }
    }

    /// <summary>A setting that takes effect at once.</summary>
    private static void Switch(List<Finding> found, string area, string key, string goodTitle, string badTitle, string why, string fix)
    {
        found.Add(AppSettings.GetBool(key)
            ? new Finding(key, "ok", goodTitle, string.Empty, null, null, null, null) { Area = area }
            : new Finding(key, "warn", badTitle, why, fix, key, "true", null) { Area = area });
    }

    /// <summary>
    /// An engine switch: read once, when the engine starts. Saved-and-on but launched-off is its own
    /// state — switched on, and not working until Aviary restarts.
    /// </summary>
    private static void EngineSwitch(List<Finding> found, string area, string key, string goodTitle, string badTitle, string why, string fix)
    {
        bool saved = AppSettings.GetBool(key);
        bool running = WebViewEnvironment.LaunchedWith.TryGetValue(key, out bool launched) ? launched : saved;

        if (saved && running)
        {
            found.Add(new(key, "ok", goodTitle, string.Empty, null, null, null, null) { Area = area });
        }
        else if (saved)
        {
            found.Add(new(key, "warn", goodTitle + " — once Aviary restarts",
                "It is switched on, but the browser engine reads this only when it starts. Close Aviary and open it again and it is working.",
                "Restart Aviary", null, null, "cmd:restart") { Area = area });
        }
        else
        {
            found.Add(new(key, "warn", badTitle, why + " This one takes effect the next time Aviary starts.", fix, key, "true", null) { Area = area });
        }
    }
}
