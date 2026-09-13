# Copies Grant's projects into Aviary's library (src\Aviary\Library\files), laid out exactly as they
# sit under Documents so every relative link inside them still resolves — the Wally Hub's
# "../Sliver a Wally2TTD Experience/Silver_v1_4.html" among them.
#
# READ-ONLY on Documents. Grant's files are history (memory: keep-old-files-as-history): this script
# opens them only to copy, never to write, and nothing here edits a copy to make it "work" either.
# Run it before publishing Aviary; the MSBuild target warns when files\ is missing.
#
#   powershell -NoProfile -File tools\library\sync.ps1
#
# Phase 5, Alpha Build 5 M2.5 — HANDOFF section 16.
param([switch]$WhatIf)

$ErrorActionPreference = 'Stop'
$docs = Join-Path $env:USERPROFILE 'Documents'
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$dest = Join-Path $repo 'src\Aviary\Library\files'

# Grant, 13 Sep 2026: "do not include claude linux, bcs tracker, claude model, parallax loading screen,
# P.A.M and damage report (damage report is active)". Enforced below, not just left off the list.
$excluded = @('BCS Tracker.html', 'claude-linux.html', 'claude-model-matrix.html',
              'parallax-loading.html', 'pam-database.html', 'steam-damage-report.html')

# Paths relative to Documents. A wildcard copies every match in that one folder (not below it).
$items = @(
    # ── The Wally series: the Complete Edition exactly as Grant keeps it ──────────────────
    'Wally2TTD Complete Edition\*.html'
    'Wally2TTD Complete Edition\Archive\*.html'
    # ── …and the rest of the series, wherever it lives ───────────────────────────────────
    'Sliver a Wally2TTD Experience\Silver_v1_*.html'
    'Wally2TTD Radio\index.old.html'
    'Wally2TTDv2.5\Wally2TTDv2.4.html'                      # "Rail Ops Crisis", 1 May 21:35
    'Wally2TTDv2.5\wally2ttd_full_evolution.html'
    'index.html'                                            # the v1.8 desktop (Electron) edition's page
    'Claude CoWork Enclosed\Wally2TTDv2\Wally2TTDv2.4.html' # "Rail Ops Crisis", 1 May 21:58
    'Claude CoWork Enclosed\Wally2TTD-Godot\build\screenshots\*.png'  # 2.4.2's own screenshots (25 Jul)
    'Claude CoWork Enclosed\WallyCore1\build\screenshots\*.png'       # WallyCore1's render/demo captures
    # ── Everything else in the CoWork folder that a browser can open ─────────────────────
    'Claude CoWork Enclosed\artemis.html'
    'Claude CoWork Enclosed\console-index-2026.html'
    'Claude CoWork Enclosed\ember.html'
    'Claude CoWork Enclosed\Game Ledger.html'
    'Claude CoWork Enclosed\grants-birthday-gazette.html'
    'Claude CoWork Enclosed\happy-birthday-grant.html'
    'Claude CoWork Enclosed\jurassic-timeline.html'
    'Claude CoWork Enclosed\rog-experience.html'
    'Claude CoWork Enclosed\spec-showdown.html'
    'Claude CoWork Enclosed\the-ai-ledger.html'
    'Claude CoWork Enclosed\the-lost-part-1.html'
    'Claude CoWork Enclosed\work-log-2026-08.html'
    'Claude CoWork Enclosed\Filma\filma.html'
    'Claude CoWork Enclosed\jurassic-empire\JurassicEmpireLandingSite.HTML'
    'Claude CoWork Enclosed\jurassic-empire\JurassicEmpireRoadmap.html'
    'Claude CoWork Enclosed\jurassic-empire\README.md'
    'Claude CoWork Enclosed\jurassic-empire\notes\part2-design-briefs.md'
    'Claude CoWork Enclosed\jurassic-empire\build\screenshots\*.png'
    'Claude CoWork Enclosed\jurassic-empire\build\web\*'    # the playable web export
    'Claude CoWork Enclosed\Zoo\Aviary.html'
    'Claude CoWork Enclosed\Zoo\Aviary-Phase0.html'
)

if (-not $dest.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw "refusing: $dest is outside the repo" }

$wanted = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$copied = 0; $same = 0; $bytes = 0L

foreach ($item in $items) {
    $folder = Split-Path -Parent $item
    $pattern = Split-Path -Leaf $item
    $source = if ($folder) { Join-Path $docs $folder } else { $docs }
    $found = @(Get-ChildItem -LiteralPath $source -File -Filter $pattern -ErrorAction SilentlyContinue)
    if ($found.Count -eq 0) { Write-Warning "nothing matches $item"; continue }

    foreach ($file in $found) {
        if ($excluded -contains $file.Name) { throw "refusing: $($file.Name) is on Grant's do-not-include list" }
        $relative = $file.FullName.Substring($docs.Length + 1)
        $target = Join-Path $dest $relative
        [void]$wanted.Add($target)
        $bytes += $file.Length

        $existing = Get-Item -LiteralPath $target -ErrorAction SilentlyContinue
        if ($existing -and $existing.Length -eq $file.Length -and $existing.LastWriteTimeUtc -eq $file.LastWriteTimeUtc) { $same++; continue }

        if ($WhatIf) { "would copy  $relative"; continue }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force   # keeps the original's timestamps
        $copied++
    }
}

# Prune: the mirror holds exactly what the list names — a file dropped from the list, or deleted
# at the source, leaves the library too.
$pruned = 0
if (Test-Path -LiteralPath $dest) {
    Get-ChildItem -LiteralPath $dest -Recurse -File | Where-Object { -not $wanted.Contains($_.FullName) } | ForEach-Object {
        if ($WhatIf) { "would remove $($_.FullName.Substring($dest.Length + 1))" } else { Remove-Item -LiteralPath $_.FullName -Force; $pruned++ }
    }
    Get-ChildItem -LiteralPath $dest -Recurse -Directory | Sort-Object { $_.FullName.Length } -Descending |
        Where-Object { -not (Get-ChildItem -LiteralPath $_.FullName -Force) } | ForEach-Object { if (-not $WhatIf) { Remove-Item -LiteralPath $_.FullName -Force } }
}

"library: {0} files ({1:N1} MB) - {2} copied, {3} unchanged, {4} removed" -f $wanted.Count, ($bytes / 1MB), $copied, $same, $pruned
