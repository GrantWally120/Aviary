# The Experimental menu, end to end (Alpha Build 5 M6 stages 1-3; 15 is bug 91, 16 bug 92, 17-25 stage 3). No desktop input unless -Restart:
# DevTools reads the pages and the engine's own behaviour, UI Automation presses Aviary's buttons and
# dialogs, and the engine's command line is read from Windows (WMI) - the one place that shows what
# the engine was REALLY started with.
#
#   experiments-test.ps1 [-Port 9401] [-Restart]
#
# -Restart also presses "Restart Aviary": the new Aviary opens a window of its own, which may come to
# the front, so guard.ps1 decides whether that part runs.
param([int]$Port = 9401, [switch]$Restart)

$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}
try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8797/raf" -TimeoutSec 2 | Out-Null }
catch { Start-Process node -ArgumentList "expsite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class XW {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RC r);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    public struct RC { public int L, T, R, B; }
    public static IntPtr Biggest(int id) {
        IntPtr best = IntPtr.Zero; long area = -1;
        EnumWindows(delegate (IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
            if (p == id && IsWindowVisible(h)) { RC r; GetWindowRect(h, out r); long a = (long)(r.R - r.L) * (r.B - r.T); if (a > area) { area = a; best = h; } }
            return true; }, IntPtr.Zero);
        return best;
    }
}
'@

$BASE = '{"startNews":"false","listsAutoUpdate":"Off","improvementCollection":"true"'
$ALLON = ',"exp.smoothScrollOff":"true","exp.darkWebsites":"true","exp.noVideoDecode":"true"'

function Launch([string]$name, [string]$seed = "", [switch]$Keep, [string]$crash = "") {
    if ($crash) { $env:AVIARY_CRASH_TEST = $crash }
    $line = if ($Keep) { & .\launch-quiet.ps1 -Profile $name -Port $Port }
            else { & .\launch-quiet.ps1 -Profile $name -Fresh -Port $Port -SettingsJson ($BASE + $seed + '}') }
    $env:AVIARY_CRASH_TEST = $null
    $script:pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
    Start-Sleep 4
    $script:pidv
}
function Alive($id) { [bool](Get-Process -Id $id -ErrorAction SilentlyContinue) }
function WaitGone($id, $s = 20) { $d = (Get-Date).AddSeconds($s); while ((Get-Date) -lt $d -and (Alive $id)) { Start-Sleep -Milliseconds 400 }; -not (Alive $id) }
# NOT "Kill": kill is an alias of Stop-Process, and an alias wins over a function - so "Kill $pid" was Stop-Process
# all along, without the wait for the process to go (found 25 Sep).
function StopAviary($id) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue; [void](WaitGone $id 10); Start-Sleep 2 }
function U { param($Id = "", $Name = "", $Action = "Invoke", $Value = "") & .\uia.ps1 -ProcessId $script:pidv -Id $Id -Name $Name -Action $Action -Value $Value }
function Tree { (U -Action Dump) -join "`n" }
# The engine as Windows started it: the msedgewebview2.exe under Aviary with no --type=.
function Engine { $k = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe' AND ParentProcessId=$script:pidv"
                  ($k | Where-Object { $_.CommandLine -notmatch '--type=' } | Select-Object -First 1).CommandLine }
