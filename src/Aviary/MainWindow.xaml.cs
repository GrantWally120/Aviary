using System;
using System.Collections.Generic;
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
    private string _searchPrefix = "https://duckduckgo.com/?q=";

    public MainWindow()
    {
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
        RootGrid.ActualThemeChanged += (_, _) => ApplyWebViewBackground();
        Closed += MainWindow_Closed;

        AddAccelerator(VirtualKey.T, VirtualKeyModifiers.Control, (_, _) => OpenNewTab(null));
        AddAccelerator(VirtualKey.W, VirtualKeyModifiers.Control, (_, _) => { if (_active is not null) CloseTab(_active); });
        AddAccelerator(VirtualKey.L, VirtualKeyModifiers.Control, (_, _) => AddressBox.Focus(FocusState.Programmatic));
        AddAccelerator(VirtualKey.Tab, VirtualKeyModifiers.Control, (_, _) => CycleTab(1));
    }

    private void AddAccelerator(VirtualKey key, VirtualKeyModifiers mods, TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler)
    {
        KeyboardAccelerator a = new() { Key = key, Modifiers = mods };
        a.Invoked += (s, e) => { e.Handled = true; handler(s, e); };
        RootGrid.KeyboardAccelerators.Add(a);
    }

    private static AppWindow? GetAppWindow(IntPtr hWnd)
    {
        try
        {
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
            // Older Windows 10 builds fall back to the system title bar.
        }
    }

    private async void RootGrid_Loaded(object sender, RoutedEventArgs e)
    {
        RootGrid.Loaded -= RootGrid_Loaded;
        UpdateCaptionSpacer();
        WebViewEnvironment.EnsureUserDataFolder();
        await OpenNewTabAsync(null);
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
        catch (Exception)
        {
            CaptionSpacer.Width = CaptionSpacerFallback;
        }
    }

    // ══ TABS ═══════════════════════════════════════════════════════════════

    private async void OpenNewTab(string? url) => await OpenNewTabAsync(url);

    private async Task OpenNewTabAsync(string? url)
    {
        BrowserTab tab = new();
        tab.Changed += OnTabChanged;
        tab.CloseRequested += t => CloseTab(t);
        tab.ShortcutPressed += OnShortcut;

        _tabs.Add(tab);

        // Added to the tree BEFORE initialising. A WebView2 created while collapsed and
        // shown later can come up rendering but not accepting the mouse; creating it in a
        // visible slot avoids that whole class of bug.
        ContentHost.Children.Add(tab.View);
        SelectTab(tab);
        RebuildTabStrip();

        try
        {
            await tab.InitialiseAsync();
        }
        catch (Exception ex)
        {
            ShowError("Aviary can't start its browser engine", ex.Message);
            return;
        }

        HideError();
        ApplyWebViewBackground();

        if (url is null)
        {
            tab.NavigateToHtml(InternalPages.StartPage, "Start Page");
        }
        else
        {
            tab.Navigate(url);
        }

        UpdateChrome();
        tab.View.Focus(FocusState.Programmatic);
    }

    private void OpenSettingsTab()
    {
        _ = OpenSettingsTabAsync();
    }

    private async Task OpenSettingsTabAsync()
    {
        BrowserTab tab = new();
        tab.Changed += OnTabChanged;
        tab.CloseRequested += t => CloseTab(t);
        tab.ShortcutPressed += OnShortcut;
        _tabs.Add(tab);
        ContentHost.Children.Add(tab.View);
        SelectTab(tab);
        RebuildTabStrip();

        try
        {
            await tab.InitialiseAsync();
        }
        catch (Exception ex)
        {
            ShowError("Aviary can't start its browser engine", ex.Message);
            return;
        }

        HideError();
        ApplyWebViewBackground();

        if (tab.View.CoreWebView2 is CoreWebView2 core)
        {
            // Settings is the one internal page allowed to talk back to the host. It is
            // trusted only because we navigate to it ourselves and never from the network.
            core.WebMessageReceived += Settings_WebMessageReceived;
        }

        tab.NavigateToHtml(InternalPages.Settings, "Settings");
        UpdateChrome();
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

        _active = tab;

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

        // Closing the last tab closes the window, the way every browser behaves.
        if (_tabs.Count == 1)
        {
            Close();
            return;
        }

        _tabs.RemoveAt(index);
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
        switch (action)
        {
            case "newtab": OpenNewTab(null); break;
            case "closetab": if (_active is not null) { CloseTab(_active); } break;
            case "address": AddressBox.Focus(FocusState.Programmatic); break;
            case "nexttab": CycleTab(1); break;
        }
    }

    private void OnTabChanged(BrowserTab tab)
    {
        if (_tabButtons.TryGetValue(tab, out ToggleButton? button)
            && button.Content is Grid content
            && content.Children.Count > 1
            && content.Children[1] is TextBlock title)
        {
            title.Text = tab.Title;
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
        TabStrip.Children.Clear();
        TabStrip.ColumnDefinitions.Clear();
        _tabButtons.Clear();

        for (int i = 0; i < _tabs.Count; i++)
        {
            TabStrip.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = TabMinWidth
            });

            BrowserTab tab = _tabs[i];
            ToggleButton button = BuildTabButton(tab);
            Grid.SetColumn(button, i);
            TabStrip.Children.Add(button);
            _tabButtons[tab] = button;
            button.IsChecked = ReferenceEquals(tab, _active);
        }
    }

    private ToggleButton BuildTabButton(BrowserTab tab)
    {
        // The favicon slot. Favicon and close occupy the SAME 16px cell and cross-fade
        // through each other on hover — Safari puts close at the tab's leading edge, in
        // the favicon's own box, and that swap is its most-copied micro-interaction.
        Border chip = new()
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(ParseHex(tab.Accent)),
            Child = new TextBlock
            {
                Text = tab.Monogram,
                FontSize = 9,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        // A text glyph rather than a Path so it inherits the tab's Foreground, which the
        // template drives from the theme. No colour is assigned in code.
        TextBlock close = new()
        {
            Text = "✕",
            FontSize = 9,
            Opacity = 0,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid slot = new() { Width = 16, Height = 16 };
        slot.Children.Add(chip);
        slot.Children.Add(close);

        TextBlock title = new()
        {
            Text = tab.Title,
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Medium,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Center,
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
        ToolTipService.SetToolTip(button, tab.Title);

        button.PointerEntered += (_, _) => { Fade(chip, 0, 150); Fade(close, 1, 150); };
        button.PointerExited += (_, _) => { Fade(chip, 1, 120); Fade(close, 0, 120); };

        button.Click += (_, _) =>
        {
            // A ToggleButton would otherwise let you un-check the active tab.
            button.IsChecked = true;
            SelectTab(tab);
        };

        // Clicking the close glyph closes rather than selects.
        close.PointerPressed += (_, e) =>
        {
            e.Handled = true;
            CloseTab(tab);
        };

        return button;
    }

    private static void Fade(UIElement element, double to, int milliseconds)
    {
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
        storyboard.Begin();
    }

    private static Windows.UI.Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }

    // ══ CHROME ═════════════════════════════════════════════════════════════

    private void UpdateChrome()
    {
        BrowserTab? tab = _active;

        BackButton.IsEnabled = tab?.CanGoBack ?? false;
        ForwardButton.IsEnabled = tab?.CanGoForward ?? false;
        ReloadButton.IsEnabled = tab?.IsReady ?? false;
        LockGlyph.Visibility = (tab?.IsSecure ?? false) ? Visibility.Visible : Visibility.Collapsed;

        if (!_addressFocused)
        {
            ShowIdleAddress();
        }

        Title = tab is null || string.IsNullOrWhiteSpace(tab.Title) || tab.Title == "Aviary"
            ? "Aviary"
            : tab.Title + " — Aviary";
    }

    private void ApplyWebViewBackground()
    {
        try
        {
            if (Application.Current.Resources["AvPageBrush"] is SolidColorBrush brush)
            {
                foreach (BrowserTab tab in _tabs)
                {
                    tab.View.DefaultBackgroundColor = brush.Color;
                }
            }
        }
        catch (Exception)
        {
            // A white flash on navigation is survivable; a crash is not.
        }
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

        if (Uri.TryCreate(text, UriKind.Absolute, out Uri? absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            target = absolute.ToString();
        }
        else if (!text.Contains(' ') && text.Contains('.'))
        {
            target = "https://" + text;
        }
        else
        {
            target = _searchPrefix + Uri.EscapeDataString(text);
        }

        HideError();
        tab.Navigate(target);
        return true;
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is { CanGoBack: true } tab)
        {
            tab.View.GoBack();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is { CanGoForward: true } tab)
        {
            tab.View.GoForward();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active is { IsReady: true } tab)
        {
            tab.View.Reload();
        }
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        _active?.NavigateToHtml(InternalPages.StartPage, "Start Page");
        UpdateChrome();
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) => OpenNewTab(null);

    // ══ THE APP MENU ═══════════════════════════════════════════════════════

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        MenuFlyout menu = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };

        MenuFlyoutItem newTab = new() { Text = "New Tab" };
        newTab.KeyboardAcceleratorTextOverride = "Ctrl+T";
        newTab.Click += (_, _) => OpenNewTab(null);
        menu.Items.Add(newTab);

        MenuFlyoutItem closeTab = new() { Text = "Close Tab" };
        closeTab.KeyboardAcceleratorTextOverride = "Ctrl+W";
        closeTab.Click += (_, _) => { if (_active is not null) CloseTab(_active); };
        menu.Items.Add(closeTab);

        menu.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutSubItem help = new() { Text = "Help" };
        MenuFlyoutItem about = new() { Text = "About Aviary" };
        about.Click += (_, _) => OpenSettingsTab();
        help.Items.Add(about);
        menu.Items.Add(help);

        MenuFlyoutItem settings = new() { Text = "Settings" };
        settings.Click += (_, _) => OpenSettingsTab();
        menu.Items.Add(settings);

        menu.Items.Add(new MenuFlyoutSeparator());

        MenuFlyoutItem exit = new() { Text = "Exit" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(exit);

        menu.ShowAt(MenuButton);
    }

    /// <summary>
    /// Settings talks back as {type, key, value}. Anything not recognised is ignored
    /// rather than trusted — the page is ours, but the handler should not assume it.
    /// </summary>
    private void Settings_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(args.TryGetWebMessageAsString());
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("key", out JsonElement keyElement))
            {
                return;
            }

            string key = keyElement.GetString() ?? string.Empty;
            JsonElement value = root.TryGetProperty("value", out JsonElement v) ? v : default;

            switch (key)
            {
                case "theme":
                    RootGrid.RequestedTheme = (value.GetString() ?? "System") switch
                    {
                        "Light" => ElementTheme.Light,
                        "Dark" => ElementTheme.Dark,
                        _ => ElementTheme.Default
                    };
                    ApplyWebViewBackground();
                    break;

                case "searchEngine":
                    _searchPrefix = (value.GetString() ?? "DuckDuckGo") switch
                    {
                        "Bing" => "https://www.bing.com/search?q=",
                        "Google" => "https://www.google.com/search?q=",
                        "Startpage" => "https://www.startpage.com/sp/search?query=",
                        _ => "https://duckduckgo.com/?q="
                    };
                    break;

                case "homeButton":
                    HomeButton.Visibility = value.ValueKind == JsonValueKind.True
                        ? Visibility.Visible
                        : Visibility.Collapsed;
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
        catch (Exception)
        {
            // A malformed message from our own page is a bug to find in testing, not a
            // reason to take the browser down.
        }
    }

    // ══ ERRORS ═════════════════════════════════════════════════════════════

    private void ShowError(string title, string detail)
    {
        ErrorTitle.Text = title;
        ErrorDetail.Text = detail;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorPanel.Visibility = Visibility.Collapsed;

    // ══ SHUTDOWN ═══════════════════════════════════════════════════════════

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        foreach (BrowserTab tab in _tabs)
        {
            tab.Dispose();
        }

        _tabs.Clear();
        _tabButtons.Clear();
    }
}
