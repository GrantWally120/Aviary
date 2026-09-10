using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Aviary;

/// <summary>Which list the one sidebar surface is currently showing.</summary>
internal enum SidebarPane
{
    Tabs,
    Bookmarks,
    ReadingList,
    History,
}

/// <summary>
/// The sidebar: one surface, four panes.
///
/// Safari does not have a separate "vertical tabs" feature — it has a sidebar whose Tab
/// Groups segment lists the window's tabs as rows. Building two panels here would be two
/// things to keep in step and would not look like Safari either. So the horizontal strip
/// collapses, the same tab buttons are re-parented into the sidebar, and the segmented
/// control at the top swaps what the list below is showing.
/// </summary>
public sealed partial class MainWindow
{
    private const double SidebarMinWidth = 180;
    private const double SidebarMaxWidth = 420;
    private const double SidebarDefaultWidth = 250;
    private const double SidebarRowHeight = 30;

    private SidebarPane _pane = SidebarPane.Tabs;
    private bool _sidebarVisible;
    private double _sidebarWidth = SidebarDefaultWidth;

    private bool _gripDragging;
    private double _gripStartX;
    private double _gripStartWidth;

    private BrowserTab? _vDragTab;
    private double _vDragStartY;
    private bool _vDragging;

    private bool UsingSidebarTabs =>
        AppSettings.Get("tabLayout").Equals("Sidebar", StringComparison.OrdinalIgnoreCase);

    // ══ SET-UP ═════════════════════════════════════════════════════════════

    private void InitialiseSidebar()
    {
        BuildSegments();

        _sidebarWidth = ReadDouble(AppSettings.Get("sidebarWidth"), SidebarDefaultWidth);
        _sidebarWidth = Math.Clamp(_sidebarWidth, SidebarMinWidth, SidebarMaxWidth);
        SidebarRoot.Width = _sidebarWidth;

        _pane = AppSettings.Get("sidebarPane") switch
        {
            "Bookmarks" => SidebarPane.Bookmarks,
            "Reading List" => SidebarPane.ReadingList,
            "History" => SidebarPane.History,
            _ => SidebarPane.Tabs,
        };

        PageSettingsButton.IsEnabled = true;
        PageSettingsButton.Click += PageSettingsButton_Click;

        SidebarButton.IsEnabled = true;
        ToolTipService.SetToolTip(SidebarButton, "Show sidebar (Ctrl+Shift+L)");
        SidebarButton.Click += (_, _) => ToggleSidebar(null);

        SidebarGripBar.AddHandler(UIElement.PointerPressedEvent,
            new PointerEventHandler(Grip_PointerPressed), true);
        SidebarGripBar.AddHandler(UIElement.PointerMovedEvent,
            new PointerEventHandler(Grip_PointerMoved), true);
        SidebarGripBar.AddHandler(UIElement.PointerReleasedEvent,
            new PointerEventHandler(Grip_PointerReleased), true);
        SidebarGripBar.AddHandler(UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(Grip_PointerReleased), true);
        SidebarGripBar.DoubleTapped += (_, _) => SetSidebarWidth(SidebarDefaultWidth, persist: true);

        // -= before += so a second RestorePreferences (the settings reset path) cannot
        // leave two handlers rebuilding the same list twice per change.
        Bookmarks.Changed -= OnSidebarSourceChanged;
        Bookmarks.Changed += OnSidebarSourceChanged;
        ReadingList.Changed -= OnSidebarSourceChanged;
        ReadingList.Changed += OnSidebarSourceChanged;
        History.Changed -= OnSidebarSourceChanged;
        History.Changed += OnSidebarSourceChanged;

        // Restored last, so the strip and the sidebar agree before anything is drawn.
        bool wanted = AppSettings.GetBool("sidebarVisible") || UsingSidebarTabs;
        if (wanted)
        {
            ToggleSidebar(true, animate: false);
        }
        else
        {
            ApplyTabLayout();
        }
    }

    private void OnSidebarSourceChanged()
    {
        if (_sidebarVisible && _pane != SidebarPane.Tabs)
        {
            RefreshSidebar();
        }
    }

    private static double ReadDouble(string text, double fallback) =>
        double.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : fallback;

