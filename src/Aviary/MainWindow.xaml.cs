using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Windows.Graphics;
using Windows.System;

namespace Aviary;

public sealed partial class MainWindow : Window
{
    /// <summary>
    /// Safari 18 on macOS. Set per-WebView so Phase 2 can vary it per tab, and set BEFORE
    /// the first navigation — changing it later only affects subsequent ones.
    ///
    /// The engine underneath is still Chromium, so navigator.userAgentData and the
    /// Sec-CH-UA headers will contradict this string. That is a known, deliberate trade;
    /// it becomes a visible per-tab toggle rather than a silent default.
    /// </summary>
    private const string SafariUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 " +
        "(KHTML, like Gecko) Version/18.0 Safari/605.1.15";

    private const string SearchPrefix = "https://duckduckgo.com/?q=";
    private const double CaptionSpacerFallback = 141;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private AppWindow? _appWindow;
    private bool _coreReady;
    private bool _addressFocused;
    private string _currentUrl = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        // Our chrome occupies the title bar row. The system still draws and owns the
        // caption buttons, which is the point — the window is Windows, the interface is
        // Safari. DragRegion is a dedicated empty element with a minimum width, so
        // dragging works regardless of how a given SDK version treats interactive
        // children up here.
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);

        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = GetAppWindow(hWnd);

        ConfigureTitleBar();

        // AppWindow.Resize takes PHYSICAL pixels, so a fixed 1280x840 opens at 853x560
        // effective on a 150% display — small enough to squeeze the toolbar past its own
        // layout. Scale by the window's DPI instead.
        double scale = GetScaleForWindow(hWnd);
        _appWindow?.Resize(new SizeInt32((int)(1280 * scale), (int)(840 * scale)));

        RootGrid.Loaded += RootGrid_Loaded;
        RootGrid.SizeChanged += (_, _) => UpdateCaptionSpacer();
        RootGrid.ActualThemeChanged += (_, _) => ApplyWebViewBackground();
        Closed += MainWindow_Closed;
    }

    private static AppWindow? GetAppWindow(IntPtr hWnd)
    {
        try
        {
            // WindowId lives in Microsoft.UI, which importing Microsoft.UI.Windowing does
            // not bring in. Fully qualified so it cannot depend on a using directive.
            Microsoft.UI.WindowId id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(id);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double GetScaleForWindow(IntPtr hWnd)
    {
        try
        {
            uint dpi = GetDpiForWindow(hWnd);
            return dpi == 0 ? 1.0 : dpi / 96.0;
        }
        catch (Exception)
        {
            return 1.0;
        }
    }

    private void ConfigureTitleBar()
    {
        if (_appWindow is null)
        {
            return;
        }

        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            // Without this the caption buttons render 46x32 top-aligned inside our 48px
            // toolbar, floating above their own row. Tall makes them 46x48.
            _appWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

            // Safari has no window icon and no icon system menu. Hiding it is correct on
            // its own terms, and it also stops the system swallowing clicks in the
            // top-left corner, where the Back button sits.
            _appWindow.TitleBar.IconShowOptions = IconShowOptions.HideIconAndSystemMenu;
        }
        catch (Exception)
        {
            // Older Windows 10 builds fall back to the system title bar. Nothing to do.
        }
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        // Loaded fires again whenever the element re-enters the visual tree. Detaching
        // here means the seven CoreWebView2 handlers can only ever be subscribed once.
        RootGrid.Loaded -= RootGrid_Loaded;

        UpdateCaptionSpacer();
        await InitialiseWebViewAsync();
    }

    /// <summary>
    /// Reserves exactly as much room as the system caption buttons actually occupy.
    /// Hardcoding 138px is wrong the moment the display is at 150%, where the real inset
    /// is 207 device pixels.
    /// </summary>
    private void UpdateCaptionSpacer()
    {
        try
        {
            if (_appWindow is null)
            {
                CaptionSpacer.Width = CaptionSpacerFallback;
                return;
            }

            double scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0)
            {
                scale = 1.0;
            }

            double width = _appWindow.TitleBar.RightInset / scale;

            // A zero inset means the title bar is not reporting yet; never collapse the
            // spacer, or the Home button ends up underneath the close button.
            CaptionSpacer.Width = width < 100 ? CaptionSpacerFallback : width;
        }
        catch (Exception)
        {
            CaptionSpacer.Width = CaptionSpacerFallback;
        }
    }

    // ══ WEBVIEW2 ═══════════════════════════════════════════════════════════════

    private async System.Threading.Tasks.Task InitialiseWebViewAsync()
    {
        if (_coreReady)
        {
            return;
        }

        try
        {
            // Explicit environment rather than the implicit one. Aviary ships unpackaged
            // and may sit in Program Files, where the default user data folder next to
            // the .exe is unwritable and creation dies with "Access Denied".
            CoreWebView2Environment environment = await CoreWebView2Environment.CreateWithOptionsAsync(
                string.Empty,
                WebViewPaths.UserDataFolder,
                new CoreWebView2EnvironmentOptions());

            await Web.EnsureCoreWebView2Async(environment);
        }
        catch (Exception ex)
        {
            ShowError(
                "Aviary can't start its browser engine",
                "WebView2 failed to initialise. " + ex.Message);
            return;
        }

        if (Web.CoreWebView2 is null)
        {
            ShowError(
                "Aviary can't start its browser engine",
                "WebView2 initialised without a core. Is the Evergreen runtime installed?");
            return;
        }

        CoreWebView2 core = Web.CoreWebView2;

        core.Settings.UserAgent = SafariUserAgent;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;

        core.SourceChanged += Core_SourceChanged;
        core.HistoryChanged += Core_HistoryChanged;
        core.NavigationStarting += Core_NavigationStarting;
        core.NavigationCompleted += Core_NavigationCompleted;
        core.DocumentTitleChanged += Core_DocumentTitleChanged;
        core.NewWindowRequested += Core_NewWindowRequested;
        core.ProcessFailed += Core_ProcessFailed;

        _coreReady = true;
        ApplyWebViewBackground();
        UpdateNavigationState();
        GoHome();
    }

    /// <summary>
    /// Without this every navigation flashes white in dark mode, because WebView2 paints
    /// its own background before the page has one.
    /// </summary>
    private void ApplyWebViewBackground()
    {
        try
        {
            if (Application.Current.Resources["AvPageBrush"] is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
            {
                Web.DefaultBackgroundColor = brush.Color;
            }
        }
        catch (Exception)
        {
            // Leave WebView2's own default. A white flash is survivable; a crash is not.
        }
    }

    // The args type of SourceChanged differs between the WinRT and .NET WebView2
    // reference docs. Declaring it as object compiles against both, and the argument is
    // never read anyway.
    private void Core_SourceChanged(CoreWebView2 sender, object args)
    {
        _currentUrl = sender.Source;
        if (!_addressFocused)
        {
            ShowIdleAddress();
        }
    }

    private void Core_HistoryChanged(CoreWebView2 sender, object args) => UpdateNavigationState();

    private void Core_NavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        HideError();
    }

    private void Core_NavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        UpdateNavigationState();

        if (!args.IsSuccess && args.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
        {
            ShowError("This page didn't load", args.WebErrorStatus.ToString());
        }
    }

    private void Core_DocumentTitleChanged(CoreWebView2 sender, object args)
    {
        string title = sender.DocumentTitle;

        // "Page — Aviary" is the Windows convention, but an internal page already called
        // Aviary would come out as "Aviary — Aviary".
        Title = string.IsNullOrWhiteSpace(title) || title == "Aviary"
            ? "Aviary"
            : title + " — Aviary";
    }

    /// <summary>
    /// Without this, target="_blank" and window.open() escape the app entirely. Phase 2
    /// turns these into new tabs; for now they open in place.
    /// </summary>
    private void Core_NewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        sender.Navigate(args.Uri);
    }

    /// <summary>
    /// A dead render process is recoverable — Reload genuinely works. A dead browser
    /// process is not, and every guarded call has to stop pretending otherwise, or the
    /// next click throws and closes the app.
    /// </summary>
    private void Core_ProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        if (args.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited)
        {
            ShowError("This page stopped responding", "Press Reload to try again.");
            return;
        }

        _coreReady = false;
        UpdateNavigationState();
        ShowError(
            "Aviary's browser engine stopped",
            "The browser process ended unexpectedly (" + args.ProcessFailedKind +
            "). Close and reopen Aviary to carry on.");
    }

    // ══ CHROME ═════════════════════════════════════════════════════════════════

    private void UpdateNavigationState()
    {
        BackButton.IsEnabled = _coreReady && Web.CanGoBack;
        ForwardButton.IsEnabled = _coreReady && Web.CanGoForward;
        ReloadButton.IsEnabled = _coreReady;

        bool secure = _currentUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        LockGlyph.Visibility = secure ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// An internal page has no address worth showing. Without this the start page reads
    /// "about:blank", and pressing Enter searches the web for it.
    /// </summary>
    private static bool IsInternal(string url) =>
        string.IsNullOrWhiteSpace(url)
        || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
        || url.StartsWith("about:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Idle: the registrable domain only, centred. Not the scheme, not the path.</summary>
    private void ShowIdleAddress()
    {
        AddressBox.TextAlignment = TextAlignment.Center;
        AddressBox.Text = FriendlyHost(_currentUrl);
    }

    private static string FriendlyHost(string url)
    {
        if (IsInternal(url))
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

    private void AddressBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _addressFocused = true;
        FieldFocus.Opacity = 1;

        // The switch to left-aligned full URL happens on a single frame. Animating it
        // reads as a web-page effect; Safari snaps it.
        AddressBox.TextAlignment = TextAlignment.Left;
        AddressBox.Text = IsInternal(_currentUrl) ? string.Empty : _currentUrl;

        // Deferred: a click delivers focus and then places the caret, which would collapse
        // a selection made synchronously here.
        AddressBox.DispatcherQueue?.TryEnqueue(() =>
        {
            if (_addressFocused)
            {
                AddressBox.SelectAll();
            }
        });
    }

    private void AddressBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _addressFocused = false;
        FieldFocus.Opacity = 0;
        AddressBox.Select(0, 0);
        ShowIdleAddress();
    }

    private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;

        // Only surrender focus if something actually happened. Pressing Enter on an empty
        // field should leave the caret where it is.
        if (Navigate(AddressBox.Text))
        {
            Web.Focus(FocusState.Programmatic);
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_coreReady && Web.CanGoBack)
        {
            Web.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_coreReady && Web.CanGoForward)
        {
            Web.GoForward();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_coreReady)
        {
            Web.Reload();
        }
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e) => GoHome();

    private void GoHome()
    {
        if (!_coreReady || Web.CoreWebView2 is null)
        {
            return;
        }

        HideError();
        _currentUrl = string.Empty;
        Web.CoreWebView2.NavigateToString(HomePage);
        ShowIdleAddress();

        // Leave focus on the page, not the address bar. Otherwise the window opens
        // wearing its focus ring, which is the first thing anyone judging the chrome
        // sees, and a blue outline is not the resting state of this design.
        Web.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Something that looks like a host becomes a URL; anything else becomes a search.
    /// Returns whether a navigation was actually started.
    /// </summary>
    private bool Navigate(string input)
    {
        if (!_coreReady || Web.CoreWebView2 is null)
        {
            return false;
        }

        string text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        string target;

        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? absolute) &&
            (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            target = absolute.ToString();
        }
        else if (!text.Contains(' ') && text.Contains('.'))
        {
            target = "https://" + text;
        }
        else
        {
            target = SearchPrefix + Uri.EscapeDataString(text);
        }

        HideError();

        // Update the model before navigating. Otherwise LostFocus — which fires the moment
        // focus moves to the WebView — repaints the bar with the PREVIOUS page's domain and
        // leaves it there for the whole navigation. SourceChanged corrects it on redirect.
        _currentUrl = target;
        Web.CoreWebView2.Navigate(target);
        return true;
    }

    private void ShowError(string title, string detail)
    {
        ErrorTitle.Text = title;
        ErrorDetail.Text = detail;
        ErrorPanel.Visibility = Visibility.Visible;
        Web.Visibility = Visibility.Collapsed;
    }

    private void HideError()
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        Web.Visibility = Visibility.Visible;
    }

    // ══ SHUTDOWN ═══════════════════════════════════════════════════════════════

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // Unsubscribe before closing, then close explicitly. Relying on collection leaves
        // the renderer process alive. The whole body is guarded because during teardown
        // the core may already be gone, and an exception escaping a Closed handler is the
        // exact failure this was written to avoid.
        try
        {
            if (Web.CoreWebView2 is CoreWebView2 core)
            {
                core.SourceChanged -= Core_SourceChanged;
                core.HistoryChanged -= Core_HistoryChanged;
                core.NavigationStarting -= Core_NavigationStarting;
                core.NavigationCompleted -= Core_NavigationCompleted;
                core.DocumentTitleChanged -= Core_DocumentTitleChanged;
                core.NewWindowRequested -= Core_NewWindowRequested;
                core.ProcessFailed -= Core_ProcessFailed;
            }

            Web.Close();
        }
        catch (Exception)
        {
            // Already gone. Nothing useful to do while the window is closing.
        }
    }

    // ══ THE PLACEHOLDER HOME PAGE ══════════════════════════════════════════════
    // The real Start Page is Phase 4. This exists so Phase 1 has something honest to
    // open with, and so the Safari user agent can be checked without leaving the app.

    private const string HomePage = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<!-- Without a title, WebView2 falls back to the document URL — and NavigateToString's
     URL is the entire page base64'd into a data: URI, which then lands in the window
     title and the taskbar. -->
