# The Experimental menu, end to end (Alpha Build 5 M6 stage 1). No desktop input unless -Restart:
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
function Kill($id) { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue; [void](WaitGone $id 10); Start-Sleep 2 }
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
    if (-not $gone) { Kill $script:pidv }
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
"1. the pane: the promise, the labels, three rows in their groups, one button for everything"
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
Check "three switches, each with a badge and a What might happen line" ($sh.rows -eq 3 -and $sh.badges -eq 3 -and $sh.might -eq 3) $shape
Check "in their groups: Speed and memory, Looks, Troubleshooting" (($sh.groups -join '|') -eq 'Speed and memory|Looks|Troubleshooting') ($sh.groups -join '|')
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
$after = @('exp.smoothScrollOff', 'exp.darkWebsites', 'exp.noVideoDecode' | ForEach-Object { Setting "xp1" $_ })
Check "every exp.* in settings.json is false" (($after -join ',') -eq 'false,false,false') ($after -join ',')
$drawn = S "[].map.call(document.querySelectorAll('.sw[data-key^=\'exp.\']'),function(s){return s.getAttribute('aria-checked')}).join(',')"
Check "every switch is drawn off at once" ($drawn -match '^"?false,false,false"?$') $drawn
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
Kill $script:pidv
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
Kill $script:pidv
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
Kill $script:pidv

"6. settings.json cannot put a switch on the engine"
Launch "xp6" ',"exp.darkWebsites":"--disable-web-security","exp.noVideoDecode":"yes","exp.smoothScrollOff":"true --no-sandbox","exp.evil":"true"' | Out-Null
$cl = Engine
Check "no value from settings.json reaches the engine" ($cl -and $cl -notmatch 'disable-web-security|no-sandbox') $cl
Check "and a value that is not exactly true switches nothing on" ($cl -notmatch 'WebContentsForceDark|disable-accelerated-video-decode|disable-smooth-scrolling') $cl
Kill $script:pidv

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
Kill $script:pidv
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
Kill $script:pidv

"9. the other ways a run goes wrong count too"
Launch "xp9a" ',"exp.smoothScrollOff":"true"' -crash "ui" | Out-Null
Start-Sleep 5
Check "(a UI-thread failure: Aviary survived it)" (Alive $script:pidv) "gone"
[void](CloseClean)
$m = Marker "xp9a"
Check "a survived failure is kept in the marker even after a normal close" ($m.Problem -match 'failed and recovered' -and -not $m.Running) ($m | ConvertTo-Json -Compress)
Launch "xp9a" -Keep | Out-Null
Check "and the next launch switches it off" ((Setting "xp9a" 'exp.smoothScrollOff') -eq 'false' -and (Tree) -match 'part of it failed and recovered') (Setting "xp9a" 'exp.smoothScrollOff')
Kill $script:pidv
Launch "xp9b" ',"exp.smoothScrollOff":"true"' | Out-Null
& .\kill-engine.ps1 -ProcessId $script:pidv | Out-Null; Start-Sleep 3
[void](CloseClean)
Launch "xp9b" -Keep | Out-Null
Check "the engine dying counts: switched off, and it says the engine stopped" ((Setting "xp9b" 'exp.smoothScrollOff') -eq 'false' -and (Tree) -match 'browser engine stopped unexpectedly') (Setting "xp9b" 'exp.smoothScrollOff')
Kill $script:pidv
Launch "xp9c" ',"exp.smoothScrollOff":"true","improvementCollection":"false"' -crash "fatal" | Out-Null
[void](WaitGone $script:pidv 20)
Launch "xp9c" -Keep | Out-Null
Check "with data collection OFF it works just the same" ((Setting "xp9c" 'exp.smoothScrollOff') -eq 'false' -and (Tree) -match 'Aviary switched off') (Setting "xp9c" 'exp.smoothScrollOff')
Check "(and no diagnostics folder was needed for it)" (-not (Test-Path "profiles\xp9c\user.data.improvementcollection\events.jsonl")) "folder exists"
Kill $script:pidv

"10. 'soon' has an end: two normal runs, and a crash is no longer blamed on them (controls)"
Launch "xp10" ',"exp.noVideoDecode":"true"' | Out-Null; [void](CloseClean)
Launch "xp10" -Keep | Out-Null; Kill $script:pidv
Launch "xp10" -Keep | Out-Null
Check "after ONE normal run, a crash still switches them off" ((Setting "xp10" 'exp.noVideoDecode') -eq 'false') (Setting "xp10" 'exp.noVideoDecode')
Kill $script:pidv
Launch "xp10b" ',"exp.noVideoDecode":"true"' | Out-Null; [void](CloseClean)
Launch "xp10b" -Keep | Out-Null; [void](CloseClean)
Check "(two normal runs counted)" ((Marker "xp10b").CleanRuns -eq 2) ((Marker "xp10b") | ConvertTo-Json -Compress)
Launch "xp10b" -Keep | Out-Null; Kill $script:pidv
Launch "xp10b" -Keep | Out-Null
Check "after TWO normal runs, a crash leaves them on, and nothing is asked" ((Setting "xp10b" 'exp.noVideoDecode') -eq 'true' -and (Tree) -notmatch 'Aviary switched off') (Setting "xp10b" 'exp.noVideoDecode')
Check "(and the engine has it)" ((Engine) -match 'disable-accelerated-video-decode') (Engine)
Kill $script:pidv
Launch "xp10c" | Out-Null; Kill $script:pidv
Launch "xp10c" -Keep | Out-Null
Check "a crash with NO experiments on asks nothing" ((Tree) -notmatch 'Aviary switched off') "asked"
Kill $script:pidv

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
            Kill $script:pidv
        }
    }
}

""
"experiments-test: $pass passed, $fail failed"
