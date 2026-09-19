# Page full screen on and off N times in a TEST Aviary, then print the stalls and any probe lines
# (M4.5 stage 4 - leaving full screen stalled the UI thread 62-207 ms). No input: the page asks
# for full screen itself (DevTools evaluates with a user gesture).
#   fullscreen-bench.ps1 -Profile fsb [-Look "Liquid Glass"] [-Times 5] [-Size 2540x1380]
param([string]$Profile = "fsb", [string]$Look = "Liquid Glass", [int]$Times = 5, [string]$Size = "2540x1380", [int]$Port = 9333)

$kit = $PSScriptRoot; Set-Location $kit; $env:CDP_PORT = "$Port"
& .\launch-quiet.ps1 -Profile $Profile -Fresh -Port $Port -Size $Size -SettingsJson ('{"improvementCollection":"true","look":"' + $Look + '"}') | Out-Null
$procId = [int](Get-Content pid.txt); Start-Sleep 3
node cdp.js nav "#0" "https://example.com/" | Out-Null; Start-Sleep 2
for ($i = 0; $i -lt $Times; $i++) {
    node cdp.js eval "#0" "document.documentElement.requestFullscreen().then(()=>'on',e=>'refused')" | Out-Null; Start-Sleep -Milliseconds 1400
    node cdp.js eval "#0" "document.exitFullscreen().then(()=>'off',e=>'refused')" | Out-Null; Start-Sleep -Milliseconds 1400
}
$d = "profiles\$Profile\user.data.improvementcollection"
"=== $Look, $Times times, $Size ==="
if (Test-Path "$d\issues.jsonl") {
    Get-Content "$d\issues.jsonl" | Select-String "ui-stall" | ForEach-Object { if ($_.Line -match '"ms":([0-9.]+),"cause":"([^"]*)","uiThreadCpuMs":([0-9.]+)') { "  stall {0} ms  {1}  ui-cpu {2} ms" -f $Matches[1], $Matches[2], $Matches[3] } }
} else { "  no stalls over 50 ms" }
Get-Content "$d\perf.jsonl" | Select-String "probe" | ForEach-Object { $_.Line -replace '^.*?"k":"([^"]*)",?', '  $1 ' }
$p = Get-Process -Id $procId -ErrorAction SilentlyContinue
for ($n = 0; $p -and -not $p.HasExited -and $n -lt 6; $n++) { [void]$p.CloseMainWindow(); Start-Sleep 2 }
if ($p -and -not $p.HasExited) { Stop-Process -Id $procId -Force }
