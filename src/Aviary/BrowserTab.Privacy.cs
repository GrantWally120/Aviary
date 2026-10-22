using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Phase 5 M2 — the privacy work that happens per navigation and per request: the Safari
/// identity, tracking parameters, HTTPS-Only and referrer trimming. (Third-party cookies,
/// Global Privacy Control and WebRTC are engine switches: <see cref="WebViewEnvironment"/>.)
/// </summary>
internal sealed partial class BrowserTab
{
    // ══ IDENTIFY AS SAFARI ═════════════════════════════════════════════════

    /// <summary>What the engine calls itself before Aviary overrides it (Edge WebView2's own).</summary>
    private string _defaultUserAgent = string.Empty;

    private string? _hideUaDataScriptId;

    /// <summary>
    /// Real Safari has no navigator.userAgentData; a Chromium engine always does, and it says
    /// "Microsoft Edge WebView2" to any script that looks. Removed from the prototype so
    /// `'userAgentData' in navigator` is false, as it is in Safari. Workers keep theirs — no
    /// injection reaches a worker. The matching Sec-CH-UA HEADERS are taken off every request
    /// in CleanOutgoingRequest: plain Chromium stops sending them when the user agent is
    /// overridden, but WebView2's override does not (measured, M2).
    /// </summary>
    private const string HideUaDataScript = """
(function(){
  try { delete Navigator.prototype.userAgentData; } catch (e) {}
})();
""";

    /// <summary>
    /// The identity this tab last told the engine to use (true = Safari). Set BEFORE the awaits in
    /// <see cref="ApplyUserAgentAsync(string)"/>, so a navigation re-issued for an identity change finds
    /// it already applied and cannot go round again.
    /// </summary>
    private bool _identityIsSafari;

    /// <summary>
    /// Applies the "Identify as Safari" switch to this tab, for the site it is showing. Takes effect
    /// from the next page this tab loads — the page already showing was requested with the old identity.
    /// </summary>
    public Task ApplyUserAgentAsync() => ApplyUserAgentAsync(HostOf(Url));

    /// <summary>
    /// The same, for a site this tab is about to load. A site that breaks under the Safari identity
    /// (<see cref="SafariIdentity"/>) gets the engine's own, whatever the switch says.
    /// </summary>
    private async Task ApplyUserAgentAsync(string host)
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            bool safari = SafariIdentity.WantedFor(host, AppSettings.GetBool("safariUa"));
            _identityIsSafari = safari;
            core.Settings.UserAgent = safari ? SafariUserAgent : _defaultUserAgent;

