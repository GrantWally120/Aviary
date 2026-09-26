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
#   uia.ps1 -ProcessId <pid> -Id AddressBox -Action SetValue -Value "aviary bird"   # (M4) set a text box's
#       text through its ValuePattern — raises TextChanged like typing, no keyboard. The box must already
#       have focus for Aviary to treat it as typing (Ctrl+L over DevTools gives it focus without fronting).
#   uia.ps1 -ProcessId <pid> -Id AddressBox -Action Value      # read a text box's text, and whether it has focus
#   uia.ps1 -ProcessId <pid> -Name "Forget localhost" -Action Select -Value "When I close it"   # (M4.9) choose an
#       item in a ComboBox. Its items do not exist in the tree until the drop-down is open (WinUI virtualizes
#       them), so this expands it, selects by name through SelectionItemPattern, then closes it again.
param([int]$ProcessId, [string]$Name = "", [string]$Id = "", [ValidateSet("Invoke", "Toggle", "Dump", "Help", "SetValue", "Value", "Focus", "Select")][string]$Action = "Invoke", [string]$Window = "", [string]$Value = "")

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]
$T = [System.Windows.Automation.TreeScope]

# A popup that goes while the tree is being walked (a tooltip fading, a flyout opening) throws
# ElementNotAvailable half-way (26 Sep: "Forget 127.0.0.2" was never chosen). Walk again - four tries.
for ($attempt = 1; ; $attempt++) {
    try {
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
        break
    }
    catch {
        # PowerShell wraps it in MethodInvocationException: look inside.
        $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }
        if ($attempt -ge 4 -or $e -isnot [System.Windows.Automation.ElementNotAvailableException]) { throw }
        Start-Sleep -Milliseconds 400
    }
}

if ($Action -eq "Dump") { $found | Select-Object -First 200; exit 0 }
if ($found.Count -eq 0) { Write-Output "NOT FOUND name='$Name' id='$Id'"; exit 1 }
$target = $found[0]

# A name can belong to more than a button's label — a tooltip with the same text lives in a popup,
# and popups are searched first (19 Sep: a tab's tooltip was found before the tab). For Invoke and
# Toggle, take the first match that sits inside something that can do it.
if ($Action -in "Invoke", "Toggle") {
    $prop = if ($Action -eq "Invoke") { $A::IsInvokePatternAvailableProperty } else { $A::IsTogglePatternAvailableProperty }
    $w = [System.Windows.Automation.TreeWalker]::ControlViewWalker
    foreach ($candidate in $found) {
        $p = $candidate
        while ($p -and -not [bool]$p.GetCurrentPropertyValue($prop)) { $p = $w.GetParent($p) }
        if ($p) { $target = $candidate; break }
    }
}

switch ($Action) {
    "Invoke" {
        # A label inside a button (a Smart Search row, M4) cannot be invoked itself: climb to the
        # nearest ancestor that can.
        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $hit = $target
        while ($hit -and -not [bool]$hit.GetCurrentPropertyValue($A::IsInvokePatternAvailableProperty)) { $hit = $walker.GetParent($hit) }
        if (-not $hit) { Write-Output "NOT INVOKABLE '$($target.Current.Name)'"; exit 1 }
        $hit.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); Write-Output "INVOKED '$($target.Current.Name)' ($($hit.Current.ControlType.ProgrammaticName))"
    }
    "Toggle" {
        # A tab's label sits inside its ToggleButton (19 Sep): climb to the nearest ancestor that toggles.
        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $hit = $target
        while ($hit -and -not [bool]$hit.GetCurrentPropertyValue($A::IsTogglePatternAvailableProperty)) { $hit = $walker.GetParent($hit) }
        if (-not $hit) { Write-Output "NOT TOGGLEABLE '$($target.Current.Name)'"; exit 1 }
        $p = $hit.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern); $p.Toggle(); Write-Output "TOGGLED '$($target.Current.Name)' -> $($p.Current.ToggleState)"
    }
    "Focus"  {
        # Gives a control the keyboard focus without any keystroke - for checking the focus ring
        # while Grant is at the PC (M4.5). UI Automation's SetFocus is how a screen reader does it.
        $target.SetFocus()
        Start-Sleep -Milliseconds 250
        Write-Output ("FOCUS '{0}' hasKeyboardFocus={1}" -f $target.Current.Name, $target.Current.HasKeyboardFocus)
    }
    "Help"   { Write-Output ("HELP '{0}': {1}" -f $target.Current.Name, $target.Current.HelpText) }
    "SetValue" { $target.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Value); Write-Output "SET '$($target.Current.AutomationId)' = '$Value'" }
    "Value"  { $v = $target.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value; Write-Output ("VALUE '{0}' = '{1}' focus={2}" -f $target.Current.AutomationId, $v, $target.Current.HasKeyboardFocus) }
    "Select" {
        # A ComboBox's items are created when it opens and destroyed when it closes, so the drop-down
        # must really be open while the item is found. The list part is a popup of its own, which is
        # why every top-level window of the process is searched again rather than $target's subtree.
        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $box = $target
        while ($box -and -not [bool]$box.GetCurrentPropertyValue($A::IsExpandCollapsePatternAvailableProperty)) { $box = $walker.GetParent($box) }
        if (-not $box) { Write-Output "NOT A DROP-DOWN '$($target.Current.Name)'"; exit 1 }
        $ec = $box.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        $ec.Expand()
        Start-Sleep -Milliseconds 400

        $item = $null
        foreach ($root in $A::RootElement.FindAll($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, $ProcessId)))) {
            foreach ($el in $root.FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition)) {
                if ($el.Current.Name -eq $Value -and [bool]$el.GetCurrentPropertyValue($A::IsSelectionItemPatternAvailableProperty)) { $item = $el; break }
            }
            if ($item) { break }
        }

        if (-not $item) { $ec.Collapse(); Write-Output "NO ITEM '$Value' in '$($box.Current.Name)'"; exit 1 }
        $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Start-Sleep -Milliseconds 250
        try { $ec.Collapse() } catch { }
        Write-Output "SELECTED '$Value' in '$($box.Current.Name)'"
    }
}
