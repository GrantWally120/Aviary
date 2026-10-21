using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// One tab's side of the ad blocker (Phase 5 M3): the page its requests are judged against,
/// the verdict for each request, the element-hiding script, what was blocked on this page
/// (the shield popover's count), and Strict's warning for a site the lists block outright.
/// </summary>
internal sealed partial class BrowserTab
{
    /// <summary>The page this tab's requests are judged against — its host, site, and whether the lists allow it.</summary>
    private FilterEngine.Page _filterPage = FilterEngine.Page.Empty;

    private FilterEngine? _filterPageEngine;
    private string _filterPageUrl = string.Empty;

    /// <summary>Shields are down for the page on screen. Kept in step, not looked up per request.</summary>
    private bool _filterPageDown;

    /// <summary>This page's site is filtered strictly - its own choice, or the default (M4.5).</summary>
    private bool _filterPageStrict;

    /// <summary>
    /// The top-level document being navigated to, as a <see cref="DocumentKey"/>. A request of
    /// kind Document with this address is the page itself; any other is a frame. WebView2 reports
    /// both as "Document" and has no other way to tell them apart.
    /// </summary>
    private string _mainDocument = string.Empty;

    private string? _cosmeticScript;
    private readonly DocumentScriptSlot _cosmeticScripts = new();
    private Task _cosmeticPending = Task.CompletedTask;

    /// <summary>The accessibility stylesheet for websites (M4.5): its script, and its id.</summary>
    private string? _aidsScript;
    private readonly DocumentScriptSlot _aidsScripts = new();
    private long _aidsGeneration;

    /// <summary>A host the person chose to open anyway after Strict's warning — this tab, until it leaves that host.</summary>
    private string? _openedAnyway;

    private readonly Dictionary<string, int> _blockedHosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Requests blocked on the page now showing. Reset when a new page starts loading.</summary>
    public int BlockedOnPage { get; private set; }

