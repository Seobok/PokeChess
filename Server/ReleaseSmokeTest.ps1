param([Parameter(Mandatory)][string]$Archive)
$ErrorActionPreference='Stop'
$Archive=[IO.Path]::GetFullPath($Archive)
$hashLine=(Get-Content -LiteralPath ($Archive+'.sha256') -Raw).Trim()
if(($hashLine -split '\s+')[0] -ne (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Archive checksum mismatch'}
$destination=Join-Path ([IO.Path]::GetTempPath()) ('pokechess-release-'+[guid]::NewGuid().ToString('N'))
Expand-Archive -LiteralPath $Archive -DestinationPath $destination
function Verify([bool]$shouldPass) {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'VerifyRelease.ps1') -ReleaseDirectory $destination
    if(($LASTEXITCODE -eq 0) -ne $shouldPass){throw 'Release verification did not match expected result'}
}
Verify $true
$readme=Join-Path $destination 'deploy/README.md'
$original=[IO.File]::ReadAllBytes($readme)
try {
    [IO.File]::WriteAllText($readme,'tampered test content')
    Verify $false
} finally {[IO.File]::WriteAllBytes($readme,$original)}
$missing=Join-Path ([IO.Path]::GetTempPath()) ('pokechess-readme-'+[guid]::NewGuid().ToString('N'))
try {Move-Item -LiteralPath $readme -Destination $missing; Verify $false} finally {Move-Item -LiteralPath $missing -Destination $readme}
$extra=Join-Path $destination 'unexpected-test-file.txt'
try {[IO.File]::WriteAllText($extra,'test'); Verify $false} finally {Remove-Item -LiteralPath $extra}
Verify $true
$manifest=Get-Content -LiteralPath (Join-Path $destination 'release-manifest.json') -Raw | ConvertFrom-Json
if($IsWindows -and $manifest.runtime -eq 'win-x64') {
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'AccountApi/OperationsSmokeTest.ps1') -ApiDllPath (Join-Path $destination 'app/AccountApi.dll')
    if($LASTEXITCODE -ne 0){throw 'Published Windows server smoke test failed'}
} else {Write-Host 'Runtime execution not tested on this OS; archive verification only.'}
Write-Host 'PASS: archive checksum, extraction, tamper/missing/extra-file rejection.'
