$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '../TestResults/WBS5.7'
$seq=@{Editor=0;Windows=0}
function Read-QA($role){try{Get-Content (Join-Path $root "$role/status.json") -Raw|ConvertFrom-Json}catch{$null}}
function Read-Combat($role){try{Get-Content (Join-Path $root "$role/combat.json") -Raw|ConvertFrom-Json}catch{$null}}
function Wait-QA($role,$condition){$end=(Get-Date).AddSeconds(55);do{$s=Read-QA $role;if($s -and (& $condition $s)){return $s};Start-Sleep -Milliseconds 200}while((Get-Date)-lt $end);throw "Timeout ${role}: $($s|ConvertTo-Json -Compress -Depth 4)"}
function Send-QA($role,$action,$fields=@{}){$seq[$role]++;$c=@{sequence=$seq[$role];action=$action};foreach($k in $fields.Keys){$c[$k]=$fields[$k]};$path=Join-Path $root "$role/command.json";$c|ConvertTo-Json|Set-Content ($path+'.tmp');for($attempt=0;$attempt -lt 20;$attempt++){try{[IO.File]::Move(($path+'.tmp'),$path,$true);break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 50}};$s=Wait-QA $role {param($s) $s.sequence -eq $seq[$role] -and !$s.busy};if($s.qaError){throw $s.qaError};return $s}
function Check($name,$pass,$data){if(!$pass){throw "FAIL $name"};$data|ConvertTo-Json -Depth 14|Set-Content (Join-Path $root "$name.json");Write-Output "PASS $name"}
foreach($role in @('Editor','Windows')){$s=Wait-QA $role {param($s) $s.state -eq 'Idle'};$seq[$role]=$s.sequence}
foreach($hostRole in @('Editor','Windows')){
    $clientRole=if($hostRole -eq 'Editor'){'Windows'}else{'Editor'};$prefix=$hostRole.ToLower()
    $s=Send-QA $hostRole create @{name='Combat QA';capacity=2;isPrivate=$true};$code=$s.code
    Send-QA $clientRole code @{code=$code}|Out-Null;Wait-QA $clientRole {param($s) $s.identityBound -and $s.connectionConfirmed}|Out-Null
    Send-QA $clientRole ready @{ready=$true}|Out-Null;Wait-QA $hostRole {param($s) !$s.startBlocked}|Out-Null
    Send-QA $hostRole start|Out-Null;Send-QA $hostRole combatSpeed @{speed=0}|Out-Null
    Wait-QA $clientRole {param($s) $s.synchronized}|Out-Null
    Send-QA $hostRole combatFixture|Out-Null
    Wait-QA $clientRole {param($s) $s.match.units.Count -eq 1}|Out-Null
    Send-QA $hostRole advanceRound|Out-Null
    $s=Wait-QA $clientRole {param($s) $s.publicState.phase -eq 3 -and (Read-Combat $clientRole).latest.battleId}
    $c=Read-Combat $clientRole;Check "$prefix-start-roster" ($c.latest.units.Count -eq 2 -and $c.latest.units[0].rank -gt 0 -and !$c.error) $c
    Send-QA $hostRole combatSpeed @{speed=1}|Out-Null
    Wait-QA $clientRole {param($s) (Read-Combat $clientRole).latest.tick -ge 100}|Out-Null
    Send-QA $hostRole combatSpeed @{speed=0}|Out-Null
    $c=Read-Combat $clientRole;Check "$prefix-live-events" ($c.events -gt 0 -and !$c.error) $c
    Send-QA $clientRole game @{kind='Buy';slot=0}|Out-Null;$s=Wait-QA $clientRole {param($s) $s.synchronized -and !$s.pendingCommand}
    $c=Read-Combat $clientRole;Check "$prefix-combat-buy-frozen" ($s.ack.accepted -and $s.match.units.Count -eq 2 -and $c.latest.units.Count -eq 2) $s
    Send-QA $clientRole clearCombat|Out-Null;Send-QA $clientRole syncState|Out-Null
    Wait-QA $clientRole {param($s) (Read-Combat $clientRole).current.battleId}|Out-Null
    $c=Read-Combat $clientRole;$before=$c.events;Send-QA $clientRole syncState|Out-Null;Start-Sleep -Seconds 1
    $c=Read-Combat $clientRole;Check "$prefix-sync-duplicate" ($c.events -eq $before -and $c.latest.units.Count -eq 2) $c
    Send-QA $hostRole holdCombat @{ready=$true}|Out-Null;Send-QA $hostRole combatSpeed @{speed=1}|Out-Null
    Start-Sleep -Seconds 5;Send-QA $hostRole combatSpeed @{speed=0}|Out-Null;Send-QA $hostRole holdCombat @{ready=$false}|Out-Null
    Wait-QA $clientRole {param($s) (Read-Combat $clientRole).gaps -gt 0}|Out-Null
    $c=Read-Combat $clientRole;Check "$prefix-gap-recovery" ($c.gaps -gt 0 -and !$c.error) $c
    Write-Output 'COMBAT_SCREEN_READY';Start-Sleep -Seconds 15
    Send-QA $hostRole combatSpeed @{speed=20}|Out-Null
    Wait-QA $hostRole {param($s) $s.publicState.phase -eq 5}|Out-Null
    $s=Wait-QA $clientRole {param($s) $s.publicState.phase -eq 5 -and !(Read-Combat $clientRole).playing}
    $h=Read-QA $hostRole;$c=Read-Combat $clientRole
    Check "$prefix-full-match" ($s.publicState.winnerPlayerId -eq $h.match.playerId -and $s.publicState.round -gt 1 -and $c.current.complete -and !$c.error -and $s.publicState.revision -eq $h.publicState.revision) $s
    Send-QA $hostRole assertPool|Out-Null;Check "$prefix-pool-conserved" $true $h
    Write-Output 'FINAL_SCREEN_READY';Start-Sleep -Seconds 10
    Send-QA $clientRole leave|Out-Null;Send-QA $hostRole leave|Out-Null
    Wait-QA $clientRole {param($s) $s.state -eq 'Idle'}|Out-Null
}
Send-QA Windows quit|Out-Null
Write-Output 'ALL_ONLINE_COMBAT_QA_PASSED'
