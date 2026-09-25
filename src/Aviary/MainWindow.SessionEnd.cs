using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aviary;

/// <summary>
/// Windows shutting down, restarting or signing out with Aviary still open (bug 64).
///
/// Found in Grant's own diagnostics: on 16 Sep he shut Windows down from the Start menu at
/// 16:55:59 with Aviary open, and the next launch reported that session as "never closed
/// cleanly". Windows does not close the windows of a process it is ending. It asks every
/// top-level window WM_QUERYENDSESSION, then tells it WM_ENDSESSION — and once that has been
/// answered the process may be ended at any moment. WinUI raises no event for either, so
/// Window.Closed never ran: History, the Privacy Report and the favicon index lost whatever was
/// still waiting for its debounced write, and diagnostics wrote no ending — a false alarm.
///
/// So each window's procedure is subclassed for WM_ENDSESSION. It does for the data exactly what
/// the last window's Closed does — every pending write, then diagnostics' last summary, with the
/// reason — and nothing more: no tab is torn down, because the process is going anyway and time
/// spent there is time Windows may not give. Once per process, whichever window hears it first.
/// WM_QUERYENDSESSION is left to the default (yes): Aviary never holds up a shutdown.
/// </summary>
public sealed partial class MainWindow
{
    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr refData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private const uint WmEndSession = 0x0016;
    private const long EndSessionCloseApp = 0x1;
    private const long EndSessionLogoff = 0x80000000;
    private static readonly UIntPtr SessionEndSubclassId = new(0xA71A);

    /// <summary>Kept for the window's life: comctl32 holds a pointer to it.</summary>
    private SubclassProc? _sessionEndProc;

    private static int _sessionEnded;

    private void WatchForSessionEnd(IntPtr hWnd)
    {
        try
        {
            _sessionEndProc = OnWindowMessage;
            if (!SetWindowSubclass(hWnd, _sessionEndProc, SessionEndSubclassId, UIntPtr.Zero))
            {
                Diagnostics.Anomaly("session-end-hook", "SetWindowSubclass refused the window");
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Swallowed(ex, "session end: subclass the window");
        }
    }

    private const uint WmEnterSizeMove = 0x0231;
    private const uint WmExitSizeMove = 0x0232;
    private const uint WmEnterMenuLoop = 0x0211;
    private const uint WmExitMenuLoop = 0x0212;

    /// <summary>WM_SIZE, and the wParam that means the window has just been minimised (M4.9).</summary>
    private const uint WmSize = 0x0005;
    private const int SizeMinimized = 1;

    private IntPtr OnWindowMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr refData)
    {
        switch (msg)
        {
            // wParam is non-zero when the session really is ending (zero: another app cancelled it).
            case WmEndSession when wParam != IntPtr.Zero:
                SaveForSessionEnd(lParam.ToInt64());
                break;

            // Windows' own modal loops (bug 67): while the window is dragged, sized or has its
            // system menu open, Windows runs the message loop, and the watchdog must not report
            // the wait as Aviary failing to respond.
            case WmEnterSizeMove:
                Diagnostics.NoteModalLoop("moving or resizing the window", entering: true);
                break;
            case WmExitSizeMove:
                Diagnostics.NoteModalLoop("moving or resizing the window", entering: false);
                break;
            case WmEnterMenuLoop:
                Diagnostics.NoteModalLoop("a window menu", entering: true);
                break;
            case WmExitMenuLoop:
                Diagnostics.NoteModalLoop("a window menu", entering: false);
                break;

            // M4.9: memory saver on Aggressive lets go the moment the window is minimised —
            // nothing on screen is being looked at. Posted to the queue rather than done here:
            // this is inside Windows' own window procedure, and suspending a WebView from it
            // would run engine work inside a message Windows is waiting on.
            case WmSize when wParam.ToInt32() == SizeMinimized:
                DispatcherQueue.TryEnqueue(OnWindowMinimised);
                break;
        }

        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }

    private static void SaveForSessionEnd(long flags)
    {
        if (Interlocked.Exchange(ref _sessionEnded, 1) == 1)
        {
            return;
        }

        // M4.9: Windows ending the session closes Aviary too - forgetful sites get their due times.
        MarkForgetfulOnExit();

        // M5: and the download list gets its quit rule and its last write.
        Downloads.OnQuit();

        string reason = (flags & EndSessionLogoff) != 0 ? "Windows signed out"
            : (flags & EndSessionCloseApp) != 0 ? "closed by Windows for an installer or update"
            : "Windows shut down or restarted";

        // The same writes, in the same order, as MainWindow_Closed for the last window.
        try { Favicons.FlushAsync().GetAwaiter().GetResult(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "session end: favicon flush"); }
        try { PrivacyReport.Save(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "session end: privacy report"); }
        try { History.Save(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "session end: history"); }
        try { AppSettings.SaveIfUnsaved(); } catch (Exception ex) { Diagnostics.Swallowed(ex, "session end: settings"); }

        // M6: Windows ending the session is a normal close, not a crash to blame on an experiment.
        SafetyMarker.CleanEnd();

        Diagnostics.Shutdown(reason);
    }
}
