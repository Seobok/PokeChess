param([switch]$RecoveryEdgesOnly)
$ErrorActionPreference='Stop'
$project=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$root=Join-Path $project 'TestResults/WBS6.1'
$seq=@{Editor=0;Windows=0}
$script:qaPlayer=$null
function Read-QA($role){try{Get-Content (Join-Path $root "$role/status.json") -Raw|ConvertFrom-Json}catch{$null}}
function Read-Client($role){try{Get-Content (Join-Path $root "$role/client.json") -Raw|ConvertFrom-Json}catch{$null}}
function Read-Combat($role){try{Get-Content (Join-Path $root "$role/combat.json") -Raw|ConvertFrom-Json}catch{$null}}
function Wait-QA($role,$condition){$end=(Get-Date).AddSeconds(65);do{$s=Read-QA $role;if($s -and (& $condition $s)){return $s};Start-Sleep -Milliseconds 200}while((Get-Date)-lt $end);throw "Timeout ${role}: $($s|ConvertTo-Json -Compress -Depth 6)"}
function Send-QA($role,$action,$fields=@{}){$seq[$role]++;$c=@{sequence=$seq[$role];action=$action};foreach($k in $fields.Keys){$c[$k]=$fields[$k]};$path=Join-Path $root "$role/command.json";$c|ConvertTo-Json|Set-Content ($path+'.tmp');for($i=0;$i -lt 20;$i++){try{[IO.File]::Move(($path+'.tmp'),$path,$true);break}catch{if($i -eq 19){throw};Start-Sleep -Milliseconds 50}};$s=Wait-QA $role {param($s) $s.sequence -eq $seq[$role] -and !$s.busy};if($s.qaError){throw $s.qaError};return $s}
function Check($name,$pass,$data){if(!$pass){throw "FAIL $name"};$data|ConvertTo-Json -Depth 18|Set-Content (Join-Path $root "$name.json");Write-Output "PASS $name"}
function Fingerprint($s){@{gold=$s.match.gold;xp=$s.match.xp;level=$s.match.level;locked=$s.match.locked;shop=$s.match.shop;units=$s.match.units;inventory=$s.match.inventory}|ConvertTo-Json -Compress -Depth 8}
function Start-Windows {
    $folder=Join-Path $root 'Windows';New-Item -ItemType Directory -Force $folder|Out-Null
    foreach($name in @('command.json','status.json','combat.json','client.json')){Remove-Item -LiteralPath (Join-Path $folder $name) -ErrorAction SilentlyContinue}
    $script:qaPlayer=Start-Process -FilePath (Join-Path $project 'Builds/WBS6.1/PokeChessReconnect.exe') -ArgumentList "-pokechess-room-qa $folder -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile $(Join-Path $root ('windows-'+[DateTime]::UtcNow.Ticks+'.log'))" -WindowStyle Hidden -PassThru
    $seq.Windows=0
}
function Start-Match($hostRole,$extra=0){$clientRole=if($hostRole -eq 'Editor'){'Windows'}else{'Editor'};$s=Send-QA $hostRole create @{name='Reconnect QA';capacity=2;isPrivate=$true};Send-QA $clientRole code @{code=$s.code}|Out-Null;Wait-QA $clientRole {param($s) $s.identityBound -and $s.connectionConfirmed}|Out-Null;Send-QA $clientRole ready @{ready=$true}|Out-Null;if($extra){Send-QA $hostRole extraPlayers @{capacity=$extra}|Out-Null};Wait-QA $hostRole {param($s) !$s.startBlocked}|Out-Null;Send-QA $hostRole start|Out-Null;Send-QA $hostRole combatSpeed @{speed=0}|Out-Null;Wait-QA $clientRole {param($s) $s.synchronized -and (Read-Client $clientRole).online}|Out-Null}
function Restore($role){Send-QA $role holdReconnect @{ready=$false}|Out-Null;Send-QA $role reconnect|Out-Null;Wait-QA $role {param($s) $s.state -eq 'Connected' -and $s.synchronized -and !$s.recovering -and !$s.pendingCommand}}
Start-Windows
foreach($role in @('Editor','Windows')){$s=Wait-QA $role {param($s) $s.state -eq 'Idle'};$seq[$role]=$s.sequence}
if(!$RecoveryEdgesOnly){
Start-Match Editor
$clientId=(Read-QA Windows).playerId
Send-QA Editor clientRankFixture @{targetPlayer=$clientId}|Out-Null;Wait-QA Windows {param($s) $s.match.gold -eq 20}|Out-Null
Send-QA Windows gameUI @{name='ShopCard_0'}|Out-Null;Wait-QA Windows {param($s) !$s.pendingCommand -and $s.synchronized}|Out-Null
Send-QA Windows gameUI @{name='Lock'}|Out-Null
$before=Wait-QA Windows {param($s) !$s.pendingCommand -and $s.match.locked};$saved=Fingerprint $before;$oldClient=$before.networkClientId
Send-QA Windows holdReconnect @{ready=$true}|Out-Null;Send-QA Windows disconnectTransport|Out-Null
$offline=Wait-QA Windows {param($s) $s.recovering -and !$s.synchronized}
Wait-QA Editor {param($s) ($s.publicState.players|Where-Object id -eq $clientId).disconnected}|Out-Null
Check 'prep-input-blocked-and-state-held' (!(Read-Client Windows).buyEnabled -and (Read-Client Windows).online -and (Fingerprint $offline) -eq $saved) $offline
Write-Output 'GRACE_30_SECONDS';Start-Sleep -Seconds 30
$after=Restore Windows
Check 'prep-30s-same-player-new-client' ($after.playerId -eq $clientId -and $after.networkClientId -ne $oldClient -and (Fingerprint $after) -eq $saved -and $after.publicState.players.Count -eq 2) $after
Send-QA Editor dropAck|Out-Null;$slot=(Read-QA Windows).match.shop|Where-Object {$_.cost -gt 0}|Select-Object -First 1
Send-QA Windows game @{kind='Buy';slot=$slot.index}|Out-Null;$pending=Read-QA Windows
Check 'buy-ack-lost-pending' ($pending.pendingCommand) $pending
$hostAfterBuy=Read-QA Editor;$clientHost=$hostAfterBuy.publicState.players|Where-Object id -eq $clientId
$expected=Fingerprint $pending
Send-QA Windows holdReconnect @{ready=$true}|Out-Null;Send-QA Windows disconnectTransport|Out-Null
$after=Restore Windows
Check 'buy-recovery-no-duplicate' (!$after.pendingCommand -and $after.ack.accepted -and (Fingerprint $after) -eq $expected) $after

$before=Read-QA Windows;$saved=Fingerprint $before;$oldClient=$before.networkClientId
Write-Output 'RESTART_CLIENT_PROCESS';Stop-Process -Id $script:qaPlayer.Id -Force
Wait-QA Editor {param($s) ($s.publicState.players|Where-Object id -eq $clientId).disconnected}|Out-Null
Start-Windows;$after=Wait-QA Windows {param($s) $s.state -eq 'Connected' -and $s.synchronized -and !$s.recovering}
Check 'process-restart-snapshot' ($after.playerId -eq $clientId -and $after.networkClientId -ne $oldClient -and (Fingerprint $after) -eq $saved) $after
foreach($unit in (Read-QA Windows).match.units){Send-QA Windows game @{kind='Sell';unitId=$unit.id}|Out-Null;Wait-QA Windows {param($s) $s.synchronized -and !$s.pendingCommand}|Out-Null}
Send-QA Editor combatFixture|Out-Null;Send-QA Editor advanceRound|Out-Null;Send-QA Editor combatSpeed @{speed=1}|Out-Null
Wait-QA Windows {param($s) (Read-Combat Windows).latest.tick -ge 80}|Out-Null;$tick=(Read-Combat Windows).latest.tick
Send-QA Windows holdReconnect @{ready=$true}|Out-Null;Send-QA Windows disconnectTransport|Out-Null
Start-Sleep -Seconds 4;Send-QA Editor combatSpeed @{speed=0}|Out-Null;$hostFrame=(Read-Combat Editor).latest
$after=Restore Windows;Wait-QA Windows {param($s) (Read-Combat Windows).latest.tick -ge $hostFrame.tick}|Out-Null
Check 'combat-current-baseline' ((Read-Combat Windows).latest.tick -gt $tick -and (Read-Combat Windows).latest.battleId -eq $hostFrame.battleId -and !(Read-Combat Windows).error) (Read-Combat Windows)
Send-QA Windows leave|Out-Null;Send-QA Editor leave|Out-Null
Start-Match Editor
Send-QA Windows holdReconnect @{ready=$true}|Out-Null;Send-QA Windows disconnectTransport|Out-Null
Wait-QA Editor {param($s) ($s.publicState.players|Where-Object id -eq $clientId).disconnected}|Out-Null
Write-Output 'GRACE_REAL_120_SECONDS'; for($gracePart=1;$gracePart -le 4;$gracePart++){Start-Sleep -Seconds 31;Write-Output "GRACE_WAIT_PART_$gracePart"}
Check 'timeout-elimination-host' (($s=Read-QA Editor).publicState.players|Where-Object {$_.id -eq $clientId -and $_.eliminated -and $_.eliminationReason -eq 'DisconnectTimeout'}) (Read-QA Editor)
Send-QA Windows holdReconnect @{ready=$false}|Out-Null;Send-QA Windows reconnect|Out-Null
$failed=Wait-QA Windows {param($s) $s.state -eq 'Failed' -and !$s.recovering}
Check 'timeout-reconnect-rejected' ($failed.error -eq 'ReconnectExpired') $failed
Send-QA Windows leave|Out-Null;Send-QA Editor leave|Out-Null
}
Start-Match Windows 2
$before=Read-QA Editor;$saved=Fingerprint $before;$oldClient=$before.networkClientId
Send-QA Editor holdReconnect @{ready=$true}|Out-Null;Send-QA Editor disconnectTransport|Out-Null
Wait-QA Editor {param($s) $s.recovering -and !$s.synchronized}|Out-Null
Write-Output 'EDITOR_RECOVERY_UI_READY';Start-Sleep -Seconds 8
$after=Restore Editor
Check 'reverse-host-recovery' ($after.networkClientId -ne $oldClient -and (Fingerprint $after) -eq $saved) $after
Send-QA Windows combatFixture|Out-Null;Send-QA Windows advanceRound|Out-Null
Wait-QA Editor {param($s) (Read-Combat Editor).current.battleId}|Out-Null;$own=Read-Combat Editor
$other=((Read-QA Editor).publicState.players|Where-Object {$_.id -ne $own.current.one -and $_.id -ne $own.current.two})[0].id
Send-QA Editor clientWatch @{targetPlayer=$other}|Out-Null
Wait-QA Editor {param($s) (Read-Combat Editor).latest.battleId -ne $own.current.battleId -and ((Read-Combat Editor).latest.one -eq $other -or (Read-Combat Editor).latest.two -eq $other)}|Out-Null
$watchedBattle=(Read-Combat Editor).latest.battleId
Send-QA Editor holdReconnect @{ready=$true}|Out-Null;Send-QA Editor disconnectTransport|Out-Null
$after=Restore Editor
Check 'spectator-subscription-restored' ((Read-Client Editor).observed -eq $other -and (Read-Combat Editor).latest.battleId -eq $watchedBattle) (Read-Combat Editor)
Send-QA Windows leave|Out-Null;$failed=Wait-QA Editor {param($s) $s.state -eq 'Failed' -and !$s.recovering}
Check 'host-loss-terminates-match' ($failed.error -eq 'HostLost' -and !$failed.start.matchId -and !(Read-Client Editor).online) $failed
Send-QA Editor leave|Out-Null;Start-Match Editor
Send-QA Windows holdReconnect @{ready=$true}|Out-Null;Send-QA Windows disconnectTransport|Out-Null
Wait-QA Windows {param($s) $s.recovering}|Out-Null;Send-QA Windows leave|Out-Null
Start-Sleep -Seconds 3;$left=Read-QA Windows
Check 'cancel-recovery-stays-idle' ($left.state -eq 'Idle' -and !$left.recovering -and !$left.start.matchId -and !(Read-Client Windows).online) $left
Send-QA Windows quit|Out-Null;Wait-Process -Id $script:qaPlayer.Id -Timeout 10 -ErrorAction SilentlyContinue;Start-Windows
$fresh=Wait-QA Windows {param($s) $s.state -eq 'Idle'};Start-Sleep -Seconds 3;$fresh=Read-QA Windows
Check 'cancel-clears-restart-ticket' ($fresh.state -eq 'Idle' -and !$fresh.recovering -and !$fresh.start.matchId) $fresh
Send-QA Editor leave|Out-Null;Send-QA Windows quit|Out-Null
if($RecoveryEdgesOnly){Write-Output 'RECOVERY_EDGE_QA_PASSED'}else{Write-Output 'ALL_RECONNECT_QA_PASSED'}


