param([string]$ApiDllPath=(Join-Path $PSScriptRoot "bin/Debug/net10.0/AccountApi.dll"))
$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) ('pokechess-ops-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory $root | Out-Null
$server=$null
function StartServer($trusted) {
    $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0); $listener.Start(); $script:port=$listener.LocalEndpoint.Port; $listener.Stop()
    $info=[Diagnostics.ProcessStartInfo]::new('dotnet'); $info.ArgumentList.Add(([IO.Path]::GetFullPath($ApiDllPath)))
    $info.UseShellExecute=$false; $info.CreateNoWindow=$true; $info.WorkingDirectory=$root
    $info.Environment['ASPNETCORE_ENVIRONMENT']='Production'; $info.Environment['ASPNETCORE_URLS']="http://127.0.0.1:$port"
    $info.Environment['WebOrigin']='https://localhost'; $info.Environment['TrustedProxies']=$trusted
    $info.Environment['AccountDb']=Join-Path $root 'accounts.db'
    $script:server=[Diagnostics.Process]::Start($info)
    for($i=0;$i -lt 60;$i++) { try { $null=Invoke-WebRequest "http://127.0.0.1:$port/auth/me" -SkipHttpErrorCheck; return } catch { if($server.HasExited){throw 'Startup failed'}; Start-Sleep -Milliseconds 100 } }
    throw 'Startup timeout'
}
function Request($method,$path,$body,$token,$proto='https') {
    $h=@{'Host'='game.example.com';'X-PokeChess-Request'='1';'X-Forwarded-Proto'=$proto;'X-Forwarded-For'='192.0.2.9'}
    if($token){$h.Authorization="Bearer $token"}
    $p=@{Method=$method;Uri="http://127.0.0.1:$port$path";Headers=$h;SkipHttpErrorCheck=$true}
    if($body){$p.Body=($body|ConvertTo-Json -Compress);$p.ContentType='application/json'}
    Invoke-WebRequest @p
}
function Expect($r,$status){if($r.StatusCode -ne $status){throw "Expected $status, got $($r.StatusCode)"}}
function StopServer {if($server -and !$server.HasExited){$server.Kill();$server.WaitForExit()}}
try {
    StartServer ''
    Expect (Request GET '/auth/me' $null $null) 400 # unset allowlist never trusts forwarded headers
    StopServer
    StartServer '192.0.2.1'
    Expect (Request GET '/auth/me' $null $null) 400 # untrusted peer cannot forge HTTPS
    StopServer
    StartServer '127.0.0.1'
    Expect (Request GET '/auth/me' $null $null 'http') 400
    $credentials=@{username='operations_user';password='operations-smoke-password-123'}
    Expect (Request POST '/auth/register' $credentials $null) 201
    $login=Request POST '/auth/login?client=app' $credentials $null; Expect $login 200
    $token=($login.Content|ConvertFrom-Json).token
    $me=Request GET '/auth/me' $null $token; Expect $me 200
    if(!$me.Headers.ContainsKey('Strict-Transport-Security')){throw 'Missing HSTS'}
    StopServer
    StartServer '127.0.0.1'
    Expect (Request GET '/auth/me' $null $token) 200
    Expect (Request POST '/auth/logout' $null $token) 204
    Expect (Request GET '/auth/me' $null $token) 401
    Write-Host 'PASS: untrusted proxy rejection, HTTPS enforcement, HSTS, process restart session persistence, logout.'
} finally {StopServer}
