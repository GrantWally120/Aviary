# What the window shows WHILE a page is replaced (bug 61): captures the test instance's window as
# fast as PrintWindow allows and logs every change of colour at three points on the right half of
# the page — where bgsite.js pages have no text — with a navigation fired part-way through.
#
#   flash-watch.ps1 -Then 'node cdp.js nav "#0" http://127.0.0.2:8772/black' [-Ms 2500] [-At 300]
#                   [-Resize 1280x800] [-Shots <prefix>]
#   flash-watch.ps1 -Snap out.png     one capture of the whole window, nothing else
#   -Title Private                    the window whose title contains this (the private window)
#
# Sends no input and never fronts the window (PrintWindow, PW_RENDERFULLCONTENT), so it is safe
# while Grant is at the PC. -Resize restores the window (even from minimised), sizes it and parks
# it at the BOTTOM of the z-order without activating it — behind his windows; a smaller window
# also captures faster. A colour that lasts less than one capture can be missed: repeat a run
# rather than trusting a single clean one. -Resize alone (no -Then) just parks and captures -Ms.
param(
    [int]$ProcessId = 0,
    [string]$Then = "",
    [int]$Ms = 2500,
    [int]$At = 300,
    [string]$Resize = "",
    [string]$Shots = "",
    [string]$Snap = "",
    [string]$Title = ""
)

$sp = $PSScriptRoot
if ($ProcessId -eq 0) { $ProcessId = [int](Get-Content (Join-Path $sp 'pid.txt')) }

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public static class FlashWatch {
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    public struct RC { public int L, T, R, B; }

    // Process.MainWindowHandle is not reliable here: once a tooltip or popup has existed it can
    // name a 160x28 helper window. The browser window is the process's largest visible one.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);

    public static IntPtr Biggest(int processId, string title) {
        IntPtr best = IntPtr.Zero; long bestArea = 0;
        EnumWindows(delegate (IntPtr h, IntPtr l) {
            uint pid; GetWindowThreadProcessId(h, out pid);
            StringBuilder t = new StringBuilder(256); GetWindowText(h, t, 256);
            if (pid == processId && IsWindowVisible(h) && (title.Length == 0 || t.ToString().Contains(title))) {
                RC r; GetWindowRect(h, out r);
                long area = (long)(r.R - r.L) * (r.B - r.T);
                if (area > bestArea) { bestArea = area; best = h; }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    public static void Shot(IntPtr h, string path) {
        RC r; GetWindowRect(h, out r);
        using (Bitmap bmp = new Bitmap(r.R - r.L, r.B - r.T, PixelFormat.Format32bppArgb)) {
            using (Graphics g = Graphics.FromImage(bmp)) { IntPtr hdc = g.GetHdc(); PrintWindow(h, hdc, 2); g.ReleaseHdc(hdc); }
            bmp.Save(path, ImageFormat.Png);
        }
    }

    [DllImport("user32.dll")] static extern bool SetWindowPlacement(IntPtr h, ref WP p);
    public struct PT { public int X, Y; }
    public struct WP { public int length, flags, showCmd; public PT min, max; public RC normal; }

    // Restored (even from minimised), sized, and parked at the BOTTOM of the z-order without being
    // activated: behind whatever Grant has open. The test launch flags keep Chromium drawing there.
    public static void Park(IntPtr h, int w, int ht) {
        const uint NOSIZE = 0x1, NOMOVE = 0x2, NOACTIVATE = 0x10;
        IntPtr bottom = new IntPtr(1);
        SetWindowPos(h, bottom, 0, 0, 0, 0, NOSIZE | NOMOVE | NOACTIVATE);
        WP p = new WP();
        p.length = Marshal.SizeOf(typeof(WP));
        p.showCmd = 4;                                       // SW_SHOWNOACTIVATE
        p.normal.L = 80; p.normal.T = 60; p.normal.R = 80 + w; p.normal.B = 60 + ht;
        SetWindowPlacement(h, ref p);
        SetWindowPos(h, bottom, 0, 0, 0, 0, NOSIZE | NOMOVE | NOACTIVATE);
    }

    public static string Run(IntPtr h, int ms, int at, string exe, string args, string workDir, string shots) {
        RC r; GetWindowRect(h, out r);
        int w = r.R - r.L, ht = r.B - r.T;
        Point[] pts = { new Point(w * 70 / 100, ht * 50 / 100), new Point(w * 90 / 100, ht * 75 / 100), new Point(w * 60 / 100, ht * 92 / 100) };
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(string.Format("window {0}x{1}; points {2},{3} {4},{5} {6},{7}", w, ht, pts[0].X, pts[0].Y, pts[1].X, pts[1].Y, pts[2].X, pts[2].Y));
        using (Bitmap bmp = new Bitmap(w, ht, PixelFormat.Format32bppArgb)) {
            Stopwatch sw = Stopwatch.StartNew();
            string last = null; int frames = 0, saved = 0; bool fired = false;
            while (sw.ElapsedMilliseconds < ms) {
                if (!fired && exe != null && sw.ElapsedMilliseconds >= at) {
                    ProcessStartInfo psi = new ProcessStartInfo(exe, args);
                    psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WorkingDirectory = workDir;
                    Process.Start(psi);
                    fired = true;
                    sb.AppendLine(string.Format("{0,6} ms  >> {1} {2}", sw.ElapsedMilliseconds, exe, args));
                }
                using (Graphics g = Graphics.FromImage(bmp)) {
                    IntPtr hdc = g.GetHdc();
                    PrintWindow(h, hdc, 2);
                    g.ReleaseHdc(hdc);
                }
                long t = sw.ElapsedMilliseconds;
                frames++;
                string[] parts = new string[pts.Length];
                for (int i = 0; i < pts.Length; i++) {
                    Color c = bmp.GetPixel(pts[i].X, pts[i].Y);
                    parts[i] = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
                }
                string key = string.Join(" ", parts);
                if (key != last) {
                    sb.AppendLine(string.Format("{0,6} ms  {1}", t, key));
                    last = key;
                    if (shots.Length > 0 && saved < 10) {
                        bmp.Save(shots + "-" + saved.ToString("00") + "-" + t + "ms.png", ImageFormat.Png);
                        saved++;
                    }
                }
            }
            sb.AppendLine(string.Format("{0} captures in {1} ms (one per {2:0.0} ms)", frames, ms, (double)ms / frames));
        }
        return sb.ToString();
    }
}
'@

$h = [FlashWatch]::Biggest($ProcessId, $Title)
if ($h -eq [IntPtr]::Zero) { "no visible window for process $ProcessId"; exit 1 }
if ($Resize) {
    $wh = $Resize.Split('x') | ForEach-Object { [int]$_ }
    [FlashWatch]::Park($h, $wh[0], $wh[1])
    Start-Sleep -Milliseconds 700
}
if ($Snap) { [FlashWatch]::Shot($h, $Snap); "SAVED $Snap"; exit 0 }

$exe = $null; $args2 = ""
if ($Then) {
    $first, $rest = $Then.Split(' ', 2)
    $exe = (Get-Command $first -ErrorAction Stop).Source
    $args2 = if ($rest) { $rest } else { "" }
}
[FlashWatch]::Run($h, $Ms, $At, $exe, $args2, $sp, $Shots)
