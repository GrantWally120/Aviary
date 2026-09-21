# Does a menu still BLOOM? (M4.7.) Captures the flyout's own popup window as fast as PrintWindow
# goes from the moment it appears, and reports how many captures differ from the one before -
# an arrival that animates changes for ~160 ms, one that snaps is identical from the first frame.
# No input, no focus change.
#
#   bloom-frames.ps1 -ProcessId <pid> -Id MenuButton [-Ms 500]
# NOTE: PowerShell variables are case-insensitive - $ms would BE the -Ms parameter (README, 19 Sep).
param([int]$ProcessId, [string]$Id = "MenuButton", [int]$Ms = 500, [string]$Out = "")

$sp = $PSScriptRoot
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class BF {
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
}
'@ -ErrorAction SilentlyContinue

# Open the flyout on a background runspace so capturing can start immediately.
$job = Start-Job -ScriptBlock {
    param($sp2, $procId2, $id2)
    & (Join-Path $sp2 'uia.ps1') -ProcessId $procId2 -Id $id2 -Action Invoke | Out-Null
} -ArgumentList $sp, $ProcessId, $Id

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$frames = @()
$last = $null
$changes = 0
while ($sw.ElapsedMilliseconds -lt $Ms) {
    $h = [BF]::Popup($ProcessId)
    if ($h -eq [IntPtr]::Zero) { continue }
    $r = New-Object BF+RC; [void][BF]::GetWindowRect($h, [ref]$r)
    $w = $r.R - $r.L; $ht = $r.B - $r.T
    if ($w -le 0 -or $ht -le 0) { continue }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp); $hdc = $g.GetHdc()
    [void][BF]::PrintWindow($h, $hdc, 2); $g.ReleaseHdc($hdc); $g.Dispose()
    $buf = New-Object System.IO.MemoryStream
    $bmp.Save($buf, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $hash = [System.BitConverter]::ToString([System.Security.Cryptography.MD5]::Create().ComputeHash($buf.ToArray()))
    $buf.Dispose()
    if ($hash -ne $last) { $changes++; $frames += ("{0,5} ms  {1}x{2}" -f $sw.ElapsedMilliseconds, $w, $ht); $last = $hash }
    if ($Out -and $changes -eq 1) { $bmp.Save((Join-Path $PWD $Out), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
}
Receive-Job $job -Wait -AutoRemoveJob | Out-Null
"distinct frames while the flyout arrived: $changes"
$frames | Select-Object -First 14
if ($changes -le 1) { "`nNO MOVEMENT - the arrival is not animating"; exit 1 }
"`nit blooms"
