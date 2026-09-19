# Settings -> Performance against Windows' own counters (M4.5): the page's per-process memory, GPU
# memory and GPU busy figures beside what Get-Counter reads for the same process ids at the same
# moment. The Performance pane must be open in the test instance's Settings tab (cdp.js on CDP_PORT).
#   perf-compare.ps1 [-Port 9333]
param([int]$Port = 9333)
$kit = $PSScriptRoot; Set-Location $kit; $env:CDP_PORT = "$Port"

$json = node cdp.js eval Settings "JSON.stringify([...document.querySelectorAll('#pf-procs .pf-row:not(.pf-head)')].map(function(r){ var t = r.innerText.split('\n'); return t; }))" | ConvertFrom-Json | ConvertFrom-Json
$page = @{}
foreach ($row in $json) {
    $text = $row -join ' | '
    if ($text -match 'Process (\d+)') {
        $procId = [int]$Matches[1]
        $mem = if ($text -match '\| ([0-9.]+) (MB|GB) \|') { [double]$Matches[1] * $(if ($Matches[2] -eq 'GB') { 1024 } else { 1 }) } else { $null }
        $gmem = if ($text -match '([0-9.]+) (MB|GB) of graphics memory') { [double]$Matches[1] * $(if ($Matches[2] -eq 'GB') { 1024 } else { 1 }) } else { 0 }
        $page[$procId] = @{ kind = $row[0]; mem = $mem; gmem = $gmem }
    }
}

$c = Get-Counter -Counter '\Process(*)\ID Process', '\Process(*)\Working Set - Private', '\GPU Process Memory(*)\Dedicated Usage' -ErrorAction SilentlyContinue
$idByInstance = @{}; $wsByInstance = @{}; $gmemByPid = @{}
foreach ($s in $c.CounterSamples) {
    # InstanceName drops the #n that tells same-named processes apart; the path keeps it.
    $inst = if ($s.Path -match '\(([^)]*)\)\\[^\\]+$') { $Matches[1] } else { $s.InstanceName }
    if ($s.Path -like '*\id process') { $idByInstance[$inst] = [int]$s.CookedValue }
    elseif ($s.Path -like '*\working set - private') { $wsByInstance[$inst] = $s.CookedValue }
    elseif ($s.Path -like '*\dedicated usage' -and $inst -match '^pid_(\d+)_') { $p = [int]$Matches[1]; $gmemByPid[$p] = ($gmemByPid[$p] + $s.CookedValue) }
}
$wsByPid = @{}
foreach ($inst in $idByInstance.Keys) { if ($wsByInstance.ContainsKey($inst)) { $wsByPid[$idByInstance[$inst]] = $wsByInstance[$inst] } }

"{0,-28} {1,7}  {2,10} {3,10} {4,7}   {5,10} {6,10}" -f 'process', 'pid', 'page MB', 'Windows MB', 'diff', 'page GPU', 'Windows'
$worst = 0
foreach ($procId in $page.Keys | Sort-Object) {
    $p = $page[$procId]
    $w = if ($wsByPid.ContainsKey($procId)) { [math]::Round($wsByPid[$procId] / 1MB, 1) } else { $null }
    $diff = if ($w -and $p.mem) { [math]::Round(100 * ($p.mem - $w) / $w, 1) } else { $null }
    if ($diff -ne $null -and [math]::Abs($diff) -gt $worst -and [math]::Abs($p.mem - $w) -gt 1) { $worst = [math]::Abs($diff) }
    $gw = if ($gmemByPid.ContainsKey($procId)) { [math]::Round($gmemByPid[$procId] / 1MB, 1) } else { 0 }
    "{0,-28} {1,7}  {2,10} {3,10} {4,6}%   {5,10} {6,10}" -f $p.kind, $procId, $p.mem, $w, $diff, $p.gmem, $gw
}
"largest memory difference beyond 1 MB: $worst%"
