$ErrorActionPreference = 'Stop'
$itemProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Set-Location -LiteralPath $itemProjectRoot
# Reuse a development Player's native engine and content while compiling the current
# managed assemblies. This is an isolated runtime smoke test, not a fresh Unity build.
$itemProjectXml = [xml](Get-Content -Raw PokeChess.Client.csproj)
$itemDefines = ($itemProjectXml.Project.PropertyGroup.DefineConstants | Where-Object { $_ }) -split ';' | Where-Object { $_ -notlike 'UNITY_EDITOR*' }
$itemFlags = ($itemDefines + 'DEVELOPMENT_BUILD') -join '%3B'
dotnet build PokeChess.Client.csproj --no-restore -v quiet "-p:DefineConstants=$itemFlags" > (Join-Path $PSScriptRoot 'player-compile.log')
if ($LASTEXITCODE -ne 0) { throw 'Player assembly compilation failed; see player-compile.log.' }
$itemPlayerRoot = Join-Path $PSScriptRoot 'Player'
if (-not (Test-Path -LiteralPath $itemPlayerRoot)) { Copy-Item -LiteralPath (Join-Path $itemProjectRoot 'Builds/WBS6.1') -Destination $itemPlayerRoot -Recurse }
foreach ($itemAssembly in @('PokeChess.Core','PokeChess.Client','PokeChess.Network')) {
    Copy-Item -LiteralPath (Join-Path $itemProjectRoot "Temp/Bin/Debug/$itemAssembly/$itemAssembly.dll") -Destination (Join-Path $itemPlayerRoot "PokeChessReconnect_Data/Managed/$itemAssembly.dll") -Force
}
$itemOutput = Join-Path $PSScriptRoot 'PlayerQA'
$itemProcess = Start-Process -FilePath (Join-Path $itemPlayerRoot 'PokeChessReconnect.exe') -ArgumentList @('-batchmode','-screen-width','1280','-screen-height','720','-pokechess-items-qa','-pokechess-qa-output',$itemOutput,'-logFile',(Join-Path $PSScriptRoot 'player-qa.log')) -WorkingDirectory $itemPlayerRoot -WindowStyle Hidden -PassThru
$itemProcess.Id | Set-Content (Join-Path $PSScriptRoot 'player-pid.txt')
Write-Output "QA player PID: $($itemProcess.Id); results: $itemOutput"
