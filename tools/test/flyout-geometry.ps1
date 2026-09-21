# Is every flyout's INPUT geometry where the flyout is DRAWN? (M4.7, bug 75.)
#
# A popup is drawn inside its own PopupWindowSiteBridge window; the XAML content inside it reports
# its own rect. When those two disagree the menu looks perfect and answers the mouse somewhere else,
# so every click on it counts as a click outside: the menu dismisses and nothing is chosen. That is
# what M4.6's bloom did to every menu in Aviary by writing a composition transform onto the popup's
# own root visual.
#
# Healthy = the content sits ~(10,2) inside its popup window (the drop shadow's inset).
#
#   flyout-geometry.ps1 -ProcessId <pid> [-Ids MenuButton,PageSettingsButton,ShieldsButton]
param([int]$ProcessId, [string[]]$Ids = @("MenuButton", "PageSettingsButton", "ShieldsButton", "DownloadsButton"))

$sp = $PSScriptRoot
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System; using System.Text; using System.Runtime.InteropServices;
public class FG {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RC r);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int m);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  public struct RC { public int L,T,R,B; }
  public static IntPtr Popup(int pid2) { IntPtr f = IntPtr.Zero;
    EnumWindows(delegate(IntPtr h, IntPtr l) { uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid2 && IsWindowVisible(h)) { var c = new StringBuilder(256); GetClassName(h, c, 256);
        if (c.ToString().Contains("PopupWindowSiteBridge")) f = h; } return true; }, IntPtr.Zero);
    return f; }
}
'@ -ErrorAction SilentlyContinue

$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]
$bad = 0

foreach ($id in $Ids) {
    & (Join-Path $sp 'uia.ps1') -ProcessId $ProcessId -Id $id -Action Invoke | Out-Null
    Start-Sleep -Milliseconds 1100

    $h = [FG]::Popup($ProcessId)
    if ($h -eq [IntPtr]::Zero) { "{0,-20} no popup opened" -f $id; continue }
    $r = New-Object FG+RC; [void][FG]::GetWindowRect($h, [ref]$r)

    $content = $null
    $roots = $A::RootElement.FindAll($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)))
    foreach ($root in $roots) {
        foreach ($el in $root.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
            $ct = $el.Current.ControlType.ProgrammaticName
            if ($ct -eq 'ControlType.Menu' -or $ct -eq 'ControlType.MenuItem') { $content = $el; break }
        }
        if ($content) { break }
    }
    if (-not $content) { "{0,-20} popup at {1},{2} but no menu content found" -f $id, $r.L, $r.T; continue }

    $b = $content.Current.BoundingRectangle
    $dx = [int]$b.X - $r.L; $dy = [int]$b.Y - $r.T
    $ok = ([Math]::Abs($dx) -le 40 -and [Math]::Abs($dy) -le 40)
    if (-not $ok) { $bad++ }
    "{0,-20} popup=({1},{2})  content=({3},{4})  offset=({5},{6})  {7}" -f `
        $id, $r.L, $r.T, [int]$b.X, [int]$b.Y, $dx, $dy, $(if ($ok) { "OK" } else { "*** DETACHED ***" })

    # Esc without keyboard: opening the next one dismisses this one anyway; for the last, click-away
    # is not needed because the process is about to be measured only.
}

if ($bad -gt 0) { "`n$bad flyout(s) DETACHED from their own window - clicks will miss"; exit 1 }
"`nall flyouts land inside their own window"
