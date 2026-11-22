param(
    [ValidateSet('linux-x64','linux-arm64','win-x64')][string]$Runtime='linux-x64',
    [string]$OutputRoot=(Join-Path $PSScriptRoot '../.codex-artifacts/account-releases')
)
$ErrorActionPreference='Stop'
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
# Every invocation gets a new directory; previous releases are never overwritten.
$release=Join-Path $OutputRoot ("account-$Runtime-"+[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $release | Out-Null
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Verify.ps1')
if($LASTEXITCODE -ne 0){throw 'Release verification failed; no archive produced'}
$payload=Join-Path $release 'payload'
$app=Join-Path $payload 'app'
dotnet publish (Join-Path $PSScriptRoot 'AccountApi/AccountApi.csproj') -c Release -r $Runtime --self-contained false -p:UseAppHost=false -p:IsTransformWebConfigDisabled=true -p:DebugType=None -p:DebugSymbols=false -p:NuGetAudit=true -p:NuGetAuditMode=all -warnaserror -o $app
if($LASTEXITCODE -ne 0){throw 'Publish failed; no archive produced'}
# A release contains executable dependencies, never runtime settings, DBs, or keys.
foreach($file in Get-ChildItem -LiteralPath $app -Recurse -File) {
    $allowed=$file.Extension -in @('.dll','.so','.dylib') -or $file.Name -in @('AccountApi.deps.json','AccountApi.runtimeconfig.json','AccountApi.staticwebassets.endpoints.json')
    if(!$allowed){throw "Unexpected published file type: $($file.Name). Review before packaging."}
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AccountApi/deploy') -Destination (Join-Path $payload 'deploy') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'VerifyRelease.ps1') -Destination $payload
# The deploy directory must contain only the reviewed examples and instructions.
$templates=@('account.env.example','nginx.conf.example','pokechess-account.service.example','README.md')
foreach($file in Get-ChildItem -LiteralPath (Join-Path $payload 'deploy') -Recurse -File){if($file.Name -notin $templates){throw 'Unexpected deploy file; refusing to package local settings'}}
$files=@(Get-ChildItem -LiteralPath $payload -Recurse -File | ForEach-Object {
    [ordered]@{path=[IO.Path]::GetRelativePath($payload,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant();bytes=$_.Length}
} | Sort-Object path)
[ordered]@{schema=1;runtime=$Runtime;framework='net10.0';selfContained=$false;builtUtc=[DateTime]::UtcNow.ToString('o');files=$files} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $payload 'release-manifest.json') -Encoding utf8NoBOM
& pwsh -NoProfile -File (Join-Path $payload 'VerifyRelease.ps1') -ReleaseDirectory $payload
if($LASTEXITCODE -ne 0){throw 'Manifest verification failed'}
$archive=Join-Path $release "account-api-$Runtime.zip"
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive
(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($archive) | Set-Content -LiteralPath ($archive+'.sha256') -Encoding ascii
Write-Host "Release archive: $archive"
