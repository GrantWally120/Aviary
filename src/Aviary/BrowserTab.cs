using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Aviary;

/// <summary>
/// One tab: one WebView2, its own history, its own title, its own event subscriptions.
///
/// The shape is lifted from Microsoft's WebView2Browser sample, which is the one part of
/// that sample genuinely worth copying — an object per tab owning exactly one WebView plus
/// its registrations, held in a keyed collection with a separate "active" pointer.
/// </summary>
internal sealed class BrowserTab
{
    /// <summary>
    /// Safari 18 on macOS. Per-tab on purpose: CoreWebView2Settings.UserAgent is a
    /// per-WebView property, so a later phase can make this a switch on individual tabs
    /// rather than a silent global.
    /// </summary>
    private const string SafariUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/18.0 Safari/605.1.15";

    public Microsoft.UI.Xaml.Controls.WebView2 View { get; }

    public string Title { get; private set; } = "New Tab";
    public string Url { get; private set; } = string.Empty;
    public bool IsSecure { get; private set; }
    public bool IsReady { get; private set; }

    /// <summary>
    /// The fallback letter, shown only when the site has no icon of its own. The colour
    /// behind it is no longer chosen here — the hues live in the theme dictionaries and
    /// FaviconView derives a stable index from the domain, so a site keeps its colour
    /// between launches instead of being assigned one in tab-open order.
    /// </summary>
    public string Monogram { get; private set; } = "•";

    /// <summary>This tab's favicon cache key, kept in step with <see cref="Url"/>.</summary>
    public string IconKey { get; private set; } = string.Empty;

    /// <summary>True for Aviary's own pages, which show the dome mark rather than a letter.</summary>
    public bool IsInternalPage => _internalTitle is not null;

    /// <summary>
    /// True between NavigationStarting and NavigationCompleted. Safari's reload glyph
    /// becomes a stop glyph while this is true — a browser that cannot be told to stop is
    /// one you have to wait for.
    /// </summary>
    public bool IsLoading { get; private set; }

    /// <summary>Raised whenever anything the chrome displays has changed.</summary>
    public event Action<BrowserTab>? Changed;

    /// <summary>Raised when the page asks to close itself (window.close()).</summary>
    public event Action<BrowserTab>? CloseRequested;

    /// <summary>
    /// Raised for a browser shortcut pressed while the PAGE had keyboard focus.
    ///
    /// XAML KeyboardAccelerators never see those: WebView2 hosts a child window that takes
    /// keyboard input before XAML's pipeline runs, and disabling Chromium's own
    /// accelerators stops it acting on them without handing them back. So the keys get
    /// caught inside the page and posted up.
    /// </summary>
    public event Action<string>? ShortcutPressed;

    /// <summary>Raised when a page starts downloading a file.</summary>
    public event Action<CoreWebView2DownloadOperation>? DownloadStarted;

    /// <summary>Raised when a navigation fails, so the chrome can show its own message.</summary>
    public event Action<BrowserTab, CoreWebView2WebErrorStatus>? NavigationFailed;

    /// <summary>Raised after a navigation SUCCEEDS, once the document is there to inspect.</summary>
    public event Action<BrowserTab>? Navigated;

    /// <summary>Set for Aviary's own pages, whose titles we own rather than read.</summary>
    private string? _internalTitle;

    /// <summary>Kept so blocked requests can be answered with a synthetic response.</summary>
    private CoreWebView2Environment? _environment;

    /// <summary>
    /// A navigation that turns into a download reports itself as ABORTED, because from the
    /// engine's point of view the page load really was cancelled. Without this flag every
    /// successful download would be followed by an error page saying the connection was
    /// cut short.
    /// </summary>
    private bool _navigationBecameDownload;

    public BrowserTab()
    {
        View = new Microsoft.UI.Xaml.Controls.WebView2
        {
            Visibility = Visibility.Collapsed
        };
    }

