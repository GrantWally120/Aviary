using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Aviary;

/// <summary>
/// Aviary's entry point — its own rather than the one the XAML compiler generates
/// (DISABLE_XAML_GENERATED_MAIN in the project), for one reason: one Aviary per profile.
///
/// Bug 51 (12 Sep 2026): launching Aviary while it was already open started a second process
/// on the same profile — two writers on settings, history and the diagnostics folder — and the
/// second one's start-up read the first one's LIVE session as "never closed cleanly", a false
/// unclean-exit in Grant's diagnostics. So before anything touches the profile, a launch asks
/// whether an Aviary already owns it (the Windows App SDK's AppInstance, keyed on the profile
/// folder). If one does, the launch is handed to it — its window comes to the front — and this
/// process ends having written nothing.
///
/// Keyed on the profile folder, so a test instance on a scratch profile never hands itself to
/// Grant's copy, nor his to a test.
///
/// A launch that reaches an Aviary whose last window is already closing would otherwise be
/// lost (the easy way to hit it: close Aviary for a "restart Aviary" setting and open it again
/// at once). That Aviary finishes writing, gives up the profile, and then starts a new one.
///
/// The rest of Main is exactly what the generated one did. There is still no
/// Bootstrap.Initialize: the self-contained Windows App SDK is set up by the SDK's own module
/// initializer, before Main runs.
/// </summary>
public static class Program
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    private enum InstanceState { Running, Closing, Released }

    private static readonly object Gate = new();
    private static InstanceState _state = InstanceState.Running;
    private static bool _relaunchRequested;
    private static AppInstance? _owner;
    private static DispatcherQueue? _ui;

    /// <summary>Why one-Aviary-per-profile could not be set up, for diagnostics to report once it runs.</summary>
    internal static Exception? InstanceFailure { get; private set; }

    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (HandedToRunningAviary())
        {
            return;
        }

        Application.Start(p =>
        {
            DispatcherQueueSynchronizationContext context = new(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    /// <summary>True when another Aviary owns this profile and has been handed this launch.</summary>
    private static bool HandedToRunningAviary()
    {
        try
        {
            AppInstance owner = AppInstance.FindOrRegisterForKey(InstanceKey());
            if (owner.IsCurrent)
            {
                _owner = owner;
                return false;
            }

            // This process may take the foreground — the user just launched it — and passes
            // that right on, so the running Aviary's window can come to the front.
            AllowSetForegroundWindow(owner.ProcessId);

            AppActivationArguments args = AppInstance.GetCurrent().GetActivatedEventArgs();

            // Off this STA thread, and bounded: a running Aviary that has hung must not hang this
            // launch too. Either way this process ends — a second writer on the profile is the
            // very thing being prevented.
            Task.Run(() => owner.RedirectActivationToAsync(args).AsTask()).Wait(TimeSpan.FromSeconds(10));
            return true;
        }
        catch (Exception ex)
        {
            // Without it Aviary still runs, exactly as it did before bug 51's fix.
            InstanceFailure = ex;
            return false;
        }
    }

    /// <summary>The profile folder, as a short stable key (case and trailing separators aside).</summary>
    private static string InstanceKey()
    {
        string root = System.IO.Path.GetFullPath(AppPaths.Root).TrimEnd('\\', '/').ToUpperInvariant();
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(root));
        return "Aviary-" + Convert.ToHexString(hash, 0, 12);
    }

    /// <summary>
    /// From App, once the UI thread exists: from here on, a launch handed over by another process
    /// is shown by <paramref name="show"/> on that thread.
    /// </summary>
    internal static void ListenForOtherLaunches(DispatcherQueue ui, Action show)
    {
        if (_owner is null)
        {
            return;
        }

        _ui = ui;
        _owner.Activated += (_, _) =>
        {
            // Raised on a background thread.
            if (_ui is not { } queue || !queue.TryEnqueue(() => ShowOrDefer(show)))
            {
                ShowOrDefer(null);
            }
        };
    }

    private static void ShowOrDefer(Action? show)
    {
        bool relaunchNow = false;
        lock (Gate)
        {
            switch (_state)
            {
                case InstanceState.Running when show is not null:
                    break;

                case InstanceState.Running:
                case InstanceState.Closing:
                    _relaunchRequested = true;
                    return;

                case InstanceState.Released:
                    relaunchNow = true;
                    break;
            }
        }

        if (relaunchNow)
        {
            StartAnotherAviary();
            return;
        }

        show!();
    }

    /// <summary>The last window has started closing: a launch from now on waits for <see cref="Release"/>.</summary>
    internal static void BeginClosing()
    {
        lock (Gate)
        {
            _state = InstanceState.Closing;
        }
    }

    /// <summary>
    /// Everything is written: give up the profile, and if a launch arrived while this Aviary was
    /// closing, start the Aviary it asked for.
    /// </summary>
    internal static void Release()
    {
        bool relaunch;
        lock (Gate)
        {
            _state = InstanceState.Released;
            relaunch = _relaunchRequested;
        }

        try
        {
            _owner?.UnregisterKey();
        }
        catch (Exception)
        {
            // Nothing is left to record it in (diagnostics has already shut down), and the key
            // goes with this process moments from now anyway.
        }

        if (relaunch)
        {
            StartAnotherAviary();
        }
    }

    private static void StartAnotherAviary()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                // Same environment, so a test instance's scratch profile goes with it.
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
            }
        }
        catch (Exception)
        {
            // Diagnostics has shut down; the launch is lost, as it would have been before.
        }
    }
}
