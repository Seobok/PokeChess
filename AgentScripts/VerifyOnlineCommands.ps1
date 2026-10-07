$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '../TestResults/WBS5.5'
$seq=@{Editor=0;Windows=0}
function Read-QA($role){try{Get-Content (Join-Path $root "$role/status.json") -Raw | ConvertFrom-Json}catch{$null}}
function Wait-QA($role,$condition){$end=(Get-Date).AddSeconds(65); do{$s=Read-QA $role;if($s -and (& $condition $s)){return $s};Start-Sleep -Milliseconds 300}while((Get-Date)-lt $end);throw "Timeout $role : $($s|ConvertTo-Json -Compress -Depth 5)"}
function Send-QA($role,$action,$fields=@{}){$seq[$role]++;$c=@{sequence=$seq[$role];action=$action};foreach($k in $fields.Keys){$c[$k]=$fields[$k]};$path=Join-Path $root "$role/command.json"; $c|ConvertTo-Json|Set-Content ($path+".tmp");for($attempt=0;$attempt -lt 20;$attempt++){try{[System.IO.File]::Move(($path+".tmp"),$path,$true);break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 50}};Wait-QA $role {param($s) $s.sequence -eq $seq[$role] -and !$s.busy}}
function Check($name,$pass,$data){if(!$pass){throw "FAIL: $name"};$data|ConvertTo-Json -Depth 6|Set-Content (Join-Path $root "$name.json");Write-Output "PASS $name"}


