using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Aviary;

/// <summary>
/// The tab strip in motion (Alpha Build 5 M4.5 "Smooth Flight").
///
/// <b>The strip keeps its buttons.</b> It used to throw every tab button away and build them all
/// again on every open, close, pin and drag — which cost a full rebuild each time and meant
/// nothing could ever move: there was no "before" to animate from. Now each tab keeps its button
/// for as long as it lives; a change adds, removes or re-columns only what changed. Arriving tabs
/// grow in, a closed one shrinks away where it stood, and the rest glide (Smooth Flight) or
/// spring (Fluid) into place — an implicit Offset animation armed only around the change, so a
/// window resize never drags tabs along behind the mouse. A full rebuild remains for what really
/// needs one: a look swap (tab geometry is StaticResource), a new display scale, site icons
/// switched off or cleared.
///
/// <b>The active tab's chip is one shape</b>, drawn behind the strip in Composition rather than by
/// each tab's own Checked state, so it can travel. Its left and right edges are separate
/// animated values: under Smooth Flight both glide together; under Fluid the leading edge springs
/// out first and the trailing edge follows — the chip stretches toward the tab you picked, then
/// settles into it, like Safari 26's.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<BrowserTab, ToggleButton> _stripButtons = new();
    private readonly HashSet<ToggleButton> _stripPinned = new();
    private bool _stripBuiltOnce;

    private ShapeVisual? _pill;
    private CompositionRoundedRectangleGeometry? _pillShape;
    private CompositionColorBrush? _pillBrush;
    private CompositionPropertySet? _pillEdges;
    private bool _pillPlaced;
    private float _pillLeft, _pillRight;
    private bool _pillQueued;
    private bool _pillQueuedAnimate;

    /// <summary>
    /// Brings the strip in line with <c>_tabs</c>. <paramref name="fresh"/> builds every button
    /// again — for a look swap, a new scale, or site icons switched off or cleared.
    /// </summary>
    private void RebuildTabStrip(bool fresh = false)
    {
        _tabButtons.Clear();

        // M5 stage 4: the set of tabs changed - the group in front is written a second later, and
        // the tab group button's "8 Tabs" follows the count.
        QueueGroupSave();
        UpdateTabGroupButton();

        // And the Tab Groups pane's own count, if it is showing - it said "0 Tabs" over a Start Page
        // at launch, because it was drawn before the first tab existed and nothing redrew it.
        if (_sidebarVisible && _pane == SidebarPane.Groups)
        {
            RefreshSidebar();
        }

        // Tabs down the side: the strip is empty and the sidebar draws them (BOTH hosts are
        // cleared — a button can have one parent, and one left in the other host throws).
        if (UsingSidebarTabs && _sidebarVisible)
        {
            TabStrip.Children.Clear();
            TabStrip.ColumnDefinitions.Clear();
            _stripButtons.Clear();
            _stripPinned.Clear();
            HidePill();
            RefreshSidebar();
            return;
        }

        if (fresh)
        {
            TabStrip.Children.Clear();
            _stripButtons.Clear();
            _stripPinned.Clear();
        }

        bool animate = _stripBuiltOnce && !fresh;

        // A tab that closed shrinks away where it stood; the rest glide into its room.
        foreach (KeyValuePair<BrowserTab, ToggleButton> gone in _stripButtons.Where(p => !_tabs.Contains(p.Key)).ToList())
        {
            _stripButtons.Remove(gone.Key);
            _stripPinned.Remove(gone.Value);
            ToggleButton leaving = gone.Value;
            if (animate)
            {
                Motion.Leave(leaving, () => TabStrip.Children.Remove(leaving));
            }
            else
            {
                TabStrip.Children.Remove(leaving);
            }
        }

        if (animate)
        {
            Motion.GlideNextLayout(_stripButtons.Values.Cast<UIElement>().ToList());
        }

        TabStrip.ColumnDefinitions.Clear();
        for (int i = 0; i < _tabs.Count; i++)
        {
            BrowserTab tab = _tabs[i];
            bool pinned = _pinned.Contains(tab);

            // A pinned tab is a fixed narrow cell; the rest still divide what is left,
            // which is what keeps the Safari silhouette rather than Chrome's fixed widths.
            TabStrip.ColumnDefinitions.Add(pinned
                ? new ColumnDefinition { Width = new GridLength(PinnedTabWidth) }
                : new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = TabMinWidth });

            if (!_stripButtons.TryGetValue(tab, out ToggleButton? button) || _stripPinned.Contains(button) != pinned)
            {
                if (button is not null)
                {
                    TabStrip.Children.Remove(button);
                    _stripPinned.Remove(button);
                }

                button = pinned ? BuildPinnedTabButton(tab) : BuildTabButton(tab);
                UseTabPill(button);
                _stripButtons[tab] = button;
                if (pinned)
                {
                    _stripPinned.Add(button);
                }

                TabStrip.Children.Add(button);

                if (animate)
                {
                    ToggleButton arriving = button;
                    RoutedEventHandler? grow = null;
                    grow = (_, _) =>
                    {
                        arriving.Loaded -= grow;
                        Motion.Enter(arriving);
                    };
                    arriving.Loaded += grow;
                }
            }

            Grid.SetColumn(button, i);
            _tabButtons[tab] = button;
            button.IsChecked = ReferenceEquals(tab, _active);
        }

        _stripBuiltOnce = true;
        QueuePill(animate);
    }

    /// <summary>
    /// A tab as Narrator and UI Automation see it (bug 69, found 19 Sep testing M4.5): until now a
    /// tab button had NO accessible name — a screen reader heard "toggle button" — and toggling it
    /// through UI Automation (Narrator's default action) only flipped its checked look without
    /// switching to the tab, because tabs answered Click alone. The name is the tab's title
    /// (updated with it), Checked switches to the tab, and the active tab cannot be un-checked.
    /// </summary>
    private void WireTabAccess(ToggleButton button, BrowserTab tab)
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, ShownTitle(tab));
        button.Checked += (_, _) =>
        {
            if (!ReferenceEquals(tab, _active) && _tabs.Contains(tab))
            {
                SelectTab(tab);
            }
        };
        button.Unchecked += (_, _) =>
        {
            if (ReferenceEquals(tab, _active))
            {
                button.IsChecked = true;
            }
        };
    }

    /// <summary>
    /// A strip button draws no chip of its own when checked — the pill behind the strip is the
    /// chip. Lightweight styling: the template's Checked setter looks the brush up from the
    /// button outward, so a transparent one here wins for this button only (sidebar rows keep
    /// theirs).
    /// </summary>
    private static void UseTabPill(ToggleButton button) =>
        button.Resources["AvTabActiveBrush"] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    /// <summary>After the next layout pass — positions are only known once the strip is arranged.</summary>
    private void QueuePill(bool animate)
    {
        _pillQueuedAnimate |= animate;
        if (_pillQueued)
        {
            return;
        }

        _pillQueued = true;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            _pillQueued = false;
            bool a = _pillQueuedAnimate;
            _pillQueuedAnimate = false;
            PlacePill(a);
        });
    }

    private bool EnsurePill()
    {
        if (_pill is not null)
        {
            return true;
        }

        try
        {
            Visual host = ElementCompositionPreview.GetElementVisual(TabPillHost);
            Compositor c = host.Compositor;

            _pillEdges = c.CreatePropertySet();
            _pillEdges.InsertScalar("L", 0f);
            _pillEdges.InsertScalar("R", 0f);
            _pillEdges.InsertScalar("T", 0f);
            _pillEdges.InsertScalar("H", 0f);

            _pillShape = c.CreateRoundedRectangleGeometry();
            ExpressionAnimation offset = c.CreateExpressionAnimation("Vector2(e.L, e.T)");
            offset.SetReferenceParameter("e", _pillEdges);
            _pillShape.StartAnimation("Offset", offset);
            ExpressionAnimation size = c.CreateExpressionAnimation("Vector2(Max(e.R - e.L, 0.0), e.H)");
            size.SetReferenceParameter("e", _pillEdges);
            _pillShape.StartAnimation("Size", size);

            _pillBrush = c.CreateColorBrush();
            CompositionSpriteShape sprite = c.CreateSpriteShape(_pillShape);
            sprite.FillBrush = _pillBrush;

            _pill = c.CreateShapeVisual();
            _pill.Shapes.Add(sprite);
            ExpressionAnimation fill = c.CreateExpressionAnimation("host.Size");
            fill.SetReferenceParameter("host", host);
            _pill.StartAnimation("Size", fill);
            _pill.IsVisible = false;

            ElementCompositionPreview.SetElementChildVisual(TabPillHost, _pill);
            TabStrip.SizeChanged += (_, _) => QueuePill(false);
            RefreshPillBrush();
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab pill: create");
            _pill = null;
            return false;
        }
    }

    /// <summary>The chip's colour and corners follow the look and theme on screen.</summary>
    private void RefreshPillBrush()
    {
        if (_pillBrush is null || _pillShape is null)
        {
            return;
        }

        try
        {
            // Under M6's toolbar tint, the chip is the tinted strip's theme's (a pale chip with white words
            // was unreadable - MainWindow.TabsExperiments.cs).
            _pillBrush.Color = ColorOf(ThemeBrushIn("AvTabActiveBrush", TintTheme ?? EffectiveThemeKey()));
            float radius = (float)Res<CornerRadius>("AvTabCornerRadius", new CornerRadius(6)).TopLeft;
            _pillShape.CornerRadius = new Vector2(radius, radius);
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab pill: brush");
        }
    }

    /// <summary>
    /// Tells Aviary's own open pages (Settings, the Start Page) which motion style is in force:
    /// their CSS keys off &lt;html data-motion="flight|fluid|off"&gt;. Only internal pages are ever
    /// told — a host push reaching a website is bug 62.
    /// </summary>
    /// <summary>Always / On hover / Never - what the tab strip does with its close crosses (M4.6).</summary>
    private static string TabCloseShows => AppSettings.Get("tabCloseButton") is { Length: > 0 } how ? how : "On hover";

    /// <summary>
    /// Puts every tab's icon and cross where the current choice says they belong, without waiting
    /// for the pointer. Called when the setting changes and after a rebuild.
    /// </summary>
    private void ApplyTabCloseButtons()
    {
        bool always = TabCloseShows == "Always";
        foreach (BrowserTab tab in _tabs)
        {
            if (!_tabButtons.TryGetValue(tab, out ToggleButton? button) || button.Tag is not TabChrome chrome)
            {
                continue;
            }

            FadeTo(chrome.Icon, always ? 0 : 1, 120);
            FadeTo(chrome.ClosePad, always ? 1 : 0, 120);
        }
    }

    private void PushMotionToPages()
    {
        string script = "document.documentElement.dataset.motion=" + System.Text.Json.JsonSerializer.Serialize(Motion.PageToken)
                      + ";document.documentElement.dataset.speed=" + System.Text.Json.JsonSerializer.Serialize(Motion.PageSpeed);
        foreach (BrowserTab tab in _tabs)
        {
            if (!tab.IsInternalPage || tab.View.CoreWebView2 is not Microsoft.Web.WebView2.Core.CoreWebView2 core)
            {
                continue;
            }

            try
            {
                _ = core.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Diagnostics.Swallowed(ex, "motion: tell page");
            }
        }
    }

    private void HidePill()
    {
        if (_pill is not null)
        {
            _pill.IsVisible = false;
        }

        _pillPlaced = false;
    }

    private void PlacePill(bool animate)
    {
        if (_active is null || !_tabButtons.TryGetValue(_active, out ToggleButton? button)
            || button.Parent != TabStrip || TabStripRow.Visibility != Visibility.Visible
            || button.ActualWidth <= 0 || !EnsurePill())
        {
            HidePill();
            return;
        }

        try
        {
            Point at = button.TransformToVisual(TabPillHost).TransformPoint(new Point(0, 0));
            float left = (float)at.X;
            float right = (float)(at.X + button.ActualWidth);
            float top = (float)at.Y;
            float height = (float)button.ActualHeight;

            _pill!.IsVisible = true;
            _pillEdges!.InsertScalar("T", top);
            _pillEdges.InsertScalar("H", height);

            Motion.Style style = Motion.Current;
            bool moving = animate && _pillPlaced && style != Motion.Style.Off
                          && (Math.Abs(left - _pillLeft) > 0.5f || Math.Abs(right - _pillRight) > 0.5f);

            if (!moving)
            {
                _pillEdges.StopAnimation("L");
                _pillEdges.StopAnimation("R");
                _pillEdges.InsertScalar("L", left);
                _pillEdges.InsertScalar("R", right);
            }
            else
            {
                Compositor c = _pillEdges.Compositor;
                if (style == Motion.Style.Fluid)
                {
                    // Gel: the edge on the side it is heading springs out first; the other
                    // follows a beat later — so it stretches, then settles into the tab.
                    bool rightward = left > _pillLeft;
                    SpringScalarNaturalMotionAnimation lead = Motion.Spring1(c, 0.74f, 34);
                    SpringScalarNaturalMotionAnimation trail = Motion.Spring1(c, 0.82f, 58);
                    lead.FinalValue = rightward ? right : left;
                    trail.FinalValue = rightward ? left : right;
                    _pillEdges.StartAnimation(rightward ? "R" : "L", lead);
                    _pillEdges.StartAnimation(rightward ? "L" : "R", trail);
                }
                else
                {
                    foreach ((string edge, float to) in new[] { ("L", left), ("R", right) })
                    {
                        ScalarKeyFrameAnimation glide = c.CreateScalarKeyFrameAnimation();
                        glide.InsertKeyFrame(1f, to, Motion.Ease(c));
                        glide.Duration = Motion.Standard + TimeSpan.FromMilliseconds(20);
                        _pillEdges.StartAnimation(edge, glide);
                    }
                }
            }

            _pillLeft = left;
            _pillRight = right;
            _pillPlaced = true;
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "tab pill: place");
        }
    }
}
