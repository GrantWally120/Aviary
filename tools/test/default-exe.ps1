# Which Aviary.exe a test script runs when it was not given -Exe.
#
# The dev build in tools/test/pub is rebuildable output (README, "Build"), and the 22 Oct 2026 cleanup
# removed it along with the other redundant copies. So: the dev copy when one has been made, otherwise
# the installed publish/Aviary copy - the same application, which is what most of these harnesses need.
#
#   . "$PSScriptRoot\default-exe.ps1"
#   $exe = Get-AviaryExe $PSScriptRoot
function Get-AviaryExe([string]$Kit) {
    $dev = Join-Path $Kit "pub\Aviary.exe"
    if (Test-Path -LiteralPath $dev) { return $dev }

    $installed = Join-Path $Kit "..\..\publish\Aviary\Aviary.exe"
    if (Test-Path -LiteralPath $installed) { return (Resolve-Path -LiteralPath $installed).Path }

    throw "No Aviary.exe to test: make the dev copy (tools/test/README.md, 'Build') or install one in publish\Aviary."
}

# For harnesses that find (and kill) their own instances by path under tools/test: only the dev copy is theirs.
# The installed publish/Aviary copy is the one Grant may be using, so they must never run - or clean up - against it.
function Get-AviaryDevExe([string]$Kit) {
    $dev = Join-Path $Kit "pub\Aviary.exe"
    if (Test-Path -LiteralPath $dev) { return $dev }
    throw "This harness needs the dev copy in tools/test/pub (it is not there since the 22 Oct cleanup): build it first - tools/test/README.md, 'Build'."
}