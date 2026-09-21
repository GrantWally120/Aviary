# Aggressive lets go as soon as the window is minimised (M4.9).
#
# Deliberately WITHOUT AVIARY_SLEEP_TEST, so the idle timer is the real five minutes and cannot
# be what sleeps the tab: anything that sleeps inside this run came from the minimise, not the
# clock. Minimising is a window-state change (ShowWindow), not desktop input, and it only ever
# touches the test window - guard.ps1 is not needed.
#
#   minimise-sleep.ps1 [-Mode Aggressive] [-Port 9410]
param([string]$Mode = "Aggressive", [int]$Port = 9410, [string]$Profile = "minsleep")

$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"

& .\launch-quiet.ps1 -Profile $Profile -Fresh -Port $Port -Size 1280x860 `
    -SettingsJson ('{"improvementCollection":"true","memorySaver":"' + $Mode + '"}') | Out-Null
$procId = [int](Get-Content pid.txt)
Start-Sleep -Seconds 3

node cdp.js nav "#0" "https://example.com/" | Out-Null
Start-Sleep -Seconds 3
& .\uia.ps1 -ProcessId $procId -Id NewTabButton -Action Invoke | Out-Null
Start-Sleep -Seconds 2
node cdp.js nav "Aviary" "https://example.org/" | Out-Null
Start-Sleep -Seconds 4

$ev = Join-Path $kit "profiles\$Profile\user.data.improvementcollection\events.jsonl"
function Slept { if (Test-Path $ev) { @(Get-Content $ev | Select-String "memory-saver-slept").Count } else { 0 } }
"before minimising: slept=$(Slept)  (the idle timer is a real 5 minutes, so this should be 0)"

Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class Mini {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public static IntPtr Win(int pid2) { IntPtr f = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid2 && IsWindowVisible(h)) { var c = new StringBuilder(256); GetClassName(h, c, 256);
        if (c.ToString().Contains("WinUIDesktopWin32WindowClass")) f = h; } return true; }, IntPtr.Zero);
    return f; }
}
'@ -ErrorAction SilentlyContinue

$h = [Mini]::Win($procId)
[void][Mini]::ShowWindow($h, 6)     # SW_MINIMIZE
Start-Sleep -Seconds 5
"after minimising:  slept=$(Slept)"
[void][Mini]::ShowWindow($h, 4)     # SW_SHOWNOACTIVATE - put it back
Start-Sleep -Seconds 2

if (Test-Path $ev) { Get-Content $ev | Select-String "memory-saver-slept" | ForEach-Object { "  $_" } }
Get-Process -Id $procId -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
