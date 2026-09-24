using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace Aviary;

/// <summary>
/// The toolbar's download button (Alpha Build 5 M5) — Safari's <c>arrow.down.circle</c>, which is
/// ONE glyph: the circle round the arrow is also the progress track. While anything downloads, a
/// second arc in the accent colour sweeps round it from twelve o'clock, for every running download
/// at once; it spins when no server has said how big its file is; and when the last one lands the
/// button bounces once.
///
/// <para><b>No colours here.</b> Both circles and the arrow take theme resources in the XAML, so
/// the ring follows Classic, Liquid Glass, light, dark and high contrast by itself. This class
/// touches only dash lengths, opacity and a rotation.</para>
///
/// <para><b>The arc is a dash, not a geometry.</b> <c>StrokeDashArray</c> is measured in multiples
/// of the stroke's thickness, not pixels, so the circle's length is
/// π·(diameter − thickness)/thickness of them. The visible dash is one unit shorter than the
/// fraction, because the round caps each overhang by half a stroke: without that, 3% draws like 8%.</para>
///
/// <para><b>Moved by a 100 ms tick, not per byte.</b> The engine reports progress many times a
/// second per file. The ring eases 28% of the way to where it should be on each tick, so a lumpy
/// connection still sweeps smoothly and the arc never runs backwards.</para>
/// </summary>
internal sealed class DownloadRing
{
    private readonly FrameworkElement _button;
    private readonly Ellipse _track;
    private readonly Ellipse _progress;
    private readonly RotateTransform _spin;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _tick;
    private readonly bool _private;

    /// <summary>Where the arc starts: an Ellipse's stroke begins at three o'clock, so a quarter turn back.</summary>
    private const double StartAngle = -90;

    private double _units = 28.27;
    private double _shown;
    private double _target;
    private bool _indeterminate;
    private bool _wasActive;
    private Storyboard? _spinning;

    public DownloadRing(FrameworkElement button, Ellipse track, Ellipse progress, RotateTransform spin,
                        Microsoft.UI.Dispatching.DispatcherQueue queue, bool isPrivate)
    {
        _button = button;
        _track = track;
        _progress = progress;
        _spin = spin;
        _private = isPrivate;
        _spin.Angle = StartAngle;

        _tick = queue.CreateTimer();
        _tick.Interval = TimeSpan.FromMilliseconds(100);
        _tick.Tick += (_, _) => Frame();

        _progress.SizeChanged += (_, _) => Measure();
    }

    /// <summary>"Downloading 2 files — 40%", for the button's tooltip.</summary>
    public string Tooltip { get; private set; } = "Downloads";

    /// <summary>What the ring shows right now, 0 to 1 — for the tests, which cannot see a dash.</summary>
    public double Shown => _indeterminate ? -1 : _shown;

    /// <summary>
    /// Everything running in this kind of window, read again. Called when any download starts,
    /// stops or moves; cheap, because the drawing happens on the tick.
    /// </summary>
    public void Sync()
    {
        List<DownloadItem> active = Downloads.Active(_private);
        long received = 0, total = 0;
        int sized = 0;

        foreach (DownloadItem item in active)
        {
            // A download whose server gave no size cannot say how far along it is, so it is left out
            // of the sum; only when NONE of them can say does the ring spin instead.
            if (item.Record.TotalBytes is long t && t > 0)
            {
                sized++;
                received += item.Record.ReceivedBytes;
                total += t;
            }
        }

        if (active.Count == 0)
        {
            bool landed = _wasActive;
            _wasActive = false;
            _target = 0;
            _shown = 0;
            SetIndeterminate(false);
            _tick.Stop();
            Paint();
            Tooltip = "Downloads";

            // Once, when the LAST one finishes — five files landing together are one bounce.
            if (landed)
            {
                Motion.Pop(_button, 1.22f);
            }

            return;
        }

        _wasActive = true;
        SetIndeterminate(sized == 0);
        if (sized > 0)
        {
            _target = Math.Clamp((double)received / total, 0, 1);
        }

        int percent = (int)Math.Round(_target * 100);
        string files = active.Count == 1 ? "" : " " + active.Count + " files";
        Tooltip = _indeterminate ? "Downloading" + files + "…" : "Downloading" + files + " — " + percent + "%";

        if (!_tick.IsRunning)
        {
            _tick.Start();
        }

        Paint();
    }

    public void Stop()
    {
        _tick.Stop();
        _spinning?.Stop();
        _spinning = null;
    }

    private void Measure()
    {
        double thickness = _progress.StrokeThickness;
        double size = Math.Min(_progress.ActualWidth, _progress.ActualHeight);
        if (thickness > 0 && size > thickness)
        {
            _units = Math.PI * (size - thickness) / thickness;
        }

        Paint();
    }

    private int _ticks;

    private void Frame()
    {
        // Once a second: ask the engine about what it does not announce (a stall, a reconnect).
        if (++_ticks % 10 == 0)
        {
            Downloads.Poll();
        }

        if (_indeterminate)
        {
            return;
        }

        double gap = _target - _shown;
        _shown = Math.Abs(gap) < 0.002 ? _target : _shown + gap * 0.28;
        Paint();
    }

    private void Paint()
    {
        bool active = _wasActive;

        // The circle is the arrow's own outline when nothing is happening, and a quieter track
        // under the arc while something is.
        _track.Opacity = active ? 0.32 : 1;

        if (_indeterminate)
        {
            _progress.Opacity = 1;
            _progress.StrokeDashArray = new DoubleCollection { Math.Max(0.1, _units * 0.28 - 1), _units };
            _progress.StrokeDashOffset = 0;
            return;
        }

        if (!active || _shown <= 0.0001)
        {
            _progress.Opacity = 0;
            return;
        }

        double visible = Math.Max(0.01, _units * _shown - 1.0);
        _progress.Opacity = 1;
        _progress.StrokeDashArray = new DoubleCollection { visible, _units + 1 };
        _progress.StrokeDashOffset = 0;
    }

    private void SetIndeterminate(bool on)
    {
        if (on == _indeterminate)
        {
            return;
        }

        _indeterminate = on;
        _spinning?.Stop();
        _spinning = null;
        _spin.Angle = StartAngle;

        // With motion off (Reduce motion) a still quarter arc says "working" without moving.
        if (on && Motion.On)
        {
            DoubleAnimation turn = new()
            {
                From = StartAngle,
                To = StartAngle + 360,
                Duration = new Duration(TimeSpan.FromMilliseconds(1100)),
                RepeatBehavior = RepeatBehavior.Forever,
            };

            // RotateTransform.Angle is an independent animation: it runs on the compositor, not
            // on the UI thread, so a busy page cannot make it stutter.
            Storyboard.SetTarget(turn, _spin);
            Storyboard.SetTargetProperty(turn, "Angle");
            _spinning = new Storyboard();
            _spinning.Children.Add(turn);
            _spinning.Begin();
        }

        Paint();
    }
}
