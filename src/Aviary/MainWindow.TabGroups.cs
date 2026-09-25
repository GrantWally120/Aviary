using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Aviary;

/// <summary>
/// Tab groups in the window (Alpha Build 5 M5 stage 4) — Safari's: a window shows its own tabs or
/// one named group, and switching swaps the whole set.
///
/// <para><b>Switching</b> puts the current set away — a group's pages into the group
/// (<see cref="TabGroups"/>), the window's own tabs into memory — and brings the other set back
/// DORMANT: each tab has its address, title and icon, and only the one in front starts an engine;
/// the others start when first chosen (<see cref="RouseAsync"/>). Pinned tabs stay where they are
/// whatever is showing, and a tab still downloading is parked, not closed (stage 1's rule), so
/// putting a set away never kills a download.</para>
///
/// <para><b>The group in front is kept up to date</b> as its tabs open, close, move and load — a
/// second after the last change — so a crash loses nothing but that second. One window shows a
/// group at a time: choosing one open in another window brings that window forward.</para>
///
/// <para>Where it is: a Tab Groups pane in the sidebar (both tab layouts), a button with the group's
/// name beside the sidebar button once any group exists, "Move to Tab Group" on a tab's menu and
/// "New Tab Group" in the app menu. None of it in a private window.</para>
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The group this window shows, or null for the window's own tabs.</summary>
    private TabGroup? _group;

    /// <summary>The window's own tabs while a group is in front, and which one was in front.</summary>
    private List<GroupTab> _ownTabs = new();
    private int _ownActive;

    private readonly HashSet<BrowserTab> _rousing = new();
    private bool _groupSaveQueued;

    private void WireTabGroups()
    {
        if (_private)
        {
            return;
        }

        TabGroups.Changed += OnTabGroupsChanged;
        UpdateTabGroupButton();
    }

    private void UnwireTabGroups()
    {
        TabGroups.Changed -= OnTabGroupsChanged;

        // Closing the window puts its group away as it stands.
        if (_group is { } group)
        {
            TabGroups.Update(group, Snapshot(out int active), active);
            group.OpenIn = null;
        }
    }

    private void OnTabGroupsChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTabGroupButton();
            if (_sidebarVisible && _pane == SidebarPane.Groups)
            {
                RefreshSidebar();
            }
        });
    }

    // ── the set of tabs, as pages ─────────────────────────────────────────────

    /// <summary>
    /// The unpinned tabs, in order, as a group keeps them - and which is in front. A Start Page is kept
    /// AS a Start Page (an empty address), because Safari counts it as a tab and so should the count
    /// on the button; Aviary's other pages (Settings and the like) are not kept.
    /// </summary>
    private List<GroupTab> Snapshot(out int active)
    {
        List<GroupTab> pages = new();
        active = 0;
        foreach (BrowserTab tab in _tabs)
        {
            if (_pinned.Contains(tab))
            {
                continue;
            }

            bool startPage = tab.IsInternalPage ? tab.Title == "Start Page" : tab.Dormant && tab.Url.Length == 0;
            if (startPage)
            {
                if (ReferenceEquals(tab, _active))
                {
                    active = pages.Count;
                }

                pages.Add(new GroupTab { Url = string.Empty, Title = "Start Page" });
                continue;
            }

            if (tab.IsInternalPage || BrowserTab.HostOf(tab.Url).Length == 0)
            {
                continue;
            }

            if (ReferenceEquals(tab, _active))
            {
                active = pages.Count;
            }

            pages.Add(new GroupTab { Url = tab.Url, Title = tab.Title });
        }

        return pages;
    }

    /// <summary>
    /// The group in front changed (a tab opened, closed, moved or loaded): written a second after the
    /// last change, not on every one.
    /// </summary>
    private void QueueGroupSave()
    {
        if (_group is null || _groupSaveQueued || _private)
        {
            return;
        }

        _groupSaveQueued = true;
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            _groupSaveQueued = false;
            if (_group is { } group)
            {
                TabGroups.Update(group, Snapshot(out int active), active);
            }
        };
        timer.Start();
    }

    // ── dormant tabs ──────────────────────────────────────────────────────────

    /// <summary>A tab with an address, a title and an icon, and no engine yet (<see cref="BrowserTab.Dormant"/>).</summary>
    private BrowserTab CreateDormantTab(GroupTab page) => CreateDormantTab(page.Url, page.Title, "tab group");

    /// <summary>M6: also a pinned tab restored at launch with "Open pinned tabs only when clicked" on.</summary>
    private BrowserTab CreateDormantTab(string url, string title, string reason)
    {
        BrowserTab tab = new(_private);
        WireTab(tab);
        tab.MakeDormant(url, title);
        tab.SetThemePageColour(ThemePageColour());
        _tabs.Add(tab);
        DiagUi("tab-open", new { tab = tab.Id, reason, index = _tabs.Count - 1, tabs = _tabs.Count, dormant = true });
        return tab;
    }

    /// <summary>A dormant tab was chosen: its engine starts, then it goes where it was.</summary>
    private async Task RouseAsync(BrowserTab tab)
    {
        if (!tab.Dormant || !_rousing.Add(tab))
        {
            return;
        }

        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            // In the tree BEFORE its engine starts: a WebView created outside it and added later
            // can come up drawing but not taking the mouse (CreateTabAsync's note).
            if (!ContentHost.Children.Contains(tab.View))
            {
                ContentHost.Children.Add(tab.View);
            }

            await tab.InitialiseAsync();
            TrackFocusHandoff(tab);
        }
        catch (Exception ex)
        {
            Diagnostics.Anomaly("webview-start", ex.GetType().Name + " — " + ex.Message, new { reason = "tab group", hresult = "0x" + ex.HResult.ToString("X8") });
            ShowNotice(tab, new TabNotice { Kind = "engine-start", Title = "Aviary can't start its browser engine", Detail = ex.Message });
            return;
        }
        finally
        {
            _rousing.Remove(tab);
        }

        string url = tab.Url;
        tab.Roused();
        if (_tabs.Contains(tab) && url.Length == 0 && tab.View.CoreWebView2 is Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            // A Start Page kept in the set comes back as a Start Page.
            core.WebMessageReceived += StartPage_WebMessageReceived;
            tab.NavigateToHtml(BuildStartPageHtml(), "Start Page");
        }
        else if (_tabs.Contains(tab))
        {
            tab.Navigate(url);
            DiagPerf("tab-rouse", new { tab = tab.Id, ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1) });
        }

        UpdateChrome();
    }

    // ── switching ─────────────────────────────────────────────────────────────

    /// <summary>Show a group - or, with null, the window's own tabs.</summary>
    private async Task SwitchTabGroupAsync(TabGroup? target)
    {
        if (_private || ReferenceEquals(target, _group))
        {
            return;
        }

        // One window shows a group at a time: go to the window that has it.
        if (target?.OpenIn is MainWindow other && !ReferenceEquals(other, this) && OpenWindows.Contains(other))
        {
            other.BringToFront();
            return;
        }

        // 1. The set in front, as pages, put away where it belongs.
        List<GroupTab> leaving = Snapshot(out int leavingActive);
        if (_group is { } current)
        {
            TabGroups.Update(current, leaving, leavingActive);
            current.OpenIn = null;
        }
        else
        {
            _ownTabs = leaving;
            _ownActive = leavingActive;
        }

        List<BrowserTab> old = _tabs.Where(t => !_pinned.Contains(t)).ToList();

        // 2. The other set comes in dormant, BEFORE the old one goes, so the window is never empty.
        List<GroupTab> incoming = target is null ? _ownTabs : target.Tabs.ToList();
        int want = target is null ? _ownActive : target.Active;
        _group = target;
        if (target is not null)
        {
            target.OpenIn = this;
        }
        else
        {
            _ownTabs = new List<GroupTab>();
        }

        BrowserTab? front = null;
        for (int i = 0; i < incoming.Count; i++)
        {
            BrowserTab tab = CreateDormantTab(incoming[i]);
            if (i == Math.Clamp(want, 0, incoming.Count - 1))
            {
                front = tab;
            }
        }

        if (front is not null)
        {
            SelectTab(front);
        }
        else
        {
            // An empty group - a new one, or one whose pages were all moved out - shows a Start Page.
            await OpenNewTabAsync(null);
        }

        // 3. The old set goes (parked where a download is still running).
        foreach (BrowserTab tab in old)
        {
            _tabs.Remove(tab);
            _tabButtons.Remove(tab);
            DisposeOrPark(tab);
        }

        RebuildTabStrip();
        UpdateChrome();
        UpdateTabGroupButton();
        RefreshSidebar();
        DiagFeature("tab-group-switch", new { to = target is null ? "window" : "group", name = target?.Name, tabs = incoming.Count });
    }

    // ── making, naming, removing ──────────────────────────────────────────────

    private async Task NewEmptyTabGroupAsync()
    {
        string? name = await AskTabGroupNameAsync("New Tab Group", TabGroups.NextName(), "Create");
        if (name is null)
        {
            return;
        }

        TabGroup group = TabGroups.Create(name, new List<GroupTab>(), 0);
        DiagFeature("tab-group-new", new { name = group.Name, tabs = 0 });
        await SwitchTabGroupAsync(group);
    }

    /// <summary>
    /// "New Tab Group with These Tabs": the tabs in front become the group - they stay open, nothing
    /// reloads; the set they came from (the window's own, or another group) no longer has them.
    /// </summary>
    private async Task NewTabGroupFromTabsAsync()
    {
        List<GroupTab> pages = Snapshot(out int active);
        string? name = await AskTabGroupNameAsync("New Tab Group", TabGroups.NextName(), "Create");
        if (name is null)
        {
            return;
        }

        if (_group is { } from)
        {
            TabGroups.Update(from, new List<GroupTab>(), 0);
            from.OpenIn = null;
        }
        else
        {
            _ownTabs = new List<GroupTab>();
            _ownActive = 0;
        }

        TabGroup group = TabGroups.Create(name, pages, active);
        group.OpenIn = this;
        _group = group;
        DiagFeature("tab-group-new", new { name = group.Name, tabs = pages.Count, fromTabs = true });
        UpdateTabGroupButton();
        RefreshSidebar();
    }

    private async Task RenameTabGroupAsync(TabGroup group)
    {
        string? name = await AskTabGroupNameAsync("Rename Tab Group", group.Name, "Rename");
        if (name is not null && name != group.Name)
        {
            TabGroups.Rename(group, name);
            DiagFeature("tab-group-rename", new { name = group.Name });
        }
    }

    private async Task DeleteTabGroupAsync(TabGroup group)
    {
        int count = ReferenceEquals(group, _group) ? Snapshot(out _).Count : group.Tabs.Count;
        bool delete = await ConfirmAsync(
            "Delete “" + group.Name + "”?",
            count == 0 ? "The group is empty." : "Its " + (count == 1 ? "tab is" : count + " tabs are") + " closed with it. Your history and favourites are not touched.",
            "Delete");
        if (!delete)
        {
            return;
        }

        // Deleting the group in front first shows the window's own tabs, so nothing is left without a set.
        if (ReferenceEquals(group, _group))
        {
            await SwitchTabGroupAsync(null);
        }

        if (group.OpenIn is MainWindow other && !ReferenceEquals(other, this) && OpenWindows.Contains(other))
        {
            await other.SwitchTabGroupAsync(null);
        }

        TabGroups.Delete(group);
        DiagFeature("tab-group-delete", new { name = group.Name, tabs = count });
    }

    /// <summary>A tab's menu → Move to Tab Group: it joins the group's pages and leaves this set.</summary>
    private void MoveTabToGroup(BrowserTab tab, TabGroup group)
    {
        if (BrowserTab.HostOf(tab.Url).Length == 0 || tab.IsInternalPage)
        {
            return;
        }

        TabGroups.Append(group, new GroupTab { Url = tab.Url, Title = tab.Title });
        DiagFeature("tab-group-move", new { to = group.Name });
        CloseTab(tab);
    }

    private async Task MoveTabToNewGroupAsync(BrowserTab tab)
    {
        string? name = await AskTabGroupNameAsync("New Tab Group", TabGroups.NextName(), "Create");
        if (name is null)
        {
            return;
        }

        TabGroup group = TabGroups.Create(name, new List<GroupTab>(), 0);
        DiagFeature("tab-group-new", new { name = group.Name, tabs = 1, fromTab = true });
        MoveTabToGroup(tab, group);
    }

    /// <summary>A name for a group, asked in a small dialog. Null means Cancel.</summary>
    private async Task<string?> AskTabGroupNameAsync(string title, string suggested, string verb)
    {
        if (_dialogOpen)
        {
            return null;
        }

        _dialogOpen = true;
        try
        {
            TextBox box = new() { Text = suggested, MinWidth = 300 };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(box, "Tab group name");
            box.Loaded += (_, _) => { box.Focus(FocusState.Programmatic); box.SelectAll(); };

            ContentDialog dialog = new()
            {
                XamlRoot = RootGrid.XamlRoot,
                RequestedTheme = RootGrid.ActualTheme,
                Title = title,
                Content = box,
                PrimaryButtonText = verb,
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
            };

            return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab groups: ask for a name");
            return null;
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    // ── the menu: toolbar button, app menu, tab menu ─────────────────────────

    /// <summary>The button beside the sidebar button: the group in front by name, once any group exists.</summary>
    private void UpdateTabGroupButton()
    {
        bool show = !_private && TabGroups.All.Count > 0;
        TabGroupButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        TabGroupLabel.Text = _group?.Name ?? TabsLabel();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TabGroupButton, "Tab Groups: " + TabGroupLabel.Text);
        ToolTipService.SetToolTip(TabGroupButton, "Tab Groups");
    }

    /// <summary>
    /// "8 Tabs" - the window's own set, named the way Safari names it. While it is in front every
    /// unpinned tab counts, a Start Page too; while a group is in front, what was put away (pages).
    /// </summary>
    private string TabsLabel()
    {
        int n = _group is null ? Snapshot(out _).Count : _ownTabs.Count;
        return n == 1 ? "1 Tab" : n + " Tabs";
    }

    private void TabGroupButton_Click(object sender, RoutedEventArgs e)
    {
        MenuFlyout menu = Themed(new MenuFlyout(), "tab groups");
        FillTabGroupMenu(menu.Items);
        menu.ShowAt(TabGroupButton);
    }

    private void FillTabGroupMenu(IList<MenuFlyoutItemBase> items)
    {
        ToggleMenuFlyoutItem own = new() { Text = TabsLabel(), IsChecked = _group is null };
        own.Click += (_, _) => _ = SwitchTabGroupAsync(null);
        items.Add(own);

        foreach (TabGroup group in TabGroups.All)
        {
            TabGroup captured = group;
            ToggleMenuFlyoutItem item = new() { Text = group.Name, IsChecked = ReferenceEquals(group, _group) };
            item.Click += (_, _) => _ = SwitchTabGroupAsync(captured);
            items.Add(item);
        }

        items.Add(new MenuFlyoutSeparator());
        MenuFlyoutItem empty = new() { Text = "New Empty Tab Group" };
        empty.Click += (_, _) => _ = NewEmptyTabGroupAsync();
        items.Add(empty);

        int here = Snapshot(out _).Count;
        MenuFlyoutItem withTabs = new() { Text = here == 1 ? "New Tab Group with This Tab" : "New Tab Group with " + here + " Tabs", IsEnabled = here > 0 };
        withTabs.Click += (_, _) => _ = NewTabGroupFromTabsAsync();
        items.Add(withTabs);

        // The group in front can be renamed or deleted from here too - the sidebar row's menu is
        // not the only way in.
        if (_group is { } current)
        {
            items.Add(new MenuFlyoutSeparator());
            MenuFlyoutItem rename = new() { Text = "Rename “" + current.Name + "”…" };
            rename.Click += (_, _) => _ = RenameTabGroupAsync(current);
            MenuFlyoutItem delete = new() { Text = "Delete “" + current.Name + "”…" };
            delete.Click += (_, _) => _ = DeleteTabGroupAsync(current);
            items.Add(rename);
            items.Add(delete);
        }
    }

    /// <summary>A tab's menu: Move to Tab Group ▸ every other group, and a new one.</summary>
    private MenuFlyoutSubItem? MoveToGroupItem(BrowserTab tab)
    {
        if (_private || tab.IsInternalPage || BrowserTab.HostOf(tab.Url).Length == 0 || _pinned.Contains(tab))
        {
            return null;
        }

        MenuFlyoutSubItem move = new() { Text = "Move to Tab Group" };
        foreach (TabGroup group in TabGroups.All.Where(g => !ReferenceEquals(g, _group)))
        {
            TabGroup captured = group;
            MenuFlyoutItem item = new() { Text = group.Name };
            item.Click += (_, _) => MoveTabToGroup(tab, captured);
            move.Items.Add(item);
        }

        if (move.Items.Count > 0)
        {
            move.Items.Add(new MenuFlyoutSeparator());
        }

        MenuFlyoutItem fresh = new() { Text = "New Tab Group…" };
        fresh.Click += (_, _) => _ = MoveTabToNewGroupAsync(tab);
        move.Items.Add(fresh);
        return move;
    }

    // ── the sidebar pane ─────────────────────────────────────────────────────

    private void RenderGroupsPane()
    {
        SidebarBody.Children.Add(SectionHeader("Tab Groups"));
        SidebarBody.Children.Add(GroupRow(TabsLabel(), null, _group is null, "this window"));

        foreach (TabGroup group in TabGroups.All)
        {
            int count = ReferenceEquals(group, _group) ? Snapshot(out _).Count : group.Tabs.Count;
            SidebarBody.Children.Add(GroupRow(group.Name, group, ReferenceEquals(group, _group), count == 1 ? "1 tab" : count + " tabs"));
        }

        StackPanel actions = new() { Margin = new Thickness(2, 10, 2, 0), Spacing = 2 };
        Button empty = new() { Content = "New Tab Group", Style = (Style)RootGrid.Resources["AvSidebarRowButton"] };
        empty.Click += (_, _) => _ = NewEmptyTabGroupAsync();
        int here = Snapshot(out _).Count;
        Button withTabs = new()
        {
            Content = here == 1 ? "New Tab Group with This Tab" : "New Tab Group with These " + here + " Tabs",
            Style = (Style)RootGrid.Resources["AvSidebarRowButton"],
            IsEnabled = here > 0,
        };
        withTabs.Click += (_, _) => _ = NewTabGroupFromTabsAsync();
        actions.Children.Add(empty);
        actions.Children.Add(withTabs);
        SidebarBody.Children.Add(actions);
    }

    private Button GroupRow(string name, TabGroup? group, bool current, string note)
    {
        Grid content = new() { ColumnSpacing = 8 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Microsoft.UI.Xaml.Shapes.Path glyph = new()
        {
            Width = 16,
            Height = 16,
            StrokeThickness = 1.3,
            StrokeLineJoin = PenLineJoin.Round,
            Stroke = current ? ThemeBrush("AvAccentBrush") : ThemeBrush("AvIconBrush"),
            Data = (Geometry)Microsoft.UI.Xaml.Markup.XamlBindingHelper.ConvertValue(typeof(Geometry),
                group is null
                    ? "M2.4,3.6 H13.6 A1.4,1.4 0 0 1 15,5 V11 A1.4,1.4 0 0 1 13.6,12.4 H2.4 A1.4,1.4 0 0 1 1,11 V5 A1.4,1.4 0 0 1 2.4,3.6 Z"
                    : "M4.4,5.6 H13.2 A1.2,1.2 0 0 1 14.4,6.8 V12.4 A1.2,1.2 0 0 1 13.2,13.6 H4.4 A1.2,1.2 0 0 1 3.2,12.4 V6.8 A1.2,1.2 0 0 1 4.4,5.6 Z M5.6,3.4 H11.6 A1.2,1.2 0 0 1 12.8,4.6"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(glyph);

        TextBlock label = new()
        {
            Text = name,
            FontSize = 12.5,
            FontWeight = current ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(label, 1);
        content.Children.Add(label);

        TextBlock trailing = new() { Text = note, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeBrush("AvLabelTertiaryBrush") };
        Grid.SetColumn(trailing, 2);
        content.Children.Add(trailing);

        Button row = new() { Content = content, Style = (Style)RootGrid.Resources["AvSidebarRowButton"] };
        // "This window's own tabs, 3 Tabs, showing" / "Tab group Travel, 3 tabs" - what a screen reader says.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(row,
            (group is null ? "This window’s own tabs, " + name : "Tab group " + name + ", " + note) + (current ? ", showing" : string.Empty));
        row.Click += (_, _) => _ = SwitchTabGroupAsync(group);

        if (group is not null)
        {
            // A ContextFlyout, not a RightTapped handler: it opens from the keyboard's menu key and
            // Shift+F10 as well as a right-click, so renaming and deleting are not mouse-only.
            MenuFlyout menu = Themed(new MenuFlyout(), "sidebar tab group");
            MenuFlyoutItem rename = new() { Text = "Rename…" };
            rename.Click += (_, _) => _ = RenameTabGroupAsync(group);
            MenuFlyoutItem delete = new() { Text = "Delete…" };
            delete.Click += (_, _) => _ = DeleteTabGroupAsync(group);
            menu.Items.Add(rename);
            menu.Items.Add(new MenuFlyoutSeparator());
            menu.Items.Add(delete);
            row.ContextFlyout = menu;
        }

        return row;
    }
}