<title>Aviary</title>
<style>
  :root { color-scheme: light dark; }
  html,body { height:100%; margin:0; }
  body {
    display:grid; place-items:center;
    font: 400 15px/1.6 "Segoe UI Variable Text","Segoe UI",system-ui,sans-serif;
    letter-spacing:-.006em;
    background:#FFFFFF; color:rgba(0,0,0,.85);
  }
  @media (prefers-color-scheme: dark) {
    body { background:#1E1E1E; color:rgba(255,255,255,.92); }
    .card { background:rgba(255,255,255,.05); border-color:rgba(255,255,255,.10); }
    .muted { color:rgba(255,255,255,.55); }
    code { background:rgba(255,255,255,.08); }
  }
  main { width:min(560px, 100% - 64px); text-align:center; }
  h1 { font-size:34px; font-weight:600; letter-spacing:-.024em; margin:0 0 6px; }
  .muted { color:rgba(0,0,0,.5); }
  .phase {
    display:inline-block; margin-bottom:22px; padding:4px 10px; border-radius:99px;
    font:500 11px/1 ui-monospace,Consolas,monospace; letter-spacing:.08em; text-transform:uppercase;
    border:1px solid currentColor; opacity:.55;
  }
  .links { display:flex; flex-wrap:wrap; gap:8px; justify-content:center; margin:26px 0 0; }
  .links a {
    text-decoration:none; padding:8px 14px; border-radius:8px;
    border:1px solid rgba(128,128,128,.28); color:inherit; font-size:14px;
  }
  .links a:hover { border-color:rgba(128,128,128,.6); }
  .card {
    margin-top:28px; padding:16px 18px; border-radius:10px; text-align:left;
    background:rgba(0,0,0,.03); border:1px solid rgba(0,0,0,.07);
  }
  .card h2 { margin:0 0 8px; font-size:12px; letter-spacing:.08em; text-transform:uppercase; opacity:.55; font-weight:600; }
  code { font:400 12px/1.5 ui-monospace,Consolas,monospace; word-break:break-all; background:rgba(0,0,0,.05); padding:2px 5px; border-radius:4px; }
  .verdict { margin-top:10px; font-size:13px; }
</style>
</head>
<body>
  <main>
    <div class="phase">Phase 1 &middot; Shell</div>
    <h1>Aviary</h1>
    <p class="muted">The window works. Type a URL above and it browses.<br>
       The real Start Page arrives in Phase 4.</p>

    <div class="links">
      <a href="https://en.wikipedia.org">Wikipedia</a>
      <a href="https://news.ycombinator.com">Hacker News</a>
      <a href="https://learn.microsoft.com">Microsoft Learn</a>
      <a href="https://developer.mozilla.org">MDN</a>
    </div>

    <div class="card">
      <h2>User agent being sent</h2>
      <code id="ua"></code>
      <div class="verdict" id="verdict"></div>
    </div>
  </main>
<script>
  var ua = navigator.userAgent;
  document.getElementById('ua').textContent = ua;
  var looksSafari = ua.indexOf('Safari') !== -1 && ua.indexOf('Chrome') === -1;
  document.getElementById('verdict').textContent = looksSafari
    ? '✓ Sites will serve their Safari code paths.'
    : '✗ Still identifying as Chromium — the user agent did not take.';
</script>
</body>
</html>
""";
}
