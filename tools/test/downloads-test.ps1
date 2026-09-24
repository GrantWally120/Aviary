# Downloads, end to end (Alpha Build 5 M5 stage 1). No desktop input: DevTools clicks start the
# downloads, UI Automation presses the ring, the rows and the dialogs, PrintWindow reads the ring.
#
# Every download goes to a scratch folder seeded as downloadDir - never Grant's Downloads folder.
# AVIARY_OPEN_TEST=1 makes "Open" record what it would open instead of running it.
#
#   downloads-test.ps1 [-Port 9371] [-Picker]
#
# -Picker also drives the real Windows folder picker for Location -> Change..., which needs the
# keyboard, so guard.ps1 decides whether it runs (the file picker's rules - HANDOFF §25).
param([int]$Port = 9371, [switch]$Picker, [switch]$PickerOnly, [switch]$Stage2)
if ($PickerOnly) { $Picker = $true }

$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}

try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8793/page" -TimeoutSec 2 | Out-Null }
catch { Start-Process node -ArgumentList "dlsite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }

Add-Type -AssemblyName System.Drawing
$script:pidv = 0
$script:profile = ""
$script:files = ""

function Launch([string]$name, [string]$extra = "", [switch]$Keep) {
    $script:profile = $name
    $script:files = Join-Path $kit "profiles\$name-files"
    if (-not $Keep) {
        if (Test-Path $script:files) { Remove-Item $script:files -Recurse -Force }
        New-Item -ItemType Directory -Force $script:files | Out-Null
    }
    $seed = '{"improvementCollection":"true","startNews":"false","listsAutoUpdate":"Off","downloadDir":' + (ConvertTo-Json $script:files) + $extra + '}'
    $env:AVIARY_OPEN_TEST = "1"
    if ($Keep) {
        $json = Get-Content "profiles\$name\settings.json" -Raw | ConvertFrom-Json
        & .\launch-quiet.ps1 -Profile $name -Port $Port | Out-Null
    } else {
        & .\launch-quiet.ps1 -Profile $name -Fresh -Port $Port -SettingsJson $seed | Out-Null
    }
    $env:AVIARY_OPEN_TEST = $null
    $script:pidv = [int](Get-Content pid.txt)
    Start-Sleep 4
}
function Quit {
    Stop-Process -Id $script:pidv -Force -ErrorAction SilentlyContinue
    Start-Sleep 3
}
function U { param($Id = "", $Name = "", $Action = "Invoke", $Value = "", $Window = "") & .\uia.ps1 -ProcessId $script:pidv -Id $Id -Name $Name -Action $Action -Value $Value -Window $Window }
function Tree { (U -Action Dump) -join "`n" }
function Rows { U -Action Dump | Select-String "ListItem" | ForEach-Object { ($_.Line.Trim() -replace '^ListItem\s+', '') } }
function PopoverOpen { (Tree) -match 'Clear downloads' }
function OpenPopover { if (-not (PopoverOpen)) { U -Id DownloadsButton | Out-Null; Start-Sleep -Milliseconds 900 } }
function ClosePopover { if (PopoverOpen) { U -Id DownloadsButton | Out-Null; Start-Sleep -Milliseconds 700 } }
function Record($file) { (Get-Content "profiles\$script:profile\downloads.json" -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json) | Where-Object FileName -eq $file | Select-Object -First 1 }
function Size($file) { $f = Get-Item (Join-Path $script:files $file) -ErrorAction SilentlyContinue; if ($f) { $f.Length } else { -1 } }
function Events($pattern) { $e = "profiles\$script:profile\user.data.improvementcollection\events.jsonl"; if (Test-Path $e) { @(Select-String -Path $e -Pattern $pattern) } else { @() } }
function Visible { (Tree) -match 'DownloadsButton' }
function Tabs { @(node cdp.js list | Where-Object { $_ -match '^page' }) }

# How many accent-blue pixels the toolbar's right end shows: the ring's arc, and nothing else there is blue.
function ArcPixels {
    $shot = Join-Path $kit "shots\dl-ring.png"
    & .\peek.ps1 -ProcessId $script:pidv -Out $shot | Out-Null
    $b = [System.Drawing.Bitmap]::FromFile($shot); $n = 0
    for ($x = $b.Width - 320; $x -lt $b.Width - 140; $x++) { for ($y = 8; $y -lt 56; $y++) {
        # An anti-aliased 1.5 px arc is mostly blends of the accent and the toolbar: "clearly blue",
        # not "exactly the accent". The toolbar's grey and the white glyphs have B close to R.
        $c = $b.GetPixel($x, $y); if ($c.B -gt 140 -and $c.B -gt $c.R + 80 -and $c.B -gt $c.G + 25) { $n++ } } }
    $b.Dispose(); $n
}
function Page { node cdp.js nav "#0" "http://127.0.0.1:8793/page" | Out-Null; Start-Sleep 2 }

# Bug 89: small visible windows owned by the ENGINE's processes - where Edge's own Downloads panel
# appears. A capture of Aviary's window cannot show it; only listing the engine's windows can.
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class EngineWin { delegate bool EP(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [StructLayout(LayoutKind.Sequential)] public struct RC { public int L,T,R,B; }
 [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RC r);
 public static int Small(uint[] pids) { var set = new HashSet<uint>(pids); int n = 0;
  EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p); RC r;
   if (set.Contains(p) && IsWindowVisible(h) && GetWindowRect(h, out r) && (r.R-r.L) < 500 && (r.B-r.T) < 300 && (r.R-r.L) > 100) n++; return true; }, IntPtr.Zero); return n; } }