function Setting($name, $key) { (Get-Content "profiles\$name\settings.json" -Raw -Encoding UTF8 | ConvertFrom-Json).$key }
function Marker($name) { Get-Content "profiles\$name\experiments-safety.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json }
function Pages { node cdp.js list | Where-Object { $_ -match '^page' } }
# A person's close: WM_CLOSE to the real window, answering "Close N Tabs" if it asks.
function CloseClean {
    [void][XW]::PostMessage([XW]::Biggest($script:pidv), 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
    Start-Sleep 1.5
    $t = Tree
    if ($t -match '(Close \d+ Tabs)') { U -Name $Matches[1] | Out-Null }
    $gone = WaitGone $script:pidv 20
    if (-not $gone) { StopAviary $script:pidv }
    Start-Sleep 1
    $gone
}
function Probe { $raw = node switch-probe.js all; try { $raw | ConvertFrom-Json } catch { $null } }
function Luminance($b64) {
    if (-not $b64) { return -1 }
    $bmp = [System.Drawing.Bitmap]::new([System.IO.MemoryStream]::new([Convert]::FromBase64String($b64)))
    $c = $bmp.GetPixel(1, 1); $bmp.Dispose(); [int](($c.R + $c.G + $c.B) / 3)
}
function Gpc { node cdp.js eval "#0" "navigator.globalPrivacyControl === true" }
function OpenSettingsAt([string]$pane) {
    U -Id MenuButton | Out-Null; Start-Sleep 1
    U -Name "Settings" | Out-Null; Start-Sleep 2.5
    $script:sid = (@(Pages) | Where-Object { $_ -match 'Settings' } | Select-Object -First 1)
    if ($script:sid) { $script:sid = $script:sid.Substring(7, 8); node cdp.js click $script:sid "button[data-target=$pane]" | Out-Null; Start-Sleep 1 }
}
function S([string]$js) { node cdp.js eval $script:sid $js }
# A DevTools screenshot of a tab that is not being drawn (one in the background) waits for ever
# (HANDOFF 5) - so every shot gets 15 s and is abandoned after that.
function Shot([string]$tab, [string]$out) {
    $p = Start-Process node -ArgumentList "cdp.js", "shot", $tab, $out, "1180x860" -WorkingDirectory $kit -NoNewWindow -PassThru
    if (-not $p.WaitForExit(15000)) { $p.Kill(); return $false }
    Test-Path (Join-Path $kit $out)
}
function Pixel([string]$out, [int]$x, [int]$y) {
    $bmp = [System.Drawing.Bitmap]::FromFile((Join-Path $kit $out)); $px = $bmp.GetPixel($x, $y); $bmp.Dispose(); $px
}

# ---------------------------------------------------------------------------------------------
"1. the pane: the promise, the labels, fifteen rows in their groups, one button for everything"
Launch "xp1" | Out-Null
OpenSettingsAt "system"
$sys = S "(function(){var b=document.querySelector('#pane-system .btn[data-goto=experimental]');return b?(b.disabled?'disabled':'live'):'none';})()"
Check "System's Experimental features row is live (it was badged)" ($sys -match 'live') $sys
node cdp.js click $script:sid "#pane-system .btn[data-goto=experimental]" | Out-Null; Start-Sleep 1
$on = S "document.getElementById('pane-experimental').classList.contains('on')"
Check "and it opens the Experimental pane" ($on -eq 'true') $on
$lede = S "document.querySelector('#pane-experimental .lede').innerText"
Check "the header says none of them can make Aviary less safe" ($lede -match 'None of them can make Aviary less safe') $lede
$shape = S "(function(){var p=document.getElementById('pane-experimental');return JSON.stringify({rows:p.querySelectorAll('.sw[data-key^=\'exp.\']').length,badges:p.querySelectorAll('.row .xt .xb').length,might:p.querySelectorAll('.might').length,groups:[].map.call(p.querySelectorAll('h2.upd-h'),function(h){return h.textContent}),off:!!p.querySelector('.btn[data-cmd=experimentsOff]')});})()"
$sh = ($shape | ConvertFrom-Json) | ConvertFrom-Json
Check "fifteen switches (stages 1-3), each with a badge and a What might happen line" ($sh.rows -eq 15 -and $sh.badges -eq 15 -and $sh.might -eq 15) $shape
Check "in their groups: Speed and memory, Looks, Tabs, Troubleshooting" (($sh.groups -join '|') -eq 'Speed and memory|Looks|Tabs|Troubleshooting') ($sh.groups -join '|')
Check "a Turn All Off button" ($sh.off -eq $true) $shape
$badgeColour = S "getComputedStyle(document.querySelector('#pane-experimental .xb--looks')).backgroundColor"
Check "the labels are drawn as coloured labels (not the description's grey)" ($badgeColour -notmatch 'rgba\(0, 0, 0, 0\)' -and $badgeColour -match 'rgb') $badgeColour

"2. switching one on saves it, is recorded, and an engine switch says it needs a restart"
node cdp.js click $script:sid ".sw[data-key='exp.smoothScrollOff']" | Out-Null; Start-Sleep 1
Check "settings.json says exp.smoothScrollOff = true" ((Setting "xp1" 'exp.smoothScrollOff') -eq 'true') (Setting "xp1" 'exp.smoothScrollOff')
$note = S "document.querySelector('.note[data-restart=\'exp.smoothScrollOff\']').hidden"
Check "its Restart Aviary note shows" ($note -eq 'false') $note
$rbtn = S "!!document.querySelector('.note[data-restart=\'exp.smoothScrollOff\'] .btn[data-cmd=restart]')"
Check "with a Restart Aviary button in it" ($rbtn -eq 'true') $rbtn
$ev = @(Select-String -Path "profiles\xp1\user.data.improvementcollection\events.jsonl" -Pattern '"experiment".*exp\.smoothScrollOff' -ErrorAction SilentlyContinue)
Check "diagnostics records it as an experiment (category 7)" ($ev.Count -ge 1) "$($ev.Count) lines"
$m = Marker "xp1"
Check "the safety marker notes the change" ($m -and $m.ChangedAt -and $m.CleanRuns -eq 0) ($m | ConvertTo-Json -Compress)
Check "the running engine did NOT get it (it only applies at launch)" ((Engine) -notmatch 'disable-smooth-scrolling') (Engine)
$dnote = S "!!document.querySelector('.note[data-restart=\'exp.darkWebsites\']')"
Check "Make every website dark has NO restart note - it works at once" ($dnote -eq 'false') $dnote

"3. Turn All Off turns everything off, and the page follows"
node cdp.js click $script:sid ".sw[data-key='exp.smoothScrollOff']" | Out-Null; Start-Sleep 0.8
node cdp.js click $script:sid ".btn[data-cmd=experimentsOff]" | Out-Null; Start-Sleep 1.5
# Every key the page shows - not a list kept here, which went stale the moment stage 3 added nine.
$keys = @(("$(S "[].map.call(document.querySelectorAll('.sw[data-key^=\'exp.\']'),function(s){return s.getAttribute('data-key')}).join(',')")" -replace '"', '').Split(','))
$after = @($keys | ForEach-Object { Setting "xp1" $_ })
Check "every exp.* in settings.json is false ($($keys.Count) of them)" ($keys.Count -eq 15 -and @($after | Where-Object { $_ -ne 'false' }).Count -eq 0) (($keys | ForEach-Object { "$_=$(Setting 'xp1' $_)" }) -join ' ')
$drawn = S "[].filter.call(document.querySelectorAll('.sw[data-key^=\'exp.\']'),function(s){return s.getAttribute('aria-checked')!=='false'}).length"
Check "every switch is drawn off at once" ($drawn -eq '0') $drawn
$notes = S "[].filter.call(document.querySelectorAll('.note[data-restart^=\'exp.\']'),function(n){return !n.hidden}).length"
Check "and no restart note is left (nothing changed from how the engine started)" ($notes -eq '0') $notes
[void](CloseClean)

# ---------------------------------------------------------------------------------------------
"4. each one reaches the engine - and does what it says (control: none on)"
Launch "xp4c" | Out-Null
$cl = Engine; $pc = Probe; $gpcC = Gpc
Check "control: none of the switches on the engine" ($cl -and $cl -notmatch 'disable-smooth-scrolling|WebContentsForceDark|disable-accelerated-video-decode') $cl
$featuresC = @([regex]::Matches($cl, '--enable-features=\S*') | ForEach-Object { $_.Value }) -join ' '
Check "control: Page Down glides (many positions)" ($pc.scroll.distinct -gt 10) ($pc.scroll | ConvertTo-Json -Compress)
Check "control: a page with no colours stays white" ((Luminance $pc.darkPng) -gt 200) (Luminance $pc.darkPng)
Check "control: the graphics card decodes video" ($pc.video.h264.powerEfficient -eq $true) ($pc.video | ConvertTo-Json -Compress)
StopAviary $script:pidv
Launch "xp4" $ALLON | Out-Null
$cl = Engine; $p = Probe; $gpc = Gpc
Check "the engine has --disable-smooth-scrolling" ($cl -match '--disable-smooth-scrolling') $cl
Check "the engine has --disable-accelerated-video-decode" ($cl -match '--disable-accelerated-video-decode') $cl
$features = @([regex]::Matches($cl, '--enable-features=\S*') | ForEach-Object { $_.Value }) -join ' '
Check "dark is NOT an engine feature (per tab instead), and the features list is unchanged" ($cl -notmatch 'WebContentsForceDark' -and $features -eq $featuresC) "on: $features | control: $featuresC"
Check "Page Down now jumps (1-2 positions)" ($p.scroll.distinct -le 2 -and $p.scroll.end -gt 300) ($p.scroll | ConvertTo-Json -Compress)
Check "a page with no colours is painted dark" ((Luminance $p.darkPng) -lt 60) (Luminance $p.darkPng)
Check "video is no longer decoded by the graphics card" ($p.video.h264.powerEfficient -eq $false -and $p.video.vp9.powerEfficient -eq $false) ($p.video | ConvertTo-Json -Compress)
Check "GPC still reaches pages with all three on" ($gpc -eq 'true' -and $gpcC -eq 'true') "on: $gpc, control: $gpcC"

"5. dark websites, per tab: websites dark, Aviary's own pages and Reader not, and on/off at once"
StopAviary $script:pidv
Launch "xp5" ',"exp.darkWebsites":"true","theme":"Light"' | Out-Null
Remove-Item "shots\xp5-*.png" -ErrorAction SilentlyContinue
# Every shot is of the tab IN FRONT - a background tab is not drawn (HANDOFF 5).
function Mean($px) { [int](($px.R + $px.G + $px.B) / 3) }
function ShotMean([string]$tab, [string]$name, [int]$x, [int]$y) {
    if (Shot $tab "shots\xp5-$name.png") { Mean (Pixel "shots\xp5-$name.png" $x $y) } else { -1 }
}
$t0 = @(Pages)[0].Substring(7, 8)
$v = ShotMean $t0 "start" 590 820
Check "the Start Page (light theme) stays light" ($v -gt 170) $v
node cdp.js nav $t0 "http://127.0.0.1:8797/article" | Out-Null; Start-Sleep 2
$v = ShotMean $t0 "article" 1150 10
Check "a website in the same window is dark (the control for the two above and below)" ($v -ge 0 -and $v -lt 60) $v
U -Id PageSettingsButton | Out-Null; Start-Sleep 1; U -Name "Show Reader" | Out-Null; Start-Sleep 2
$v = ShotMean $t0 "reader" 1150 10
Check "Reader keeps its own (White) background" ($v -gt 200) $v
U -Id PageSettingsButton | Out-Null; Start-Sleep 1; U -Name "Hide Reader" | Out-Null; Start-Sleep 2
$v = ShotMean $t0 "reader-off" 1150 10
Check "and leaving Reader, the website is dark again" ($v -ge 0 -and $v -lt 60) $v
OpenSettingsAt "experimental"
$v = ShotMean $script:sid "settings" 900 60
Check "Settings (light theme) stays light" ($v -gt 200) $v
node cdp.js click $script:sid ".sw[data-key='exp.darkWebsites']" | Out-Null; Start-Sleep 1
U -Name "exp article" -Action Toggle | Out-Null; Start-Sleep 1.5
$v = ShotMean $t0 "off-at-once" 1150 10
Check "switched OFF in Settings: the open website is light at once, no restart" ($v -gt 200) $v
Check "(and the switch said off)" ((Setting "xp5" 'exp.darkWebsites') -eq 'false') (Setting "xp5" 'exp.darkWebsites')
U -Name "Settings" -Action Toggle | Out-Null; Start-Sleep 1.2
node cdp.js click $script:sid ".sw[data-key='exp.darkWebsites']" | Out-Null; Start-Sleep 1
Check "(switched on again)" ((Setting "xp5" 'exp.darkWebsites') -eq 'true') (Setting "xp5" 'exp.darkWebsites')
U -Name "exp article" -Action Toggle | Out-Null; Start-Sleep 1.5
$v = ShotMean $t0 "on-at-once" 1150 10
Check "switched back ON: dark again at once" ($v -ge 0 -and $v -lt 60) $v
# A DIFFERENT site (127.0.0.2 - a new renderer process) in the same tab. Not the Collection: its
# index is a dark design of its own, so it reads dark with the experiment off too (found 25 Sep).
node cdp.js nav $t0 "http://127.0.0.2:8797/white" | Out-Null; Start-Sleep 2.5
$v = ShotMean $t0 "cross-site" 1150 10
Check "it follows the tab to a different site (a new renderer process)" ($v -ge 0 -and $v -lt 60) $v
node cdp.js nav $t0 "http://127.0.0.2:8797/article" | Out-Null; Start-Sleep 2
U -Id PageSettingsButton | Out-Null; Start-Sleep 1; U -Name "Show Reader" | Out-Null; Start-Sleep 2
node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 2
$v = ShotMean $t0 "after-reader" 1150 10
Check "leaving a page while Reader was on does not leave the next site undarkened" ($v -ge 0 -and $v -lt 60) $v
StopAviary $script:pidv

"6. settings.json cannot put a switch on the engine"
Launch "xp6" ',"exp.darkWebsites":"--disable-web-security","exp.noVideoDecode":"yes","exp.smoothScrollOff":"true --no-sandbox","exp.evil":"true"' | Out-Null
$cl = Engine
Check "no value from settings.json reaches the engine" ($cl -and $cl -notmatch 'disable-web-security|no-sandbox') $cl
Check "and a value that is not exactly true switches nothing on" ($cl -notmatch 'WebContentsForceDark|disable-accelerated-video-decode|disable-smooth-scrolling') $cl
StopAviary $script:pidv

# ---------------------------------------------------------------------------------------------
"7. a crash soon after: next launch has them all off, BEFORE the engine starts, and asks"
Launch "xp7" ',"exp.noVideoDecode":"true"' -crash "fatal" | Out-Null
$died = WaitGone $script:pidv 20
Check "(the deliberate fatal crash ended that Aviary)" $died "still running"
Launch "xp7" -Keep | Out-Null
Start-Sleep 1
$t = Tree
Check "the question is on screen" ($t -match 'Aviary switched off experimental features') "no dialog"
Check "with Keep Them Off and Turn Them Back On" ($t -match 'Keep Them Off' -and $t -match 'Turn Them Back On') "buttons missing"
Check "it names the experiment" ($t -match 'Turn off video hardware decoding') "no name"
Check "settings.json has it off" ((Setting "xp7" 'exp.noVideoDecode') -eq 'false') (Setting "xp7" 'exp.noVideoDecode')
Check "and the engine started WITHOUT it" ((Engine) -notmatch 'disable-accelerated-video-decode') (Engine)
U -Name "Keep Them Off" | Out-Null; Start-Sleep 1
Check "Keep Them Off: the question goes, it stays off" (((Tree) -notmatch 'Aviary switched off') -and (Setting "xp7" 'exp.noVideoDecode') -eq 'false') (Setting "xp7" 'exp.noVideoDecode')
[void](CloseClean)
Launch "xp7" -Keep | Out-Null
Check "and it is not asked again next time" ((Tree) -notmatch 'Aviary switched off experimental features') "asked again"
[void](CloseClean)

"8. Turn Them Back On (and the restart it then offers, cancelled)"
Launch "xp8" ',"exp.noVideoDecode":"true"' -crash "fatal" | Out-Null
[void](WaitGone $script:pidv 20)
Launch "xp8" -Keep | Out-Null
Start-Sleep 1
U -Name "Turn Them Back On" | Out-Null; Start-Sleep 1.5
Check "Turn Them Back On: settings.json has it on again" ((Setting "xp8" 'exp.noVideoDecode') -eq 'true') (Setting "xp8" 'exp.noVideoDecode')
$t = Tree
Check "and, as it needs a restart, Restart Aviary? is asked" ($t -match 'Restart Aviary\?') "no restart question"
U -Name "Cancel" | Out-Null; Start-Sleep 1
Check "Cancel: Aviary stays open" (Alive $script:pidv) "gone"
StopAviary $script:pidv
Launch "xp8b" ',"exp.darkWebsites":"true"' -crash "fatal" | Out-Null
[void](WaitGone $script:pidv 20)
Launch "xp8b" -Keep | Out-Null
Start-Sleep 1
U -Name "Turn Them Back On" | Out-Null; Start-Sleep 1.5
Check "one that works at once (dark websites): back on, and NO restart is asked" ((Setting "xp8b" 'exp.darkWebsites') -eq 'true' -and (Tree) -notmatch 'Restart Aviary\?') (Setting "xp8b" 'exp.darkWebsites')
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 2
Remove-Item "shots\xp8b.png" -ErrorAction SilentlyContinue
$v = if (Shot $t0 "shots\xp8b.png") { $px = Pixel "shots\xp8b.png" 1150 10; [int](($px.R + $px.G + $px.B) / 3) } else { -1 }
Check "and websites are dark again in this very session" ($v -ge 0 -and $v -lt 60) $v
StopAviary $script:pidv

"9. the other ways a run goes wrong count too"
Launch "xp9a" ',"exp.smoothScrollOff":"true"' -crash "ui" | Out-Null
Start-Sleep 5
Check "(a UI-thread failure: Aviary survived it)" (Alive $script:pidv) "gone"
[void](CloseClean)
$m = Marker "xp9a"
Check "a survived failure is kept in the marker even after a normal close" ($m.Problem -match 'failed and recovered' -and -not $m.Running) ($m | ConvertTo-Json -Compress)
Launch "xp9a" -Keep | Out-Null
Check "and the next launch switches it off" ((Setting "xp9a" 'exp.smoothScrollOff') -eq 'false' -and (Tree) -match 'part of it failed and recovered') (Setting "xp9a" 'exp.smoothScrollOff')
StopAviary $script:pidv
Launch "xp9b" ',"exp.smoothScrollOff":"true"' | Out-Null
& .\kill-engine.ps1 -ProcessId $script:pidv | Out-Null; Start-Sleep 3
[void](CloseClean)
Launch "xp9b" -Keep | Out-Null
Check "the engine dying counts: switched off, and it says the engine stopped" ((Setting "xp9b" 'exp.smoothScrollOff') -eq 'false' -and (Tree) -match 'browser engine stopped unexpectedly') (Setting "xp9b" 'exp.smoothScrollOff')
StopAviary $script:pidv
Launch "xp9c" ',"exp.smoothScrollOff":"true","improvementCollection":"false"' -crash "fatal" | Out-Null
[void](WaitGone $script:pidv 20)
Launch "xp9c" -Keep | Out-Null
Check "with data collection OFF it works just the same" ((Setting "xp9c" 'exp.smoothScrollOff') -eq 'false' -and (Tree) -match 'Aviary switched off') (Setting "xp9c" 'exp.smoothScrollOff')
Check "(and no diagnostics folder was needed for it)" (-not (Test-Path "profiles\xp9c\user.data.improvementcollection\events.jsonl")) "folder exists"
StopAviary $script:pidv

"10. 'soon' has an end: two normal runs, and a crash is no longer blamed on them (controls)"
Launch "xp10" ',"exp.noVideoDecode":"true"' | Out-Null; [void](CloseClean)
Launch "xp10" -Keep | Out-Null; StopAviary $script:pidv
Launch "xp10" -Keep | Out-Null
Check "after ONE normal run, a crash still switches them off" ((Setting "xp10" 'exp.noVideoDecode') -eq 'false') (Setting "xp10" 'exp.noVideoDecode')
StopAviary $script:pidv
Launch "xp10b" ',"exp.noVideoDecode":"true"' | Out-Null; [void](CloseClean)
Launch "xp10b" -Keep | Out-Null; [void](CloseClean)
Check "(two normal runs counted)" ((Marker "xp10b").CleanRuns -eq 2) ((Marker "xp10b") | ConvertTo-Json -Compress)
Launch "xp10b" -Keep | Out-Null; StopAviary $script:pidv
Launch "xp10b" -Keep | Out-Null
Check "after TWO normal runs, a crash leaves them on, and nothing is asked" ((Setting "xp10b" 'exp.noVideoDecode') -eq 'true' -and (Tree) -notmatch 'Aviary switched off') (Setting "xp10b" 'exp.noVideoDecode')
Check "(and the engine has it)" ((Engine) -match 'disable-accelerated-video-decode') (Engine)
StopAviary $script:pidv
Launch "xp10c" | Out-Null; StopAviary $script:pidv
Launch "xp10c" -Keep | Out-Null
Check "a crash with NO experiments on asks nothing" ((Tree) -notmatch 'Aviary switched off') "asked"
StopAviary $script:pidv

# ---------------------------------------------------------------------------------------------
"12. Open pinned tabs only when clicked (stage 2) - control: off"
$PINS = ',"pinnedTabs":"[\"http://127.0.0.1:8797/white\",\"http://127.0.0.1:8797/long\"]"'
Launch "xp12c" $PINS | Out-Null
$c = @(Pages).Count
Check "control: every pinned tab starts an engine at launch (2 + the Start Page)" ($c -eq 3) (@(Pages) -join ' | ')
StopAviary $script:pidv
Launch "xp12" ($PINS + ',"exp.pinnedOnClick":"true"') | Out-Null
$p = @(Pages)
Check "on: only the Start Page has an engine at launch" ($p.Count -eq 1) ($p -join ' | ')
Check "both pinned tabs are in the strip all the same" (@((U -Action Dump -Name "127.0.0.1") | Where-Object { $_ -match '^Button' }).Count -ge 2) ((U -Action Dump -Name "127.0.0.1") -join ' | ')
U -Name "127.0.0.1" -Action Toggle | Out-Null; Start-Sleep 3
$p = @(Pages)
Check "pressing one starts it, and it loads its page" ($p.Count -eq 2 -and ($p -join ' ') -match '8797/(white|long)') ($p -join ' | ')
[void](CloseClean)
$saved = (Setting "xp12" 'pinnedTabs')
Check "a normal close keeps BOTH pinned (one never started)" ($saved -match 'white' -and $saved -match 'long') $saved

"13. Mute tabs in the background (stage 2) - what comes out of the speakers (control: off)"
foreach ($run in @(@('xp13c', ''), @('xp13', ',"exp.muteBackground":"true"'))) {
    Launch $run[0] $run[1] | Out-Null
    $on = [bool]$run[1]
    $t0 = @(Pages)[0].Substring(7, 8)
    node cdp.js nav $t0 "http://127.0.0.1:8797/tone" | Out-Null; Start-Sleep 1.5
    node cdp.js eval $t0 "play()" | Out-Null; Start-Sleep 1
    $front = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
    U -Id NewTabButton | Out-Null; Start-Sleep 2
    $behind = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
    U -Name "exp tone" -Action Toggle | Out-Null; Start-Sleep 1.5
    $back = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
    $label = if ($on) { "on" } else { "control" }
    Check "${label}: the tone is heard with its tab in front" ($front -gt 0.005) $front
    Check "${label}: behind another tab it is $(if ($on) { 'SILENT' } else { 'still heard' })" $(if ($on) { $behind -eq 0 } else { $behind -gt 0.005 }) $behind
    Check "${label}: back in front it is heard again" ($back -gt 0.005) $back
    # A tab opened BEHIND (Ctrl+click) never passes SelectTab - muted from the start when on; the
    # control proves a tab behind CAN play (so silence is the mute, not a page that never started).
    node cdp.js eval $t0 "window.__ctx.close().then(function(){return true})" | Out-Null
    node cdp.js eval $t0 "(function(){var a=document.createElement('a');a.id='totone';a.href='/tone?b=1';a.textContent='tone';document.body.appendChild(a);return true})()" | Out-Null
    node cdp.js mclick $t0 "#totone" ctrl | Out-Null; Start-Sleep 2.5
    $bg = (@(Pages) | Where-Object { $_ -match 'tone\?b=1' } | Select-Object -First 1)
    if ($bg) { node cdp.js eval $bg.Substring(7, 8) "play()" | Out-Null; Start-Sleep 1 }
    $v = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
    Check "${label}: a tab opened behind with Ctrl+click is $(if ($on) { 'silent from the start' } else { 'heard' })" $(if ($on) { $bg -and $v -eq 0 } else { $bg -and $v -gt 0.005 }) "tab: $bg, peak $v"
    if ($on) {
        # A video popped out into picture in picture keeps its sound when its tab goes behind.
        node cdp.js nav $t0 "http://127.0.0.1:8797/pipsound" | Out-Null; Start-Sleep 1.5
        $pip = node cdp.js eval $t0 "pip()"
        U -Name "exp pipsound" -Action Toggle | Out-Null; Start-Sleep 1
        U -Id NewTabButton | Out-Null; Start-Sleep 2.5
        $v = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
        Check "on: a video in picture in picture keeps its sound behind another tab" ($pip -eq 'true' -and $v -gt 0.005) "pip: $pip, peak $v"
        # And switching the row off unmutes at once. The popped-out video's stream is stopped first
        # (it kept its sound on purpose), so the only thing playing is the muted Ctrl+click tab behind.
        node cdp.js eval $t0 "(function(){var v=document.getElementById('v');v.srcObject.getTracks().forEach(function(t){t.stop()});if(document.pictureInPictureElement){document.exitPictureInPicture()}return true})()" | Out-Null
        # The known limit, measured and now said in the row: a video popped out from INSIDE a frame (an
        # embedded player) is invisible to the top page, so its tab is muted like any other.
        node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 1.5
        U -Name "exp white" -Action Toggle | Out-Null; Start-Sleep 1
        $fp = node frame-pip.js $t0 | ConvertFrom-Json
        Start-Sleep 1; $inFront = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
        U -Id NewTabButton | Out-Null; Start-Sleep 2.5
        $v = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
        Check "on: a video popped out from inside a FRAME goes quiet with its tab (the row says so; the top page cannot see it)" ($fp.framePip -eq $true -and $fp.topSaysPip -eq $false -and $inFront -gt 0.005 -and $v -eq 0) "pip: $($fp | ConvertTo-Json -Compress), in front $inFront, behind $v"
        OpenSettingsAt "experimental"
        $row = S "document.querySelector('.sw[data-key=\'exp.muteBackground\']').closest('.row').innerText"
        Check "(the Mute row names that exception)" ($row -match 'inside a frame on the page') $row
        $quiet = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
        node cdp.js click $script:sid ".sw[data-key='exp.muteBackground']" | Out-Null; Start-Sleep 1.5
        $v = [double](& .\audio-peak.ps1 -ProcessId $script:pidv)
        Check "on -> off in Settings: the tab behind is heard again at once (silent just before)" ($quiet -eq 0 -and $v -gt 0.005) "before: $quiet, after: $v"
    }
    StopAviary $script:pidv
}

"14. Show speed and memory on top of pages (stage 2) - control: off"
# The readout is not in the UI Automation tree (measured: nothing laid over the web view is), so
# AVIARY_OVERLAY_TEST=1 makes Aviary write what it DREW to overlay-test.txt at every update.
function OverlayText([string]$name) { $f = "profiles\$name\overlay-test.txt"; if (Test-Path $f) { Get-Content $f -Raw } else { "" } }
# The old capture is deleted first: a capture that fails must read -1, never the last run's picture
# (25 Sep: a save that failed left a dark corner from an earlier run, which passed the "on" check).
function CornerMean { $f = Join-Path $kit "shots\xp14.png"; Remove-Item $f -ErrorAction SilentlyContinue
    & .\peek.ps1 -ProcessId $script:pidv -Out $f | Out-Null
    if (-not (Test-Path $f)) { return -1 }
    $b = [System.Drawing.Bitmap]::FromFile($f); $p = $b.GetPixel($b.Width - 60, $b.Height - 30); $b.Dispose(); [int](($p.R + $p.G + $p.B) / 3) }
$env:AVIARY_OVERLAY_TEST = "1"
Launch "xp14c" | Out-Null
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 3
$v = CornerMean
Check "control: nothing drawn (no readout written, the white page's corner is white)" ((OverlayText "xp14c") -eq "" -and $v -gt 200) "file: '$(OverlayText "xp14c")', corner $v"
StopAviary $script:pidv
Launch "xp14" ',"exp.perfOverlay":"true"' | Out-Null
$env:AVIARY_OVERLAY_TEST = $null
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 3.5
$ov = OverlayText "xp14"
Check "a readout is drawn: Aviary, the tab in front, the processor" ($ov -match 'Aviary +\d' -and $ov -match 'Processor +\d') $ov
Check "with the tab in front measured (not '-')" ($ov -match 'This tab +\d') $ov
$v = CornerMean
Check "it is drawn over the page's corner (a dark box on a white page)" ($v -ge 0 -and $v -lt 120) "corner $v"
$e = node cdp.js eval $t0 "(function(){var x=document.elementFromPoint(innerWidth-40,innerHeight-20);return (x===document.body||x===document.documentElement||x.id==='swatch')&&document.querySelectorAll('*').length<14})()"
Check "the page cannot see it (nothing of Aviary's in the page under that corner)" ($e -eq 'true') $e
OpenSettingsAt "perf"; Start-Sleep 3
$pane = S "document.getElementById('pf-mem') && document.getElementById('pf-mem').innerText"
Check "the Performance page works at the same time (one sampler, two listeners)" ($pane -match '\d') $pane
node cdp.js click $script:sid "button[data-target=experimental]" | Out-Null; Start-Sleep 2
# A page behind takes ~64 MB, so a readout that is still being updated MUST show a different figure.
$c1 = OverlayText "xp14"
node cdp.js eval $t0 "window.__big = new Float64Array(8e6).fill(1.5); true" | Out-Null
Start-Sleep 4; $c2 = OverlayText "xp14"
$m1 = [regex]::Match($c1, 'Aviary +(\d+)').Groups[1].Value; $m2 = [regex]::Match($c2, 'Aviary +(\d+)').Groups[1].Value
Check "and it keeps updating after the Performance page stops listening (Aviary's memory went up)" ($m1 -and $m2 -and [int]$m2 -gt [int]$m1 + 30) "before: $m1 MB, after: $m2 MB"
# "How often to measure" with the readout keeping the sampler going (found 25 Sep: the new rate never
# reached a sampler that was already running, and the page said a rate it was not using - bug 92).
node cdp.js click $script:sid "button[data-target=perf]" | Out-Null; Start-Sleep 1.5
S "document.querySelector('.sel[data-key=perfInterval]').scrollIntoView({block:'center'})" | Out-Null; Start-Sleep 0.8
node cdp.js press $script:sid ".sel[data-key=perfInterval] .sel__btn" | Out-Null; Start-Sleep 0.6
node cdp.js press $script:sid ".sel[data-key=perfInterval] .sel__opt[data-v='Every 5 seconds']" | Out-Null; Start-Sleep 1
$seen = @{}; $d = (Get-Date).AddSeconds(12)
while ((Get-Date) -lt $d) { $o = OverlayText "xp14"; if ($o) { $seen[$o.Split("`n")[0]] = 1 }; Start-Sleep -Milliseconds 250 }
$gaps = S "new Promise(function(done){var ts=[],f=document.getElementById('pf-foot');new MutationObserver(function(){ts.push(Date.now())}).observe(f,{childList:true,characterData:true,subtree:true});setTimeout(function(){var g=[];for(var i=1;i<ts.length;i++)g.push(ts[i]-ts[i-1]);done(g.join(','))},12000)})"
$g = @(("$gaps" -replace '"', '').Split(',') | Where-Object { $_ } | ForEach-Object { [int]$_ })
Check "choosing Every 5 seconds takes effect at once: the page's updates come 5 s apart" ($g.Count -ge 1 -and @($g | Where-Object { $_ -lt 4500 -or $_ -gt 5500 }).Count -eq 0) "gaps: $gaps"
Check "and so do the readout's (2-3 in 12 s, not 12)" ($seen.Count -ge 2 -and $seen.Count -le 4) "$($seen.Count) updates"
$foot = S "document.getElementById('pf-foot').textContent"
Check "the page says 5 seconds, and that it carries on for the corner readout" ($foot -match 'every 5 seconds' -and $foot -match 'carries on after you leave this page') $foot
node cdp.js press $script:sid ".sel[data-key=perfInterval] .sel__btn" | Out-Null; Start-Sleep 0.6
node cdp.js press $script:sid ".sel[data-key=perfInterval] .sel__opt[data-v='Every second']" | Out-Null; Start-Sleep 1
node cdp.js click $script:sid "button[data-target=experimental]" | Out-Null; Start-Sleep 1.5
node cdp.js click $script:sid ".sw[data-key='exp.perfOverlay']" | Out-Null; Start-Sleep 2
$s1 = OverlayText "xp14"; Start-Sleep 3; $s2 = OverlayText "xp14"
Check "switched off in Settings: it stops at once (no update in 3 s)" ($s1 -ne "" -and $s1 -eq $s2) "before: $($s1.Split("`n")[0]) after: $($s2.Split("`n")[0])"
StopAviary $script:pidv

"15. bug 91: a website's speculation rules no longer load pages around Aviary"
Launch "xp15" | Out-Null
$sp = node spec-probe.js | ConvertFrom-Json
Check "HTMLScriptElement.supports('speculationrules') is false" ($sp.supports -eq $false) $sp.supports
Check "rules in the page's HTML load nothing (immediate, on hover, prerender)" (@($sp.staticHtml).Count -eq 0 -and $sp.rulesLeftInPage -eq 0) (@($sp.staticHtml) -join ', ')
Check "rules added by script load nothing" (@($sp.addedByScript).Count -eq 0) (@($sp.addedByScript) -join ', ')
Check "<link rel=prefetch> - which DOES pass Aviary's filter - still works" (@($sp.linkPrefetchControl).Count -eq 1) (@($sp.linkPrefetchControl) -join ', ')
# The second look (25 Sep): four more ways in got past the first guard - each measured loading on it.
$closed = 'shadowOpen', 'shadowClosed', 'otherRealm', 'dsdOpen'
$loaded = @($closed | Where-Object { $sp.late.$_ -ne 0 })
Check "a script made rules AFTER it was added, or changed away at once, loads nothing" ($sp.late.lateType -eq 0 -and $sp.late.lateEmpty -eq 0 -and $sp.late.typeAway -eq 0) ($sp.late | ConvertTo-Json -Compress)
Check "rules inside shadow roots load nothing (made by script open or closed, even with another frame's attachShadow; declared open in the HTML)" ($loaded.Count -eq 0) ($sp.late | ConvertTo-Json -Compress)
"  OPEN  dsdClosed - a CLOSED shadow root declared in the page's own HTML (said so in HANDOFF): loaded $($sp.late.dsdClosed)"
StopAviary $script:pidv
$m5 = Join-Path $kit "..\..\publish\Aviary\Aviary.exe"
if ((Test-Path $m5) -and (Get-Item $m5).VersionInfo.ProductVersion -like "*0e8d432*") {
    $line = & .\launch-quiet.ps1 -Profile xp15c -Fresh -Port $Port -Exe (Resolve-Path $m5).Path -SettingsJson ($BASE + '}')
    $script:pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
    Start-Sleep 4
    $spc = node spec-probe.js | ConvertFrom-Json
    Check "control: the published M5 build (no guard) DID load them" (@($spc.staticHtml).Count -ge 2 -and @($spc.addedByScript).Count -ge 1) ((@($spc.staticHtml) + @($spc.addedByScript)) -join ', ')
    $routes = 'lateType', 'lateEmpty', 'typeAway', 'shadowOpen', 'shadowClosed', 'otherRealm', 'dsdOpen', 'dsdClosed'
    Check "control: and every one of the second look's routes loads there (so each is a real way in)" (@($routes | Where-Object { $spc.late.$_ -lt 1 }).Count -eq 0) ($spc.late | ConvertTo-Json -Compress)
    StopAviary $script:pidv
} else { "  (control skipped: publish/Aviary is no longer the M5 build)" }

"16. bug 92: leaving Settings -> Performance stops the measuring, as its foot says (control: M5)"
# Each update of the page rewrites #pf-foot, so an observer inside the page counts them - also while
# the pane is hidden, which is exactly what must not happen. Readout off: nothing else may sample.
$COUNT = "(function(){window.__pfTs=[];new MutationObserver(function(){window.__pfTs.push(Date.now())}).observe(document.getElementById('pf-foot'),{childList:true,characterData:true,subtree:true});return true})()"
function Updates { [int]("$(S 'window.__pfTs.length')" -replace '"', '') }
function LeaveAndCount {
    $script:showing = Updates
    node cdp.js click $script:sid "button[data-target=privacy]" | Out-Null; Start-Sleep 1
    $at = Updates; Start-Sleep 5; $script:awayPane = (Updates) - $at
}
Launch "xp16" | Out-Null
OpenSettingsAt "perf"; Start-Sleep 1.5
S $COUNT | Out-Null; Start-Sleep 4
LeaveAndCount
Check "the page updates while it is showing" ($script:showing -ge 3) $script:showing
Check "and not once in 5 s on another pane" ($script:awayPane -eq 0) "$($script:awayPane) updates while away"
node cdp.js click $script:sid "button[data-target=perf]" | Out-Null; Start-Sleep 1
$at = Updates; Start-Sleep 3
Check "back on the pane, it measures again" ((Updates) -gt $at) "$at -> $(Updates)"
U -Id NewTabButton | Out-Null; Start-Sleep 1
$at = Updates; Start-Sleep 5; $awayTab = (Updates) - $at
Check "and not once in 5 s while another TAB is in front" ($awayTab -eq 0) "$awayTab updates"
StopAviary $script:pidv
if ((Test-Path $m5) -and (Get-Item $m5).VersionInfo.ProductVersion -like "*0e8d432*") {
    $line = & .\launch-quiet.ps1 -Profile xp16c -Fresh -Port $Port -Exe (Resolve-Path $m5).Path -SettingsJson ($BASE + '}')
    $script:pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
    Start-Sleep 4
    OpenSettingsAt "perf"; Start-Sleep 1.5
    S $COUNT | Out-Null; Start-Sleep 4
    LeaveAndCount
    Check "control: the published M5 build went on measuring on another pane (the bug)" ($script:showing -ge 3 -and $script:awayPane -ge 3) "$($script:awayPane) updates while away"
    StopAviary $script:pidv
} else { "  (control skipped: publish/Aviary is no longer the M5 build)" }

# ---------------------------------------------------------------------------------------------
# STAGE 3 - tabs and looks. Every experiment against a control with its switch off.
function Val([string]$t, [string]$js) { "$(node cdp.js eval $t $js)" -replace '^"|"$', '' }
function Bar([string]$t) { [int](Val $t "innerWidth - document.documentElement.clientWidth") }
function Zm([string]$t) { Val $t "document.documentElement.style.zoom || '1'" }
function InReader([string]$t) { Val $t "!!document.getElementById('av-reader')" }
function Offered { $m = [regex]::Match((Tree), '(Show|Hide) Reader'); if ($m.Success) { $m.Value } else { '(none)' } }
function Tab([string]$like) { $p = @(Pages | Where-Object { $_ -match [regex]::Escape($like) }); if ($p.Count) { $p[0].Substring(7, 8) } else { '' } }
function TintNow([string]$name) { $f = "profiles\$name\tint-test.txt"; if (Test-Path $f) { (Get-Content $f -Raw).Trim() } else { '(never painted)' } }
function Peak { [double](& .\audio-peak.ps1 -ProcessId $script:pidv) }

"17. Thin scrollbars on websites - control: off"
Launch "xp17c" | Out-Null
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/long" | Out-Null; Start-Sleep 2
$c = Bar $t0
Check "control: a page's scroll bar takes 15 px" ($c -eq 15) $c
StopAviary $script:pidv
Launch "xp17" ',"exp.thinScrollbars":"true"' | Out-Null
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/long" | Out-Null; Start-Sleep 2
$v = Bar $t0
Check "on: it takes 10" ($v -eq 10) $v
node cdp.js eval $t0 "(function(){var s=document.createElement('style');s.textContent='html{scrollbar-width:none}';document.head.appendChild(s);return true})()" | Out-Null; Start-Sleep 0.5
$v = Bar $t0
Check "a site that sets its own width keeps it (its 'none' beats Aviary's layered 'thin')" ($v -eq 0) $v
node cdp.js nav $t0 "http://127.0.0.1:8797/long" | Out-Null; Start-Sleep 2
OpenSettingsAt "experimental"
$own = S "getComputedStyle(document.documentElement).scrollbarWidth + ' / ' + document.adoptedStyleSheets.length"
# Bug 95: with diagnostics on (as here) a new tab's registration of the websites' stylesheet won the race
# with the tab becoming Settings every time, so Settings wore it. The script now checks the page itself.
Check "bug 95: Aviary's own pages keep their look (Settings: auto, no websites' stylesheet)" ($own -match 'auto / 0') $own
node cdp.js click $script:sid ".sw[data-key='exp.thinScrollbars']" | Out-Null; Start-Sleep 1.5
$v = Bar $t0
Check "switched off: the open page's bar is back to 15 at once" ($v -eq 15) $v
StopAviary $script:pidv
if ((Test-Path $m5) -and (Get-Item $m5).VersionInfo.ProductVersion -like "*0e8d432*") {
    $line = & .\launch-quiet.ps1 -Profile xp17m -Fresh -Port $Port -Exe (Resolve-Path $m5).Path -SettingsJson ($BASE + ',"underlineLinks":"true"}')
    $script:pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }; Start-Sleep 4
    $t0 = @(Pages)[0].Substring(7, 8)
    node cdp.js nav $t0 "http://127.0.0.1:8797/long" | Out-Null; Start-Sleep 2
    OpenSettingsAt "privacy"
    $own = S "document.adoptedStyleSheets.length"
    Check "control (bug 95): the published M5 build, diagnostics on, put the websites' underline sheet on Settings" ($own -eq '1') $own
    StopAviary $script:pidv
}

"18. Remember each website's zoom - control: off (one zoom for every site)"
function ZoomStep([string]$item, [string]$window = "") {
    & .\uia.ps1 -ProcessId $script:pidv -Id PageSettingsButton -Window $window | Out-Null; Start-Sleep 1
    & .\uia.ps1 -ProcessId $script:pidv -Name $item -Window $window | Out-Null; Start-Sleep 1
}
function SiteZoomSaved([string]$name, [string]$site) {
    $f = "profiles\$name\sites.json"
    if (-not (Test-Path $f)) { return '' }
    $j = Get-Content $f -Raw | ConvertFrom-Json; "$($j.PSObject.Properties | Where-Object { $_.Name -eq "$site|zoom" } | ForEach-Object { $_.Value })"
}
foreach ($run in @(@('xp18c', ''), @('xp18', ',"exp.siteZoom":"true"'))) {
    $on = [bool]$run[1]; $label = if ($on) { 'on' } else { 'control' }
    Launch $run[0] $run[1] | Out-Null
    $a = @(Pages)[0].Substring(7, 8)
    node cdp.js nav $a "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 1.5
    U -Id NewTabButton | Out-Null; Start-Sleep 2
    $b = @(Pages | Where-Object { $_.Substring(7, 8) -ne $a })[0].Substring(7, 8)
    node cdp.js nav $b "http://127.0.0.2:8797/long" | Out-Null; Start-Sleep 1.5
    U -Name "exp white" -Action Toggle | Out-Null; Start-Sleep 1
    ZoomStep "Zoom In"
    $za = Zm $a; $zb = Zm $b
    if (-not $on) {
        Check "control: zooming one site zooms every site ($za here, $zb on the other)" ($za -eq '1.1' -and $zb -eq '1.1') "$za / $zb"
        StopAviary $script:pidv
        continue
    }
    Check "on: zooming 127.0.0.1 leaves 127.0.0.2 alone" ($za -eq '1.1' -and $zb -eq '1') "$za / $zb"
    Check "and it is kept for that site in sites.json" ((SiteZoomSaved $run[0] '127.0.0.1') -eq '110%') (SiteZoomSaved $run[0] '127.0.0.1')
    Check "the default zoom is untouched (absent, or 100%)" ((Setting $run[0] 'zoom') -in @($null, '', '100%')) "'$(Setting $run[0] 'zoom')'"
    node cdp.js nav $b "http://127.0.0.1:8797/long" | Out-Null; Start-Sleep 1.5
    Check "another tab opening that site gets its zoom" ((Zm $b) -eq '1.1') (Zm $b)
    node cdp.js nav $a "http://127.0.0.2:8797/white" | Out-Null; Start-Sleep 1.5
    Check "and leaving the site goes back to the default" ((Zm $a) -eq '1') (Zm $a)
    OpenSettingsAt "experimental"
    node cdp.js click $script:sid ".sw[data-key='exp.siteZoom']" | Out-Null; Start-Sleep 1.5
    Check "switched off: every site is back on the default at once" ((Zm $b) -eq '1') (Zm $b)
    node cdp.js click $script:sid ".sw[data-key='exp.siteZoom']" | Out-Null; Start-Sleep 1.5
    Check "on again: the site's zoom comes back (it was kept)" ((Zm $b) -eq '1.1') (Zm $b)
    U -Name "exp long" -Action Toggle | Out-Null; Start-Sleep 1
    ZoomStep "Actual Size  (110%)"
    Check "Actual Size puts it back at 100% - the default - and forgets it" ((Zm $b) -eq '1' -and (SiteZoomSaved $run[0] '127.0.0.1') -eq '') "$(Zm $b), kept: '$(SiteZoomSaved $run[0] '127.0.0.1')'"
    # A private window keeps nothing.
    $before = (node cdp.js list) -join "`n"
    U -Id MenuButton | Out-Null; Start-Sleep 1
    U -Name "New Private Window" | Out-Null; Start-Sleep 3
    $priv = (Pages | ForEach-Object { $_.Substring(7, 8) } | Where-Object { $before -notmatch $_ } | Select-Object -First 1)
    node cdp.js nav $priv "http://127.0.0.2:8797/white" | Out-Null; Start-Sleep 1.5
    ZoomStep "Zoom In" "Private"
    Check "a private window zooms its site ($(Zm $priv))..." ((Zm $priv) -eq '1.1') (Zm $priv)
    Check "...and writes nothing down" ((SiteZoomSaved $run[0] '127.0.0.2') -eq '') (SiteZoomSaved $run[0] '127.0.0.2')
    StopAviary $script:pidv
}

"19. Use Reader on every article - and bug 93 (Reader's state per tab; control: the published M5 build)"
function Bug93([string]$name, [string]$exe = "") {
    if ($exe) {
        $line = & .\launch-quiet.ps1 -Profile $name -Fresh -Port $Port -Exe $exe -SettingsJson ($BASE + '}')
        $script:pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }; Start-Sleep 4
    } else { Launch $name | Out-Null }
    $a = @(Pages)[0].Substring(7, 8)
    node cdp.js nav $a "http://127.0.0.1:8797/article" | Out-Null; Start-Sleep 2
    U -Id PageSettingsButton | Out-Null; Start-Sleep 1; U -Name "Show Reader" | Out-Null; Start-Sleep 1.5
    U -Id NewTabButton | Out-Null; Start-Sleep 2
    $b = @(Pages | Where-Object { $_.Substring(7, 8) -ne $a })[0].Substring(7, 8)
    node cdp.js nav $b "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 2
    U -Name "exp article" -Action Toggle | Out-Null; Start-Sleep 1.5
    U -Id PageSettingsButton | Out-Null; Start-Sleep 1.2
    $offer = Offered
    U -Name $offer 2>&1 | Out-Null; Start-Sleep 1.5
    $left = (InReader $a) -eq 'false'
    StopAviary $script:pidv
    @{ offer = $offer; left = $left }
}
$r = Bug93 "xp19b"
Check "bug 93: back on a tab in Reader after another tab loaded, its menu offers Hide Reader - and it works" ($r.offer -eq 'Hide Reader' -and $r.left) "offered $($r.offer), left Reader: $($r.left)"
if ((Test-Path $m5) -and (Get-Item $m5).VersionInfo.ProductVersion -like "*0e8d432*") {
    $r = Bug93 "xp19bc" (Resolve-Path $m5).Path
    Check "control: the published M5 build offered Show Reader, and the page stayed in Reader" ($r.offer -eq 'Show Reader' -and -not $r.left) "offered $($r.offer), left Reader: $($r.left)"
}
foreach ($run in @(@('xp19c', ''), @('xp19', ',"exp.autoReader":"true"'))) {
    $on = [bool]$run[1]; $label = if ($on) { 'on' } else { 'control' }
    Launch $run[0] $run[1] | Out-Null
    $a = @(Pages)[0].Substring(7, 8)
    node cdp.js nav $a "http://127.0.0.1:8797/article" | Out-Null; Start-Sleep 2.5
    Check "${label}: an article $(if ($on) { 'opens in Reader by itself' } else { 'opens as the site made it' })" ((InReader $a) -eq $(if ($on) { 'true' } else { 'false' })) (InReader $a)
    node cdp.js nav $a "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 2
    Check "${label}: a page that is not an article does not" ((InReader $a) -eq 'false') (InReader $a)
    # An article opened BEHIND with Ctrl+click: in Reader when it first comes to the front.
    node cdp.js eval $a "(function(){var l=document.createElement('a');l.id='toart';l.href='/article?bg=1';l.textContent='article';document.body.appendChild(l);return true})()" | Out-Null
    node cdp.js mclick $a "#toart" ctrl | Out-Null; Start-Sleep 3
    $bg = Tab 'article?bg=1'
    U -Name "exp article" -Action Toggle | Out-Null; Start-Sleep 2
    Check "${label}: an article opened behind $(if ($on) { 'is in Reader when you switch to it' } else { 'is not' })" ($bg -and (InReader $bg) -eq $(if ($on) { 'true' } else { 'false' })) "tab $bg, in Reader: $(if ($bg) { InReader $bg })"
    if ($on) {
        U -Id PageSettingsButton | Out-Null; Start-Sleep 1
        $t = Tree
        Check "on: the Aa menu's per-site item says every site already is" ($t -match 'Use Reader Automatically on Every Site \(Experimental\)') ([regex]::Match($t, 'Use Reader Automatically[^\r\n]*').Value)
        U -Name "Hide Reader" | Out-Null; Start-Sleep 1.5
        Check "on: and Hide Reader shows the page the way the site made it" ((InReader $bg) -eq 'false') (InReader $bg)
    }
    StopAviary $script:pidv
}

