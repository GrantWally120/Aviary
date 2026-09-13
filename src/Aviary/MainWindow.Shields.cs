using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Aviary;

/// <summary>
/// The Shields button and its popover (Phase 5 M3) — Brave's per-site panel, dressed as
/// Aviary: this site's switch, what was blocked on this page and from where, how much
/// Aviary blocks, and the way to the Privacy Report and to Settings. Also Strict's warning
/// panel for a site the lists stop as a whole.
/// </summary>
public sealed partial class MainWindow
{
    private Flyout? _shieldsFlyout;
    private BrowserTab? _shieldsFlyoutTab;
    private bool _shieldsRefreshQueued;

    private void WireShields(BrowserTab tab)
    {
        tab.BlockedChanged += OnTabBlockedChanged;
        tab.SiteBlocked += OnSiteBlocked;
    }

    private void OnTabBlockedChanged(BrowserTab tab)
    {
        if (!ReferenceEquals(tab, _active))
        {
            return;
        }

        UpdateShieldsButton();

        // A busy page blocks in bursts; the open popover redraws once per burst, not per request.
        if (_shieldsFlyout is not null && ReferenceEquals(_shieldsFlyoutTab, tab) && !_shieldsRefreshQueued)
        {
            _shieldsRefreshQueued = true;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                _shieldsRefreshQueued = false;
                if (_shieldsFlyout is { } open && ReferenceEquals(_shieldsFlyoutTab, _active))
                {
                    open.Content = BuildShieldsPanel(_active);
                }
            });
        }
    }

    /// <summary>The toolbar glyph: a slash when nothing is being blocked here, the page's count when something was.</summary>
    private void UpdateShieldsButton()
    {
        BrowserTab? tab = _active;
        bool applies = tab?.ShieldsApply ?? false;
        bool off = !Shields.Enabled || (applies && tab!.ShieldsDown);

        ShieldSlash.Visibility = off ? Visibility.Visible : Visibility.Collapsed;
        ShieldGlyph.Opacity = off ? 0.55 : 1;

        int count = applies && !off ? tab!.BlockedOnPage : 0;
        ShieldCount.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ShieldCountText.Text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);

        string tip = !Shields.Enabled ? "Shields: blocking is off everywhere"
            : !applies ? "Shields"
            : tab!.ShieldsDown ? "Shields are down for " + Shields.SiteOf(tab.PageHost)
            : count == 0 ? "Shields: nothing blocked on this page"
            : "Shields: " + count + (count == 1 ? " ad or tracker" : " ads and trackers") + " blocked on this page";
        ToolTipService.SetToolTip(ShieldsButton, tip);
    }

    private void ShieldsButton_Click(object sender, RoutedEventArgs e)
    {
        BrowserTab? tab = _active;
        Flyout flyout = new()
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            Content = BuildShieldsPanel(tab),
        };

        flyout.Opened += (_, _) =>
        {
            ThemeOpenPopups();
            DiagUi("menu-open", new { menu = "shields" });
        };
        flyout.Closed += (_, _) =>
        {
            if (ReferenceEquals(_shieldsFlyout, flyout))
            {
                _shieldsFlyout = null;
                _shieldsFlyoutTab = null;
            }
        };

        _shieldsFlyout = flyout;
        _shieldsFlyoutTab = tab;
        flyout.ShowAt(ShieldsButton);
    }

    private FrameworkElement BuildShieldsPanel(BrowserTab? tab)
    {
        Brush label = ThemeBrush("AvLabelBrush");
        Brush secondary = ThemeBrush("AvLabelSecondaryBrush");
        Brush tertiary = ThemeBrush("AvLabelTertiaryBrush");
        Brush accent = ThemeBrush("AvAccentBrush");
        Brush hairline = ThemeBrush("AvHairlineBrush");

        StackPanel panel = new() { Width = 292, Spacing = 10, RequestedTheme = RootGrid.ActualTheme };

        bool applies = tab?.ShieldsApply ?? false;
        string site = applies ? Shields.SiteOf(tab!.PageHost) : string.Empty;

        // ── heading: the site, and its switch ──
        Grid head = new() { ColumnSpacing = 10 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        head.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
                "M8.5,1.9 L3.3,4 V8 C3.3,11.1 5.4,13.8 8.5,14.8 C11.6,13.8 13.7,11.1 13.7,8 V4 Z"),
            Stroke = applies && Shields.Enabled && !tab!.ShieldsDown ? accent : tertiary,
            StrokeThickness = 1.4,
            StrokeLineJoin = PenLineJoin.Round,
        });

        StackPanel title = new() { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        TextBlock name = new()
        {
            Text = applies ? site : "Shields",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = label,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        TextBlock state = new() { FontSize = 12, Foreground = secondary, TextWrapping = TextWrapping.Wrap };
        title.Children.Add(name);
        title.Children.Add(state);
        Grid.SetColumn(title, 1);
        head.Children.Add(title);

        if (!Shields.Enabled)
        {
            state.Text = "Blocking is off everywhere.";
            Button on = new() { Content = "Turn On", VerticalAlignment = VerticalAlignment.Center };
            on.Click += (_, _) =>
            {
                AppSettings.Set("blockTrackers", "true");
                TrackerBlocker.Enabled = true;
                Diagnostics.Shields("blocking-on", new { scope = "everywhere", from = "shields popover" });
                Shields.Refresh();
                PushToSettingsPage("blockTrackers", true);
                AfterShieldsChange();
            };
            Grid.SetColumn(on, 2);
            head.Children.Add(on);
        }
        else if (applies)
        {
            bool down = tab!.ShieldsDown;
            state.Text = down ? "Shields are down: nothing is blocked or hidden on this site." : "Shields are up for this site.";

            ToggleSwitch toggle = new()
            {
                IsOn = !down,
                OnContent = string.Empty,
                OffContent = string.Empty,
                MinWidth = 0,
                VerticalAlignment = VerticalAlignment.Center,
            };
            AutomationProperties(toggle, "Shields for " + site);
            toggle.Toggled += async (_, _) => await SetShieldsAsync(tab, !toggle.IsOn);
            Grid.SetColumn(toggle, 2);
            head.Children.Add(toggle);
        }
        else
        {
            state.Text = tab?.IsInternalPage == true || (tab?.PageHost.Length ?? 0) > 0
                ? "Aviary’s own page — nothing to block here."
                : "Open a website to see what Shields stops on it.";
        }

        panel.Children.Add(head);
        panel.Children.Add(new Border { Height = 1, Background = hairline });

        // ── this page ──
        if (applies && Shields.Enabled && !tab!.ShieldsDown)
        {
            int count = tab.BlockedOnPage;
            StackPanel figure = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
            figure.Children.Add(new TextBlock
            {
                Text = count.ToString("N0", CultureInfo.InvariantCulture),
                FontSize = 28,
                FontWeight = FontWeights.SemiBold,
                Foreground = count > 0 ? accent : secondary,
            });
            figure.Children.Add(new TextBlock
            {
                Text = (count == 1 ? "ad or tracker" : "ads and trackers") + "\nblocked on this page",
                FontSize = 12,
                Foreground = secondary,
                VerticalAlignment = VerticalAlignment.Center,
            });
            panel.Children.Add(figure);

            IReadOnlyList<KeyValuePair<string, int>> hosts = tab.BlockedHosts;
            if (hosts.Count > 0)
            {
                StackPanel list = new() { Spacing = 3 };
                int shown = 0;
                foreach (KeyValuePair<string, int> pair in hosts)
                {
                    if (shown++ == 6)
                    {
                        list.Children.Add(new TextBlock
                        {
                            Text = "and " + (hosts.Count - 6) + " more " + (hosts.Count - 6 == 1 ? "site" : "sites"),
                            FontSize = 12,
                            Foreground = tertiary,
                        });
                        break;
                    }

                    Grid row = new();
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Host names come from the page's requests: shown as text, never markup.
                    row.Children.Add(new TextBlock
                    {
                        Text = pair.Key,
                        FontSize = 12,
                        Foreground = label,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                    TextBlock n = new() { Text = pair.Value.ToString(CultureInfo.InvariantCulture), FontSize = 12, Foreground = secondary };
                    Grid.SetColumn(n, 1);
                    row.Children.Add(n);
                    list.Children.Add(row);
                }

                panel.Children.Add(list);
            }
            else if (FilterLists.Engine is null)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = FilterLists.Loading.IsCompleted
                        ? "Both filter lists are switched off in Settings; only Aviary’s own short list is blocking."
                        : "The filter lists are still loading.",
                    FontSize = 12,
                    Foreground = tertiary,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            panel.Children.Add(new Border { Height = 1, Background = hairline });
        }

        // ── how much, and the ways on ──
        (long blocked, long _, double _, string _) = PrivacyReport.Stats(allTime: false);
        panel.Children.Add(new TextBlock
        {
            Text = (Shields.Strict
                        ? "Strict: ads and trackers from every source, and a warning before sites that exist only to serve them."
                        : "Standard: ads and trackers from other companies. A site’s own content is left alone.")
                   + (blocked > 0 ? "\n" + blocked.ToString("N0", CultureInfo.InvariantCulture) + " blocked in the last 7 days." : string.Empty),
            FontSize = 12,
            Foreground = secondary,
            TextWrapping = TextWrapping.Wrap,
        });

        StackPanel links = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(-10, -4, 0, -6) };
        HyperlinkButton report = new() { Content = "Privacy Report", FontSize = 12, Foreground = accent };
        report.Click += (_, _) =>
        {
            _shieldsFlyout?.Hide();
            DiagUi("shields-privacy-report");
            _ = OpenNewTabAsync(null, "privacy");
        };
        HyperlinkButton settings = new() { Content = "Shields Settings", FontSize = 12, Foreground = accent };
        settings.Click += (_, _) =>
        {
            _shieldsFlyout?.Hide();
            DiagUi("shields-settings");
            OpenSettingsTab("shields");
        };
        links.Children.Add(report);
        links.Children.Add(settings);
        panel.Children.Add(links);

        return panel;
    }

    private static void AutomationProperties(UIElement element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);

    /// <summary>
    /// The popover's switch: lower or raise Shields for this page's site, then reload it — after
    /// the tab's element-hiding script has been swapped, so the reloaded page gets the new one.
    /// </summary>
    private async Task SetShieldsAsync(BrowserTab tab, bool down)
    {
        try
        {
            if (!tab.ShieldsApply || tab.ShieldsDown == down)
            {
                return;
            }

            Shields.Set(tab.PageHost, down, _private);
            await tab.RefreshCosmeticAsync();
            AfterShieldsChange();
            tab.View.Reload();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "shields: set for site");
        }
    }

    /// <summary>After any Shields change: the button, an open popover, and Settings' list of sites.</summary>
    private void AfterShieldsChange()
    {
        UpdateShieldsButton();
        if (_shieldsFlyout is { } open)
        {
            open.Content = BuildShieldsPanel(_active);
        }
    }

    // ══ SETTINGS → SHIELDS ═════════════════════════════════════════════════

    /// <summary>The lists' versions, counts and last check, into the Settings page that asked.</summary>
    private static async Task PushListsAsync(Microsoft.Web.WebView2.Core.CoreWebView2 page, bool updating)
    {
        try
        {
            // "Checking…" is shown before the check itself has begun, so it is said, not read.
            string json = System.Text.Json.JsonSerializer.Serialize(FilterLists.Describe(updating ? true : null));
            await page.ExecuteScriptAsync("window.__aviaryListsStatus && window.__aviaryListsStatus(" + json + ")");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "settings: push filter lists");
        }
    }

    /// <summary>
    /// "Ads" or "Trackers" switched on or off: rebuild the engine from the lists that are on, then
    /// bring the lists line in this window's Settings pages up to date.
    /// </summary>
    private async Task ReloadListsAsync()
    {
        try
        {
            await FilterLists.Reload("setting");
            string json = System.Text.Json.JsonSerializer.Serialize(FilterLists.Describe());
            foreach (BrowserTab tab in _tabs)
            {
                if (tab.IsInternalPage && tab.Title == "Settings" && tab.View.CoreWebView2 is { } core)
                {
                    _ = core.ExecuteScriptAsync("window.__aviaryListsStatus && window.__aviaryListsStatus(" + json + ")");
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "settings: reload filter lists");
        }
    }

    /// <summary>The sites with Shields down, into the Settings page that asked. Names go in as text.</summary>
    private static async Task PushShieldsSitesAsync(Microsoft.Web.WebView2.Core.CoreWebView2 page)
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(Shields.DownSites());
            await page.ExecuteScriptAsync("window.__aviaryShieldsSites && window.__aviaryShieldsSites(" + json + ")");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "settings: push shields sites");
        }
    }

    // ══ STRICT'S WARNING ═══════════════════════════════════════════════════

    /// <summary>The site Strict stopped, while its warning is showing.</summary>
    private string? _blockedSiteUrl;

    private void OnSiteBlocked(BrowserTab tab, string url, string rule, string list)
    {
        if (!ReferenceEquals(tab, _active))
        {
            return;
        }

        string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url;
        string source = list switch
        {
            "EasyPrivacy" => "EasyPrivacy, the tracker list,",
            "EasyList" => "EasyList, the ad list,",
            "Aviary" => "Aviary’s own tracker list",
            _ => list,
        };

        ShowError("Aviary stopped this site",
            host + " is on " + source + " as a site that exists to serve ads or track people, so Strict "
          + "blocking didn’t open it. If you trust it, you can open it anyway — only in this tab, and only "
          + "until you leave it. (The rule: " + rule + ")");

        _fallbackUrl = null;
        _blockedSiteUrl = url;
        ErrorContinueButton.Content = "Open Anyway";
        ErrorBackButton.IsEnabled = true;
        ErrorActions.Visibility = Visibility.Visible;
    }
}
