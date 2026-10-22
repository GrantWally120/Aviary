# Quik, end to end (Alpha Build 5 M7 "Super Quik", 26 Sep 2026). Scratch profiles only; no desktop input -
# DevTools reads the pages, UI Automation presses Aviary's buttons, AVIARY_QUIK_TEST=1 has Aviary write what
# its loading screen did into quik-test.txt in the profile.
#
#   quik-test.ps1 [-Port 9401]
#
# Stage 1: the Quik pane locked and unlocked, the loading screen (once, muted, Skip, no video, Reduce
# motion), the Quik animation speed, crash protection.
param([int]$Port = 9401)

$kit = $PSScriptRoot
. "$PSScriptRoot\default-exe.ps1"
Set-Location $kit
[Environment]::CurrentDirectory = $kit
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class QW {
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
}
'@

$BASE = '{"startNews":"false","listsAutoUpdate":"Off","improvementCollection":"true"'
function Launch([string]$name, [string]$seed = "", [switch]$Keep, [switch]$Record, [switch]$AsShipped) {
    if ($Record) { $env:AVIARY_QUIK_TEST = "1" }
    $line = if ($Keep) { & .\launch-quiet.ps1 -Profile $name -Port $Port }
            else { & .\launch-quiet.ps1 -Profile $name -Fresh -Port $Port -SettingsJson ($BASE + $seed + '}') -AsShipped:$AsShipped }
    $env:AVIARY_QUIK_TEST = $null
    $script:pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
    Start-Sleep 4
}
function Alive($id) { [bool](Get-Process -Id $id -ErrorAction SilentlyContinue) }
function WaitGone($id, $s = 20) { $d = (Get-Date).AddSeconds($s); while ((Get-Date) -lt $d -and (Alive $id)) { Start-Sleep -Milliseconds 400 }; -not (Alive $id) }
function StopAviary($id) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue; [void](WaitGone $id 10); Start-Sleep 2 }
function U { param($Id = "", $Name = "", $Action = "Invoke", $Value = "") & .\uia.ps1 -ProcessId $script:pidv -Id $Id -Name $Name -Action $Action -Value $Value }
function Tree { (U -Action Dump) -join "`n" }
# A person's close - WM_CLOSE, answering "Close N Tabs" if asked. A kill would count as a crash, and the
# crash protection would rightly switch Quik off.
function CloseClean {
    $p = Get-Process -Id $script:pidv -ErrorAction SilentlyContinue
    if (-not $p) { return $true }
    [void][QW]::PostMessage($p.MainWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero); Start-Sleep 1.5
    $t = Tree
    if ($t -match '(Close \d+ Tabs)') { U -Name $Matches[1] | Out-Null }
    $gone = WaitGone $script:pidv 20
    if (-not $gone) { StopAviary $script:pidv }
    Start-Sleep 1.5
    $gone
}
function Pages { node cdp.js list | Where-Object { $_ -match '^page' } }
# The page on screen. DevTools lists Quik's hidden ready tab as a page too, and "the first page" was it in two
# runs of three on 1 Oct - the probe then drove the wrong tab and measured nothing.
function FrontPage {
    foreach ($p in @(Pages)) {
        $id = $p.Substring(7, 8)
        if ("$(node cdp.js eval $id 'document.visibilityState')" -match 'visible') { return $id }
    }
    return $null
}
# The page that started first - the launch's own tab, never the ready tab made a moment later. For runs as
# shipped, where a parked window may count as covered and no page reads visible.
function Oldest { $best = $null; $bt = [double]::MaxValue; foreach ($p in @(Pages)) { $id = $p.Substring(7, 8); $t = [double]("$(node cdp.js eval $id 'performance.timeOrigin')"); if ($t -lt $bt) { $bt = $t; $best = $id } }; $best }
# ...and the page that started last: in a window with one tab, Quik's ready tab.
function Newest { $best = $null; $bt = 0; foreach ($p in @(Pages)) { $id = $p.Substring(7, 8); $t = [double]("$(node cdp.js eval $id 'performance.timeOrigin')"); if ($t -gt $bt) { $bt = $t; $best = $id } }; $best }
function OpenSettingsAt([string]$pane) {
    $script:sid = $null
    for ($try = 0; $try -lt 2 -and -not $script:sid; $try++) {
        U -Id MenuButton | Out-Null; Start-Sleep 1
        U -Name "Settings" | Out-Null
        for ($i = 0; $i -lt 16 -and -not $script:sid; $i++) {
            Start-Sleep -Milliseconds 500
            $script:sid = (@(Pages) | Where-Object { $_ -match 'Settings' } | Select-Object -First 1)
        }
    }
    if ($script:sid) { $script:sid = $script:sid.Substring(7, 8); Start-Sleep 1; node cdp.js click $script:sid "button[data-target=$pane]" | Out-Null; Start-Sleep 1 }
}
function S([string]$js) { "$(node cdp.js eval $script:sid $js)" -replace '^"|"$', '' -replace '\\"', '"' }
function Pick([string]$key, [string]$v, [string]$pane) {
    node cdp.js click $script:sid "#pane-$pane .sel[data-key=$key] .sel__btn" | Out-Null; Start-Sleep 0.6
    node cdp.js press $script:sid "#pane-$pane .sel[data-key=$key] .sel__opt[data-v='$v']" | Out-Null; Start-Sleep 0.8
}
function Setting($name, $key) { (Get-Content "profiles\$name\settings.json" -Raw -Encoding UTF8 | ConvertFrom-Json).$key }
function Record($name) { @(Get-Content "profiles\$name\quik-test.txt" -ErrorAction SilentlyContinue) }
function Marker($name) { Test-Path "profiles\$name\quik-intro-pending" }
$PANE = "(function(){var p=document.getElementById('pane-quik');return JSON.stringify({kicker:p.querySelector('.hero__kicker').textContent,on:p.querySelector('.sw[data-key=quik]').getAttribute('aria-checked'),speedLocked:p.querySelector('.sel[data-key=animationSpeed]').getAttribute('data-disabled')==='true',badge:(p.querySelector('.soon')||{}).textContent||'',lockSays:p.textContent.indexOf('These open up once')>=0})})()"
function Pane { (S $PANE) | ConvertFrom-Json }
# Window stalls the watchdog logged from half a second before the loading screen to 1.5 s after it ended.
# Before 26 Sep's fix every one ended with one: 204 ms on Grant's first try, 206 and 213 ms here.
function StallsDuringIntro($name) {
    $rec = Record $name
    $shown = ($rec | Where-Object { $_ -match 'intro shown' } | Select-Object -Last 1) -replace ' .*', ''
    $end = ($rec | Where-Object { $_ -match 'intro (ended|skipped)' } | Select-Object -Last 1) -replace ' .*', ''
    if (-not $shown -or -not $end) { return @('(no loading screen recorded)') }
    $from = [TimeSpan]::Parse($shown).Add([TimeSpan]::FromMilliseconds(-500))
    $to = [TimeSpan]::Parse($end).Add([TimeSpan]::FromMilliseconds(1500))
    @(Get-Content "profiles\$name\user.data.improvementcollection\issues.jsonl" -ErrorAction SilentlyContinue |
      Where-Object { $_ -match 'ui-stall' } |
      Where-Object { $_ -match '"t":"\d{4}-\d\d-\d\dT([\d:.]+)' -and [TimeSpan]::Parse($Matches[1]) -ge $from -and [TimeSpan]::Parse($Matches[1]) -le $to } |
      ForEach-Object { if ($_ -match '"detail":"(\d+ ms)') { $Matches[1] } else { 'a stall' } })
}
function WaitRecord($name, $pattern, $s) { $d = (Get-Date).AddSeconds($s); while ((Get-Date) -lt $d -and -not ((Record $name) -match $pattern)) { Start-Sleep -Milliseconds 400 }; @((Record $name) -match $pattern).Count -gt 0 }

