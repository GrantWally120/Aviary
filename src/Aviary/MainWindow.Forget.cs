using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Forgetful browsing (Alpha Build 5 M4.9) — Brave's idea, with the time Grant asked for:
/// <i>"forgetful browsing but add a time thingy"</i>. Per site, Aviary can forget everything the
/// site has stored — its cookies, local storage, databases, offline copies — when you close its last
/// tab, or five minutes, an hour or a day after. Stay signed in where you want to be; be a stranger
/// everywhere else, without clearing everything.
/// </summary>
internal static class Forgetful
{
    /// <summary>The per-site key in SiteSettings: "close", "5m", "1h" or "1d". Absent = never.</summary>
    public const string ModeKey = "forget";

    /// <summary>When a closed site is due to be forgotten (Unix milliseconds), so a restart keeps the promise.</summary>
    public const string DueKey = "forgetAt";

    /// <summary>
    /// Set once a site HAS been forgotten, and removed the moment it is open again (bug 84).
    /// Without it the sweep forgets a closed site over and over - measured at 69 times in two
    /// minutes - because "when I close it" is due the instant it is due. There is nothing left to
    /// take the second time, so every repeat is a cookie-store scan and five engine calls for
    /// nothing, every fifteen seconds, for as long as Aviary is open.
    /// </summary>
    public const string DoneKey = "forgot";

    /// <summary>
    /// M6 stage 4: "Never", CHOSEN — stored, where the default "Never" is simply no entry. Only
    /// "Forget everything when Aviary closes" needs the difference: there, a site set to Never is kept
    /// and every other site is forgotten. The sweep treats it as never (it has no delay).
    /// </summary>
    public const string KeepMode = "never";

    private static readonly (string Mode, string Label)[] Plain =
    {
        ("", "Never"),
        ("close", "When I close it"),
        ("5m", "5 minutes after"),
        ("1h", "An hour after"),
        ("1d", "A day after"),
    };

    // While "Forget everything when Aviary closes" is on, a site with no choice IS forgotten when Aviary
    // closes - so that is what its default says, and keeping it is a choice of its own.
    private static readonly (string Mode, string Label)[] WithEverything =
    {
        ("", "When Aviary closes"),
        (KeepMode, "Never — keep it"),
        ("close", "When I close it"),
        ("5m", "5 minutes after"),
        ("1h", "An hour after"),
        ("1d", "A day after"),
    };

    public static (string Mode, string Label)[] Choices => ForgetEverything.On ? WithEverything : Plain;

    /// <summary>
    /// AVIARY_FORGET_TEST=&lt;seconds&gt; turns every wait into that many seconds, and makes the sweep tick
    /// every two, so "a day after" can be watched happening. Environment only - nothing inside the
    /// browser can reach it (the AVIARY_CRASH_TEST rule), and a shipped build never sets it.
    /// </summary>
    public static readonly int TestSeconds =
        int.TryParse(Environment.GetEnvironmentVariable("AVIARY_FORGET_TEST"), out int s) && s > 0 ? s : 0;

    public static TimeSpan? Delay(string mode) => mode switch
    {
        "close" => TimeSpan.Zero,
        "5m" => TestSeconds > 0 ? TimeSpan.FromSeconds(TestSeconds) : TimeSpan.FromMinutes(5),
        "1h" => TestSeconds > 0 ? TimeSpan.FromSeconds(TestSeconds * 2) : TimeSpan.FromHours(1),
        "1d" => TestSeconds > 0 ? TimeSpan.FromSeconds(TestSeconds * 3) : TimeSpan.FromDays(1),
        _ => null,
    };

    // A kept site ("never") reads as plain "Never" once "Forget everything" is off - which is what it is.
    public static string Label(string mode) => Choices.FirstOrDefault(c => c.Mode == mode).Label ?? "Never";

    public static string ModeFor(string site) => site.Length == 0 ? string.Empty : SiteSettings.Get(site, ModeKey);

    public static void Set(string site, string mode)
    {
        if (site.Length == 0)
        {
            return;
        }

        if (mode == KeepMode)
        {
            SiteSettings.Set(site, ModeKey, KeepMode);
            SiteSettings.Remove(site, DueKey);
            SiteSettings.Remove(site, DoneKey);
            return;
        }

        if (Delay(mode) is null)
        {
            SiteSettings.Remove(site, ModeKey);
            SiteSettings.Remove(site, DueKey);
            SiteSettings.Remove(site, DoneKey);
        }
        else
        {
            SiteSettings.Set(site, ModeKey, mode);
        }
    }
}

/// <summary>
/// The window's half of forgetful browsing: a sweep every fifteen seconds, and straight after a tab
/// closes, that works out which forgetful sites are no longer open anywhere and forgets each one
/// once its time has come.
///
/// <para><b>How "closed" is decided:</b> a site is open if any tab in any NORMAL window is on it.
/// Private windows are left out on both sides — they forget everything anyway, and a private tab
/// must not keep a normal site's data alive or be touched by it.</para>
///
/// <para><b>Why the due time is saved:</b> "a day after" has to survive Aviary being closed. When a
/// forgetful site stops being open its due time is written into SiteSettings; the sweep after the
/// next launch forgets whatever came due meanwhile. A site that is still open when Aviary closes is
/// given its due time as Aviary closes, so "when I close it" includes closing Aviary.</para>
///
/// <para>Going back to the site before its time is up cancels it: open again means not closed.</para>
/// </summary>
public sealed partial class MainWindow
{
    private DispatcherTimer? _forgetTimer;
    private static int _forgetting;

