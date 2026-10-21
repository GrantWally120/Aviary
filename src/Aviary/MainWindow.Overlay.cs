using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Settings → Experimental → "Show speed and memory on top of pages" (Alpha Build 5 M6 stage 2): a
/// small readout in the corner of every window — all of Aviary's memory, the tab in front, the
/// processor and the graphics card — from the SAME sampler as Settings → Performance
/// (<see cref="LiveStats"/>, now able to feed both at once), at the rate that page's "How often to
/// measure" row sets.
///
/// <para>Drawn by Aviary, in the window, OVER the page — never put into the page, so no website can
/// see it, read it or be slowed by it — and it takes no clicks (<c>IsHitTestVisible</c> off). While
/// the row is off nothing runs at all: no sampler, no process watching.</para>
/// </summary>
public sealed partial class MainWindow
{
    private Border? _overlay;
    private TextBlock? _overlayText;

    private static bool _overlayRunning;
    private static CoreWebView2Environment? _overlayEnvironment;
    private static Dictionary<int, List<BrowserTab>> _overlayTabsByPid = new();

    public static bool PerfOverlayOn => AppSettings.GetBool("exp.perfOverlay");

    /// <summary>On or off in every window, starting or stopping the sampler to match. From a window loading and from the row.</summary>
    private static async Task ApplyPerfOverlayAsync()
    {
        bool on = PerfOverlayOn && OpenWindows.Count > 0;
        foreach (MainWindow w in OpenWindows.ToList())
        {
            w.ShowOverlay(on);
        }

        try
        {
            if (on && !_overlayRunning)
            {
                _overlayRunning = true;
                int generation = ++_overlayGeneration;
                CoreWebView2Environment environment = await WebViewEnvironment.GetAsync();
                Dictionary<int, List<BrowserTab>> tabsByPid = await RefreshEngineProcessesAsync(environment);

                // Switched off (or off and on again) while those were asked: whoever did that has
                // already stopped or started it - starting here as well would leave a sampler running
                // with the row off, or listening to the engine twice.
                if (generation != _overlayGeneration)
                {
                    return;
                }

                _overlayEnvironment = environment;
                _overlayEnvironment.ProcessInfosChanged += OnOverlayProcessesChanged;
                _overlayTabsByPid = tabsByPid;
                LiveStats.Start("overlay", reading =>
                {
                    // On the sampler's thread; every window shares the one UI thread.
                    OpenWindows.FirstOrDefault()?.DispatcherQueue.TryEnqueue(() =>
                    {
                        foreach (MainWindow w in OpenWindows.ToList())
                        {
                            w.UpdateOverlay(reading);
                        }
                    });
                });
                DiagFeatureStatic("perf-overlay", new { on = true });
            }
            else if (!on && _overlayRunning)
            {
                StopPerfOverlay();
                DiagFeatureStatic("perf-overlay", new { on = false });
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "overlay: start");
        }
    }

    /// <summary>Counts every start and stop, so a start still waiting on the engine knows it was overtaken.</summary>
    private static int _overlayGeneration;

    private static void StopPerfOverlay()
    {
        _overlayGeneration++;
        _overlayRunning = false;
        LiveStats.Stop("overlay");
        if (_overlayEnvironment is not null)
        {
            _overlayEnvironment.ProcessInfosChanged -= OnOverlayProcessesChanged;
            _overlayEnvironment = null;
        }
    }

