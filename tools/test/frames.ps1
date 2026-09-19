# Film an animation in a TEST Aviary (M4.5 motion): PrintWindow as fast as it goes while -Then runs,
# cropped to -Crop "x,y,w,h" (window pixels), and every capture that differs from the one before
# kept - then laid out top to bottom as ONE contact sheet with each frame's time written beside it.
# No input, no focus change: the window can sit behind Grant's. Pair with cdp.js / uia.ps1 in -Then.
#
#   frames.ps1 -ProcessId <pid> -Crop "0,40,1280,48" -Then "node cdp.js key #0 ctrl+KeyT" -Ms 700 -Out shots/tab-open.png
param([int]$ProcessId, [string]$Crop, [string]$Then = "", [int]$At = 60, [int]$Ms = 700, [string]$Out, [int]$MaxFrames = 18, [int]$Scale = 1)

Add-Type -ReferencedAssemblies System.Drawing @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

public static class Frames {
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    public struct RC { public int L, T, R, B; }

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

    static string Sig(Bitmap b) {
        // A cheap fingerprint: a coarse grid of pixels.
        StringBuilder sb = new StringBuilder();
        for (int y = 2; y < b.Height; y += Math.Max(1, b.Height / 6))
            for (int x = 2; x < b.Width; x += Math.Max(1, b.Width / 64)) { Color c = b.GetPixel(x, y); sb.Append(c.R / 8).Append(c.G / 8).Append(c.B / 8); }
        return sb.ToString();
    }

    public static string Run(IntPtr h, int cx, int cy, int cw, int ch, int ms, int at, string exe, string args, string dir, string outPath, int maxFrames, int scale) {
        RC r; GetWindowRect(h, out r);
        int w = r.R - r.L, ht = r.B - r.T;
        List<Bitmap> kept = new List<Bitmap>(); List<long> times = new List<long>();
        StringBuilder log = new StringBuilder();
        using (Bitmap full = new Bitmap(w, ht, PixelFormat.Format32bppArgb)) {
            Stopwatch sw = Stopwatch.StartNew(); string last = null; bool fired = exe == null; int caps = 0;
            while (sw.ElapsedMilliseconds < ms) {
                if (!fired && sw.ElapsedMilliseconds >= at) {
                    ProcessStartInfo psi = new ProcessStartInfo(exe, args); psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WorkingDirectory = dir;
                    Process.Start(psi); fired = true; log.AppendLine(string.Format("{0,5} ms  >> {1} {2}", sw.ElapsedMilliseconds, exe, args));
                }
                using (Graphics g = Graphics.FromImage(full)) { IntPtr hdc = g.GetHdc(); PrintWindow(h, hdc, 2); g.ReleaseHdc(hdc); }
                caps++;
                Bitmap crop = full.Clone(new Rectangle(cx, cy, cw, ch), PixelFormat.Format32bppArgb);
                string s = Sig(crop);
                if (s != last && kept.Count < maxFrames) { kept.Add(crop); times.Add(sw.ElapsedMilliseconds); last = s; }
                else crop.Dispose();
            }
            log.AppendLine(string.Format("{0} captures in {1} ms; {2} distinct frames kept", caps, ms, kept.Count));
        }
        int label = 64;
        using (Bitmap sheet = new Bitmap((cw + label) * scale, Math.Max(1, ch * kept.Count) * scale, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(sheet))
        using (Font f = new Font("Consolas", 11)) {
            g.Clear(Color.Black);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            for (int i = 0; i < kept.Count; i++) {
                g.DrawString(times[i] + " ms", f, Brushes.White, 2 * scale, (i * ch + ch / 2 - 8) * scale);
                g.DrawImage(kept[i], label * scale, i * ch * scale, cw * scale, ch * scale);
                kept[i].Dispose();
            }
            sheet.Save(outPath, ImageFormat.Png);
        }
        return log.ToString();
    }
}
'@

$h = [Frames]::Biggest($ProcessId)
if ($h -eq [IntPtr]::Zero) { "no window for $ProcessId"; exit 1 }
$c = $Crop.Split(',') | ForEach-Object { [int]$_ }
$exe = $null; $args2 = ""
if ($Then) { $first, $rest = $Then.Split(' ', 2); $exe = (Get-Command $first -ErrorAction Stop).Source; $args2 = if ($rest) { $rest } else { "" } }
[Frames]::Run($h, $c[0], $c[1], $c[2], $c[3], $Ms, $At, $exe, $args2, $PSScriptRoot, $Out, $MaxFrames, $Scale)
"SAVED $Out"