    /// <summary>
    /// Reads a geometry value back out of the CURRENTLY merged dictionary.
    ///
    /// StaticResource resolves once at load, so anything that differs between the looks has
    /// to be re-applied by hand — but duplicating the numbers in C# is how Classic and
    /// Liquid Glass quietly disagree after the third swap. Read them from the dictionary
    /// that is actually loaded instead.
    /// </summary>
    private static T Res<T>(string key, T fallback)
    {
        try
        {
            if (Application.Current.Resources.TryGetValue(key, out object? value) && value is T typed)
            {
                return typed;
            }
        }
        catch (Exception)
        {
        }

        return fallback;
    }

    // ══ THE SEGMENTED CONTROL ══════════════════════════════════════════════

    private readonly Dictionary<SidebarPane, ToggleButton> _segments = new();

    private void BuildSegments()
    {
        SegmentStrip.Children.Clear();
        _segments.Clear();

        (SidebarPane pane, string name, string geometry)[] items =
        {
            (SidebarPane.Tabs, "Tabs",
                "M2.4,3.6 H13.6 A1.4,1.4 0 0 1 15,5 V11 A1.4,1.4 0 0 1 13.6,12.4 H2.4 A1.4,1.4 0 0 1 1,11 V5 A1.4,1.4 0 0 1 2.4,3.6 Z M6,3.6 V12.4"),
            (SidebarPane.Bookmarks, "Bookmarks",
                "M4.2,2.4 H11.8 V13.6 L8,10.6 L4.2,13.6 Z"),
            (SidebarPane.ReadingList, "Reading List",
                "M4.6,6.6 A2.2,2.2 0 1 0 4.6,11 A2.2,2.2 0 1 0 4.6,6.6 M11.4,6.6 A2.2,2.2 0 1 0 11.4,11 A2.2,2.2 0 1 0 11.4,6.6 M6.8,8.8 H9.2 M2.4,7.4 L3.6,4.6 M13.6,7.4 L12.4,4.6"),
            (SidebarPane.History, "History",
                "M8,3.4 A4.6,4.6 0 1 1 3.4,8 M8,5.4 V8 L9.9,9.4 M3.4,8 L1.9,6.6 M3.4,8 L4.9,6.6"),
        };

        for (int i = 0; i < items.Length; i++)
        {
            (SidebarPane pane, string name, string geometry) = items[i];

            Microsoft.UI.Xaml.Shapes.Path glyph = new()
            {
                Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper
                    .ConvertValue(typeof(Geometry), geometry),
                Width = 16,
                Height = 16,
                StrokeThickness = 1.3,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            // Stroke is assigned, not bound. The glyph is the button's CONTENT, so it sits
            // outside the template's namescope and a TemplatedParent binding resolves to
            // nothing — which renders an invisible icon and no error. Path.Stroke is not an
            // inherited property either, so the presenter's Foreground cannot reach it.
            // RestampSegments re-reads the brushes whenever the theme or look changes.

            ToggleButton segment = new()
            {
                Content = glyph,
                Style = (Style)RootGrid.Resources["AvSegmentButton"],
            };

            // Icon-only segments are invisible to a screen reader without this.
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(segment, name);
            ToolTipService.SetToolTip(segment, name);

            SidebarPane captured = pane;
            segment.Click += (_, _) => ShowPane(captured);

            SegmentStrip.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(segment, i);
            SegmentStrip.Children.Add(segment);
            _segments[pane] = segment;
        }
    }

    /// <summary>
    /// Selection state plus the glyph colours, which are code-side brushes and therefore
    /// have to be re-read rather than following the theme on their own.
    /// </summary>
    internal void RestampSegments()
    {
        Brush selected = ThemeBrush("AvLabelBrush");
        Brush idle = ThemeBrush("AvIconBrush");

        foreach (KeyValuePair<SidebarPane, ToggleButton> pair in _segments)
        {
            pair.Value.IsChecked = pair.Key == _pane;

            if (pair.Value.Content is Microsoft.UI.Xaml.Shapes.Path glyph)
            {
                glyph.Stroke = pair.Key == _pane ? selected : idle;
            }
        }
    }

    private void ShowPane(SidebarPane pane)
    {
        _pane = pane;
        AppSettings.Set("sidebarPane", pane switch
        {
            SidebarPane.Bookmarks => "Bookmarks",
            SidebarPane.ReadingList => "Reading List",
            SidebarPane.History => "History",
            _ => "Tabs",
        });

        RefreshSidebar();
    }

    // ══ SHOW AND HIDE ══════════════════════════════════════════════════════

    private void ToggleSidebar(bool? show, bool animate = true)
    {
        bool target = show ?? !_sidebarVisible;
        if (target == _sidebarVisible && SidebarRoot.Visibility == (target ? Visibility.Visible : Visibility.Collapsed))
        {
            return;
        }

        _sidebarVisible = target;
        AppSettings.Set("sidebarVisible", target ? "true" : "false");

        // Aviary must never reach a state where the tabs are unreachable. Safari can leave
        // you tabless because its tab bar is always underneath; ours is not.
        if (!target && UsingSidebarTabs)
        {
            AppSettings.Set("tabLayout", "Top bar");
            PushToSettingsPage("tabLayout", "Top bar");
        }

        ToolTipService.SetToolTip(SidebarButton,
            target ? "Hide sidebar (Ctrl+Shift+L)" : "Show sidebar (Ctrl+Shift+L)");

        if (target)
        {
            SidebarRoot.Visibility = Visibility.Visible;
            SidebarGripBar.Visibility = Visibility.Visible;
            ApplyContentInsets();
            RefreshSidebar();
        }

        if (!animate)
        {
            SidebarRoot.Width = target ? _sidebarWidth : 0;
            if (!target)
            {
                SidebarRoot.Visibility = Visibility.Collapsed;
                SidebarGripBar.Visibility = Visibility.Collapsed;
                ApplyContentInsets();
            }

            ApplyTabLayout();
            return;
        }

        DoubleAnimation slide = new()
        {
            From = SidebarRoot.ActualWidth,
            To = target ? _sidebarWidth : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },

            // Without this the animation silently does nothing — no exception, no warning,
            // the sidebar simply appears instantly. Width triggers layout, and WinUI
            // refuses layout-affecting animations unless they are opted in.
            EnableDependentAnimation = true,
        };

        Storyboard.SetTarget(slide, SidebarRoot);
        Storyboard.SetTargetProperty(slide, "Width");

        Storyboard story = new();
        story.Children.Add(slide);
        story.Completed += (_, _) =>
        {
            if (!target)
            {
                SidebarRoot.Visibility = Visibility.Collapsed;
                SidebarGripBar.Visibility = Visibility.Collapsed;
                ApplyContentInsets();
            }

            ApplyTabLayout();
        };

        story.Begin();
        ApplyTabLayout();
    }