    private static void OnOverlayProcessesChanged(CoreWebView2Environment sender, object args) =>
        OpenWindows.FirstOrDefault()?.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                if (_overlayRunning)
                {
                    _overlayTabsByPid = await RefreshEngineProcessesAsync(sender);
                }
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "overlay: engine processes changed");
            }
        });

    private static void DiagFeatureStatic(string name, object data) => Diagnostics.Feature(name, data);

    private void ShowOverlay(bool on)
    {
        if (!on)
        {
            if (_overlay is not null)
            {
                _overlay.Visibility = Visibility.Collapsed;
            }

            return;
        }

        if (_overlay is null)
        {
            _overlayText = new TextBlock
            {
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 11,
                LineHeight = 16,
                Foreground = new SolidColorBrush(ColorHelper.FromArgb(0xEE, 0xFF, 0xFF, 0xFF)),
                Text = "Measuring…",
            };
            _overlay = new Border
            {
                Child = _overlayText,
                Background = new SolidColorBrush(ColorHelper.FromArgb(0xCC, 0x1C, 0x1C, 0x1E)),
                // A faint edge, so the box still reads as a box on a dark page (found 25 Sep, the dark Start Page).
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 0, 14, 14),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                IsHitTestVisible = false,
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_overlay, "PerfOverlay");
            Canvas.SetZIndex(_overlay, 1000);
            ContentHost.Children.Add(_overlay);
        }

        _overlay.Visibility = Visibility.Visible;
    }

    private void UpdateOverlay(LiveStats.Reading r)
    {
        if (_overlayText is null || _overlay?.Visibility != Visibility.Visible)
        {
            return;
        }

        // The tab in front: the page process that draws it (several tabs of one site can share one).
        string thisTab = "—";
        if (_active is { } tab)
        {
            bool found = false;
            foreach ((int pid, List<BrowserTab> tabs) in _overlayTabsByPid)
            {
                if (tabs.Contains(tab) && r.Processes.FirstOrDefault(p => p.Pid == pid) is { } f)
                {
                    thisTab = f.MemoryAvailable ? Mb(f.MemoryMB) + (tabs.Count > 1 ? " (shared)" : string.Empty) : "—";
                    found = true;
                    break;
                }
            }

            // Not in the map: the engine announces a new page process when it STARTS, before the tab's
            // page has moved into it (found 25 Sep - a pinned tab just clicked read "—"), and says
            // nothing when it does. So ask again; one question at a time.
            if (!found && tab.IsReady && !_overlayRemapping && _overlayEnvironment is { } environment)
            {
                _overlayRemapping = true;
                _ = RemapOverlayAsync(environment);
            }
        }

        _overlayText.Text =
            "Aviary     " + (r.MemoryAvailable ? Mb(r.MemoryMB) : "—") + "\n" +
            "This tab   " + thisTab + "\n" +
            "Processor  " + (r.CpuReady ? Math.Round(r.CpuPercent, 1).ToString("0.0") + " %" : "Measuring…") +
            (r.GpuAvailable ? "\nGraphics   " + Math.Round(r.GpuPercent, 1).ToString("0.0") + " %" : string.Empty);

        // The readout is NOT in the UI Automation tree - nothing laid over the web view is, and it is
        // not IsHitTestVisible (measured both ways, 25 Sep) - so a screen reader cannot read it, and
        // neither can a test. The row says so; this lets a test read what was drawn. Only from
        // Aviary's own environment, like AVIARY_MOTION_TEST: nothing inside the browser can set it.
        if (OverlayTest)
        {
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(AppPaths.Root, "overlay-test.txt"),
                    DateTimeOffset.Now.ToString("HH:mm:ss.fff") + "\n" + _overlayText.Text);
            }
            catch (Exception)
            {
                // A test hook; a missed write only means a stale file.
            }
        }
    }

    private static readonly bool OverlayTest = Environment.GetEnvironmentVariable("AVIARY_OVERLAY_TEST") == "1";

    private static bool _overlayRemapping;

    private static async Task RemapOverlayAsync(CoreWebView2Environment environment)
    {
        try
        {
            _overlayTabsByPid = await RefreshEngineProcessesAsync(environment);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "overlay: find the tab's process");
        }
        finally
        {
            _overlayRemapping = false;
        }
    }

    private static string Mb(double mb)
    {
        if (!double.IsFinite(mb) || mb < 0) { return "—"; }
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = mb * 1048576;
        int unit = 0;
        while (value >= 1000 && unit < units.Length - 1) { value /= 1000; unit++; }
        if (unit > 0 && Math.Round(value, value < 10 ? 2 : 1, MidpointRounding.AwayFromZero) >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }
        return value.ToString(unit == 0 ? "0" : value < 10 ? "0.00" : "0.0") + " " + units[unit];
    }
}
