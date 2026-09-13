using System;
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

    private void SetFullScreen(bool on, bool byPage)
    {
        if (_appWindow is null || on == _fullScreen)
        {
            return;
        }

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

        _fullScreen = on;
        _fullScreenByPage = on && byPage;

        DiagUi(on ? "full-screen-on" : "full-screen-off", new { by = byPage ? "page" : "F11" });

        ApplyFullScreenChrome();

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