"21. Pop a playing video out when you switch tabs - control: off"
function VideoState([string]$t) { (Val $t "state()") -replace '\\"', '"' | ConvertFrom-Json }
Launch "xp21c" | Out-Null
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/video" | Out-Null; Start-Sleep 1.5
node cdp.js eval $t0 "start()" | Out-Null; Start-Sleep 1
U -Id NewTabButton | Out-Null; Start-Sleep 2.5
$s = VideoState $t0
Check "control: switching away leaves the video playing unseen in its tab" (-not $s.pip -and $s.playing) ($s | ConvertTo-Json -Compress)
StopAviary $script:pidv
Launch "xp21" ',"exp.videoPopOut":"true","exp.muteBackground":"true"' | Out-Null
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/video" | Out-Null; Start-Sleep 1.5
node cdp.js eval $t0 "start()" | Out-Null; Start-Sleep 1
U -Id NewTabButton | Out-Null; Start-Sleep 2.5
$s = VideoState $t0; $v = Peak
Check "on: switching away pops the video out, still playing" ($s.pip -and $s.playing) ($s | ConvertTo-Json -Compress)
Check "and with Mute tabs in the background also on, it keeps its sound" ($v -gt 0.005) $v
U -Name "exp video" -Action Toggle | Out-Null; Start-Sleep 2
$s = VideoState $t0
Check "going back puts it back into the page, still playing" (-not $s.pip -and $s.playing) ($s | ConvertTo-Json -Compress)
foreach ($case in @(@('muted=1', 'a muted video'), @('nopip=1', 'a video the site bars from picture in picture'))) {
    node cdp.js nav $t0 "http://127.0.0.1:8797/video?$($case[0])" | Out-Null; Start-Sleep 1.5
    node cdp.js eval $t0 "start()" | Out-Null; Start-Sleep 1
    U -Id NewTabButton | Out-Null; Start-Sleep 2.5
    $s = VideoState $t0
    Check "$($case[1]) is left where it is" (-not $s.pip) ($s | ConvertTo-Json -Compress)
    U -Name "exp video" -Action Toggle | Out-Null; Start-Sleep 1
}
node cdp.js nav $t0 "http://127.0.0.1:8797/video" | Out-Null; Start-Sleep 1.5
node cdp.js eval $t0 "start().then(function(){document.getElementById('v').pause();return true})" | Out-Null; Start-Sleep 1
U -Id NewTabButton | Out-Null; Start-Sleep 2.5
$s = VideoState $t0
Check "so is a paused one" (-not $s.pip) ($s | ConvertTo-Json -Compress)
StopAviary $script:pidv

