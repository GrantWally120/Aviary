# Compare the actual ZIP payload with the file manifest before it is handed over.
param([Parameter(Mandatory)][string]$Zip)
$ErrorActionPreference = 'Stop'
$zipPath = (Resolve-Path -LiteralPath $Zip).Path
$manifest = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $zipPath) 'manifest.json') -Raw | ConvertFrom-Json
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if (!$name.StartsWith('Aviary/') -or ($name.Split('/') -contains '..')) { throw "Unsafe ZIP path: $name" }
        if (!$entry.Name) { continue }
        if ($entries.ContainsKey($name)) { throw "Duplicate ZIP path: $name" }
        $entries.Add($name, $entry)
    }
    if ($entries.Count -ne $manifest.verification.entries.Count) { throw 'ZIP file count differs from the manifest.' }
    foreach ($file in $manifest.verification.entries) {
        $entry = $entries['Aviary/'+$file.path]
        if (!$entry -or $entry.Length -ne $file.bytes) { throw "ZIP file missing or wrong size: $($file.path)" }
        $stream = $entry.Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','').ToLowerInvariant() }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($hash -ne $file.sha256) { throw "ZIP hash mismatch: $($file.path)" }
    }
}
finally { $archive.Dispose() }
$actual = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$expected = (Get-Content -LiteralPath ($zipPath+'.sha256') -Raw).Split(' ')[0]
if ($actual -ne $expected) { throw 'ZIP checksum does not match its .sha256 file.' }
"Verified ZIP: $($manifest.verification.entries.Count) files match the manifest and checksum."