    /// <summary>What was blocked on this page, by host, most first.</summary>
    public IReadOnlyList<KeyValuePair<string, int>> BlockedHosts
    {
        get
        {
            List<KeyValuePair<string, int>> list = new(_blockedHosts);
            list.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.CompareOrdinal(a.Key, b.Key));
            return list;
        }
    }

    /// <summary>Raised on the UI thread when this page's blocked count changes.</summary>
    public event Action<BrowserTab>? BlockedChanged;

    /// <summary>Raised when Strict stops a site as a whole: (tab, address, rule, list).</summary>
    public event Action<BrowserTab, string, string, string>? SiteBlocked;

    /// <summary>The exact host of the page on screen (no "www." trimming — Shields keys are sites anyway).</summary>
    public string PageHost => _filterPage.Host;

    /// <summary>True when Shields can apply to this page at all: a website, not one of Aviary's own.</summary>
    public bool ShieldsApply => _internalTitle is null && _filterPage.Host.Length > 0 && !Shields.IsOwnHost(_filterPage.Host);

    /// <summary>Shields are down for this page's site (in this window).</summary>
    public bool ShieldsDown => _filterPageDown;

    /// <summary>How much Shields are doing on this page's site: Off, Standard or Strict (M4.5).</summary>
    public Shields.Mode ShieldsMode => _filterPage.Host.Length == 0 ? Shields.Default : Shields.ModeFor(_filterPage.Host, IsPrivate);

    /// <summary>True when this page's site has a Standard/Strict/Off choice of its own.</summary>
    public bool ShieldsChosen => _filterPage.Host.Length > 0
        && (_filterPageDown || SiteSettings.Get(_filterPage.Host, Shields.ModeKey).Length > 0
            || Shields.ModeFor(_filterPage.Host, IsPrivate) != Shields.Default);

    /// <summary>The last few things stopped on this page, newest first. Memory only (M4.5).</summary>
    public IReadOnlyList<(string Url, string Rule, string List)> RecentBlocked => _recentOrder;

    /// <summary>
    /// M7 "Super Quik", a slightly debloated launch: a tab that opens on the Start Page - Aviary's own page, which
    /// Shields leaves alone - registers its page scripts (the element-hiding one is ~786 KB, parsed in EVERY
    /// document) just after that page has appeared, not before it (<see cref="RegisterDeferredShieldsScripts"/>).
    /// </summary>
    public bool DeferShieldsScripts { get; set; }

    private bool _shieldsScriptsDeferred;

    private void AttachShields()
    {
        FilterLists.EngineChanged += OnFilterEngineChanged;
        Shields.Changed += OnShieldsChanged;
        if (DeferShieldsScripts)
        {
            _shieldsScriptsDeferred = true;
            return;
        }

        _ = RefreshCosmeticAsync();

        // The accessibility stylesheet rides the same mechanism (M4.5), and is nothing at all
        // when its switches are off.
        if (!ReadingAids.Nothing)
        {
            _ = RefreshAidsAsync();
        }
    }

    /// <summary>
    /// The first page has appeared - or a website is about to load, whichever comes first: the deferred scripts go
    /// now. A person cannot type an address and reach a website in the few milliseconds this takes; a website the
    /// tab starts to load before its first page has appeared registers them at its NavigationStarting.
    /// </summary>
    private void RegisterDeferredShieldsScripts()
    {
        if (!_shieldsScriptsDeferred)
        {
            return;
        }

        _shieldsScriptsDeferred = false;
        _ = RefreshCosmeticAsync();
        if (!ReadingAids.Nothing)
        {
            _ = RefreshAidsAsync();
        }
    }

    private void DetachShields()
    {
        // Static events hold their listeners: a closed tab must let go of both.
        FilterLists.EngineChanged -= OnFilterEngineChanged;
        Shields.Changed -= OnShieldsChanged;
        BlockedChanged = null;
        SiteBlocked = null;
    }

    /// <summary>A new engine (the first load, or an update) — raised off the UI thread.</summary>
    private void OnFilterEngineChanged()
    {
        View.DispatcherQueue?.TryEnqueue(() =>
        {
            _filterPageUrl = string.Empty;
            UpdateFilterPage(Url);
            _ = RefreshCosmeticAsync();
        });
    }

    private void OnShieldsChanged(string site)
    {
        _filterPageDown = _filterPage.Host.Length > 0 && Shields.IsDown(_filterPage.Host, IsPrivate);
        _filterPageStrict = _filterPage.Host.Length > 0 && Shields.StrictFor(_filterPage.Host, IsPrivate);
        _ = RefreshCosmeticAsync();
    }

    /// <summary>Re-derives the page context when the address on screen changes.</summary>
    private void UpdateFilterPage(string url)
    {
        FilterEngine? engine = FilterLists.Engine;
        if (string.Equals(url, _filterPageUrl, StringComparison.Ordinal) && ReferenceEquals(engine, _filterPageEngine))
        {
            return;
        }

        _filterPageUrl = url;
        _filterPageEngine = engine;

        if (!IsWebAddress(url))
        {
            _filterPage = FilterEngine.Page.Empty;
            _filterPageDown = false;
            _filterPageStrict = false;
            return;
        }

        try
        {
            _filterPage = engine is not null
                ? engine.PageFor(url)
                : new FilterEngine.Page(Uri.TryCreate(url, UriKind.Absolute, out Uri? u) ? u.Host : string.Empty);
        }
        catch (Exception ex)
        {
            _filterPage = FilterEngine.Page.Empty;
            Diagnostics.Swallowed(ex, "tab: filter page context");
        }

        _filterPageDown = _filterPage.Host.Length > 0 && Shields.IsDown(_filterPage.Host, IsPrivate);
        _filterPageStrict = _filterPage.Host.Length > 0 && Shields.StrictFor(_filterPage.Host, IsPrivate);
    }

    private static bool IsWebAddress(string url) =>
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The address with its fragment and its http(s) scheme removed — what identifies the page's
    /// own document request. The scheme has to go because Chromium upgrades a typed http://
    /// address to https:// INSIDE the navigation (its "HTTPS Upgrades"), so the document request
    /// for http://site/ arrives as https://site/. Compared with the scheme, the page's own request
    /// looked like a frame's and was judged as one — a site on a list then got a blank 403 page
    /// in Standard, which never stops a page (found in M3's first real-app run, 13 Sep).
    /// </summary>
    private static string DocumentKey(string url)
    {
        int hash = url.IndexOf('#');
        string address = hash < 0 ? url : url.Substring(0, hash);
        int scheme = address.IndexOf("://", StringComparison.Ordinal);
        return scheme is > 0 and <= 5 ? address.Substring(scheme + 3) : address;
    }

    /// <summary>
    /// Registers this tab's element-hiding script, replacing the one before. Returns the swap in
    /// flight, so the Shields switch can wait for the new script before it reloads the page.
    /// </summary>
    public Task RefreshCosmeticAsync()
    {
        if (_disposed) { return Task.CompletedTask; }
        string? script = Shields.CosmeticScript(IsPrivate);
        if (ReferenceEquals(script, _cosmeticScript))
        {
            return _cosmeticPending;
        }

        _cosmeticScript = script;
        _cosmeticPending = SwapCosmeticAsync(script);
        return _cosmeticPending;
    }

    /// <summary>
    /// Registers this tab's accessibility stylesheet for websites (underlined links), replacing
    /// the one before, and applies it to the page already on screen so nothing has to be
    /// reloaded. Aviary's own pages are left alone: they have their own text size and focus ring.
    /// </summary>
    public async Task RefreshAidsAsync()
    {
        try
        {
            if (_disposed || View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            string? script = ReadingAids.Nothing && !_aidsScripts.Requested ? null : ReadingAids.Script();
            if (string.Equals(script, _aidsScript, StringComparison.Ordinal))
            {
                return;
            }

            _aidsScript = script;
            long generation = ++_aidsGeneration;
            await _aidsScripts.SetAsync(ReadingAids.Nothing ? null : script,
                async text => await core.AddScriptToExecuteOnDocumentCreatedAsync(text), core.RemoveScriptToExecuteOnDocumentCreated);

            if (script is null)
            {
                return;
            }

            // Apply only the latest choice to the current page. The slot owns its future
            // registrations even if an on/off/on sequence completes out of order.
            if (generation == _aidsGeneration && !_disposed && !IsInternalPage)
            {
                await core.ExecuteScriptAsync(script);
            }

            if (ReadingAids.Nothing)
            {
                // Nothing left to apply: no script is registered, and the id stays null so the
                // next switch-on registers one again.
                _aidsScript = null;
                return;
            }

        }
        catch (Exception ex)
        {
            if (!_aidsScripts.Requested) { _aidsScript = null; }
            Diagnostics.Swallowed(ex, "tab: accessibility stylesheet");
        }
    }

    private async Task SwapCosmeticAsync(string? script)
    {
        try
        {
            if (_disposed || View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            await _cosmeticScripts.SetAsync(script,
                async text => await core.AddScriptToExecuteOnDocumentCreatedAsync(text), core.RemoveScriptToExecuteOnDocumentCreated);
        }
        catch (Exception ex)
        {
            if (!_cosmeticScripts.Requested) { _cosmeticScript = null; }
            Diagnostics.Swallowed(ex, "tab: element-hiding script");
        }
    }

    /// <summary>
    /// The top of NavigationStarting for a new page: its context, a fresh count — and, in Strict,
    /// the warning instead of a site the lists block as a whole. True when the navigation was
    /// stopped.
    /// </summary>
    private bool ShieldsOnNavigationStarting(CoreWebView2NavigationStartingEventArgs args)
    {
        try
        {
            string uri = args.Uri;
            string host = IsWebAddress(uri) && Uri.TryCreate(uri, UriKind.Absolute, out Uri? target)
                ? target.Host.ToLowerInvariant()
                : string.Empty;

            // Strict's check comes first, before this tab's state moves to a page that may
            // never load: a stopped navigation leaves the page on screen exactly as it was.
            if (Shields.Enabled && Shields.WarnsAboutSites && host.Length > 0 && !Shields.IsOwnHost(host)
                && Shields.StrictFor(host, IsPrivate)
                && !string.Equals(host, _openedAnyway, StringComparison.Ordinal)
                && !Shields.IsDown(host, IsPrivate)
                && FilterLists.Engine is { } engine
                && engine.MatchPage(uri) is { Blocked: true } verdict)
            {
                args.Cancel = true;
                if (Records)
                {
                    Diagnostics.Blocked(Id, uri, verdict.Rule ?? "?", "Page", Url, verdict.List);
                }

                SiteBlocked?.Invoke(this, uri, verdict.Rule ?? string.Empty, verdict.List ?? string.Empty);
                return true;
            }

            if (_openedAnyway is not null && !string.Equals(host, _openedAnyway, StringComparison.Ordinal))
            {
                _openedAnyway = null;
            }

            _mainDocument = DocumentKey(uri);
            if (!args.IsRedirected)
            {
                BlockedOnPage = 0;
                _blockedHosts.Clear();
                _recentBlocked.Clear();
                _recentOrder.Clear();
                BlockedChanged?.Invoke(this);
            }

            UpdateFilterPage(uri);
            return false;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: shields at navigation");
            return false;
        }
    }

    /// <summary>Strict's warning, answered "Open Anyway": this tab, this site, until it leaves it.</summary>
    public void OpenBlockedSite(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            return;
        }

        _openedAnyway = uri.Host.ToLowerInvariant();
        Diagnostics.Shields("page-opened-anyway", IsPrivate ? null : new { site = Shields.SiteOf(_openedAnyway) });
        View.CoreWebView2?.Navigate(url);
    }

    /// <summary>
    /// The blocker's verdict for one request: the rule that stops it and the list it came from,
    /// or null to let it through. The page itself is never stopped here (Strict's whole-site
    /// check happens at NavigationStarting, where it can show a warning instead of a blank page).
    /// </summary>
    private (string Rule, string List)? ShieldsVerdict(string uri, CoreWebView2WebResourceContext context, bool linkPing)
    {
        if (!Shields.Enabled || _internalTitle is not null || _filterPageDown || _filterPage.Host.Length == 0
            || Shields.IsOwnHost(_filterPage.Host))
        {
            return null;
        }

        ReqType type = TypeOf(context, uri);
        if (type == ReqType.Document)
        {
            return null;
        }

        // M6 stage 4, "Stop link-click pings": every <a ping>, the site's OWN included - EasyPrivacy's
        // *$ping,third-party already stops other sites' (measured), and a site's own is exactly what
        // Standard below would let through. Nothing but the ping: the link still opens.
        if (type == ReqType.Ping && (AppSettings.GetBool("exp.noPings")
            || (linkPing && AppSettings.GetBool("blockLinkPings"))))
        {
            return ("*$ping", AppSettings.GetBool("exp.noPings") ? "Experimental" : "Link tracking protection");
        }

        // A site's own requests are never stopped in Standard — by any list, Aviary's included, or
        // a tracker company's own website would load as a broken page — and never on a page the
        // person chose to open anyway after Strict's warning.
        bool strict = _filterPageStrict;
        bool openedAnyway = _openedAnyway is not null && string.Equals(_filterPage.Host, _openedAnyway, StringComparison.Ordinal);
        if ((!strict || openedAnyway) && FilterEngine.IsSameSite(uri, _filterPage))
        {
            return null;
        }

        // M6 stage 4, "Block scripts from other websites" - the M3 Shields row that was only promised.
        // Any script not from the page's own site (a subdomain of it is its own), in Standard and Strict
        // alike; never a page the person opened anyway.
        if (type == ReqType.Script && !openedAnyway && AppSettings.GetBool("exp.thirdPartyScripts")
            && !FilterEngine.IsSameSite(uri, _filterPage))
        {
            return ("*$script,third-party", "Experimental");
        }

        string? own = TrackerBlocker.Match(uri);
        if (FilterLists.Engine is not { } engine)
        {
            return own is null ? null : ("||" + own + "^", "Aviary");
        }

        // A targeted allow rule also applies to Aviary's built-in list. Keep the rest of
        // Shields active instead of forcing someone to disable all protection for a login.
        FilterEngine.Verdict verdict = engine.Match(uri, type, _filterPage, thirdPartyOnly: !strict, fallbackHost: own);
        return verdict.Blocked ? (verdict.Rule ?? "?", verdict.List ?? "?") : null;
    }

    private ReqType TypeOf(CoreWebView2WebResourceContext context, string uri) => context switch
    {
        CoreWebView2WebResourceContext.Document =>
            string.Equals(DocumentKey(uri), _mainDocument, StringComparison.Ordinal) ? ReqType.Document : ReqType.Subdocument,
        CoreWebView2WebResourceContext.Stylesheet => ReqType.Stylesheet,
        CoreWebView2WebResourceContext.Image => ReqType.Image,
        CoreWebView2WebResourceContext.Media => ReqType.Media,
        CoreWebView2WebResourceContext.Font => ReqType.Font,
        CoreWebView2WebResourceContext.Script => ReqType.Script,
        CoreWebView2WebResourceContext.XmlHttpRequest => ReqType.Xhr,
        CoreWebView2WebResourceContext.Fetch => ReqType.Xhr,
        CoreWebView2WebResourceContext.Websocket => ReqType.WebSocket,
        CoreWebView2WebResourceContext.Ping => ReqType.Ping,
        _ => ReqType.Other,
    };

    /// <summary>
    /// This page's blocked addresses and the rule for each, so diagnostics can label a page's
    /// "failed file" as one Aviary refused. Bounded: a page that keeps retrying must not grow it.
    /// </summary>
    private readonly Dictionary<string, string> _recentBlocked = new(StringComparer.Ordinal);

    /// <summary>
    /// The same blocks in the order they happened, newest first, for the popover's "Recently
    /// blocked on this page" (M4.5). Ten is all it shows; nothing here is written down, and a
    /// private tab keeps its own copy that goes when the tab does.
    /// </summary>
    private readonly List<(string Url, string Rule, string List)> _recentOrder = new();

    private void CountBlockedOnPage(string uri, string rule, string list)
    {
        BlockedOnPage++;
        string host = Uri.TryCreate(uri, UriKind.Absolute, out Uri? u) ? u.Host : "?";
        _blockedHosts[host] = _blockedHosts.TryGetValue(host, out int n) ? n + 1 : 1;

        _recentOrder.Insert(0, (uri, rule, list));
        if (_recentOrder.Count > 10)
        {
            _recentOrder.RemoveRange(10, _recentOrder.Count - 10);
        }

        if (_recentBlocked.Count >= 500)
        {
            _recentBlocked.Clear();
        }

        _recentBlocked[uri] = rule;
        BlockedChanged?.Invoke(this);
    }

    /// <summary>The rule that blocked this address on this page, if Aviary blocked it.</summary>
    private string? BlockedRuleFor(string url) =>
        _recentBlocked.TryGetValue(url, out string? rule) ? rule : TrackerBlocker.Match(url);

    private readonly List<string> _collapse = new();
    private bool _collapseQueued;

    /// <summary>
    /// A frame or picture Aviary blocked leaves an empty box where the ad was — a bordered blank
    /// for a frame (seen in M3's first run). Hidden in the page itself, in batches: one script for
    /// however many blocks a burst of loading produced. The page's own frames are reached; a frame
    /// nested inside another site's frame is not (hiding rules usually cover its container).
    /// </summary>
    private void QueueCollapse(string uri, CoreWebView2WebResourceContext context)
    {
        if (context is not (CoreWebView2WebResourceContext.Document or CoreWebView2WebResourceContext.Image))
        {
            return;
        }

        if (_collapse.Count < 200)
        {
            _collapse.Add(uri);
        }

        if (_collapseQueued)
        {
            return;
        }

        _collapseQueued = true;
        View.DispatcherQueue?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, FlushCollapse);
    }

    private void FlushCollapse()
    {
        _collapseQueued = false;
        try
        {
            if (_collapse.Count == 0 || View.CoreWebView2 is not CoreWebView2 core)
            {
                _collapse.Clear();
                return;
            }

            // JSON with the default encoder: every <, > and quote escaped, so an address can only
            // ever be data inside this script.
            string urls = System.Text.Json.JsonSerializer.Serialize(_collapse);
            _collapse.Clear();
            _ = core.ExecuteScriptAsync(CollapseScript.Replace("__URLS__", urls, StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab: collapse blocked frames");
        }
    }

    private const string CollapseScript =
        "(function(u){var s=new Set(u);document.querySelectorAll('iframe,frame,img').forEach(function(e){" +
        "var src=e.currentSrc||e.src;if(src&&s.has(src)){e.style.setProperty('display','none','important');}});})(__URLS__);";
}
