using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aviary;

/// <summary>
/// The chrome's smaller motions (Alpha Build 5 M4.5 "Smooth Flight"): the address field's loading
/// line, the chrome dimming while Aviary is not the active window, and the hooks the rest of the
/// window calls. The timing and the two styles are Motion.cs's; this file only decides WHEN.
/// </summary>
public sealed partial class MainWindow
{
    private BrowserTab? _loadLineTab;
    private bool _loadLineRunning;
    private bool _reloadShowsStop;
    private BrowserTab? _shieldPopTab;
    private int _shieldPopCount;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _dimTimer;

    // ── The loading line ────────────────────────────────────────────────────────

    /// <summary>
    /// From UpdateChrome, which runs on every navigation event of the active tab and on every
    /// switch. WebView2 gives no percentage, so the line eases out toward three quarters over a
    /// few seconds (Safari's does the same) and completes when the page says it has finished.
    /// </summary>
    private void UpdateLoadLine(BrowserTab? tab)
    {
        bool loading = tab?.IsLoading ?? false;
        bool switched = !ReferenceEquals(tab, _loadLineTab);
        _loadLineTab = tab;

        if (!Motion.On)
        {
            LoadLine.Opacity = 0;
            _loadLineRunning = false;
            return;
        }

        try
        {
            Visual v = Motion.VisualOf(LoadLine);
            Compositor c = v.Compositor;
            v.CenterPoint = new Vector3(0, 1, 0);

            if (loading && (!_loadLineRunning || switched))
            {
                _loadLineRunning = true;
                Motion.Reveal(LoadLine, true);
                Vector3KeyFrameAnimation run = c.CreateVector3KeyFrameAnimation();
                run.InsertKeyFrame(0f, new Vector3(switched ? 0.45f : 0.03f, 1f, 1f));
                run.InsertKeyFrame(1f, new Vector3(0.78f, 1f, 1f), c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.6f), new Vector2(0.3f, 1f)));
                run.Duration = TimeSpan.FromMilliseconds(switched ? 1500 : 2800);
                v.StartAnimation("Scale", run);
            }
            else if (!loading && _loadLineRunning)
            {
                _loadLineRunning = false;
                if (switched)
                {
                    v.StopAnimation("Scale");
                    LoadLine.Opacity = 0;
                    return;
                }

                Vector3KeyFrameAnimation finish = c.CreateVector3KeyFrameAnimation();
                finish.InsertKeyFrame(1f, Vector3.One, Motion.Ease(c));
                finish.Duration = Motion.Quick + TimeSpan.FromMilliseconds(40);
                v.StartAnimation("Scale", finish);
                Motion.Reveal(LoadLine, false, TimeSpan.FromMilliseconds(360));
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "motion: loading line");
        }
    }

    // ── Reload ↔ stop, the shield's count ───────────────────────────────────────

    /// <summary>The reload arrow becomes the stop cross (and back) with a small bloom, not a jump.</summary>
    private void NoteReloadGlyph(bool stop)
    {
        if (stop != _reloadShowsStop)
        {
            _reloadShowsStop = stop;
            Motion.Enter(ReloadGlyph);
        }
    }

    /// <summary>The count on the shield pops each time it goes up on the page you are looking at.</summary>
    private void NoteShieldCount(BrowserTab? tab, int count)
    {
        if (ReferenceEquals(tab, _shieldPopTab) && count > _shieldPopCount && ShieldCount.Visibility == Visibility.Visible)
        {
            Motion.Pop(ShieldCount, 1.22f);
        }

        _shieldPopTab = tab;
        _shieldPopCount = count;
    }

    // ── The chrome dims while Aviary is not the active window ──────────────────

    /// <summary>
    /// Safari greys its toolbar's contents while another window is in front; so does Aviary. Only
    /// the contents dim — the bars keep their colour — and only after a moment, so a flyout or a
    /// menu that briefly takes activation does not make the chrome flicker.
    /// </summary>
    private void InitialiseActivationDim()
    {
        Activated += (_, e) =>
        {
            bool inactive = e.WindowActivationState == WindowActivationState.Deactivated;
            _dimTimer?.Stop();

            if (!inactive)
            {
                DimChrome(false);
                return;
            }

            _dimTimer ??= DispatcherQueue.CreateTimer();
            _dimTimer.Interval = TimeSpan.FromMilliseconds(180);
            _dimTimer.IsRepeating = false;
            _dimTimer.Tick -= DimTick;
            _dimTimer.Tick += DimTick;
            _dimTimer.Start();
        };
    }

    private void DimTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => DimChrome(true);

    private void DimChrome(bool dim)
    {
        float to = dim ? 0.55f : 1f;
        TimeSpan d = dim ? Motion.Slow : Motion.Quick;
        foreach (UIElement element in DimmableChrome())
        {
            Motion.FadeTo(element, to, d);
        }
    }

    private IEnumerable<UIElement> DimmableChrome()
    {
        foreach (UIElement child in ToolbarRow.Children)
        {
            if (child is Panel && !ReferenceEquals(child, AddressField))
            {
                yield return child;
            }
        }

        yield return AddressField;
        yield return TabStrip;
        yield return NewTabButton;
        yield return SegmentStrip;
        yield return SidebarBody;
    }
}
