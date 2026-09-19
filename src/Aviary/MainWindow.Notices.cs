using System;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Aviary;

/// <summary>
/// What a tab shows in place of its page: a page that did not load, a site with no secure
/// connection, a site Strict stopped, a page whose renderer died, an engine that would not start.
/// </summary>
internal sealed class TabNotice
{
    /// <summary>For diagnostics: failed, https-fallback, site-blocked, page-gone, engine-start.</summary>
    public required string Kind { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }

    /// <summary>The right-hand button (Continue to Site, Open Anyway, Reload Page); null = none.</summary>
    public string? Action { get; init; }

    /// <summary>The left-hand Go Back button, and whether it can be pressed.</summary>
    public bool Back { get; init; }
    public bool BackEnabled { get; init; }

    /// <summary>HTTPS-Only's insecure address, for Continue to Site.</summary>
    public string? FallbackUrl { get; init; }

    /// <summary>The site Strict stopped, for Open Anyway.</summary>
    public string? BlockedUrl { get; init; }
}

/// <summary>
/// Aviary's panel, one per tab (bug 65, 18 Sep 2026).
///
/// It used to be one panel for the window, shown for whichever tab was on screen at the moment
/// something went wrong and hidden only by the next typed address. So switching tabs left it over
/// the tab you switched TO; switching back showed Chromium's own error page — Edge logo and all
/// (bug 9's class) — because the tab's WebView was made visible again; and a tab in the background
/// got no panel at all: Strict stopping a site there, or HTTPS-Only finding no secure connection,
/// happened silently (HANDOFF §8). Now each tab carries its <see cref="TabNotice"/>: set whether or
/// not the tab is on screen, drawn by <see cref="RenderNotice"/> whenever it is — on arrival and on
/// every switch to it — and cleared when the tab starts a new navigation of its own.
/// </summary>
public sealed partial class MainWindow
{
    private void ShowNotice(BrowserTab tab, TabNotice notice)
    {
        tab.Notice = notice;
        OnTabChanged(tab);   // its label and the window title go by the panel now (ShownTitle)

        // What the person actually SAW (or will, on switching to the tab) — the navigation line
        // records the raw status.
        DiagUi("error-page-shown", new
        {
            kind = notice.Kind,
            title = notice.Title,
            detail = notice.Detail,
            url = tab.Url,
            tab = tab.Id,
            onScreen = ReferenceEquals(tab, _active),
        });

        if (ReferenceEquals(tab, _active))
        {
            RenderNotice(tab);
        }
    }

    private void ClearNotice(BrowserTab? tab)
    {
        if (tab is null)
        {
            return;
        }

        if (tab.Notice is null)
        {
            return;
        }

        tab.Notice = null;
        OnTabChanged(tab);
        if (ReferenceEquals(tab, _active))
        {
            RenderNotice(tab);
        }
    }

    /// <summary>The panel for the tab on screen: its notice, or none and its page.</summary>
    private void RenderNotice(BrowserTab? tab)
    {
        if (tab?.Notice is not { } notice)
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            ErrorActions.Visibility = Visibility.Collapsed;
            if (tab is not null)
            {
                tab.View.Visibility = Visibility.Visible;
            }

            return;
        }

        ErrorTitle.Text = notice.Title;
        ErrorDetail.Text = notice.Detail;
        ErrorBackButton.Visibility = notice.Back ? Visibility.Visible : Visibility.Collapsed;
        ErrorBackButton.IsEnabled = notice.BackEnabled;
        ErrorContinueButton.Visibility = notice.Action is null ? Visibility.Collapsed : Visibility.Visible;
        ErrorContinueButton.Content = notice.Action ?? string.Empty;
        ErrorActions.Visibility = notice.Back || notice.Action is not null ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;

        // Hide the WebView, or Chromium's own page shows behind ours — complete with a Microsoft
        // Edge logo, which has no business appearing in this browser.
        tab.View.Visibility = Visibility.Collapsed;
    }

    private void ErrorBack_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { Notice: { } notice } tab)
        {
            return;
        }

        // Strict's warning: the stopped navigation never left the page that was showing, so
        // going back is simply putting that page on screen again.
        if (notice.BlockedUrl is not null)
        {
            DiagUi("site-blocked-back");
            ClearNotice(tab);
            return;
        }

        DiagUi("https-fallback-back");
        ClearNotice(tab);
        if (tab.CanGoBack)
        {
            tab.View.GoBack();
        }
    }

    private void ErrorContinue_Click(object sender, RoutedEventArgs e)
    {
        if (_active is not { Notice: { } notice } tab)
        {
            return;
        }

        if (notice.BlockedUrl is { } blocked)
        {
            DiagUi("site-blocked-open-anyway");
            ClearNotice(tab);
            tab.OpenBlockedSite(blocked);
            return;
        }

        if (notice.Kind == "page-gone")
        {
            DiagUi("page-gone-reload", new { tab = tab.Id });
            ClearNotice(tab);
            tab.View.Reload();
            return;
        }

        if (notice.FallbackUrl is { } url)
        {
            DiagUi("https-fallback-continue");
            ClearNotice(tab);
            tab.ContinueInsecure(url);
        }
    }

    /// <summary>
    /// The renderer showing this tab's page died (a crash, or ended from outside). Chromium's
    /// answer is its own "This page is having a problem" page, which is Edge's, not Aviary's
    /// (HANDOFF §8). Reload brings the page back in a new renderer.
    /// </summary>
    private void OnPageGone(BrowserTab tab, string reason)
    {
        string why = reason switch
        {
            nameof(CoreWebView2ProcessFailedReason.Crashed) => "crashed",
            nameof(CoreWebView2ProcessFailedReason.OutOfMemory) => "ran out of memory",
            nameof(CoreWebView2ProcessFailedReason.Terminated) => "was ended from outside Aviary",
            nameof(CoreWebView2ProcessFailedReason.LaunchFailed) => "couldn't start",
            _ => "stopped",
        };

        ShowNotice(tab, new TabNotice
        {
            Kind = "page-gone",
            Title = "This page stopped working",
            Detail = "The part of Aviary that was showing this page " + why + ". Nothing else is "
                   + "affected, and reloading usually brings the page straight back — though anything "
                   + "you had typed into it may be gone.",
            Action = "Reload Page",
        });
    }
}
