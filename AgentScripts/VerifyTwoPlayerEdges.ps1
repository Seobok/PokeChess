. (Join-Path $PSScriptRoot 'TwoPlayerQAHelpers.ps1')
$folder=Join-Path $root 'Windows';New-Item -ItemType Directory -Force $folder|Out-Null
foreach($name in @('command.json','status.json','combat.json','client.json')){Remove-Item -LiteralPath (Join-Path $folder $name) -ErrorAction SilentlyContinue}
$project=(Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$qaPlayer=Start-Process -FilePath (Join-Path $project 'Builds/WBS6.1/PokeChessReconnect.exe') -ArgumentList "-pokechess-room-qa $folder -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile $(Join-Path $root ('windows-'+[DateTime]::UtcNow.Ticks+'.log'))" -WindowStyle Hidden -PassThru
foreach($role in @('Editor','Windows')){$s=Wait-QA $role {param($s) $s.state -eq 'Idle'};$seq[$role]=$s.sequence}
function Start-Match {
    $s=Send-QA Editor create @{name='Two-player boundaries';capacity=2;isPrivate=$true};Send-QA Windows code @{code=$s.code}|Out-Null
    Wait-QA Windows {param($s) $s.identityBound -and $s.connectionConfirmed}|Out-Null
    Send-QA Windows holdReconnect @{ready=$false}|Out-Null
    Send-QA Windows ready @{ready=$true}|Out-Null;Wait-QA Editor {param($s) !$s.startBlocked}|Out-Null
    Send-QA Windows ready @{ready=$false}|Out-Null;Wait-QA Editor {param($s) $s.startBlocked}|Out-Null
    Send-QA Windows ready @{ready=$true}|Out-Null;Wait-QA Editor {param($s) !$s.startBlocked}|Out-Null
    Send-QA Editor start|Out-Null;Send-QA Editor combatSpeed @{speed=0}|Out-Null
    Wait-QA Windows {param($s) $s.synchronized -and (Read-Client Windows).online}|Out-Null
    Send-QA Editor combatFixture|Out-Null;Wait-QA Windows {param($s) $s.match.units.Count -eq 1}|Out-Null
}
function Enter-Phase($phase){if($phase -eq 2){return};Send-QA Editor advanceRound|Out-Null;if($phase -eq 4){Send-QA Editor combatSpeed @{speed=10}|Out-Null;Wait-QA Editor {param($s) $s.publicState.phase -eq 4}|Out-Null;Send-QA Editor combatSpeed @{speed=0}|Out-Null};Wait-QA Windows {param($s) $s.publicState.phase -eq $phase -and $s.synchronized}|Out-Null}
function Leave-Both {Send-QA Windows leave|Out-Null;Send-QA Editor leave|Out-Null;foreach($role in @('Editor','Windows')){Wait-QA $role {param($s) $s.state -eq 'Idle' -and !(Read-Client $role).online}|Out-Null}}
function Restore {Send-QA Windows holdReconnect @{ready=$false}|Out-Null;Send-QA Windows reconnect|Out-Null;Wait-QA Windows {param($s) !$s.recovering -and ($s.state -eq 'Connected' -and $s.synchronized -or $s.state -eq 'Failed')}}
foreach($phase in @(2,3,4)){
    Start-Match;Enter-Phase $phase
    Send-QA Windows game @{kind='Surrender'}|Out-Null
    $s=Wait-QA Windows {param($s) $s.synchronized -and (Read-Client Windows).final -and (Read-Client Editor).final}
    $loser=$s.publicState.players|Where-Object id -eq $s.playerId
    Check "surrender-phase-$phase-final" ($loser.placement -eq 2 -and $loser.eliminationReason -eq 'Surrender' -and !(Read-Client Windows).buyEnabled) $s
    $revision=$s.publicState.revision;Send-QA Windows repeat|Out-Null;$s=Wait-QA Windows {param($s) $s.synchronized -and !$s.pendingCommand}
    Check "surrender-phase-$phase-duplicate" ($s.ack.accepted -and $s.publicState.revision -eq $revision) $s
    Send-QA Editor assertPool|Out-Null;Leave-Both
}
foreach($phase in @(3,4)){
    Start-Match;Enter-Phase $phase;Send-QA Windows holdReconnect @{ready=$true}|Out-Null;Send-QA Windows disconnectTransport|Out-Null
    $id=(Read-QA Windows).playerId;Wait-QA Editor {param($s) ($s.publicState.players|Where-Object id -eq $id).disconnected}|Out-Null
    Send-QA Editor expireDisconnected|Out-Null;$s=Wait-QA Editor {param($s) (Read-Client Editor).final}
    Check "timeout-phase-$phase-final" (($s.publicState.players|Where-Object id -eq $id).eliminationReason -eq 'DisconnectTimeout' -and $s.publicState.winnerPlayerId -eq $s.playerId) $s
    $s=Restore;Check "timeout-phase-$phase-recovery-closed" ($s.state -eq 'Failed' -and $s.error -eq 'ReconnectExpired' -and !(Read-Client Windows).online) $s
    Send-QA Editor assertPool|Out-Null;Leave-Both
}
Start-Match;Enter-Phase 4;$round=(Read-QA Windows).publicState.round
Send-QA Windows holdReconnect @{ready=$true}|Out-Null;Send-QA Windows disconnectTransport|Out-Null
Send-QA Editor combatSpeed @{speed=10}|Out-Null;Wait-QA Editor {param($s) $s.publicState.round -gt $round -and $s.publicState.phase -eq 3}|Out-Null;Send-QA Editor combatSpeed @{speed=0}|Out-Null
$s=Restore;Check 'result-to-next-combat-recovery' ($s.state -eq 'Connected' -and $s.publicState.round -gt $round -and (Read-Combat Windows).latest.round -eq $s.publicState.round) $s
Send-QA Windows holdReconnect @{ready=$true}|Out-Null;Send-QA Windows disconnectTransport|Out-Null
Send-QA Editor combatSpeed @{speed=20}|Out-Null;Wait-QA Editor {param($s) $s.publicState.phase -eq 5}|Out-Null
$s=Restore;Check 'offline-match-finished-closes-recovery' ($s.state -eq 'Failed' -and $s.error -in @('PlayerAlreadyEliminated','MatchFinished') -and !(Read-Client Windows).online) $s
Send-QA Editor assertPool|Out-Null;Leave-Both
Start-Match;Send-QA Windows repeat|Out-Null;$s=Wait-QA Windows {param($s) !$s.pendingCommand -and $s.synchronized}
Check 'old-match-command-rejected' (!$s.ack.accepted -and $s.ack.code -eq 'WrongMatch' -and $s.match.gold -eq 5) $s
Send-QA Editor leave|Out-Null;$s=Wait-QA Windows {param($s) $s.state -eq 'Failed'}
Check 'host-session-end-closed' ($s.error -eq 'HostLost' -and !(Read-Client Windows).online) $s
Send-QA Windows quit|Out-Null
Write-Output 'TWO_PLAYER_EDGE_REGRESSION_PASSED'
