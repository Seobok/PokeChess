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
    $alice = @{username='recovery_alice';password='recovery-original-password'}
    $bob = @{username='recovery_bob';password='recovery-original-password'}
    $signup = Request POST '/auth/register' $alice
    Expect $signup 201
    $code = ($signup.Content | ConvertFrom-Json).recoveryCode
    if ($code -notmatch '^[A-F0-9]{8}(-[A-F0-9]{8}){7}$' -or $signup.Headers['Cache-Control'] -notmatch 'no-store') { throw 'Missing/unsafe recovery issuance' }
    Expect (Request POST '/auth/register' $bob) 201
    function Login($credentials) {
        $response = Request POST '/auth/login?client=app' $credentials
        Expect $response 200
        if ($response.Content -match 'recoveryCode') { throw 'Login must not disclose recovery code' }
        $response.Content | ConvertFrom-Json
    }
    function Headers($token) { @{'Authorization'="Bearer $token";'X-PokeChess-Request'='1'} }
    $aliceToken = (Login $alice).token
    $bobToken = (Login $bob).token
    $updated = 'recovery-updated-password'
    Expect (Request POST '/auth/recover' @{username=$bob.username;recoveryCode=$code;newPassword=$updated}) 401
    Expect (Request POST '/auth/recover' @{username='missing_user';recoveryCode=$code;newPassword=$updated}) 401
    Expect (Request POST '/auth/recover' @{username=$alice.username;recoveryCode=$code;newPassword='short'}) 400
    Expect (Request POST '/auth/recover' @{username=$alice.username;recoveryCode='invalid-code';newPassword=$updated}) 401
    $reset = Request POST '/auth/recover' @{username='RECOVERY_ALICE';recoveryCode=$code.Replace('-','').ToLowerInvariant();newPassword=$updated}
    Expect $reset 200
    $replacement = ($reset.Content | ConvertFrom-Json).recoveryCode
    if (!$replacement -or $replacement -eq $code -or $reset.Content -match '"token"' -or $reset.Headers['Set-Cookie'] -notmatch '__Host-pokechess=;' -or $reset.Headers['Cache-Control'] -notmatch 'no-store') { throw 'Unsafe recovery success response' }
    Expect (Request GET '/auth/me' $null (Headers $aliceToken)) 401
    Expect (Request GET '/auth/me' $null (Headers $bobToken)) 200
    Expect (Request POST '/auth/recover' @{username=$alice.username;recoveryCode=$code;newPassword=$alice.password}) 401
    Expect (Request POST '/auth/login?client=app' $alice) 401
    $newToken = (Login @{username=$alice.username;password=$updated}).token
    Expect (Request POST '/auth/recovery-code/rotate' @{currentPassword=$alice.password} (Headers $newToken)) 401
    $rotation = Request POST '/auth/recovery-code/rotate' @{currentPassword=$updated} (Headers $newToken)
    Expect $rotation 200
    $rotated = ($rotation.Content | ConvertFrom-Json).recoveryCode
    if (!$rotated -or $rotated -eq $replacement) { throw 'Missing replacement on rotation' }
    Expect (Request POST '/auth/recover' @{username=$alice.username;recoveryCode=$replacement;newPassword=$alice.password}) 401
    $resetAgain = Request POST '/auth/recover' @{username=$alice.username;recoveryCode=$rotated;newPassword=$alice.password}
    Expect $resetAgain 200
    $finalCode = ($resetAgain.Content | ConvertFrom-Json).recoveryCode
    for ($i=0; $i -lt 5; $i++) {
        Expect (Request POST '/auth/recover' @{username=$alice.username;recoveryCode='invalid-code';newPassword=$updated}) 401
    }
    $throttled = Request POST '/auth/recover' @{username=$alice.username;recoveryCode=$finalCode;newPassword=$updated}
    Expect $throttled 429
    if ([int]($throttled.Headers['Retry-After'] | Select-Object -First 1) -lt 1) { throw 'Missing recovery retry-after' }
    $null = Login $alice
    'PASS: HTTP recovery issuance, account binding, uniform failure, password policy, replacement/single-use, no automatic login, session revocation/isolation, reauthenticated rotation, separate recovery throttle.'
} finally {
    if (!$server.HasExited) { $server.Kill(); $server.WaitForExit() }
    # Keep the isolated temporary database for inspection; never copy it into the repository.
}



