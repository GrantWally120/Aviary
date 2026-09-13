# Launch Aviary, wait for its window, maximise it, and screenshot just that window.
#
# HANDOFF section 5 rules, all of them:
#   - never send input without first confirming Aviary is the FOREGROUND window
#     (keystrokes leaked into Grant's chat window once and posted two messages)
#   - SetCursorPos does not drive WinUI's pointer pipeline for DRAGS; mouse_event does
#   - if fronting fails, abort rather than click blind
param(
    [string]$Exe,
    [string]$Out,
    [int]$SettleMs = 5000
)

Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$p = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Milliseconds $SettleMs

for ($i = 0; $i -lt 25 -and $p.MainWindowHandle -eq 0; $i++) {
    Start-Sleep -Milliseconds 400
    $p.Refresh()
}

if ($p.HasExited) {
    Write-Output "EXITED early with code $($p.ExitCode)"
    $log = Join-Path $env:LOCALAPPDATA "Aviary\crash.log"
    if (Test-Path $log) { Write-Output "--- crash.log ---"; Get-Content $log -Tail 40 }
    exit 1
}

$h = $p.MainWindowHandle
if ($h -eq 0) { Write-Output "NO WINDOW after 10s"; exit 1 }

[void][Win]::ShowWindow($h, 3)          # maximise, so clicks cannot land elsewhere
[void][Win]::SetForegroundWindow($h)
Start-Sleep -Milliseconds 1200

if ([Win]::GetForegroundWindow() -ne $h) {
    Write-Output "NOT FOREGROUND - refusing to send input, screenshot only"
}

$r = New-Object Win+RECT
[void][Win]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L
$ht = $r.B - $r.T
Write-Output "PID=$($p.Id) HWND=$h RECT=$($r.L),$($r.T) ${w}x${ht}"

$bmp = New-Object System.Drawing.Bitmap $w, $ht
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()
Write-Output "SAVED $Out"
Write-Output "PID $($p.Id)"