"22. Show a preview when you point at a tab - control: off (the picture itself is checked, not only that one was made)"
$env:AVIARY_PREVIEW_TEST = "1"
foreach ($run in @(@('xp22c', ''), @('xp22', ',"exp.tabPreview":"true"'))) {
    $on = [bool]$run[1]
    Launch $run[0] $run[1] | Out-Null
    $t0 = @(Pages)[0].Substring(7, 8)
    node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 1.5
    node cdp.js eval $t0 "(function(){document.body.style.background='#c0392b';return true})()" | Out-Null; Start-Sleep 0.8
    U -Id NewTabButton | Out-Null; Start-Sleep 3
    $lines = @(Get-Content "profiles\$($run[0])\preview-test.txt" -ErrorAction SilentlyContinue)
    if (-not $on) { Check "control: no picture is taken" ($lines.Count -eq 0) ($lines -join ' | '); StopAviary $script:pidv; continue }
    Check "on: the tab left behind is photographed" ($lines.Count -ge 1 -and $lines[0] -match '8797/white') ($lines -join ' | ')
    $jpg = @(Get-ChildItem "profiles\xp22\preview-*.jpg" -ErrorAction SilentlyContinue)[0]
    $mean = if ($jpg) { $b = [System.Drawing.Bitmap]::FromFile($jpg.FullName); $p = $b.GetPixel([int]($b.Width * 0.75), [int]($b.Height / 2)); $b.Dispose(); $p } else { $null }
    Check "and the picture is the page - red, as it was painted just before (not a blank frame)" ($mean -and $mean.R -gt 150 -and $mean.G -lt 100 -and $mean.B -lt 100) "$($mean.R),$($mean.G),$($mean.B)"
    $ms = @(Select-String -Path "profiles\xp22\user.data.improvementcollection\perf.jsonl" -Pattern '"k":"tab-preview"' | ForEach-Object { [regex]::Match($_.Line, '"ms":([\d.]+)').Groups[1].Value })
    Check "it takes well under half a second ($($ms -join ', ') ms)" ($ms.Count -ge 1 -and [double]$ms[0] -lt 500) ($ms -join ', ')
    StopAviary $script:pidv
}
$env:AVIARY_PREVIEW_TEST = $null

