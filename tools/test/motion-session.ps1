# Exercise every animated path in a TEST Aviary (M4.5 "Smooth Flight"), then report what the
# watchdog and the crash recorder saw. No desktop input: UI Automation and DevTools only.
#
#   motion-session.ps1 -Profile ms-fluid -Settings '{"look":"Liquid Glass"}' [-Rounds 2]
# The profile is created fresh with improvement collection on. Prints stalls, crashes, swallowed
# errors and the minute samples for the session; closes the instance at the end.
param([string]$Profile = "ms", [string]$Settings = "{}", [int]$Rounds = 2, [int]$Port = 9333)

$kit = $PSScriptRoot; Set-Location $kit
$env:CDP_PORT = "$Port"
$seed = $Settings.TrimEnd('}').TrimEnd(); if ($seed -ne '{') { $seed += ',' }
$seed += '"improvementCollection":"true"}'
& .\launch-quiet.ps1 -Profile $Profile -Fresh -Port $Port -SettingsJson $seed | Out-Null
$procId = [int](Get-Content pid.txt)
Start-Sleep 2

function Uia([string]$Id = "", [string]$Name = "", [string]$Action = "Invoke") {
    & (Join-Path $kit "uia.ps1") -ProcessId $procId -Id $Id -Name $Name -Action $Action | Out-Null
}
function Cdp([string[]]$a) { node cdp.js @a 2>$null | Out-Null }

for ($r = 0; $r -lt $Rounds; $r++) {
    foreach ($i in 1..3) { Uia -Id NewTabButton; Start-Sleep -Milliseconds 300 }
    Cdp @('nav', '#0', 'https://example.com/'); Start-Sleep -Milliseconds 1500
    Uia -Name 'Example Domain' -Action Toggle; Start-Sleep -Milliseconds 400
    Uia -Id SidebarButton; Start-Sleep -Milliseconds 500
    foreach ($pane in 'Reading List', 'History', 'Bookmarks') { Uia -Name $pane -Action Toggle; Start-Sleep -Milliseconds 300 }
    Uia -Id SidebarButton; Start-Sleep -Milliseconds 500
    Cdp @('key', 'Example', 'ctrl+Comma'); Start-Sleep -Milliseconds 1500
    foreach ($pane in 'look', 'shields', 'privacy', 'updates', 'access') { Cdp @('click', 'Settings', "nav button[data-target=$pane]"); Start-Sleep -Milliseconds 350 }
    # Close three tabs the way a page closes itself (Ctrl+W over DevTools only reaches a window in
    # front). The tabs' own close buttons take the same CloseTab path.
    foreach ($i in 1..3) { Cdp @('eval', '#0', 'window.close()'); Start-Sleep -Milliseconds 450 }
}

# A look swap each way, through the real dropdown.
Cdp @('key', '#0', 'ctrl+Comma'); Start-Sleep -Milliseconds 1500
Cdp @('click', 'Settings', 'nav button[data-target=look]'); Start-Sleep -Milliseconds 400
foreach ($look in 'Liquid Glass', 'Classic', 'Liquid Glass', 'Classic') {
    Cdp @('press', 'Settings', '.sel[data-key=look] .sel__btn'); Start-Sleep -Milliseconds 500
    Cdp @('press', 'Settings', ".sel[data-key=look] .sel__opt[data-v='$look']"); Start-Sleep -Milliseconds 900
}

# Wait for the next minute sample, so the session's latency figures are in.
$d = Join-Path $kit "profiles\$Profile\user.data.improvementcollection"
$issues = Join-Path $d "issues.jsonl"
$before = @(Select-String -Path "$d\perf.jsonl" -Pattern '"k":"sample"').Count
for ($w = 0; $w -lt 40 -and @(Select-String -Path "$d\perf.jsonl" -Pattern '"k":"sample"').Count -le $before; $w++) { Start-Sleep 2 }

"=== $Profile  $Settings ==="
"minute samples:"
Select-String -Path "$d\perf.jsonl" -Pattern '"k":"sample"' | ForEach-Object { if ($_.Line -match '"ui":(\{[^}]*\})') { "  " + $Matches[1] } }
"stalls over 50 ms:"
if (Test-Path $issues) { Select-String -Path $issues -Pattern '"ui-stall"' | ForEach-Object { "  " + $_.Line.Substring(0, [Math]::Min(260, $_.Line.Length)) } } else { "  (no issues.jsonl - nothing went wrong)" }
"crashes:"
Get-ChildItem "$d\crashes" -File | ForEach-Object { "  " + $_.Name + "  " + ((Get-Content $_.FullName -Raw) -replace '(?s).*?"message":"([^"]*)".*', '$1') }
"swallowed errors:"
if (Test-Path $issues) { Select-String -Path $issues -Pattern '"k":"swallowed"' | ForEach-Object { if ($_.Line -match '"name":"([^"]*)".*?"message":"([^"]*)"') { "  " + $Matches[1] + " - " + $Matches[2] } } }
& (Join-Path $kit "peek.ps1") -ProcessId $procId -Out (Join-Path $kit "shots/$Profile-end.png") -Crop "0,0,1280,140" | Out-Null
"tabs at the end: " + ((node cdp.js list 2>$null) -join ' | ')

$p = Get-Process -Id $procId -ErrorAction SilentlyContinue
for ($n = 0; $p -and -not $p.HasExited -and $n -lt 6; $n++) { [void]$p.CloseMainWindow(); Start-Sleep 2 }
if ($p -and -not $p.HasExited) { Stop-Process -Id $procId -Force; "(had to force-close)" }
