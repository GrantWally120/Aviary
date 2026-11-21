# Beta Prep 2 (21 Nov 2026): the "I agree" screen for the Terms of Use, on real fresh profiles.
#
# The other harnesses answer the screen through AVIARY_TEST_TERMS (launch-quiet.ps1), because a native
# dialog cannot be pressed through the DevTools port. This one leaves it up (-ShowTerms) and answers it
# through UI Automation, the way uia.ps1 presses any other XAML control - no mouse, no keyboard, no
# focus change, so it is safe while Grant is at the PC.
#
# What it proves:
#   1. a fresh profile is shown the screen, with the short version and both documents folded on it;
#   2. while it is up there is NO engine and NO tab (the DevTools port does not answer);
#   3. "I agree" makes the first tab, and settings.json holds the revision and the date;
#   4. the Start Page then carries the one-time note, and Settings -> Legal draws both documents;
#   5. the next launch of the same profile is not asked again;
#   6. "Quit Aviary" on another fresh profile closes Aviary and records nothing.
#
#   terms-gate-test.ps1 [-Exe <path to Aviary.exe>] [-Port 9471]
param([string]$Exe = "", [int]$Port = 9471)

$kit = $PSScriptRoot
Set-Location $kit
. "$kit\default-exe.ps1"
if (-not $Exe) { $Exe = Get-AviaryExe $kit }
$env:CDP_PORT = "$Port"
$pass = 0; $fail = 0
function Check($what, $ok, $saw) {
    if ($ok) { $script:pass++; "  PASS  $what" }
    else { $script:fail++; "  FAIL  $what  --  saw: $saw" }
}
function Launch($profile, [switch]$Fresh) {
    $line = if ($Fresh) { & .\launch-quiet.ps1 -Profile $profile -Fresh -Port $Port -ShowTerms -Exe $Exe -SettingsJson '{"startNews":"false"}' }
            else { & .\launch-quiet.ps1 -Profile $profile -Port $Port -ShowTerms -Exe $Exe }
    if ("$line" -match 'TEST PID (\d+)') { [int]$Matches[1] } else { 0 }
}
function Tabs { try { (node cdp.js list 2>$null | Where-Object { $_ -match '^page' }).Count } catch { 0 } }
function Close($procId) {
    $p = Get-Process -Id $procId -ErrorAction SilentlyContinue
    for ($n = 0; $p -and -not $p.HasExited -and $n -lt 8; $n++) { [void]$p.CloseMainWindow(); Start-Sleep 1; $p.Refresh() }
    if ($p -and -not $p.HasExited) { Stop-Process -Id $procId -Force }
}

"== the copy under test: $Exe"

# ── 1-4: agree ──
$procId = Launch "termsAgree" -Fresh
Start-Sleep 4
$tree = (& .\uia.ps1 -ProcessId $procId -Id TermsDialog -Action Dump 2>&1) -join "`n"
Check "a fresh profile is shown the Terms screen" ($tree -match 'Welcome to Aviary') ($tree.Substring(0, [Math]::Min(160, $tree.Length)))
Check "it carries the short version" (($tree -match 'comes as it is, with no guarantee') -and ($tree -match 'stays on this computer') -and ($tree -match 'responsible for what you browse')) "short version missing"
Check "both documents can be read on it" (($tree -match 'Read the Terms of Use') -and ($tree -match 'Read the Privacy Policy')) "folds missing"
Check "it has exactly the two answers" (($tree -match 'I agree') -and ($tree -match 'Quit Aviary')) "buttons missing"
Check "nothing exists behind it: no engine, no tab" ((Tabs) -eq 0) "$(Tabs) tabs"
$settings = Join-Path $kit "profiles\termsAgree\settings.json"
Check "nothing is recorded before an answer" (-not ((Get-Content $settings -Raw) -match 'termsAccepted')) "termsAccepted present"

& .\uia.ps1 -ProcessId $procId -Id PrimaryButton -Action Invoke | Out-Null
Start-Sleep 5
Check "I agree makes the first tab" ((Tabs) -ge 1) "$(Tabs) tabs"
$saved = Get-Content $settings -Raw
$revision = if ((Get-Content (Join-Path $kit "..\..\src\Aviary\LegalTexts.cs") -Raw) -match 'TermsRevision = "([^"]+)"') { $Matches[1] } else { "?" }
Check "settings.json holds the revision agreed to ($revision)" ($saved -match ('"termsAccepted":\s*"' + [regex]::Escape($revision) + '"')) $saved.Substring(0, [Math]::Min(200, $saved.Length))
Check "...and the day" ($saved -match '"termsAcceptedOn":\s*"\d{1,2} \w+ \d{4}"') "no date"

$tab = (node cdp.js list | Select-Object -First 1) -split '\s+' | Select-Object -Index 1
$note = node cdp.js eval $tab "(document.getElementById('lnote')||{}).innerText||''"
Check "the Start Page says where the legal pages are, once" ($note -match 'Settings') $note
node cdp.js eval $tab "document.querySelector('[data-legal=seen]').click(); 'ok'" | Out-Null
Start-Sleep 1
Check "...and remembers that it was read" ((Get-Content $settings -Raw) -match '"legalNoteSeen"') "legalNoteSeen missing"

node cdp.js key $tab "ctrl+Comma" | Out-Null
Start-Sleep 3
$legal = node cdp.js eval Settings "showPane('legal'); document.getElementById('pane-legal').innerText"
Check "Settings -> Legal names all three documents and the agreement" (($legal -match 'Terms of Use') -and ($legal -match 'Privacy Policy') -and ($legal -match 'Copyright notice') -and ($legal -match 'You agreed to the Terms of Use')) $legal
$terms = node cdp.js eval Settings "showPane('terms'); document.getElementById('legal-terms-copy').innerText.length + '|' + document.getElementById('legal-terms-copy').querySelectorAll('h2').length"
Check "the Terms of Use are drawn in full (13 sections)" ($terms -match '^"?\d{4,}\|13') $terms
$copy = node cdp.js eval Settings "showPane('copyright'); document.getElementById('legal-copyright-copy').innerText"
Check "the Copyright notice is drawn" ($copy -match 'All rights reserved') $copy
Close $procId

# ── 5: not asked again ──
$procId = Launch "termsAgree"
Start-Sleep 5
$tree = (& .\uia.ps1 -ProcessId $procId -Id TermsDialog -Action Dump 2>&1) -join "`n"
Check "the same profile is not asked again" ((-not ($tree -match 'Welcome to Aviary')) -and ((Tabs) -ge 1)) "dialog shown or no tab"
$tab = (node cdp.js list | Select-Object -First 1) -split '\s+' | Select-Object -Index 1
$note = node cdp.js eval $tab "!!document.getElementById('lnote')"
Check "...and the note is not shown twice" ($note -match 'false') $note
Close $procId

# ── 6: quit ──
$procId = Launch "termsQuit" -Fresh
Start-Sleep 4
& .\uia.ps1 -ProcessId $procId -Id SecondaryButton -Action Invoke | Out-Null
Start-Sleep 4
$alive = Get-Process -Id $procId -ErrorAction SilentlyContinue
Check "Quit Aviary closes Aviary" ($null -eq $alive -or $alive.HasExited) "still running"
if ($alive -and -not $alive.HasExited) { Stop-Process -Id $procId -Force }
$quit = Join-Path $kit "profiles\termsQuit\settings.json"
Check "...and records no agreement" (-not ((Get-Content $quit -Raw) -match 'termsAccepted')) "termsAccepted present"

""
"Terms screen: $pass passed, $fail failed."
exit $(if ($fail) { 1 } else { 0 })
