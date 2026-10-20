# Launch a TEST instance of Aviary without ever taking the foreground (M4.5, 19 Sep 2026).
#
# launch-m2.ps1 maximises and fronts its window, which is only fair when Grant is away. This one
# starts Aviary through CreateProcess with STARTF_USESHOWWINDOW + SW_SHOWMINNOACTIVE - Windows applies
# that to the app's FIRST ShowWindow, whatever the app asks for - so the window opens minimised and
# inactive. Once it exists it is restored at the BOTTOM of the z-order, again without activating
# (flash-watch.ps1's Park). The test flags keep Chromium drawing there, so cdp.js, uia.ps1 and
# peek.ps1 all work with Grant's windows in front. No input is sent; guard.ps1 is not needed.
#
# Also a standing check of bug 50: this is a launch that is minimised before its first page.
#
#   launch-quiet.ps1 -Profile b68 -Fresh -SettingsJson '{"tabLayout":"Top bar"}' [-Size 1280x860]
#
# -AsShipped (1 Oct 2026): the engine as Aviary ships it - WITHOUT the three keep-drawing flags, which
# exist to change what the engine counts as on screen and what it throttles. So anything about sleeping,
# page visibility or background throttling is measured -AsShipped (bug 97 was: it shows either way). The
# cost: a window parked behind Grant's may count as covered, and its pages as hidden - find tabs by id or
# by when their page started, not by "the visible one".
param(
    [string]$Profile = "p1",
    [switch]$Fresh,
    [string]$SettingsJson = "",
    [int]$Port = 9222,
    [string]$ExtraArgs = "",
    [string]$Size = "1280x860",
    [string]$Exe = "",
    [switch]$AsShipped
)

$sp = $PSScriptRoot
if (-not $Exe) { $Exe = Join-Path $sp "pub\Aviary.exe" }
$profileDir = Join-Path $sp ("profiles\" + $Profile)
if ($Fresh -and (Test-Path $profileDir)) { Remove-Item $profileDir -Recurse -Force }
New-Item -ItemType Directory -Force $profileDir | Out-Null
if ($SettingsJson) { [System.IO.File]::WriteAllText((Join-Path $profileDir "settings.json"), $SettingsJson) }

Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class Quiet {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SI { public int cb; public string r, desk, title; public int x, y, w, h, cx, cy, fill, flags; public short show, r2; public IntPtr r3, i, o, e; }
    [StructLayout(LayoutKind.Sequential)]
    struct PI { public IntPtr hp, ht; public int pid, tid; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string dir, ref SI si, out PI pi);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    public static int Start(string exe, string dir) {
        SI si = new SI(); si.cb = Marshal.SizeOf(typeof(SI));
        si.flags = 0x1;          // STARTF_USESHOWWINDOW
        si.show = 7;             // SW_SHOWMINNOACTIVE
        PI pi;
        // Inherits this process's environment: AVIARY_PROFILE_DIR and AVIARY_TEST_BROWSER_ARGS.
        if (!CreateProcess(exe, new StringBuilder("\"" + exe + "\""), IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, dir, ref si, out pi))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(pi.hp); CloseHandle(pi.ht);
        return pi.pid;
    }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h, ref WP p);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);

    // Aviary activates its own window during start-up whatever the launch asked for (seen 19 Sep:
    // the window took the foreground from Grant). If it has, minimise it - Windows then activates
    // the next window, which is the one Grant was in - and restore it again without activating.
    public static bool GiveBackFocus(IntPtr h, int processId) {
        uint fgPid; GetWindowThreadProcessId(GetForegroundWindow(), out fgPid);
        if (fgPid != processId) return false;
        const uint NOSIZE = 0x1, NOMOVE = 0x2, NOACTIVATE = 0x10;
        ShowWindow(h, 6);                                     // SW_MINIMIZE
        System.Threading.Thread.Sleep(400);
        ShowWindow(h, 4);                                     // SW_SHOWNOACTIVATE
        SetWindowPos(h, new IntPtr(1), 0, 0, 0, 0, NOSIZE | NOMOVE | NOACTIVATE);
        return true;
    }
    delegate bool EnumProc(IntPtr h, IntPtr l);
    public struct RC { public int L, T, R, B; }
    public struct PT { public int X, Y; }
    public struct WP { public int length, flags, showCmd; public PT min, max; public RC normal; }

    // The largest visible window of the process - MainWindowHandle can name a helper window.
    public static IntPtr Biggest(int processId) {
        IntPtr best = IntPtr.Zero; long bestArea = -1;
        EnumWindows(delegate (IntPtr h, IntPtr l) {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == processId && IsWindowVisible(h)) {
                RC r; GetWindowRect(h, out r);
                long area = (long)(r.R - r.L) * (r.B - r.T);
                if (area > bestArea) { bestArea = area; best = h; }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    public static void Park(IntPtr h, int w, int ht) {
        const uint NOSIZE = 0x1, NOMOVE = 0x2, NOACTIVATE = 0x10;
        IntPtr bottom = new IntPtr(1);
        SetWindowPos(h, bottom, 0, 0, 0, 0, NOSIZE | NOMOVE | NOACTIVATE);
        WP p = new WP(); p.length = Marshal.SizeOf(typeof(WP));
        p.showCmd = 4;           // SW_SHOWNOACTIVATE
        p.normal.L = 80; p.normal.T = 60; p.normal.R = 80 + w; p.normal.B = 60 + ht;
        SetWindowPlacement(h, ref p);
        SetWindowPos(h, bottom, 0, 0, 0, 0, NOSIZE | NOMOVE | NOACTIVATE);
    }
}
'@

$env:AVIARY_PROFILE_DIR = $profileDir
$keepDrawing = if ($AsShipped) { "" } else { "--disable-features=CalculateNativeWinOcclusion --disable-backgrounding-occluded-windows --disable-background-timer-throttling" }
$env:AVIARY_TEST_BROWSER_ARGS = ("--remote-debugging-port=$Port $keepDrawing " + $ExtraArgs).Trim()
$fgBefore = [Quiet]::GetForegroundWindow()
$procId = [Quiet]::Start($Exe, (Split-Path $Exe))
$env:AVIARY_PROFILE_DIR = $null; $env:AVIARY_TEST_BROWSER_ARGS = $null

$h = [IntPtr]::Zero
for ($i = 0; $i -lt 40 -and $h -eq [IntPtr]::Zero; $i++) {
    Start-Sleep -Milliseconds 250
    if (-not (Get-Process -Id $procId -ErrorAction SilentlyContinue)) { "EXITED early (pid $procId)"; exit 1 }
    $h = [Quiet]::Biggest($procId)
}
if ($h -eq [IntPtr]::Zero) { "NO WINDOW after 10 s (pid $procId)"; exit 1 }

Start-Sleep -Milliseconds 2500          # let the first page start while still minimised (bug 50's window)
if (-not (Get-Process -Id $procId -ErrorAction SilentlyContinue)) { "EXITED while minimised (pid $procId) - bug 50?"; exit 1 }
$wh = $Size.Split('x') | ForEach-Object { [int]$_ }
$h = [Quiet]::Biggest($procId)
[Quiet]::Park($h, $wh[0], $wh[1])
Start-Sleep -Milliseconds 300
$gaveBack = [Quiet]::GiveBackFocus($h, $procId)
$stole = [Quiet]::GiveBackFocus($h, $procId)          # still ours after giving it back = could not
$procId | Set-Content (Join-Path $sp "pid.txt")
"TEST PID $procId profile=$profileDir took-focus-and-gave-it-back=$gaveBack still-has-focus=$stole"
