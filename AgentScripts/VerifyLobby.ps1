$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '../TestResults/WBS5.4'
$seq=@{Editor=0;Windows=0}
function Read-QA($role){try{Get-Content (Join-Path $root "$role/status.json") -Raw | ConvertFrom-Json}catch{$null}}
function Wait-QA($role,$condition){$end=(Get-Date).AddSeconds(65); do{$s=Read-QA $role;if($s -and (& $condition $s)){return $s};Start-Sleep -Milliseconds 300}while((Get-Date)-lt $end);throw "Timeout $role : $($s|ConvertTo-Json -Compress -Depth 5)"}
function Send-QA($role,$action,$fields=@{}){$seq[$role]++;$c=@{sequence=$seq[$role];action=$action};foreach($k in $fields.Keys){$c[$k]=$fields[$k]};$path=Join-Path $root "$role/command.json"; $c|ConvertTo-Json|Set-Content ($path+".tmp");for($attempt=0;$attempt -lt 20;$attempt++){try{[System.IO.File]::Move(($path+".tmp"),$path,$true);break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 50}};Wait-QA $role {param($s) $s.sequence -eq $seq[$role] -and !$s.busy}}
function Check($name,$pass,$data){if(!$pass){throw "FAIL: $name"};$data|ConvertTo-Json -Depth 6|Set-Content (Join-Path $root "$name.json");Write-Output "PASS $name"}

Wait-QA Windows {param($s) $s.connectionConfirmed -eq $false}|Out-Null
$s=Send-QA Editor create @{name='Lobby QA';capacity=8;isPrivate=$false};$roomId=$s.id;$code=$s.code
$s=Send-QA Editor start;Check minimum-two ($s.lobbyPhase -eq 'Waiting' -and $s.error -match '2 players') $s
$s=Send-QA Windows code @{code=$code};$s=Wait-QA Windows {param($s) $s.state -eq 'Connected' -and $s.connectionConfirmed -and !$s.busy};Check initial-not-ready (!$s.localReady) $s
$s=Send-QA Windows start;Check client-start-denied ($s.error -match 'Only the Host' -and $s.lobbyPhase -eq 'Waiting') $s
$s=Send-QA Windows readyUI;$s=Wait-QA Editor {param($s) !$s.startBlocked};Check ready-shared (@($s.ready|Where-Object {$_}).Count -eq 1) $s
$s=Send-QA Windows readyUI;$s=Wait-QA Editor {param($s) $s.startBlocked -match 'ready'};Check cancel-ready-shared (@($s.ready|Where-Object {$_}).Count -eq 0) $s
$s=Send-QA Editor start;Check unready-start-denied ($s.lobbyPhase -eq 'Waiting' -and !$s.start.matchId) $s
$s=Send-QA Windows ready @{ready=$true};Wait-QA Editor {param($s) !$s.startBlocked}|Out-Null
$s=Send-QA Windows holdAck @{ready=$true}
# Dispatch start without waiting so the other process can leave during confirmation.
$seq.Editor++;@{sequence=$seq.Editor;action='start'}|ConvertTo-Json|Set-Content (Join-Path $root 'Editor/command.json.tmp');[IO.File]::Move((Join-Path $root 'Editor/command.json.tmp'),(Join-Path $root 'Editor/command.json'),$true)
Wait-QA Windows {param($s) $s.lobbyPhase -eq 'Starting'}|Out-Null
$s=Send-QA Windows ready @{ready=$false};Check ready-frozen ($s.error -eq 'ReadyUnavailable' -and $s.localReady) $s
$s=Send-QA Windows leave;$s=Wait-QA Editor {param($s) !$s.busy -and $s.lobbyPhase -eq 'Waiting'};Check start-cancelled-on-leave ($s.error -eq 'PlayersChangedDuringStart' -and !$s.start.matchId) $s
Start-Sleep -Milliseconds 1200;$s=Send-QA Windows refresh;Check cancelled-room-unlocked (@($s.rooms|Where-Object id -eq $roomId).Count -eq 1) $s
$s=Send-QA Windows code @{code=$code};Wait-QA Windows {param($s) $s.connectionConfirmed -and !$s.busy}|Out-Null;$s=Read-QA Windows;Check rejoin-clears-ready (!$s.localReady) $s
$s=Send-QA Windows ready @{ready=$true};Wait-QA Editor {param($s) !$s.startBlocked}|Out-Null
$s=Send-QA Editor start;Check ack-timeout-rollback ($s.lobbyPhase -eq 'Waiting' -and $s.error -match 'Timeout' -and !$s.start.matchId) $s
$s=Send-QA Windows holdAck @{ready=$false};$s=Send-QA Editor startTwice;$hostStart=$s.start;Check start-once ($s.lobbyPhase -eq 'Started' -and $hostStart.players.Count -eq 2) $s
$s=Wait-QA Windows {param($s) $s.lobbyPhase -eq 'Started' -and $s.start.matchId};Check same-start ($s.start.matchId -eq $hostStart.matchId -and $s.start.seed -eq $hostStart.seed -and ($s.start.players -join ',') -eq ($hostStart.players -join ',') -and $s.start.ruleVersion -eq $hostStart.ruleVersion) $s
Write-Output 'STARTED_FOR_CAPTURE';Start-Sleep -Seconds 8
$s=Send-QA Editor start;Check duplicate-after-start ($s.start.matchId -eq $hostStart.matchId) $s
$s=Send-QA Windows ready @{ready=$false};Check ready-after-start-denied ($s.error -eq 'ReadyUnavailable') $s
$s=Send-QA Windows leave;$s=Send-QA Windows code @{code=$code};Check started-room-locked ($s.state -eq 'Failed' -and !$s.id) $s
$s=Send-QA Editor leave
# Reverse Host role, verify fresh state and the same start contract.
$s=Send-QA Windows create @{name='Windows Lobby QA';capacity=2;isPrivate=$true};$code=$s.code
$s=Send-QA Editor code @{code=$code};Wait-QA Editor {param($s) $s.state -eq 'Connected' -and $s.connectionConfirmed -and !$s.busy}|Out-Null
$s=Send-QA Editor ready @{ready=$true};Wait-QA Windows {param($s) !$s.startBlocked}|Out-Null
$s=Send-QA Windows startUI;$reverse=$s.start;$s=Wait-QA Editor {param($s) $s.start.matchId};Check reverse-start ($s.start.matchId -eq $reverse.matchId -and $s.start.seed -eq $reverse.seed) $s
$s=Send-QA Windows leave;$s=Wait-QA Editor {param($s) $s.state -eq 'Idle'};Check host-delete-cleanup (!$s.start.matchId -and !$s.localReady) $s
$s=Send-QA Windows quit
Write-Output 'ALL_LOBBY_QA_PASSED'



