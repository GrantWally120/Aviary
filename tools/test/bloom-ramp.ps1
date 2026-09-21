# Does a menu actually BLOOM? (M4.9 - replaces bloom-frames.ps1's claim, which was never checked
# against a control and turned out to count every repaint: reduce-motion scored HIGHER than motion.)
#
# A bloom fades the menu's opacity 0 -> 1 over ~160 ms. Captured from the moment its popup window
# exists, that is a brightness RAMP across the first frames (dark theme: black -> the drawn menu).
# With no bloom the first capture is already the finished menu, so the series is flat from frame 1.
# Prints each capture's time since the popup appeared and its mean brightness, then the verdict:
# how far below the settled brightness the FIRST capture was.
#
#   bloom-ramp.ps1 -ProcessId <pid> [-Id MenuButton] [-Ms 700]
param([int]$ProcessId, [string]$Id = "MenuButton", [int]$Ms = 700)

$sp = $PSScriptRoot
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class BR {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public struct RC { public int L,T,R,B; }
  public static IntPtr Popup(int pid2) { IntPtr f = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid2 && IsWindowVisible(h)) { var c = new StringBuilder(256); GetClassName(h, c, 256);
        if (c.ToString().Contains("PopupWindowSiteBridge")) f = h; } return true; }, IntPtr.Zero);
    return f; }
  // Mean brightness of every 3rd pixel - fast enough to sample many frames.
  public static double Mean(System.Drawing.Bitmap b) {
    long sum = 0; int n = 0;
    for (int y = 2; y < b.Height - 2; y += 3) for (int x = 2; x < b.Width - 2; x += 3) {
      var c = b.GetPixel(x, y); sum += c.R + c.G + c.B; n++; }
    return n == 0 ? 0 : sum / (3.0 * n); }
}
'@ -ReferencedAssemblies System.Drawing -ErrorAction Stop

if ([BR]::Popup($ProcessId) -ne [IntPtr]::Zero) { "a popup is already open - close it first, or the first frame is not the arrival"; exit 2 }

$job = Start-Job -ScriptBlock { param($s, $p, $i) & (Join-Path $s 'uia.ps1') -ProcessId $p -Id $i -Action Invoke | Out-Null } -ArgumentList $sp, $ProcessId, $Id

# Wait for the popup to exist (the job needs a moment to start), then time from THAT moment.
$wait = [System.Diagnostics.Stopwatch]::StartNew()
$h = [IntPtr]::Zero
while ($h -eq [IntPtr]::Zero -and $wait.ElapsedMilliseconds -lt 8000) { $h = [BR]::Popup($ProcessId) }
if ($h -eq [IntPtr]::Zero) { "the popup never opened"; Receive-Job $job -Wait -AutoRemoveJob | Out-Null; exit 1 }

$series = @()
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.ElapsedMilliseconds -lt $Ms) {
    $r = New-Object BR+RC; [void][BR]::GetWindowRect($h, [ref]$r)
    $w = $r.R - $r.L; $ht = $r.B - $r.T
    if ($w -le 8 -or $ht -le 8) { continue }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [void][BR]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
    $series += [pscustomobject]@{ ms = $sw.ElapsedMilliseconds; mean = [Math]::Round([BR]::Mean($bmp), 1) }
    $bmp.Dispose()
}
Receive-Job $job -Wait -AutoRemoveJob | Out-Null

$settled = ($series | Select-Object -Last 5 | Measure-Object mean -Average).Average
$first = $series[0].mean
"captures: $($series.Count)   first at +$($series[0].ms) ms"
$series | Select-Object -First 10 | ForEach-Object { "  +{0,4} ms  brightness {1}" -f $_.ms, $_.mean }
"  ..."
"settled brightness {0:N1} - first capture {1:N1} = {2:N1} below" -f $settled, $first, ($settled - $first)
