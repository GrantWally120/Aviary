using System;
using System.Collections.Generic;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// Full screen: F11, and a page's own full-screen request (a video's button).
///
/// The window changes presenter — AppWindow's FullScreen presenter covers the taskbar and
/// drops the caption buttons — and every row of chrome collapses so the page fills the
/// screen edge to edge. Leaving puts back exactly what was there: the tab strip or the
/// sidebar per the tab layout, the favourites bar per its switch, the look's insets.
///
/// Two ways in, tracked apart. F11 is the person's choice and only F11 undoes it. A page's
/// request lasts as long as the page's full-screen element does — Esc or the video's own
/// button — and must NOT end a full screen the person had already chosen with F11.
/// </summary>
public sealed partial class MainWindow
{
    private bool _fullScreen;
    private bool _fullScreenByPage;
    private bool _wasMaximised;

    private void ToggleFullScreen()
    {
        if (_fullScreen && _fullScreenByPage)
        {
            // F11 during a page's full screen: the person wants OUT, so end the page's
            // element full screen as well — otherwise the video still fills the content area
            // and looks stuck.
            _ = _active?.View.CoreWebView2?.ExecuteScriptAsync(
                "document.fullscreenElement && document.exitFullscreen && document.exitFullscreen()");
        }

        SetFullScreen(!_fullScreen, byPage: false);
    }

    /// <summary>A tab's page entered or left element full screen.</summary>
    private void OnTabFullScreenChanged(BrowserTab tab, bool on)
    {
        DiagUi("page-full-screen", new { tab = tab.Id, on, active = ReferenceEquals(tab, _active) });

        if (!ReferenceEquals(tab, _active))
        {
            return;
        }

        if (on)
        {
            if (!_fullScreen)
            {
                SetFullScreen(true, byPage: true);
            }
        }
        else if (_fullScreen && _fullScreenByPage)
        {
            SetFullScreen(false, byPage: true);
        }
    }

    /// <summary>The rows that leave and come back, in the order they sit down the window.</summary>
    private IReadOnlyList<FrameworkElement> FullScreenRows => new FrameworkElement[]
    {
        ToolbarRow,
        TabStripRow,
        BookmarksBar,
    };

    /// <summary>An animated switch is in flight; a second F11 finishes it rather than fighting it.</summary>
    private bool _fullScreenMoving;

    /// <summary>
    /// F11, with the motion M4.6 gave it (Grant: "the animation is bad its just not there for me").
    ///
    /// The switch itself cannot be animated: <see cref="AppWindow.SetPresenter"/> holds the UI
    /// thread for 85-120 ms, which is 5 to 7 frames of nothing moving whatever we ask for. So the
    /// motion is put where the thread is free, and the switch is hidden inside it:
    ///
    ///  - <b>Going in</b> the chrome lifts away first, and the presenter changes on the last frame
    ///    of that animation - by which point there is nothing left on screen for the stall to
    ///    interrupt.
    ///  - <b>Coming out</b> the presenter changes while the page still fills the screen, and the
    ///    chrome settles back down afterwards.
    ///
    /// Reduce motion, or Animation style Off, takes the old instant path.
    /// </summary>
    private void SetFullScreen(bool on, bool byPage)
    {
        if (_appWindow is null || on == _fullScreen)
        {
            return;
        }

        if (_fullScreenMoving)
        {
            // Mid-flight: put the rows down where they belong and switch without ceremony, so a
            // fast double press cannot leave the chrome hanging above the window.
            Motion.SettleNow(FullScreenRows);
            _fullScreenMoving = false;
            SwitchFullScreen(on, byPage);
            return;
        }

        if (!Motion.On)
        {
            SwitchFullScreen(on, byPage);
            return;
        }

        long moveStart = System.Diagnostics.Stopwatch.GetTimestamp();
        _fullScreenMoving = true;

        if (on)
        {
            Motion.LiftAway(FullScreenRows, () =>
            {
                _fullScreenMoving = false;
                SwitchFullScreen(true, byPage,
                    Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(moveStart).TotalMilliseconds, 1));
            });

            return;
        }