    public async Task InitialiseAsync()
    {
        if (IsReady)
        {
            return;
        }

        CoreWebView2Environment environment = await WebViewEnvironment.GetAsync();
        _environment = environment;
        await View.EnsureCoreWebView2Async(environment);

        if (View.CoreWebView2 is not CoreWebView2 core)
        {
            throw new InvalidOperationException("WebView2 initialised without a core.");
        }

        // Before the first navigation. Setting it later only affects subsequent ones.
        core.Settings.UserAgent = SafariUserAgent;
        core.Settings.IsStatusBarEnabled = false;

        core.SourceChanged += OnSourceChanged;
        core.HistoryChanged += OnHistoryChanged;
        core.DocumentTitleChanged += OnDocumentTitleChanged;
        core.FaviconChanged += OnFaviconChanged;
        core.NavigationStarting += OnNavigationStarting;
        core.NavigationCompleted += OnNavigationCompleted;
        core.NewWindowRequested += OnNewWindowRequested;
        core.WindowCloseRequested += OnWindowCloseRequested;
        core.WebMessageReceived += OnWebMessageReceived;

        // Chromium would otherwise act on Ctrl+T itself and swallow it.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        // Injected into EVERY document, including ones we did not write, so Ctrl+T keeps
        // working while the caret is inside a text box on somebody else's website.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ShortcutScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(FindInPage.Script);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(IconHarvestScript);
        await core.AddScriptToExecuteOnDocumentCreatedAsync(Reader.Script);

        core.DownloadStarting += OnDownloadStarting;

        ApplyColorScheme();
        ApplyZoom();

        // Every request passes through us before it leaves the machine.
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;

        IsReady = true;
    }

    /// <summary>
    /// Ad and tracker requests are answered with a 403 of our own rather than forwarded.
    /// Nothing reaches the network, which is the whole point — a blocker that merely hides
    /// elements has already told the tracker you were here.
    /// </summary>
    private void OnWebResourceRequested(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        try
        {
            // Aviary's own pages ask for site icons over a synthetic origin. TryServe
            // answers EVERY request to that host, a bad one included — if it fell through,
            // Chromium would do a real DNS lookup for aviary.localhost.
            if (_environment is not null
                && Favicons.TryServe(args.Request.Uri, _environment, out CoreWebView2WebResourceResponse? icon))
            {
                args.Response = icon;
                return;
            }

            if (!TrackerBlocker.ShouldBlock(args.Request.Uri) || _environment is null)
            {
                return;
            }

            TrackerBlocker.CountBlocked();

            // The session counter resets when you quit, so on its own it can never say
            // anything more interesting than "since you opened the app". The seven-day
            // store is what the Start Page's Privacy Report reads.
            if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out Uri? blocked))
            {
                PrivacyReport.Record(blocked.Host, HostOf(Url));
            }

