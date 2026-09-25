using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

namespace Aviary;

/// <summary>
/// Settings → Experimental, stage 3 (Alpha Build 5 M6): the experiments that act as tabs change
/// places, and the ones that belong to the window's own chrome. Each reads its switch every time,
/// so none needs a restart, and each does nothing at all while its switch is off.
/// <list type="bullet">
/// <item><b>Pop a playing video out when you switch tabs</b> — the tab left pops its video out; the
/// tab returned to takes back the one Aviary popped (BrowserTab.PopOutAndTint.cs).</item>
/// <item><b>Show a preview when you point at a tab</b> — the tab left is photographed as it goes
/// behind (CapturePreviewAsync), and its tooltip becomes that picture above its name.</item>
/// <item><b>Close tabs you have not looked at in a week</b> — a sweep every half hour.</item>
/// <item><b>Tint the toolbar to match the page</b> — the page's theme-color behind the toolbar and
/// the tab strip, with their words and icons switched light or dark to stay readable.</item>
/// <item><b>Double-click below your tabs for a new tab</b> — the sidebar's empty space.</item>
/// <item><b>Switch to a tab that is already open</b> — <see cref="FindOpenTab"/>, from the address bar.</item>
/// </list>
/// </summary>
public sealed partial class MainWindow
{
    private static bool ExpOn(string key) => AppSettings.GetBool(key);

    /// <summary>From SelectTab, once the tab in front has changed.</summary>
    private void OnTabSwitchedForExperiments(BrowserTab? leaving, BrowserTab arriving)
    {
        if (arriving.PoppedOut)
        {
            _ = arriving.ReturnVideoAsync();
        }

        if (leaving is not null && _tabs.Contains(leaving))
        {
            if (ExpOn("exp.videoPopOut"))
            {
                _ = PopOutAsync(leaving);
            }

            if (ExpOn("exp.tabPreview"))
            {
                _ = CaptureTabPreviewAsync(leaving);
            }
        }

        // The tab in front never shows its own picture: you are looking at it.
        if (_tabButtons.TryGetValue(arriving, out ToggleButton? button))
        {
            SetTabTip(button, arriving);
        }

        ApplyTint();
    }

    private async Task PopOutAsync(BrowserTab tab)
    {
        string answer = await tab.PopOutVideoAsync();
        if (answer != "none" && answer != "no page")
        {
            DiagFeature("video-pop-out", new { tab = tab.Id, answer });
        }
    }

    // ══ SHOW A PREVIEW WHEN YOU POINT AT A TAB ═══════════════════════════════

    /// <summary>Each tab's last picture, decoded small. Dropped with the tab, and when the switch goes off.</summary>
    private readonly Dictionary<BrowserTab, BitmapImage> _previews = new();

    private const int PreviewWidth = 280;

