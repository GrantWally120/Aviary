# Press a control in a test Aviary through UI Automation — no mouse, no keyboard, no focus change.
# Safe while Grant is at the PC: nothing moves and his window stays in front. (M3, 13 Sep 2026.)
#
#   uia.ps1 -ProcessId <pid> -Name "Open Anyway"               # invoke a button by its name
#   uia.ps1 -ProcessId <pid> -Id ShieldsButton                 # ...or by AutomationId (x:Name)
#   uia.ps1 -ProcessId <pid> -Name "Shields for news.test" -Action Toggle
#   uia.ps1 -ProcessId <pid> -Action Dump [-Name text]         # list controls (optionally filtered)
#   uia.ps1 -ProcessId <pid> -Id ShieldsButton -Action Help    # read a control's tooltip/help text
# Popups (a flyout) live in their own top-level window of the same process, so every window of the
# process is searched.
#   -Window "Private"   only search top-level windows whose title contains this (the private window)
param([int]$ProcessId, [string]$Name = "", [string]$Id = "", [ValidateSet("Invoke", "Toggle", "Dump", "Help")][string]$Action = "Invoke", [string]$Window = "")

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]

$roots = $A::RootElement.FindAll($T::Children,
    (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)))
if ($roots.Count -eq 0) { Write-Output "NO WINDOW for pid $ProcessId"; exit 2 }

$found = @()
foreach ($root in $roots) {
    if ($Window -and $root.Current.Name -notlike "*$Window*") { continue }
    foreach ($el in $root.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
        $n = $el.Current.Name; $aid = $el.Current.AutomationId
        if ($Action -eq "Dump") {
            if (-not $Name -or $n -like "*$Name*") { $found += "{0,-22} {1,-28} {2}" -f $el.Current.ControlType.ProgrammaticName.Replace('ControlType.', ''), $aid, $n }
            continue
        }
        if (($Name -and $n -eq $Name) -or ($Id -and $aid -eq $Id)) { $found += $el }
    }
}

if ($Action -eq "Dump") { $found | Select-Object -First 200; exit 0 }
if ($found.Count -eq 0) { Write-Output "NOT FOUND name='$Name' id='$Id'"; exit 1 }
$target = $found[0]

switch ($Action) {
    "Invoke" { $target.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Write-Output "INVOKED '$($target.Current.Name)' ($($target.Current.AutomationId))" }
    "Toggle" { $p = $target.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern); $p.Toggle(); Write-Output "TOGGLED '$($target.Current.Name)' -> $($p.Current.ToggleState)" }
    "Help"   { Write-Output ("HELP '{0}': {1}" -f $target.Current.Name, $target.Current.HelpText) }
}
