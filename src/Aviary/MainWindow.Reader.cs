using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// The "Aa" menu, and Reader.
///
/// The Aa affordance has sat inside the address field since Phase 1 doing nothing. In
/// Safari it is the page menu: zoom, Show Reader, and the per-site settings — and it lives
/// INSIDE the field on the left, which is one of the two placements (with reload on the
/// right) that make a toolbar read as Safari rather than Chrome.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly string[] ReaderFonts =
    {
        "San Francisco", "New York", "Athelas", "Charter", "Georgia",
        "Iowan", "Palatino", "Seravek", "Times New Roman",
    };

    private static readonly string[] ReaderThemes = { "White", "Sepia", "Grey", "Black" };

    // Bug 93: the tab's own state (BrowserTab.ReaderState.cs), read from the tab in front - these used
    // to be the window's, reset by any tab's navigation and never by switching tabs.
    private bool ReaderActive => _active?.ReaderShown ?? false;
    private bool ReaderAvailable => _active?.ReaderAvailable ?? false;

    private string ReaderFont => AppSettings.Get("readerFont") is { Length: > 0 } f ? f : "New York";
    private string ReaderTheme => AppSettings.Get("readerTheme") is { Length: > 0 } t ? t : "White";

    private double ReaderSize =>
        double.TryParse(AppSettings.Get("readerSize"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double s) && s >= 12 && s <= 28
            ? s
            : 19;

    private string ReaderOptions() => JsonSerializer.Serialize(new
    {
        font = ReaderFont,
        theme = ReaderTheme,
        size = ReaderSize,
        width = 40,
    });

    // ══ AVAILABILITY ═══════════════════════════════════════════════════════

    /// <summary>
    /// Asks the page whether it has an article worth reading. Runs after every navigation
    /// so the menu can grey out its Reader row honestly, rather than offering a button
    /// that does nothing on two thirds of the web.
    /// </summary>
    private async Task ProbeReaderAsync(BrowserTab tab)
    {
        tab.ReaderAvailable = false;

        try
        {
            if (tab.View.CoreWebView2 is not CoreWebView2 core || tab.IsInternalPage)
            {
                return;
            }

            string asked = tab.Url;
            string json = await core.ExecuteScriptAsync(
                "window.__aviaryReader ? window.__aviaryReader.probe() : '{}'");

            // The tab may have moved on to another page while it answered; that page asks for itself.
            if (!string.Equals(asked, tab.Url, StringComparison.Ordinal))
            {
                return;
            }

            // ExecuteScriptAsync returns the value JSON-encoded, so a string result comes
            // back as a quoted, escaped string that has to be unwrapped before parsing.
            string? inner = JsonSerializer.Deserialize<string>(json);
            if (string.IsNullOrEmpty(inner))
            {
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(inner);
            tab.ReaderAvailable = doc.RootElement.TryGetProperty("readable", out JsonElement r)
                                  && r.ValueKind == JsonValueKind.True;

            // Category 14: Reader's verdict on every page — the data M7 needs to tell whether
            // the extraction is too strict or too eager.
            DiagFeature("reader-verdict", new
            {
                url = tab.Url,
                readable = tab.ReaderAvailable,
                words = doc.RootElement.TryGetProperty("words", out JsonElement w) && w.TryGetInt32(out int n) ? n : (int?)null,
            });
        }
        catch (Exception ex)
        {
            // A page that will not answer simply does not offer Reader.
            Diagnostics.Swallowed(ex, "reader: probe page");
        }

        if (ReferenceEquals(tab, _active))
        {
            UpdatePageButton();
        }
    }

    private void UpdatePageButton()
    {
        PageSettingsButton.IsEnabled = _active is { IsReady: true, IsInternalPage: false };
        ToolTipService.SetToolTip(PageSettingsButton,
            ReaderActive ? "Page settings — Reader is on" : "Page settings");
    }

    // ══ READER ═════════════════════════════════════════════════════════════

    private async void ToggleReader()
    {
        // The tab asked about, held: the answer below belongs to IT, even if another tab is in
        // front by the time the page has answered (bug 93).
        BrowserTab? tab = _active;
        if (tab?.View.CoreWebView2 is not CoreWebView2 core)
        {
            return;
        }

        try
        {
            if (tab.ReaderShown)
            {
                await core.ExecuteScriptAsync("window.__aviaryReader && window.__aviaryReader.exit()");

                // M6: Reader keeps the background chosen for it, whatever "Make every website dark" says.
                tab.SetReaderShown(false);
            }
            else
            {
                if (!tab.ReaderAvailable)
                {
                    return;
                }

                await core.ExecuteScriptAsync(
                    "window.__aviaryReader && window.__aviaryReader.enter(" + ReaderOptions() + ")");
                tab.SetReaderShown(true);
            }

            DiagFeature(tab.ReaderShown ? "reader-on" : "reader-off", new
            {
                url = tab.Url,
                font = ReaderFont,
                background = ReaderTheme,
            });
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "reader: toggle");
        }

        UpdatePageButton();

        // M6 stage 3: Reader has its own look, so the toolbar's tint comes off (and back after).
        ApplyTint();
    }

    private async void RestyleReader()
    {
        if (!ReaderActive || _active?.View.CoreWebView2 is not CoreWebView2 core)
        {
            return;
        }

        try
        {
            await core.ExecuteScriptAsync(
                "window.__aviaryReader && window.__aviaryReader.restyle(" + ReaderOptions() + ")");
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "reader: restyle");
        }
    }

    // ══ THE Aa MENU ════════════════════════════════════════════════════════

    private void PageSettingsButton_Click(object sender, RoutedEventArgs e) => ShowPageMenu();

    private async void ShowPageMenu()
    {
        BrowserTab? pageTab = _active;
        MenuFlyout menu = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        Themed(menu, "page settings (Aa)");

        // ── zoom row ──
        MenuFlyoutItem zoomOut = new() { Text = "Zoom Out" };
        zoomOut.KeyboardAcceleratorTextOverride = "Ctrl+-";
        zoomOut.Click += (_, _) => Zoom(-1);
        menu.Items.Add(zoomOut);

        // M6 stage 3: the page in front's zoom - its site's own when each website keeps one.
        string zoomNow = ZoomInFront();
        MenuFlyoutItem zoomLabel = new()
        {
            Text = "Actual Size  (" + zoomNow + ")",
            IsEnabled = zoomNow != "100%",
        };
        zoomLabel.KeyboardAcceleratorTextOverride = "Ctrl+0";
        zoomLabel.Click += (_, _) => Zoom(0);
        menu.Items.Add(zoomLabel);

        MenuFlyoutItem zoomIn = new() { Text = "Zoom In" };
        zoomIn.KeyboardAcceleratorTextOverride = "Ctrl++";
        zoomIn.Click += (_, _) => Zoom(+1);
        menu.Items.Add(zoomIn);

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── reader ──
        MenuFlyoutItem reader = new()
        {
            Text = ReaderActive ? "Hide Reader" : "Show Reader",
            IsEnabled = ReaderActive || ReaderAvailable,
        };
        reader.KeyboardAcceleratorTextOverride = "Ctrl+Shift+R";
        reader.Click += (_, _) => ToggleReader();
        menu.Items.Add(reader);

        if (!ReaderAvailable && !ReaderActive)
        {
            // Saying WHY it is greyed out is the difference between a considered menu and
            // one that looks broken.
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "No article found on this page",
                IsEnabled = false,
            });
        }

        if (ReaderActive)
        {
            MenuFlyoutSubItem font = new() { Text = "Reader Font" };
            foreach (string name in ReaderFonts)
            {
                string captured = name;
                ToggleMenuFlyoutItem item = new() { Text = name, IsChecked = name == ReaderFont };
                item.Click += (_, _) => { AppSettings.Set("readerFont", captured); RestyleReader(); };
                font.Items.Add(item);
            }

            menu.Items.Add(font);

            MenuFlyoutSubItem theme = new() { Text = "Reader Background" };
            foreach (string name in ReaderThemes)
            {
                string captured = name;
                ToggleMenuFlyoutItem item = new() { Text = name, IsChecked = name == ReaderTheme };
                item.Click += (_, _) => { AppSettings.Set("readerTheme", captured); RestyleReader(); };
                theme.Items.Add(item);
            }

            menu.Items.Add(theme);

            MenuFlyoutItem smaller = new() { Text = "Smaller Text" };
            smaller.Click += (_, _) => StepReaderSize(-1);
            MenuFlyoutItem bigger = new() { Text = "Larger Text" };
            bigger.Click += (_, _) => StepReaderSize(+1);
            menu.Items.Add(smaller);
            menu.Items.Add(bigger);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        // ── per-site ──
        string host = BrowserTab.HostOf(_active?.Url ?? string.Empty);
        if (host.Length > 0)
        {
            // M6 stage 3: with "Use Reader on every article" on, every site already is - the item
            // says so rather than offering a switch that changes nothing.
            bool everySite = AppSettings.GetBool("exp.autoReader");
            ToggleMenuFlyoutItem auto = new()
            {
                Text = everySite
                    ? "Use Reader Automatically on Every Site (Experimental)"
                    : "Use Reader Automatically on " + host,
                IsChecked = everySite || SiteSettings.GetBool(host, "autoReader"),
                IsEnabled = !everySite,
            };
            auto.Click += (_, _) =>
            {
                bool now = !SiteSettings.GetBool(host, "autoReader");
                SiteSettings.Set(host, "autoReader", now ? "true" : "false");
                DiagFeature("reader-automatic", new { site = host, on = now });
                if (now && ReaderAvailable && !ReaderActive)
                {
                    ToggleReader();
                }
            };
            menu.Items.Add(auto);
        }

        // The page menu reaches a playing video even when its player consumes right-clicks.
        // Recheck the selected tab after the asynchronous probe so a late answer cannot open
        // a menu for the page the person has just left.
        if (pageTab is { IsInternalPage: false } && pageTab.View.CoreWebView2 is { } core)
        {
            string? pip = await PipStateAsync(core, -1, -1);
            if (!ReferenceEquals(pageTab, _active) || !OpenWindows.Contains(this))
            {
                return;
            }

            if (pip is not null)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                Add(menu, pip == "in" ? "Leave Picture in Picture" : "Picture in Picture",
                    () => _ = TogglePictureInPictureAsync(core, -1, -1));
            }
        }

        menu.ShowAt(PageSettingsButton);
    }

    private void StepReaderSize(int direction)
    {
        double next = Math.Clamp(ReaderSize + direction, 12, 28);
        AppSettings.Set("readerSize", next.ToString("0", CultureInfo.InvariantCulture));
        RestyleReader();
    }
}