    /// <summary>
    /// Photographs a tab's page as it goes behind. The engine draws the page it has; a JPEG of the
    /// whole view is decoded at 280 pixels wide, which is all the tooltip keeps.
    /// </summary>
    private async Task CaptureTabPreviewAsync(BrowserTab tab)
    {
        if (tab.IsInternalPage || tab.View.CoreWebView2 is not CoreWebView2 core)
        {
            return;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            using Windows.Storage.Streams.InMemoryRandomAccessStream stream = new();
            Task capture = core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, stream).AsTask();
            if (await Task.WhenAny(capture, Task.Delay(2000)) != capture)
            {
                DiagPerf("tab-preview", new { tab = tab.Id, timedOut = true });
                return;
            }

            await capture;
            ulong bytes = stream.Size;
            if (bytes == 0 || !_tabs.Contains(tab) || !ExpOn("exp.tabPreview"))
            {
                return;
            }

            if (PreviewTest)
            {
                // The test hook keeps the picture itself too, so a test can LOOK at it: a blank
                // capture has the same size as a good one.
                using Windows.Storage.Streams.DataReader reader = new(stream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)bytes);
                byte[] jpeg = new byte[bytes];
                reader.ReadBytes(jpeg);
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(AppPaths.Root, "preview-" + tab.Id + ".jpg"), jpeg);
            }

            stream.Seek(0);
            BitmapImage image = new() { DecodePixelWidth = PreviewWidth };
            await image.SetSourceAsync(stream);
            _previews[tab] = image;

            // PixelWidth/Height are the SOURCE's size; the tooltip keeps it decoded 280 pixels wide.
            double ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1);
            DiagPerf("tab-preview", new { tab = tab.Id, ms, bytes, width = image.PixelWidth, height = image.PixelHeight });
            WritePreviewTest(tab, bytes, image);

            if (_tabButtons.TryGetValue(tab, out ToggleButton? button))
            {
                SetTabTip(button, tab);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab preview: capture");
        }
    }

    /// <summary>
    /// A tab's tooltip: its name — or, for a tab behind with a picture while the experiment is on,
    /// the picture above its name. Every place that retitles a tab comes through here.
    /// </summary>
    private void SetTabTip(ToggleButton button, BrowserTab tab)
    {
        if (!ExpOn("exp.tabPreview") || ReferenceEquals(tab, _active) || !_previews.TryGetValue(tab, out BitmapImage? image))
        {
            ToolTipService.SetToolTip(button, ShownTitle(tab));
            return;
        }

        StackPanel tip = new() { Spacing = 6, Width = PreviewWidth };
        tip.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(6),
            Child = new Image { Source = image, Width = PreviewWidth, Stretch = Stretch.Uniform },
        });
        tip.Children.Add(new TextBlock
        {
            Text = ShownTitle(tab),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        });
        ToolTip tool = new() { Content = tip, MaxWidth = PreviewWidth + 40 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tool, "Preview of " + ShownTitle(tab));
        ToolTipService.SetToolTip(button, tool);
    }

    /// <summary>The switch went off (or a tab closed): pictures are let go and tooltips are names again.</summary>
    private void DropPreviews(BrowserTab? only = null)
    {
        foreach (BrowserTab t in only is null ? _previews.Keys.ToList() : new List<BrowserTab> { only })
        {
            _previews.Remove(t);
            if (_tabButtons.TryGetValue(t, out ToggleButton? button))
            {
                SetTabTip(button, t);
            }
        }
    }

    // A test cannot hover a tab without the mouse, and a tooltip is not in the UI Automation tree
    // until it shows, so - like AVIARY_OVERLAY_TEST - Aviary writes what each capture produced.
    private static readonly bool PreviewTest = Environment.GetEnvironmentVariable("AVIARY_PREVIEW_TEST") == "1";

    private static void WritePreviewTest(BrowserTab tab, ulong bytes, BitmapImage image)
    {
        if (!PreviewTest)
        {
            return;
        }

        try
        {
            System.IO.File.AppendAllText(System.IO.Path.Combine(AppPaths.Root, "preview-test.txt"),
                DateTimeOffset.Now.ToString("HH:mm:ss.fff") + " tab " + tab.Id + " " + tab.Url + " bytes " + bytes
                + " " + image.PixelWidth + "x" + image.PixelHeight + "\n");
        }
        catch (Exception)
        {
            // A test hook; a missed line only means a shorter file.
        }
    }

    // ══ CLOSE TABS YOU HAVE NOT LOOKED AT IN A WEEK ═════════════════════════

    private DispatcherTimer? _oldTabsTimer;

    /// <summary>AVIARY_TABAGE_TEST=seconds: a "week" of that many seconds, swept every 2 s. Environment only.</summary>
    private static readonly int OldTabsTestSeconds =
        int.TryParse(Environment.GetEnvironmentVariable("AVIARY_TABAGE_TEST"), out int s) && s > 0 ? s : 0;

    private static TimeSpan OldTabAge => OldTabsTestSeconds > 0 ? TimeSpan.FromSeconds(OldTabsTestSeconds) : TimeSpan.FromDays(7);

    /// <summary>Starts or stops the sweep to match the switch. From the window loading and from any experiment change.</summary>
    private void ApplyCloseOldTabs()
    {
        bool on = ExpOn("exp.closeOldTabs");
        if (on && _oldTabsTimer is null)
        {
            _oldTabsTimer = new DispatcherTimer
            {
                Interval = OldTabsTestSeconds > 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromMinutes(30),
            };
            _oldTabsTimer.Tick += (_, _) => CloseOldTabs();
            _oldTabsTimer.Start();
        }
        else if (!on && _oldTabsTimer is not null)
        {
            _oldTabsTimer.Stop();
            _oldTabsTimer = null;
        }
    }

    /// <summary>
    /// Every tab last in front a week ago or more — never the tab in front, a pinned tab, one
    /// playing sound, or any tab while this window shows a tab group (those are kept pages, and
    /// closing one would take it out of its group). "Last in front" is stamped as a tab LEAVES the
    /// front (bug 94), so a tab you read for a day counts from the day you left it.
    /// </summary>
    private void CloseOldTabs()
    {
        if (!ExpOn("exp.closeOldTabs") || _group is not null)
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow - OldTabAge;
        foreach (BrowserTab tab in _tabs.ToList())
        {
            if (_tabs.Count <= 1 || ReferenceEquals(tab, _active) || !_tabs.Contains(tab)
                || _pinned.Contains(tab) || tab.LastActiveUtc > cutoff || tab.IsPlayingAudio)
            {
                continue;
            }

            DiagFeature("closed-old-tab", new
            {
                tab = tab.Id,
                days = Math.Round((DateTime.UtcNow - tab.LastActiveUtc).TotalDays, 2),
            });
            CloseTab(tab);
        }
    }

    // ══ TINT THE TOOLBAR TO MATCH THE PAGE ═══════════════════════════════════

    private bool _tinted;

    /// <summary>
    /// "Light" or "Dark" while the toolbar is tinted - the theme chosen to read on the page's colour -
    /// else null. The parts Aviary colours from code (the active tab's chip, the caption buttons) read it.
    /// </summary>
    private string? TintTheme { get; set; }

    /// <summary>A page arrived or changed address: ask it for its colour, and repaint if it is in front.</summary>
    private async Task ReadTintAsync(BrowserTab tab)
    {
        if (!ExpOn("exp.tintToolbar"))
        {
            return;
        }

        bool changed = await tab.ReadTintAsync();
        if (changed && ReferenceEquals(tab, _active))
        {
            ApplyTint();
        }
    }

    /// <summary>
    /// The tab in front's colour behind the toolbar and the tab strip — as a layer under their
    /// controls (TintToolbar, TintTabStrip), so their own theme brushes are never replaced — and the
    /// two rows' theme switched to whichever of light or dark reads on it. Off: the layers go and
    /// the rows follow the window's theme again.
    /// </summary>
    private void ApplyTint()
    {
        // Never under a Windows contrast theme: it overrides every colour on purpose.
        Color? tint = ExpOn("exp.tintToolbar")
                      && _active is { IsInternalPage: false, ReaderShown: false, Darkened: false } tab
                      && EffectiveThemeKey() != "HighContrast"
            ? tab.TintColor
            : null;
        string? themeWas = TintTheme;

        if (tint is Color c)
        {
            // Out to the rows' edges: each look pads its rows differently (Classic 12,0,0,0; Liquid Glass 6).
            Thickness p = ToolbarRow.Padding, q = TabStripRow.Padding;
            TintToolbar.Margin = new Thickness(-p.Left, -p.Top, -p.Right, -p.Bottom);
            TintTabStrip.Margin = new Thickness(-q.Left, -q.Top, -q.Right, -q.Bottom);

            SolidColorBrush brush = new(c);
            TintToolbar.Background = brush;
            TintTabStrip.Background = brush;
            TintToolbar.Visibility = Visibility.Visible;
            TintTabStrip.Visibility = Visibility.Visible;

            // Relative luminance (sRGB): light enough for dark words, or not.
            ElementTheme onIt = Luminance(c) > 0.45 ? ElementTheme.Light : ElementTheme.Dark;
            ToolbarRow.RequestedTheme = onIt;
            TabStripRow.RequestedTheme = onIt;
            TintTheme = onIt == ElementTheme.Light ? "Light" : "Dark";
            _tinted = true;
            WriteTintTest(c, onIt);
        }
        else if (_tinted)
        {
            TintToolbar.Visibility = Visibility.Collapsed;
            TintTabStrip.Visibility = Visibility.Collapsed;
            ToolbarRow.ClearValue(FrameworkElement.RequestedThemeProperty);
            TabStripRow.ClearValue(FrameworkElement.RequestedThemeProperty);
            TintTheme = null;
            _tinted = false;
            WriteTintTest(null, ElementTheme.Default);
        }

        // The chip and the caption buttons are coloured from code, by theme: repaint them when it changed.
        if (!string.Equals(themeWas, TintTheme, StringComparison.Ordinal))
        {
            RefreshPillBrush();
            ApplyCaptionColors();
        }
    }

    private static double Luminance(Color c)
    {
        static double Lin(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
    }

    // The tint is drawn by Aviary, not the page: a test reads what it painted, as for the readout.
    private static readonly bool TintTest = Environment.GetEnvironmentVariable("AVIARY_TINT_TEST") == "1";

    private static void WriteTintTest(Color? c, ElementTheme onIt)
    {
        if (!TintTest)
        {
            return;
        }

        try
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.Root, "tint-test.txt"),
                c is Color k ? "#" + k.R.ToString("x2") + k.G.ToString("x2") + k.B.ToString("x2") + " " + onIt : "none");
        }
        catch (Exception)
        {
            // A test hook.
        }
    }

    // ══ DOUBLE-CLICK BELOW YOUR TABS FOR A NEW TAB ═══════════════════════════

    /// <summary>
    /// The sidebar's empty space, while it lists the window's tabs. Along the top the tabs always
    /// fill their bar (the Safari silhouette, MainWindow.TabMotion.cs), so there is no empty space
    /// there to use - measured in the audit, and said on the row.
    /// </summary>
    private void SidebarScroll_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (!ExpOn("exp.dblClickNewTab") || !UsingSidebarTabs || !_sidebarVisible || _pane != SidebarPane.Tabs)
        {
            return;
        }

        // A double-click on a tab, a button or a heading is theirs, not the empty space's.
        for (DependencyObject? d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, SidebarScroll))
            {
                break;
            }

            if (d is ButtonBase || d is TextBlock)
            {
                return;
            }
        }

        e.Handled = true;
        DiagUi("new-tab", new { via = "double-click below the tabs" });
        OpenNewTab(null);
    }

    // ══ SWITCH TO A TAB THAT IS ALREADY OPEN ═════════════════════════════════

    /// <summary>
    /// Another tab of this window already showing this address, if the experiment is on. The same
    /// address to a person: a site that sent you on to its www. name, http or https, a slash at the
    /// end, or a #place on the page, are all the same page.
    /// </summary>
    private BrowserTab? FindOpenTab(string address, BrowserTab except)
    {
        if (!ExpOn("exp.switchToTab"))
        {
            return null;
        }

        string want = SameAddress(address);
        return want.Length == 0
            ? null
            : _tabs.FirstOrDefault(t => !ReferenceEquals(t, except) && !t.IsInternalPage && SameAddress(t.Url) == want);
    }

    private static string SameAddress(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return string.Empty;
        }

        string host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host.Substring(4) : uri.Host;
        string path = uri.AbsolutePath.TrimEnd('/');
        return host.ToLowerInvariant() + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port) + path + uri.Query;
    }
}
