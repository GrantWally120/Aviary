# Bug 97, end to end (Alpha Build 5 M7 stage 2, 1 Oct 2026): making a tab's engine turned every tab already
# behind back to "visible" in the engine's eyes - unthrottled, and refused by Memory saver - until each was
# visited again. Aviary now tells the engine again that they are out of sight, one tab per idle moment
# (MainWindow.MemorySaver.cs). Every check has its control in the SAME build: AVIARY_REHIDE_TEST=off leaves
# the fix out; =noqueue keeps only Memory saver's ask-again, to prove that on its own.
#
# The engine as shipped (launch-quiet -AsShipped): the keep-drawing flags exist to change what the engine
# counts as on screen. So tabs are found by when their page started, never by "the visible one".
#
#   rehide-test.ps1 [-Port 9401]
param([int]$Port = 9401)

$kit = $PSScriptRoot
Set-Location $kit
[Environment]::CurrentDirectory = $kit
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}

try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8797/white" -TimeoutSec 2 | Out-Null }
catch { Start-Process node -ArgumentList "expsite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }

$BASE = '{"startNews":"false","listsAutoUpdate":"Off","improvementCollection":"true","httpsOnly":"false"'
function Launch([string]$name, [string]$seed = "", [string]$rehide = "", [string]$sleep = "4") {
    $env:AVIARY_SLEEP_TEST = $sleep; $env:AVIARY_QUIK_TEST = "1"; $env:AVIARY_REHIDE_TEST = $rehide
    $line = & .\launch-quiet.ps1 -Profile $name -Fresh -Port $Port -SettingsJson ($BASE + $seed + '}') -AsShipped
    $env:AVIARY_SLEEP_TEST = $null; $env:AVIARY_QUIK_TEST = $null; $env:AVIARY_REHIDE_TEST = $null
    $script:pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
    Start-Sleep 4
}
function StopAviary { Stop-Process -Id $script:pidv -Force -ErrorAction SilentlyContinue; Start-Sleep 2 }
function U { param($Id = "", $Name = "", $Action = "Invoke") & .\uia.ps1 -ProcessId $script:pidv -Id $Id -Name $Name -Action $Action | Out-Null }
function Ids { @(node cdp.js list | Where-Object { $_ -match '^page' } | ForEach-Object { $_.Substring(7, 8) }) }
# The page that started first: the launch's own tab, never the ready tab Quik makes a moment later.
function Oldest { $best = $null; $bt = [double]::MaxValue; foreach ($id in Ids) { $t = [double]("$(node cdp.js eval $id 'performance.timeOrigin')"); if ($t -lt $bt) { $bt = $t; $best = $id } }; $best }
function State($id) { "$(node cdp.js eval $id 'document.visibilityState')" -replace '"', '' }
function Nav($id, $path) { node cdp.js nav $id "http://127.0.0.1:8797$path" | Out-Null; Start-Sleep 1.5 }
function Slept($name) { @(Get-Content "profiles\$name\user.data.improvementcollection\events.jsonl" -ErrorAction SilentlyContinue | Where-Object { $_ -match 'memory-saver-slept' } | ForEach-Object { if ($_ -match '"tab":(\d+)') { [int]$Matches[1] } }) }
function Record($name) { @(Get-Content "profiles\$name\quik-test.txt" -ErrorAction SilentlyContinue) }
function OpenSettingsAt([string]$pane) {
    $script:sid = $null
    for ($try = 0; $try -lt 2 -and -not $script:sid; $try++) {
        U -Id MenuButton; Start-Sleep 1
        U -Name "Settings"
        for ($i = 0; $i -lt 16 -and -not $script:sid; $i++) {
            Start-Sleep -Milliseconds 500
            $script:sid = (@(node cdp.js list | Where-Object { $_ -match '^page' -and $_ -match 'Settings' }) | Select-Object -First 1)
        }
    }
    if ($script:sid) { $script:sid = $script:sid.Substring(7, 8); Start-Sleep 1; node cdp.js click $script:sid "button[data-target=$pane]" | Out-Null; Start-Sleep 1 }
}

"1. Three tabs, each made in front: the first, behind for longest, reads hidden after the third is made - and sleeps"
foreach ($run in @(@('xr1', ''), @('xr1c', 'off'))) {
    $fixed = $run[1] -eq ''
    Launch $run[0] -rehide $run[1]
    $t1 = Oldest; Nav $t1 "/white"
    U -Id NewTabButton; Start-Sleep 1.5
    $t2 = @(Ids | Where-Object { $_ -ne $t1 })[0]; Nav $t2 "/long"
    U -Id NewTabButton; Start-Sleep 3
    $s1 = State $t1
    Start-Sleep 16
    $slept = Slept $run[0]
    if ($fixed) {
        Check "tab 1 reads hidden, and Memory saver slept tabs 1 and 2" ($s1 -eq 'hidden' -and $slept -contains 1 -and $slept -contains 2) "tab 1 $s1; slept $($slept -join ',')"
    } else {
        Check "control, the fix left out: tab 1 reads visible, and only tab 2 slept" ($s1 -eq 'visible' -and $slept -notcontains 1 -and $slept -contains 2) "tab 1 $s1; slept $($slept -join ',')"
    }
    StopAviary
}

"2. The re-hide on a refusal, on its own (=noqueue): Memory saver, refused, puts the tab out of sight again - the next check sleeps it"
Launch 'xr2' -rehide 'noqueue'
$t1 = Oldest; Nav $t1 "/white"
U -Id NewTabButton; Start-Sleep 1.5
$t2 = @(Ids | Where-Object { $_ -ne $t1 })[0]; Nav $t2 "/long"
U -Id NewTabButton; Start-Sleep 16
$again = @(Record 'xr2' | Where-Object { $_ -match 're-hidden after a refusal: tab 1' }).Count
Check "refused once, re-hidden, then slept" ((Slept 'xr2') -contains 1 -and $again -eq 1) "slept $((Slept 'xr2') -join ','); re-hidden after a refusal $again time(s)"
StopAviary

"2b. ...and Performance's Sleep button (=noqueue, Memory saver off): refused, it re-hides, waits a moment and asks again"
Launch 'xr2b' ',"memorySaver":"Off"' -rehide 'noqueue' -sleep ''
$t1 = Oldest; Nav $t1 "/white"
U -Id NewTabButton; Start-Sleep 1.5
$t2 = @(Ids | Where-Object { $_ -ne $t1 })[0]; Nav $t2 "/long"
OpenSettingsAt "perf"; Start-Sleep 3          # Settings is a new engine too: tab 1 now counts as on screen
$s1 = State $t1
node cdp.js click $script:sid "button[data-perf-tab=sleep\:1]" | Out-Null; Start-Sleep 1.5
$pressed = @(Get-Content "profiles\xr2b\user.data.improvementcollection\events.jsonl" -ErrorAction SilentlyContinue | Where-Object { $_ -match '"perf-sleep-tab","tab":1' })
Check "tab 1 read visible, and Sleep put it to sleep" ($s1 -eq 'visible' -and $pressed.Count -eq 1 -and $pressed[0] -match '"slept":true') "tab 1 $s1; pressed: $($pressed -join ' ')"
StopAviary

"3. A link opened BEHIND (Ctrl+click) after switching: the tab behind it reads hidden and sleeps"
Launch 'xr3'
$t1 = Oldest; Nav $t1 "/white"
U -Id NewTabButton; Start-Sleep 1.5
$t2 = @(Ids | Where-Object { $_ -ne $t1 })[0]; Nav $t2 "/hover"
node cdp.js mclick $t2 "#same" "ctrl" | Out-Null; Start-Sleep 3
$s1 = State $t1
Start-Sleep 14
Check "tab 1 reads hidden after the link's tab is made, and slept" ($s1 -eq 'hidden' -and (Slept 'xr3') -contains 1) "tab 1 $s1; slept $((Slept 'xr3') -join ',')"
StopAviary

"4. Quik: the ready tab made again after a new tab no longer keeps the tab behind awake"
foreach ($run in @(@('xr4', ''), @('xr4c', 'off'))) {
    $fixed = $run[1] -eq ''
    Launch $run[0] ',"quik":"true","quikKeepAwake":"false"' -rehide $run[1]
    $t1 = Oldest; Nav $t1 "/white"
    U -Id NewTabButton; Start-Sleep 3
    $s1 = State $t1
    Start-Sleep 10
    $ready = @(Record $run[0] | Where-Object { $_ -match 'tab-open .* \(ready\)' }).Count
    $slept = Slept $run[0]
    if ($fixed) {
        Check "the new tab was the ready tab; tab 1 reads hidden and slept" ($ready -ge 1 -and $s1 -eq 'hidden' -and $slept -contains 1) "ready $ready; tab 1 $s1; slept $($slept -join ',')"
    } else {
        Check "control, the fix left out: tab 1 reads visible and never sleeps" ($ready -ge 1 -and $s1 -eq 'visible' -and $slept -notcontains 1) "ready $ready; tab 1 $s1; slept $($slept -join ',')"
    }
    StopAviary
}

"5. A tab already ASLEEP stays asleep when a new tab is made - the engine wakes a sleeping tab that is shown"
Launch 'xr5'
$t1 = Oldest; Nav $t1 "/white"
U -Id NewTabButton; Start-Sleep 9
$before = @(Slept 'xr5' | Where-Object { $_ -eq 1 }).Count
U -Id NewTabButton; Start-Sleep 8
$after = @(Slept 'xr5' | Where-Object { $_ -eq 1 }).Count
$shown = @(Record 'xr5' | Where-Object { $_ -match 'rehide tab 1 ' }).Count
Check "tab 1 slept once and was never re-hidden after (a woken tab would have slept a second time)" ($before -eq 1 -and $after -eq 1 -and $shown -le 1) "slept before $before, after $after; tab 1 re-hidden $shown time(s)"
StopAviary

"6. What it costs: twelve tabs behind, each a page, then one more - one tab per idle moment, never a block"
Launch 'xr6' ',"memorySaver":"Off"' -sleep ''
$known = @(Oldest); Nav $known[0] "/long"
for ($i = 0; $i -lt 12; $i++) {
    U -Id NewTabButton; Start-Sleep 1.2
    $n = @(Ids | Where-Object { $known -notcontains $_ })[0]
    if ($n) { $known += $n; Nav $n "/long" }
}
U -Id NewTabButton; Start-Sleep 3
$ms = @(Record 'xr6' | Where-Object { $_ -match 'rehide tab \d+ in ([\d.]+) ms' } | ForEach-Object { [double]($_ -replace '^.* in ([\d.]+) ms$', '$1') })
$max = ($ms | Measure-Object -Maximum).Maximum
$visible = @($known | Where-Object { (State $_) -eq 'visible' }).Count
$stalls = @(Get-Content "profiles\xr6\user.data.improvementcollection\issues.jsonl" -ErrorAction SilentlyContinue | Where-Object { $_ -match 'ui-stall' }).Count
Check "every one of the $($known.Count) tabs behind reads hidden" ($visible -eq 0 -and $known.Count -ge 13) "$visible visible of $($known.Count)"
Check "the slowest single re-hide $max ms (one tab, one idle moment; goal: under 15), no window stall" ($ms.Count -gt 0 -and $max -lt 15 -and $stalls -eq 0) "$($ms.Count) re-hides, slowest $max ms, $stalls stalls"
# Never while the engine is still starting a new tab (on 1 Oct one took 69 ms then, and the new tab came 70 ms later): every re-hide
# comes well after the newest tab before it was on screen.
$lastOpen = $null; $early = 0; $gaps = @()
foreach ($line in (Record 'xr6')) {
    if ($line -notmatch '^(\d\d:\d\d:\d\d\.\d+) (.*)$') { continue }
    $at = [TimeSpan]::Parse($Matches[1]); $what = $Matches[2]
    if ($what -match '^tab-open') { $lastOpen = $at }
    elseif ($what -match '^rehide' -and $lastOpen) { $gap = ($at - $lastOpen).TotalMilliseconds; $gaps += $gap; if ($gap -lt 500) { $early++ } }
}
$minGap = ($gaps | Measure-Object -Minimum).Minimum
Check "no re-hide within half a second of a new tab appearing (closest: $([Math]::Round($minGap)) ms)" ($gaps.Count -gt 0 -and $early -eq 0) "$early early of $($gaps.Count)"
StopAviary

""
"rehide-test: $pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
