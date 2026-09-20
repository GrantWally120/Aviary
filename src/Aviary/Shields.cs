using System;
using System.Collections.Generic;
using System.Linq;

namespace Aviary;

/// <summary>
/// Shields (M3, Brave's word for it): what gets blocked, and where Aviary steps back.
///
/// <list type="bullet">
/// <item><b>Everywhere</b> — the "Block ads and trackers" switch (<c>blockTrackers</c>), which has
/// always been <see cref="TrackerBlocker.Enabled"/>. Off means nothing is blocked or hidden.</item>
/// <item><b>How much</b> — <c>blockMode</c>. <b>Standard</b>, Brave's default: the lists stop what
/// OTHER sites load into a page (the ads and trackers that follow you around); a site's own
/// requests are left alone. <b>Strict</b>: the lists apply to every request, and a site the lists
/// mark as ad-only or a scam gets a warning instead of opening.</item>
/// <item><b>Per site</b> — Shields down for one site (its registrable domain, like every other
/// per-site choice in <see cref="SiteSettings"/>): nothing on its pages is blocked or hidden,
/// including what it loads from elsewhere. In a private window the choice lasts until the window
/// closes and is never written down.</item>
/// </list>
///
/// Aviary's own pages and the Collection are never filtered — there is nothing to block.
/// </summary>
internal static class Shields
{
    public const string SiteKey = "shieldsDown";

    /// <summary>The site key holding a site's own Standard/Strict choice (M4.5).</summary>
    public const string ModeKey = "shieldsMode";

    /// <summary>How much Shields do on a site: nothing, Standard, or Strict.</summary>
    public enum Mode
    {
        Off,
        Standard,
        Strict,
    }

    /// <summary>Hosts that are Aviary itself.</summary>
    public static readonly string[] OwnHosts = { Library.Host, Library.LegacyHost, "aviary.localhost" };

    private static readonly object Gate = new();

    /// <summary>Sites lowered in a private window: this session only, never saved.</summary>
    private static readonly HashSet<string> PrivateDown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sites given their own Standard/Strict in a private window: this session only.</summary>
    private static readonly Dictionary<string, string> PrivateMode = new(StringComparer.OrdinalIgnoreCase);

    private static string? _script;
    private static string? _privateScript;
    private static FilterEngine? _scriptEngine;

    /// <summary>Raised on the UI thread when any site's Shields change.</summary>
    public static event Action<string>? Changed;

    public static bool Enabled => TrackerBlocker.Enabled;

    public static bool Strict => string.Equals(AppSettings.Get("blockMode"), "Strict", StringComparison.OrdinalIgnoreCase);

    /// <summary>Hide the empty spaces blocked ads leave behind (M4.5: its own switch — some sites lay out worse without their boxes).</summary>
    public static bool HidesSpaces => !string.Equals(AppSettings.Get("cosmeticHiding"), "false", StringComparison.OrdinalIgnoreCase);

    /// <summary>Warn before opening a site the lists say exists only to serve ads or track people (M4.5: its own switch).</summary>
    public static bool WarnsAboutSites => !string.Equals(AppSettings.Get("strictWarning"), "false", StringComparison.OrdinalIgnoreCase);

