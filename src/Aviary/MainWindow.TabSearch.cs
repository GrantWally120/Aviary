using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Aviary;

/// <summary>
/// Search your open tabs (Alpha Build 5 M4.9, one of Grant's five picks from Brave, Safari and
/// Chrome): Ctrl+Shift+A, as in Chrome, or the app menu's "Search Tabs…". Type, and every open tab
/// whose title or address holds every word you typed is listed; Up and Down move, Enter goes there.
///
/// <para>Every window's tabs are listed — Chrome's does the same, and it is the point when a tab
/// is lost somewhere — but only windows of the SAME kind: a normal window never lists what is open
/// in a private one, nor a private window what is open in a normal one. The two are meant not to
/// know about each other.</para>
///
/// <para>One more popover through <see cref="ThemeOpenPopups"/>, so it takes the theme and the
/// arrival every other one does (and, since M4.7, stays clickable).</para>
/// </summary>
public sealed partial class MainWindow
{
    private Flyout? _tabSearch;

    /// <summary>One listed tab, and the window that holds it.</summary>
    private sealed record TabHit(MainWindow Window, BrowserTab Tab, string Title, string Address, bool Here);

    private void OpenTabSearch()
    {
        if (_tabSearch is not null)
        {
            return;
        }

        List<TabHit> all = new();
        foreach (MainWindow window in OpenWindows.OrderBy(w => ReferenceEquals(w, this) ? 0 : 1))
        {
            if (window._private != _private)
            {
                continue;
            }

            foreach (BrowserTab tab in window._tabs)
            {
                string title = ShownTitle(tab);
                all.Add(new TabHit(window, tab, string.IsNullOrWhiteSpace(title) ? "Untitled" : title,
                    tab.IsInternalPage ? "Aviary" : tab.Url ?? string.Empty, ReferenceEquals(window, this)));
            }
        }

        Brush label = ThemeBrush("AvLabelBrush");
        Brush secondary = ThemeBrush("AvLabelSecondaryBrush");

        TextBox box = new()
        {
            PlaceholderText = "Search your open tabs",
            MinWidth = 440,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "Search your open tabs");

        TextBlock count = new() { Foreground = secondary, FontSize = 12, Margin = new Thickness(2, 8, 0, 4) };

        ListView list = new()
        {
            SelectionMode = ListViewSelectionMode.Single,
            IsItemClickEnabled = true,
            MaxHeight = 380,
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(list, "Open tabs");

        StackPanel panel = new() { Width = 460 };
        panel.Children.Add(box);
        panel.Children.Add(count);
        panel.Children.Add(list);

        Flyout flyout = new() { Placement = FlyoutPlacementMode.Bottom, Content = panel };
        _tabSearch = flyout;

        void Fill()
        {
            string[] words = box.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<TabHit> hits = all
                .Where(h => words.All(w => h.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase)
                                        || h.Address.Contains(w, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            list.Items.Clear();
            foreach (TabHit hit in hits)
            {
                // Tag on both: ItemClick can hand over the container or its content.
                StackPanel row = new() { Padding = new Thickness(2, 4, 2, 4), Tag = hit };
                row.Children.Add(new TextBlock
                {
                    Text = hit.Title,
                    Foreground = label,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                row.Children.Add(new TextBlock
                {
                    Text = BrowserTab.HostOf(hit.Address) is { Length: > 0 } host && !hit.Tab.IsInternalPage
                        ? host + (hit.Here ? string.Empty : " · another window")
                        : hit.Address + (hit.Here ? string.Empty : " · another window"),
                    Foreground = secondary,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });

                ListViewItem item = new() { Content = row, Tag = hit };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, hit.Title);
                list.Items.Add(item);
            }

            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }

            count.Text = hits.Count == 0 ? "No open tab matches."
                : words.Length == 0 ? all.Count + (all.Count == 1 ? " open tab" : " open tabs")
                : hits.Count + " of " + all.Count + " tabs";
        }

        void Go(TabHit? hit)
        {
            if (hit is null)
            {
                return;
            }

            DiagUi("tab-search-go", new
            {
                query = box.Text,
                shown = list.Items.Count,
                of = all.Count,
                otherWindow = !hit.Here,
            });

            flyout.Hide();
            hit.Window.SelectTab(hit.Tab);
            if (!hit.Here)
            {
                hit.Window.Activate();
            }
        }

        box.TextChanged += (_, _) => Fill();
        box.KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case VirtualKey.Down when list.Items.Count > 0:
                    list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
                    list.ScrollIntoView(list.SelectedItem);
                    e.Handled = true;
                    break;
                case VirtualKey.Up when list.Items.Count > 0:
                    list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1);
                    list.ScrollIntoView(list.SelectedItem);
                    e.Handled = true;
                    break;
                case VirtualKey.Enter:
                    Go((list.SelectedItem as ListViewItem)?.Tag as TabHit);
                    e.Handled = true;
                    break;
            }
        };
        list.ItemClick += (_, e) => Go((e.ClickedItem as ListViewItem)?.Tag as TabHit
                                       ?? (e.ClickedItem as FrameworkElement)?.Tag as TabHit);

        flyout.Opened += (_, _) =>
        {
            ThemeOpenPopups();
            DiagUi("menu-open", new { menu = "tab-search", tabs = all.Count });
            box.Focus(FocusState.Programmatic);
        };
        flyout.Closed += (_, _) => _tabSearch = null;

        Fill();
        flyout.ShowAt(AddressBox);
    }
}