        SwitchFullScreen(false, byPage,
            Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(moveStart).TotalMilliseconds, 1));

        // The rows are visible and laid out again by now; start them above and let them settle.
        _fullScreenMoving = false;
        Motion.SettleBack(FullScreenRows);
    }

    private void SwitchFullScreen(bool on, bool byPage, double animateMs = 0)
    {
        if (_appWindow is null || on == _fullScreen)
        {
            return;
        }

        long switchStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            if (on)
            {
                _wasMaximised = _appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
                _appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else
            {
                _appWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
                if (_wasMaximised && _appWindow.Presenter is OverlappedPresenter overlapped)
                {
                    overlapped.Maximize();
                }
            }
        }
        catch (Exception ex)
        {
            // A presenter the system will not grant leaves the window as it was.
            Diagnostics.Swallowed(ex, "window: full-screen presenter");
            return;
        }

        // Timed for M7 (M4.5 found it): leaving full screen stalls the UI thread 60-110 ms, and
        // 85-110 ms of it is inside the Windows App SDK's own AppWindow.SetPresenter — Aviary's
        // part is about 3 ms, and handing back the original presenter object does not help.
        double presenterMs = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(switchStart).TotalMilliseconds, 1);

        _fullScreen = on;
        _fullScreenByPage = on && byPage;

        DiagUi(on ? "full-screen-on" : "full-screen-off", new { by = byPage ? "page" : "F11" });

        long chromeStart = System.Diagnostics.Stopwatch.GetTimestamp();
        ApplyFullScreenChrome();
        DiagPerf("full-screen", new
        {
            on,
            presenterMs,
            chromeMs = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(chromeStart).TotalMilliseconds, 1),

            // M4.6: how long the motion ran before the switch (going in) or before the rows came
            // back (coming out). presenterMs landing inside animateMs is the whole point.
            animateMs,
        });

        if (on)
        {
            ShowFullScreenHint(byPage ? "Press Esc to exit full screen" : "Press F11 to exit full screen");
        }
        else
        {
            HideFullScreenHint();
        }
    }

    /// <summary>
    /// Collapses or restores every row of chrome. The restoring half goes through the same
    /// code the rows normally answer to, so full screen can never leave one in a state its
    /// own setting would not have produced.
    /// </summary>
    private void ApplyFullScreenChrome()
    {
        Visibility chrome = _fullScreen ? Visibility.Collapsed : Visibility.Visible;
        ToolbarRow.Visibility = chrome;

        if (_fullScreen)
        {
            TabStripRow.Visibility = Visibility.Collapsed;
            BookmarksBar.Visibility = Visibility.Collapsed;
            SidebarRoot.Visibility = Visibility.Collapsed;
            SidebarGripBar.Visibility = Visibility.Collapsed;
            ContentHost.Margin = new Thickness(0);
            ContentHost.CornerRadius = new CornerRadius(0);
            return;
        }

        if (_sidebarVisible)
        {
            SidebarRoot.Visibility = Visibility.Visible;
            SidebarGripBar.Visibility = Visibility.Visible;
            SidebarRoot.Width = _sidebarWidth;
        }

        ApplyTabLayout();
        RefreshBookmarksBar();
        ApplyLookGeometry(AppSettings.Get("look").Equals("Liquid Glass", StringComparison.OrdinalIgnoreCase));
        UpdateCaptionSpacer();
    }

    private DispatcherTimer? _hintTimer;

    private void ShowFullScreenHint(string text)
    {
        FullScreenHintText.Text = text;
        FullScreenHint.Visibility = Visibility.Visible;
        Motion.SlideIn(FullScreenHint, 14);
        FadeTo(FullScreenHint, 1, 180);

        _hintTimer?.Stop();
        _hintTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer?.Stop();
            HideFullScreenHint();
        };
        _hintTimer.Start();
    }

    private void HideFullScreenHint()
    {
        _hintTimer?.Stop();
        FadeTo(FullScreenHint, 0, 220);
    }
}
