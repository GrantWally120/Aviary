# Capture the test instance's open popup (a menu or popover) by its OWN window (M5).
#
# A WinUI flyout is drawn in a separate top-level PopupWindowSiteBridge window, so peek.ps1 on the
# main window never shows it (M4.7). This finds the process's visible popup window and PrintWindows
# THAT - no input, no focus change. The last one found wins; close tooltips first (M4.7's note).
#
#   popup-shot.ps1 -ProcessId <pid> -Out shots\x.png [-Zoom 2]
param([int]$ProcessId, [string]$Out, [int]$Zoom = 1)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class PS {
  delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  [StructLayout(LayoutKind.Sequential)] public struct RC { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
  public static IntPtr Find(uint pid) {
    IntPtr f = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h)) { var c = new StringBuilder(256); GetClassName(h, c, 256);
        if (c.ToString().Contains("PopupWindowSiteBridge")) f = h; } return true; }, IntPtr.Zero);
    return f;
  }
}
'@ -ErrorAction SilentlyContinue
$h = [PS]::Find([uint32]$ProcessId)
if ($h -eq [IntPtr]::Zero) { "NO POPUP OPEN"; exit 1 }
$r = New-Object PS+RC; [void][PS]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $hh = $r.B - $r.T
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp); $dc = $g.GetHdc()
$ok = [PS]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc); $g.Dispose()
if ($Zoom -gt 1) {
  $big = New-Object System.Drawing.Bitmap ($w * $Zoom), ($hh * $Zoom)
  $g2 = [System.Drawing.Graphics]::FromImage($big); $g2.InterpolationMode = 'HighQualityBicubic'
  $g2.DrawImage($bmp, 0, 0, $w * $Zoom, $hh * $Zoom); $g2.Dispose(); $bmp.Dispose(); $bmp = $big
}
$bmp.Save($Out); $bmp.Dispose()
"PrintWindow=$ok  popup ${w}x${hh} at $($r.L),$($r.T)  SAVED $Out"