    private void StartForgetting()
    {
        if (_private || _forgetTimer is not null)
        {
            return;
        }

        _forgetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Forgetful.TestSeconds > 0 ? 2 : 15) };
        _forgetTimer.Tick += (_, _) => _ = ForgetSweepAsync();
        _forgetTimer.Start();
    }

    /// <summary>Every site open in a normal window right now, as registrable domains.</summary>
    private static HashSet<string> OpenSites()
    {
        HashSet<string> open = new(StringComparer.OrdinalIgnoreCase);
        foreach (MainWindow window in OpenWindows)
        {
            if (window._private)
            {
                continue;
            }

            foreach (BrowserTab tab in window._tabs)
            {
                if (!tab.IsInternalPage && BrowserTab.HostOf(tab.Url ?? string.Empty) is { Length: > 0 } host)
                {
                    open.Add(PublicSuffix.RegistrableDomain(host));
                }
            }
        }

        return open;
    }

    /// <summary>
    /// One pass: cancel what has been reopened, start the clock on what has just closed, and forget
    /// what is due. One pass at a time across every window.
    /// </summary>
    private async Task ForgetSweepAsync()
    {
        if (_private || System.Threading.Interlocked.Exchange(ref _forgetting, 1) == 1)
        {
            return;
        }

        try
        {
            List<(string Site, string Mode)> forgetful = SiteSettings.SitesWithAny(Forgetful.ModeKey);
            if (forgetful.Count == 0)
            {
                return;
            }

            HashSet<string> open = OpenSites();
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (Forgetful.TestSeconds > 0)
            {
                DiagFeature("forget-sweep", new
                {
                    forgetful = forgetful.Select(f => f.Site + "=" + f.Mode).ToArray(),
                    open = open.ToArray(),
                    due = forgetful.Select(f => f.Site + "@" + SiteSettings.Get(f.Site, Forgetful.DueKey)).ToArray(),
                    done = forgetful.Where(f => SiteSettings.Get(f.Site, Forgetful.DoneKey).Length > 0).Select(f => f.Site).ToArray(),
                });
            }

            foreach ((string site, string mode) in forgetful)
            {
                if (Forgetful.Delay(mode) is not TimeSpan delay)
                {
                    continue;
                }

                string due = SiteSettings.Get(site, Forgetful.DueKey);

                if (open.Contains(site))
                {
                    // Open again: nothing to forget yet, and whatever was forgotten before is
                    // history - the next close starts the whole thing over.
                    if (due.Length > 0)
                    {
                        SiteSettings.Remove(site, Forgetful.DueKey);
                    }

                    if (SiteSettings.Get(site, Forgetful.DoneKey).Length > 0)
                    {
                        SiteSettings.Remove(site, Forgetful.DoneKey);
                    }

                    continue;
                }

                // Closed and already forgotten: there is nothing left to take (bug 84).
                if (SiteSettings.Get(site, Forgetful.DoneKey).Length > 0)
                {
                    continue;
                }

                if (!long.TryParse(due, NumberStyles.Integer, CultureInfo.InvariantCulture, out long dueAt))
                {
                    dueAt = now + (long)delay.TotalMilliseconds;
                    SiteSettings.Set(site, Forgetful.DueKey, dueAt.ToString(CultureInfo.InvariantCulture));
                }

                if (dueAt > now)
                {
                    continue;
                }

                // Any live tab will do: the cookie store and the Storage domain are the profile's.
                CoreWebView2? page = _tabs.Select(t => t.View.CoreWebView2).FirstOrDefault(c => c is not null);
                if (page is null)
                {
                    return;
                }

                await ClearSiteDataAsync(page, site);
                SiteSettings.Set(site, Forgetful.DoneKey, "1");
                SiteSettings.Remove(site, Forgetful.DueKey);
                DiagFeature("site-forgotten", new { site, mode });
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "forgetful browsing: sweep");
        }
        finally
        {
            System.Threading.Interlocked.Exchange(ref _forgetting, 0);
        }
    }

    /// <summary>
    /// Aviary is closing: every forgetful site still open gets its due time now, so "when I close
    /// it" also means "when I close Aviary", and the first sweep of the next launch keeps the promise.
    /// </summary>
    private static void MarkForgetfulOnExit()
    {
        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            HashSet<string> open = OpenSites();
            foreach ((string site, string mode) in SiteSettings.SitesWithAny(Forgetful.ModeKey))
            {
                if (open.Contains(site) && Forgetful.Delay(mode) is TimeSpan delay
                    && SiteSettings.Get(site, Forgetful.DueKey).Length == 0)
                {
                    SiteSettings.Set(site, Forgetful.DueKey,
                        (now + (long)delay.TotalMilliseconds).ToString(CultureInfo.InvariantCulture));
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "forgetful browsing: on exit");
        }
    }
}
