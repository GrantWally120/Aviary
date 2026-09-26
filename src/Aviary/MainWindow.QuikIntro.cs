using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Aviary;

/// <summary>
/// Quik's loading screen (Alpha Build 5 M7 "Super Quik"). Grant's video plays ONCE — as Aviary starts running
/// Quik for the first time after it was switched on — muted, with a Skip button (his choices, 26 Sep). The
/// browser starts underneath at the same moment, so the video costs the start nothing: by the time it ends,
/// the first page is there.
///
/// <para>The video is <c>Assets\Quik\loading.mp4</c> — Grant's work in progress, replaceable by dropping in a
/// new file of the same name. No file, a file that will not play, or Reduce motion on: no overlay, and nothing
/// is owed any more. It can never hold the window: it ends by itself after <see cref="IntroLimit"/> whatever
/// the video does.</para>
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan IntroLimit = TimeSpan.FromSeconds(15);

    /// <summary>Shown once per launch at most, whichever window gets there first.</summary>
    private static bool _quikIntroTried;

    private Grid? _quikIntro;
    private MediaPlayer? _quikPlayer;
    private MediaPlayerElement? _quikVideo;
    private long _quikIntroStarted;

    /// <summary>From the first ordinary window's start (RootGrid_Loaded), before its tabs are made.</summary>
    private void ShowQuikIntroIfDue()
    {
        if (_private || _linkWindow || _quikIntroTried || !Quik.IntroDue)
        {
            return;
        }

        _quikIntroTried = true;
        string file = Path.Combine(AppContext.BaseDirectory, "Assets", "Quik", "loading.mp4");
        if (AppSettings.GetBool("reduceMotion") || !File.Exists(file))
        {
            string why = File.Exists(file) ? "reduce motion" : "no video";
            Quik.Test("intro not shown: " + why);
            DiagFeature("quik-intro", new { how = "not shown", why });
            Quik.IntroDone();
            return;
        }

        try
        {
            MediaPlayer player = new() { IsMuted = true, Source = MediaSource.CreateFromUri(new Uri(file)) };
            MediaPlayerElement video = new()
            {
                AreTransportControlsEnabled = false,
                Stretch = Stretch.UniformToFill,
            };
            video.SetMediaPlayer(player);

            Button skip = new()
            {
                Content = "Skip",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 28, 28),
                Padding = new Thickness(18, 7, 18, 8),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(skip, "Skip");
            skip.Click += (_, _) => EndQuikIntro("skipped");

            Grid overlay = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Black) };
            // Esc skips too - the keyboard is on Skip while it plays.
            overlay.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape)
                {
                    e.Handled = true;
                    EndQuikIntro("skipped");
                }
            };
            Grid.SetRowSpan(overlay, 5);
            Canvas.SetZIndex(overlay, 10000);
            overlay.Children.Add(video);
            overlay.Children.Add(skip);
            RootGrid.Children.Add(overlay);

            player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() => EndQuikIntro("ended"));
            player.MediaFailed += (_, e) => DispatcherQueue.TryEnqueue(() => EndQuikIntro("failed: " + e.ErrorMessage));

            _quikIntro = overlay;
            _quikPlayer = player;
            _quikVideo = video;
            _quikIntroStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            player.Play();
            skip.Focus(FocusState.Programmatic);
            Quik.Test("intro shown, muted=" + player.IsMuted);

            // Whatever the video does, the window is the person's again after the limit.
            DispatcherQueue.TryEnqueue(async () =>
            {
                await System.Threading.Tasks.Task.Delay(IntroLimit);
                EndQuikIntro("time limit");
            });
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik: loading screen");
            EndQuikIntro("failed: " + ex.GetType().Name);
        }
    }

    private void EndQuikIntro(string how)
    {
        if (_quikIntro is null && _quikPlayer is null)
        {
            if (how.StartsWith("failed", StringComparison.Ordinal))
            {
                Quik.IntroDone();
            }

            return;
        }

        // Measured 26 Sep, step by step: pausing, taking the player off the picture and the picture off the window
        // cost 0.1-2.3 ms together - and Dispose() alone 214-219 ms, every time: on the window's thread that was a
        // stall at the end of every loading screen (204 ms on Grant's first try). Disposing it off that thread while
        // the picture still held it HUNG Aviary on a Skip. So: detach and remove here, then dispose a player that
        // nothing on the window's thread holds any more, off it.
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        MediaPlayer? player = _quikPlayer;
        try
        {
            player?.Pause();
            _quikVideo?.SetMediaPlayer(null);
            if (_quikIntro is { } overlay)
            {
                RootGrid.Children.Remove(overlay);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "quik: end the loading screen");
        }

        Quik.Test("intro taken down in " + Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1) + " ms on the window's thread");
        if (player is not null)
        {
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    player.Dispose();
                }
                catch (Exception ex)
                {
                    Diagnostics.Swallowed(ex, "quik: dispose the loading screen's player");
                }
            });
        }

        _quikIntro = null;
        _quikPlayer = null;
        _quikVideo = null;
        Quik.IntroDone();

        // The keyboard was on Skip: it goes back to the page, where it would have been.
        FocusPage(_active);

        double ms = Math.Round(System.Diagnostics.Stopwatch.GetElapsedTime(_quikIntroStarted).TotalMilliseconds);
        Quik.Test("intro " + how + " after " + ms + " ms");
        DiagFeature("quik-intro", new { how, ms });
    }
}