/// <summary>
/// Per-site preferences, keyed by host, in %LOCALAPPDATA%\Aviary\sites.json.
///
/// Safari keeps a row per website — zoom, auto-Reader, autoplay, camera, and so on. This
/// is the same idea kept deliberately small: one flat map of "host|key" to value, so
/// adding a per-site preference later is one call site rather than a schema change.
/// </summary>
internal static class SiteSettings
{
    private static readonly object Gate = new();
    private static Dictionary<string, string>? _values;

    private static string FilePath => AppPaths.Of("sites.json");

    private static Dictionary<string, string> Values
    {
        get
        {
            lock (Gate)
            {
                if (_values is not null)
                {
                    return _values;
                }

                _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    if (System.IO.File.Exists(FilePath))
                    {
                        _values = JsonSerializer.Deserialize<Dictionary<string, string>>(
                                      System.IO.File.ReadAllText(FilePath))
                                  ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch (Exception ex)
                {
                    _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    Diagnostics.Swallowed(ex, "site settings: load sites.json");
                }

                return _values;
            }
        }
    }

    private static string Compose(string host, string key) =>
        PublicSuffix.RegistrableDomain(host) + "|" + key;

    public static string Get(string host, string key)
    {
        lock (Gate)
        {
            return Values.TryGetValue(Compose(host, key), out string? value) ? value : string.Empty;
        }
    }

    public static bool GetBool(string host, string key) =>
        string.Equals(Get(host, key), "true", StringComparison.OrdinalIgnoreCase);

    public static void Set(string host, string key, string value)
    {
        lock (Gate)
        {
            Values[Compose(host, key)] = value;
        }

        Save();
    }

    public static void Remove(string host, string key)
    {
        lock (Gate)
        {
            Values.Remove(Compose(host, key));
        }

        Save();
    }

    /// <summary>Every site (registrable domain) holding this key with this value, sorted.</summary>
    public static List<string> SitesWith(string key, string value = "true")
    {
        string suffix = "|" + key;
        lock (Gate)
        {
            List<string> sites = new();
            foreach (KeyValuePair<string, string> pair in Values)
            {
                if (pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(pair.Value, value, StringComparison.OrdinalIgnoreCase))
                {
                    sites.Add(pair.Key.Substring(0, pair.Key.Length - suffix.Length));
                }
            }

            sites.Sort(StringComparer.OrdinalIgnoreCase);
            return sites;
        }
    }

    /// <summary>Every site holding this key, with its value, whatever the value (M4.9: forget modes).</summary>
    public static List<(string Site, string Value)> SitesWithAny(string key)
    {
        string suffix = "|" + key;
        lock (Gate)
        {
            List<(string, string)> sites = new();
            foreach (KeyValuePair<string, string> pair in Values)
            {
                if (pair.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && pair.Value.Length > 0)
                {
                    sites.Add((pair.Key.Substring(0, pair.Key.Length - suffix.Length), pair.Value));
                }
            }

            sites.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Item1, b.Item1));
            return sites;
        }
    }

    public static void Clear()
    {
        lock (Gate) { Values.Clear(); }
        Save();
    }

    private static void Save()
    {
        try
        {
            string json;
            lock (Gate)
            {
                json = JsonSerializer.Serialize(Values, new JsonSerializerOptions { WriteIndented = true });
            }

            string folder = System.IO.Path.GetDirectoryName(FilePath)!;
            System.IO.Directory.CreateDirectory(folder);

            string temp = FilePath + ".tmp";
            System.IO.File.WriteAllText(temp, json);
            System.IO.File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "site settings: save sites.json");
        }
    }
}