            // The page loads Aviary starts itself (the address bar, a bookmark) get their
            // Sec-CH-UA hints attached AFTER the request filter has run, so removing them there
            // is not enough (measured: typed addresses still sent "Microsoft Edge WebView2").
            // A DevTools user-agent override with NO hint values is, in Chromium's own
            // client_hints.cc, the case that turns the hints off for this tab's navigations.
            // An empty override removes it again.
            try
            {
                await core.CallDevToolsProtocolMethodAsync("Emulation.setUserAgentOverride",
                    System.Text.Json.JsonSerializer.Serialize(new { userAgent = safari ? SafariUserAgent : string.Empty }));
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "tab: DevTools user-agent override");
            }

            if (safari && _hideUaDataScriptId is null)
            {
                _hideUaDataScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(HideUaDataScript);
            }
            else if (!safari && _hideUaDataScriptId is not null)
            {
                core.RemoveScriptToExecuteOnDocumentCreated(_hideUaDataScriptId);
                _hideUaDataScriptId = null;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: apply user agent");
        }
    }

    // ══ PASSWORDS AND FORM ENTRIES ═════════════════════════════════════════

    /// <summary>
    /// "Offer to save passwords" and "Suggest what you've typed before", made real (M2).
    ///
    /// Both are PROFILE-wide in WebView2 — every tab on a profile shares one value — so every
    /// tab sets the same thing and the last write simply agrees with the others. Form-entry
    /// autofill is ON in WebView2 unless an app says otherwise, which meant the engine was
    /// quietly keeping what you typed into websites while Settings said autofill came "after
    /// beta"; it now follows its own switch, off by default. A private window's off-the-record
    /// profile keeps nothing either way.
    /// </summary>
    public void ApplyAutofill()
    {
        try
        {
            if (View.CoreWebView2?.Profile is not CoreWebView2Profile profile)
            {
                return;
            }

            profile.IsPasswordAutosaveEnabled = !IsPrivate && AppSettings.GetBool("savePasswords");
            profile.IsGeneralAutofillEnabled = !IsPrivate && AppSettings.GetBool("formAutofill");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: apply autofill settings");
        }
    }

    // ══ THE PRIVACY STEP OF A NAVIGATION ═══════════════════════════════════

    /// <summary>The address Aviary itself asked this tab to load (address bar, bookmark…).</summary>
    private string _hostNavigation = string.Empty;

    /// <summary>The previous address in the navigation now in flight — a redirect's source.</summary>
    private string _prevUriInNavigation = string.Empty;

    /// <summary>A navigation Aviary cancelled and replaced; its "cancelled" line is not news.</summary>
    private ulong _replacedNavId;

    /// <summary>Where tracking parameters were last taken off, for the redirect-loop guard.</summary>
    private string _lastCleaned = string.Empty;
    private long _lastCleanedAt;

    /// <summary>An HTTPS-Only upgrade in flight: the https address tried, the http one it replaced.</summary>
    private sealed record Upgrade(string Https, string Http, ulong NavId);

    private Upgrade? _upgrade;
    private bool _upgradeTimedOut;
    private DispatcherQueueTimer? _upgradeTimer;

    /// <summary>How long the secure attempt gets to start answering before Aviary gives up on it.</summary>
    private static readonly TimeSpan UpgradeDeadline = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Decides whether this navigation goes out as asked, or is replaced by a cleaner one —
    /// tracking parameters removed, http:// made https:// — and if so cancels it and issues
    /// the replacement. True when it did.
    ///
    /// Cancel-and-renavigate is the only way WebView2 lets a host change where a top-level
    /// navigation goes (NavigationStarting's address is read-only), so two guards matter:
    /// never for a form submission (a POST re-issued as a GET loses the form), and never
    /// in a loop (a site that insists on its parameters, or answers https by redirecting
    /// back to http, gets what it insists on — or the fallback panel — not an endless fight).
    /// </summary>
    private bool MaybeRewriteNavigation(CoreWebView2 core, CoreWebView2NavigationStartingEventArgs args)
    {
        try
        {
            string uri = args.Uri;
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? target) || target.Scheme is not ("http" or "https"))
            {
                return false;
            }

            bool redirected = args.IsRedirected;
            bool fromHost = !redirected && string.Equals(_hostNavigation, uri, StringComparison.Ordinal);
            if (!redirected)
            {
                _hostNavigation = string.Empty;
            }

            // Where this navigation came FROM: a redirect's previous hop, the page a link was
            // on, or nowhere at all for the address bar, a bookmark or a fresh tab.
            string from = redirected ? _prevUriInNavigation : fromHost || IsInternalPage ? string.Empty : Url;

            if (HasRequestBody(args))
            {
                return false;
            }

            string next = uri;
            List<string>? removed = null;
            bool upgraded = false;

            // ── tracking parameters: only when crossing between sites ──
            if (AppSettings.GetBool("stripTracking")
                && CrossesSites(from, target)
                && LinkCleaner.TryClean(next, out string cleaned, out List<string> names))
            {
                bool insisting = redirected
                    && string.Equals(_prevUriInNavigation, _lastCleaned, StringComparison.Ordinal)
                    && System.Diagnostics.Stopwatch.GetElapsedTime(_lastCleanedAt).TotalSeconds < 10;

                if (!insisting)
                {
                    next = cleaned;
                    removed = names;
                }
            }

            // ── HTTPS-Only ──
            if (target.Scheme == "http" && NeedsHttpsUpgrade(target))
            {
                // The secure attempt redirected straight back to http: the site will not serve
                // itself securely. Offer the choice rather than bounce between the two forever.
                if (redirected && _upgrade is { } attempt
                    && Uri.TryCreate(attempt.Http, UriKind.Absolute, out Uri? original)
                    && string.Equals(original.Host, target.Host, StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                    _replacedNavId = args.NavigationId;
                    RaiseHttpsFallback(attempt.Http, "redirected back to http");
                    return true;
                }

                next = "https://" + next.Substring("http://".Length);
                upgraded = true;
            }

            // ── identity: a site that breaks under "Identify as Safari" (YouTube) is asked for with the
            //    engine's own, and one that does not is asked for with Safari's again. The identity can
            //    only change before the request goes out, so this too is cancel-and-re-issue. Never for
            //    Back/Forward or Reload, which re-issuing would turn into a new history entry.
            bool switchIdentity = _identityIsSafari != SafariIdentity.WantedFor(target.Host, AppSettings.GetBool("safariUa"))
                                  && !KeepsHistoryEntry(args);

            if (string.Equals(next, uri, StringComparison.Ordinal) && !switchIdentity)
            {
                return false;
            }

            args.Cancel = true;
            _replacedNavId = args.NavigationId;

            if (removed is not null)
            {
                _lastCleaned = next;
                _lastCleanedAt = System.Diagnostics.Stopwatch.GetTimestamp();

                if (Records)
                {
                    Diagnostics.Browse("link-cleaned", string.Join(",", removed), new
                    {
                        tab = Id,
                        removed,
                        url = next,
                        redirect = redirected,
                    });
                }
            }

            if (upgraded)
            {
                string http = removed is null ? uri : "http://" + next.Substring("https://".Length);
                _upgrade = new Upgrade(next, http, 0);
                _upgradeTimedOut = false;

                if (Records)
                {
                    Diagnostics.Browse("https-upgrade", target.Host, new { tab = Id, from = http, to = next });
                }
            }

            string go = next;
            View.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    if (switchIdentity && Uri.TryCreate(go, UriKind.Absolute, out Uri? goUri))
                    {
                        await ApplyUserAgentAsync(goUri.Host);
                    }

                    Url = go;
                    core.Navigate(go);
                }
                catch (Exception ex)
                {
                    Diagnostics.Swallowed(ex, "tab: re-issue cleaned navigation");
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            // Let the navigation through untouched rather than lose it.
            Diagnostics.Swallowed(ex, "tab: privacy step of navigation");
            return false;
        }
    }

    /// <summary>Back, Forward or Reload: a navigation that must not be re-issued as a new one.</summary>
    private static bool KeepsHistoryEntry(CoreWebView2NavigationStartingEventArgs args)
    {
        try
        {
            return args.NavigationKind is CoreWebView2NavigationKind.BackOrForward or CoreWebView2NavigationKind.Reload;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: NavigationKind (older runtime?)");
            return true; // unknown: leave it alone
        }
    }

    /// <summary>A form submission, whose body a re-issued navigation would lose.</summary>
    private static bool HasRequestBody(CoreWebView2NavigationStartingEventArgs args)
    {
        try
        {
            return args.RequestHeaders.Contains("Content-Type");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: read navigation headers");
            return true; // unknown: leave it alone
        }
    }

    /// <summary>No initiating site, or a different registrable domain.</summary>
    private static bool CrossesSites(string from, Uri target)
    {
        if (string.IsNullOrEmpty(from)
            || !Uri.TryCreate(from, UriKind.Absolute, out Uri? source)
            || source.Scheme is not ("http" or "https"))
        {
            return true;
        }

        return !string.Equals(PublicSuffix.RegistrableDomain(source.Host),
            PublicSuffix.RegistrableDomain(target.Host), StringComparison.OrdinalIgnoreCase);
    }

    private bool NeedsHttpsUpgrade(Uri target) =>
        AppSettings.GetBool("httpsOnly")
        && !IsLocalAddress(target.Host)
        && !SiteSettings.GetBool(target.Host, "httpAllowed")
        && !(IsPrivate && PrivateHttpAllowed.Contains(PublicSuffix.RegistrableDomain(target.Host)));

    /// <summary>
    /// "Continue to the site" from a PRIVATE window: remembered for as long as Aviary runs and
    /// never written to sites.json, which would be a list of sites visited privately.
    /// </summary>
    private static readonly HashSet<string> PrivateHttpAllowed = new(StringComparer.OrdinalIgnoreCase);

    internal static void ForgetPrivatePrivacyChoices() => PrivateHttpAllowed.Clear();

    /// <summary>
    /// Addresses that almost never have a certificate a browser would accept: this computer,
    /// the local network, and single-word intranet names. HTTPS-Only leaves them alone, as
    /// Chrome's and Firefox's do.
    /// </summary>
    internal static bool IsLocalAddress(string host)
    {
        host = host.Trim('[', ']').ToLowerInvariant();

        if (host.Length == 0 || host == "localhost" || host.EndsWith(".localhost", StringComparison.Ordinal)
            || !host.Contains('.') && !host.Contains(':')
            || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal)
            || host.EndsWith(".home.arpa", StringComparison.Ordinal) || host.EndsWith(".internal", StringComparison.Ordinal))
        {
            return true;
        }

        if (System.Net.IPAddress.TryParse(host, out System.Net.IPAddress? ip))
        {
            if (System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal
                || ip.IsIPv6UniqueLocal || ip.Equals(System.Net.IPAddress.Any))
            {
                return true;
            }

            byte[] b = ip.GetAddressBytes();
            return b.Length == 4 && (b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || b[0] == 127 || b[0] == 0);
        }

        return false;
    }

    private void StartUpgradeDeadline()
    {
        _upgradeTimer?.Stop();
        _upgradeTimer = View.DispatcherQueue.CreateTimer();
        _upgradeTimer.Interval = UpgradeDeadline;
        _upgradeTimer.IsRepeating = false;
        _upgradeTimer.Tick += (_, _) =>
        {
            if (_upgrade is { NavId: > 0 } && IsLoading)
            {
                // The completion that follows is "cancelled"; this flag turns it into the
                // fallback instead of silence.
                _upgradeTimedOut = true;
                try { View.CoreWebView2?.Stop(); }
                catch (Exception ex) { Diagnostics.Swallowed(ex, "tab: stop slow https attempt"); }
            }
        };
        _upgradeTimer.Start();
    }

    /// <summary>
    /// The secure attempt is over. Returns true when it failed in a way that means "this site
    /// does not do HTTPS", which the fallback panel then handles instead of the error panel.
    /// </summary>
    private bool FinishUpgrade(CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_upgrade is not { } attempt || attempt.NavId == 0 || args.NavigationId != attempt.NavId)
        {
            return false;
        }

        _upgradeTimer?.Stop();
        _upgrade = null;

        if (args.IsSuccess || _certificateFailureStatus is not null)
        {
            return false;
        }

        CoreWebView2WebErrorStatus status = args.WebErrorStatus;
        bool timedOut = _upgradeTimedOut && status == CoreWebView2WebErrorStatus.OperationCanceled;
        _upgradeTimedOut = false;

        bool noHttps = timedOut || status is
            CoreWebView2WebErrorStatus.CannotConnect or
            CoreWebView2WebErrorStatus.ConnectionAborted or
            CoreWebView2WebErrorStatus.ConnectionReset or
            CoreWebView2WebErrorStatus.Timeout or
            CoreWebView2WebErrorStatus.ServerUnreachable or
            CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse or
            CoreWebView2WebErrorStatus.Unknown;

        // A server that answered over https with an error page of its own DID answer securely.
        if (!noHttps || ServerAnswered(args))
        {
            return false;
        }

        RaiseHttpsFallback(attempt.Http, timedOut ? "no answer within 8 seconds" : status.ToString());
        return true;
    }

    private void RaiseHttpsFallback(string httpUrl, string why)
    {
        if (Records)
        {
            Diagnostics.Browse("https-fallback", httpUrl, new { tab = Id, url = httpUrl, why });
        }

        // The address is the one the person asked for, and nothing on screen is secure.
        Url = httpUrl;
        ShowingFailure();
        Changed?.Invoke(this);
        HttpsFallback?.Invoke(this, httpUrl);
    }

    /// <summary>
    /// "Continue to the site": remembers the choice for this site (it is a per-site exception,
    /// kept until browsing data is cleared) and loads the insecure address.
    /// </summary>
    public void ContinueInsecure(string httpUrl)
    {
        if (Uri.TryCreate(httpUrl, UriKind.Absolute, out Uri? uri))
        {
            if (IsPrivate)
            {
                PrivateHttpAllowed.Add(PublicSuffix.RegistrableDomain(uri.Host));
            }
            else
            {
                SiteSettings.Set(uri.Host, "httpAllowed", "true");
            }

            Navigate(httpUrl);
        }
    }

    // ══ THE PRIVACY STEP OF A REQUEST ══════════════════════════════════════

    /// <summary>
    /// What happens to a request that is allowed out, before it goes:
    ///
    /// - With the Safari identity on, the Sec-CH-UA client hints are taken off. WebView2's
    ///   user-agent override keeps sending them (unlike plain Chromium's), so every request
    ///   said "Safari on a Mac" in User-Agent and "Microsoft Edge WebView2 on Windows" two
    ///   lines below — found by M2's echo test, 12 Sep. Safari sends no client hints at all.
    ///
    /// - Tracking parameters are taken off a document request's own address. The navigation
    ///   step (MaybeRewriteNavigation) cleans the address bar, but for a redirect or a link
    ///   opening a new tab the engine has already sent the request by the time it reports the
    ///   navigation: the test site received ?fbclid=… and then the clean address 4 ms later.
    ///   Rewriting the request itself means the server never receives them at all.
    /// </summary>
    private void CleanOutgoingRequest(CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            CoreWebView2WebResourceRequest request = args.Request;

            if (_hideUaDataScriptId is not null)
            {
                RemoveClientHints(request.Headers);
            }

            if (args.ResourceContext == CoreWebView2WebResourceContext.Document
                && AppSettings.GetBool("stripTracking")
                && string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
            {
                string uri = request.Uri;
                string from = request.Headers.Contains("Referer") ? request.Headers.GetHeader("Referer") : string.Empty;

                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri? target)
                    && CrossesSites(from, target)
                    && LinkCleaner.TryClean(uri, out string cleaned, out _)
                    && !SiteInsists(cleaned))
                {
                    request.Uri = cleaned;
                }
            }
        }
        catch (Exception ex)
        {
            // Let the request through as it was rather than lose it.
            Diagnostics.Swallowed(ex, "tab: privacy step of request");
        }
    }

    private static void RemoveClientHints(CoreWebView2HttpRequestHeaders headers)
    {
        List<string>? hints = null;
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (header.Key.StartsWith("sec-ch-ua", StringComparison.OrdinalIgnoreCase))
            {
                (hints ??= new List<string>()).Add(header.Key);
            }
        }

        if (hints is null)
        {
            return;
        }

        foreach (string name in hints)
        {
            headers.RemoveHeader(name);
        }
    }

    /// <summary>
    /// How often each clean address was produced in the last few seconds. A site that redirects
    /// straight back to add its parameters again is given them after the second try, rather
    /// than bounced until the engine gives up with "too many redirects".
    /// </summary>
    private readonly Dictionary<string, (int Count, long At)> _recentlyCleaned = new(StringComparer.Ordinal);

    private bool SiteInsists(string cleaned)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_recentlyCleaned.Count > 64)
        {
            _recentlyCleaned.Clear();
        }

        if (_recentlyCleaned.TryGetValue(cleaned, out (int Count, long At) seen)
            && System.Diagnostics.Stopwatch.GetElapsedTime(seen.At).TotalSeconds < 10)
        {
            _recentlyCleaned[cleaned] = (seen.Count + 1, now);
            return seen.Count >= 2;
        }

        _recentlyCleaned[cleaned] = (1, now);
        return false;
    }

    // ══ REFERRER TRIMMING ══════════════════════════════════════════════════

    /// <summary>
    /// A request going to a DIFFERENT site carries only the origin of the page it came from —
    /// "https://news.example/", never the article's full address — whatever the page's own
    /// referrer policy asks for. Chromium's default already does this unless a page opts
    /// out; this is the part a page cannot opt out of.
    /// </summary>
    private static void TrimReferrer(CoreWebView2WebResourceRequest request, string uri)
    {
        if (!AppSettings.GetBool("trimReferrer"))
        {
            return;
        }

        CoreWebView2HttpRequestHeaders headers = request.Headers;
        if (!headers.Contains("Referer"))
        {
            return;
        }

        string referer = headers.GetHeader("Referer");
        if (!Uri.TryCreate(referer, UriKind.Absolute, out Uri? from)
            || !Uri.TryCreate(uri, UriKind.Absolute, out Uri? to))
        {
            return;
        }

        // Cross-ORIGIN, the line Chromium's own default policy draws, so the header and what
        // scripts read (ReferrerScript) always agree.
        string origin = from.GetLeftPart(UriPartial.Authority) + "/";
        if (string.Equals(origin, to.GetLeftPart(UriPartial.Authority) + "/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!string.Equals(referer, origin, StringComparison.Ordinal))
        {
            headers.SetHeader("Referer", origin);
        }
    }

    private string? _referrerScriptId;

    /// <summary>
    /// The other half of referrer trimming. The Referer HEADER is rewritten on the way out, but
    /// a page's scripts read document.referrer, which the engine fills from the navigation
    /// itself: M2's test sent the origin in the header and still showed the full
    /// "…/ref-top?secret-path-part=1" to the destination page's JavaScript. This gives
    /// document.referrer the same rule, in every frame, before any page script runs.
    /// </summary>
    private const string ReferrerScript = """
(function(){
  try {
    var d = Object.getOwnPropertyDescriptor(Document.prototype, 'referrer');
    if (!d || !d.get) { return; }
    var real = d.get;
    Object.defineProperty(Document.prototype, 'referrer', {
      configurable: true, enumerable: d.enumerable,
      get: function(){
        var r = real.call(this);
        if (!r) { return r; }
        try {
          var from = new URL(r);
          if (from.origin !== this.location.origin) { return from.origin + '/'; }
        } catch (e) {}
        return r;
      }
    });
  } catch (e) {}
})();
""";

    /// <summary>Adds or removes the document.referrer rule to follow the switch.</summary>
    public async Task ApplyReferrerScriptAsync()
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            bool on = AppSettings.GetBool("trimReferrer");
            if (on && _referrerScriptId is null)
            {
                _referrerScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(ReferrerScript);
            }
            else if (!on && _referrerScriptId is not null)
            {
                core.RemoveScriptToExecuteOnDocumentCreated(_referrerScriptId);
                _referrerScriptId = null;
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: apply referrer rule");
        }
    }
}
