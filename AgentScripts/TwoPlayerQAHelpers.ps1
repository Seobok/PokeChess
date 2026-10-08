$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot '../TestResults/TwoPlayerRegression'
$seq=@{Editor=0;Windows=0}
function Read-QA($role){try{Get-Content (Join-Path $root "$role/status.json") -Raw|ConvertFrom-Json}catch{$null}}
function Read-Combat($role){try{Get-Content (Join-Path $root "$role/combat.json") -Raw|ConvertFrom-Json}catch{$null}}
function Wait-QA($role,$condition){$end=(Get-Date).AddSeconds(55);do{$s=Read-QA $role;if($s -and (& $condition $s)){return $s};Start-Sleep -Milliseconds 200}while((Get-Date)-lt $end);throw "Timeout ${role}: $($s|ConvertTo-Json -Compress -Depth 4)"}
function Send-QA($role,$action,$fields=@{}){$seq[$role]++;$c=@{sequence=$seq[$role];action=$action};foreach($k in $fields.Keys){$c[$k]=$fields[$k]};$path=Join-Path $root "$role/command.json";$c|ConvertTo-Json|Set-Content ($path+'.tmp');for($attempt=0;$attempt -lt 20;$attempt++){try{[IO.File]::Move(($path+'.tmp'),$path,$true);break}catch{if($attempt -eq 19){throw};Start-Sleep -Milliseconds 50}};$s=Wait-QA $role {param($s) $s.sequence -eq $seq[$role] -and !$s.busy};if($s.qaError){throw $s.qaError};return $s}
function Check($name,$pass,$data){if(!$pass){throw "FAIL $name"};$data|ConvertTo-Json -Depth 14|Set-Content (Join-Path $root "$name.json");Write-Output "PASS $name"}
function Read-Client($role){try{Get-Content (Join-Path $root "$role/client.json") -Raw|ConvertFrom-Json}catch{$null}}
function Click($role,$name){Send-QA $role gameUI @{name=$name}|Out-Null;if($name -eq 'New match' -or $name -eq 'Leave match'){return (Wait-QA $role {param($s) $s.state -eq 'Idle'})};Wait-QA $role {param($s) $s.synchronized -and !$s.pendingCommand}}
