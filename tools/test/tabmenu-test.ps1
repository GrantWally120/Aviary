# A tab's right-click menu, and "Move to Tab Group" in it (M5 stage 4).
#
# DESKTOP INPUT - guard.ps1 must pass (30 s). A tab's menu opens on ContextRequested (right-click,
# long press or the keyboard's menu key - it was RightTapped only until M5), and no DevTools or UI
# Automation call can raise that, so this sends ONE real right-click on the tab. The test window is
# lifted topmost without activating; the cursor goes back and the foreground is handed back.
# The rest is UI Automation: the submenu is expanded through its ExpandCollapse pattern.
#
#   tabmenu-test.ps1 [-Port 9392]
param([int]$Port = 9392)
$kit = $PSScriptRoot
Set-Location $kit
$env:CDP_PORT = "$Port"
& .\guard.ps1 -QuietMs 30000 | Out-Null
if ($LASTEXITCODE -ne 0) { "GUARD: someone is using the PC - not clicking"; exit 3 }
$pass = 0; $fail = 0
function Check($what, $ok, $saw) { if ($ok) { $script:pass++; "  PASS  $what" } else { $script:fail++; "  FAIL  $what  --  saw: $saw" } }
try { Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:8795/links" -TimeoutSec 2 | Out-Null }
catch { Start-Process node -ArgumentList "linksite.js" -WorkingDirectory $kit -WindowStyle Hidden | Out-Null; Start-Sleep 2 }

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Threading;
public static class RClick {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out PT p);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int ht, uint f);
    public struct PT { public int X, Y; }
    public static void Right(int x, int y) {
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        mouse_event(0x8001, x * 65535 / (w - 1), y * 65535 / (h - 1), 0, IntPtr.Zero); Thread.Sleep(150);
        mouse_event(0x8, 0, 0, 0, IntPtr.Zero); Thread.Sleep(40); mouse_event(0x10, 0, 0, 0, IntPtr.Zero);
    }
}
'@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
function Find([string]$name, [int]$pidv) {
    $byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $pidv)
    $byName = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    foreach ($w in $A::RootElement.FindAll($T::Children, $byPid)) { $hit = $w.FindFirst($T::Descendants, $byName); if ($hit) { return $hit } }
    return $null
}
# MENU ITEMS with that name - a MenuFlyoutItem's own text label carries the same name, so a
# plain name count reads 2 for one menu.
function Count([string]$name, [int]$pidv) {
    $n = 0
    $byPid = New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $pidv)
    $byName = New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)), (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)))
    foreach ($w in $A::RootElement.FindAll($T::Children, $byPid)) { $n += $w.FindAll($T::Descendants, $byName).Count }
    return $n
}

$dir = Join-Path $kit "profiles\tgmenu"
if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
New-Item -ItemType Directory -Force $dir | Out-Null
[IO.File]::WriteAllText("$dir\tabgroups.json", '[{"Id":"g1","Name":"Travel","Tabs":[{"Url":"http://127.0.0.1:8795/dest?n=t1","Title":"dest t1"}],"Active":0}]')
[IO.File]::WriteAllText("$dir\settings.json", '{"improvementCollection":"true","startNews":"false","listsAutoUpdate":"Off"}')
& .\launch-quiet.ps1 -Profile tgmenu -Port $Port | Out-Null
$pidv = [int](Get-Content pid.txt); Start-Sleep 4
node cdp.js nav "#0" "http://127.0.0.1:8795/dest?n=move" | Out-Null; Start-Sleep 1.2
& .\uia.ps1 -ProcessId $pidv -Id NewTabButton | Out-Null; Start-Sleep 1.5

$before = [RClick]::GetForegroundWindow()
$cursor = New-Object RClick+PT; [void][RClick]::GetCursorPos([ref]$cursor)
$win = [IntPtr]([long](Get-Process -Id $pidv).MainWindowHandle)
[void][RClick]::SetWindowPos($win, [IntPtr](-1), 0, 0, 0, 0, 0x13); Start-Sleep -Milliseconds 400
$tab = Find "dest move" $pidv
Check "the tab is on screen" ($null -ne $tab) ""
if ($tab) {
    $r = $tab.Current.BoundingRectangle
    [RClick]::Right([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2))
    Set-Content -Path (Join-Path $kit "lastinput.txt") -Value ([uint32]([Environment]::TickCount -band 0xFFFFFFFF))
    [void][RClick]::SetCursorPos($cursor.X, $cursor.Y)
    Start-Sleep 1.2
    Check "a real right-click opens the tab's menu" ((Count "Close Other Tabs" $pidv) -ge 1) ""
    Check "ONCE - not two menus (ContextRequested and RightTapped both fire on a right-click)" ((Count "Close Other Tabs" $pidv) -eq 1) (Count "Close Other Tabs" $pidv)
    $sub = Find "Move to Tab Group" $pidv
    Check "it offers Move to Tab Group" ($null -ne $sub) ""
    if ($sub) {
        $sub.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand(); Start-Sleep 0.8
        $target = Find "Travel" $pidv
        Check "which lists the saved group" ($null -ne $target) ""
        if ($target) { $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Start-Sleep 2 }
        $g = @(Get-Content "$dir\tabgroups.json" -Raw | ConvertFrom-Json | ForEach-Object { $_ })
        Check "the tab joins the group" ((($g[0].Tabs | ForEach-Object Url) -join ',') -match 'n=move' -and $g[0].Tabs.Count -eq 2) (($g[0].Tabs | ForEach-Object Url) -join ',')
        Check "and leaves this window" (-not ((node cdp.js list) -join ' ' -match 'n=move')) ((node cdp.js list) -join ' | ')
    }
}
[void][RClick]::SetWindowPos($win, [IntPtr](-2), 0, 0, 0, 0, 0x13)
[void][RClick]::ShowWindow($win, 6); Start-Sleep -Milliseconds 400; [void][RClick]::ShowWindow($win, 4)
[void][RClick]::SetWindowPos($win, [IntPtr]1, 0, 0, 0, 0, 0x13)
"foreground back with the window that had it: " + ([RClick]::GetForegroundWindow() -eq $before)
Stop-Process -Id $pidv -Force
"`n$pass passed, $fail failed"
if ($fail -gt 0) { exit 1 }
