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
