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
    $credentials = @{username='smoke_user';password='smoke-test-password-123'}
    Expect (Request POST '/auth/register' $credentials) 201
    Expect (Request POST '/auth/register' $credentials) 409
    Expect (Request POST '/auth/login?client=app' @{username='smoke_user';password='wrong-password-123'}) 401
    Expect (Request POST '/auth/login?client=app' @{username='missing_user';password='smoke-test-password-123'}) 401
    Expect (Request POST '/auth/login' $credentials @{'Origin'='https://evil.example';'X-PokeChess-Request'='1'}) 403
    Expect (Request POST '/auth/login' $credentials @{}) 403
    $login = Request POST '/auth/login?client=app' $credentials
    Expect $login 200
    $data = $login.Content | ConvertFrom-Json
    $headers = @{'Authorization'="Bearer $($data.token)";'X-PokeChess-Request'='1'}
    $me = Request GET '/auth/me' $null $headers
    Expect $me 200
    if (($me.Content | ConvertFrom-Json).accountId -ne $data.accountId) { throw 'Wrong account binding' }
    $web = Request POST '/auth/login' $credentials
    Expect $web 200
    if ($web.Content -match '"token"' -or $web.Headers['Set-Cookie'] -notmatch 'httponly' -or $web.Headers['Set-Cookie'] -notmatch 'secure') { throw 'Unsafe web session response' }
    Expect (Request POST '/auth/logout' $null $headers) 204
    Expect (Request GET '/auth/me' $null $headers) 401
    $limited=$false
    for ($i=0; $i -lt 35; $i++) { if ((Request GET '/auth/me' $null).StatusCode -eq 429) { $limited=$true; break } }
    if (!$limited) { throw 'Rate limit missing' }
    'PASS: registration, duplicate, wrong/unknown credentials, origin/header defense, native session, web cookie attributes, logout revocation, rate limit.'
} finally {
    if (!$server.HasExited) { $server.Kill(); $server.WaitForExit() }
    # Keep the isolated temporary database for inspection; never copy it into the repository.
}