function Owned($report){$m=$report.match|ConvertTo-Json -Depth 8|ConvertFrom-Json;$m.nextSequence=0;return $m|ConvertTo-Json -Depth 8 -Compress}
Wait-QA Windows {param($s) !$s.identityBound}|Out-Null
$s=Send-QA Editor create @{name='Command QA';capacity=8;isPrivate=$false};$code=$s.code
$s=Send-QA Windows code @{code=$code};$s=Wait-QA Windows {param($s) $s.connectionConfirmed -and $s.identityBound -and !$s.busy};Check bound-client $s.identityBound $s
$s=Send-QA Windows ready @{ready=$true};Wait-QA Editor {param($s) !$s.startBlocked}|Out-Null
$s=Send-QA Editor start;$s=Wait-QA Editor {param($s) $s.match.matchId};Check host-match-created ($s.match.gold -eq 5 -and $s.match.shop.Count -eq 5) $s
$s=Wait-QA Windows {param($s) $s.match.matchId -and !$s.pendingCommand};Check owner-state-received ($s.match.gold -eq 5 -and $s.match.playerId -ne (Read-QA Editor).match.playerId) $s
$hostId=(Read-QA Editor).match.playerId;$before=Owned $s
foreach($bad in @(@{targetPlayer=$hostId},@{stale=$true},@{wrongRound=$true},@{wrongPhase=$true},@{wrongMatch=$true},@{badVersion=$true})){
  $fields=@{kind='Buy';slot=0};foreach($k in $bad.Keys){$fields[$k]=$bad[$k]};$s=Send-QA Windows game $fields;Check ('reject-'+($bad.Keys|Select-Object -First 1)) (!$s.ack.accepted -and (Owned $s) -eq $before -and !$s.qaError) $s
}
$s=Send-QA Windows game @{kind='Buy';slot=0};Check client-buy ($s.ack.accepted -and $s.match.gold -eq 4 -and $s.match.units.Count -eq 1) $s;$unit=$s.match.units[0].id;$afterBuy=Owned $s
$s=Send-QA Windows repeat;Check duplicate-buy ($s.ack.accepted -and (Owned $s) -eq $afterBuy) $s
$s=Send-QA Windows repeatChanged;Check conflicting-duplicate ($s.ack.code -eq 'DuplicateConflict' -and (Owned $s) -eq $afterBuy) $s
$s=Send-QA Windows game @{kind='Move';unitId=$unit;column=0;row=0};Check client-move ($s.ack.accepted -and $s.match.units[0].placement -eq 1) $s
$s=Send-QA Editor game @{kind='Buy';slot=0};Check host-buy-same-path ($s.ack.accepted -and $s.match.gold -eq 4 -and $s.match.units.Count -eq 1) $s;$hostUnit=$s.match.units[0].id
$hostRevision=$s.match.revision;$s=Wait-QA Windows {param($s) $s.match.revision -eq $hostRevision};$before=Owned $s;$s=Send-QA Windows game @{kind='Move';unitId=$hostUnit;column=1;row=0};Check foreign-unit-rejected ($s.ack.code -eq 'UnitNotOwned' -and (Owned $s) -eq $before) $s
$s=Send-QA Windows game @{kind='Sell';unitId=$unit};Check client-sell ($s.ack.accepted -and $s.match.gold -eq 5 -and $s.match.units.Count -eq 0) $s
$s=Send-QA Windows gameUI @{name='Shop 1'};Check ui-buy (!$s.qaError -and $s.ack.accepted -and $s.match.gold -eq 4 -and $s.match.units.Count -eq 1) $s
$s=Send-QA Windows gameUI @{name='B0'};$s=Send-QA Windows gameUI @{name='0,0'};Check ui-move (!$s.qaError -and $s.ack.accepted -and $s.match.units[0].placement -eq 1) $s
$s=Send-QA Windows gameUI @{name='Sell selected'};Check ui-sell (!$s.qaError -and $s.ack.accepted -and $s.match.gold -eq 5 -and $s.match.units.Count -eq 0) $s
$s=Send-QA Windows game @{kind='Lock';locked=$true};Check lock ($s.ack.accepted -and $s.match.locked) $s
$s=Send-QA Windows game @{kind='Lock';locked=$false};Check unlock ($s.ack.accepted -and !$s.match.locked) $s
$s=Send-QA Windows game @{kind='Reroll'};Check reroll ($s.ack.accepted -and $s.match.gold -eq 3) $s
$s=Send-QA Windows game @{kind='BuyXP'};Check insufficient-gold (!$s.ack.accepted -and $s.match.gold -eq 3) $s
$s=Send-QA Editor game @{kind='Sell';unitId=$hostUnit};$s=Send-QA Editor gameUI @{name='Buy XP'};Check ui-xp (!$s.qaError -and $s.ack.accepted -and $s.match.gold -eq 1 -and $s.match.level -ge 2) $s
$s=Send-QA Editor dropAck;$s=Send-QA Windows game @{kind='Buy';slot=2};Check delayed-ack ($s.pendingCommand -and $s.match.gold -eq 2) $s
$s=Send-QA Windows retryCommand;Check retry-without-double-charge (!$s.pendingCommand -and $s.ack.accepted -and $s.match.gold -eq 2 -and $s.match.units.Count -eq 1) $s
Check owner-filtered-state ($s.match.units.Count -eq 1 -and @($s.match.units|Where-Object id -eq $hostUnit).Count -eq 0) $s
Write-Output 'MATCH_READY_FOR_UI_CAPTURE';Start-Sleep -Seconds 8
$s=Send-QA Windows game @{kind='Surrender'};Check surrender ($s.ack.accepted -and $s.match.eliminated -and $s.match.phase -eq 5) $s
$s=Wait-QA Editor {param($s) $s.match.phase -eq 5};Check public-result-updated (@($s.match.players|Where-Object eliminated).Count -eq 1) $s
$s=Send-QA Windows leave;$s=Send-QA Editor leave
# Reverse hosting role.
$s=Send-QA Windows create @{name='Reverse Commands';capacity=2;isPrivate=$true};$code=$s.code
$s=Send-QA Editor code @{code=$code};Wait-QA Editor {param($s) $s.identityBound -and $s.connectionConfirmed -and !$s.busy}|Out-Null
$s=Send-QA Editor ready @{ready=$true};Wait-QA Windows {param($s) !$s.startBlocked}|Out-Null;$s=Send-QA Windows start
Wait-QA Editor {param($s) $s.match.matchId -and !$s.pendingCommand}|Out-Null;$s=Send-QA Editor gameUI @{name='Shop 0'};Check reverse-ui-buy (!$s.qaError -and $s.ack.accepted -and $s.match.gold -eq 4) $s
$s=Send-QA Windows leave;$s=Wait-QA Editor {param($s) $s.state -eq 'Idle'};Check cleanup-clears-match (!$s.match.matchId -and !$s.identityBound -and !$s.pendingCommand) $s
$s=Send-QA Windows quit
Write-Output 'ALL_ONLINE_COMMAND_QA_PASSED'

