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

    private bool _readerActive;
    private bool _readerAvailable;

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
        _readerAvailable = false;

        try
        {
            if (tab.View.CoreWebView2 is not CoreWebView2 core || tab.IsInternalPage)
            {
                return;
            }

            string json = await core.ExecuteScriptAsync(
                "window.__aviaryReader ? window.__aviaryReader.probe() : '{}'");

            // ExecuteScriptAsync returns the value JSON-encoded, so a string result comes
            // back as a quoted, escaped string that has to be unwrapped before parsing.
            string? inner = JsonSerializer.Deserialize<string>(json);
            if (string.IsNullOrEmpty(inner))
            {
                return;
            }

            using JsonDocument doc = JsonDocument.Parse(inner);
            _readerAvailable = doc.RootElement.TryGetProperty("readable", out JsonElement r)
                               && r.ValueKind == JsonValueKind.True;

            // Category 14: Reader's verdict on every page — the data M7 needs to tell whether
            // the extraction is too strict or too eager.
            DiagFeature("reader-verdict", new
            {
                url = tab.Url,
                readable = _readerAvailable,
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
            _readerActive ? "Page settings — Reader is on" : "Page settings");
    }

    // ══ READER ═════════════════════════════════════════════════════════════

    private async void ToggleReader()
    {
        if (_active?.View.CoreWebView2 is not CoreWebView2 core)
        {
            return;
        }

        try
        {
            if (_readerActive)
            {
                await core.ExecuteScriptAsync("window.__aviaryReader && window.__aviaryReader.exit()");
                _readerActive = false;
            }
            else
            {
                if (!_readerAvailable)
                {
                    return;
                }

                await core.ExecuteScriptAsync(
                    "window.__aviaryReader && window.__aviaryReader.enter(" + ReaderOptions() + ")");
                _readerActive = true;
            }

            DiagFeature(_readerActive ? "reader-on" : "reader-off", new
            {
                url = _active?.Url,
                font = ReaderFont,
                background = ReaderTheme,
            });
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "reader: toggle");
        }

        UpdatePageButton();
    }

    private async void RestyleReader()
    {
        if (!_readerActive || _active?.View.CoreWebView2 is not CoreWebView2 core)
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

    private void ShowPageMenu()
    {
        MenuFlyout menu = new() { Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        Themed(menu, "page settings (Aa)");

        // ── zoom row ──
        MenuFlyoutItem zoomOut = new() { Text = "Zoom Out" };
        zoomOut.KeyboardAcceleratorTextOverride = "Ctrl+-";
        zoomOut.Click += (_, _) => Zoom(-1);
        menu.Items.Add(zoomOut);

        MenuFlyoutItem zoomLabel = new()
        {
            Text = "Actual Size  (" + AppSettings.Get("zoom") + ")",
            IsEnabled = AppSettings.Get("zoom") != "100%",
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
            Text = _readerActive ? "Hide Reader" : "Show Reader",
            IsEnabled = _readerActive || _readerAvailable,
        };
        reader.KeyboardAcceleratorTextOverride = "Ctrl+Shift+R";
        reader.Click += (_, _) => ToggleReader();
        menu.Items.Add(reader);

        if (!_readerAvailable && !_readerActive)
        {
            // Saying WHY it is greyed out is the difference between a considered menu and
            // one that looks broken.
            menu.Items.Add(new MenuFlyoutItem
            {
                Text = "No article found on this page",
                IsEnabled = false,
            });
        }

        if (_readerActive)
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
            ToggleMenuFlyoutItem auto = new()
            {
                Text = "Use Reader Automatically on " + host,
                IsChecked = SiteSettings.GetBool(host, "autoReader"),
            };
            auto.Click += (_, _) =>
            {
                bool now = !SiteSettings.GetBool(host, "autoReader");
                SiteSettings.Set(host, "autoReader", now ? "true" : "false");
                DiagFeature("reader-automatic", new { site = host, on = now });
                if (now && _readerAvailable && !_readerActive)
                {
                    ToggleReader();
                }
            };
            menu.Items.Add(auto);
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
