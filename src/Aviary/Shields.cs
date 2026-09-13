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

    /// <summary>Hosts that are Aviary itself.</summary>
    public static readonly string[] OwnHosts = { Library.Host, Library.LegacyHost, "aviary.localhost" };

    private static readonly object Gate = new();

    /// <summary>Sites lowered in a private window: this session only, never saved.</summary>
    private static readonly HashSet<string> PrivateDown = new(StringComparer.OrdinalIgnoreCase);

    private static string? _script;
    private static string? _privateScript;
    private static FilterEngine? _scriptEngine;

    /// <summary>Raised on the UI thread when any site's Shields change.</summary>
    public static event Action<string>? Changed;

    public static bool Enabled => TrackerBlocker.Enabled;

    public static bool Strict => string.Equals(AppSettings.Get("blockMode"), "Strict", StringComparison.OrdinalIgnoreCase);

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
        if (!Enabled || engine is null)
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