'@ -ErrorAction SilentlyContinue
function EnginePanels {
    $engine = @(Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.ParentProcessId -eq $script:pidv } | ForEach-Object { [uint32]$_.ProcessId })
    if ($engine.Count -eq 0) { return 0 }
    [EngineWin]::Small([uint32[]]$engine)
}
# Window helpers for the two steps that type into Windows' own dialogs (folder picker, Save window).
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class FolderDlg {
  [StructLayout(LayoutKind.Sequential)] public struct RC { public int L,T,R,B; }
  delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr p);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] static extern void mouse_event(uint f, uint x, uint y, uint d, IntPtr e);
  public static IntPtr Find() { IntPtr found = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { if (!IsWindowVisible(h)) return true;
      var c = new StringBuilder(200); GetClassName(h, c, 200); if (c.ToString() == "#32770") { found = h; return false; } return true; }, IntPtr.Zero);
    return found; }
  public static void Click(int x, int y) { SetCursorPos(x, y); mouse_event(0x0002,0,0,0,IntPtr.Zero); mouse_event(0x0004,0,0,0,IntPtr.Zero); }
  public static bool Front(IntPtr h) { IntPtr fg = GetForegroundWindow(); uint them = GetWindowThreadProcessId(fg, IntPtr.Zero), me = GetCurrentThreadId();
    AttachThreadInput(them, me, true); BringWindowToTop(h); SetForegroundWindow(h); AttachThreadInput(them, me, false);
    System.Threading.Thread.Sleep(700); return GetForegroundWindow() == h; }
}
'@ -ErrorAction SilentlyContinue
function Get([string]$css) { node cdp.js click "dl probe" $css | Out-Null }
function WaitFor($file, $bytes, $seconds) { for ($i = 0; $i -lt $seconds * 2; $i++) { if ((Size $file) -eq $bytes) { return $true }; Start-Sleep -Milliseconds 500 }; $false }