            args.Response = _environment.CreateWebResourceResponse(null, 403, "Blocked by Aviary", string.Empty);
        }
        catch (Exception)
        {
            // Let the request through rather than take the page down. A missed advert is
            // cheaper than a broken browser.
        }
    }

    /// <summary>
    /// Handled = true suppresses WebView2's own download bar, so ours is the only one the
    /// user sees. The download itself still proceeds to the default folder.
    /// </summary>
    private void OnDownloadStarting(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs args)
    {
        try
        {
            args.Handled = true;
            _navigationBecameDownload = true;
            DownloadStarted?.Invoke(args.DownloadOperation);
        }
        catch (Exception)
        {
            // Let the default UI take over rather than lose the download entirely.
        }
    }

    /// <summary>
    /// Makes WEB PAGES follow Aviary's theme rather than Windows'. Without this, pinning
    /// the browser to Light leaves every site still reporting prefers-color-scheme: dark,
    /// so the chrome goes light and the page underneath stays black.
    /// </summary>
    public void ApplyColorScheme()
    {
        try
        {
            if (View.CoreWebView2?.Profile is not CoreWebView2Profile profile)
            {
                return;
            }

            profile.PreferredColorScheme = AppSettings.Get("theme") switch
            {
                "Light" => CoreWebView2PreferredColorScheme.Light,
                "Dark" => CoreWebView2PreferredColorScheme.Dark,
                _ => CoreWebView2PreferredColorScheme.Auto
            };
        }
        catch (Exception)
        {
            // Older runtimes may not expose the profile; the page just keeps following
            // Windows, which is a cosmetic mismatch rather than a failure.
        }
    }

    /// <summary>
    /// Gentle memory saving: a background tab is told it may release caches and drop its
    /// memory priority. Deliberately NOT TrySuspendAsync — suspension stops timers and
    /// audio, so music in a background tab would die, and the docs are explicit that the
    /// two approaches must not be mixed.
    /// </summary>
    public void SetBackgrounded(bool backgrounded)
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            core.MemoryUsageTargetLevel = backgrounded && AppSettings.GetBool("memorySaver")
                ? CoreWebView2MemoryUsageTargetLevel.Low
                : CoreWebView2MemoryUsageTargetLevel.Normal;
        }
        catch (Exception)
        {
            // Best effort by design — the API can decline.
        }
    }

    /// <summary>
    /// Messages from our own injected scripts, dispatched on their declared type.
    ///
    /// This used to hunt for quote characters around an "action" key with IndexOf. That
    /// worked while there was exactly one message type; adding a second on top of it is
    /// how you get a shortcut firing because an icon message happened to contain the word.
    /// </summary>
    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(args.TryGetWebMessageAsString());
            JsonElement root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out JsonElement typeElement))
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "shortcut":
                    if (root.TryGetProperty("action", out JsonElement action)
                        && action.GetString() is { Length: > 0 } name)
                    {
                        ShortcutPressed?.Invoke(name);
                    }

                    break;

                case "icons":
                {
                    if (!root.TryGetProperty("links", out JsonElement links)
                        || links.ValueKind != JsonValueKind.Array)
                    {
                        return;
                    }

                    string origin = root.TryGetProperty("origin", out JsonElement o)
                        ? o.GetString() ?? string.Empty
                        : string.Empty;

                    List<IconCandidate> candidates = new();
                    foreach (JsonElement link in links.EnumerateArray())
                    {
                        candidates.Add(new IconCandidate
                        {
                            Href = link.TryGetProperty("href", out JsonElement h) ? h.GetString() ?? "" : "",
                            Rel = link.TryGetProperty("rel", out JsonElement r) ? r.GetString() ?? "" : "",
                            Px = link.TryGetProperty("px", out JsonElement p) && p.TryGetInt32(out int px) ? px : 0,
                            Type = link.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? "" : "",
                        });
                    }

                    Favicons.OfferCandidates(IconKey, origin, candidates);
                    break;
                }
            }
        }
        catch (Exception)
        {
            // A malformed message from our own injected script is a bug to find in
            // testing, not a reason to take the browser down.
        }
    }

    /// <summary>
    /// Browser shortcuts, caught inside the page and posted up.
    ///
    /// The combo is built from ALL the modifiers before matching. The previous version
    /// bailed out on Alt and ignored Shift entirely, so Ctrl+Shift+D lowercased to "d" and
    /// fired the plain bookmark action — you would add to the Reading List and get a
    /// favourite, and conclude the Reading List was broken.
    ///
    /// e.code, not e.key, for the digits: on a French or German layout the number row
    /// produces punctuation and Ctrl+1 would never match.
    /// </summary>
    private const string ShortcutScript = """
(function(){
  if (window.__aviaryKeys) { return; }
  window.__aviaryKeys = 1;

  var MAP = {
    'ctrl+t': 'newtab',
    'ctrl+w': 'closetab',
    'ctrl+l': 'address',
    'ctrl+tab': 'nexttab',
    'ctrl+shift+tab': 'prevtab',
    'ctrl+f': 'find',
    'ctrl+d': 'bookmark',
    'ctrl+shift+d': 'readinglist',
    'ctrl+h': 'history',
    'ctrl+shift+l': 'sidebar',
    'ctrl+r': 'reload',
    'ctrl+j': 'downloads',
    'ctrl+shift+r': 'reader',
    'ctrl+=': 'zoomin',
    'ctrl++': 'zoomin',
    'ctrl+-': 'zoomout',
    'ctrl+0': 'zoomreset',
    'ctrl+digit1': 'tab1', 'ctrl+digit2': 'tab2', 'ctrl+digit3': 'tab3',
    'ctrl+digit4': 'tab4', 'ctrl+digit5': 'tab5', 'ctrl+digit6': 'tab6',
    'ctrl+digit7': 'tab7', 'ctrl+digit8': 'tab8', 'ctrl+digit9': 'tablast'
  };

  window.addEventListener('keydown', function(e){
    if (e.metaKey) { return; }

    var key = (e.key || '').toLowerCase();

    // Digits come from e.code so a layout where the number row is punctuation still works.
    if (/^Digit[1-9]$/.test(e.code || '')) { key = (e.code || '').toLowerCase(); }

    var combo = (e.ctrlKey ? 'ctrl+' : '')
              + (e.shiftKey ? 'shift+' : '')
              + (e.altKey ? 'alt+' : '')
              + key;

    var action = MAP[combo];
    if (!action) { return; }

    e.preventDefault();
    e.stopPropagation();
    try { window.chrome.webview.postMessage(JSON.stringify({type:'shortcut', action:action})); } catch(err){}
  }, true);
})();
""";

    /// <summary>
    /// The page zoom, as a CSS zoom on the document element.
    ///
    /// NOT CoreWebView2Controller.ZoomFactor, which is the obvious API and is unreachable
    /// here: WinUI 3's WebView2 element owns its controller privately and never exposes it.
    /// CSS zoom is applied by Chromium at layout, so text reflows properly rather than
    /// being scaled like an image — and it has to be re-applied after every navigation,
    /// because it lives on a document that the next navigation replaces.
    /// </summary>
    public void ApplyZoom()
    {
        try
        {
            if (View.CoreWebView2 is not CoreWebView2 core)
            {
                return;
            }

            string text = AppSettings.Get("zoom").TrimEnd('%');
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double percent)
                || percent < 25 || percent > 500)
            {
                percent = 100;
            }

            string factor = (percent / 100.0).ToString(
                "0.###", System.Globalization.CultureInfo.InvariantCulture);

            _ = core.ExecuteScriptAsync(
                "try{document.documentElement.style.zoom='" + factor + "';}catch(e){}");
        }
        catch (Exception)
        {
            // A zoom that will not apply is cosmetic; the page still renders.
        }
    }

    public bool CanGoBack => IsReady && View.CanGoBack;

    public bool CanGoForward => IsReady && View.CanGoForward;

    public void Navigate(string url)
    {
        if (IsReady && View.CoreWebView2 is not null)
        {
            Url = url;
            _internalTitle = null;
            _navigationBecameDownload = false;
            View.CoreWebView2.Navigate(url);
        }
    }

    public void NavigateToHtml(string html, string displayTitle)
    {
        if (IsReady && View.CoreWebView2 is not null)
        {
            Url = string.Empty;
            Favicons.Unpin(IconKey);
            IconKey = string.Empty;
            Title = displayTitle;
            _internalTitle = displayTitle;
            Monogram = displayTitle.Length > 0 ? displayTitle.Substring(0, 1).ToUpperInvariant() : "•";
            View.CoreWebView2.NavigateToString(html);
            Changed?.Invoke(this);
        }
    }

    private void OnSourceChanged(CoreWebView2 sender, object args)
    {
        Url = sender.Source;
        IsSecure = Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        string previous = IconKey;
        IconKey = Favicons.KeyFor(Url);

        if (!string.Equals(previous, IconKey, StringComparison.OrdinalIgnoreCase))
        {
            // Pinned while open, so the cache never evicts the icon of a site you are
            // actually looking at.
            Favicons.Unpin(previous);
            Favicons.Pin(IconKey);
        }

        Changed?.Invoke(this);
    }

    /// <summary>
    /// Chromium finished decoding a site's icon.
    ///
    /// Fires more than once per page — typically an empty one first, then the real one —
    /// so the read has to be re-checked against the current key after it completes.
    /// </summary>
    private void OnFaviconChanged(CoreWebView2 sender, object args)
    {
        if (!Favicons.EngineApiAvailable || !AppSettings.GetBool("siteIcons"))
        {
            return;
        }

        _ = CaptureFaviconAsync(sender, IconKey);
    }

    private static async Task CaptureFaviconAsync(CoreWebView2 core, string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        try
        {
            // IAsyncOperation<IRandomAccessStream> here, NOT Task<Stream>: WinUI 3 consumes
            // the CsWinRT projection, not the WinForms/WPF managed wrapper. A copied WPF
            // sample gives CS1061 on .ReadAsync.
            Windows.Storage.Streams.IRandomAccessStream stream =
                await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);

            if (stream is null || stream.Size == 0 || stream.Size > 256 * 1024)
            {
                return;
            }

            byte[] bytes = new byte[stream.Size];
            using (Stream managed = stream.AsStreamForRead())
            {
                int read = 0;
                while (read < bytes.Length)
                {
                    int n = await managed.ReadAsync(bytes.AsMemory(read, bytes.Length - read));
                    if (n <= 0) { break; }
                    read += n;
                }

                if (read == 0) { return; }
            }

            // The tab may have navigated while that was in flight — without this check the
            // previous site's icon gets stamped onto the new domain.
            if (!string.Equals(key, Favicons.KeyFor(core.Source), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Favicons.Offer(key, bytes, core.Source, IconRank.Engine);
        }
        catch (Exception)
        {
            // GetFaviconAsync arrived in WebView2 102. Below that it throws; latch it off
            // once rather than throwing on every page load for the rest of the session.
            Favicons.MarkEngineApiUnavailable();
        }
    }

    /// <summary>
    /// Reports the icons a page DECLARED, which the engine's own API does not expose.
    /// apple-touch-icon is the crisp one Safari uses for Start Page tiles, and it is
    /// usually the only way to get anything better than 32px.
    /// </summary>
    private const string IconHarvestScript = """
(function(){
  if (window.__aviaryIcons) { return; }
  window.__aviaryIcons = 1;

  function px(el){
    var s = el.getAttribute('sizes') || '';
    if (/any/i.test(s)) { return 1024; }
    var m = /(\d+)\s*[xX]\s*(\d+)/.exec(s);
    if (m) { return parseInt(m[1], 10) || 0; }
    // No declared size. An apple-touch-icon is 180 by convention; a plain icon is small.
    return /apple-touch/i.test(el.getAttribute('rel') || '') ? 180 : 0;
  }

  function harvest(){
    try {
      var out = [];
      var links = document.querySelectorAll(
        'link[rel~="icon" i], link[rel~="shortcut" i], link[rel*="apple-touch" i], link[rel~="mask-icon" i]');
      for (var i = 0; i < links.length && out.length < 12; i++){
        var el = links[i];
        var href = el.getAttribute('href');
        if (!href) { continue; }
        out.push({ href: href, rel: (el.getAttribute('rel')||'').toLowerCase(),
                   px: px(el), type: (el.getAttribute('type')||'').toLowerCase() });
      }
      if (!out.length) { return; }
      window.chrome.webview.postMessage(JSON.stringify(
        { type:'icons', origin: location.origin, links: out }));
    } catch(e){}
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', harvest, { once:true });
  } else {
    harvest();
  }
})();
""";

    private void OnHistoryChanged(CoreWebView2 sender, object args) => Changed?.Invoke(this);

    private void OnDocumentTitleChanged(CoreWebView2 sender, object args)
    {
        if (_internalTitle is not null)
        {
            // Our own pages keep the name we gave them. The start page has a <title> so the
            // WINDOW reads "Aviary"; that should not relabel the tab itself "Aviary".
            Changed?.Invoke(this);
            return;
        }

        string title = sender.DocumentTitle;
        Title = string.IsNullOrWhiteSpace(title) ? HostOf(Url) : title;

        // First letter of the host, not of the title — a monogram should stay put while a
        // single-page app rewrites its title underneath you.
        Monogram = PublicSuffix.Monogram(HostOf(Url));
        History.UpdateTitle(Url, Title);

        Changed?.Invoke(this);
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        IsLoading = true;
        Changed?.Invoke(this);
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        IsLoading = false;
        ApplyZoom();
        Changed?.Invoke(this);

        // Once per successful navigation. The title is usually still the old one here, so
        // OnDocumentTitleChanged corrects it WITHOUT counting a second visit.
        if (args.IsSuccess && _internalTitle is null)
        {
            History.Record(Url, Title);
            Navigated?.Invoke(this);
        }

        if (_navigationBecameDownload)
        {
            _navigationBecameDownload = false;
            return;
        }

        if (!args.IsSuccess && args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            NavigationFailed?.Invoke(this, args.WebErrorStatus);
        }
    }

    /// <summary>
    /// Asks the host for a fresh, initialised, un-navigated CoreWebView2 to hand a new
    /// window to. Null means the host could not make one.
    /// </summary>
    public Func<Task<CoreWebView2?>>? BlankTabRequested { get; set; }

    /// <summary>
    /// target="_blank", window.open(), Ctrl+click and middle-click.
    ///
    /// This used to call sender.Navigate(args.Uri) — the new page REPLACED the one you
    /// were reading, which is wrong in a way you notice within a minute of real browsing.
    /// Opening a genuine tab means handing the engine a second CoreWebView2, and creating
    /// one is asynchronous, so the event has to be deferred: without the deferral the
    /// handler returns before NewWindow is assigned and the engine falls back to its own
    /// window.
    ///
    /// The engine navigates the child itself. Calling Navigate on it here would load the
    /// page twice and leave a bogus first entry in the new tab's back history.
    /// </summary>
    private async void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;

        // Take the deferral SYNCHRONOUSLY. Anything after the first await is too late —
        // the event has already returned by then.
        //
        // Windows.Foundation.Deferral, NOT CoreWebView2Deferral: that name belongs to the
        // .NET wrapper in the net462 lib, and this project binds the WinRT projection
        // (Microsoft.Web.WebView2.Core.winmd), where every deferral is the standard one.
        Deferral deferral = args.GetDeferral();

        try
        {
            // A window the user did not ask for is a pop-up. One they did — a click, a
            // Ctrl+click, a middle-click — always opens, which is the distinction the
            // setting's description promises.
            if (!args.IsUserInitiated && AppSettings.GetBool("popups"))
            {
                return;
            }

            if (BlankTabRequested is not { } request)
            {
                return;
            }

            if (await request() is CoreWebView2 blank)
            {
                args.NewWindow = blank;
            }
        }
        catch (Exception)
        {
            // Losing the new window is survivable; an unhandled exception on an async void
            // handler is not — it comes back as a process-level crash.
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void OnWindowCloseRequested(CoreWebView2 sender, object args) => CloseRequested?.Invoke(this);

    public static string HostOf(string url)
    {
        if (string.IsNullOrWhiteSpace(url)
            || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            string host = uri.Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : host;
        }

        return url;
    }

    /// <summary>
    /// Unsubscribe, then close. Relying on collection leaves the renderer process alive —
    /// close ten tabs without this and ten renderers stay resident.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (View.CoreWebView2 is CoreWebView2 core)
            {
                core.SourceChanged -= OnSourceChanged;
                core.HistoryChanged -= OnHistoryChanged;
                core.DocumentTitleChanged -= OnDocumentTitleChanged;
                core.FaviconChanged -= OnFaviconChanged;
                core.NavigationStarting -= OnNavigationStarting;
                core.NavigationCompleted -= OnNavigationCompleted;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.WindowCloseRequested -= OnWindowCloseRequested;
                core.WebMessageReceived -= OnWebMessageReceived;
                core.WebResourceRequested -= OnWebResourceRequested;
                core.DownloadStarting -= OnDownloadStarting;
            }

            Favicons.Unpin(IconKey);
            View.Close();
        }
        catch (Exception)
        {
            // Already gone.
        }

        Changed = null;
        CloseRequested = null;
        ShortcutPressed = null;
        DownloadStarted = null;
        NavigationFailed = null;
        Navigated = null;
        IsReady = false;
    }
}