    public static bool IsOwnHost(string host)
    {
        foreach (string own in OwnHosts)
        {
            if (string.Equals(host, own, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The site a host belongs to — what a Shields choice is kept against.</summary>
    public static string SiteOf(string host) => host.Length == 0 ? string.Empty : FilterEngine.SiteOf(host.ToLowerInvariant());

    public static bool IsDown(string host, bool isPrivate)
    {
        if (host.Length == 0)
        {
            return false;
        }

        if (isPrivate)
        {
            lock (Gate)
            {
                if (PrivateDown.Contains(SiteOf(host)))
                {
                    return true;
                }
            }
        }

        return SiteSettings.GetBool(host, SiteKey);
    }

    /// <summary>Lowers or raises Shields for a host's whole site. Call on the UI thread.</summary>
    public static void Set(string host, bool down, bool isPrivate)
    {
        string site = SiteOf(host);
        if (site.Length == 0)
        {
            return;
        }

        if (isPrivate)
        {
            lock (Gate)
            {
                if (down) { PrivateDown.Add(site); } else { PrivateDown.Remove(site); }
            }

            // Raising Shields in a private window must also raise a saved "down" — otherwise
            // the switch would appear to do nothing there.
            if (!down && SiteSettings.GetBool(host, SiteKey))
            {
                SiteSettings.Remove(host, SiteKey);
            }
        }
        else if (down)
        {
            SiteSettings.Set(host, SiteKey, "true");
        }
        else
        {
            SiteSettings.Remove(host, SiteKey);
        }

        Invalidate();

        // A private window's choices are never written anywhere, diagnostics included.
        if (!isPrivate)
        {
            Diagnostics.Shields(down ? "site-down" : "site-up", new { site });
        }

        Changed?.Invoke(site);
    }

    /// <summary>Every site with Shields down in this profile (not a private window's).</summary>
    public static IReadOnlyList<string> DownSites() => SiteSettings.SitesWith(SiteKey);

    // == PER SITE: OFF, STANDARD OR STRICT (M4.5) =============================

    /// <summary>What a site with no choice of its own gets: the "How much to block" dropdown.</summary>
    public static Mode Default => Strict ? Mode.Strict : Mode.Standard;

    /// <summary>
    /// How much Shields do on this host's site: its own choice if it has one, otherwise the
    /// default. A site's choice survives a change to the default - it was made deliberately.
    /// </summary>
    public static Mode ModeFor(string host, bool isPrivate)
    {
        if (!Enabled)
        {
            return Mode.Off;
        }

        if (host.Length == 0)
        {
            return Default;
        }

        if (IsDown(host, isPrivate))
        {
            return Mode.Off;
        }

        string chosen = string.Empty;
        if (isPrivate)
        {
            lock (Gate)
            {
                chosen = PrivateMode.TryGetValue(SiteOf(host), out string? own) ? own : string.Empty;
            }
        }

        if (chosen.Length == 0)
        {
            chosen = SiteSettings.Get(host, ModeKey);
        }

        return chosen.ToLowerInvariant() switch
        {
            "strict" => Mode.Strict,
            "standard" => Mode.Standard,
            _ => Default,
        };
    }

    /// <summary>Whether this page is filtered strictly: the site's choice, or the default.</summary>
    public static bool StrictFor(string host, bool isPrivate) => ModeFor(host, isPrivate) == Mode.Strict;

    /// <summary>Gives one site its own setting. Call on the UI thread.</summary>
    public static void SetMode(string host, Mode mode, bool isPrivate)
    {
        string site = SiteOf(host);
        if (site.Length == 0)
        {
            return;
        }

        if (mode == Mode.Off)
        {
            ClearMode(site, isPrivate);
            Set(host, down: true, isPrivate);
            return;
        }

        string word = mode == Mode.Strict ? "Strict" : "Standard";
        if (isPrivate)
        {
            lock (Gate) { PrivateMode[site] = word; }
        }
        else
        {
            SiteSettings.Set(site, ModeKey, word);
        }

        // Standard and Strict are how much, not whether, so either also puts Shields back up.
        if (IsDown(host, isPrivate))
        {
            Set(host, down: false, isPrivate);
        }
        else
        {
            Invalidate();
            if (!isPrivate)
            {
                Diagnostics.Shields("site-mode", new { site, mode = word });
            }

            Changed?.Invoke(site);
        }
    }

    /// <summary>Back to the default for one site: no choice of its own, Shields up.</summary>
    public static void ResetSite(string host, bool isPrivate)
    {
        string site = SiteOf(host);
        if (site.Length == 0)
        {
            return;
        }

        ClearMode(site, isPrivate);
        if (IsDown(host, isPrivate))
        {
            Set(host, down: false, isPrivate);
        }
        else
        {
            Invalidate();
            Changed?.Invoke(site);
        }
    }

    private static void ClearMode(string site, bool isPrivate)
    {
        if (isPrivate)
        {
            lock (Gate) { PrivateMode.Remove(site); }
        }

        if (SiteSettings.Get(site, ModeKey).Length > 0)
        {
            SiteSettings.Remove(site, ModeKey);
        }
    }

    /// <summary>Every site with a choice of its own, and what it is. Saved choices only.</summary>
    public static List<(string Site, Mode Mode)> OwnSites()
    {
        Dictionary<string, Mode> sites = new(StringComparer.OrdinalIgnoreCase);
        foreach (string site in SiteSettings.SitesWith(ModeKey, "Standard"))
        {
            sites[site] = Mode.Standard;
        }

        foreach (string site in SiteSettings.SitesWith(ModeKey, "Strict"))
        {
            sites[site] = Mode.Strict;
        }

        // Off wins: a site with Shields down is off whatever else is written against it.
        foreach (string site in DownSites())
        {
            sites[site] = Mode.Off;
        }

        List<(string Site, Mode Mode)> list = sites.Select(p => (p.Key, p.Value)).ToList();
        list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Site, b.Site));
        return list;
    }

    /// <summary>Clears every site's own choice. Call on the UI thread.</summary>
    public static void ResetAllSites()
    {
        foreach ((string site, Mode _) in OwnSites())
        {
            SiteSettings.Remove(site, ModeKey);
            SiteSettings.Remove(site, SiteKey);
        }

        lock (Gate)
        {
            PrivateDown.Clear();
            PrivateMode.Clear();
        }

        Diagnostics.Shields("sites-reset", new { });
        Refresh();
    }

    /// <summary>The page script needs rebuilding: a site changed, the lists changed, or they were cleared.</summary>
    public static void Invalidate()
    {
        lock (Gate)
        {
            _script = null;
            _privateScript = null;
        }
    }

    /// <summary>
    /// Something beyond one site changed — blocking switched on or off everywhere, or every
    /// site's choice cleared — so every tab, in every window, re-reads its state and its script.
    /// Call on the UI thread.
    /// </summary>
    public static void Refresh()
    {
        Invalidate();
        Changed?.Invoke(string.Empty);
    }

    /// <summary>
    /// The element-hiding script for a tab, or null when nothing should be hidden (blocking off,
    /// or the lists not loaded yet). Built once and shared by every tab: it is large (~700 KB) and
    /// identical for them all.
    /// </summary>
    public static string? CosmeticScript(bool isPrivate)
    {
        FilterEngine? engine = FilterLists.Engine;
        if (!Enabled || !HidesSpaces || engine is null)
        {
            return null;
        }

        lock (Gate)
        {
            if (!ReferenceEquals(engine, _scriptEngine))
            {
                _script = null;
                _privateScript = null;
                _scriptEngine = engine;
            }

            if (isPrivate)
            {
                return _privateScript ??= engine.CosmeticScript(DownSites().Concat(PrivateDown).Distinct(StringComparer.OrdinalIgnoreCase), OwnHosts);
            }

            return _script ??= engine.CosmeticScript(DownSites(), OwnHosts);
        }
    }
}
