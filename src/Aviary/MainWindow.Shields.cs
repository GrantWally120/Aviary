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
        bool badge = !string.Equals(AppSettings.Get("shieldBadge"), "false", StringComparison.OrdinalIgnoreCase);
        ShieldCount.Visibility = count > 0 && badge ? Visibility.Visible : Visibility.Collapsed;
        ShieldCountText.Text = count > 99 ? "99+" : count.ToString(CultureInfo.InvariantCulture);
        NoteShieldCount(tab, count);

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
            Shields.Mode mode = tab!.ShieldsMode;
            state.Text = mode switch
            {
                Shields.Mode.Off => "Shields are down: nothing is blocked or hidden here.",
                Shields.Mode.Strict => "Strict on this site: ads and trackers from anywhere, its own included.",
                _ => "Standard on this site: ads and trackers from other companies.",
            };

            if (tab.ShieldsChosen)
            {
                // A site whose setting differs from the default says so, and how to undo it.
                Border chip = new()
                {
                    Background = ThemeBrush("AvSegmentBrush"),
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(7, 2, 7, 3),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "Its own", FontSize = 11, Foreground = secondary },
                };
                ToolTipService.SetToolTip(chip, "This site has a Shields setting of its own.");
                Grid.SetColumn(chip, 2);
                head.Children.Add(chip);
            }
        }
        else
        {
            state.Text = tab?.IsInternalPage == true || (tab?.PageHost.Length ?? 0) > 0
                ? "Aviary’s own page — nothing to block here."
                : "Open a website to see what Shields stops on it.";
        }

        panel.Children.Add(head);

        // M4.5: how much for THIS site - Off, Standard or Strict - instead of only on and off.
        if (applies && Shields.Enabled)
        {
            panel.Children.Add(ShieldsModeRow(tab!, site, accent, label, secondary));
        }

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

                IReadOnlyList<(string Url, string Rule, string List)> recent = tab.RecentBlocked;
                if (recent.Count > 0)
                {
                    StackPanel latest = new() { Spacing = 4 };
                    latest.Children.Add(new TextBlock
                    {
                        Text = "Recently blocked on this page",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = secondary,
                    });

                    int i = 0;
                    foreach ((string url, string rule, string which) in recent)
                    {
                        if (i++ == 4)
                        {
                            break;
                        }

                        StackPanel entry = new() { Spacing = 0 };

                        // Addresses and rules are website- and list-controlled: text, never markup.
                        entry.Children.Add(new TextBlock
                        {
                            Text = ShortAddress(url),
                            FontSize = 11,
                            Foreground = label,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        });
                        entry.Children.Add(new TextBlock
                        {
                            Text = rule + "  ·  " + which,
                            FontSize = 10,
                            Foreground = tertiary,
                            TextTrimming = TextTrimming.CharacterEllipsis,
                        });
                        latest.Children.Add(entry);
                    }

                    panel.Children.Add(new Border { Height = 1, Background = hairline });
                    panel.Children.Add(latest);
                }
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
            Text = ((applies ? tab!.ShieldsMode == Shields.Mode.Strict : Shields.Strict)
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

    /// <summary>
    /// Off / Standard / Strict for one site (M4.5). Three buttons in a capsule, the chosen one
    /// filled; and, when the site has a setting of its own, a way back to the default.
    /// </summary>
    private FrameworkElement ShieldsModeRow(BrowserTab tab, string site, Brush accent, Brush label, Brush secondary)
    {
        StackPanel wrap = new() { Spacing = 6 };

        Border capsule = new()
        {
            Height = 28,
            Background = ThemeBrush("AvSegmentBrush"),
            CornerRadius = Res("AvSegmentRadius", new CornerRadius(7)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        Grid row = new();
        Shields.Mode current = tab.ShieldsMode;
        (Shields.Mode Mode, string Text, string Says)[] choices =
        {
            (Shields.Mode.Off, "Off", "Nothing is blocked or hidden on this site."),
            (Shields.Mode.Standard, "Standard", "Ads and trackers from other companies."),
            (Shields.Mode.Strict, "Strict", "Ads and trackers from anywhere, this site’s own included."),
        };

        for (int i = 0; i < choices.Length; i++)
        {
            (Shields.Mode mode, string text, string says) = choices[i];
            bool chosen = mode == current;
            ToggleButton segment = new()
            {
                Style = (Style)RootGrid.Resources["AvSegmentButton"],
                Content = new TextBlock { Text = text, FontSize = 12 },
                Foreground = chosen ? label : secondary,
                IsChecked = chosen,
            };
            AutomationProperties(segment, "Shields " + text + " for " + site);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(segment, says);
            ToolTipService.SetToolTip(segment, says);

            // A segment is a choice, not a switch: clicking the one already chosen must not
            // clear it, and clicking another redraws the panel anyway.
            segment.Click += async (_, _) =>
            {
                segment.IsChecked = true;
                await SetShieldsModeAsync(tab, mode);
            };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(segment, i);
            row.Children.Add(segment);
        }

        capsule.Child = row;
        wrap.Children.Add(capsule);

        if (tab.ShieldsChosen)
        {
            HyperlinkButton back = new()
            {
                Content = "Use the default (" + (Shields.Default == Shields.Mode.Strict ? "Strict" : "Standard") + ")",
                FontSize = 11,
                Foreground = accent,
                Margin = new Thickness(-10, -6, 0, -6),
            };
            back.Click += async (_, _) => await SetShieldsModeAsync(tab, null);
            wrap.Children.Add(back);
        }

        return wrap;
    }

    /// <summary>An address as the popover shows it: host, then a shortened path.</summary>
    private static string ShortAddress(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? u))
        {
            return url.Length > 54 ? url.Substring(0, 53) + "…" : url;
        }

        string path = u.AbsolutePath;
        string shown = u.Host + (path.Length > 1 ? path : string.Empty);
        return shown.Length > 54 ? shown.Substring(0, 53) + "…" : shown;
    }

    private static void AutomationProperties(UIElement element, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(element, name);

    /// <summary>
    /// The popover's switch: lower or raise Shields for this page's site, then reload it — after
    /// the tab's element-hiding script has been swapped, so the reloaded page gets the new one.
    /// </summary>
    private async Task SetShieldsModeAsync(BrowserTab tab, Shields.Mode? mode)
    {
        try
        {
            if (!tab.ShieldsApply)
            {
                return;
            }

            if (mode is { } chosen)
            {
                if (chosen == tab.ShieldsMode && tab.ShieldsChosen)
                {
                    return;
                }

                Shields.SetMode(tab.PageHost, chosen, _private);
            }
            else
            {
                Shields.ResetSite(tab.PageHost, _private);
            }

            await tab.RefreshCosmeticAsync();
            AfterShieldsChange();
            tab.View.Reload();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "shields: set how much for site");
        }
    }

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

    /// <summary>
    /// The sites with a Shields setting of their own, into the Settings page that asked: each
    /// site and whether it is Off, Standard or Strict (M4.5). Names go in as text.
    /// </summary>
    private static async Task PushShieldsSitesAsync(Microsoft.Web.WebView2.Core.CoreWebView2 page)
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                fallback = Shields.Default == Shields.Mode.Strict ? "Strict" : "Standard",
                sites = Shields.OwnSites().ConvertAll(s => new { site = s.Site, mode = s.Mode.ToString() }),
            });
            await page.ExecuteScriptAsync("window.__aviaryShieldsSites && window.__aviaryShieldsSites(" + json + ")");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "settings: push shields sites");
        }
    }

    /// <summary>
    /// What Shields stopped: this session, the last seven days, and all time, with the trackers
    /// and the sites at the top of each (M4.5). Everything here is already in the Privacy Report;
    /// a private window's pages were never counted into it.
    /// </summary>
    private static async Task PushShieldStatsAsync(Microsoft.Web.WebView2.Core.CoreWebView2 page)
    {
        try
        {
            (long week, long weekBytes, double weekSeconds, string _) = PrivacyReport.Stats(allTime: false);
            (long all, long allBytes, double allSeconds, string since) = PrivacyReport.Stats(allTime: true);

            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                session = PrivacyReport.SessionBlocked,
                week,
                weekBytes,
                weekSeconds = Math.Round(weekSeconds, 1),
                all,
                allBytes,
                allSeconds = Math.Round(allSeconds, 1),
                since,
                days = PrivacyReport.LastWeek(),
                trackers = PrivacyReport.TopTrackers(8).ConvertAll(t => new { name = t.Tracker, count = t.Count }),
                sites = PrivacyReport.TopSites(8).ConvertAll(t => new { name = t.Site, count = t.Count }),
            });
            await page.ExecuteScriptAsync("window.__aviaryShieldStats && window.__aviaryShieldStats(" + json + ")");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "settings: push shields statistics");
        }
    }

    // ══ STRICT'S WARNING ═══════════════════════════════════════════════════

    /// <summary>
    /// Strict stopped a site. A tab in the background gets its warning too, shown when it is
    /// switched to — it used to happen there silently (HANDOFF §8, bug 65).
    /// </summary>
    private void OnSiteBlocked(BrowserTab tab, string url, string rule, string list)
    {
        string host = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ? uri.Host : url;
        string source = list switch
        {
            "EasyPrivacy" => "EasyPrivacy, the tracker list,",
            "EasyList" => "EasyList, the ad list,",
            "Aviary" => "Aviary’s own tracker list",
            _ => list,
        };

        ShowNotice(tab, new TabNotice
        {
            Kind = "site-blocked",
            Title = "Aviary stopped this site",
            Detail = host + " is on " + source + " as a site that exists to serve ads or track people, so Strict "
                   + "blocking didn’t open it. If you trust it, you can open it anyway — only in this tab, and only "
                   + "until you leave it. (The rule: " + rule + ")",
            Back = true,
            BackEnabled = true,
            Action = "Open Anyway",
            BlockedUrl = url,
        });
    }
}
