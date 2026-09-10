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

    private void OnContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs args)
    {
        try
        {
            // Handled = true suppresses the engine's menu entirely. Without it BOTH menus
            // appear, one over the other.
            args.Handled = true;

            CoreWebView2ContextMenuTarget target = args.ContextMenuTarget;
            MenuFlyout menu = new();
            Themed(menu);

            string linkUri = SafeString(() => target.LinkUri);
            string sourceUri = SafeString(() => target.SourceUri);
            string selection = SafeString(() => target.SelectionText);
            CoreWebView2ContextMenuTargetKind kind = target.Kind;

            // ── a link ──
            if (linkUri.Length > 0)
            {
                Add(menu, "Open Link in New Tab", () => OpenNewTab(linkUri));
                Add(menu, "Add Link to Reading List",
                    () => ReadingList.Add(SafeString(() => target.LinkText), linkUri));
                Add(menu, "Copy Link", () => CopyText(linkUri));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            // ── an image ──
            if (kind == CoreWebView2ContextMenuTargetKind.Image && sourceUri.Length > 0)
            {
                Add(menu, "Open Image in New Tab", () => OpenNewTab(sourceUri));
                Add(menu, "Copy Image Address", () => CopyText(sourceUri));
                menu.Items.Add(new MenuFlyoutSeparator());
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

                string engine = AppSettings.Get("searchEngine");
                Add(menu, "Search " + (engine.Length > 0 ? engine : "the web") + " for “" + trimmed + "”",
                    () => OpenNewTab(_searchPrefix + Uri.EscapeDataString(selection)));
                menu.Items.Add(new MenuFlyoutSeparator());
            }

            // ── the page itself ──
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
        catch (Exception)
        {
            // A failed menu should not take the page down with it.
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
        catch (Exception) { return string.Empty; }
    }

    private static void CopyText(string text)
    {
        try
        {
            Windows.ApplicationModel.DataTransfer.DataPackage package = new();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception)
        {
        }
    }

    // ══ TAB CONTEXT MENU ═══════════════════════════════════════════════════

    private void ShowTabMenu(BrowserTab tab, FrameworkElement anchor)
    {
        MenuFlyout menu = new();
        Themed(menu);

        Add(menu, "New Tab", () => OpenNewTab(null));
        Add(menu, "Duplicate Tab", () => OpenNewTab(tab.Url.Length > 0 ? tab.Url : null));
        menu.Items.Add(new MenuFlyoutSeparator());

        ToggleMenuFlyoutItem pin = new() { Text = "Pin Tab", IsChecked = _pinned.Contains(tab) };
        pin.Click += (_, _) => TogglePinned(tab);
        menu.Items.Add(pin);

        Add(menu, "Add to Reading List",
            () => ReadingList.Add(tab.Title, tab.Url), BrowserTab.HostOf(tab.Url).Length > 0);

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