    /// <summary>
    /// Collapsing the horizontal strip is enough to reclaim its height, because its row is
    /// Auto — setting Height=0 as well would fight ApplyLookGeometry's margin assignments.
    /// </summary>
    private void ApplyTabLayout()
    {
        bool vertical = UsingSidebarTabs && _sidebarVisible;
        TabStripRow.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
        RebuildTabStrip();
    }

    private void SetSidebarWidth(double width, bool persist)
    {
        _sidebarWidth = Math.Clamp(Math.Round(width), SidebarMinWidth, SidebarMaxWidth);
        SidebarRoot.Width = _sidebarWidth;

        if (persist)
        {
            // On release only. Writing settings.json every pointer-move would fsync-storm
            // the disk for the whole duration of a drag.
            AppSettings.Set("sidebarWidth",
                _sidebarWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>
    /// The gap between the sidebar and the page, and who owns it.
    ///
    /// Under Liquid Glass the visible gap is 18px: a 6px grip plus the content sheet's own
    /// 12px left inset. Only the 6px grabbed. Found in Phase 5 M0 by dragging it in the
    /// running app — aimed at the middle of the gap you can SEE, the drag landed in the dead
    /// 12px and nothing happened, which reads as "the sidebar cannot be resized". So while
    /// the sidebar is showing, the grip takes the whole gap and the sheet gives up its left
    /// inset: it looks identical and all of it grabs.
    ///
    /// Classic has no gap at all — the sidebar's hairline meets the page — so the grip stays
    /// a thin 6px there and must not reach over the web content, or it would steal the
    /// first pixels of every page's clicks.
    /// </summary>
    private void ApplyContentInsets()
    {
        bool glass = AppSettings.Get("look").Equals("Liquid Glass", StringComparison.OrdinalIgnoreCase);
        bool beside = SidebarGripBar.Visibility == Visibility.Visible;

        SidebarGripBar.Width = glass ? 18 : 6;
        ContentHost.Margin = glass
            ? new Thickness(beside ? 0 : 12, 8, 12, 12)
            : new Thickness(0);
    }

    // ══ THE RESIZE GRIP ════════════════════════════════════════════════════

    private void Grip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _gripDragging = true;
        _gripStartX = e.GetCurrentPoint(ContentRow).Position.X;
        _gripStartWidth = SidebarRoot.ActualWidth;
        SidebarGripBar.CapturePointer(e.Pointer);
        SidebarGripBar.Background = ThemeBrush("AvSidebarGripHoverBrush");
    }

    private void Grip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_gripDragging)
        {
            return;
        }

        Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(ContentRow);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        SetSidebarWidth(_gripStartWidth + (point.Position.X - _gripStartX), persist: false);
    }

