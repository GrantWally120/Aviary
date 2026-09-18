using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace Aviary;

public sealed partial class MainWindow : Window
{
    private const double CaptionSpacerFallback = 141;
    // No maximum. Safari tabs always share the whole strip; capping them at a fixed
    // width is precisely what gives Chrome its silhouette.
    private const double TabMinWidth = 56;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly List<BrowserTab> _tabs = new();
    private readonly Dictionary<BrowserTab, ToggleButton> _tabButtons = new();

    private AppWindow? _appWindow;
    private BrowserTab? _active;
    private bool _addressFocused;
    private bool _showFullUrl;
    private int _findMatches;
    private BrowserTab? _dragTab;
    private double _dragStartX;
    private bool _dragging;
    /// <summary>
    /// The chosen engine's results address; the words go on the end (SearchEngines.cs). Read
    /// from the store every time rather than cached per window: a cached copy was only updated in
    /// the window whose Settings tab changed it, so a private window kept searching the old engine.
    /// </summary>
    private static string SearchPrefix => SearchEngines.Current.SearchUrl;

    public MainWindow(bool isPrivate = false)
    {
        _private = isPrivate;
        OpenWindows.Add(this);

        if (!isPrivate)
        {
            Diagnostics.Mark("window-constructing");
        }

        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);

        IntPtr hWnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = GetAppWindow(hWnd);
        ConfigureTitleBar();

        // AppWindow.Resize takes PHYSICAL pixels, so a fixed size opens small on a
        // scaled display. Scale by the window's DPI.
        double scale = GetScaleForWindow(hWnd);
        _appWindow?.Resize(new SizeInt32((int)(1280 * scale), (int)(860 * scale)));

        RootGrid.Loaded += RootGrid_Loaded;
        RootGrid.SizeChanged += (_, _) => UpdateCaptionSpacer();
        RootGrid.ActualThemeChanged += (_, _) => OnEffectiveThemeChanged();
        Closed += MainWindow_Closed;

        AddAccelerator(VirtualKey.T, VirtualKeyModifiers.Control, (_, _) => OpenNewTab(null));
        AddAccelerator(VirtualKey.W, VirtualKeyModifiers.Control, (_, _) => { if (_active is not null) CloseTab(_active); });
        AddAccelerator(VirtualKey.L, VirtualKeyModifiers.Control, (_, _) => AddressBox.Focus(FocusState.Programmatic));
        AddAccelerator(VirtualKey.Tab, VirtualKeyModifiers.Control, (_, _) => CycleTab(1));
        AddAccelerator(VirtualKey.F, VirtualKeyModifiers.Control, (_, _) => OpenFindBar());
        AddAccelerator(VirtualKey.Escape, VirtualKeyModifiers.None, (_, _) => CloseFindBar());
        AddAccelerator(VirtualKey.D, VirtualKeyModifiers.Control, (_, _) => AddBookmark());

        AddAccelerator(VirtualKey.Tab, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            (_, _) => CycleTab(-1));
        AddAccelerator(VirtualKey.D, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            (_, _) => AddToReadingList());
        AddAccelerator(VirtualKey.L, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            (_, _) => ToggleSidebar(null));
        AddAccelerator(VirtualKey.H, VirtualKeyModifiers.Control, (_, _) => ShowHistory());
        AddAccelerator(VirtualKey.R, VirtualKeyModifiers.Control, (_, _) => ReloadOrStop());
        AddAccelerator(VirtualKey.R, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift,
            (_, _) => ToggleReader());
        AddAccelerator(VirtualKey.J, VirtualKeyModifiers.Control,
            (_, _) => DownloadsButton_Click(DownloadsButton, new RoutedEventArgs()));

        // Ctrl+1..8 select that tab; Ctrl+9 is the LAST one, which is what every browser
        // on this platform does and what the muscle memory expects.
        for (int i = 0; i < 8; i++)
        {
            int index = i;
            AddAccelerator((VirtualKey)((int)VirtualKey.Number1 + i), VirtualKeyModifiers.Control,
                (_, _) => SelectTabByIndex(index));
        }

        AddAccelerator(VirtualKey.Number9, VirtualKeyModifiers.Control,
            (_, _) => SelectTabByIndex(_tabs.Count - 1));