"23. Close tabs you have not looked at in a week (a 'week' of 6 s, AVIARY_TABAGE_TEST) - control: off"
$env:AVIARY_TABAGE_TEST = "6"
foreach ($run in @(@('xp23c', ''), @('xp23', ',"exp.closeOldTabs":"true"'))) {
    $on = [bool]$run[1]; $label = if ($on) { 'on' } else { 'control' }
    Launch $run[0] ($run[1] + ',"pinnedTabs":"[\"http://127.0.0.1:8797/long\"]"') | Out-Null
    $s0 = Tab 'Aviary'; if (-not $s0) { $s0 = @(Pages | Where-Object { $_ -notmatch '8797/long' })[0].Substring(7, 8) }
    node cdp.js nav $s0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 1.5
    U -Id NewTabButton | Out-Null; Start-Sleep 2
    $c = @(Pages | Where-Object { $_ -notmatch '8797/(long|white)' })[0].Substring(7, 8)
    node cdp.js nav $c "http://127.0.0.1:8797/tone" | Out-Null; Start-Sleep 1.5
    node cdp.js eval $c "play()" | Out-Null
    U -Id NewTabButton | Out-Null; Start-Sleep 2
    $b = @(Pages | Where-Object { $_ -notmatch '8797/(long|white|tone)' })[0].Substring(7, 8)
    node cdp.js nav $b "http://127.0.0.1:8797/raf" | Out-Null
    Start-Sleep 12
    $open = (Pages) -join ' '
    if ($on) {
        Check "on: the tab left 'a week' ago closed" ($open -notmatch '8797/white') $open
        Check "on: the pinned tab, the one playing sound and the one in front are all still open" ($open -match '8797/long' -and $open -match '8797/tone' -and $open -match '8797/raf') $open
        $ev = @(Select-String -Path "profiles\xp23\user.data.improvementcollection\events.jsonl" -Pattern 'closed-old-tab')
        Check "on: exactly one closed, and diagnostics says so" ($ev.Count -eq 1) "$($ev.Count) lines"
    } else {
        Check "control: nothing closed" ($open -match '8797/white' -and $open -match '8797/tone') $open
    }
    StopAviary $script:pidv
}
$env:AVIARY_TABAGE_TEST = $null

