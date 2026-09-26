using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Aviary;

/// <summary>
/// Pinned tabs: the sites you keep open, held at the left of the strip at a fixed narrow
/// width showing only their icon, and reopened on every launch.
///
/// A pinned tab that does not come back is not pinned, so the URLs are persisted and
/// restored at startup. This is the one piece of session restore that belongs in Phase 4
/// rather than waiting for Beta Build 1 — the rest of session restore is about bringing
/// back what you happened to have open, which is a different promise.
/// </summary>
public sealed partial class MainWindow
{
    private readonly HashSet<BrowserTab> _pinned = new();

    /// <summary>Safari's pinned tabs are icon-only and about this wide.</summary>
    private const double PinnedTabWidth = 44;

    /// <remarks>
    /// This threw on EVERY pin until Phase 5 M0 exercised it in the running app. It removed
    /// the tab from <c>_tabs</c> and then called <c>PinnedCount()</c> — which counts pinned
    /// tabs that are IN <c>_tabs</c> — so the tab it had just removed was no longer counted,
    /// the count came back 0 for the first pin, and <c>Insert(-1)</c> threw. Worse, the throw
    /// landed after the removal, so the crash handler kept the app alive with the tab still
    /// on screen but gone from the list. The insertion point is now taken from the OTHER
    /// pinned tabs, computed once, and clamped.
    /// </remarks>
    private void TogglePinned(BrowserTab tab)
    {
        // Pinned tabs are remembered on disk; a private window remembers nothing.
        if (_private)
        {
            return;
        }

        int from = _tabs.IndexOf(tab);
        if (from < 0)
        {
            return;
        }

        bool pinning = !_pinned.Contains(tab);

        _tabs.RemoveAt(from);

        // With the tab out of the list, this is exactly the count of the OTHER pinned tabs —
        // which is the right slot both ways: the end of the pinned group when pinning, and
        // the first slot after it when unpinning. Moving an unpinned tab to the far end
        // would lose you the tab you were just looking at.
        int pinnedAhead = _tabs.Count(t => _pinned.Contains(t) && !ReferenceEquals(t, tab));

        if (pinning)
        {
            _pinned.Add(tab);
        }
        else
        {
            _pinned.Remove(tab);
        }

        _tabs.Insert(Math.Clamp(pinnedAhead, 0, _tabs.Count), tab);

        Diagnostics.TabPinned(tab.Id, pinning);
        DiagFeature(pinning ? "tab-pin" : "tab-unpin", new { tab = tab.Id, url = tab.Url, from, to = _tabs.IndexOf(tab) });

        SavePinned();
        RebuildTabStrip();
    }

    private int PinnedCount() => _tabs.Count(t => _pinned.Contains(t));

    private void SavePinned()
    {
        // M5 stage 3: a Shift+click window starts with none of the pinned tabs, so its set is not
        // THE set - saving it would replace every pinned tab with this window's one or none.
        if (_linkWindow)
        {
            return;
        }

        try
        {
            List<string> urls = _tabs
                .Where(t => _pinned.Contains(t) && BrowserTab.HostOf(t.Url).Length > 0)
                .Select(t => t.Url)
                .ToList();

            AppSettings.Set("pinnedTabs", JsonSerializer.Serialize(urls));
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "pinned tabs: save");
        }
    }

    /// <summary>Reopens the pinned tabs, in order, before anything else is shown.</summary>
    private async Task RestorePinnedAsync(bool forceDormant = false)
    {
        List<string> urls;

        try
        {
            string raw = AppSettings.Get("pinnedTabs");
            if (string.IsNullOrWhiteSpace(raw))
            {
                return;
            }

            urls = JsonSerializer.Deserialize<List<string>>(raw) ?? new List<string>();
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "pinned tabs: read saved list");
            return;
        }

        // M6: "Open pinned tabs only when clicked" - each comes back with its icon and address and NO
        // engine, and starts the first time it is chosen (M5's dormant tabs, MainWindow.RouseAsync).
        // Read once, here: it decides how THIS launch restores, so the row says "restart".
        // M6 stage 4: also when a forget is owed from the last session - nothing may load a site first.
        bool onClick = AppSettings.GetBool("exp.pinnedOnClick") || forceDormant;

        foreach (string url in urls.Take(12))
        {
            if (BrowserTab.HostOf(url).Length == 0)
            {
                continue;
            }

            if (onClick)
            {
                BrowserTab dormant = CreateDormantTab(url, string.Empty, "pinned tab restored, dormant");
                _pinned.Add(dormant);
                Diagnostics.TabPinned(dormant.Id, true);
                continue;
            }

            BrowserTab? tab = await CreateTabAsync(select: false, reason: "pinned tab restored");
            if (tab is null)
            {
                return;
            }

            _pinned.Add(tab);
            Diagnostics.TabPinned(tab.Id, true);
            tab.Navigate(url);
        }

        RebuildTabStrip();
    }

    /// <summary>
    /// A pinned tab's button: the icon alone, no title, no close glyph.
    ///
    /// Close is deliberately absent rather than hidden — closing a pinned tab by clicking
    /// where its favicon is would undo the pinning by accident, which is exactly what
    /// pinning exists to prevent. The context menu unpins it.
    /// </summary>
    private ToggleButton BuildPinnedTabButton(BrowserTab tab)
    {
        FaviconView icon = new(16, RootGrid.Resources);
        icon.SetSource(tab.Url, tab.Monogram, tab.IsInternalPage);

        Grid content = new();
        content.Children.Add(icon);

        ToggleButton button = new()
        {
            Style = (Style)RootGrid.Resources["AvTabButton"],
            Content = content,
        };

        ToolTipService.SetToolTip(button, tab.Title);
        WireTabAccess(button, tab);

        button.Click += (_, _) =>
        {
            button.IsChecked = true;
            if (!ReferenceEquals(tab, _active))
            {
                SelectTab(tab);
            }
        };

        // M5 stage 4: ContextRequested, not RightTapped - the same menu from a right-click, a long
        // press AND the keyboard (the menu key, Shift+F10). RightTapped alone left a tab's menu - and
        // with it "Move to Tab Group" - out of reach of anyone not using a mouse.
        button.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            ShowTabMenu(tab, button);
        };

        // A TabChrome so OnTabChanged can find the icon without a reach-through, and so
        // the drag handlers see the shape they expect.
        button.Tag = new TabChrome
        {
            Tab = tab,
            Icon = icon,
            Title = new TextBlock(),   // pinned tabs show no title
            ClosePad = new Grid(),
        };

        return button;
    }
}
