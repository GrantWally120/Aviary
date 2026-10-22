# The smoke test for a PUBLISHED copy (25 Sep 2026, after M5's publish). Runs publish/Aviary's own
# Aviary.exe - not the test copy - on a fresh SCRATCH profile, so it proves the folder that was
# shipped starts and works: the files are all there, the Collection is bundled, the build is the one
# that was committed. No desktop input: DevTools, UI Automation and PrintWindow only.
#
#   publish-smoke.ps1 [-Exe <path to Aviary.exe>] [-Port 9333] [-Sha <expected commit>]
#
# Everything downloads into profiles/<name>-files. Closes its instance the way a person would
# (CloseMainWindow), so the scratch profile's diagnostics must say the session ENDED.
param([string]$Exe = "", [int]$Port = 9333, [string]$Sha = "", [string]$Profile = "smokePub")

$kit = $PSScriptRoot
Set-Location $kit
if (-not $Exe) { $Exe = (Resolve-Path (Join-Path $kit "..\..\publish\Aviary\Aviary.exe")).Path }
if (-not $Sha) { $Sha = (git -C $kit rev-parse HEAD).Trim() }
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}
try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8793/page" -TimeoutSec 2 | Out-Null }
catch { Start-Process node -ArgumentList "dlsite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }

"== the copy under test: $Exe"
$ver = (Get-Item $Exe).VersionInfo.ProductVersion
Check "its version stamp is the commit expected ($($Sha.Substring(0,7)))" ($ver -like "*+$Sha") $ver

$files = Join-Path $kit "profiles\$Profile-files"
if (Test-Path $files) { Remove-Item $files -Recurse -Force }
New-Item -ItemType Directory -Force $files | Out-Null
$seed = '{"improvementCollection":"true","startNews":"false","listsAutoUpdate":"Off","downloadDir":' + (ConvertTo-Json $files) + '}'
# The PID comes from the launcher's own line, not pid.txt: that file is shared by every harness,
# and on 25 Sep another session's harness was launching instances at the same time.
$line = & .\launch-quiet.ps1 -Profile $Profile -Fresh -Port $Port -SettingsJson $seed -Exe $Exe
$line
$pidv = if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
Start-Sleep 4
$proc = Get-Process -Id $pidv -ErrorAction SilentlyContinue
Check "it starts and stays up" ($null -ne $proc) "no process"
Check "the process is the published exe, not the test copy" ($proc.Path -eq $Exe) $proc.Path
function U { param($Id = "", $Name = "", $Action = "Invoke", $Value = "") & .\uia.ps1 -ProcessId $pidv -Id $Id -Name $Name -Action $Action -Value $Value }
# Wrap the CALL in @(): a function returning a one-item list returns the item (HANDOFF 26).
function Pages { node cdp.js list | Where-Object { $_ -match '^page' } }

"== the first tab"
$p = @(Pages)
Check "one page open at launch" ($p.Count -eq 1) ($p -join ' | ')
$first = $p[0].Substring(7, 8)
$sp = node cdp.js eval $first "document.body.innerText.length > 50 && !!document.querySelector('*')"
Check "it is a Start Page with something on it" ($sp -eq 'true') $sp

"== a web page"
node cdp.js nav $first "http://127.0.0.1:8793/page" | Out-Null; Start-Sleep 2
$t = node cdp.js eval $first "document.title"
Check "a page loads from a local site" ($t -match 'dl probe') $t

"== the Collection (bundled, offline)"
node cdp.js nav $first "https://collection.aviary/" | Out-Null; Start-Sleep 3
$c = (node cdp.js eval $first "location.href + ' | ' + document.title + ' | ' + document.querySelectorAll('a').length") -join ' '
$links = 0; [void][int]::TryParse(($c -replace '"', '').Split('|')[-1].Trim(), [ref]$links)
Check "https://collection.aviary/ serves its index with links in it" ($c -match 'collection\.aviary' -and $links -gt 20) $c
$pageCount = (Get-ChildItem (Join-Path (Split-Path $Exe) "Library") -Recurse -File -ErrorAction SilentlyContinue).Count
Check "the Collection's files shipped with it" ($pageCount -gt 40) "$pageCount files"

"== a download"
node cdp.js nav $first "http://127.0.0.1:8793/page" | Out-Null; Start-Sleep 2
node cdp.js click $first "#quick" | Out-Null
$deadline = (Get-Date).AddSeconds(15); $got = $null
while ((Get-Date) -lt $deadline) { $got = Get-Item (Join-Path $files "quick.txt") -ErrorAction SilentlyContinue; if ($got -and $got.Length -eq 20000) { break }; Start-Sleep -Milliseconds 500 }
Check "quick.txt lands in the chosen folder, whole (20000 bytes)" ($got -and $got.Length -eq 20000) $(if ($got) { $got.Length } else { 'none' })
Start-Sleep 2
$rec = (Get-Content "profiles\$Profile\downloads.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json) | Where-Object FileName -eq 'quick.txt' | Select-Object -First 1
Check "the download list keeps it" ($null -ne $rec) (Get-Content "profiles\$Profile\downloads.json" -Raw -ErrorAction SilentlyContinue)
Check "the ring is on the toolbar" (((U -Action Dump) -join "`n") -match 'DownloadsButton') "no DownloadsButton"

"== Settings -> Updates"
U -Id MenuButton | Out-Null; Start-Sleep 1
U -Name "Settings" | Out-Null; Start-Sleep 2.5
$set = @(Pages) | Where-Object { $_ -match 'Settings' } | Select-Object -First 1
Check "Settings opens from the app menu" ($null -ne $set) ((Pages) -join ' | ')
if ($set) {
    $sid = $set.Substring(7, 8)
    node cdp.js click $sid "button[data-target=updates]" | Out-Null; Start-Sleep 1
    $u = node cdp.js eval $sid "document.getElementById('pane-updates').innerText"
    # Written for M5's "12 of 15 milestones done"; AB5 shipped on 21 Oct (16 of 16), and a shipped build has no
    # progress bar - its notes say which build it is and that it is the latest. Reads the build, not a count.
    Check "Updates names the shipped build (Alpha Build 5, The Long Run, Latest)" (($u -match 'Alpha Build 5') -and ($u -match 'The Long Run') -and ($u -match 'Latest')) ($u.Substring(0, [Math]::Min(200, $u.Length)))
    node cdp.js click $sid "button[data-target=about]" | Out-Null; Start-Sleep 1
    $a = node cdp.js eval $sid "document.getElementById('ab-version').textContent + ' | ' + document.getElementById('ab-release').textContent"
    "  (About reads: $a)"
}

"== close it as a person would"
# WM_CLOSE to the process's LARGEST visible window: Process.MainWindowHandle can name a 160x28
# helper window (HANDOFF 5), and on 25 Sep CloseMainWindow left this instance running.
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class SmokeClose {
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
[void][SmokeClose]::PostMessage([SmokeClose]::Biggest($pidv), 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
# Two tabs are open (the page and Settings), so "Ask before closing a window with several tabs"
# (on by default) must ask first - found on 25 Sep, when this script took the question for a hang.
Start-Sleep 1.5
$tree = (U -Action Dump) -join "`n"
Check "with two tabs open it asks before closing" ($tree -match 'Close this window\?') "no question"
U -Name "Close 2 Tabs" | Out-Null
$deadline = (Get-Date).AddSeconds(20)
while ((Get-Date) -lt $deadline -and (Get-Process -Id $pidv -ErrorAction SilentlyContinue)) { Start-Sleep -Milliseconds 500 }
$alive = [bool](Get-Process -Id $pidv -ErrorAction SilentlyContinue)
Check "it closes by itself" (-not $alive) "still running"
if ($alive) { Stop-Process -Id $pidv -Force }

"== its own diagnostics (the scratch profile's)"
$d = "profiles\$Profile\user.data.improvementcollection"
$sess = Get-Content "$d\session.json" -Raw -Encoding UTF8 -ErrorAction SilentlyContinue | ConvertFrom-Json
Check "the session says it ended" ($sess -and $sess.ended) ($sess | ConvertTo-Json -Depth 1 -Compress)
$issues = @(Get-Content "$d\issues.jsonl" -ErrorAction SilentlyContinue)
Check "no issues recorded" ($issues.Count -eq 0) ($issues -join "`n")
$crashes = @(Get-ChildItem "$d\crashes" -File -ErrorAction SilentlyContinue)
Check "no crash reports" ($crashes.Count -eq 0) ($crashes.Name -join ', ')

""
"publish-smoke: $pass passed, $fail failed"
