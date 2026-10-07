$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '../TestResults/WBS5.6'
$seq=@{Editor=0;Windows=0}
function Read-QA($role){try{Get-Content (Join-Path $root "$role/status.json") -Raw | ConvertFrom-Json}catch{$null}}
function Wait-QA($role,$condition){$end=(Get-Date).AddSeconds(65); do{$s=Read-QA $role;if($s -and (& $condition $s)){return $s};Start-Sleep -Milliseconds 300}while((Get-Date)-lt $end);throw "Timeout $role : $($s|ConvertTo-Json -Compress -Depth 5)"}
function Send-QA($role,$action,$fields=@{}){$seq[$role]++;$c=@{sequence=$seq[$role];action=$action};foreach($k in $fields.Keys){$c[$k]=$fields[$k]};$path=Join-Path $root "$role/command.json"; $c|ConvertTo-Json|Set-Content ($path+".tmp");for($attempt=0;$attempt -lt 20;$attempt++){try{[System.IO.File]::Move(($path+".tmp"),$path,$true);break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 50}};$report=Wait-QA $role {param($s) $s.sequence -eq $seq[$role] -and !$s.busy};if($action -eq "gameUI" -or $action -eq "game" -and !$fields.skipStateWait){$report=Wait-QA $role {param($s) $s.synchronized -or $s.pendingCommand}};return $report}
function Check($name,$pass,$data){if(!$pass){throw "FAIL: $name"};$data|ConvertTo-Json -Depth 6|Set-Content (Join-Path $root "$name.json");Write-Output "PASS $name"}


function Owned($report){$m=$report.match|ConvertTo-Json -Depth 8|ConvertFrom-Json;$m.nextSequence=0;return $m|ConvertTo-Json -Depth 8 -Compress}