"24. Tint the toolbar to match the page - control: off"
# The strip is sampled at x=12, inside its own padding: x<8 is the window's invisible resize border,
# which PrintWindow paints black (25 Sep - the first check sampled x=4 and "failed").
function ToolbarPixel { $f = Join-Path $kit "shots\xp24.png"; Remove-Item $f -ErrorAction SilentlyContinue
    & .\peek.ps1 -ProcessId $script:pidv -Out $f | Out-Null
    if (-not (Test-Path $f)) { return $null }
    $b = [System.Drawing.Bitmap]::FromFile($f); $p = $b.GetPixel(220, 24); $q = $b.GetPixel(12, 68); $b.Dispose(); @($p, $q) }
function Near($px, [int]$r, [int]$g, [int]$b) { $px -and [Math]::Abs($px.R - $r) -le 14 -and [Math]::Abs($px.G - $g) -le 14 -and [Math]::Abs($px.B - $b) -le 14 }
$env:AVIARY_TINT_TEST = "1"
Launch "xp24c" | Out-Null
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/tint?c=%230a7d4b" | Out-Null; Start-Sleep 2.5
$px = ToolbarPixel
Check "control: nothing painted, the toolbar keeps its own colour" ((TintNow "xp24c") -eq '(never painted)' -and -not (Near $px[0] 10 125 75)) "$(TintNow 'xp24c'); pixel $($px[0].R),$($px[0].G),$($px[0].B)"
StopAviary $script:pidv
Launch "xp24" ',"exp.tintToolbar":"true","theme":"Light"' | Out-Null
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/tint?c=%230a7d4b" | Out-Null; Start-Sleep 2.5
$px = ToolbarPixel
Check "on: a page that chooses #0a7d4b gets it, with light words on it" ((TintNow "xp24") -eq '#0a7d4b Dark') (TintNow "xp24")
Check "drawn behind the toolbar AND the tab strip, out to the edges" ((Near $px[0] 10 125 75) -and (Near $px[1] 10 125 75)) "toolbar $($px[0].R),$($px[0].G),$($px[0].B); strip $($px[1].R),$($px[1].G),$($px[1].B)"
node cdp.js nav $t0 "http://127.0.0.1:8797/tint?c=%23f5e6c8" | Out-Null; Start-Sleep 2.5
Check "a pale colour gets dark words" ((TintNow "xp24") -eq '#f5e6c8 Light') (TintNow "xp24")
node cdp.js nav $t0 "http://127.0.0.1:8797/tint-scheme" | Out-Null; Start-Sleep 2.5
Check "a page with one colour per scheme gets the light one in the light theme" ((TintNow "xp24") -eq '#e0f0ff Light') (TintNow "xp24")
node cdp.js nav $t0 "http://127.0.0.1:8797/tint-app" | Out-Null; Start-Sleep 4
Check "a web app that changes colour and address without loading is followed" ((TintNow "xp24") -eq '#aa2200 Dark') (TintNow "xp24")
node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 2.5
$px = ToolbarPixel
Check "a page that chooses no colour: the tint goes" ((TintNow "xp24") -eq 'none' -and -not (Near $px[0] 170 34 0)) "$(TintNow 'xp24'); pixel $($px[0].R),$($px[0].G),$($px[0].B)"
node cdp.js nav $t0 "http://127.0.0.1:8797/tint?c=%230a7d4b&article=1" | Out-Null; Start-Sleep 2.5
U -Id PageSettingsButton | Out-Null; Start-Sleep 1; U -Name "Show Reader" | Out-Null; Start-Sleep 1.5
Check "Reader drops it (Reader has its own look)" ((TintNow "xp24") -eq 'none') (TintNow "xp24")
OpenSettingsAt "experimental"
Check "Aviary's own pages have none" ((TintNow "xp24") -eq 'none') (TintNow "xp24")
StopAviary $script:pidv
Launch "xp24d" ',"exp.tintToolbar":"true","exp.darkWebsites":"true"' | Out-Null
$t0 = @(Pages)[0].Substring(7, 8)
node cdp.js nav $t0 "http://127.0.0.1:8797/tint?c=%230a7d4b" | Out-Null; Start-Sleep 2.5
Check "with Make every website dark on, a darkened page is left untinted" ((TintNow "xp24d") -in @('none', '(never painted)')) (TintNow "xp24d")
StopAviary $script:pidv
$env:AVIARY_TINT_TEST = $null

