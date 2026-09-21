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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string id);

    /// <summary>
    /// Who Aviary is to the shell (M4.8, bug 77). Without this, an unpackaged app has no identity
    /// of its own, so Windows derives one from the .exe and the taskbar button draws the icon baked
    /// INTO the .exe — which is why Settings → App Icon appeared to do nothing: the window's icon
    /// really did change (measured through WM_GETICON, both sizes, at start-up and live), but the
    /// taskbar was never looking at it. With an explicit id the button is Aviary's own and follows
    /// the window's icon.
    ///
    /// It must be set BEFORE any window exists, which is why it lives here and not in MainWindow.
    /// The id is per profile: two Aviarys on different profiles are already two instances (bug 51),
    /// and giving them one identity would group them under a single taskbar button.
    /// </summary>
    private static void TellTheShellWhoWeAre()
    {
        try
        {
            string profile = AppPaths.Root;
            string tag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profile.ToLowerInvariant()))).Substring(0, 8);
            SetCurrentProcessExplicitAppUserModelID("Aviary.Browser." + tag);
        }
        catch (Exception ex)
        {
            // No identity means the taskbar falls back to the .exe's icon - the old behaviour,
            // not a broken one.
            Diagnostics.Swallowed(ex, "shell: app id");
        }
    }

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
        // Before anything native can fail: a C++ exception nobody catches ends in abort(), which
        // no .NET handler sees — this is what records it (bug 50).
        NativeCrash.Install();

        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (HandedToRunningAviary() || AnotherCopyOwnsProfile())
        {
            return;
        }

        // Before the first window: the taskbar decides which icon a button wears from this.
        TellTheShellWhoWeAre();

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

    /// <summary>Held from start-up until everything is written (<see cref="Release"/>).</summary>
    private static Mutex? _profileLock;

    /// <summary>
    /// The rest of bug 51 (HANDOFF §8): the Windows App SDK scopes an unpackaged app's instance keys
    /// to its EXECUTABLE, so a second copy of Aviary.exe in another folder, on the same profile,
    /// did not see the first and ran beside it — two writers again. A named lock per profile is
    /// held by whichever copy owns it, whatever its folder. A launch that finds it held waits a
    /// few seconds (the owner may be closing: it lets go once its last writes are done — and a
    /// copy that crashed leaves it abandoned, which counts as free), then says so in plain words
    /// and ends having written nothing. It cannot hand itself over the way a launch of the same
    /// copy does: nothing about the other copy's window is reachable from here.
    /// </summary>
    private static bool AnotherCopyOwnsProfile()
    {
        try
        {
            _profileLock = new Mutex(initiallyOwned: false, @"Local\" + InstanceKey() + "-profile");
            bool taken;
            try
            {
                taken = _profileLock.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                taken = true;
            }

            if (taken)
            {
                return false;
            }

            _profileLock.Dispose();
            _profileLock = null;
            MessageBoxW(IntPtr.Zero,
                "Aviary is already open — another copy of it, from a different folder, is using the same "
                + "profile. Two copies at once would both write your history, settings and bookmarks.\n\n"
                + "Close the Aviary that is open first, or carry on using that one.",
                "Aviary is already open",
                0x00000040 /* MB_ICONINFORMATION */ | 0x00010000 /* MB_SETFOREGROUND */);
            return true;
        }
        catch (Exception ex)
        {
            // Without it Aviary still runs, exactly as before this guard.
            InstanceFailure ??= ex;
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

        // Before the relaunch below, which would otherwise wait on it. On the UI thread — Main's
        // own, the thread that took it — as a mutex must be.
        try
        {
            _profileLock?.ReleaseMutex();
            _profileLock?.Dispose();
            _profileLock = null;
        }
        catch (Exception)
        {
            // Released with the process moments from now anyway.
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