try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8797/raf" -TimeoutSec 2 | Out-Null }
catch { Start-Process node -ArgumentList "expsite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }

"1. Settings -> Quik before Quik runs: its options are locked; switching it on asks for a restart"
Launch "xq1"
OpenSettingsAt "quik"
$p = Pane
Check "control: Quik off - the pane says Super Quik, the switch is off, the options are locked with Needs Quik" ($p.kicker -eq 'Super Quik' -and $p.on -eq 'false' -and $p.speedLocked -and $p.badge -eq 'Needs Quik' -and $p.lockSays) ($p | ConvertTo-Json -Compress)
node cdp.js click $script:sid ".sw[data-key=quik]" | Out-Null; Start-Sleep 1
Check "switched on: saved, the restart note shows, and the loading screen is owed" ((Setting "xq1" 'quik') -eq 'true' -and (S "!document.querySelector('.note[data-restart=quik]').hidden") -eq 'true' -and (Marker "xq1")) "saved $(Setting 'xq1' 'quik'), marker $(Marker 'xq1')"
$p = Pane
Check "but this launch is not running Quik, so the options stay locked" ($p.speedLocked -and $p.lockSays) ($p | ConvertTo-Json -Compress)
Check "(closed the way a person closes it)" (CloseClean) "still running"

"2. Restarted: Quik runs - its loading screen plays once, muted - and the options open up"
Launch "xq1" -Keep -Record
Start-Sleep 1
$peak = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
Check "the loading screen plays, muted" ((WaitRecord "xq1" 'intro shown, muted=True' 5)) ((Record "xq1") -join ' | ')
Check "and nothing comes out of the speakers while it does (peak $peak)" ($peak -lt 0.01) $peak
$ended = WaitRecord "xq1" 'intro ended after' 14
$ms = if (((Record "xq1") -join ' ') -match 'intro ended after (\d+) ms') { [int]$Matches[1] } else { -1 }
Check "it plays to its end - $ms ms of the 6 s video - and is not owed any more" ($ended -and $ms -ge 5000 -and $ms -le 10000 -and -not (Marker "xq1")) "$ms ms, marker $(Marker 'xq1')"
Start-Sleep 1.5
$st = StallsDuringIntro "xq1"
Check "and the window never stalls while it plays or as it ends" ($st.Count -eq 0) ($st -join ', ')
OpenSettingsAt "quik"
$p = Pane
Check "Quik is running: the pane says so and its options are live" ($p.kicker -eq 'Running now' -and $p.on -eq 'true' -and -not $p.speedLocked -and $p.badge -eq '' -and -not $p.lockSays) ($p | ConvertTo-Json -Compress)
[void](CloseClean)
Launch "xq1" -Keep -Record
Start-Sleep 3
Check "the next launch into Quik plays nothing - it was once" (@((Record "xq1") -match 'intro shown').Count -eq 1) ((Record "xq1") -join ' | ')

"3. The Quik animation speed: offered only in the Quik section, and only moves that fast while Quik runs"
OpenSettingsAt "look"
$opts = S "Array.prototype.map.call(document.querySelectorAll('#pane-look .sel[data-key=animationSpeed] .sel__opt'),function(o){return o.getAttribute('data-v')}).join(',')"
Check "the Appearance row offers Calm, Standard and Quick - not Quik" ($opts -eq 'Calm,Standard,Quick') $opts
node cdp.js click $script:sid "button[data-target=quik]" | Out-Null; Start-Sleep 1
Pick 'animationSpeed' 'Quik' 'quik'
$speed = S "document.documentElement.dataset.speed"
$shown = S "document.querySelector('#pane-look .sel[data-key=animationSpeed] .sel__btn span').textContent"
$note = S "(function(){var n=document.getElementById('quikspeed');return n.hidden ? '(hidden)' : n.textContent})()"
Check "chosen in the Quik section: saved, pages move at quik, the Appearance row says Quik and where it was chosen" ((Setting "xq1" 'animationSpeed') -eq 'Quik' -and $speed -eq 'quik' -and $shown -eq 'Quik' -and $note -match 'Settings . Quik\.$') "$(Setting 'xq1' 'animationSpeed') / $speed / $shown / $note"
[void](CloseClean)
Launch "xq3" ',"animationSpeed":"Quik"'
OpenSettingsAt "look"
$speed = S "document.documentElement.dataset.speed"
$note = S "(function(){var n=document.getElementById('quikspeed');return n.hidden ? '(hidden)' : n.textContent})()"
Check "control: the same choice in regular Aviary moves as Quick, and the row says so" ($speed -eq 'quick' -and $note -match 'moves as Quick') "$speed / $note"
StopAviary $script:pidv

"4. The loading screen: Skip; no video; Reduce motion - never in the way"
Launch "xq4"
OpenSettingsAt "quik"
node cdp.js click $script:sid ".sw[data-key=quik]" | Out-Null; Start-Sleep 1
[void](CloseClean)
# Straight in - no settling sleep: the video is only 6.5 s long, and finding the button takes a moment.
$env:AVIARY_QUIK_TEST = "1"
$line = & .\launch-quiet.ps1 -Profile xq4 -Port $Port
$env:AVIARY_QUIK_TEST = $null
$script:pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
[void](WaitRecord "xq4" 'intro shown' 10)
U -Name "Skip" | Out-Null
$skipped = WaitRecord "xq4" 'intro skipped after' 5
$ms = if (((Record "xq4") -join ' ') -match 'intro skipped after (\d+) ms') { [int]$Matches[1] } else { -1 }
Start-Sleep 1.5
$st = StallsDuringIntro "xq4"
Check "a Skip leaves no stall either" ($st.Count -eq 0) ($st -join ', ')
# 26 Sep: disposing the player off the window's thread while the picture still held it hung Aviary on a Skip.
Check "and Aviary still answers after a Skip" ((Get-Process -Id $script:pidv -ErrorAction SilentlyContinue).Responding -eq $true) "not responding"
Check "Skip ends it before its end ($ms ms in, of 6.5 s), and it is not owed again" ($skipped -and $ms -lt 6000 -and -not (Marker "xq4") -and (Tree) -notmatch 'Skip') "$ms ms; marker $(Marker 'xq4')"
[void](CloseClean)
$video = Join-Path (Split-Path (Get-AviaryExe $kit)) "Assets\Quik\loading.mp4"
try {
    Rename-Item $video "loading.mp4.away"
    Set-Content "profiles\xq4\quik-intro-pending" "owed"
    Launch "xq4" -Keep -Record
    Check "no video file: no overlay, nothing owed, and Aviary starts as usual" ((WaitRecord "xq4" 'intro not shown: no video' 5) -and -not (Marker "xq4") -and @(Pages).Count -ge 1) ((Record "xq4") -join ' | ')
    [void](CloseClean)
} finally { if (Test-Path "$video.away") { Rename-Item "$video.away" "loading.mp4" } }
$j = Get-Content "profiles\xq4\settings.json" -Raw | ConvertFrom-Json
$j | Add-Member -NotePropertyName reduceMotion -NotePropertyValue "true" -Force
[IO.File]::WriteAllText((Join-Path $kit "profiles\xq4\settings.json"), ($j | ConvertTo-Json -Compress))
Set-Content "profiles\xq4\quik-intro-pending" "owed"
Launch "xq4" -Keep -Record
Check "Reduce motion on: no moving picture - and nothing owed" ((WaitRecord "xq4" 'intro not shown: reduce motion' 5) -and -not (Marker "xq4")) ((Record "xq4") -join ' | ')
StopAviary $script:pidv

"5. Crash protection watches Quik, as it does experiments - and Turn All Off leaves it alone"
Launch "xq5"
OpenSettingsAt "quik"
node cdp.js click $script:sid ".sw[data-key=quik]" | Out-Null; Start-Sleep 1
StopAviary $script:pidv
Launch "xq5" -Keep
$t = Tree
Check "Aviary stopped soon after Quik was switched on: the next launch starts without it, and says so" ((Setting "xq5" 'quik') -eq 'false' -and $t -match 'Quik \(Beta\)' -and -not (Marker "xq5")) "saved $(Setting 'xq5' 'quik'); asked: $($t -match 'switched off')"
U -Name "Keep Them Off" | Out-Null; Start-Sleep 1
StopAviary $script:pidv
Launch "xq5b" ',"quik":"true"'
OpenSettingsAt "experimental"
node cdp.js click $script:sid "button[data-cmd=experimentsOff]" | Out-Null; Start-Sleep 1.5
Check "control: the Experimental pane's Turn All Off does not switch Quik off" ((Setting "xq5b" 'quik') -eq 'true') (Setting "xq5b" 'quik')
StopAviary $script:pidv

# ---------------------------------------------------------------------------------------------
# STAGE 2 - the speed, each claim against regular Aviary on the same machine.
function Perf($name, $kind) { @(Get-Content "profiles\$name\user.data.improvementcollection\perf.jsonl" -ErrorAction SilentlyContinue | Where-Object { $_ -match "`"k`":`"$kind`"" } | ForEach-Object { $_ | ConvertFrom-Json }) }
function Median($xs) { $s = @($xs | Sort-Object); if ($s.Count -eq 0) { return -1 }; $s[[int][Math]::Floor($s.Count / 2)] }
function Slept($name) { @(Select-String -Path "profiles\$name\user.data.improvementcollection\events.jsonl" -Pattern 'memory-saver-slept' -ErrorAction SilentlyContinue).Count }
function LaunchArgs([string]$name, [string]$seed, [string]$extra) {
    $line = & .\launch-quiet.ps1 -Profile $name -Fresh -Port $Port -SettingsJson ($BASE + $seed + '}') -ExtraArgs $extra
    $script:pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
}
function StartupMedian([string]$name, [string]$seed, [int]$runs) {
    Launch $name $seed; [void](CloseClean)          # the engine's first start on a new profile is not a launch anyone repeats
    for ($r = 0; $r -lt $runs; $r++) { Launch $name -Keep; [void](CloseClean) }
    Median (Perf $name 'startup' | Select-Object -Skip 1 | ForEach-Object { $_.totalMs })
}

"6. Quik starts faster: five launches each, closed like a person, the start-up record's process start to first page"
$reg = StartupMedian "xq6r" "" 5
$qk = StartupMedian "xq6q" ',"quik":"true"' 5
$pct = if ($reg -gt 0) { [Math]::Round((1 - $qk / $reg) * 100, 1) } else { 0 }
Check "Quik: median $qk ms against regular $reg ms - $pct% faster (goal: at least 20%)" ($qk -gt 0 -and $qk -le $reg * 0.8) "$qk vs $reg"
$warm = @(Perf "xq6q" 'startup' | Where-Object { $_.phases.'engine-warm' }).Count
Check "every Quik launch warmed its engine while the window was built (engine-warm before the first tab's)" ($warm -ge 5) "$warm launches"

"7. A new tab in Quik is shown, not made - control: regular Aviary makes each one"
foreach ($run in @(@('xq7r', ''), @('xq7q', ',"quik":"true"'))) {
    $on = [bool]$run[1]
    Launch $run[0] $run[1]
    Start-Sleep 2
    for ($i = 0; $i -lt 6; $i++) { U -Id NewTabButton | Out-Null; Start-Sleep 1.5 }
    $opens = Perf $run[0] 'tab-open'
    $ms = @($opens | Where-Object { $_.ready -eq $on } | ForEach-Object { $_.ms })
    $m = Median $ms; $max = ($ms | Measure-Object -Maximum).Maximum
    $title = (& .\uia.ps1 -ProcessId $script:pidv -Action Dump | Select-Object -First 1)
    if ($on) {
        Check "Quik: all six were the ready tab - median $m ms, slowest $max ms (goal: 20 ms, aiming for 10)" ($ms.Count -eq 6 -and $m -le 20 -and $max -le 40) "$($ms.Count) ready; $($ms -join ', ')"
        Check "and the strip counts seven tabs, the newest a Start Page in front" ($opens[-1].tabs -eq 7 -and (Tree) -match 'Start Page') "tabs $($opens[-1].tabs)"
    } else {
        Check "control: regular Aviary made all six - median $m ms from the request to its page on screen" ($ms.Count -eq 6 -and $m -gt 50) "$($ms.Count) made; $($ms -join ', ')"
        $script:madeMedian = $m
    }
    StopAviary $script:pidv
}

"8. The lighter launch loses nothing: a tab that held its Shields scripts back still hides ads - even on a website opened the instant it can be"
try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8771/hits" -TimeoutSec 2 | Out-Null }
catch { Start-Process node -ArgumentList "m3site.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }
$MAP = '--host-resolver-rules="MAP * 127.0.0.1:8771, EXCLUDE localhost"'
foreach ($run in @(@('xq8r', ''), @('xq8q', ',"quik":"true"'))) {
    $on = [bool]$run[1]
    LaunchArgs $run[0] ($run[1] + ',"httpsOnly":"false"') $MAP
    $t = $null
    for ($i = 0; $i -lt 60 -and -not $t; $i++) { $t = FrontPage; if (-not $t) { Start-Sleep -Milliseconds 100 } }
    node cdp.js nav $t "http://news.test/" | Out-Null
    Start-Sleep 3
    $n = @(Pages | Where-Object { $_ -match 'news\.test' })[0]
    $pr = if ($n) { (node cdp.js eval $n.Substring(7, 8) "window.__probe()") -replace '^"|"$', '' -replace '\\"', '"' | ConvertFrom-Json } else { $null }
    $els = if ($pr) { $pr.els } else { $null }
    $hidden = $els -and $els.'ad-slot' -eq 'none' -and $els.'sponsored-post' -eq 'none' -and $els.gpt -eq 'none' -and $els.content -ne 'none'
    Check "$(if ($on) { 'Quik' } else { 'control, regular' }): the ad slots are hidden and the article stays" $hidden ($els | ConvertTo-Json -Compress)
    StopAviary $script:pidv
}

"9. Keep recent tabs awake (a 'half hour' of 16 s, AVIARY_SLEEP_TEST=4) - control: regular Aviary sleeps it"
# As shipped: the keep-drawing flags change what the engine counts as on screen (launch-quiet.ps1). The ready
# tab is made again after the new tab here - before bug 97's fix that kept tab 1 awake for good (rehide-test).
foreach ($run in @(@('xq9r', ''), @('xq9q', ',"quik":"true"'))) {
    $on = [bool]$run[1]
    $env:AVIARY_SLEEP_TEST = "4"
    Launch $run[0] ($run[1] + ',"httpsOnly":"false"') -AsShipped
    $env:AVIARY_SLEEP_TEST = $null
    $t0 = Oldest
    node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 1.5
    # The engine never sleeps an error page - with the test site down, "still awake" would pass for nothing
    # (26 Sep's probes did exactly that).
    $white = "$(node cdp.js eval $t0 'document.title')" -match 'exp white'
    U -Id NewTabButton | Out-Null
    Start-Sleep 9
    $at9 = Slept $run[0]
    if ($on) {
        Check "Quik: the tab left 9 s ago - past Memory saver's 4 s - is still awake" ($white -and $at9 -eq 0) "page loaded $white; $at9 slept"
        Start-Sleep 12
        Check "and once its 16 s are up, Memory saver has it as before" ((Slept $run[0]) -ge 1) "$(Slept $run[0]) slept"
    } else {
        Check "control: regular Aviary slept it within those 9 s" ($white -and $at9 -ge 1) "page loaded $white; $at9 slept"
    }
    StopAviary $script:pidv
}

"10. Keep a new tab ready, switched off and on again - at once"
Launch "xq10" ',"quik":"true"'
Start-Sleep 2
OpenSettingsAt "quik"
node cdp.js click $script:sid ".sw[data-key=quikReadyTab]" | Out-Null; Start-Sleep 1
U -Id NewTabButton | Out-Null; Start-Sleep 1.5
$last = (Perf "xq10" 'tab-open')[-1]
Check "off: the next new tab is made the ordinary way" ((Setting "xq10" 'quikReadyTab') -eq 'false' -and $last.ready -eq $false) "saved $(Setting 'xq10' 'quikReadyTab'); ready $($last.ready)"
OpenSettingsAt "quik"
node cdp.js click $script:sid ".sw[data-key=quikReadyTab]" | Out-Null; Start-Sleep 2
U -Id NewTabButton | Out-Null; Start-Sleep 1.5
$last = (Perf "xq10" 'tab-open')[-1]
Check "on again: a moment later there is one ready, and the next new tab is it" ($last.ready -eq $true -and $last.ms -le 40) "ready $($last.ready), $($last.ms) ms"
StopAviary $script:pidv

"11. Performance shows the ready tab - its memory is what its row says it costs - and does not count it as a tab"
$PERF = "(function(){var rows=[].slice.call(document.querySelectorAll('#pf-tabs .pf-row')).filter(function(r){return r.className.indexOf('pf-head')<0});return JSON.stringify({rows:rows.map(function(r){var s=r.querySelector('.pf-n span'),m=r.querySelector('.pf-num');return {name:r.querySelector('.pf-n b').textContent,sub:s?s.textContent:'',mem:m?m.textContent:'',buttons:r.querySelectorAll('button').length}}),session:document.getElementById('pf-session').textContent})})()"
Launch "xq11" ',"quik":"true"'
Start-Sleep 2
OpenSettingsAt "perf"
Start-Sleep 4
$pf = (S $PERF) | ConvertFrom-Json
$kept = @($pf.rows | Where-Object { $_.sub -match 'Kept ready by Quik' })
Check "one row reads 'Kept ready by Quik', with its memory and no Sleep or Close" ($kept.Count -eq 1 -and $kept[0].buttons -eq 0 -and $kept[0].mem -match '\d') ($pf.rows | ConvertTo-Json -Compress)
Check "and the session line counts the two real tabs, not three" ($pf.session -match ' 2 tabs in 1 window') $pf.session
StopAviary $script:pidv

"12. A ready tab whose page stops is put away and made again - never handed over with 'This page stopped working'"
Launch "xq12" ',"quik":"true","httpsOnly":"false"' -Record
Start-Sleep 2
# Tab 1 to a website first: two Start Pages may share one page process, and only the ready tab's is to stop.
node cdp.js nav (Oldest) "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 2
$rt = Newest
$crash = Start-Process node -ArgumentList "cdp.js call $rt Page.crash" -WorkingDirectory $kit -WindowStyle Hidden -PassThru
Start-Sleep 4
if (-not $crash.HasExited) { Stop-Process -Id $crash.Id -Force -ErrorAction SilentlyContinue }
U -Id NewTabButton | Out-Null; Start-Sleep 1.5
$rec = Record "xq12"
$stopped = @($rec | Where-Object { $_ -match "ready tab's page stopped" }).Count
$made = @($rec | Where-Object { $_ -match 'ready tab made in' }).Count
$took = @($rec | Where-Object { $_ -match 'tab-open .* \(ready\)' }).Count
Check "its page stopped, it was made again, and the new tab was the fresh one" ($stopped -eq 1 -and $made -ge 2 -and $took -eq 1) "stopped $stopped, made $made, taken $took"
Check "no 'This page stopped working' anywhere in the window" ((Tree) -notmatch 'This page stopped working') "the notice is on screen"
StopAviary $script:pidv

""
"quik-test: $pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
