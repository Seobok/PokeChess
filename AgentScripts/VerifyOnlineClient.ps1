$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '../TestResults/OnlineClientIntegration'
$seq=@{Editor=0;Windows=0}
function Read-QA($role){try{Get-Content (Join-Path $root "$role/status.json") -Raw|ConvertFrom-Json}catch{$null}}
function Read-Combat($role){try{Get-Content (Join-Path $root "$role/combat.json") -Raw|ConvertFrom-Json}catch{$null}}
function Wait-QA($role,$condition){$end=(Get-Date).AddSeconds(55);do{$s=Read-QA $role;if($s -and (& $condition $s)){return $s};Start-Sleep -Milliseconds 200}while((Get-Date)-lt $end);throw "Timeout ${role}: $($s|ConvertTo-Json -Compress -Depth 4)"}
function Send-QA($role,$action,$fields=@{}){$seq[$role]++;$c=@{sequence=$seq[$role];action=$action};foreach($k in $fields.Keys){$c[$k]=$fields[$k]};$path=Join-Path $root "$role/command.json";$c|ConvertTo-Json|Set-Content ($path+'.tmp');for($attempt=0;$attempt -lt 20;$attempt++){try{[IO.File]::Move(($path+'.tmp'),$path,$true);break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 50}};$s=Wait-QA $role {param($s) $s.sequence -eq $seq[$role] -and !$s.busy};if($s.qaError){throw $s.qaError};return $s}
function Check($name,$pass,$data){if(!$pass){throw "FAIL $name"};$data|ConvertTo-Json -Depth 14|Set-Content (Join-Path $root "$name.json");Write-Output "PASS $name"}
function Read-Client($role){try{Get-Content (Join-Path $root "$role/client.json") -Raw|ConvertFrom-Json}catch{$null}}
function Click($role,$name){Send-QA $role gameUI @{name=$name}|Out-Null;if($name -eq 'New match' -or $name -eq 'Leave match'){return (Wait-QA $role {param($s) $s.state -eq 'Idle'})};Wait-QA $role {param($s) $s.synchronized -and !$s.pendingCommand}}
foreach($role in @('Editor','Windows')){$s=Wait-QA $role {param($s) $s.state -eq 'Idle'};$seq[$role]=$s.sequence}
foreach($hostRole in @('Editor','Windows')){
    $clientRole=if($hostRole -eq 'Editor'){'Windows'}else{'Editor'};$prefix=$hostRole.ToLower()
    $s=Send-QA $hostRole create @{name='Integrated client';capacity=2;isPrivate=$true};Send-QA $clientRole code @{code=$s.code}|Out-Null
    Wait-QA $clientRole {param($s) $s.identityBound -and $s.connectionConfirmed}|Out-Null;Send-QA $clientRole ready @{ready=$true}|Out-Null
    Wait-QA $hostRole {param($s) !$s.startBlocked}|Out-Null;Send-QA $hostRole start|Out-Null;Send-QA $hostRole combatSpeed @{speed=0}|Out-Null
    $s=Wait-QA $clientRole {param($s) $s.synchronized -and (Read-Client $clientRole).online -and (Read-Client $clientRole).profiles -eq 2}
    $hostId=(Read-QA $hostRole).match.playerId;$clientId=$s.match.playerId
    Check "$prefix-integrated-entry" ((Read-Client $clientRole).active -and (Read-Client $clientRole).shopExpanded) (Read-Client $clientRole)
    $s=Click $clientRole ShopCard_0;$unit=$s.match.units[0].id;Check "$prefix-shop-buy" ($s.ack.accepted -and $s.match.gold -eq 4 -and $s.match.units.Count -eq 1) $s
    Send-QA $clientRole clientSelect @{unitId=$unit}|Out-Null;Check "$prefix-unit-context" ((Read-Client $clientRole).detail.Contains('(R1)')) (Read-Client $clientRole)
    $version=(Read-QA $clientRole).match.revision; Send-QA $clientRole clientStaleDrag @{unitId=$unit;column=2;row=1}|Out-Null; Check "$prefix-stale-drag-cancelled" ((Read-QA $clientRole).match.revision -eq $version -and !(Read-Client $clientRole).dragging) (Read-Client $clientRole)
    Send-QA $clientRole clientDrag @{unitId=$unit;column=2;row=1}|Out-Null;$s=Wait-QA $clientRole {param($s) $s.synchronized -and $s.match.units[0].placement -eq 1}
    Check "$prefix-drag-deploy" ($s.ack.accepted -and $s.match.units[0].column -eq 2 -and $s.match.units[0].row -eq 1) $s
    Click $hostRole "Profile_$clientId"|Out-Null;Check "$prefix-sidebar-public-scout" ((Read-Client $hostRole).observed -eq $clientId -and (Read-QA $hostRole).match.units.Count -eq 0) (Read-Client $hostRole)
    Click $clientRole "Profile_$hostId"|Out-Null;$version=(Read-QA $clientRole).match.revision
    Send-QA $clientRole clientDrag @{unitId=$unit;column=3;row=1}|Out-Null
    Check "$prefix-scout-readonly" ((Read-QA $clientRole).match.revision -eq $version -and !(Read-Client $clientRole).dragging) (Read-QA $clientRole)
    Click $clientRole 'My board'|Out-Null;Send-QA $clientRole clientSelect @{unitId=$unit}|Out-Null;$s=Click $clientRole Sell
    Check "$prefix-sell" ($s.ack.accepted -and $s.match.units.Count -eq 0 -and $s.match.gold -eq 5) $s
    $s=Click $clientRole Lock;Check "$prefix-lock" ($s.match.locked) $s;$s=Click $clientRole Lock;Check "$prefix-unlock" (!$s.match.locked) $s
    Send-QA $hostRole clientRankFixture @{targetPlayer=$clientId}|Out-Null;Wait-QA $clientRole {param($s) $s.match.units.Count -eq 2 -and $s.match.gold -eq 20}|Out-Null
    Send-QA $clientRole clientSelect @{unitId='rank-b'}|Out-Null;$s=Click $clientRole ShopCard_0
    Wait-QA $clientRole {param($s) (Read-Client $clientRole).selected -eq 'rank-a' -and (Read-Client $clientRole).detail.Contains('(R2)')}|Out-Null
    Check "$prefix-rank-selection-items" ($s.match.units.Count -eq 1 -and $s.match.units[0].rank -eq 2 -and $s.match.inventory -contains 'qa-item') (Read-Client $clientRole)
    Click $clientRole Sell|Out-Null;$s=Click $clientRole 'Buy XP';Check "$prefix-xp" ($s.ack.accepted -and $s.match.level -eq 3) $s
    $gold=$s.match.gold;$s=Click $clientRole Reroll;Check "$prefix-reroll" ($s.ack.accepted -and $s.match.gold -eq ($gold-2)) $s
    Click $hostRole 'My board'|Out-Null;Send-QA $hostRole combatFixture|Out-Null;Send-QA $hostRole advanceRound|Out-Null;Send-QA $hostRole combatSpeed @{speed=1}|Out-Null
    Wait-QA $clientRole {param($s) (Read-Combat $clientRole).latest.tick -ge 100}|Out-Null;Send-QA $hostRole combatSpeed @{speed=0}|Out-Null
    Check "$prefix-combat-replay" ((Read-Combat $clientRole).events -gt 0 -and !(Read-Combat $clientRole).error) (Read-Combat $clientRole)
    Click $clientRole Shop|Out-Null;Check "$prefix-expand-combat-shop" ((Read-Client $clientRole).shopExpanded) (Read-Client $clientRole)
    $s=Click $clientRole ShopCard_0;Check "$prefix-combat-purchase-frozen" ($s.ack.accepted -and (Read-Combat $clientRole).latest.units.Count -eq 2) $s
    Click $clientRole "Profile_$hostId"|Out-Null;Wait-QA $clientRole {param($s) (Read-Combat $clientRole).current.battleId}|Out-Null
    Check "$prefix-combat-watch-owner-private" ((Read-Client $clientRole).observed -eq $hostId -and (Read-QA $clientRole).match.playerId -eq $clientId) (Read-Client $clientRole)
    Click $clientRole 'My board'|Out-Null;Send-QA $hostRole combatSpeed @{speed=1}|Out-Null
    Wait-QA $hostRole {param($s) $s.publicState.phase -eq 4}|Out-Null;Send-QA $hostRole combatSpeed @{speed=0}|Out-Null
    $s=Wait-QA $clientRole {param($s) (Read-Client $clientRole).result}
    $r=Read-Client $clientRole;Check "$prefix-round-result" ($r.resultHP.Contains('→') -and $r.resultReward.Contains('XP') -and !$r.final) $r
    Click $clientRole ResultDetails|Out-Null;Write-Output 'ROUND_UI_READY';Start-Sleep -Seconds 12
    Send-QA $hostRole combatSpeed @{speed=20}|Out-Null;Wait-QA $hostRole {param($s) $s.publicState.phase -eq 5}|Out-Null
    $s=Wait-QA $clientRole {param($s) (Read-Client $clientRole).final};$h=Read-QA $hostRole
    Check "$prefix-full-match-final-ui" ($s.publicState.winnerPlayerId -eq $hostId -and $s.publicState.revision -eq $h.publicState.revision -and (Read-Client $clientRole).finalText.Contains('PLACEMENT')) $s
    Send-QA $hostRole assertPool|Out-Null;Write-Output 'FINAL_UI_READY';Start-Sleep -Seconds 15
    Click $clientRole 'New match'|Out-Null;Wait-QA $clientRole {param($s) $s.state -eq 'Idle' -and !(Read-Client $clientRole).online}|Out-Null
    Send-QA $hostRole leave|Out-Null;Check "$prefix-return-rooms" (!(Read-Client $clientRole).online -and !(Read-Client $clientRole).active) (Read-Client $clientRole)
}
$s=Send-QA Editor create @{name='Spectator fixture';capacity=2;isPrivate=$true};Send-QA Windows code @{code=$s.code}|Out-Null
Wait-QA Windows {param($s) $s.identityBound -and $s.connectionConfirmed}|Out-Null;Send-QA Windows ready @{ready=$true}|Out-Null
Send-QA Editor extraPlayers @{capacity=2}|Out-Null;Wait-QA Editor {param($s) !$s.startBlocked}|Out-Null
Send-QA Editor start|Out-Null;Send-QA Editor combatSpeed @{speed=0}|Out-Null;Send-QA Editor combatFixture|Out-Null
Wait-QA Windows {param($s) (Read-Client Windows).profiles -eq 4}|Out-Null;Send-QA Editor advanceRound|Out-Null
Wait-QA Windows {param($s) (Read-Combat Windows).current.battleId}|Out-Null;$own=Read-Combat Windows
$other=((Read-QA Windows).publicState.players|Where-Object {$_.id -ne $own.current.one -and $_.id -ne $own.current.two})[0].id
Click Windows "Profile_$other"|Out-Null;Wait-QA Windows {param($s) (Read-Combat Windows).current.battleId -and (Read-Combat Windows).current.battleId -ne $own.current.battleId -and ((Read-Combat Windows).current.one -eq $other -or (Read-Combat Windows).current.two -eq $other)}|Out-Null
Check 'other-pair-spectator' ((Read-Combat Windows).current.one -eq $other -or (Read-Combat Windows).current.two -eq $other) (Read-Combat Windows)
Click Windows 'My board'|Out-Null;Click Windows Surrender|Out-Null
Wait-QA Windows {param($s) (Read-Client Windows).elimination}|Out-Null;Check 'elimination-notice' (!(Read-Client Windows).buyEnabled -and !(Read-Client Windows).final) (Read-Client Windows)
Click Windows 'Continue watching'|Out-Null;Click Windows "Profile_$other"|Out-Null;Send-QA Editor combatSpeed @{speed=1}|Out-Null
Wait-QA Windows {param($s) (Read-Combat Windows).current.tick -gt 15}|Out-Null
Check 'eliminated-continues-other-combat' ((Read-Client Windows).observed -eq $other -and !(Read-Client Windows).elimination -and (Read-QA Windows).match.eliminated) (Read-Client Windows)
Write-Output 'SPECTATOR_UI_READY';Start-Sleep -Seconds 12;Send-QA Editor combatSpeed @{speed=0}|Out-Null
Send-QA Windows leave|Out-Null;Send-QA Editor leave|Out-Null;Send-QA Windows quit|Out-Null
Write-Output 'ALL_INTEGRATED_CLIENT_QA_PASSED'

