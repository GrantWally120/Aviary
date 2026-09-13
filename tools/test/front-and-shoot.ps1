# Bring one window to the front, PROVE it is in front, then capture it.
#
# The proof matters: an occluded capture returns whatever is on top, which on this machine
# is the user's own browser. If fronting fails this aborts rather than saving that.
param([int]$ProcessId, [string]$Out)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Fg {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr pid);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [StructLayout(LayoutKind.Sequential)] public struct RC { public int Left, Top, Right, Bottom; }
}
"@

$p = Get-Process -Id $ProcessId -ErrorAction Stop
$h = $p.MainWindowHandle
if ($h -eq 0) { Write-Output "NO WINDOW"; exit 1 }

# Windows refuses SetForegroundWindow from a process that does not own the foreground.
# Attaching to the foreground window's input queue is the documented way round it.
$fg = [Fg]::GetForegroundWindow()
$them = [Fg]::GetWindowThreadProcessId($fg, [IntPtr]::Zero)
$me = [Fg]::GetCurrentThreadId()
[void][Fg]::AttachThreadInput($them, $me, $true)
[void][Fg]::ShowWindow($h, 3)
[void][Fg]::BringWindowToTop($h)
[void][Fg]::SetForegroundWindow($h)
[void][Fg]::AttachThreadInput($them, $me, $false)
Start-Sleep -Milliseconds 900

if ([Fg]::GetForegroundWindow() -ne $h) {
    Write-Output "ABORT: could not bring Aviary to the front; refusing to capture the screen"
    exit 2
}

$r = New-Object Fg+RC
[void][Fg]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left; $ht = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "FOREGROUND CONFIRMED  rect=$($r.Left),$($r.Top) ${w}x${ht}"
Write-Output "SAVED $Out"
