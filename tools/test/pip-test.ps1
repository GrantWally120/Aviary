# Picture in picture: does the menu item appear only when it can work, and does it work? (M4.9.)
#
# pipsite.js makes a real playing video out of a canvas stream - no file, no network - so the whole
# thing is testable offline. No desktop input at all: cdp.js rclick sends a TRUSTED right-click
# through the DevTools protocol, which is what raises Aviary's own context menu, and uia.ps1 presses
# the item. The proof that picture in picture really happened is a separate top-level OS window
# called "Picture in picture", owned by the WebView2 browser process - not by Aviary.
#
#   pip-test.ps1 [-Profile pip] [-Port 9343]
param([string]$Profile = "pip", [int]$Port = 9343)

$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}

Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices; using System.Collections.Generic;
public class PipWindows {
  delegate bool EP(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EP f, IntPtr l);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  public static int Count() {
    int n = 0;
    EnumWindows(delegate(IntPtr h, IntPtr l) {
      if (!IsWindowVisible(h)) return true;
      var t = new StringBuilder(300); GetWindowText(h, t, 300);
      if (t.ToString() == "Picture in picture") n++;
      return true;
    }, IntPtr.Zero);
    return n;
  }
}
'@ -ErrorAction SilentlyContinue

try { Invoke-WebRequest -UseBasicParsing "http://localhost:8792/" -TimeoutSec 2 | Out-Null }
catch {
    Start-Process node -ArgumentList "pipsite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null
    Start-Sleep -Seconds 2
}

& .\launch-quiet.ps1 -Profile $Profile -Fresh -Port $Port `
    -SettingsJson '{"improvementCollection":"true","startNews":"false","listsAutoUpdate":"Off"}' | Out-Null
$pidv = [int](Get-Content pid.txt)
"test instance $pidv"
Start-Sleep -Seconds 4
node cdp.js nav "#0" "http://localhost:8792/" | Out-Null
Start-Sleep -Seconds 4
function Menu { & .\uia.ps1 -ProcessId $pidv -Action Dump | Select-String "MenuItem" | ForEach-Object { ($_ -split '\s{2,}')[-1] } }
function Esc { & .\type-hwnd.ps1 -Hwnd ([long](Get-Process -Id $pidv).MainWindowHandle) -Text "{ESC}" -WaitMs 500 | Out-Null }
function State { node cdp.js eval "pip probe" "pipState()" | ConvertFrom-Json | ConvertFrom-Json }

"`n0. the engine, before anything is asked of it"
$caps = node cdp.js eval "pip probe" "JSON.stringify({v:document.pictureInPictureEnabled,d:typeof window.documentPictureInPicture})" | ConvertFrom-Json
Check "the engine does video picture in picture" ($caps -match '"v":true') $caps
Check "the engine does document picture in picture" ($caps -match '"d":"object"') $caps
$s = State
Check "the probe's video is playing" ($s.playing -and $s.readyState -ge 1) $s

"`n1. right-clicking a video offers it, alongside the page's own items"
node cdp.js rclick "pip probe" "#v" | Out-Null
Start-Sleep -Seconds 2
$m = Menu
Check "the item is there" ($m -contains "Picture in Picture") ($m -join ', ')
Check "the page's items are still there" ($m -contains "Reload" -and ($m -join ',') -match 'Find in Page') ($m -join ', ')

"`n2. pressing it really opens a picture-in-picture window"
$before = [PipWindows]::Count()
& .\uia.ps1 -ProcessId $pidv -Name "Picture in Picture" -Action Invoke | Out-Null
Start-Sleep -Seconds 3
$s = State
Check "the page says it is in picture in picture" ($s.inPip) $s
Check "an OS window appeared" ([PipWindows]::Count() -gt $before) ("$before -> " + [PipWindows]::Count())

"`n3. and the item turns round to take it back"
node cdp.js rclick "pip probe" "#v" | Out-Null
Start-Sleep -Seconds 2
$m = Menu
Check "it now says Leave" ($m -contains "Leave Picture in Picture") ($m -join ', ')
& .\uia.ps1 -ProcessId $pidv -Name "Leave Picture in Picture" -Action Invoke | Out-Null
Start-Sleep -Seconds 3
$s = State
Check "the page is out again" (-not $s.inPip) $s
Check "the window closed" ([PipWindows]::Count() -eq $before) ([PipWindows]::Count())

"`n4. ordinary right-clicks are untouched"
node cdp.js rclick "pip probe" "h1" | Out-Null
Start-Sleep -Milliseconds 1500
$m = Menu
Check "no item on plain page text" (-not ($m -contains "Picture in Picture")) ($m -join ', ')
Check "the page menu is intact" ($m -contains "Back" -and $m -contains "Add Bookmark") ($m -join ', ')
Esc

"`n5. a video the site refuses gets no dead item - and keeps its page menu"
node cdp.js eval "pip probe" "(function(){document.getElementById('v').disablePictureInPicture=true;return true;})()" | Out-Null
node cdp.js rclick "pip probe" "#v" | Out-Null
Start-Sleep -Seconds 2
$m = Menu
Check "no picture-in-picture item" (-not ($m -join ',' -match 'Picture in Picture')) ($m -join ', ')
Check "the page menu is still offered" ($m -contains "Reload") ($m -join ', ')
Esc

"`n--- nothing should have gone wrong ---"
$iss = "profiles\$Profile\user.data.improvementcollection\issues.jsonl"
if (Test-Path $iss) { Get-Content $iss | ForEach-Object { "  UNEXPECTED: $_" } } else { "  no issues.jsonl" }
"`n--- what it recorded ---"
Select-String -Path "profiles\$Profile\user.data.improvementcollection\events.jsonl" -Pattern "picture-in-picture" |
    ForEach-Object { "  " + $_.Line }
"`n$pass passed, $fail failed"
Stop-Process -Id $pidv -Force -ErrorAction SilentlyContinue
if ($fail -gt 0) { exit 1 }
