# Bug 65 (a tab's panel belongs to that tab) and the crashed-page panel (HANDOFF §8), end to end in
# a test Aviary: a failed page in tab 1, a new tab 2, back and forth, tab 2's renderer crashed on
# screen and in the background (DevTools Page.crash), Reload Page. DevTools + UI Automation +
# PrintWindow only: no mouse, no keyboard, the window parked at the back. Captures go to
# shots/b65-<Label>-<step>.png.
param([string]$Label = "after")

$t = $PSScriptRoot
Set-Location $t
& "$t\launch-m2.ps1" -Profile b65 -Fresh -SettingsJson '{"httpsOnly":"false","improvementCollection":"true"}' | Out-Null
if ($LASTEXITCODE -eq 3) { "guard refused"; exit 3 }
$testPid = [int](Get-Content "$t\pid.txt")

Add-Type @'
using System; using System.Runtime.InteropServices;
public class NtPark { public delegate bool CB(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
 [DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h); }
'@ -ErrorAction SilentlyContinue
[NtPark]::EnumWindows({ param($h, $l) $wp = 0; [void][NtPark]::GetWindowThreadProcessId($h, [ref]$wp); if ($wp -eq $testPid -and [NtPark]::IsWindowVisible($h)) { [void][NtPark]::SetWindowPos($h, [IntPtr]1, 0, 0, 0, 0, 0x0013) }; return $true }, [IntPtr]::Zero) | Out-Null

function Panel {
  $line = & "$t\uia.ps1" -ProcessId $testPid -Action Dump | Select-String "ErrorTitle" | Select-Object -First 1
  if ($line) { ($line.Line -replace '^\S+\s+ErrorTitle\s+', '').Trim() } else { "(no panel)" }
}
function Title { (Get-Process -Id $testPid).MainWindowTitle }
function Shot([string]$n) { & "$t\peek.ps1" -ProcessId $testPid -Out "$t\shots\b65-$Label-$n.png" | Out-Null }
function Step([string]$what) { "{0,-44} panel: {1,-28} window: {2}" -f $what, (Panel), (Title) }
function Crash([string]$match) {
  $job = Start-Job { param($d, $m) Set-Location $d; node cdp.js call $m Page.crash } -ArgumentList $t, $match
  [void](Wait-Job $job -Timeout 5); Remove-Job $job -Force
}

node cdp.js nav "#0" "http://127.0.0.1:1/" | Out-Null; Start-Sleep 3
Step "1 tab 1's page fails"; Shot 1
& "$t\uia.ps1" -ProcessId $testPid -Id NewTabButton | Out-Null; Start-Sleep 2
Step "2 new tab 2 (Start Page)"; Shot 2
& "$t\uia.ps1" -ProcessId $testPid -Name "This page didn't load" -Action Toggle | Out-Null; Start-Sleep 2
Step "3 back to tab 1"; Shot 3
& "$t\uia.ps1" -ProcessId $testPid -Name "Start Page" -Action Toggle | Out-Null; Start-Sleep 2
Step "4 over to tab 2"; Shot 4
Crash "about:blank"; Start-Sleep 3
Step "5 tab 2's renderer crashes, on screen"; Shot 5
& "$t\uia.ps1" -ProcessId $testPid -Name "Reload Page" | Out-Null; Start-Sleep 3
Step "6 Reload Page"; Shot 6
& "$t\uia.ps1" -ProcessId $testPid -Name "This page didn't load" -Action Toggle | Out-Null; Start-Sleep 2
Crash "about:blank"; Start-Sleep 3
Step "7 tab 2 crashes while tab 1 is on screen"; Shot 7
& "$t\uia.ps1" -ProcessId $testPid -Name "This page stopped working" -Action Toggle | Out-Null; Start-Sleep 2
Step "8 over to tab 2"; Shot 8
"alive: " + (-not (Get-Process -Id $testPid -ErrorAction SilentlyContinue).HasExited)
node cdp.js list