Wait-QA Windows {param($s) $s.state -eq 'Idle'}|Out-Null
$s=Send-QA Editor create @{name='State Sync QA';capacity=8;isPrivate=$false};$code=$s.code
$s=Send-QA Windows code @{code=$code};Wait-QA Windows {param($s) $s.identityBound -and $s.connectionConfirmed -and !$s.busy}|Out-Null
$s=Send-QA Windows ready @{ready=$true};Wait-QA Editor {param($s) !$s.startBlocked}|Out-Null;$s=Send-QA Editor start
$s=Wait-QA Windows {param($s) $s.synchronized -and !$s.pendingCommand};Check initial-full-sync ($s.publicState.matchId -eq $s.match.matchId -and $s.publicState.revision -eq $s.match.revision -and !$s.start.PSObject.Properties['seed']) $s
$hostId=(Read-QA Editor).match.playerId;$clientId=$s.match.playerId
$s=Send-QA Windows gameUI @{name='Shop 0'};Check private-buy ($s.match.gold -eq 4 -and $s.match.units.Count -eq 1 -and ($s.publicState.players|Where-Object id -eq $clientId).board.Count -eq 0) $s;$unit=$s.match.units[0].id
$s=Wait-QA Editor {param($s) $s.publicState.revision -eq (Read-QA Windows).publicState.revision};Check host-cannot-read-client-private ($s.match.units.Count -eq 0 -and !$s.match.PSObject.Properties['players']) $s
$s=Send-QA Windows gameUI @{name='B0'};$s=Send-QA Windows gameUI @{name='0,0'};Check own-public-board ($s.ack.accepted -and ($s.publicState.players|Where-Object id -eq $clientId).board[0].id -eq $unit) $s
$s=Wait-QA Editor {param($s) ($s.publicState.players|Where-Object id -eq $clientId).board.Count -eq 1};Check remote-public-board (($s.publicState.players|Where-Object id -eq $clientId).board[0].id -eq $unit -and $s.match.units.Count -eq 0) $s
$s=Send-QA Editor gameUI @{name='Observe next'};Check observe-ui (!$s.qaError) $s
$s=Send-QA Editor rememberState
$s=Send-QA Windows game @{kind='Move';unitId=$unit;column=2;row=1};$s=Wait-QA Windows {param($s) $s.synchronized};Check public-move ($s.ack.accepted -and ($s.publicState.players|Where-Object id -eq $clientId).board[0].column -eq 2) $s;$newVersion=$s.publicState.revision
$s=Send-QA Editor replayState;Start-Sleep -Seconds 1;$s=Read-QA Windows;Check old-wire-state-rejected ($s.publicState.revision -eq $newVersion -and ($s.publicState.players|Where-Object id -eq $clientId).board[0].column -eq 2) $s
$s=Send-QA Windows clearState;$s=Send-QA Windows syncState;$s=Wait-QA Windows {param($s) $s.synchronized -and !$s.pendingCommand};Check full-resync ($s.match.gold -eq 4 -and ($s.publicState.players|Where-Object id -eq $clientId).board[0].column -eq 2) $s
$s=Send-QA Editor holdState @{ready=$true};$s=Send-QA Windows game @{kind='Buy';slot=1;skipStateWait=$true};Check ack-awaits-state (!$s.synchronized -and $s.ack.accepted -and $s.match.gold -eq 4) $s
$s=Send-QA Editor holdState @{ready=$false};$s=Wait-QA Windows {param($s) $s.synchronized};Check withheld-state-recovers ($s.match.gold -eq 3 -and $s.match.units.Count -eq 2) $s
$s=Send-QA Editor dropAck;$s=Send-QA Windows game @{kind='Buy';slot=2};Check unknown-ack ($s.pendingCommand -and $s.match.gold -eq 2) $s
$beforeRetry=Owned $s;$s=Send-QA Windows retryCommand;Check duplicate-ack-no-extra-charge (!$s.pendingCommand -and $s.match.gold -eq 2 -and (Owned $s) -eq $beforeRetry) $s
$json=$s.publicState|ConvertTo-Json -Depth 8 -Compress
foreach($field in @('gold','xp','shop','inventory','bench','seed','nextSequence','playerRevision','Pool','PairingPlan')){if($json.Contains('"'+$field+'":')){throw "Public leak: $field"}}
Check public-schema-filtered $true $s
Write-Output 'PUBLIC_OBSERVER_READY';Start-Sleep -Seconds 20
$s=Send-QA Windows game @{kind='Surrender'};Check public-final ($s.publicState.phase -eq 5 -and $s.publicState.winnerPlayerId -eq $hostId -and ($s.publicState.players|Where-Object id -eq $clientId).placement -eq 2) $s
$s=Send-QA Windows leave;$s=Send-QA Editor leave
$s=Send-QA Windows create @{name='Reverse Sync';capacity=2;isPrivate=$true};$code=$s.code;$s=Send-QA Editor code @{code=$code};Wait-QA Editor {param($s) $s.identityBound -and $s.connectionConfirmed -and !$s.busy}|Out-Null
$s=Send-QA Editor ready @{ready=$true};Wait-QA Windows {param($s) !$s.startBlocked}|Out-Null;$s=Send-QA Windows start;Wait-QA Editor {param($s) $s.synchronized}|Out-Null
$s=Send-QA Editor gameUI @{name='My board'};$s=Send-QA Editor gameUI @{name='Shop 0'};$unit=$s.match.units[0].id;$s=Send-QA Editor game @{kind='Move';unitId=$unit;column=1;row=0};Check reverse-client-board $s.ack.accepted $s
$clientId=$s.match.playerId;$s=Wait-QA Windows {param($s) ($s.publicState.players|Where-Object id -eq $clientId).board.Count -eq 1};Check reverse-host-observes (($s.publicState.players|Where-Object id -eq $clientId).board[0].column -eq 1 -and $s.match.units.Count -eq 0) $s
$s=Send-QA Windows hostPhase;$s=Wait-QA Editor {param($s) $s.publicState.phase -eq 3 -and $s.synchronized};Check non-command-phase-sync ($s.match.phase -eq 3) $s
$s=Send-QA Windows leave;$s=Wait-QA Editor {param($s) $s.state -eq 'Idle'};Check cleanup-public-private (!$s.match.matchId -and !$s.publicState.matchId -and !$s.synchronized) $s
$s=Send-QA Windows quit
Write-Output 'ALL_STATE_SYNC_QA_PASSED'