# ---------------------------------------------------------------------------------------------
if ($Restart) {
    "11. Restart Aviary: a new Aviary, with the change in its engine"
    & .\guard.ps1 | Out-Null
    if ($LASTEXITCODE -eq 3) { "  SKIP  guard.ps1: someone is using the machine" }
    else {
        Launch "xp11" | Out-Null
        $old = $script:pidv
        OpenSettingsAt "experimental"
        node cdp.js click $script:sid ".sw[data-key='exp.smoothScrollOff']" | Out-Null; Start-Sleep 1
        node cdp.js click $script:sid ".note[data-restart='exp.smoothScrollOff'] .btn[data-cmd=restart]" | Out-Null; Start-Sleep 1.5
        $t = Tree
        Check "Restart Aviary? is asked, saying what comes back" ($t -match 'Restart Aviary\?' -and $t -match 'Pinned tabs and tab groups') "no question"
        U -Name "Restart" | Out-Null
        Check "the old Aviary closes" (WaitGone $old 20) "still running"
        $new = $null; $d = (Get-Date).AddSeconds(25)
        while ((Get-Date) -lt $d -and -not $new) {
            Start-Sleep 1
            $new = Get-CimInstance Win32_Process -Filter "Name='Aviary.exe'" | Where-Object { $_.ProcessId -ne $old -and $_.ExecutablePath -like "*tools\test\pub*" } | Select-Object -First 1
        }
        Check "a new Aviary starts by itself" ($null -ne $new) "none"
        if ($new) {
            $script:pidv = [int]$new.ProcessId
            Start-Sleep 6
            $cl = Engine
            Check "its engine was started WITH the change" ($cl -match '--disable-smooth-scrolling') $cl
            Check "on the same scratch profile" ((Get-Content "profiles\xp11\experiments-safety.json" -Raw | ConvertFrom-Json).Running -eq $true) "marker not running"
            $p = Probe
            Check "and Page Down jumps there" ($p.scroll.distinct -le 2) ($p.scroll | ConvertTo-Json -Compress)
            $m = Marker "xp11"
            Check "the restart was a NORMAL close (not blamed as a crash)" ((Tree) -notmatch 'Aviary switched off') "asked"
            StopAviary $script:pidv
        }
    }

    # Stage 3's three that need the real keyboard or pointer - each asks guard.ps1 first.
    "20. Switch to a tab that is already open - a real Enter in the address bar (control: off)"
    foreach ($run in @(@('xp20c', ''), @('xp20', ',"exp.switchToTab":"true"'))) {
        & .\guard.ps1 | Out-Null
        if ($LASTEXITCODE -eq 3) { "  SKIP  guard.ps1: someone is using the machine"; break }
        $on = [bool]$run[1]
        Launch $run[0] $run[1] | Out-Null
        $a = @(Pages)[0].Substring(7, 8)
        node cdp.js nav $a "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 1.5
        U -Id NewTabButton | Out-Null; Start-Sleep 2
        $h = [long][XW]::Biggest($script:pidv)
        & .\type-hwnd.ps1 -Hwnd $h -Text "^l" -WaitMs 600 | Out-Null
        $sent = & .\type-hwnd.ps1 -Hwnd $h -Text "127.0.0.1:8797/white{ENTER}" -WaitMs 2500
        $p = @(Pages); $whites = @($p | Where-Object { $_ -match '8797/white' })
        if ($on) {
            Check "on: Enter went to the tab that had it - no second copy" ($whites.Count -eq 1 -and "$sent" -match 'sent') "$($whites.Count) tabs with it; $sent"
            Check "and the new, empty tab it was typed into closed" ($p.Count -eq 1) ($p -join ' | ')
            Check "and that tab is the one in front" ((Val $a "document.visibilityState") -eq 'visible') (Val $a "document.visibilityState")
        } else {
            Check "control: Enter opened the page a second time" ($whites.Count -eq 2 -and "$sent" -match 'sent') "$($whites.Count) tabs with it; $sent"
        }
        StopAviary $script:pidv
    }

    "22b. the preview shows under a real pointer resting on the tab"
    $env:AVIARY_PREVIEW_TEST = "1"
    Launch "xp22h" ',"exp.tabPreview":"true"' | Out-Null
    $env:AVIARY_PREVIEW_TEST = $null
    $t0 = @(Pages)[0].Substring(7, 8)
    node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 1.5
    node cdp.js eval $t0 "(function(){document.body.style.background='#c0392b';return true})()" | Out-Null; Start-Sleep 0.8
    U -Id NewTabButton | Out-Null; Start-Sleep 3
    $tip = Join-Path $kit "shots\xp22-tip.png"; Remove-Item $tip -ErrorAction SilentlyContinue
    $said = & .\pointer.ps1 -ProcessId $script:pidv -Name "exp white" -Action Hover -Out $tip
    if ("$said" -match 'REFUSED') { "  SKIP  guard.ps1: someone is using the machine" }
    else {
        $reds = 0
        if (Test-Path $tip) {
            $b = [System.Drawing.Bitmap]::FromFile($tip)
            for ($x = 0; $x -lt $b.Width; $x += 4) { for ($y = 0; $y -lt $b.Height; $y += 4) { $c = $b.GetPixel($x, $y); if ($c.R -gt 150 -and $c.G -lt 90 -and $c.B -lt 90) { $reds++ } } }
            $b.Dispose()
        }
        Check "a tooltip opens, and it holds the page's picture (red), not only its name" ("$said" -match 'POPUP' -and $reds -gt 200) "$said; red samples $reds"
    }
    StopAviary $script:pidv

    "25. Double-click below your tabs for a new tab - tabs down the side (control: off)"
    foreach ($run in @(@('xp25c', ''), @('xp25', ',"exp.dblClickNewTab":"true"'))) {
        $on = [bool]$run[1]
        Launch $run[0] ($run[1] + ',"tabLayout":"Sidebar","sidebarVisible":"true"') | Out-Null
        $t0 = @(Pages)[0].Substring(7, 8)
        node cdp.js nav $t0 "http://127.0.0.1:8797/white" | Out-Null; Start-Sleep 1.5
        $before = @(Pages).Count
        $said = & .\pointer.ps1 -ProcessId $script:pidv -Name "exp white" -Action DoubleClick -Below 120
        if ("$said" -match 'REFUSED|NO ELEMENT') { "  SKIP  $said"; StopAviary $script:pidv; break }
        Start-Sleep 1.5
        $after = @(Pages).Count
        if ($on) { Check "on: a double-click in the empty space below the tabs opens a new tab" ($after -eq $before + 1) "$before -> $after ($said)" }
        else { Check "control: the same double-click opens nothing" ($after -eq $before) "$before -> $after ($said)" }
        if ($on) {
            $before = @(Pages).Count
            [void](& .\pointer.ps1 -ProcessId $script:pidv -Name "exp white" -Action DoubleClick)
            Start-Sleep 1.5
            Check "on: a double-click ON a tab does not" (@(Pages).Count -eq $before) "$before -> $(@(Pages).Count)"
        }
        StopAviary $script:pidv
    }
}

""
"experiments-test: $pass passed, $fail failed"
