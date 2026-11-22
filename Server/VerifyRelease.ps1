param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($ReleaseDirectory)
$manifest=Get-Content -LiteralPath (Join-Path $root 'release-manifest.json') -Raw | ConvertFrom-Json
if($manifest.schema -ne 1 -or !$manifest.files -or $manifest.runtime -notin @('linux-x64','linux-arm64','win-x64')){throw 'Invalid release manifest'}
$expected=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach($file in $manifest.files) {
    if([IO.Path]::IsPathRooted($file.path) -or $file.path -match '(^|[\/])\.\.([\/]|$)' -or !$expected.Add($file.path)){throw 'Unsafe or duplicate manifest path'}
    $path=[IO.Path]::GetFullPath((Join-Path $root $file.path))
    if(!(Test-Path -LiteralPath $path -PathType Leaf)){throw 'Release file missing'}
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $file.sha256 -or (Get-Item -LiteralPath $path).Length -ne $file.bytes){throw "Release file checksum mismatch: $($file.path)"}
}
foreach($file in Get-ChildItem -LiteralPath $root -Recurse -File) {
    $relative=[IO.Path]::GetRelativePath($root,$file.FullName).Replace('\','/')
    if($relative -ne 'release-manifest.json' -and !$expected.Contains($relative)){throw 'Unexpected file in release'}
}
Write-Host "PASS: release manifest, file sizes and SHA-256 hashes ($($expected.Count) files)."
