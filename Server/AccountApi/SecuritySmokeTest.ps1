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
    $alice = @{username='security_alice';password='test-original-password-123'}
    $bob = @{username='security_bob';password='test-original-password-123'}
    Expect (Request POST '/auth/register' $alice) 201
    Expect (Request POST '/auth/register' $bob) 201
    function Login($credentials) {
        $response = Request POST '/auth/login?client=app' $credentials
        Expect $response 200
        $response.Content | ConvertFrom-Json
    }
    function Headers($token) { @{'Authorization'="Bearer $token";'X-PokeChess-Request'='1'} }
    $tokens = @()
    for ($i=0; $i -lt 6; $i++) { $tokens += (Login $alice).token }
    $bobToken = (Login $bob).token
    Expect (Request GET '/auth/me' $null (Headers $tokens[0])) 401
    Expect (Request GET '/auth/me' $null (Headers $tokens[5])) 200
    Expect (Request POST '/auth/change-password' @{currentPassword='wrong-current-password';newPassword='test-updated-password-123'} (Headers $tokens[5])) 401
    $change = Request POST '/auth/change-password' @{currentPassword=$alice.password;newPassword='test-updated-password-123'} (Headers $tokens[5])
    Expect $change 204
    if ($change.Headers['Set-Cookie'] -notmatch '__Host-pokechess=;') { throw 'Password change must clear browser cookie' }
    Expect (Request GET '/auth/me' $null (Headers $tokens[5])) 401
    Expect (Request GET '/auth/me' $null (Headers $tokens[4])) 401
    Expect (Request POST '/auth/login?client=app' $alice) 401
    $newToken = (Login @{username=$alice.username;password='test-updated-password-123'}).token
    Expect (Request POST '/auth/logout-all' $null (Headers $newToken)) 204
    Expect (Request GET '/auth/me' $null (Headers $newToken)) 401
    Expect (Request GET '/auth/me' $null (Headers $bobToken)) 200
    for ($i=0; $i -lt 5; $i++) {
        Expect (Request POST '/auth/login?client=app' @{username='SECURITY_BOB';password='wrong-current-password'}) 401
    }
    $throttled = Request POST '/auth/login?client=app' $bob
    Expect $throttled 429
    if ([int]($throttled.Headers['Retry-After'] | Select-Object -First 1) -lt 1) { throw 'Missing retry-after' }
    Expect (Request POST '/auth/logout-all' $null (Headers $bobToken)) 204
    'PASS: HTTP session eviction, current password verification, password change/cookie deletion, all-session revocation, old-password rejection, account isolation, case-normalized throttle and Retry-After.'
} finally {
    if (!$server.HasExited) { $server.Kill(); $server.WaitForExit() }
    # Keep the isolated temporary database for inspection; never copy it into the repository.
}