    private void Grip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_gripDragging)
        {
            return;
        }

        _gripDragging = false;
        SidebarGripBar.ReleasePointerCapture(e.Pointer);
        SidebarGripBar.Background = ThemeBrush("AvSidebarGripBrush");
        SetSidebarWidth(SidebarRoot.ActualWidth, persist: true);
    }

    // ══ THE PANES ══════════════════════════════════════════════════════════

    private void RefreshSidebar()
    {
        if (!_sidebarVisible)
        {
            return;
        }

        RestampSegments();

        SidebarBody.Children.Clear();

        switch (_pane)
        {
            case SidebarPane.Tabs: RenderTabsPane(); break;
            case SidebarPane.Bookmarks: RenderBookmarksPane(); break;
            case SidebarPane.ReadingList: RenderReadingListPane(); break;
            case SidebarPane.History: RenderHistoryPane(); break;
        }
    }

    /// <summary>Sentence case, never wide-tracked caps — uppercase tracking is a Fluent voice.</summary>
    private TextBlock SectionHeader(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Margin = new Thickness(6, 10, 6, 4),
        Foreground = ThemeBrush("AvSidebarHeaderBrush"),
    };

    private void RenderTabsPane()
    {
        SidebarBody.Children.Add(SectionHeader(
            _tabs.Count == 1 ? "1 Tab" : _tabs.Count + " Tabs"));

        Grid host = new() { Name = "SidebarTabHost" };

        // RowDefinitions BEFORE Grid.SetRow: setting a row that does not exist does not
        // throw, the child silently lands in row 0 and stacks on top of another tab.
        // Fixed height, never Star — the drag maths divides by a known slot.
        for (int i = 0; i < _tabs.Count; i++)
        {
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(SidebarRowHeight) });
        }

        for (int i = 0; i < _tabs.Count; i++)
        {
            BrowserTab tab = _tabs[i];
            ToggleButton row = BuildTabButton(tab, vertical: true);
            Grid.SetRow(row, i);
            host.Children.Add(row);
            _tabButtons[tab] = row;
            row.IsChecked = ReferenceEquals(tab, _active);
        }

        SidebarBody.Children.Add(host);
    }

    private void RenderBookmarksPane()
    {
        SidebarBody.Children.Add(SectionHeader("Favourites"));

        if (Bookmarks.Items.Count == 0)
        {
            SidebarBody.Children.Add(EmptyNote("Nothing saved yet.", "Ctrl+D adds the page you are on."));
            return;
        }

        foreach (Bookmark bookmark in Bookmarks.Items)
        {
            Bookmark captured = bookmark;
            Button row = SidebarRow(bookmark.Url, bookmark.Title, bookmark.Monogram, null, false);
            row.Click += (_, _) => { _active?.Navigate(captured.Url); UpdateChrome(); };
            row.RightTapped += (_, e) =>
            {
                e.Handled = true;
                MenuFlyout menu = new();
                Themed(menu);
                MenuFlyoutItem open = new() { Text = "Open in New Tab" };
                open.Click += (_, _) => OpenNewTab(captured.Url);
                MenuFlyoutItem remove = new() { Text = "Remove" };
                remove.Click += (_, _) => { Bookmarks.Remove(captured); RefreshSidebar(); };
                menu.Items.Add(open);
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(remove);
                menu.ShowAt(row);
            };

            SidebarBody.Children.Add(row);
        }
    }

    private bool _readingListUnreadOnly;

    private void RenderReadingListPane()
    {
        StackPanel head = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(4, 8, 4, 4) };

        foreach ((string label, bool unreadOnly) in new[] { ("All", false), ("Unread", true) })
        {
            bool captured = unreadOnly;
            ToggleButton filter = new()
            {
                Content = new TextBlock { Text = label, FontSize = 11.5 },
                IsChecked = _readingListUnreadOnly == unreadOnly,
                Style = (Style)RootGrid.Resources["AvFilterButton"],
            };
            filter.Click += (_, _) => { _readingListUnreadOnly = captured; RefreshSidebar(); };
            head.Children.Add(filter);
        }

        SidebarBody.Children.Add(head);

        List<ReadingItem> items = _readingListUnreadOnly
            ? ReadingList.Items.FindAll(i => i.Unread)
            : ReadingList.Items;

        if (items.Count == 0)
        {
            SidebarBody.Children.Add(EmptyNote(
                _readingListUnreadOnly ? "Nothing unread." : "Nothing saved to read.",
                "Ctrl+Shift+D saves the page you are on."));
            return;
        }

        foreach (ReadingItem item in items)
        {
            ReadingItem captured = item;
            Button row = SidebarRow(item.Url, item.Title, null, item.Age, item.Unread);
            row.Click += (_, _) =>
            {
                ReadingList.MarkRead(captured.Url);
                _active?.Navigate(captured.Url);
                UpdateChrome();
            };
            row.RightTapped += (_, e) =>
            {
                e.Handled = true;
                MenuFlyout menu = new();
                Themed(menu);
                MenuFlyoutItem mark = new() { Text = captured.Unread ? "Mark as Read" : "Mark as Unread" };
                mark.Click += (_, _) => { ReadingList.MarkRead(captured.Url, captured.Unread); RefreshSidebar(); };
                MenuFlyoutItem remove = new() { Text = "Remove" };
                remove.Click += (_, _) => { ReadingList.Remove(captured.Url); RefreshSidebar(); };
                menu.Items.Add(mark);
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(remove);
                menu.ShowAt(row);
            };

            SidebarBody.Children.Add(row);
        }
    }

    private void RenderHistoryPane()
    {
        List<HistoryEntry> entries = History.Recent(300);

        if (entries.Count == 0)
        {
            SidebarBody.Children.Add(SectionHeader("History"));
            SidebarBody.Children.Add(EmptyNote("Nothing here yet.", "Pages you visit are listed here."));
            return;
        }

        // Safari's History menu grouping: Today, Yesterday, then the weekday and date.
        DateTime today = DateTime.Now.Date;
        string? currentGroup = null;

        foreach (HistoryEntry entry in entries)
        {
            DateTime day = entry.LastVisitUtc.ToLocalTime().Date;
            string group = day == today ? "Today"
                         : day == today.AddDays(-1) ? "Yesterday"
                         : day.ToString("dddd, d MMMM");

            if (group != currentGroup)
            {
                currentGroup = group;
                SidebarBody.Children.Add(SectionHeader(group));
            }

            HistoryEntry captured = entry;
            Button row = SidebarRow(entry.Url, entry.Title, null,
                entry.LastVisitUtc.ToLocalTime().ToString("HH:mm"), false);
            row.Click += (_, _) => { _active?.Navigate(captured.Url); UpdateChrome(); };
            row.RightTapped += (_, e) =>
            {
                e.Handled = true;
                MenuFlyout menu = new();
                Themed(menu);
                MenuFlyoutItem open = new() { Text = "Open in New Tab" };
                open.Click += (_, _) => OpenNewTab(captured.Url);
                MenuFlyoutItem forget = new() { Text = "Remove from History" };
                forget.Click += (_, _) => { History.Remove(captured.Url); RefreshSidebar(); };
                menu.Items.Add(open);
                menu.Items.Add(new MenuFlyoutSeparator());
                menu.Items.Add(forget);
                menu.ShowAt(row);
            };

            SidebarBody.Children.Add(row);
        }
    }

    private StackPanel EmptyNote(string title, string detail)
    {
        StackPanel panel = new() { Margin = new Thickness(8, 14, 8, 8), Spacing = 3 };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("AvLabelSecondaryBrush"),
        });
        panel.Children.Add(new TextBlock
        {
            Text = detail,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = ThemeBrush("AvLabelTertiaryBrush"),
        });
        return panel;
    }

    /// <summary>One 30px row: icon, label, and an optional trailing note or unread dot.</summary>
    private Button SidebarRow(string url, string title, string? monogram, string? note, bool unread)
    {
        Grid content = new() { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        FaviconView icon = new(16, RootGrid.Resources);
        icon.SetSource(url, monogram, internalPage: false);
        Grid.SetColumn(icon, 0);
        content.Children.Add(icon);

        TextBlock label = new()
        {
            Text = string.IsNullOrWhiteSpace(title) ? BrowserTab.HostOf(url) : title,
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);

        if (unread)
        {
            Ellipse dot = new()
            {
                Width = 7,
                Height = 7,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = ThemeBrush("AvUnreadDotBrush"),
            };
            Grid.SetColumn(dot, 2);
            content.Children.Add(dot);
        }
        else if (!string.IsNullOrEmpty(note))
        {
            TextBlock trailing = new()
            {
                Text = note,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeBrush("AvLabelTertiaryBrush"),
            };
            Grid.SetColumn(trailing, 2);
            content.Children.Add(trailing);
        }

        Button row = new()
        {
            Content = content,
            Style = (Style)RootGrid.Resources["AvSidebarRowButton"],
        };

        ToolTipService.SetToolTip(row, url);
        return row;
    }

    // ══ VERTICAL TAB REORDERING ════════════════════════════════════════════

    private void VTab_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not TabChrome chrome)
        {
            return;
        }

        // Touch gives the ScrollViewer the vertical gesture through direct manipulation, so
        // the drag never starts and arrives as PointerCaptureLost instead. Mouse and pen
        // never start manipulation, so gating on device keeps the primary path working.
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            return;
        }

        _vDragTab = chrome.Tab;
        _vDragStartY = e.GetCurrentPoint(SidebarBody).Position.Y;
        _vDragging = false;
        button.CapturePointer(e.Pointer);
    }

    private void VTab_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ToggleButton button
            || button.Tag is not TabChrome chrome
            || !ReferenceEquals(_vDragTab, chrome.Tab))
        {
            return;
        }

        Microsoft.UI.Input.PointerPoint point = e.GetCurrentPoint(SidebarBody);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        double y = point.Position.Y;
        if (!_vDragging && Math.Abs(y - _vDragStartY) < 6)
        {
            return;
        }

        _vDragging = true;
        button.Opacity = 0.75;
        ReorderVertically(chrome.Tab, y);
    }

    private void VTab_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is ToggleButton button)
        {
            button.ReleasePointerCapture(e.Pointer);
            button.Opacity = 1;
        }

        _vDragTab = null;

        if (_vDragging)
        {
            _vDragging = false;
            RebuildTabStrip();
        }
    }

    private void ReorderVertically(BrowserTab tab, double y)
    {
        if (_tabs.Count < 2)
        {
            return;
        }

        // The rows are a fixed height, so the slot the pointer is over is arithmetic
        // rather than a hit test — which is exactly why they must not be Star-sized.
        int target = Math.Clamp((int)((y - SidebarRowHeight) / SidebarRowHeight), 0, _tabs.Count - 1);
        int current = _tabs.IndexOf(tab);

        if (current < 0 || target == current)
        {
            return;
        }

        _tabs.RemoveAt(current);
        _tabs.Insert(target, tab);

        for (int i = 0; i < _tabs.Count; i++)
        {
            if (_tabButtons.TryGetValue(_tabs[i], out ToggleButton? b))
            {
                Grid.SetRow(b, i);
            }
        }
    }

    // ══ LOOK GEOMETRY ══════════════════════════════════════════════════════

    private void ApplySidebarGeometry(bool glass)
    {
        SidebarRoot.CornerRadius = Res<CornerRadius>("AvSidebarRadius", new CornerRadius(glass ? 18 : 0));
        SidebarRoot.Margin = Res<Thickness>("AvSidebarMargin",
            glass ? new Thickness(12, 8, 0, 12) : new Thickness(0));

        // Under Liquid Glass the panel floats, so it draws no hard right edge; under
        // Classic the hairline is the only separation there is.
        SidebarRoot.BorderThickness = new Thickness(0, 0, glass ? 0 : 1, 0);
        SegmentBar.CornerRadius = Res<CornerRadius>("AvSegmentRadius", new CornerRadius(glass ? 9 : 7));
    }
}
