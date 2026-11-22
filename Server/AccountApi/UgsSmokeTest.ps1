$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('pokechess-auth-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$start = [Diagnostics.ProcessStartInfo]::new('dotnet')
$start.ArgumentList.Add((Join-Path $PSScriptRoot 'bin/Debug/net10.0/AccountApi.dll'))
$start.WorkingDirectory = $testRoot
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.Environment['ASPNETCORE_ENVIRONMENT'] = 'Development'
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()
$start.Environment['ASPNETCORE_URLS'] = "http://127.0.0.1:$port"
$start.Environment['WebOrigin'] = 'https://localhost'
$start.Environment['AccountDb'] = Join-Path $testRoot 'accounts.db'
$start.Environment['Ugs__KeyId']=''
$start.Environment['Ugs__SecretKey']=''
$server = [Diagnostics.Process]::Start($start)
function Request($method, $path, $body, $headers = @{'X-PokeChess-Request'='1'}) {
    $parameters = @{Method=$method; Uri="http://127.0.0.1:$port$path"; Headers=$headers; SkipHttpErrorCheck=$true}
    if ($null -ne $body) { $parameters.Body = ($body | ConvertTo-Json -Compress); $parameters.ContentType = 'application/json' }
    Invoke-WebRequest @parameters
}
function Expect($response, $status) { if ($response.StatusCode -ne $status) { throw "Expected $status, got $($response.StatusCode)" } }
try {
    $ready = $false
    for ($i=0; $i -lt 50; $i++) {
        if ($server.HasExited) { throw 'Test server exited before becoming ready' }
        try { Expect (Request GET '/auth/me' $null) 401; $ready=$true; break } catch { Start-Sleep -Milliseconds 100 }
    }
    if (!$ready) { throw 'Server did not start' }
    $credentials=@{username='ugs_smoke';password='ugs-smoke-password-12345'}
    Expect (Request POST '/auth/ugs/sign-in' @{playerId='forged';accountId='forged'}) 401
    Expect (Request POST '/auth/register' $credentials) 201
    $login=Request POST '/auth/login?client=app' $credentials
    Expect $login 200
    $headers=@{'Authorization'="Bearer $(($login.Content | ConvertFrom-Json).token)";'X-PokeChess-Request'='1'}
    $response=Request POST '/auth/ugs/sign-in' @{playerId='forged';accountId='forged'} $headers
    Expect $response 503
    if(($response.Content | ConvertFrom-Json).error -ne 'online_not_configured' -or $response.Content -match 'accessToken|sessionToken|secretKey') { throw 'Unsafe unconfigured online response' }
    Expect (Request POST '/auth/ugs/admission' @{roomId='room';nonce=[Convert]::ToBase64String([byte[]]::new(32))} $headers) 503
    Expect (Request GET '/auth/me' $null $headers) 200
    Expect (Request POST '/auth/logout' $null $headers) 204
    Expect (Request POST '/auth/ugs/sign-in' @{} $headers) 401
    'PASS: HTTP own-session requirement, forged identity ignored, missing UGS credentials fail closed, own login remains usable, logout blocks exchange.'
} finally {
    if (!$server.HasExited) { $server.Kill(); $server.WaitForExit() }
    # Keep the isolated temporary database for inspection; never copy it into the repository.
}
