$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    dotnet build AccountApi.Tests/AccountApi.Tests.csproj -p:NuGetAudit=true -p:NuGetAuditMode=all -warnaserror
    if($LASTEXITCODE -ne 0){throw 'Build or dependency audit failed'}
    dotnet run --project AccountApi.Tests/AccountApi.Tests.csproj --no-build
    if($LASTEXITCODE -ne 0){throw 'Server regression failed'}
    foreach($script in @('SmokeTest','SecuritySmokeTest','RecoverySmokeTest','UgsSmokeTest','OperationsSmokeTest')) {
        & pwsh -NoProfile -File "AccountApi/$script.ps1"
        if($LASTEXITCODE -ne 0){throw "$script failed"}
    }
} finally { Pop-Location }
