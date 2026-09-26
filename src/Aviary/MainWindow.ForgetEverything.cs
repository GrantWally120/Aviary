using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Settings → Experimental → "Forget everything when Aviary closes" (Alpha Build 5 M6 stage 4).
///
/// <para>When the last ordinary window closes - however it closes: the ×, Exit, the last tab, a question
/// answered, Restart Aviary (bug 96) - every website's cookies and everything websites store
/// (the engine's "all site data", and its cache) are cleared — through the engine, in one call, not site
/// by site. Aviary's own history, bookmarks, passwords, downloads list and per-site choices stay: they
/// are not the websites' data. A site set to "Never" in Forgetful browsing (the Shields popover) is KEPT
/// — its cookies are copied out first and put back after, so it stays signed in. Only its cookies: what
/// else such a site stored goes with the rest, and the row says so.</para>
///
/// <para><b>When Aviary cannot close properly</b> — Windows shutting down with it open, a crash — there is
/// no close to wait on. So while this is on, a marker in the profile says "a session is running that has
/// something to forget"; a clean forget removes it, and a launch that finds it forgets FIRST: pinned tabs
/// come back asleep, the clearing runs through the first Start Page, and only then can a site load.</para>
/// </summary>
internal static class ForgetEverything
{
    public static bool On => AppSettings.GetBool("exp.forgetOnClose");

    private static string MarkerPath => AppPaths.Of("forget-everything-pending");

    /// <summary>
    /// A launch found the last session's forgetting not done. The marker alone decides, not the switch:
    /// after a crash, the launch's crash protection switches every experiment off (SafetyMarker) - this
    /// one too - and what that session was meant to forget must still go (found 26 Sep, a killed Aviary).
    /// </summary>
    public static bool PendingAtLaunch => File.Exists(MarkerPath);

    /// <summary>From launch and from the switch: while it is on, this session has something to forget.</summary>
    public static void MarkSession()
    {
        try
        {
            if (On)
            {
                File.WriteAllText(MarkerPath, DateTimeOffset.Now.ToString("o"));
            }
            else if (File.Exists(MarkerPath))
            {
                File.Delete(MarkerPath);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "forget everything: marker");
        }
    }

    /// <summary>Forgotten: nothing is owed to the next launch.</summary>
    public static void Done()
    {
        try
        {
            File.Delete(MarkerPath);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "forget everything: clear marker");
        }
    }
}

public sealed partial class MainWindow
{
    /// <summary>This window has done the forgetting for this close; the next close goes through.</summary>
    private bool _forgotEverything;

    /// <summary>The launch's check for a forget owed by the last session has been made (first window only).</summary>
    private static bool _forgetCheckedThisLaunch;

    /// <summary>
    /// The last ordinary window is closing - or, for a restart, every window is - with the experiment on
    /// and nothing done yet.
    /// </summary>
    private bool ForgetEverythingDue(bool everyWindow = false) =>
        ForgetEverything.On && !_private && !_forgotEverything
        && (everyWindow || !OpenWindows.Any(w => !ReferenceEquals(w, this) && !w._private));

    /// <summary>
    /// From a close (MainWindow.CloseGuard.cs, CloseForRealAsync) or a restart: forget now, if this is the
    /// close that owes it. The caller closes afterwards, whatever happened here.
    /// </summary>
    private async Task ForgetEverythingIfDueAsync(bool everyWindow = false, string when = "as Aviary closed")
    {
        if (!ForgetEverythingDue(everyWindow))
        {
            return;
        }

        _forgotEverything = true;
        if (_tabs.Select(t => t.View.CoreWebView2).FirstOrDefault(c => c is not null) is { } core
            && await ForgetEverythingNowAsync(core, when))
        {
            ForgetEverything.Done();
        }
    }

    /// <summary>
    /// Clears every website's data through the engine, keeping the cookies of sites set to "Never".
    /// Ten seconds at most: a close must not hang on it - if it does not finish, the marker stays and
    /// the next launch forgets instead.
    /// </summary>
    private static async Task<bool> ForgetEverythingNowAsync(CoreWebView2 core, string when)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            HashSet<string> keep = new(SiteSettings.SitesWith(Forgetful.ModeKey, Forgetful.KeepMode), StringComparer.OrdinalIgnoreCase);
            List<CoreWebView2Cookie> saved = new();
            if (keep.Count > 0)
            {
                foreach (CoreWebView2Cookie cookie in await core.CookieManager.GetCookiesAsync(string.Empty))
                {
                    if (keep.Contains(PublicSuffix.RegistrableDomain(cookie.Domain.TrimStart('.'))))
                    {
                        saved.Add(cookie);
                    }
                }
            }

            Task clear = core.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.AllSite | CoreWebView2BrowsingDataKinds.DiskCache).AsTask();
            if (await Task.WhenAny(clear, Task.Delay(TimeSpan.FromSeconds(10))) != clear)
            {
                DiagFeatureStatic("forgot-everything", new { when, timedOut = true });
                return false;
            }

            await clear;
            foreach (CoreWebView2Cookie cookie in saved)
            {
                core.CookieManager.AddOrUpdateCookie(cookie);
            }

            DiagFeatureStatic("forgot-everything", new
            {
                when,
                keptSites = keep.Count,
                keptCookies = saved.Count,
                ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1),
            });
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "forget everything");
            return false;
        }
    }

    /// <summary>
    /// From the first ordinary window's start: the last session ended without forgetting. Runs through
    /// the first tab - a Start Page, which stores nothing - before any pinned tab is woken.
    /// </summary>
    private async Task ForgetOwedAtLaunchAsync()
    {
        if (_tabs.Select(t => t.View.CoreWebView2).FirstOrDefault(c => c is not null) is { } core
            && await ForgetEverythingNowAsync(core, "at launch - the last session did not close properly"))
        {
            ForgetEverything.Done();
        }
    }
}
