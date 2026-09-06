using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

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

    private static readonly string[] Palette =
    {
        "#0A84FF", "#FF9F0A", "#30D158", "#FF375F", "#BF5AF2",
        "#64D2FF", "#FFD60A", "#5E5CE6", "#FF6482", "#66D4CF"
    };

    private static int _counter;

    public Microsoft.UI.Xaml.Controls.WebView2 View { get; }

    public string Title { get; private set; } = "New Tab";
    public string Url { get; private set; } = string.Empty;
    public bool IsSecure { get; private set; }
    public bool IsReady { get; private set; }

    /// <summary>Stand-in for a favicon until Phase 4 fetches real ones.</summary>
    public string Monogram { get; private set; } = "•";
    public string Accent { get; }

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

    /// <summary>Set for Aviary's own pages, whose titles we own rather than read.</summary>
    private string? _internalTitle;

    /// <summary>Kept so blocked requests can be answered with a synthetic response.</summary>
    private CoreWebView2Environment? _environment;

    public BrowserTab()
    {
        Accent = Palette[_counter++ % Palette.Length];
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
        core.NavigationCompleted += OnNavigationCompleted;
        core.NewWindowRequested += OnNewWindowRequested;
        core.WindowCloseRequested += OnWindowCloseRequested;
        core.WebMessageReceived += OnWebMessageReceived;

        // Chromium would otherwise act on Ctrl+T itself and swallow it.
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;

        // Injected into EVERY document, including ones we did not write, so Ctrl+T keeps
        // working while the caret is inside a text box on somebody else's website.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(ShortcutScript);

        ApplyColorScheme();

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
            if (!TrackerBlocker.ShouldBlock(args.Request.Uri) || _environment is null)
            {
                return;
            }

            TrackerBlocker.CountBlocked();
            args.Response = _environment.CreateWebResourceResponse(null, 403, "Blocked by Aviary", string.Empty);
        }
        catch (Exception)
        {
            // Let the request through rather than take the page down. A missed advert is
            // cheaper than a broken browser.
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

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            string raw = args.TryGetWebMessageAsString();
            const string marker = "\"action\"";
            int i = raw.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0 || !raw.Contains("shortcut", StringComparison.Ordinal))
            {
                return;
            }

            int a = raw.IndexOf('"', i + marker.Length) + 1;
            int b = a > 0 ? raw.IndexOf('"', a) : -1;
            if (a > 0 && b > a)
            {
                ShortcutPressed?.Invoke(raw.Substring(a, b - a));
            }
        }
        catch (Exception)
        {
            // A malformed message from our own injected script is a bug to find in
            // testing, not a reason to take the browser down.
        }
    }

    private const string ShortcutScript = """
(function(){
  if (window.__aviaryKeys) { return; }
  window.__aviaryKeys = 1;
  window.addEventListener('keydown', function(e){
    if (!e.ctrlKey || e.altKey || e.metaKey) { return; }
    var k = (e.key || '').toLowerCase();
    var action = null;
    if (k === 't') { action = 'newtab'; }
    else if (k === 'w') { action = 'closetab'; }
    else if (k === 'l') { action = 'address'; }
    else if (k === 'tab') { action = 'nexttab'; }
    if (!action) { return; }
    e.preventDefault();
    e.stopPropagation();
    try { window.chrome.webview.postMessage(JSON.stringify({type:'shortcut', action:action})); } catch(err){}
  }, true);
})();
""";

    public bool CanGoBack => IsReady && View.CanGoBack;

    public bool CanGoForward => IsReady && View.CanGoForward;

    public void Navigate(string url)
    {
        if (IsReady && View.CoreWebView2 is not null)
        {
            Url = url;
            _internalTitle = null;
            View.CoreWebView2.Navigate(url);
        }
    }

    public void NavigateToHtml(string html, string displayTitle)
    {
        if (IsReady && View.CoreWebView2 is not null)
        {
            Url = string.Empty;
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
        Changed?.Invoke(this);
    }

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
        string host = HostOf(Url);
        Monogram = host.Length > 0 ? host.Substring(0, 1).ToUpperInvariant() : "•";

        Changed?.Invoke(this);
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        => Changed?.Invoke(this);

    /// <summary>
    /// target="_blank" and window.open() would otherwise escape the app entirely. Phase 2
    /// keeps them in place; making them open a real new tab needs a deferral and is a
    /// Phase 3 refinement.
    /// </summary>
    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        sender.Navigate(args.Uri);
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
                core.NavigationCompleted -= OnNavigationCompleted;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.WindowCloseRequested -= OnWindowCloseRequested;
                core.WebMessageReceived -= OnWebMessageReceived;
                core.WebResourceRequested -= OnWebResourceRequested;
            }

            View.Close();
        }
        catch (Exception)
        {
            // Already gone.
        }

        Changed = null;
        CloseRequested = null;
        ShortcutPressed = null;
        IsReady = false;
    }
}
