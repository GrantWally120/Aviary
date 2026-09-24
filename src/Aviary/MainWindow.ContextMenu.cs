using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Aviary;

/// <summary>
/// Aviary's own context menus, for the page and for a tab.
///
/// Left alone, right-clicking a page gives you Chromium's menu — which on WebView2 is
/// Edge's, complete with "Search the web for…" pointing at Bing, "Add to Collections", and
/// Edge's own iconography. It is the single loudest remaining tell that this is not the
/// browser it looks like, and it appears every time anyone right-clicks anything.
///
/// So the engine's menu is suppressed and rebuilt as a WinUI MenuFlyout with Safari's own
/// wording and Safari's own ordering.
/// </summary>
public sealed partial class MainWindow
{
    private void HookContextMenu(BrowserTab tab)
    {
        if (tab.View.CoreWebView2 is not CoreWebView2 core)
        {
            return;
        }

        core.ContextMenuRequested -= OnContextMenuRequested;
        core.ContextMenuRequested += OnContextMenuRequested;
    }

    /// <summary>
    /// async void on purpose: the event is raised by the engine, so there is no caller to hand a
    /// Task to. Everything inside is in one try, and a right-click on a video is the only path that
    /// awaits anything (M4.9, picture in picture).
    /// </summary>
    private async void OnContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        Windows.Foundation.Deferral? deferral = null;
        try
        {
            // Handled = true suppresses the engine's menu entirely. Without it BOTH menus
            // appear, one over the other.
            args.Handled = true;

            CoreWebView2ContextMenuTarget target = args.ContextMenuTarget;
            MenuFlyout menu = new();
            Themed(menu, "page (right-click)");

            // Each property THROWS ("a method was called at an unexpected time") unless its Has…
            // flag is set, so a plain right-click used to raise three exceptions and lean on the
            // catch to survive them. Diagnostics showed it on its first run (M1).
            // SourceUri is read only for an image, the one kind it is used for: on plain page
            // text HasSourceUri has been seen to answer true and SourceUri to throw anyway.
            CoreWebView2ContextMenuTargetKind kind = target.Kind;
            string linkUri = target.HasLinkUri ? SafeString(() => target.LinkUri) : string.Empty;
            string sourceUri = kind == CoreWebView2ContextMenuTargetKind.Image && target.HasSourceUri
                ? SafeString(() => target.SourceUri)
                : string.Empty;
            string selection = target.HasSelection ? SafeString(() => target.SelectionText) : string.Empty;

            // What was right-clicked, not the selected text itself — that stays out unless it
            // is actually searched for, when it becomes a search like any other.
            DiagUi("page-menu", new
            {
                target = kind.ToString(),
                link = linkUri.Length > 0 ? linkUri : null,
                image = kind == CoreWebView2ContextMenuTargetKind.Image ? sourceUri : null,
                hasSelection = selection.Length > 0,
            });

            // ── a link ──
            if (linkUri.Length > 0)
            {
                Add(menu, "Open Link in New Tab", () => OpenNewTab(linkUri));
                Add(menu, "Add Link to Reading List",
                    () => ReadingList.Add(target.HasLinkText ? SafeString(() => target.LinkText) : string.Empty, linkUri, record: !_private));
                // Safari's "Copy Link" drops tracking parameters too (Link Tracking
                // Protection): what you paste somewhere else should not carry your click ID.
                Add(menu, "Copy Link", () => CopyText(
                    AppSettings.GetBool("stripTracking") && LinkCleaner.TryClean(linkUri, out string clean, out _)
                        ? clean
                        : linkUri));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            // ── an image ──
            if (kind == CoreWebView2ContextMenuTargetKind.Image && sourceUri.Length > 0)
            {
                Add(menu, "Open Image in New Tab", () => OpenNewTab(sourceUri));
                Add(menu, "Copy Image Address", () => CopyText(sourceUri));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            // ── a video (M4.9) ──
            // Only here is the page asked anything, and only here is the menu held back while it
            // answers: every other right-click builds and shows exactly as it did before.
            if (kind == CoreWebView2ContextMenuTargetKind.Video)
            {
                deferral = args.GetDeferral();
                double vx = args.Location.X, vy = args.Location.Y;
                string? pip = await PipStateAsync(sender, vx, vy);
                if (pip is not null)
                {
                    Add(menu, pip == "in" ? "Leave Picture in Picture" : "Picture in Picture",
                        () => _ = TogglePictureInPictureAsync(sender, vx, vy));
                    menu.Items.Add(new MenuFlyoutSeparator());
                }
            }

            // ── a selection ──
            if (selection.Length > 0)
            {
                Add(menu, "Copy", () => CopyText(selection));

                string trimmed = selection.Trim();
                if (trimmed.Length > 40)
                {
                    trimmed = trimmed.Substring(0, 40).TrimEnd() + "…";
                }

                // The engine that will actually answer — a junk saved value falls back to the default.
                Add(menu, "Search " + SearchEngines.Current.Name + " for “" + trimmed + "”",
                    () => OpenNewTab(SearchPrefix + Uri.EscapeDataString(selection)));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            // ── the page itself ──
            // A video keeps the page's own items too (M4.9): right-clicking one gave Back, Reload,
            // Add Bookmark and Find in Page before picture in picture existed, and taking them away
            // would leave a video whose site refuses picture in picture with no menu at all.
            if (linkUri.Length == 0 && selection.Length == 0
                && kind != CoreWebView2ContextMenuTargetKind.Image)
            {
                Add(menu, "Back", () => BackButton_Click(this, new RoutedEventArgs()),
                    _active?.CanGoBack ?? false);
                Add(menu, "Forward", () => ForwardButton_Click(this, new RoutedEventArgs()),
                    _active?.CanGoForward ?? false);
                Add(menu, "Reload", ReloadOrStop);
                menu.Items.Add(new MenuFlyoutSeparator());

                if (_readerAvailable || _readerActive)
                {
                    Add(menu, _readerActive ? "Hide Reader" : "Show Reader", ToggleReader);
                }

                Add(menu, "Add to Reading List", AddToReadingList);
                Add(menu, "Add Bookmark", AddBookmark);
                menu.Items.Add(new MenuFlyoutSeparator());
                Add(menu, "Find in Page…", OpenFindBar);
            }

            if (menu.Items.Count == 0)
            {
                return;
            }

            // Trailing separators look like a menu that lost its last item.
            while (menu.Items.Count > 0 && menu.Items[^1] is MenuFlyoutSeparator)
            {
                menu.Items.RemoveAt(menu.Items.Count - 1);
            }

            // The engine reports the point in the WEB VIEW's own coordinates, so it has to
            // be offset by wherever the WebView sits inside the window.
            Point where = new(args.Location.X, args.Location.Y);
            menu.ShowAt(ContentHost, new FlyoutShowOptions
            {
                Position = where,
                Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            });
        }
        catch (Exception ex)
        {
            // A failed menu should not take the page down with it.
            Diagnostics.Swallowed(ex, "page menu: build");
        }
        finally
        {
            // Not completing a deferral leaves the engine waiting for a menu that never comes.
            try { deferral?.Complete(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "page menu: deferral"); }
        }
    }

    private static void Add(MenuFlyout menu, string text, Action action, bool enabled = true)
    {
        MenuFlyoutItem item = new() { Text = text, IsEnabled = enabled };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static string SafeString(Func<string?> read)
    {
        try { return read() ?? string.Empty; }
        catch (Exception ex) { Diagnostics.Swallowed(ex, "page menu: read target"); return string.Empty; }
    }

    /// <summary>
    /// Diagnostics records THAT something was copied (the menu item), never what: clipboard
    /// contents are on the never-collected list.
    /// </summary>
    private static void CopyText(string text)
    {
        try
        {
            Windows.ApplicationModel.DataTransfer.DataPackage package = new();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "page menu: copy to clipboard");
        }
    }

    // ══ TAB CONTEXT MENU ═══════════════════════════════════════════════════

    private void ShowTabMenu(BrowserTab tab, FrameworkElement anchor)
    {
        MenuFlyout menu = new();
        Themed(menu, "tab");

        Add(menu, "New Tab", () => OpenNewTab(null));
        Add(menu, "Duplicate Tab", () => OpenNewTab(tab.Url.Length > 0 ? tab.Url : null));
        menu.Items.Add(new MenuFlyoutSeparator());

        ToggleMenuFlyoutItem pin = new() { Text = "Pin Tab", IsChecked = _pinned.Contains(tab), IsEnabled = !_private };
        pin.Click += (_, _) => TogglePinned(tab);
        menu.Items.Add(pin);

        Add(menu, "Add to Reading List",
            () => ReadingList.Add(tab.Title, tab.Url, record: !_private), BrowserTab.HostOf(tab.Url).Length > 0);

        menu.Items.Add(new MenuFlyoutSeparator());
        Add(menu, "Close Tab", () => CloseTab(tab));
        Add(menu, "Close Other Tabs", () => CloseOthers(tab), _tabs.Count > 1);

        menu.ShowAt(anchor);
    }

    private void CloseOthers(BrowserTab keep)
    {
        foreach (BrowserTab tab in new List<BrowserTab>(_tabs))
        {
            if (!ReferenceEquals(tab, keep))
            {
                CloseTab(tab);
            }
        }
    }
}
