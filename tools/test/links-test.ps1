# Where a modified click on a link goes (Alpha Build 5 M5 stage 3). No desktop input: DevTools
# sends TRUSTED clicks with modifiers or the middle button (cdp.js mclick) - what the engine works a
# link click's meaning out from - and UI Automation presses Aviary's own menus.
#
# Measured before stage 3 (24/25 Sep): Ctrl and middle opened a tab IN FRONT, Shift a tab rather than
# a window; Ctrl+Shift, Alt (download) and a web app's own Ctrl+click were already right.
#
#   links-test.ps1 [-Port 9388]
param([int]$Port = 9388, [string]$Exe = "")

$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}
try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8795/links" -TimeoutSec 2 | Out-Null }
catch { Start-Process node -ArgumentList "linksite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }

Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class LinkWins { delegate bool EP(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
 public static string[] Titles(uint pid) { var l = new List<string>(); EnumWindows(delegate(IntPtr h, IntPtr x){ uint p; GetWindowThreadProcessId(h, out p);
   if (p==pid && IsWindowVisible(h)) { var c=new StringBuilder(200); GetClassName(h,c,200); if (c.ToString()=="WinUIDesktopWin32WindowClass") { var t=new StringBuilder(300); GetWindowText(h,t,300); l.Add(t.ToString()); } } return true; }, IntPtr.Zero); return l.ToArray(); } }
'@ -ErrorAction SilentlyContinue

$script:pidv = 0; $script:profile = ""; $script:files = ""
function Launch([string]$name, [string]$extra = "") {
    $script:profile = $name
    $script:files = Join-Path $kit "profiles\$name-files"
    if (Test-Path $script:files) { Remove-Item $script:files -Recurse -Force }
    New-Item -ItemType Directory -Force $script:files | Out-Null
    $seed = '{"improvementCollection":"true","startNews":"false","listsAutoUpdate":"Off","downloadDir":' + (ConvertTo-Json $script:files) + $extra + '}'
    & .\launch-quiet.ps1 -Profile $name -Fresh -Port $Port -SettingsJson $seed -Exe $Exe | Out-Null
    $script:pidv = [int](Get-Content pid.txt)
    Start-Sleep 4
}
function Quit { Stop-Process -Id $script:pidv -Force -ErrorAction SilentlyContinue; Start-Sleep 3 }
function Page([string]$tab = "#0") { node cdp.js nav $tab "http://127.0.0.1:8795/links" | Out-Null; Start-Sleep 1.5 }
function Click([string]$sel, [string]$how) { node cdp.js mclick "link probe" $sel $how | Out-Null; Start-Sleep 2 }
function Windows { @([LinkWins]::Titles([uint32]$script:pidv)) }
function Front { (Windows | Where-Object { $_ -notmatch 'Private' } | Select-Object -First 1) }
function Pages { @(node cdp.js list | Where-Object { $_ -match '^page' }) }
function Events($pattern) { $e = "profiles\$script:profile\user.data.improvementcollection\events.jsonl"; if (Test-Path $e) { @(Select-String -Path $e -Pattern $pattern) } else { @() } }
function U { param($Id = "", $Name = "", $Action = "Invoke") & .\uia.ps1 -ProcessId $script:pidv -Id $Id -Name $Name -Action $Action }

"`n1. Ctrl+click: a tab BEHIND this one, next to it, in order"
Launch "lk1"
Page
Click "#plain" "ctrl"
Check "a new tab opened" (@(Pages | Where-Object { $_ -match 'dest\?n=plain' }).Count -eq 1) ((Pages) -join ' | ')
Check "and the page you were on stays in front" ((Front) -match '^link probe') (Front)
$o = Events '"reason":"link opened a background tab"' | Select-Object -Last 1
Check "right after it (index 1)" ($o.Line -match '"index":1,') $o.Line
Click "#host >>> #shadow" "ctrl"
$o = Events '"reason":"link opened a background tab"' | Select-Object -Last 1
Check "a link inside a shadow root works too, and goes AFTER the first (index 2)" ($o.Line -match '"index":2,' -and @(Pages | Where-Object { $_ -match 'n=shadow' }).Count -eq 1) $o.Line
Check "still in front: the page you were on" ((Front) -match '^link probe') (Front)
Quit

"`n2. middle-click: behind too"
Launch "lk2"
Page
Click "#plain" "middle"
Check "a new tab opened" (@(Pages | Where-Object { $_ -match 'dest\?n=plain' }).Count -eq 1) ((Pages) -join ' | ')
Check "behind" ((Front) -match '^link probe') (Front)
Quit

"`n3. Ctrl+Shift+click: a tab in FRONT (control - it was already right)"
Launch "lk3"
Page
Click "#plain" "ctrl+shift"
Check "the new tab is in front" ((Front) -match '^dest plain') (Front)
Quit

"`n4. Shift+click: a WINDOW of its own - one tab, no pinned tabs, and the pinned list is left alone"
Launch "lk4" ',"pinnedTabs":"[\"http://127.0.0.1:8795/dest?n=pinned\"]"'
Page "about:blank"   # the Start Page tab - the pinned tab is open beside it
$pinnedBefore = (Get-Content "profiles\lk4\settings.json" -Raw | ConvertFrom-Json).pinnedTabs
$pagesBefore = @(Pages).Count
Click "#plain" "shift"
Start-Sleep 1.5
$w = Windows
Check "a second window opened" ($w.Count -eq 2) ($w -join ' || ')
Check "showing the link" (@($w | Where-Object { $_ -match '^dest plain' }).Count -eq 1) ($w -join ' || ')
Check "and the first window still shows the page you were on" (@($w | Where-Object { $_ -match '^link probe' }).Count -eq 1) ($w -join ' || ')
Check "exactly ONE tab more in all - no Start Page, no second copy of the pinned tab" (@(Pages).Count -eq $pagesBefore + 1 -and @(Pages | Where-Object { $_ -match 'n=pinned' }).Count -eq 1) ((Pages) -join ' | ')
Check "the saved pinned list is untouched" ((Get-Content "profiles\lk4\settings.json" -Raw | ConvertFrom-Json).pinnedTabs -eq $pinnedBefore) ""
Quit

"`n5. Alt+click: the linked file is DOWNLOADED (Safari's Option-click - the engine already did it)"
Launch "lk5"
Page
$pagesBefore = @(Pages).Count
Click "#file" "alt"
Start-Sleep 1
Check "the file is saved" ((Get-Item (Join-Path $script:files "file.txt") -ErrorAction SilentlyContinue).Length -gt 0) ((Get-ChildItem $script:files | ForEach-Object Name) -join ', ')
Check "into the download list" (((U -Action Dump) -join "`n") -match 'DownloadsButton') ""
Check "and no tab or window opened" (@(Pages).Count -eq $pagesBefore -and (Windows).Count -eq 1) ((Pages) -join ' | ')
Click "#cross" "alt"
Start-Sleep 1
Check "a file on ANOTHER site as well" (@(Get-ChildItem $script:files -Filter "file*.txt").Count -eq 2) ((Get-ChildItem $script:files | ForEach-Object Name) -join ', ')
Quit

"`n6. the controls: an ordinary target=_blank link, and a web app's own Ctrl+click"
Launch "lk6"
Page
node cdp.js click "link probe" "#blank" | Out-Null; Start-Sleep 2
Check "a plain click on target=_blank still opens in front" ((Front) -match '^dest blank') (Front)
Quit
Launch "lk6b"
Page
$pagesBefore = @(Pages).Count
Click "#app" "ctrl"
$log = node cdp.js eval "link probe" "document.getElementById('log').textContent"
Check "a page that handles its own Ctrl+click keeps it - nothing opens" (@(Pages).Count -eq $pagesBefore -and $log -match 'the page handled ctrl') "$log; pages $pagesBefore -> $(@(Pages).Count)"
Quit

"`n7. a website cannot choose where its links open (the hint carries the launch token)"
Launch "lk7"
Page
node cdp.js eval "link probe" "chrome.webview.postMessage(JSON.stringify({type:'linkhint',how:'window',url:location.origin+'/dest?n=blank'}));1" | Out-Null
node cdp.js click "link probe" "#blank" | Out-Null; Start-Sleep 2.5
Check "a forged 'open it in a window' is ignored - the link opens as a tab" ((Windows).Count -eq 1 -and (Front) -match '^dest blank') ((Windows) -join ' || ')
Quit

"`n8. the right-click menu: Open Link in New Tab goes BEHIND; Open Link in New Window"
Launch "lk8"
Page
node cdp.js rclick "link probe" "#plain" | Out-Null; Start-Sleep 1.2
U -Name "Open Link in New Tab" | Out-Null; Start-Sleep 2
Check "Open Link in New Tab opens it" (@(Pages | Where-Object { $_ -match 'dest\?n=plain' }).Count -eq 1) ((Pages) -join ' | ')
Check "behind the page you were on" ((Front) -match '^link probe') (Front)
node cdp.js rclick "link probe" "#host >>> #shadow" | Out-Null; Start-Sleep 1.2
U -Name "Open Link in New Window" | Out-Null; Start-Sleep 3
Check "Open Link in New Window opens a window with it" ((Windows).Count -eq 2 -and @(Windows | Where-Object { $_ -match '^dest shadow' }).Count -eq 1) ((Windows) -join ' || ')
Quit

"`n9. a private window's Shift+click opens another PRIVATE window"
Launch "lk9"
$pagesBefore = (node cdp.js list) -join "`n"
U -Id MenuButton | Out-Null; Start-Sleep 1
U -Name "New Private Window" | Out-Null; Start-Sleep 3
$priv = (node cdp.js list | Where-Object { $_ -match '^page' } | ForEach-Object { $_.Substring(7, 8) } | Where-Object { $pagesBefore -notmatch $_ } | Select-Object -First 1)
node cdp.js nav $priv "http://127.0.0.1:8795/links" | Out-Null; Start-Sleep 1.5
node cdp.js mclick $priv "#plain" "shift" | Out-Null; Start-Sleep 3
$w = Windows
Check "a third window" ($w.Count -eq 3) ($w -join ' || ')
Check "and it is private" (@($w | Where-Object { $_ -match '^dest plain' -and $_ -match 'Private' }).Count -eq 1) ($w -join ' || ')
Quit

"`n10. Ctrl+click on a DOWNLOAD: listed, and no empty tab left behind"
Launch "lk10"
Page
$pagesBefore = @(Pages).Count
Click "#att" "ctrl"
Start-Sleep 2
Check "the file is saved" ((Get-Item (Join-Path $script:files "attached.bin") -ErrorAction SilentlyContinue).Length -eq 3000) ((Get-ChildItem $script:files | ForEach-Object Name) -join ', ')
Check "and the background tab it needed has gone" (@(Pages).Count -eq $pagesBefore) ("$pagesBefore -> " + @(Pages).Count)
Quit

"`n--- nothing should have gone wrong ---"
foreach ($p in @('lk1', 'lk2', 'lk3', 'lk4', 'lk5', 'lk6', 'lk6b', 'lk7', 'lk8', 'lk9', 'lk10')) {
    $iss = "profiles\$p\user.data.improvementcollection\issues.jsonl"
    if (Test-Path $iss) {
        foreach ($line in Get-Content $iss) {
            if ($line -match 'unclean-exit') { continue }
            elseif ($line -match '"ui-stall"' -and $line -match 'ui menu') { "  (known, not new) $p opening a window stalled: " + ($line -replace '.*"ms":([\d.]+).*', '$1 ms') }
            elseif ($p -eq 'lk9' -and $line -match '"ui-stall"') { "  (known, not new) lk9 a private window opening stalled: " + ($line -replace '.*"ms":([d.]+).*', '$1 ms') + " (a private window records none of its own clicks, so the stall cannot name them)" }
            elseif ($line -match '"ui-stall"' -and $line -match 'link-open') { "  (known, not new) $p opening the link's window stalled: " + ($line -replace '.*"ms":([\d.]+).*', '$1 ms') + " (any new window: 56-105 ms, the same before M5)" }
            elseif ($line -match '"ui-stall"' -and $line -match 'download start') { "  (noted) $p a first download stalled: " + ($line -replace '.*"ms":([\d.]+).*', '$1 ms') + " - seen once in 11 first downloads on 25 Sep, not reproduced in 10 more" }
            else { "  UNEXPECTED $p : $($line.Substring(0, [Math]::Min(230, $line.Length)))" }
        }
    }
}
"(every profile force-closed on purpose, so each one's unclean-exit line is expected and not shown)"
"`n$pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
