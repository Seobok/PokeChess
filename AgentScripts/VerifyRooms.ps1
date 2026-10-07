$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '../TestResults/WBS5.3'
$seq=@{Editor=0;Windows=0}
function Read-QA($role){try{Get-Content (Join-Path $root "$role/status.json") -Raw | ConvertFrom-Json}catch{$null}}
function Wait-QA($role,$condition){$end=(Get-Date).AddSeconds(65); do{$s=Read-QA $role;if($s -and (& $condition $s)){return $s};Start-Sleep -Milliseconds 300}while((Get-Date)-lt $end);throw "Timeout $role : $($s|ConvertTo-Json -Compress -Depth 5)"}
function Send-QA($role,$action,$fields=@{}){$seq[$role]++;$c=@{sequence=$seq[$role];action=$action};foreach($k in $fields.Keys){$c[$k]=$fields[$k]};$path=Join-Path $root "$role/command.json"; $c|ConvertTo-Json|Set-Content ($path+".tmp");for($attempt=0;$attempt -lt 20;$attempt++){try{[System.IO.File]::Move(($path+".tmp"),$path,$true);break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 50}};Wait-QA $role {param($s) $s.sequence -eq $seq[$role] -and !$s.busy}}
function Check($name,$pass,$data){if(!$pass){throw "FAIL: $name"};$data|ConvertTo-Json -Depth 6|Set-Content (Join-Path $root "$name.json");Write-Output "PASS $name"}
Wait-QA Windows {param($s) $true}|Out-Null
$s=Send-QA Editor create @{name='Public QA';capacity=2;isPrivate=$false};Check public-created ($s.state -eq 'Connected' -and $s.players -eq 1 -and !$s.isPrivate) $s;$publicId=$s.id;$publicCode=$s.code
$s=Send-QA Windows refresh;Check public-listed (@($s.rooms|Where-Object id -eq $publicId).Count -eq 1 -and $s.listState -eq 'Ready') $s
$s=Send-QA Windows id @{id=$publicId};$s=Wait-QA Windows {param($s) $s.state -eq 'Connected' -and $s.replies -gt 0};Check public-id-joined ($s.players -eq 2) $s
$s=Wait-QA Editor {param($s) $s.players -eq 2};Check host-members ($s.members.Count -eq 2 -and @($s.members|Where-Object {$_ -match 'Host'}).Count -eq 1) $s
Start-Sleep -Milliseconds 1200;$s=Send-QA Windows refresh;Check full-room-hidden (@($s.rooms|Where-Object id -eq $publicId).Count -eq 0) $s
$s=Send-QA Windows leave;Check client-left ($s.state -eq 'Idle') $s
$s=Wait-QA Editor {param($s) $s.players -eq 1};Check member-removed ($s.players -eq 1) $s
Start-Sleep -Milliseconds 1200;$s=Send-QA Windows refresh;Check public-reappears (@($s.rooms|Where-Object id -eq $publicId).Count -eq 1) $s
$s=Send-QA Editor leave
$s=Send-QA Editor create @{name='Private QA';capacity=8;isPrivate=$true};Check private-created ($s.state -eq 'Connected' -and $s.isPrivate) $s;$privateId=$s.id;$privateCode=$s.code
Start-Sleep -Milliseconds 1200;$s=Send-QA Windows refresh;Check private-hidden (@($s.rooms|Where-Object id -eq $privateId).Count -eq 0) $s
$s=Send-QA Windows code @{code=(' '+$privateCode.ToLower()+' ')};$s=Wait-QA Windows {param($s) $s.state -eq 'Connected' -and $s.replies -gt 0};Check private-code-joined ($s.players -eq 2) $s
Write-Output 'LOBBY_READY_FOR_CAPTURE'
Start-Sleep -Seconds 12
$s=Send-QA Editor leave;$s=Wait-QA Windows {param($s) $s.state -eq 'Idle'};Check host-delete-returns-idle ($s.players -eq 0) $s
$s=Send-QA Windows code @{code='INVALID-CODE'};Check invalid-code ($s.state -eq 'Failed' -and !$s.id) $s
$s=Send-QA Windows id @{id=$publicId};Check deleted-room ($s.state -eq 'Failed' -and !$s.id) $s
$s=Send-QA Windows create @{name='Windows QA';capacity=8;isPrivate=$false};Check windows-host-created ($s.state -eq 'Connected') $s;$winId=$s.id
$s=Send-QA Editor refresh;Check reverse-public-listed (@($s.rooms|Where-Object id -eq $winId).Count -eq 1) $s
$s=Send-QA Editor id @{id=$winId};$s=Wait-QA Editor {param($s) $s.state -eq 'Connected' -and $s.replies -gt 0};Check reverse-id-joined ($s.players -eq 2) $s
$s=Send-QA Editor leave;$s=Send-QA Windows quit
Write-Output 'ALL_ROOM_QA_PASSED'