if (-not $PickerOnly -and -not $Stage2) {
# ════════════════════════════════════════════════════════════════════════════
"`n1. the button: absent with nothing in the list, and a ring while something downloads"
Launch "dlt"
Page
Check "no button on a fresh profile" (-not (Visible)) (Visible)
$idle = ArcPixels
Get "#slow"
Start-Sleep 4
Check "the button appears when a download starts" (Visible) (Visible)
$mid = ArcPixels
Check "the ring draws an arc while it downloads (control: none before)" ($mid -gt 6 -and $idle -eq 0) "before=$idle during=$mid"
OpenPopover
$r = Rows
Check "the row says how far along it is" (($r -join '|') -match 'slow\.zip, [\d.]+ MB of 6 MB — ') ($r -join ' | ')
Check "and offers Stop" ((Tree) -match 'Stop slow\.zip') ""
ClosePopover
Check "the file lands in the chosen folder" (WaitFor "slow.zip" 6000000 14) (Size "slow.zip")
Start-Sleep 1
$done = ArcPixels
Check "the arc goes when the last download lands" ($done -eq 0) $done
OpenPopover
$r = Rows
Check "the row now says its size" (($r -join '|') -match 'slow\.zip, 6 MB') ($r -join ' | ')
Check "and offers Show in Folder" ((Tree) -match 'Show slow\.zip in Folder') ""
ClosePopover
$rec = Record "slow.zip"
Check "downloads.json keeps it, completed" ($rec.State -eq 'Completed' -and $rec.ReceivedBytes -eq 6000000) ($rec | ConvertTo-Json -Compress)

"`n2. a target=_blank download: its tab goes at once, and the download does NOT go with it"
$pagesBefore = (Tabs) -join "`n"
$panelsBefore = EnginePanels
Get "#blank"
Start-Sleep 2
$parkedPage = (Tabs | ForEach-Object { $_.Substring(7, 8) } | Where-Object { $pagesBefore -notmatch $_ -and (node cdp.js list | Select-String $_) -notmatch 'downloads-hub' } | Select-Object -First 1)
Check "the new tab left the strip as its download began" ((Events '"download-tab-closed"').Count -eq 1) (Events '"download-tab-closed"').Count
Check "it was parked, not closed" ((Events '"tab-parked"').Count -eq 1) (Events '"tab-parked"').Count
Check "bug 89: no Edge Downloads panel on screen (the engine opened one; it was closed)" ((EnginePanels) -eq $panelsBefore -and (Events 'engine-download-panel-closed').Count -ge 1) ("panels $panelsBefore -> " + (EnginePanels) + ", closed events " + (Events 'engine-download-panel-closed').Count)
Check "the download finishes anyway (a closed WebView kills it - measured)" (WaitFor "newtab.zip" 3000000 12) (Size "newtab.zip")
for ($i = 0; $i -lt 20 -and $parkedPage -and ((Tabs) -join ' ') -match $parkedPage; $i++) { Start-Sleep -Milliseconds 500 }
Check "and the parked tab is let go afterwards, its engine with it" ((Events '"tab-unparked"').Count -eq 1 -and $parkedPage -and -not (((Tabs) -join ' ') -match $parkedPage)) ("unparked " + (Events '"tab-unparked"').Count + ", page $parkedPage still listed: " + (((Tabs) -join ' ') -match "$parkedPage"))

"`n3. a dropped connection: Chromium reconnects by itself, and it stays ONE download"
# Measured on this engine: the reconnect arrives as a brand-new download (new operation, new name)
# while the old operation goes quiet for ever, InProgress with an interrupt reason set.
Get "#drop"
Start-Sleep 0.5
OpenPopover
# Sampled four times a second through the drop and the reconnect: never two rows for one download.
$most = 0; $seen = @()
for ($i = 0; $i -lt 16; $i++) { $d = @(Rows | Where-Object { $_ -match '^dropped' }); if ($d.Count -gt $most) { $most = $d.Count }; $seen += ($d -join ' / '); Start-Sleep -Milliseconds 250 }
Check "through the drop and the reconnect it is only ever one row" ($most -eq 1) (($seen | Select-Object -Unique) -join ' || ')
Check "the reconnect was recognised as the same download" ((Events '"reconnected"').Count -ge 1) (Events '"reconnected"').Count
ClosePopover
$landed = $false
for ($i = 0; $i -lt 20 -and -not $landed; $i++) { Start-Sleep -Milliseconds 500; $landed = @(Get-ChildItem $script:files -Filter "dropped*.zip" | Where-Object Length -eq 4000000).Count -eq 1 }
Check "the file lands whole" $landed ((Get-ChildItem $script:files -Filter "dropped*" | ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ', ')
$ranged = @((Invoke-RestMethod http://127.0.0.1:8793/hits) | Where-Object { $_.url -match 'dropped' -and $_.range -match 'bytes=1' })
Check "by asking for the rest, not the whole file again" ($ranged.Count -ge 1) ($ranged | ConvertTo-Json -Compress)
OpenPopover
$r = @(Rows | Where-Object { $_ -match '^dropped' })
Check "and the list shows ONE row for it, finished (not a second row stuck for ever)" ($r.Count -eq 1 -and $r[0] -match ', 4 MB$') ($r -join ' | ')
ClosePopover
Check "the ring is not left spinning for a download that ended" ((ArcPixels) -eq 0) ""

"`n4. Stop, then Download Again (no ranges: fetched afresh in a background tab that goes)"
Get "#slow"
Start-Sleep 2
OpenPopover
U -Name "Stop slow (1).zip" | Out-Null
Start-Sleep 1.5
$r = Rows
Check "Stop stops it" (($r -join '|') -match 'slow \(1\)\.zip, Stopped') ($r -join ' | ')
$tabsBefore = (Tabs).Count
U -Name "Resume slow (1).zip" | Out-Null
Start-Sleep 3
ClosePopover
Check "Download Again fetches it" ((Events '"download-resume".*"again"').Count -eq 1) (Events '"download-resume"').Count
$again = @(Get-ChildItem $script:files -Filter "slow*.zip" | Where-Object Length -eq 6000000)
for ($i = 0; $i -lt 24 -and $again.Count -lt 2; $i++) { Start-Sleep -Milliseconds 500; $again = @(Get-ChildItem $script:files -Filter "slow*.zip" | Where-Object Length -eq 6000000) }
Check "and it lands complete" ($again.Count -ge 2) (($again | ForEach-Object Name) -join ', ')
Start-Sleep 1
Check "without leaving a tab behind" ((Tabs).Count -eq $tabsBefore) ("$tabsBefore -> " + (Tabs).Count)

"`n5. programs ask before opening; other files do not"
Get "#program"
Start-Sleep 2
Get "#quick"
Start-Sleep 2
OpenPopover
U -Name "setup.exe, 512 bytes" | Out-Null
Start-Sleep 1.5
$t = Tree
Check "opening setup.exe asks first" ($t -match 'Open “setup\.exe”\?') ""
U -Name "Cancel" | Out-Null
Start-Sleep 1
$o = Events '"download-open"' | Select-Object -Last 1
Check "Cancel opens nothing" ($o.Line -match '"asked":true' -and $o.Line -match '"opened":false') $o.Line
OpenPopover
U -Name "quick.txt, 20 KB" | Out-Null
Start-Sleep 1.5
$o = Events '"download-open"' | Select-Object -Last 1
Check "a text file opens without a question (control)" ($o.Line -match '"program":false' -and $o.Line -match '"asked":false' -and $o.Line -match '"opened":true') $o.Line
Check "and no dialog appeared for it" (-not ((Tree) -match 'Open “quick')) ""

"`n6. Clear takes finished rows, keeps a running one, and never touches a file"
Get "#slow"
Start-Sleep 2
OpenPopover
U -Name "Clear downloads" | Out-Null
Start-Sleep 1
$r = Rows
Check "only the running download is left" (@($r).Count -eq 1 -and ($r -join '') -match 'slow.*\.zip, .* of 6 MB') ($r -join ' | ')
Check "every file is still on disk" ((Size "setup.exe") -eq 512 -and (Size "quick.txt") -eq 20000 -and (Size "slow.zip") -eq 6000000) "setup=$(Size 'setup.exe') quick=$(Size 'quick.txt')"
ClosePopover
Start-Sleep 12

"`n7. closing the window while a download runs asks first"
Get "#slow"
Start-Sleep 2
$h = (Get-Process -Id $script:pidv).MainWindowHandle
Add-Type @'
using System; using System.Runtime.InteropServices;
public class WClose { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
'@ -ErrorAction SilentlyContinue
[void][WClose]::PostMessage([IntPtr]$h, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero)
Start-Sleep 1.5
Check "the question is asked" ((Tree) -match 'download is still in progress') ""
U -Name "Cancel" | Out-Null
Start-Sleep 1
Check "Cancel keeps Aviary open" ($null -ne (Get-Process -Id $script:pidv -ErrorAction SilentlyContinue)) ""
Start-Sleep 12

"`n8. the list survives a restart; a download cut off by quitting says so"
Get "#slow"
Start-Sleep 2
Quit
Launch "dlt" -Keep
Page
OpenPopover
$r = Rows
# Section 6 cleared everything finished before it; what finished after it must still be here.
Check "the rows are still there after a restart" (@($r | Where-Object { $_ -match 'slow.*\.zip, 6 MB$' }).Count -ge 2) ($r -join ' | ')
Check "the one Aviary's end cut off says so" (($r -join '|') -match 'slow[^|]*\.zip, Stopped — Aviary was closed') ($r -join ' | ')
ClosePopover
Quit

"`n9. Remove download list items - Safari's four rules"
# After one day: a row that finished two days ago is not drawn; one from an hour ago is (control).
New-Item -ItemType Directory -Force "profiles\dlr" | Out-Null
$old = [DateTimeOffset]::UtcNow.AddDays(-2).ToString("o"); $new = [DateTimeOffset]::UtcNow.AddHours(-1).ToString("o")
function Planted($a, $b) { "[{`"Id`":`"a`",`"FileName`":`"old.zip`",`"FilePath`":`"C:\\nowhere\\old.zip`",`"Url`":`"http://x/old`",`"State`":`"Completed`",`"ReceivedBytes`":1,`"StartedUtc`":`"$a`",`"FinishedUtc`":`"$a`"},{`"Id`":`"b`",`"FileName`":`"new.zip`",`"FilePath`":`"C:\\nowhere\\new.zip`",`"Url`":`"http://x/new`",`"State`":`"Completed`",`"ReceivedBytes`":1,`"StartedUtc`":`"$b`",`"FinishedUtc`":`"$b`"}]" }
foreach ($rule in @('After one day', 'Manually')) {
    Launch "dlr" (',"downloadHistory":"' + $rule + '"')
    Quit
    [IO.File]::WriteAllText((Join-Path $kit "profiles\dlr\downloads.json"), (Planted $old $new))
    Launch "dlr" -Keep
    OpenPopover
    $r = (Rows) -join ' | '
    if ($rule -eq 'After one day') { Check "After one day: a two-day-old row is gone, an hour-old one stays" ($r -notmatch 'old\.zip' -and $r -match 'new\.zip') $r }
    else { Check "Manually: both stay (control)" ($r -match 'old\.zip' -and $r -match 'new\.zip') $r }
    ClosePopover
    Quit
}
# Upon successful download: the row leaves the moment it lands, and the button with it.
Launch "dls" ',"downloadHistory":"Upon successful download"'
Page
Get "#quick"
Start-Sleep 2.5
Check "Upon successful download: gone as soon as it lands" (-not (Visible) -and (Size "quick.txt") -eq 20000) ("button " + (Visible) + ", file " + (Size "quick.txt"))
Quit
# When Aviary quits: there until Aviary closes, gone at the next launch.
Launch "dlq" ',"downloadHistory":"When Aviary quits"'
Page
Get "#quick"
Start-Sleep 2.5
Check "When Aviary quits: still listed while it runs" (Visible) ""
Stop-Process -Id $script:pidv -Force; Start-Sleep 3   # a crash: the quit rule never ran...
Launch "dlq" -Keep
Check "...and it still runs at the next launch" (-not (Visible)) (Visible)
Quit

"`n10. a private window's downloads are its own, and never written down"
Launch "dlp"
$normalPages = (node cdp.js list) -join "`n"
U -Id MenuButton | Out-Null; Start-Sleep 1
U -Name "New Private Window" | Out-Null; Start-Sleep 3
# The private window's page is the one that did not exist before it opened.
$priv = (node cdp.js list | Where-Object { $_ -match '^page' } | ForEach-Object { $_.Substring(7, 8) } | Where-Object { $normalPages -notmatch $_ } | Select-Object -First 1)
node cdp.js nav $priv "http://127.0.0.1:8793/page" | Out-Null
Start-Sleep 2
node cdp.js click $priv "#quick" | Out-Null
Start-Sleep 2.5
Check "the file itself is kept" ((Size "quick.txt") -eq 20000) (Size "quick.txt")
$json = Get-Content "profiles\dlp\downloads.json" -Raw -ErrorAction SilentlyContinue
Check "downloads.json never hears of it" (-not $json -or $json -notmatch 'quick') $json
Check "the private window shows its download" (((U -Action Dump -Window "Private") -join "`n") -match 'DownloadsButton') ""
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class Titles { delegate bool EP(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
 public static string Normal(uint pid) { string found = ""; EnumWindows(delegate(IntPtr h, IntPtr l){ uint p; GetWindowThreadProcessId(h, out p);
   if (p == pid && IsWindowVisible(h)) { var t = new StringBuilder(300); GetWindowText(h, t, 300); var s = t.ToString();
     if (s.EndsWith("Aviary") && !s.Contains("Private")) found = s; } return true; }, IntPtr.Zero); return found; } }
'@ -ErrorAction SilentlyContinue
$normalTitle = [Titles]::Normal([uint32]$script:pidv)
Check "the normal window does not (control)" ($normalTitle.Length -gt 0 -and -not (((U -Action Dump -Window $normalTitle) -join "`n") -match 'DownloadsButton')) $normalTitle
Quit

}   # end of sections 1-10

if ($Picker -and -not $Stage2) {
    "`n11. Settings -> Downloads -> Location -> Change... (the real Windows folder picker)"
    & .\guard.ps1 | Out-Null
    if ($LASTEXITCODE -ne 0) { "  SKIPPED - someone is using the machine, and this one needs the keyboard" }
    else {
        Launch "dlf"
        $target = Join-Path $kit "profiles\dlf-chosen"
        if (Test-Path $target) { Remove-Item $target -Recurse -Force }
        New-Item -ItemType Directory -Force $target | Out-Null
        Add-Type -AssemblyName System.Windows.Forms

        node cdp.js key "#0" "ctrl+Comma" | Out-Null; Start-Sleep 2
        node cdp.js click "Settings" "nav button[data-target=down]" | Out-Null; Start-Sleep 1
        node cdp.js click "Settings" "[data-cmd=downloadDir]" | Out-Null; Start-Sleep 3
        $dlg = [FolderDlg]::Find()
        Check "the folder picker opened" ($dlg -ne [IntPtr]::Zero) $dlg
        # A dialog found is not yet a dialog ready for typing: it lays itself out after it appears.
        Start-Sleep 1.5
        for ($try = 0; $try -lt 2 -and [FolderDlg]::Find() -ne [IntPtr]::Zero; $try++) {
            $dlg = [FolderDlg]::Find()
            if (-not [FolderDlg]::Front($dlg)) { break }
            $r = New-Object FolderDlg+RC; [void][FolderDlg]::GetWindowRect($dlg, [ref]$r)
            [FolderDlg]::Click(($r.L + [int](($r.R - $r.L) * 0.48)), ($r.B - 68))   # the Folder: box
            Start-Sleep -Milliseconds 600
            [System.Windows.Forms.SendKeys]::SendWait("^a")
            [System.Windows.Forms.SendKeys]::SendWait($target)
            Start-Sleep -Milliseconds 600
            [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
            Start-Sleep 2
            # Enter on a typed path NAVIGATES there (and does nothing when the picker already opened
            # in it - it remembers the last folder chosen). Choosing is the Select Folder button,
            # pressed through UI Automation on the dialog itself: the dialog belongs to PickerHost,
            # not to Aviary, so it is found by its window, not by uia.ps1 -ProcessId.
            if ([FolderDlg]::Find() -ne [IntPtr]::Zero) {
                # "Select Folder" is a DirectUI element with no Invoke pattern (measured), so it is
                # clicked - bottom right, 179 px in from the right and 39 up from the bottom.
                $r = New-Object FolderDlg+RC; [void][FolderDlg]::GetWindowRect([FolderDlg]::Find(), [ref]$r)
                [FolderDlg]::Click(($r.R - 179), ($r.B - 39))
                Start-Sleep 2
            }
            if ([FolderDlg]::Find() -ne [IntPtr]::Zero) { "  (the picker is still open - trying once more)" }
        }
        if ($true) {
            Set-Content -Path (Join-Path $kit "lastinput.txt") -Value ([uint32]([Environment]::TickCount -band 0xFFFFFFFF))
            Check "the picker closed" ([FolderDlg]::Find() -eq [IntPtr]::Zero) ""
            $saved = (Get-Content "profiles\dlf\settings.json" -Raw | ConvertFrom-Json).downloadDir
            Check "the setting holds the chosen folder" ($saved -eq $target) $saved
            $shown = node cdp.js eval "Settings" "document.getElementById('dlpath').textContent"
            Check "Settings shows it at once" ($shown -match 'dlf-chosen') $shown
            Page
            Get "#quick"
            Start-Sleep 2.5
            Check "and the next download lands there" ((Get-Item (Join-Path $target "quick.txt") -ErrorAction SilentlyContinue).Length -eq 20000) (Get-ChildItem $target | ForEach-Object Name)
        }
        Quit
    }
}

# ════════════════════════════════════════════════════════════════════════════
# M5 stage 2 - "Ask where to save each file". The Save window is Windows' WinRT picker, hosted by
# PickerHost: found by class #32770, answered Cancel with WM_CLOSE (no input). Only the typed-path
# half needs the keyboard (-Picker, guard.ps1 decides).
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class SaveWin { delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<IntPtr> All(string title) { var l = new List<IntPtr>(); EnumWindows(delegate(IntPtr h, IntPtr x) { if (!IsWindowVisible(h)) return true; var c = new StringBuilder(200); GetClassName(h, c, 200); var t = new StringBuilder(300); GetWindowText(h, t, 300); if (c.ToString() == "#32770" && t.ToString() == title) l.Add(h); return true; }, IntPtr.Zero); return l; }
  public static void CloseFirst(string title) { var l = All(title); if (l.Count > 0) PostMessage(l[0], 0x10, IntPtr.Zero, IntPtr.Zero); } }
'@ -ErrorAction SilentlyContinue
function SaveWindows { @([SaveWin]::All("Save As")).Count }

if (-not $PickerOnly) {
"`n12. Ask where to save each file (stage 2) - asked before, Cancel leaves nothing"
Launch "dla" ',"askDownload":"true"'
Page
Get "#quick"
Start-Sleep 2.5
Check "a Save window comes up before the download begins" ((SaveWindows) -eq 1) (SaveWindows)
Check "and nothing is listed while it waits" (-not (Visible)) (Visible)
[SaveWin]::CloseFirst("Save As"); Start-Sleep 1.5
Check "Cancel: no Save window left" ((SaveWindows) -eq 0) (SaveWindows)
Check "Cancel: no row" (-not (Visible)) (Visible)
Check "Cancel: no file - not even the engine's temporary one" (@(Get-ChildItem $script:files).Count -eq 0) ((Get-ChildItem $script:files | ForEach-Object Name) -join ', ')
# Two downloads from two TABS (two clicks in ONE tab: the second navigation cancels the first
# before it is a download - measured with the setting off too, so it is the engine, not this).
$pages = (node cdp.js list) -join "`n"
U -Id NewTabButton | Out-Null; Start-Sleep 1.5
$tabB = (node cdp.js list | ForEach-Object { $_.Substring(7, 8) } | Where-Object { $pages -notmatch $_ } | Select-Object -First 1)
node cdp.js nav $tabB "http://127.0.0.1:8793/page?b" | Out-Null; Start-Sleep 1.5
$tabA = (node cdp.js list | Where-Object { $_ -match '/page$' } | Select-Object -First 1).Substring(7, 8)
node cdp.js click $tabA "#quick" | Out-Null
node cdp.js click $tabB "#program" | Out-Null
Start-Sleep 2.5
Check "two downloads at once: ONE Save window" ((SaveWindows) -eq 1) (SaveWindows)
[SaveWin]::CloseFirst("Save As"); Start-Sleep 2
Check "then the second, once the first is answered" ((SaveWindows) -eq 1) (SaveWindows)
[SaveWin]::CloseFirst("Save As"); Start-Sleep 1.5
Check "and both were asked" ((Events 'download-save-as').Count -eq 3) (Events 'download-save-as').Count
node cdp.js nav $tabA "http://127.0.0.1:8793/file?name=README&size=1000&rate=100000" | Out-Null; Start-Sleep 2.5
Check "a file with no extension still gets its Save window" ((SaveWindows) -eq 1) (SaveWindows)
[SaveWin]::CloseFirst("Save As"); Start-Sleep 1
Quit
}

if ($Picker) {
    "`n12b. Ask where to save - a name and a folder typed into the real Save window"
    & .\guard.ps1 | Out-Null
    if ($LASTEXITCODE -ne 0) { "  SKIPPED - someone is using the machine, and this one needs the keyboard" }
    else {
        Add-Type -AssemblyName System.Windows.Forms
        Launch "dlb" ',"askDownload":"true"'
        $chosen = Join-Path $kit "profiles\dlb-chosen"
        if (Test-Path $chosen) { Remove-Item $chosen -Recurse -Force }
        New-Item -ItemType Directory -Force $chosen | Out-Null
        function SaveAs([string]$path, [switch]$Replace) {
            Start-Sleep 1.5
            $dlg = [SaveWin]::All("Save As") | Select-Object -First 1
            if (-not $dlg -or -not [FolderDlg]::Front($dlg)) { return $false }
            $r = New-Object FolderDlg+RC; [void][FolderDlg]::GetWindowRect($dlg, [ref]$r)
            [FolderDlg]::Click(($r.L + [int](($r.R - $r.L) * 0.55)), ($r.B - 144))   # the File name box
            Start-Sleep -Milliseconds 500
            [System.Windows.Forms.SendKeys]::SendWait("^a"); [System.Windows.Forms.SendKeys]::SendWait($path)
            Start-Sleep -Milliseconds 500
            [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
            Start-Sleep 1.5
            if ($Replace -and @([SaveWin]::All("Confirm Save As")).Count -gt 0) { [System.Windows.Forms.SendKeys]::SendWait("%y"); Start-Sleep 1.5 }
            Set-Content -Path (Join-Path $kit "lastinput.txt") -Value ([uint32]([Environment]::TickCount -band 0xFFFFFFFF))
            return ((SaveWindows) -eq 0)
        }
        Page
        Get "#quick"
        $ok = SaveAs (Join-Path $chosen "renamed.txt")
        Check "the Save window took the typed name and folder" $ok (SaveWindows)
        Start-Sleep 2
        Check "the file lands THERE, under THAT name" ((Get-Item (Join-Path $chosen "renamed.txt") -ErrorAction SilentlyContinue).Length -eq 20000) ((Get-ChildItem $chosen | ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ', ')
        Check "and not in the usual folder (control)" (@(Get-ChildItem $script:files -Filter "quick*").Count -eq 0) ((Get-ChildItem $script:files | ForEach-Object Name) -join ', ')
        OpenPopover
        Check "the row names the file as saved" (((Rows) -join '|') -match 'renamed\.txt, 20 KB') ((Rows) -join ' | ')
        ClosePopover
        # Saving over a file that exists: Windows asks "Replace?", and Yes must REPLACE it - not
        # leave the old file and write "renamed (1).txt" beside it.
        [IO.File]::WriteAllText((Join-Path $chosen "renamed.txt"), "the old file")
        Get "#quick"
        $ok = SaveAs (Join-Path $chosen "renamed.txt") -Replace
        Start-Sleep 2
        $files2 = (Get-ChildItem $chosen | ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ', '
        Check "Replace: the old file is replaced, and nothing is written beside it" ((Get-Item (Join-Path $chosen "renamed.txt")).Length -eq 20000 -and @(Get-ChildItem $chosen).Count -eq 1) $files2
        Quit
    }
}

"`n--- nothing should have gone wrong ---"
foreach ($p in @('dlt', 'dlr', 'dls', 'dlq', 'dlp', 'dlf', 'dla', 'dlb')) {
    $iss = "profiles\$p\user.data.improvementcollection\issues.jsonl"
    if (Test-Path $iss) {
        foreach ($line in Get-Content $iss) {
            if ($line -match 'unclean-exit') { "  (expected) $p unclean exit - the test force-closes" }
            # Measured on the build before M5 too (105 ms, 24 Sep): opening a private window costs this.
            # Named here so it is seen, not hidden; it is an M7 item, not a downloads one.
            elseif ($p -eq 'dlp' -and $line -match '"ui-stall"' -and $line -match 'ui menu') { "  (known, not new) $p opening the private window stalled: " + ($line -replace '.*"ms":([\d.]+).*', '$1 ms') }
            else { "  UNEXPECTED $p : $($line.Substring(0, [Math]::Min(220, $line.Length)))" }
        }
    }
}
"`n$pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