        AddAccelerator((VirtualKey)187, VirtualKeyModifiers.Control, (_, _) => Zoom(+1));   // '='
        AddAccelerator((VirtualKey)189, VirtualKeyModifiers.Control, (_, _) => Zoom(-1));   // '-'
        AddAccelerator(VirtualKey.Number0, VirtualKeyModifiers.Control, (_, _) => Zoom(0));
        AddAccelerator(VirtualKey.F11, VirtualKeyModifiers.None, (_, _) => ToggleFullScreen());
        AddAccelerator(VirtualKey.N, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, (_, _) => OpenPrivateWindow());
        AddAccelerator((VirtualKey)188, VirtualKeyModifiers.Control, (_, _) => OpenSettingsTab());   // Ctrl+, — Safari's own
    }

    private void SelectTabByIndex(int index)
    {
        if (index >= 0 && index < _tabs.Count)
        {
            SelectTab(_tabs[index]);
        }
    }

    /// <summary>Ctrl+Shift+D. Safari's own shortcut for the Reading List.</summary>
    private void AddToReadingList()
    {
        if (_active is not { } tab || BrowserTab.HostOf(tab.Url).Length == 0)
        {
            return;
        }

        ReadingList.Add(tab.Title, tab.Url, record: !_private);

        if (_sidebarVisible && _pane == SidebarPane.ReadingList)
        {
            RefreshSidebar();
        }
    }

    private void ShowHistory()
    {
        ToggleSidebar(true);
        ShowPane(SidebarPane.History);
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers mods, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        KeyboardAccelerator a = new() { Key = key, Modifiers = mods };

        // "Ctrl+Shift+D", for diagnostics. Built once here rather than on every press.
        string combo = (mods.HasFlag(VirtualKeyModifiers.Control) ? "Ctrl+" : "")
                     + (mods.HasFlag(VirtualKeyModifiers.Shift) ? "Shift+" : "")
                     + (mods.HasFlag(VirtualKeyModifiers.Menu) ? "Alt+" : "")
                     + key;

        a.Invoked += (s, e) =>
        {
            e.Handled = true;
            DiagUi("shortcut", new { keys = combo, from = "window" });
            handler(s, e);
        };
        RootGrid.KeyboardAccelerators.Add(a);
    }

    private static AppWindow? GetAppWindow(IntPtr hWnd)
    {
        try
        {
            Microsoft.UI.WindowId id = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
            return AppWindow.GetFromWindowId(id);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "window: get AppWindow");
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
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "window: get DPI");
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
        catch (Exception ex)
        {
            // Older Windows 10 builds fall back to the system title bar.
            Diagnostics.Swallowed(ex, "window: configure title bar");
        }
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        Diagnostics.Mark("window-loaded");
        UpdateCaptionSpacer();
        WebViewEnvironment.EnsureUserDataFolder();

        // The watchdog and the minute sample need the UI thread; session.json needs a
        // description of the window. Both come from here — from the normal window only:
        // a private window is never described.
        if (!_private)
        {
            Diagnostics.AttachUi(DispatcherQueue, DescribeWindow);
        }

        // Whatever look was last chosen is the one Aviary opens with. Classic only wins
        // on a fresh install, because that is the default AppSettings falls back to.
        AppSettings.Load();

        // Fully qualified on purpose: this file already has `using Windows.System;` for
        // VirtualKey, so a bare DispatcherQueue resolves to Windows.System's and will not
        // accept a Microsoft.UI.Dispatching handler.
        Favicons.Initialise(DispatcherQueue);
        Favicons.Updated += OnFaviconUpdated;
        ApplyPrivateChrome();

        // Dragging the window to a monitor at a different scale invalidates every decoded
        // bitmap — 16 logical pixels stops being 16 physical ones.
        if (RootGrid.XamlRoot is not null)
        {
            RootGrid.XamlRoot.Changed += (_, _) =>
            {
                Favicons.InvalidateDecoded();
                RebuildTabStrip();
                RefreshBookmarksBar();

                // A new scale: the glass shadows are drawn in physical pixels.
                RefreshGlass();
            };
        }

        // Phase 0's glass capsule on each bar — before the look is applied, which switches it on.
        InitialiseGlass();

        RestorePreferences();
        InitialiseSidebar();

        // Never create the session's first WebView on a minimised window (bug 50).
        await WaitUntilNotMinimisedAsync();

        if (_private)
        {
            // Pinned tabs belong to the normal window; a private one starts with just the
            // Start Page, and there is no crash test to arm here.
            await OpenNewTabAsync(null);
            return;
        }

        Diagnostics.Mark("chrome-ready");

        await RestorePinnedAsync();
        Diagnostics.Mark("pinned-tabs-restored");
        await OpenNewTabAsync(null);
        Diagnostics.Mark("first-tab-created");

        // The engine's code is loaded now; make sure a native crash still reaches Aviary's recorder.
        NativeCrash.CheckStillInstalled();

        ArmCrashTest();
    }

    /// <summary>
    /// Bug 50 — PARTLY fixed. Minimising the window while its FIRST tab is being created kills
    /// Aviary outright: a native fail-fast (0xc0000409 in ucrtbase.dll) with no .NET exception,
    /// no crash report, nothing in crash.log. Measured by minimising N ms after the window
    /// appears: 0, 100, 250 ms crash; 400 ms and later never; and a LATER tab created while
    /// minimised (Ctrl+T, a pop-up) never crashes. The fault is inside the engine or WinUI,
    /// out of reach of a catch, and starting the engine earlier did not move it.
    ///
    /// What this does cover: a window that is ALREADY minimised when it finishes loading
    /// (minimised at 0 ms) waits here, and its first tab is made once it is back on screen.
    /// What it cannot: a minimise that lands in the ~300 ms the first tab takes to build.
    /// Found while testing M2, 12 Sep; the repro script is in HANDOFF §10, bug 50.
    /// </summary>
    private Task WaitUntilNotMinimisedAsync()
    {
        if (_appWindow is not { } window
            || window.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized })
        {
            return Task.CompletedTask;
        }

        TaskCompletionSource restored = new();

        void OnChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (sender.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Minimized })
            {
                sender.Changed -= OnChanged;
                restored.TrySetResult();
            }
        }

        window.Changed += OnChanged;
        DiagUi("first-tab-deferred", new { reason = "window minimised during start-up" });
        return restored.Task;
    }

    /// <summary>
    /// Proves the crash recorder works — PHASE5-PLAN.md says "genuinely works" is a test, and a
    /// crash cannot be tested without causing one.
    ///
    /// Reachable ONLY through an environment variable on Aviary's own process, which nothing
    /// inside the browser — no page, setting, menu or message — can set:
    ///   AVIARY_CRASH_TEST=ui     a failure on the UI thread; Aviary survives it (as it did the
    ///                            M0 pin bug) and crashes/ gets a report.
    ///   AVIARY_CRASH_TEST=fatal  a failure on a background thread, which ends the process;
    ///                            the report must still be written first.
    ///   AVIARY_CRASH_TEST=native the C runtime's abort() on the UI thread — the way bug 50 ends
    ///                            Aviary; NativeCrash must write crash.log and crashes/ first.
    /// </summary>
    private void ArmCrashTest()
    {
        string? mode = Environment.GetEnvironmentVariable("AVIARY_CRASH_TEST");
        if (mode is not ("ui" or "fatal" or "native"))
        {
            return;
        }

        // A DispatcherTimer's Tick is an ordinary framework-raised handler, the same path a
        // menu click takes — which is how the real M0 crash arrived.
        DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();

            if (mode == "native")
            {
                NativeCrash.AbortForTest();
                return;
            }

            if (mode == "fatal")
            {
                new System.Threading.Thread(() =>
                    throw new InvalidOperationException("AVIARY_CRASH_TEST=fatal: a deliberate background-thread failure."))
                { IsBackground = true }.Start();
                return;
            }

            throw new InvalidOperationException("AVIARY_CRASH_TEST=ui: a deliberate UI-thread failure.");
        };
        timer.Start();
    }

    /// <summary>
    /// The window, for diagnostics' session.json. Read on the UI thread by the minute sample;
    /// the result is plain values, so the writer can serialise it from any thread.
    /// </summary>
    private object? DescribeWindow()
    {
        string state = string.Empty;
        int width = 0, height = 0, x = 0, y = 0;

        if (_appWindow is not null)
        {
            width = _appWindow.Size.Width;
            height = _appWindow.Size.Height;
            x = _appWindow.Position.X;
            y = _appWindow.Position.Y;
            state = _appWindow.Presenter is OverlappedPresenter presenter ? presenter.State.ToString() : string.Empty;
        }

        return new
        {
            widthPx = width,
            heightPx = height,
            x,
            y,
            state,
            scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0,
            tabs = _tabs.Count,
            pinnedTabs = _pinned.Count,
            activeTab = _active is null ? -1 : _tabs.IndexOf(_active),
            look = AppSettings.Get("look"),
            theme = AppSettings.Get("theme"),
            shownTheme = RootGrid.ActualTheme.ToString(),
            tabLayout = AppSettings.Get("tabLayout"),
            sidebar = _sidebarVisible ? _pane.ToString() : "hidden",
        };
    }

    /// <summary>An icon finished downloading or decoding. The strip's cells listen for
    /// themselves; the favourites bar is rebuilt wholesale because it is small.</summary>
    private void OnFaviconUpdated(string key) => RefreshBookmarksBar();

    /// <param name="resubscribe">
    /// False when re-applying after a settings reset — the Bookmarks.Changed handler is
    /// already attached, and adding it a second time makes the favourites bar rebuild
    /// twice for every change.
    /// </param>
    private void RestorePreferences(bool resubscribe = true)
    {
        // After "Reset settings" this is what switches collection back off — its default.
        Diagnostics.SetEnabled(AppSettings.GetBool(Diagnostics.SettingKey));

        TrackerBlocker.Enabled = AppSettings.GetBool("blockTrackers");
        _showFullUrl = AppSettings.GetBool("fullUrl");
        HomeButton.Visibility = AppSettings.GetBool("homeButton") ? Visibility.Visible : Visibility.Collapsed;

        RootGrid.RequestedTheme = AppSettings.Get("theme") switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        ApplyLook(AppSettings.Get("look"));

        if (resubscribe)
        {
            Bookmarks.Changed += RefreshBookmarksBar;
        }

        RefreshBookmarksBar();
    }

    // ══ FAVOURITES BAR ═════════════════════════════════════════════════════

    private void AddBookmark()
    {
        if (_active is not { } tab || BrowserTab.HostOf(tab.Url).Length == 0)
        {
            return;
        }

        Bookmarks.Add(tab.Title, tab.Url, record: !_private);

        // Adding one with the bar hidden would look like nothing happened.
        if (!AppSettings.GetBool("bookmarksBar"))
        {
            AppSettings.Set("bookmarksBar", "true");

            // …and if Settings is open in another tab, its switch has to follow, or it
            // sits there reading "off" while the bar it controls is plainly visible.
            PushToSettingsPage("bookmarksBar", true);
        }

        RefreshBookmarksBar();
    }

    private void RefreshBookmarksBar()
    {
        BookmarksStrip.Children.Clear();

        foreach (Bookmark bookmark in Bookmarks.Items)
        {
            FaviconView chip = new(16, RootGrid.Resources);
            chip.SetSource(bookmark.Url, bookmark.Monogram, internalPage: false);

            StackPanel content = new() { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(chip);
            content.Children.Add(new TextBlock
            {
                Text = bookmark.Title,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 160
            });

            Button item = new()
            {
                Style = (Style)RootGrid.Resources["AvBookmarkButton"],
                Content = content
            };
            ToolTipService.SetToolTip(item, bookmark.Url);

            Bookmark captured = bookmark;
            item.Click += (_, _) => { _active?.Navigate(captured.Url); UpdateChrome(); };

            item.RightTapped += (_, e) =>
            {
                e.Handled = true;
                MenuFlyout menu = new();
                Themed(menu, "favourites bar");
                MenuFlyoutItem remove = new() { Text = "Remove" };
                remove.Click += (_, _) => { Bookmarks.Remove(captured); RefreshBookmarksBar(); };
                menu.Items.Add(remove);
                menu.ShowAt(item);
            };

            BookmarksStrip.Children.Add(item);
        }

        // The bar follows its SWITCH, not its contents. Requiring at least one bookmark
        // meant turning the toggle on with an empty list did visibly nothing, which reads
        // as a broken switch rather than as an empty bar. Safari shows the empty strip.
        bool on = AppSettings.GetBool("bookmarksBar");
        BookmarksBar.Visibility = on && !_fullScreen ? Visibility.Visible : Visibility.Collapsed;
        BookmarksEmptyHint.Visibility = on && Bookmarks.Items.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

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
            CaptionSpacer.Width = width < 100 ? CaptionSpacerFallback : width;
        }
        catch (Exception ex)
        {
            CaptionSpacer.Width = CaptionSpacerFallback;
            Diagnostics.Swallowed(ex, "window: caption spacer");
        }
    }

    // ══ TABS ═══════════════════════════════════════════════════════════════

    private async void OpenNewTab(string? url) => await OpenNewTabAsync(url);

    /// <summary>
    /// Creates a tab, wires it, puts it in the tree and starts its engine — and does NOT
    /// navigate it.
    ///
    /// The three callers need three different things afterwards: a new tab loads the Start
    /// Page or a URL, a settings tab loads Settings, and a tab opened for target="_blank"
    /// loads nothing at all, because the engine navigates that one itself. Sharing the
    /// wiring matters beyond tidiness: this is the list of event subscriptions, and a tab
    /// created down one path that missed one would silently lose downloads or shortcuts.
    /// </summary>
    private async Task<BrowserTab?> CreateTabAsync(bool select, string reason = "new tab")
    {
        long created = System.Diagnostics.Stopwatch.GetTimestamp();
        BrowserTab tab = new(_private);
        tab.Changed += OnTabChanged;
        tab.CloseRequested += t => CloseTab(t);
        tab.ShortcutPressed += OnShortcut;
        tab.DownloadStarted += OnDownloadStarted;
        tab.NavigationFailed += OnNavigationFailed;
        tab.Navigated += OnTabNavigated;
        tab.FullScreenChanged += OnTabFullScreenChanged;
        tab.HttpsFallback += OnHttpsFallback;
        WireShields(tab);
        tab.BlankTabRequested = OpenBlankTabAsync;

        _tabs.Add(tab);

        // The colour Aviary's pages sit on, from the start. The tab itself stays invisible until
        // its first page has painted (BrowserTab.Background.cs, bug 61).
        tab.SetThemePageColour(ThemePageColour());

        // Added to the tree BEFORE initialising. A WebView2 created while collapsed and
        // shown later can come up rendering but not accepting the mouse; creating it in a
        // visible slot avoids that whole class of bug.
        ContentHost.Children.Add(tab.View);

        if (select)
        {
            SelectTab(tab);
        }

        RebuildTabStrip();

        try
        {
            await tab.InitialiseAsync();
        }
        catch (Exception ex)
        {
            // Category 11: a WebView that did not start. The user sees the message below;
            // diagnostics keeps the exception that explains it.
            Diagnostics.Anomaly("webview-start", ex.GetType().Name + " — " + ex.Message,
                new { reason, hresult = "0x" + ex.HResult.ToString("X8"), stack = ex.StackTrace });
            ShowError("Aviary can't start its browser engine", ex.Message);
            return null;
        }

        double ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(created).TotalMilliseconds, 1);
        DiagPerf("tab-create", new { tab = tab.Id, reason, ms, tabs = _tabs.Count });
        DiagTrackSlow("tab-create", ms, reason);
        DiagUi("tab-open", new { tab = tab.Id, reason, tabs = _tabs.Count });

        HideError();
        HookContextMenu(tab);
        return tab;
    }

    /// <summary>
    /// A tab for the engine to hand a target="_blank" or window.open() page to. Selected
    /// straight away, because that is what clicking a link that opens a tab does — and
    /// deliberately not navigated here.
    /// </summary>
    private async Task<CoreWebView2?> OpenBlankTabAsync()
    {
        BrowserTab? tab = await CreateTabAsync(select: true, reason: "link opened a new tab");
        if (tab is null)
        {
            return null;
        }

        UpdateChrome();
        return tab.View.CoreWebView2;
    }

    /// <param name="focus">For a Start Page: a section to open it scrolled to (see BuildStartPageHtml).</param>
    private async Task OpenNewTabAsync(string? url, string? focus = null)
    {
        BrowserTab? tab = await CreateTabAsync(select: true, reason: url is null ? "new tab" : "open in new tab");
        if (tab is null)
        {
            return;
        }

        if (url is null)
        {
            if (tab.View.CoreWebView2 is CoreWebView2 startCore)
            {
                startCore.WebMessageReceived += StartPage_WebMessageReceived;
            }

            tab.NavigateToHtml(BuildStartPageHtml(focus), "Start Page");
        }
        else
        {
            tab.Navigate(url);
        }

        UpdateChrome();
        tab.View.Focus(FocusState.Programmatic);
    }

    /// <param name="section">
    /// Which pane to land on. The app menu's "About Aviary" used to open Settings and
    /// leave you looking at Get started, which is not what the menu item said.
    /// </param>
    private void OpenSettingsTab(string? section = null)
    {
        _ = OpenSettingsTabAsync(section);
    }

    private async Task OpenSettingsTabAsync(string? section)
    {
        BrowserTab? tab = await CreateTabAsync(select: true, reason: "settings");
        if (tab is null)
        {
            return;
        }

        if (tab.View.CoreWebView2 is CoreWebView2 core)
        {
            // Settings is the one internal page allowed to talk back to the host. It is
            // trusted only because we navigate to it ourselves and never from the network.
            core.WebMessageReceived += Settings_WebMessageReceived;
        }

        tab.NavigateToHtml(BuildSettingsHtml(section), "Settings");
        UpdateChrome();
    }

    /// <summary>
    /// Settings is a static document, so the saved values are injected into it as a JSON
    /// blob just before it is navigated to. Without this the page would render its own
    /// hardcoded defaults and silently disagree with what the browser is actually doing.
    ///
    /// It used to inject a hand-written list of thirteen keys while the message handler
    /// persisted every key the page could send — so nine controls wrote to disk and then
    /// came back showing their default, which reads exactly like a broken switch. The list
    /// is gone. <see cref="AppSettings.Snapshot"/> is everything the store knows, and the
    /// page registers its own keys on load through the schema handshake, so there is no
    /// second place to remember.
    /// </summary>
    private static string BuildSettingsHtml(string? section = null)
    {
        string values = JsonSerializer.Serialize(AppSettings.Snapshot());
        string folder = AppSettings.Get("downloadDir") is { Length: > 0 } dir
            ? dir
            : KnownFolders.Downloads;

        return InternalPages.Settings
            .Replace("/*__AVIARY_SETTINGS__*/", "window.__aviarySettings = " + values + ";"
                   + " window.__aviaryToken = " + JsonSerializer.Serialize(PageToken) + ";"
                   + " window.__aviaryLaunch = " + JsonSerializer.Serialize(WebViewEnvironment.LaunchedWith) + ";")
            // The engine version is known by now: this page is loading in a tab that has started.
            .Replace("/*__AVIARY_BUILD__*/", "window.__aviaryBuild = " + BuildInfo.Json(Diagnostics.EngineVersion) + ";")
            .Replace("/*__AVIARY_SECTION__*/", section is null
                ? string.Empty
                : "window.__aviarySection = " + JsonSerializer.Serialize(section) + ";")
            .Replace("__AV_CHANNEL__", BuildInfo.Channel)
            // Serialised as JSON, never pasted into the page's markup. A Windows path is
            // full of backslashes, and the settings row that shows it is built inside a
            // JavaScript string literal — where C:\Users\... quietly becomes C:Users....
            .Replace("/*__AVIARY_PATHS__*/",
                "window.__aviaryPaths = " + JsonSerializer.Serialize(new { downloads = folder }) + ";")
            // The collection row's status, its folder, and the 16 categories — from the same
            // list manifest.json and summary.md are written from, so the three cannot drift.
            // JSON for the same reason as the paths above: the folder is full of backslashes.
            .Replace("/*__AVIARY_DIAG__*/",
                "window.__aviaryDiag = " + JsonSerializer.Serialize(Diagnostics.DescribeForSettings()) + ";")
            // The filter lists' versions and counts, and the last check (Shields, M3).
            .Replace("/*__AVIARY_LISTS__*/",
                "window.__aviaryLists = " + JsonSerializer.Serialize(FilterLists.Describe()) + ";")
            // The search engines, and which of them can suggest (M4) — the same table the
            // address bar searches with, so the list cannot offer an engine it does not know.
            .Replace("/*__AVIARY_ENGINES__*/",
                "window.__aviaryEngines = " + JsonSerializer.Serialize(SearchEngines.Describe()) + ";")
            .Replace("__AV_ICONCACHE__", DescribeIconCache())
            .Replace("__AV_BLOCKCOUNT__",
                TrackerBlocker.BlockedCount == 0
                    ? "Nothing blocked yet this session."
                    : TrackerBlocker.BlockedCount + " request" + (TrackerBlocker.BlockedCount == 1 ? "" : "s") +
                      " blocked so far this session.");
    }

    private static string DescribeIconCache()
    {
        (int count, long bytes) = Favicons.Stats();
        if (count == 0)
        {
            return "No site icons stored yet.";
        }

        string size = bytes >= 1024 * 1024
            ? (bytes / 1024d / 1024d).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB"
            : Math.Max(1, bytes / 1024) + " KB";

        return count + (count == 1 ? " site" : " sites") + " · " + size + ", kept on this machine only.";
    }

    /// <summary>
    /// Safari's real Start Page, with everything it shows injected as one JSON blob.
    ///
    /// The theme block is generated from the LIVE dictionary rather than hardcoded in the
    /// HTML, so this surface follows Classic/Liquid Glass and light/dark exactly as the
    /// XAML does. An HTML page cannot read a XAML ResourceDictionary, and duplicating the
    /// palette in CSS is how the two quietly drift apart.
    /// </summary>
    /// <param name="focus">
    /// A section to show and scroll to on this one page, whatever Customise says — the Shields
    /// popover's "Privacy Report" lands on the report even when its card is switched off.
    /// </param>
    private string BuildStartPageHtml(string? focus = null)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        string html = BuildStartPageHtmlCore(focus);

        // A baseline for M7, which lists Start Page build cost as something to optimise.
        double ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 2);
        DiagPerf("start-page-build", new { ms, bytes = html.Length });
        DiagTrackSlow("start-page-build", ms, html.Length + " characters");
        return html;
    }

    private string BuildStartPageHtmlCore(string? focus)
    {
        List<object> favourites = new();
        HashSet<string> favouriteHosts = new(StringComparer.OrdinalIgnoreCase);

        foreach (Bookmark bookmark in Bookmarks.Items)
        {
            string key = Favicons.KeyFor(bookmark.Url);
            favouriteHosts.Add(BrowserTab.HostOf(bookmark.Url));
            favourites.Add(new
            {
                url = bookmark.Url,
                title = bookmark.Title,
                key,
                hasIcon = Favicons.HasAnyMaster(key),
                mono = bookmark.Monogram,
                hue = PublicSuffix.AccentIndex(BrowserTab.HostOf(bookmark.Url)),
            });
        }

        // Never repeats a site already in Favourites - Safari's own rule, and without it
        // the two rows are mostly the same eight sites twice over.
        List<object> frequent = History.TopSites(10, favouriteHosts)
            .Select(e => (object)new
            {
                url = e.Url,
                title = string.IsNullOrWhiteSpace(e.Title) ? e.Host : e.Title,
                key = e.IconKey,
                hasIcon = Favicons.HasAnyMaster(e.IconKey),
                mono = PublicSuffix.Monogram(e.Host),
                hue = PublicSuffix.AccentIndex(e.Host),
            })
            .ToList();

        List<object> reading = ReadingList.Items.Take(12)
            .Select(r => (object)new
            {
                url = r.Url,
                title = r.Title,
                host = BrowserTab.HostOf(r.Url),
                key = r.IconKey,
                hasIcon = Favicons.HasAnyMaster(r.IconKey),
                age = r.Age,
                unread = r.Unread,
            })
            .ToList();

        // The Collection row: the exhibits catalog.json marks featured (Library.cs). Null when this
        // build carries no library, which also takes the row out of the Customise popover. Its
        // field and settings key keep the name it was built under (menagerie) so saved choices
        // survive the rename.
        object? menagerie = null;
        if (Library.Available)
        {
            (IReadOnlyList<Library.Featured> items, int count) = Library.FeaturedExhibits();
            if (items.Count > 0)
            {
                menagerie = new
                {
                    home = Library.Home,
                    count,
                    items = items.Select(f => new { title = f.Title, url = f.Url, thumb = f.Thumb, accent = f.Accent, note = f.Note, mono = f.Mono }),
                };
            }
        }

        var payload = new
        {
            token = PageToken,
            isPrivate = _private,
            iconBase = Favicons.IconBase,
            now = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            sections = new
            {
                favourites = AppSettings.Get("startFavourites") != "false",
                menagerie = AppSettings.Get("startMenagerie") != "false",
                frequent = AppSettings.Get("startFrequent") != "false",
                reading = AppSettings.Get("startReading") != "false",
                privacy = AppSettings.Get("startPrivacy") != "false",
            },
            favourites,
            menagerie,
            frequent,
            reading,
            privacy = BuildPrivacyPayload(),
            focus,
        };

        return StartPage.Html
            .Replace("/*__AVIARY_START__*/",
                "window.__aviaryStart = " + JsonSerializer.Serialize(payload) + ";")
            .Replace("/*__AVIARY_THEME__*/", BuildThemeCss());
    }

    /// <summary>
    /// The Privacy Report card's data: the seven-day chart always, and the three figures for
    /// whichever range is chosen (Settings → Privacy, or the switch on the card itself).
    /// </summary>
    private static object BuildPrivacyPayload()
    {
        bool allTime = AppSettings.Get("privacyRange") == "All time";
        (long blocked, long bytes, double seconds, string since) = PrivacyReport.Stats(allTime);

        return new
        {
            total = PrivacyReport.WeekTotal(),
            days = PrivacyReport.LastWeek(),
            top = PrivacyReport.TopTracker(),
            range = allTime ? "all" : "week",
            blocked,
            bytes,
            seconds,
            since,
        };
    }

    /// <summary>
    /// Bridges the XAML palette into CSS custom properties, read from whichever look is
    /// merged right now so the page cannot disagree with the chrome around it.
    /// </summary>
    private string BuildThemeCss()
    {
        // Resolved against the theme Aviary is SHOWING. Application.Current.Resources
        // resolves against Windows' theme instead, which rendered the whole Start Page dark
        // inside light chrome whenever Aviary was pinned Light on a dark machine.
        string Hex(string key, string fallback)
        {
            try
            {
                if (ThemeBrush(key) is SolidColorBrush brush && brush.Color.A > 0)
                {
                    Windows.UI.Color c = brush.Color;
                    string a = (c.A / 255.0).ToString("0.###",
                        System.Globalization.CultureInfo.InvariantCulture);
                    return "rgba(" + c.R + "," + c.G + "," + c.B + "," + a + ")";
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "start page: theme colour " + key);
            }

            return fallback;
        }

        return ":root{"
             + "--bg:" + Hex("AvPageBrush", "#FFFFFF") + ";"
             + "--ink:" + Hex("AvLabelBrush", "rgba(0,0,0,.85)") + ";"
             + "--ink-2:" + Hex("AvLabelSecondaryBrush", "rgba(0,0,0,.5)") + ";"
             + "--ink-3:" + Hex("AvLabelTertiaryBrush", "rgba(0,0,0,.32)") + ";"
             + "--well:" + Hex("AvFieldBrush", "rgba(0,0,0,.045)") + ";"
             + "--well-hover:" + Hex("AvHoverBrush", "rgba(0,0,0,.075)") + ";"
             + "--line:" + Hex("AvHairlineBrush", "rgba(0,0,0,.1)") + ";"
             + "--accent:" + Hex("AvAccentBrush", "#007AFF") + ";"
             + "--card:" + Hex("AvChromeBrush", "rgba(255,255,255,.72)") + ";"
             + "--rib:" + Hex("AvSidebarBorderBrush", "rgba(0,0,0,.07)") + ";"
             + "--sky:" + Hex("AvSelectedBrush", "rgba(0,122,255,.05)") + ";"
             + "--tile-radius:"
             + (AppSettings.Get("look") == "Liquid Glass" ? "18px" : "14px") + ";"
             + "}";
    }

    // ══ THE TWO LOOKS ══════════════════════════════════════════════════════

    /// <summary>
    /// Swaps which theme dictionary is merged, then forces the tree to re-resolve against
    /// it.
    ///
    /// Two things make this less simple than it looks. ThemeResource references only
    /// re-evaluate when the element's theme changes, so the theme is nudged and put back.
    /// And StaticResource references — which is what geometry uses — resolve exactly once
    /// at load and never again, so every value that differs between the looks has to be
    /// re-applied by hand afterwards.
    /// </summary>
    private void ApplyLook(string look)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();

        try
        {
            bool glass = look.Equals("Liquid Glass", StringComparison.OrdinalIgnoreCase);
            var merged = Application.Current.Resources.MergedDictionaries;

            if (merged.Count > 0)
            {
                merged.RemoveAt(merged.Count - 1);
            }

            merged.Add(new ResourceDictionary
            {
                Source = new Uri(glass
                    ? "ms-appx:///Themes/LiquidGlass.xaml"
                    : "ms-appx:///Themes/Classic.xaml")
            });

            ElementTheme restore = RootGrid.RequestedTheme;
            RootGrid.RequestedTheme = restore == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
            RootGrid.RequestedTheme = restore;

            ApplyLookGeometry(glass);
            OnEffectiveThemeChanged();

            double ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1);
            DiagPerf("look-swap", new { look, ms, tabs = _tabs.Count });
            DiagTrackSlow("look-swap", ms, look);
        }
        catch (Exception ex)
        {
            // A failed look swap should leave the browser usable in whatever it is wearing.
            Diagnostics.Swallowed(ex, "window: apply look " + look);
        }
    }

    private void ApplyLookGeometry(bool glass)
    {
        // Liquid Glass floats: nothing touches the window edge, and the visible gap is
        // where Mica finally shows through. Classic is welded to the top at y=0.
        Thickness toolbarMargin = glass ? new Thickness(12, 8, 12, 0) : new Thickness(0);
        Thickness stripMargin = glass ? new Thickness(12, 8, 12, 0) : new Thickness(0);

        ToolbarRow.Margin = toolbarMargin;
        ToolbarRow.CornerRadius = new CornerRadius(glass ? 24 : 0);
        ToolbarRow.Height = 48;

        TabStripRow.Margin = stripMargin;
        TabStripRow.CornerRadius = new CornerRadius(glass ? 20 : 0);
        TabStripRow.BorderThickness = new Thickness(0, 0, 0, glass ? 0 : 1);

        // The content sheet is inset and rounded under Liquid Glass, flush under Classic.
        // The content sheet's inset depends on whether the sidebar is beside it, so it is
        // owned by ApplyContentInsets rather than set here.
        ApplyContentInsets();
        ContentHost.CornerRadius = new CornerRadius(glass ? 12 : 0);

        // Every live identity cell, for the same reason the rows above are re-applied:
        // AvMonogramRadius is a StaticResource and will not re-resolve on a look swap.
        FaviconView.ApplyLookGeometryToAll(glass);
        ApplySidebarGeometry(glass);
        RestampSegments();

        // Sheen, rim, edges, shadow and the press spring — Liquid Glass only (GlassCapsule.cs).
        ApplyGlass(glass);
    }

    private void SelectTab(BrowserTab tab)
    {
        if (!_tabs.Contains(tab))
        {
            return;
        }

        // Show the incoming tab BEFORE hiding the outgoing one, or the window flashes
        // empty for a frame between them.
        tab.View.Visibility = Visibility.Visible;

        foreach (BrowserTab other in _tabs)
        {
            if (!ReferenceEquals(other, tab))
            {
                other.View.Visibility = Visibility.Collapsed;
            }
        }

        // A page's own full screen belongs to that page; switching away ends it.
        if (_fullScreenByPage && !ReferenceEquals(_active, tab))
        {
            SetFullScreen(false, byPage: true);
        }

        if (!ReferenceEquals(_active, tab))
        {
            DiagUi("tab-switch", new { tab = tab.Id, index = _tabs.IndexOf(tab), tabs = _tabs.Count });
        }

        _active = tab;
        Diagnostics.TabActive(tab.Id);

        // Gentle memory saving: only the tab you are looking at keeps normal priority.
        foreach (BrowserTab other in _tabs)
        {
            other.SetBackgrounded(!ReferenceEquals(other, tab));
        }

        foreach (KeyValuePair<BrowserTab, ToggleButton> pair in _tabButtons)
        {
            pair.Value.IsChecked = ReferenceEquals(pair.Key, tab);
        }

        UpdateChrome();
    }

    private void CloseTab(BrowserTab tab)
    {
        int index = _tabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        DiagUi("tab-close", new { tab = tab.Id, index, url = tab.Url, pinned = _pinned.Contains(tab), tabs = _tabs.Count });

        // Closing the last tab closes the window, the way every browser behaves.
        if (_tabs.Count == 1)
        {
            Close();
            return;
        }

        _tabs.RemoveAt(index);
        _pinned.Remove(tab);
        ContentHost.Children.Remove(tab.View);
        tab.Dispose();

        if (ReferenceEquals(_active, tab))
        {
            SelectTab(_tabs[Math.Min(index, _tabs.Count - 1)]);
        }

        RebuildTabStrip();
    }

    private void CycleTab(int delta)
    {
        if (_active is null || _tabs.Count < 2)
        {
            return;
        }

        int i = (_tabs.IndexOf(_active) + delta + _tabs.Count) % _tabs.Count;
        SelectTab(_tabs[i]);
    }

    /// <summary>Shortcuts forwarded up from a page that had keyboard focus.</summary>
    private void OnShortcut(string action)
    {
        DiagUi("shortcut", new { action, from = "page" });

        switch (action)
        {
            case "newtab": OpenNewTab(null); break;
            case "closetab": if (_active is not null) { CloseTab(_active); } break;
            case "address": AddressBox.Focus(FocusState.Programmatic); break;
            case "nexttab": CycleTab(1); break;
            case "find": OpenFindBar(); break;
            case "bookmark": AddBookmark(); break;
            case "prevtab": CycleTab(-1); break;
            case "readinglist": AddToReadingList(); break;
            case "history": ShowHistory(); break;
            case "sidebar": ToggleSidebar(null); break;
            case "reload": ReloadOrStop(); break;
            case "reader": ToggleReader(); break;
            case "downloads": DownloadsButton_Click(DownloadsButton, new RoutedEventArgs()); break;
            case "zoomin": Zoom(+1); break;
            case "zoomout": Zoom(-1); break;
            case "zoomreset": Zoom(0); break;
            case "tablast": SelectTabByIndex(_tabs.Count - 1); break;
            case "fullscreen": ToggleFullScreen(); break;
            case "privatewindow": OpenPrivateWindow(); break;
            case "settings": OpenSettingsTab(); break;
            default:
                if (action.StartsWith("tab", StringComparison.Ordinal)
                    && int.TryParse(action.AsSpan(3), out int n))
                {
                    SelectTabByIndex(n - 1);
                }

                break;
        }
    }

    // ══ FIND IN PAGE ═══════════════════════════════════════════════════════

    private void OpenFindBar()
    {
        DiagUi("find-open");
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus(FocusState.Programmatic);
        FindBox.SelectAll();

        if (FindBox.Text.Length > 0)
        {
            _ = RunFindAsync(FindBox.Text);
        }
    }

    private async void CloseFindBar()
    {
        if (FindBar.Visibility != Visibility.Visible)
        {
            return;
        }

        // The term is logged when Find closes (and on every step), not per keystroke: what was
        // looked for, and whether it was there, is the useful part.
        DiagUi("find-close", new { term = FindBox.Text, matches = _findMatches });

        FindBar.Visibility = Visibility.Collapsed;
        FindCount.Text = string.Empty;
        await FindInPage.StopAsync(_active);
        _active?.View.Focus(FocusState.Programmatic);
    }

    private async System.Threading.Tasks.Task RunFindAsync(string term)
    {
        _findMatches = await FindInPage.StartAsync(_active, term);
        FindCount.Text = term.Length == 0 ? string.Empty
                       : _findMatches == 0 ? "Not found"
                       : "1 of " + _findMatches;

        FindPrev.IsEnabled = _findMatches > 0;
        FindNext.IsEnabled = _findMatches > 0;
    }

    private async void StepFind(int direction)
    {
        if (_findMatches == 0)
        {
            return;
        }

        int index = await FindInPage.StepAsync(_active, direction);
        FindCount.Text = index + " of " + _findMatches;
        DiagUi("find-step", new { term = FindBox.Text, direction, index, matches = _findMatches });
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e) => _ = RunFindAsync(FindBox.Text);

    private void FindBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            bool back = (Microsoft.UI.Input.InputKeyboardSource
                .GetKeyStateForCurrentThread(VirtualKey.Shift) & Windows.UI.Core.CoreVirtualKeyStates.Down)
                == Windows.UI.Core.CoreVirtualKeyStates.Down;
            StepFind(back ? -1 : 1);
        }
        else if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CloseFindBar();
        }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e) => StepFind(1);

    private void FindPrev_Click(object sender, RoutedEventArgs e) => StepFind(-1);

    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFindBar();

    // ══ DOWNLOADS ══════════════════════════════════════════════════════════

    /// <summary>
    /// After every navigation: ask whether the page has an article, and honour a per-site
    /// "always use Reader here" preference once we know one exists.
    /// </summary>
    private async void OnTabNavigated(BrowserTab tab)
    {
        _readerActive = false;
        await ProbeReaderAsync(tab);

        if (!ReferenceEquals(tab, _active) || !_readerAvailable)
        {
            return;
        }

        string host = BrowserTab.HostOf(tab.Url);
        if (host.Length > 0 && SiteSettings.GetBool(host, "autoReader"))
        {
            ToggleReader();
        }
    }

    private void OnNavigationFailed(BrowserTab tab, CoreWebView2WebErrorStatus status)
    {
        if (!ReferenceEquals(tab, _active))
        {
            return;
        }

        string detail = DescribeError(status);
        if (detail.Length == 0)
        {
            return;
        }

        ShowError("This page didn't load", detail);
    }

    private void OnDownloadStarted(CoreWebView2DownloadOperation operation)
    {
        Downloads.Add(new DownloadItem(operation, _private));
        DownloadsButton.Visibility = Visibility.Visible;
    }

    private void DownloadsButton_Click(object sender, RoutedEventArgs e)
    {
        MenuFlyout menu = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        Themed(menu, "downloads");

        if (Downloads.Items.Count == 0)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "No downloads yet", IsEnabled = false });
        }
        else
        {
            foreach (DownloadItem item in Downloads.Items)
            {
                MenuFlyoutItem entry = new()
                {
                    Text = item.FileName,
                    // The status line rides in the tooltip so the menu stays one line per
                    // file rather than becoming a panel of its own this phase.
                    Tag = item
                };
                ToolTipService.SetToolTip(entry, item.Status);
                entry.Click += (_, _) => Downloads.Reveal(item);
                menu.Items.Add(entry);
            }

            menu.Items.Add(new MenuFlyoutSeparator());
            MenuFlyoutItem folder = new() { Text = "Open Downloads folder" };
            folder.Click += (_, _) =>
            {
                DownloadItem? first = Downloads.Items.Count > 0 ? Downloads.Items[0] : null;
                if (first is not null)
                {
                    Downloads.Reveal(first);
                }
            };
            menu.Items.Add(folder);
        }

        menu.ShowAt(DownloadsButton);
    }

    private void OnTabChanged(BrowserTab tab)
    {
        if (_tabButtons.TryGetValue(tab, out ToggleButton? button)
            && button.Tag is TabChrome chrome)
        {
            chrome.Title.Text = tab.Title;
            chrome.Icon.SetSource(tab.Url, tab.Monogram, tab.IsInternalPage);
            ToolTipService.SetToolTip(button, tab.Title);
        }

        if (ReferenceEquals(tab, _active))
        {
            UpdateChrome();
        }
    }

    /// <summary>
    /// Rebuilds the strip's columns so tabs divide its full width. Safari tabs always
    /// share the whole bar; capping them at a Chrome-like fixed width is the fastest way
    /// to lose the silhouette.
    /// </summary>
    private void RebuildTabStrip()
    {
        // BOTH hosts, every time. A UIElement can have exactly one parent, so a button
        // still living in the other host throws ArgumentException on Children.Add — and
        // the message ("value does not fall within the expected range") says nothing.
        TabStrip.Children.Clear();
        TabStrip.ColumnDefinitions.Clear();
        _tabButtons.Clear();

        if (UsingSidebarTabs && _sidebarVisible)
        {
            RefreshSidebar();
            return;
        }

        for (int i = 0; i < _tabs.Count; i++)
        {
            BrowserTab tab = _tabs[i];
            bool pinned = _pinned.Contains(tab);

            // A pinned tab is a fixed narrow cell; the rest still divide what is left,
            // which is what keeps the Safari silhouette rather than Chrome's fixed widths.
            TabStrip.ColumnDefinitions.Add(pinned
                ? new ColumnDefinition { Width = new GridLength(PinnedTabWidth) }
                : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = TabMinWidth });

            ToggleButton button = pinned ? BuildPinnedTabButton(tab) : BuildTabButton(tab);
            Grid.SetColumn(button, i);
            TabStrip.Children.Add(button);
            _tabButtons[tab] = button;
            button.IsChecked = ReferenceEquals(tab, _active);
        }
    }

    /// <summary>
    /// What a tab button's visuals are, held in its Tag.
    ///
    /// OnTabChanged used to reach in as content.Children[1] is TextBlock title. Inserting
    /// any layer breaks that with NO exception — the title simply stops updating. Naming
    /// the parts makes the reach-through impossible.
    /// </summary>
    private sealed class TabChrome
    {
        public required BrowserTab Tab { get; init; }
        public required FaviconView Icon { get; init; }
        public required TextBlock Title { get; init; }
        public required Grid ClosePad { get; init; }
    }

    /// <param name="vertical">
    /// True for a sidebar row: left-aligned title, 30px tall, and the vertical drag
    /// handlers. The content graph is otherwise identical, which is the whole reason the
    /// sidebar can reuse this rather than growing a second tab renderer to keep in step.
    /// </param>
    private ToggleButton BuildTabButton(BrowserTab tab, bool vertical = false)
    {
        // The favicon slot. Favicon and close occupy the SAME 16px cell and cross-fade
        // through each other on hover — Safari puts close at the tab's leading edge, in
        // the favicon's own box, and that swap is its most-copied micro-interaction.
        FaviconView icon = new(16, RootGrid.Resources);
        icon.SetSource(tab.Url, tab.Monogram, tab.IsInternalPage);

        // A Path's stroke is hit-testable only where the ink is, so a click landing between
        // the arms of the X would fall through and SELECT the tab instead of closing it.
        // The transparent background gives it a solid hit area — a null background is not
        // hit-testable, Transparent is.
        Microsoft.UI.Xaml.Shapes.Path closeGlyph = new()
        {
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
                typeof(Geometry), "M4.4,4.4 L11.6,11.6 M11.6,4.4 L4.4,11.6"),
            Width = 16,
            Height = 16,
        };

        if (RootGrid.Resources.TryGetValue("AvTabGlyphStyle", out object? glyphStyle)
            && glyphStyle is Style gs)
        {
            closeGlyph.Style = gs;
        }

        Grid close = new()
        {
            Width = 16,
            Height = 16,
            Opacity = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
        };
        close.Children.Add(closeGlyph);

        Grid slot = new() { Width = 16, Height = 16 };
        slot.Children.Add(icon);

        // Topmost, and the icon below is IsHitTestVisible=false — an Opacity-0 element is
        // still hit-testable in WinUI, so otherwise the icon would steal the press.
        slot.Children.Add(close);

        TextBlock title = new()
        {
            Text = tab.Title,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            TextAlignment = vertical ? TextAlignment.Left : TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };

        Grid content = new();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(slot, 0);
        Grid.SetColumn(title, 1);
        content.Children.Add(slot);
        content.Children.Add(title);

        ToggleButton button = new()
        {
            Style = (Style)RootGrid.Resources["AvTabButton"],
            Content = content
        };

        if (vertical)
        {
            button.Height = 30;
            button.CornerRadius = Res<CornerRadius>("AvSidebarRowRadius", new CornerRadius(6));
        }
        ToolTipService.SetToolTip(button, tab.Title);

        button.PointerEntered += (_, _) => { FadeTo(icon, 0, 150); FadeTo(close, 1, 150); };
        button.PointerExited += (_, _) => { FadeTo(icon, 1, 120); FadeTo(close, 0, 120); };

        button.Click += (_, _) =>
        {
            // A ToggleButton would otherwise let you un-check the active tab.
            button.IsChecked = true;
            SelectTab(tab);
        };

        // Drag to reorder.
        //
        // These MUST be AddHandler with handledEventsToo:true. ButtonBase marks pointer
        // events as handled while driving its own press visuals, so an ordinary +=
        // subscription never sees them and the drag silently does nothing — which is
        // exactly what happened the first time.
        //
        // The buttons are MOVED between grid columns rather than the strip being rebuilt,
        // because rebuilding destroys the element holding the pointer capture and the drag
        // would die on its first swap.
        button.Tag = new TabChrome { Tab = tab, Icon = icon, Title = title, ClosePad = close };

        PointerEventHandler pressed = vertical ? VTab_PointerPressed : Tab_PointerPressed;
        PointerEventHandler moved = vertical ? VTab_PointerMoved : Tab_PointerMoved;
        PointerEventHandler released = vertical ? VTab_PointerReleased : Tab_PointerReleased;

        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(pressed), true);
        button.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(moved), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(released), true);
        button.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(released), true);

        button.RightTapped += (_, e) =>
        {
            e.Handled = true;
            ShowTabMenu(tab, button);
        };

        // Clicking the close pad closes rather than selects.
        close.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            CloseTab(tab);
        };

        return button;
    }

    private void Tab_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not TabChrome chrome)
        {
            return;
        }

        BrowserTab tab = chrome.Tab;
        _dragTab = tab;
        _dragFrom = _tabs.IndexOf(tab);
        _dragStartX = e.GetCurrentPoint(TabStrip).Position.X;
        _dragging = false;
        button.CapturePointer(e.Pointer);
    }

    /// <summary>Where a dragged tab started, so diagnostics can record from → to.</summary>
    private int _dragFrom = -1;

    private void Tab_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ToggleButton button
            || button.Tag is not TabChrome chrome
            || !ReferenceEquals(_dragTab, chrome.Tab))
        {
            return;
        }

        BrowserTab tab = chrome.Tab;

        Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(TabStrip);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        double x = point.Position.X;

        // A few pixels of slop, so a click with a shaky hand is still a click.
        if (!_dragging && Math.Abs(x - _dragStartX) < 6)
        {
            return;
        }

        _dragging = true;
        button.Opacity = 0.75;
        ReorderDuringDrag(tab, x);
    }

    private void Tab_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ToggleButton button)
        {
            button.ReleasePointerCapture(e.Pointer);
            button.Opacity = 1;
        }

        BrowserTab? dragged = _dragTab;
        _dragTab = null;

        if (_dragging)
        {
            _dragging = false;

            if (dragged is not null)
            {
                DiagUi("tab-reorder", new { tab = dragged.Id, from = _dragFrom, to = _tabs.IndexOf(dragged), strip = "top" });
            }

            RebuildTabStrip();
        }
    }

    private void ReorderDuringDrag(BrowserTab tab, double x)
    {
        if (_tabs.Count < 2 || TabStrip.ActualWidth <= 0)
        {
            return;
        }

        // Pinned cells are a fixed width, so a uniform slot would drift by exactly the
        // difference on every pinned tab present.
        int pinned = PinnedCount();
        double pinnedWidth = pinned * PinnedTabWidth;
        int loose = _tabs.Count - pinned;
        double slot = loose > 0 ? Math.Max(1, (TabStrip.ActualWidth - pinnedWidth) / loose) : 1;

        int target = _pinned.Contains(tab)
            ? Math.Clamp((int)(x / Math.Max(1, PinnedTabWidth)), 0, Math.Max(0, pinned - 1))
            : Math.Clamp(pinned + (int)((x - pinnedWidth) / slot), pinned, _tabs.Count - 1);

        int current = _tabs.IndexOf(tab);

        if (current < 0 || target == current)
        {
            return;
        }

        _tabs.RemoveAt(current);
        _tabs.Insert(target, tab);

        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabButtons.TryGetValue(_tabs[i], out ToggleButton? b))
            {
                Grid.SetColumn(b, i);
            }
        }
    }

    /// <summary>
    /// One storyboard per element, stopped before it is reused.
    ///
    /// A WinUI Storyboard animating Opacity HOLDS its final value, and a later plain
    /// assignment to Opacity is then silently ignored. The previous helper created a fresh,
    /// unowned storyboard on every hover and never stopped the last one, so a fast
    /// in-out-in left two of them fighting and an icon could stick at zero.
    /// </summary>
    private static readonly Dictionary<UIElement, Storyboard> Fades = new();

    private static void FadeTo(UIElement element, double to, int milliseconds)
    {
        if (Fades.TryGetValue(element, out Storyboard? running))
        {
            try { running.Stop(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "window: stop fade"); }
        }

        DoubleAnimation animation = new()
        {
            To = to,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");

        Storyboard storyboard = new();
        storyboard.Children.Add(animation);
        Fades[element] = storyboard;
        storyboard.Begin();
    }

    // ══ CHROME ═════════════════════════════════════════════════════════════

    private void UpdateChrome()
    {
        BrowserTab? tab = _active;

        BackButton.IsEnabled = tab?.CanGoBack ?? false;
        ForwardButton.IsEnabled = tab?.CanGoForward ?? false;
        ReloadButton.IsEnabled = tab?.IsReady ?? false;

        bool loading = tab?.IsLoading ?? false;
        ReloadGlyph.Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(
            typeof(Geometry), loading ? StopGeometry : ReloadGeometry);
        ToolTipService.SetToolTip(ReloadButton, loading ? "Stop (Esc)" : "Reload (Ctrl+R)");
        LockGlyph.Visibility = (tab?.IsSecure ?? false) ? Visibility.Visible : Visibility.Collapsed;
        UpdateShieldsButton();

        if (!_addressFocused)
        {
            ShowIdleAddress();
        }

        string app = _private ? "Aviary Private" : "Aviary";
        Title = tab is null || string.IsNullOrWhiteSpace(tab.Title) || tab.Title == "Aviary"
            ? app
            : tab.Title + " — " + app;
    }

    /// <summary>
    /// Hands every tab the theme's page colour: what Aviary's own pages, and a tab that has shown
    /// nothing yet, sit on. Websites sit on white whatever the theme, as in Chrome and Safari —
    /// each tab decides which of the two applies, and when (BrowserTab.Background.cs, bug 61).
    /// </summary>
    private void ApplyWebViewBackground()
    {
        Windows.UI.Color page = ThemePageColour();
        foreach (BrowserTab tab in _tabs)
        {
            tab.SetThemePageColour(page);
        }
    }

    /// <summary>
    /// The shown theme's page colour, not Windows' — resolved against Windows', a pinned-Light
    /// window flashed dark (bug 25).
    /// </summary>
    private Windows.UI.Color ThemePageColour()
    {
        try
        {
            if (ThemeBrush("AvPageBrush") is SolidColorBrush brush)
            {
                return brush.Color;
            }
        }
        catch (Exception ex)
        {
            // A wrong colour under a page is survivable; a crash is not.
            Diagnostics.Swallowed(ex, "window: WebView background colour");
        }

        return Microsoft.UI.Colors.White;
    }

    private void ShowIdleAddress()
    {
        AddressBox.TextAlignment = TextAlignment.Center;
        string url = _active?.Url ?? string.Empty;
        AddressBox.Text = _showFullUrl ? url : BrowserTab.HostOf(url);
    }

    private void AddressBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _addressFocused = true;
        FieldFocus.Opacity = 1;
        AddressBox.TextAlignment = TextAlignment.Left;

        string url = _active?.Url ?? string.Empty;
        AddressBox.Text = BrowserTab.HostOf(url).Length == 0 ? string.Empty : url;

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
        HideSuggestions();
        AddressBox.Select(0, 0);
        ShowIdleAddress();
    }

    private void AddressBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        // The suggestion list gets first refusal on Down, Up, Escape and — only when a row
        // is actually highlighted — Enter.
        if (HandleSuggestionKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;
        HideSuggestions();

        if (Navigate(AddressBox.Text))
        {
            _active?.View.Focus(FocusState.Programmatic);
        }
    }

    private bool Navigate(string input)
    {
        if (_active is not { IsReady: true } tab)
        {
            return false;
        }

        string text = (input ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        string target;
        bool search = false;

        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            target = absolute.ToString();
        }
        else if (!text.Contains(' ') && (text.Contains('.') || LooksLocal(text)))
        {
            // https by default, but NOT for localhost and friends: a local dev server is
            // almost never running TLS, and forcing https there means the address bar
            // simply cannot reach it. Chrome makes the same exception.
            target = (LooksLocal(text) ? "http://" : "https://") + text;
        }
        else
        {
            target = SearchPrefix + Uri.EscapeDataString(text);
            search = true;
        }

        // Category 2. Only what was submitted — never the keystrokes on the way there.
        if (search)
        {
            DiagBrowse("search", "search: " + text, new
            {
                tab = tab.Id,
                query = text,
                engine = AppSettings.Get("searchEngine"),
                target,
                via = "address bar",
            });
        }
        else
        {
            DiagBrowse("typed", text, new { tab = tab.Id, typed = text, target });
        }

        HideError();
        tab.Navigate(target);
        return true;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        DiagUi("back");
        if (_active is { CanGoBack: true } tab)
        {
            tab.View.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        DiagUi("forward");
        if (_active is { CanGoForward: true } tab)
        {
            tab.View.GoForward();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e) => ReloadOrStop();

    /// <summary>
    /// One button, two jobs — which is what Safari's is.
    ///
    /// A reload glyph that stays a reload glyph while the page is loading gives you no way
    /// to abandon a slow page except closing the tab.
    /// </summary>
    private void ReloadOrStop()
    {
        if (_active is not { IsReady: true } tab)
        {
            return;
        }

        if (tab.IsLoading)
        {
            DiagUi("stop", new { tab = tab.Id });
            tab.View.CoreWebView2?.Stop();
        }
        else
        {
            DiagUi("reload", new { tab = tab.Id });
            tab.View.Reload();
        }
    }

    // The circular-arrow reload glyph, and the ✕ it becomes mid-load.
    private const string ReloadGeometry = "M11.9,6.5 A4.4,4.4 0 1 1 10.5,3.3 M11.6,1.4 V4.6 H8.4";
    private const string StopGeometry = "M3.8,3.8 L9.2,9.2 M9.2,3.8 L3.8,9.2";

    private static readonly double[] ZoomLevels = { 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200 };

    /// <summary>Ctrl+plus, Ctrl+minus, Ctrl+0. Direction 0 resets to 100%.</summary>
    private void Zoom(int direction)
    {
        double current = 100;
        string saved = AppSettings.Get("zoom").TrimEnd('%');
        _ = double.TryParse(saved, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out current);

        double next;
        if (direction == 0)
        {
            next = 100;
        }
        else
        {
            int index = Array.FindIndex(ZoomLevels, z => Math.Abs(z - current) < 0.5);
            if (index < 0)
            {
                index = Array.FindIndex(ZoomLevels, z => z >= current);
                if (index < 0) { index = ZoomLevels.Length - 1; }
            }

            next = ZoomLevels[Math.Clamp(index + direction, 0, ZoomLevels.Length - 1)];
        }

        string text = next.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";
        DiagUi("zoom", new { from = current, to = next });
        AppSettings.Set("zoom", text);
        PushToSettingsPage("zoom", text);

        foreach (BrowserTab t in _tabs)
        {
            t.ApplyZoom();
        }
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        DiagUi("home");
        if (_active?.View.CoreWebView2 is CoreWebView2 core)
        {
            // -= first: Home can be pressed repeatedly, and a second subscription would
            // act on every message twice.
            core.WebMessageReceived -= StartPage_WebMessageReceived;
            core.WebMessageReceived += StartPage_WebMessageReceived;
        }

        _active?.NavigateToHtml(BuildStartPageHtml(), "Start Page");
        UpdateChrome();
    }

    /// <summary>
    /// A per-launch secret that Aviary writes into its own pages (Settings and the Start Page)
    /// and that every message from them carries back.
    ///
    /// The Settings and Start Page handlers stay attached to their tab's engine for the life of
    /// the tab, so once the tab moves on to a website, that website's postMessage calls arrive
    /// at them too. Until M1's review, any site loaded in a tab that had shown Settings could
    /// post {type:'setting'} and change any preference — switch the blocker off, plant pinned
    /// tabs, switch diagnostics collection on — and every new tab carried the Start Page
    /// handler. A website cannot learn this token: Aviary's pages have an opaque origin, so no
    /// site's script can read them. (The first attempt checked for a data: source and broke
    /// Settings outright — NavigateToString documents report about:blank, which a hostile page
    /// can also end up as. Found by running it.)
    /// </summary>
    internal static readonly string PageToken =
        Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static bool FromAviaryPage(JsonElement root) =>
        root.TryGetProperty("token", out JsonElement token)
        && token.ValueKind == JsonValueKind.String
        && string.Equals(token.GetString(), PageToken, StringComparison.Ordinal);

    /// <summary>A website posted one of an internal page's own message types. Refused, and recorded.</summary>
    private void RefuseMessage(string handler, string type, CoreWebView2WebMessageReceivedEventArgs args)
    {
        string source;
        try { source = args.Source; }
        catch (Exception ex) { source = string.Empty; Diagnostics.Swallowed(ex, "refused message: source"); }

        // Recorded even from a private window — it is an attack on Aviary, not browsing — but
        // without the address it came from.
        Diagnostics.Anomaly("untrusted-message", "a website posted a " + handler + " message (" + type + ")",
            new { handler, type, source = _private ? null : source });
    }

    /// <summary>The Start Page's own messages: navigation, section toggles, tile menus.</summary>
    private void StartPage_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(args.TryGetWebMessageAsString());
            JsonElement root = doc.RootElement;
            string type = root.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? "" : "";
            bool trusted = FromAviaryPage(root);

            // Every page's own injected scripts (shortcuts, icons, page errors) post on this
            // same event; only the Start Page's own types are refused, and only those recorded.
            if (!trusted)
            {
                if (type is "navigate" or "startSection" or "tileMenu" or "privacyRange")
                {
                    RefuseMessage("Start Page", type, args);
                }

                return;
            }

            switch (type)
            {
                case "navigate":
                    if (root.TryGetProperty("url", out JsonElement u)
                        && u.GetString() is { Length: > 0 } target)
                    {
                        DiagFeature("start-page-open", new { url = target });
                        _active?.Navigate(target);
                        UpdateChrome();
                    }

                    break;

                case "startSection":
                {
                    if (!root.TryGetProperty("key", out JsonElement k))
                    {
                        return;
                    }

                    string name = k.GetString() ?? string.Empty;
                    if (name.Length == 0)
                    {
                        return;
                    }

                    bool on = root.TryGetProperty("value", out JsonElement v)
                              && v.ValueKind == JsonValueKind.True;

                    AppSettings.Set(
                        "start" + char.ToUpperInvariant(name[0]) + name.Substring(1),
                        on ? "true" : "false");
                    break;
                }

                case "privacyRange":
                {
                    string range = root.TryGetProperty("value", out JsonElement rv) && rv.GetString() == "all"
                        ? "All time"
                        : "Last 7 days";

                    DiagFeature("privacy-report-range", new { range, from = "start page" });
                    AppSettings.Set("privacyRange", range);
                    PushToSettingsPage("privacyRange", range);
                    RefreshStartPages();
                    break;
                }

                case "tileMenu":
                {
                    if (!root.TryGetProperty("url", out JsonElement mu)
                        || mu.GetString() is not { Length: > 0 } tile)
                    {
                        return;
                    }

                    MenuFlyout menu = new();
                    Themed(menu, "start page tile");
                    MenuFlyoutItem open = new() { Text = "Open in New Tab" };
                    open.Click += (_, _) => OpenNewTab(tile);
                    menu.Items.Add(open);

                    if (Bookmarks.Contains(tile))
                    {
                        menu.Items.Add(new MenuFlyoutSeparator());
                        MenuFlyoutItem remove = new() { Text = "Remove from Favourites" };
                        remove.Click += (_, _) =>
                        {
                            Bookmark? found = Bookmarks.Items.Find(
                                b => string.Equals(b.Url, tile, StringComparison.OrdinalIgnoreCase));
                            if (found is not null)
                            {
                                Bookmarks.Remove(found);
                                RefreshStartPages();
                            }
                        };
                        menu.Items.Add(remove);
                    }

                    menu.ShowAt(ContentHost);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // A malformed message from our own page is a bug to find in testing.
            Diagnostics.Swallowed(ex, "start page: message");
        }
    }

    /// <summary>Re-renders any open Start Page after its data changed underneath it.</summary>
    private void RefreshStartPages()
    {
        foreach (BrowserTab tab in _tabs)
        {
            if (tab.IsInternalPage && tab.Title == "Start Page")
            {
                tab.NavigateToHtml(BuildStartPageHtml(), "Start Page");
            }
        }
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        DiagUi("new-tab-button");
        OpenNewTab(null);
    }

    // ══ THE APP MENU ═══════════════════════════════════════════════════════

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        MenuFlyout menu = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        Themed(menu, "app");

        MenuFlyoutItem newTab = new() { Text = "New Tab" };
        newTab.KeyboardAcceleratorTextOverride = "Ctrl+T";
        newTab.Click += (_, _) => OpenNewTab(null);
        menu.Items.Add(newTab);

        MenuFlyoutItem privateWindow = new() { Text = "New Private Window" };
        privateWindow.KeyboardAcceleratorTextOverride = "Ctrl+Shift+N";
        privateWindow.Click += (_, _) => OpenPrivateWindow();
        menu.Items.Add(privateWindow);

        MenuFlyoutItem closeTab = new() { Text = "Close Tab" };
        closeTab.KeyboardAcceleratorTextOverride = "Ctrl+W";
        closeTab.Click += (_, _) => { if (_active is not null) CloseTab(_active); };
        menu.Items.Add(closeTab);

        menu.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem find = new() { Text = "Find in Page…" };
        find.KeyboardAcceleratorTextOverride = "Ctrl+F";
        find.Click += (_, _) => OpenFindBar();
        menu.Items.Add(find);

        MenuFlyoutItem bookmark = new() { Text = "Add Bookmark" };
        bookmark.KeyboardAcceleratorTextOverride = "Ctrl+D";
        bookmark.Click += (_, _) => AddBookmark();
        menu.Items.Add(bookmark);

        MenuFlyoutItem fullScreen = new() { Text = _fullScreen ? "Exit Full Screen" : "Enter Full Screen" };
        fullScreen.KeyboardAcceleratorTextOverride = "F11";
        fullScreen.Click += (_, _) => ToggleFullScreen();
        menu.Items.Add(fullScreen);

        MenuFlyoutItem downloads = new() { Text = "Downloads" };
        downloads.Click += (_, _) => DownloadsButton_Click(DownloadsButton, new RoutedEventArgs());
        menu.Items.Add(downloads);

        // Grant's own projects, bundled and offline (Library.cs). Absent from a build without them.
        if (Library.Available)
        {
            MenuFlyoutItem collection = new() { Text = Library.Name };
            collection.Click += (_, _) =>
            {
                DiagUi("collection-open", new { from = "app menu" });
                OpenNewTab(Library.Home);
            };
            menu.Items.Add(collection);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutSubItem help = new() { Text = "Help" };
        MenuFlyoutItem whatsNew = new() { Text = "What’s New in Aviary" };
        whatsNew.Click += (_, _) => OpenSettingsTab("updates");
        help.Items.Add(whatsNew);
        MenuFlyoutItem about = new() { Text = "About Aviary" };
        about.Click += (_, _) => OpenSettingsTab("about");
        help.Items.Add(about);
        menu.Items.Add(help);

        MenuFlyoutItem settings = new() { Text = "Settings" };
        settings.KeyboardAcceleratorTextOverride = "Ctrl+,";
        settings.Click += (_, _) => OpenSettingsTab();
        menu.Items.Add(settings);

        menu.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem exit = new() { Text = "Exit" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(exit);

        menu.ShowAt(MenuButton);
    }

    /// <summary>
    /// Settings talks back as {type, …}. Dispatch is on TYPE, not on whether a "key"
    /// happens to be present — that was how a button ended up being persisted as if it
    /// were a preference, writing a junk reset:true key that nothing ever read.
    ///
    /// Anything unrecognised is ignored rather than trusted — and so is anything, recognised
    /// or not, that did not come from the Settings page itself (see <see cref="FromAviaryPage"/>:
    /// the handler outlives the page when the tab is navigated to a website).
    /// </summary>
    private void Settings_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(args.TryGetWebMessageAsString());
            JsonElement root = doc.RootElement;
            string type = root.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? "" : "";
            bool trusted = FromAviaryPage(root);

            if (!trusted)
            {
                if (type is "setting" or "command" or "schema" or "rendered")
                {
                    RefuseMessage("Settings", type, args);
                }

                return;
            }

            switch (type)
            {
                case "schema":
                {
                    // The page's own literal defaults for every keyed control it drew.
                    // After this the store knows every key the page can render, which is
                    // what makes "saves but comes back as the default" impossible.
                    if (!root.TryGetProperty("values", out JsonElement seedElement))
                    {
                        return;
                    }

                    Dictionary<string, string> seed = new(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty p in seedElement.EnumerateObject())
                    {
                        seed[p.Name] = AsString(p.Value);
                    }

                    AppSettings.SeedDefaults(seed);
                    return;
                }

                case "rendered":
                {
                    // What each control actually DREW, for the round-trip check. Only read
                    // while collecting; the page sends it regardless because it cannot know.
                    if (!Diagnostics.On || !root.TryGetProperty("values", out JsonElement drawn))
                    {
                        return;
                    }

                    Dictionary<string, string> rendered = new(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty p in drawn.EnumerateObject())
                    {
                        rendered[p.Name] = AsString(p.Value);
                    }

                    Diagnostics.SettingsRendered(rendered);
                    return;
                }

                case "setting":
                {
                    if (!root.TryGetProperty("key", out JsonElement k))
                    {
                        return;
                    }

                    string settingKey = k.GetString() ?? string.Empty;
                    JsonElement settingValue = root.TryGetProperty("value", out JsonElement sv) ? sv : default;

                    // Persist first, so a setting survives even if applying it throws.
                    AppSettings.Set(settingKey, AsString(settingValue));
                    ApplySetting(settingKey, settingValue);
                    return;
                }

                case "command":
                {
                    string name = root.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                    string? arg = root.TryGetProperty("arg", out JsonElement a) && a.ValueKind == JsonValueKind.String
                        ? a.GetString()
                        : null;
                    DiagUi("settings-command", new { command = name });
                    _ = RunSettingsCommandAsync(name, sender, arg);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            // A malformed message from our own page is a bug to find in testing, not a
            // reason to take the browser down.
            Diagnostics.Swallowed(ex, "settings page: message");
        }

        static string AsString(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.String => value.GetString() ?? string.Empty,
            _ => value.ToString()
        };
    }

    /// <summary>Makes a saved setting take effect on the running browser.</summary>
    private void ApplySetting(string key, JsonElement value)
    {
        try
        {
            switch (key)
            {
                case "look":
                    ApplyLook(value.GetString() ?? "Classic");
                    break;

                case "blockTrackers":
                    TrackerBlocker.Enabled = value.ValueKind == JsonValueKind.True;
                    Diagnostics.Shields(TrackerBlocker.Enabled ? "blocking-on" : "blocking-off", new { scope = "everywhere" });

                    // Element hiding follows the switch too: every tab's page script goes (or
                    // comes back) for the pages it loads next.
                    Shields.Refresh();
                    UpdateShieldsButton();
                    break;

                case "blockMode":
                    Diagnostics.Shields("mode", new { mode = value.GetString() });
                    UpdateShieldsButton();
                    break;

                case "listAds":
                case "listTrackers":
                    // A list on or off: the engine is rebuilt from the lists that are on, off this
                    // thread, and reaches every tab by itself (FilterLists.EngineChanged).
                    Diagnostics.Shields("list", new { list = key == "listAds" ? "EasyList" : "EasyPrivacy", on = value.ValueKind == JsonValueKind.True });
                    _ = ReloadListsAsync();
                    break;

                case Diagnostics.SettingKey:
                    Diagnostics.SetEnabled(value.ValueKind == JsonValueKind.True);
                    _ = PushDiagnosticsStatusAsync();
                    break;

                case "bookmarksBar":
                    RefreshBookmarksBar();
                    break;

                case "memorySaver":
                    foreach (BrowserTab t in _tabs)
                    {
                        t.SetBackgrounded(!ReferenceEquals(t, _active));
                    }
                    break;

                case "theme":
                    RootGrid.RequestedTheme = (value.GetString() ?? "System") switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default
                    };
                    ApplyWebViewBackground();
                    foreach (BrowserTab t2 in _tabs)
                    {
                        t2.ApplyColorScheme();
                    }
                    break;

                case "searchEngine":
                    SearchEngineChanged(value.GetString());
                    break;

                case "suggest":
                    // Either way, nothing the engine said before is shown again; off also stops
                    // whatever was about to be sent (MainWindow.SearchSuggest.cs).
                    foreach (MainWindow window in OpenWindows)
                    {
                        window.ForgetEngineSuggestions();
                    }
                    break;

                case "homeButton":
                    HomeButton.Visibility = value.ValueKind == JsonValueKind.True
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    break;

                case "tabLayout":
                    // Choosing Sidebar has to OPEN the sidebar, or the tabs vanish.
                    if (value.GetString() == "Sidebar")
                    {
                        ToggleSidebar(true);
                    }

                    ApplyTabLayout();
                    break;

                case "zoom":
                    foreach (BrowserTab zt in _tabs)
                    {
                        zt.ApplyZoom();
                    }

                    break;

                case "siteIcons":
                    // Turning it off has to be visible immediately, or it reads as inert.
                    Favicons.InvalidateDecoded();
                    RebuildTabStrip();
                    RefreshBookmarksBar();
                    break;

                case "privacyRange":
                    RefreshStartPages();
                    break;

                case "safariUa":
                    foreach (BrowserTab ut in _tabs)
                    {
                        _ = ut.ApplyUserAgentAsync();
                    }

                    break;

                case "savePasswords":
                case "formAutofill":
                    foreach (BrowserTab at in _tabs)
                    {
                        at.ApplyAutofill();
                    }

                    break;

                case "trimReferrer":
                    foreach (BrowserTab rt in _tabs)
                    {
                        _ = rt.ApplyReferrerScriptAsync();
                    }

                    break;

                case "fullUrl":
                    _showFullUrl = value.ValueKind == JsonValueKind.True;
                    if (!_addressFocused)
                    {
                        ShowIdleAddress();
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            // Applying a setting must never take the browser down; it is already saved.
            Diagnostics.Swallowed(ex, "settings: apply " + key);
        }
    }

    /// <summary>
    /// Refreshes the "Collected data" line in any open Settings page. The switch has already
    /// moved by itself; the words under it have to follow or they describe the past.
    /// </summary>
    private async Task PushDiagnosticsStatusAsync()
    {
        // The folder is created on the writer thread; wait for it (off this thread) so the
        // line says "3 KB in 5 files" rather than "starting".
        await Task.Run(() => Diagnostics.Flush(2000));

        string payload = JsonSerializer.Serialize(Diagnostics.DescribeForSettings());

        foreach (BrowserTab tab in _tabs)
        {
            if (tab.View.CoreWebView2 is not CoreWebView2 core || tab.Title != "Settings" || !tab.IsInternalPage)
            {
                continue;
            }

            try
            {
                _ = core.ExecuteScriptAsync("window.__aviaryDiagStatus && window.__aviaryDiagStatus(" + payload + ")");
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "settings: push collection status");
            }
        }
    }

    // ══ SETTINGS COMMANDS ══════════════════════════════════════════════════
    //
    // A command is a thing the page ASKS the host to do. It is not a preference, has no
    // value, and is never written to settings.json.

    private async Task RunSettingsCommandAsync(string name, CoreWebView2 page, string? arg = null)
    {
        try
        {
            switch (name)
            {
                case "reset":
                {
                    if (!await ConfirmAsync(
                            "Reset all settings?",
                            "Every preference on this page goes back to its default. Your history, "
                          + "bookmarks and downloaded files are not touched.",
                            "Reset"))
                    {
                        return;
                    }

                    AppSettings.Reset();
                    RestorePreferences(resubscribe: false);

                    foreach (BrowserTab t in _tabs)
                    {
                        t.ApplyColorScheme();
                    }

                    // Re-rendered through NavigateToHtml, never CoreWebView2.NavigateToString:
                    // the latter skips _internalTitle and the base64 data: URI lands in the
                    // window title again — bug 5 in the handoff, straight back.
                    TabFor(page)?.NavigateToHtml(BuildSettingsHtml(), "Settings");
                    break;
                }

                case "clearData":
                {
                    // Diagnostics keeps its own copy of every address visited, so clearing
                    // history while leaving that would clear the list and keep the trail.
                    // Offered, not forced: it is also the record of what went wrong.
                    string? alsoOffer = null;
                    if (Diagnostics.HasData)
                    {
                        (int _, long bytes) = Diagnostics.Stats();
                        alsoOffer = "Also delete the improvement data Aviary has collected ("
                                  + Math.Max(1, bytes / 1024) + " KB)";
                    }

                    (bool clear, bool alsoDiagnostics) = await ConfirmWithOptionAsync(
                            "Clear browsing data?",
                            "History, cookies, cached files and site data for this profile are "
                          + "removed. Your bookmarks, reading list and downloaded files are not "
                          + "touched.",
                            "Clear",
                            alsoOffer);

                    if (!clear)
                    {
                        return;
                    }

                    if (alsoDiagnostics)
                    {
                        await Task.Run(Diagnostics.DeleteAll);
                    }

                    // After the delete, so that if collection is on, the fresh folder's first
                    // line says what just happened to the old one.
                    DiagUi("clear-browsing-data", new { alsoDiagnostics });

                    History.Clear();
                    PrivacyReport.Clear();
                    SiteSettings.Clear();

                    // Site settings include Shields: every site is back to Shields up.
                    Shields.Refresh();

                    // The engine's own store — cookies, cache, local storage, service
                    // workers. Without this, "cleared" would mean the list only.
                    try
                    {
                        if (_active?.View.CoreWebView2?.Profile is { } profile)
                        {
                            await profile.ClearBrowsingDataAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        // An older runtime may not expose it; the local stores are still gone.
                        Diagnostics.Swallowed(ex, "clear browsing data: engine store");
                    }

                    RefreshSidebar();
                    RefreshStartPages();
                    TabFor(page)?.NavigateToHtml(BuildSettingsHtml("privacy"), "Settings");
                    break;
                }

                case "privateWindow":
                    OpenPrivateWindow();
                    break;

                case "siteData":
                    await PushSiteDataAsync(page);
                    break;

                case "siteDataRemove":
                    await RemoveSiteDataAsync(page, arg ?? string.Empty);
                    break;

                // ── Shields (M3) ──
                case "listsUpdate":
                {
                    // A new engine reaches every tab by itself (FilterLists.EngineChanged); the
                    // page only needs to hear how it went.
                    await PushListsAsync(page, updating: true);
                    await FilterLists.UpdateAsync("manual");
                    await PushListsAsync(page, updating: false);
                    break;
                }

                case "shieldsSites":
                    await PushShieldsSitesAsync(page);
                    break;

                case "shieldsSiteUp":
                    if (!string.IsNullOrWhiteSpace(arg))
                    {
                        Shields.Set(arg, down: false, isPrivate: _private);
                        AfterShieldsChange();
                    }

                    await PushShieldsSitesAsync(page);
                    break;

                case "clearPasswords":
                {
                    if (!await ConfirmAsync(
                            "Remove saved passwords?",
                            "Every password Aviary has saved in this profile is deleted. Sites will ask you to "
                          + "sign in again the next time you visit.",
                            "Remove"))
                    {
                        return;
                    }

                    try
                    {
                        if (_active?.View.CoreWebView2?.Profile is { } profile)
                        {
                            await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.PasswordAutosave);
                        }
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Swallowed(ex, "settings: clear saved passwords");
                    }

                    break;
                }

                case "clearFormData":
                {
                    try
                    {
                        if (_active?.View.CoreWebView2?.Profile is { } profile)
                        {
                            await profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.GeneralAutofill);
                        }
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Swallowed(ex, "settings: clear form entries");
                    }

                    break;
                }

                case "clearFavicons":
                {
                    Favicons.ClearAll();
                    RebuildTabStrip();
                    RefreshBookmarksBar();

                    // Re-rendered so the "N sites · M KB" line is not left claiming icons
                    // that were just deleted.
                    TabFor(page)?.NavigateToHtml(BuildSettingsHtml("privacy"), "Settings");
                    break;
                }

                case "diagOpen":
                {
                    if (Diagnostics.HasData)
                    {
                        Diagnostics.OpenFolder();
                    }
                    else
                    {
                        // Never a button that silently does nothing.
                        await InformAsync("Nothing collected yet",
                            "The folder is created the moment \"Allow data collection for improvements\" is "
                          + "switched on, and fills as you use Aviary.");
                    }

                    break;
                }

                case "diagDelete":
                {
                    if (!Diagnostics.HasData)
                    {
                        await InformAsync("Nothing to delete", "Aviary has not collected anything.");
                        break;
                    }

                    if (!await ConfirmAsync(
                            "Delete the collected data?",
                            "Everything in the improvement data folder is deleted — every recorded address, "
                          + "search, error and crash report. "
                          + (Diagnostics.On ? "Collection stays on and starts again from nothing." : ""),
                            "Delete"))
                    {
                        return;
                    }

                    await Task.Run(Diagnostics.DeleteAll);
                    await PushDiagnosticsStatusAsync();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            // A failed command leaves the browser exactly as it was.
            Diagnostics.Swallowed(ex, "settings: command " + name);
        }
    }

    private BrowserTab? TabFor(CoreWebView2 core) =>
        _tabs.Find(t => ReferenceEquals(t.View.CoreWebView2, core));

    /// <summary>
    /// ContentDialog in WinUI 3 desktop throws a COMException without an explicit XamlRoot,
    /// and throws again if a second one opens while the first is up.
    /// </summary>
    private bool _dialogOpen;

    private async Task<bool> ConfirmAsync(string title, string body, string verb)
    {
        if (_dialogOpen)
        {
            return false;
        }

        _dialogOpen = true;
        try
        {
            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme,
                Title = title,
                Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "Cancel",
                SecondaryButtonText = verb,
                // The destructive verb is deliberately NOT the default button, so Enter
                // cancels rather than wipes.
                DefaultButton = ContentDialogButton.Primary
            };

            return await dialog.ShowAsync() == ContentDialogResult.Secondary;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "dialog: confirm");
            return false;
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    /// <summary>
    /// <see cref="ConfirmAsync"/> with an optional, unticked checkbox under the message. With
    /// no <paramref name="option"/> it is exactly the plain confirmation.
    /// </summary>
    private async Task<(bool Confirmed, bool Option)> ConfirmWithOptionAsync(string title, string body, string verb, string? option)
    {
        if (option is null)
        {
            return (await ConfirmAsync(title, body, verb), false);
        }

        if (_dialogOpen)
        {
            return (false, false);
        }

        _dialogOpen = true;
        try
        {
            CheckBox box = new() { Content = option, IsChecked = false, Margin = new Thickness(0, 14, 0, 0) };
            StackPanel content = new();
            content.Children.Add(new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(box);

            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme,
                Title = title,
                Content = content,
                PrimaryButtonText = "Cancel",
                SecondaryButtonText = verb,
                DefaultButton = ContentDialogButton.Primary
            };

            bool confirmed = await dialog.ShowAsync() == ContentDialogResult.Secondary;
            return (confirmed, confirmed && box.IsChecked == true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "dialog: confirm with option");
            return (false, false);
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    /// <summary>A one-button message, for a command that has nothing to act on.</summary>
    private async Task InformAsync(string title, string body)
    {
        if (_dialogOpen)
        {
            return;
        }

        _dialogOpen = true;
        try
        {
            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme,
                Title = title,
                Content = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "OK",
                DefaultButton = ContentDialogButton.Close
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "dialog: inform");
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    /// <summary>
    /// Pushes a host-side change into an open Settings page. Deliberately one-way: the
    /// page's handler sends nothing back, or Ctrl+D flipping the favourites bar on would
    /// ping-pong between the two forever.
    ///
    /// Only into Settings tabs, like the lists and collection pushes. It used to run in EVERY
    /// tab, so any website that defined window.__aviaryApply was told each change (found in
    /// M4's review: search suggestions going off was about to be one of them).
    /// </summary>
    private void PushToSettingsPage(string key, object value)
    {
        string payload = JsonSerializer.Serialize(key) + "," + JsonSerializer.Serialize(value);

        foreach (BrowserTab tab in _tabs)
        {
            if (tab.View.CoreWebView2 is not CoreWebView2 core || !tab.IsInternalPage || tab.Title != "Settings")
            {
                continue;
            }

            try
            {
                _ = core.ExecuteScriptAsync(
                    "window.__aviaryApply && window.__aviaryApply(" + payload + ")");
            }
            catch (Exception ex)
            {
                // A tab that is mid-teardown simply does not get told.
                Diagnostics.Swallowed(ex, "settings: push value to page");
            }
        }
    }

    // ══ ERRORS ═════════════════════════════════════════════════════════════

    /// <summary>
    /// localhost, 127.0.0.1, 0.0.0.0, ::1 and *.localhost — with or without a port.
    /// </summary>
    private static bool LooksLocal(string text)
    {
        string host = text.Split('/')[0];
        int colon = host.LastIndexOf(':');
        if (colon > 0 && !host.Contains(']'))
        {
            host = host.Substring(0, colon);
        }

        host = host.Trim('[', ']');

        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.StartsWith("127.", StringComparison.Ordinal)
            || host == "0.0.0.0"
            || host == "::1";
    }

    /// <summary>The insecure address the HTTPS-Only panel offers, while it is showing.</summary>
    private string? _fallbackUrl;

    /// <summary>
    /// HTTPS-Only asked the site for a secure connection and did not get one. Safari has no
    /// equivalent to copy; this follows Chrome's HTTPS-First wording: say what failed, say
    /// what the risk is in plain words, make going back the easy choice.
    /// </summary>
    private void OnHttpsFallback(BrowserTab tab, string httpUrl)
    {
        if (!ReferenceEquals(tab, _active))
        {
            return;
        }

        string host = Uri.TryCreate(httpUrl, UriKind.Absolute, out Uri? uri) ? uri.Host : httpUrl;
        ShowError("This site doesn't offer a secure connection",
            "Aviary tried a secure (HTTPS) connection to " + host + " and the site didn't answer securely. "
          + "On an insecure connection, anyone on your network can see and change the pages you "
          + "load and anything you send.");
        _fallbackUrl = httpUrl;
        ErrorActions.Visibility = Visibility.Visible;
        ErrorBackButton.IsEnabled = tab.CanGoBack;
    }

    private void ErrorBack_Click(object sender, RoutedEventArgs e)
    {
        // Strict's warning: the stopped navigation never left the page that was showing, so
        // going back is simply putting that page on screen again.
        if (_blockedSiteUrl is not null)
        {
            DiagUi("site-blocked-back");
            HideError();
            return;
        }

        DiagUi("https-fallback-back");
        BrowserTab? tab = _active;
        HideError();
        if (tab is { CanGoBack: true })
        {
            tab.View.GoBack();
        }
    }

    private void ErrorContinue_Click(object sender, RoutedEventArgs e)
    {
        if (_blockedSiteUrl is { } blocked && _active is { } blockedTab)
        {
            DiagUi("site-blocked-open-anyway");
            HideError();
            blockedTab.OpenBlockedSite(blocked);
            return;
        }

        if (_fallbackUrl is not { } url || _active is not { } tab)
        {
            return;
        }

        DiagUi("https-fallback-continue");
        HideError();
        tab.ContinueInsecure(url);
    }

    private void ShowError(string title, string detail)
    {
        // What the person actually SAW — the navigation line records the raw status.
        DiagUi("error-page-shown", new { title, detail, url = _active?.Url });

        ErrorTitle.Text = title;
        ErrorDetail.Text = detail;
        ErrorPanel.Visibility = Visibility.Visible;

        // Hide the WebView, or Chromium's own error page shows behind ours — complete
        // with a Microsoft Edge logo, which has no business appearing in this browser.
        if (_active is not null)
        {
            _active.View.Visibility = Visibility.Collapsed;
        }
    }

    private void HideError()
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        ErrorActions.Visibility = Visibility.Collapsed;
        _fallbackUrl = null;
        _blockedSiteUrl = null;
        ErrorContinueButton.Content = "Continue to Site";

        if (_active is not null)
        {
            _active.View.Visibility = Visibility.Visible;
        }
    }

    /// <summary>Turns a WebView2 error status into something a person can act on.</summary>
    private static string DescribeError(CoreWebView2WebErrorStatus status) => status switch
    {
        CoreWebView2WebErrorStatus.HostNameNotResolved => "That address doesn't seem to exist. Check the spelling.",
        CoreWebView2WebErrorStatus.CannotConnect => "Nothing answered at that address. The site may be down, or you may be offline.",
        CoreWebView2WebErrorStatus.Timeout => "The site took too long to answer.",
        CoreWebView2WebErrorStatus.Disconnected => "The connection dropped.",
        CoreWebView2WebErrorStatus.ConnectionAborted => "The connection was cut short.",
        CoreWebView2WebErrorStatus.ConnectionReset => "The site closed the connection.",
        CoreWebView2WebErrorStatus.ServerUnreachable => "That server can't be reached.",
        CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse => "That address answered with something Aviary couldn't read. If it is a local server, try http:// instead of https://.",
        CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect => "That site's security certificate doesn't match its address.",
        CoreWebView2WebErrorStatus.CertificateExpired => "That site's security certificate has expired.",
        CoreWebView2WebErrorStatus.CertificateIsInvalid => "That site's security certificate isn't valid.",
        CoreWebView2WebErrorStatus.OperationCanceled => "",
        _ => "The page couldn't be loaded (" + status + ")."
    };

    // ══ SHUTDOWN ═══════════════════════════════════════════════════════════

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        Favicons.Updated -= OnFaviconUpdated;
        Bookmarks.Changed -= RefreshBookmarksBar;
        OpenWindows.Remove(this);

        if (OpenWindows.Count > 0)
        {
            // Another window is still open (a private one, or the normal one behind a private
            // one). Only this window's tabs go; the shared stores and diagnostics carry on.
            foreach (BrowserTab t in _tabs)
            {
                t.Dispose();
            }

            _tabs.Clear();
            _tabButtons.Clear();
            return;
        }

        // The last window: a launch arriving from here on waits until everything below is
        // written, then gets a new Aviary (Program.cs, bug 51).
        Program.BeginClosing();

        // Before the tabs go: the index write is debounced and the accumulated
        // last-used timestamps are only in memory.
        try { Favicons.FlushAsync().GetAwaiter().GetResult(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "close: favicon flush"); }

        // Every debounced store gets a final synchronous write. A debounce that is still
        // pending when the process ends is data that was never saved.
        try { PrivacyReport.Save(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "close: privacy report"); }
        try { History.Save(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "close: history"); }

        foreach (BrowserTab tab in _tabs)
        {
            tab.Dispose();
        }

        _tabs.Clear();
        _tabButtons.Clear();

        // Last: the final summary.md and a session.json that says this session ended
        // cleanly. Bounded, so a stuck disk cannot hold the window open.
        Diagnostics.Shutdown();

        // Only now, with nothing left to write, may another Aviary have the profile.
        Program.Release();
    }
}
