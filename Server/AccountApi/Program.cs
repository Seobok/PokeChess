using System.Threading.RateLimiting;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.RateLimiting;

if(args.Length==3 && args[0]=="backup") {
    if(!File.Exists(args[1])) throw new FileNotFoundException("Source database missing.");
    new AccountStore(args[1]).BackupTo(args[2]);
    Console.WriteLine("Verified database backup created."); return;
}
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 4096);
builder.Services.AddSingleton(new AccountStore(builder.Configuration["AccountDb"] ?? "data/accounts.db"));
builder.Services.AddSingleton<PasswordWorkGate>();
builder.Services.AddHostedService<AccountMaintenance>();
builder.Services.Configure<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>(o => {
    o.ForwardedHeaders=Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto;
    o.ForwardLimit=1; o.KnownIPNetworks.Clear(); o.KnownProxies.Clear();
    foreach(var ip in (builder.Configuration["TrustedProxies"]??"").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)) o.KnownProxies.Add(System.Net.IPAddress.Parse(ip));
    if(!string.IsNullOrWhiteSpace(builder.Configuration["TrustedProxies"]) && o.KnownProxies.Count==0) throw new InvalidOperationException("TrustedProxies must contain explicit IP addresses.");
});
var ugsOptions=builder.Configuration.GetSection("Ugs").Get<UgsOptions>()??new UgsOptions();
builder.Services.AddSingleton(ugsOptions);
builder.Services.AddSingleton<IUgsIdentityIssuer>(_=>new UnityCustomIdIssuer(new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(15) },ugsOptions));
builder.Services.AddSingleton<OnlineIdentityService>();
builder.Services.AddRateLimiter(o => {
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter((context.Connection.RemoteIpAddress?.ToString() ?? "unknown")+(context.Request.Path.StartsWithSegments("/auth/ugs")?":online":":account"), _ =>
            new FixedWindowRateLimiterOptions { PermitLimit = context.Request.Path.StartsWithSegments("/auth/ugs")?120:30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
// Never enable forwarded headers with an empty allowlist (which means trust all).
if(!string.IsNullOrWhiteSpace(builder.Configuration["TrustedProxies"])) app.UseForwardedHeaders();
if(!app.Environment.IsDevelopment()) {
    app.UseExceptionHandler(handler=>handler.Run(async c=> { c.Response.StatusCode=500; await c.Response.WriteAsJsonAsync(new { error="server_error" }); }));
    app.UseHsts();
}
var webOrigin = builder.Configuration["WebOrigin"];
if (!Uri.TryCreate(webOrigin, UriKind.Absolute, out var origin) || origin.Scheme != "https" || origin.GetLeftPart(UriPartial.Authority) != webOrigin)
    throw new InvalidOperationException("Set WebOrigin to the exact HTTPS origin serving the web client.");
app.Use(async (context, next) => {
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (!context.Request.IsHttps && !(app.Environment.IsDevelopment() && context.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip)))
    { context.Response.StatusCode = 400; return; }
    // Same-origin deployment only. Never trust a client-provided Origin for authorization.
    var requestOrigin = context.Request.Headers.Origin.ToString();
    if ((requestOrigin.Length > 0 && requestOrigin != webOrigin) ||
        (context.Request.Method != "GET" && !context.Request.Headers.ContainsKey("X-PokeChess-Request")))
    { context.Response.StatusCode = 403; return; }
    await next(context);
});
app.UseRateLimiter();
const string cookieName = "__Host-pokechess";
string? Token(HttpContext c) {
    var header = c.Request.Headers.Authorization.ToString();
    return header.StartsWith("Bearer ", StringComparison.Ordinal) ? header[7..] : c.Request.Cookies[cookieName];
}
CookieOptions Cookie() => new() { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromDays(7) };
bool Valid(Credentials c) => c.Username is not null && Regex.IsMatch(c.Username, "\\A[a-zA-Z0-9_]{3,32}\\z") && c.Password is not null && c.Password.Length is >= 15 and <= 128;
bool ValidPassword(string? password) => password is not null && password.Length is >= 15 and <= 128;
IResult Retry(HttpContext context, int seconds, int status) {
    context.Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
    return Results.Json(new { error = status == 429 ? "too_many_attempts" : "server_busy" }, statusCode: status);
}
async Task<IResult> Work(HttpContext context, PasswordWorkGate gate, Func<IResult> action) {
    using var lease = gate.TryEnter();
    if (lease is null) return Retry(context, 1, 503);
    // Do not release the slot on request cancellation while Argon2 is still running.
    return await Task.Run(action);
}
IResult LoginResponse(AuthResult result, HttpContext context) {
    if (result.Status == AuthStatus.Throttled) return Retry(context, result.RetryAfterSeconds, 429);
    if (result.Status != AuthStatus.Success) return Results.Unauthorized();
    if (context.Request.Query["client"] == "app") return Results.Ok(new { accountId = result.AccountId, token = result.Token, expiresIn = 604800 });
    context.Response.Cookies.Append(cookieName, result.Token!, Cookie());
    return Results.Ok(new { accountId = result.AccountId, expiresIn = 604800 });
}
app.MapPost("/auth/register", async (Credentials c, HttpContext context, AccountStore store, PasswordWorkGate gate) => {
    if (!Valid(c)) return Results.BadRequest(new { error = "invalid_credentials_format" });
    return await Work(context, gate, () => {
        var code = store.Register(c.Username!.ToLowerInvariant(), c.Password!);
        return code is not null ? Results.Json(new { recoveryCode = code }, statusCode: 201)
            : Results.Conflict(new { error = "username_unavailable" });
    });
});
app.MapPost("/auth/login", async (Credentials c, HttpContext context, AccountStore store, PasswordWorkGate gate) => {
    if (!Valid(c)) return Results.Unauthorized();
    return await Work(context, gate, () => LoginResponse(store.Login(c.Username!.ToLowerInvariant(), c.Password!), context));
});
app.MapGet("/auth/me", (HttpContext context, AccountStore store) => {
    var token = Token(context); var id = token is null ? null : store.Resolve(token);
    return id is null ? Results.Unauthorized() : Results.Ok(new { accountId = id });
});
app.MapPost("/auth/logout", (HttpContext context, AccountStore store) => {
    var token = Token(context); if (token is not null) store.Revoke(token);
    context.Response.Cookies.Delete(cookieName, Cookie()); return Results.NoContent();
});
app.MapPost("/auth/logout-all", (HttpContext context, AccountStore store) => {
    var token = Token(context);
    if (token is null || !store.RevokeAll(token)) return Results.Unauthorized();
    context.Response.Cookies.Delete(cookieName, Cookie()); return Results.NoContent();
});
app.MapPost("/auth/change-password", async (PasswordChange c, HttpContext context, AccountStore store, PasswordWorkGate gate) => {
    if (!ValidPassword(c.CurrentPassword) || !ValidPassword(c.NewPassword) || c.CurrentPassword == c.NewPassword)
        return Results.BadRequest(new { error = "invalid_password_format" });
    var token = Token(context);
    if (token is null) return Results.Unauthorized();
    return await Work(context, gate, () => {
        var result = store.ChangePassword(token, c.CurrentPassword!, c.NewPassword!);
        if (result.Status == AuthStatus.Throttled) return Retry(context, result.RetryAfterSeconds, 429);
        if (result.Status != AuthStatus.Success) return Results.Unauthorized();
        context.Response.Cookies.Delete(cookieName, Cookie());
        return Results.NoContent();
    });
});
IResult RecoveryResponse(RecoveryResult result, HttpContext context, bool clearCookie) {
    if (result.Status == AuthStatus.Throttled) return Retry(context, result.RetryAfterSeconds, 429);
    if (result.Status != AuthStatus.Success) return Results.Unauthorized();
    if (clearCookie) context.Response.Cookies.Delete(cookieName, Cookie());
    return Results.Ok(new { recoveryCode = result.RecoveryCode });
}
app.MapPost("/auth/recover", async (RecoveryRequest c, HttpContext context, AccountStore store, PasswordWorkGate gate) => {
    if (c.Username is null || !Regex.IsMatch(c.Username, "\\A[a-zA-Z0-9_]{3,32}\\z") || !ValidPassword(c.NewPassword))
        return Results.BadRequest(new { error = "invalid_recovery_format" });
    if (c.RecoveryCode is null || c.RecoveryCode.Length > 128) return Results.Unauthorized();
    return await Work(context, gate, () => RecoveryResponse(store.Recover(c.Username.ToLowerInvariant(), c.RecoveryCode, c.NewPassword!), context, true));
});
app.MapPost("/auth/recovery-code/rotate", async (RecoveryRotation c, HttpContext context, AccountStore store, PasswordWorkGate gate) => {
    if (!ValidPassword(c.CurrentPassword)) return Results.BadRequest(new { error = "invalid_password_format" });
    var token = Token(context);
    if (token is null) return Results.Unauthorized();
    return await Work(context, gate, () => RecoveryResponse(store.RotateRecoveryCode(token, c.CurrentPassword!), context, false));
});
app.MapPost("/auth/ugs/sign-in",async (HttpContext context,OnlineIdentityService online) => {
    var token=Token(context); if(token==null) return Results.Unauthorized();
    try { return Results.Ok(await online.SignInAsync(token,context.RequestAborted)); }
    catch(OnlineIdentityException error) {
        int status=error.Code=="session_expired"?401:error.Code=="ugs_identity_conflict"?409:error.Code=="ugs_rate_limited"?429:503;
        if(status==503 || status==429) context.Response.Headers.RetryAfter="30";
        return Results.Json(new { error=error.Code },statusCode:status);
    }
});
app.MapPost("/auth/ugs/admission",(AdmissionRequest request,HttpContext context,OnlineIdentityService online,AccountStore store) => {
    var token=Token(context); if(token==null || store.Resolve(token)==null) return Results.Unauthorized();
    if(!online.Options.IsConfigured) return Results.Json(new { error="online_not_configured" },statusCode:503);
    if(!AccountStore.ValidRoom(request.RoomId)||!AccountStore.ValidNonce(request.Nonce)) return Results.BadRequest();
    var ticket=store.IssueAdmission(token,Guid.Parse(online.Options.ProjectId).ToString("D"),Guid.Parse(online.Options.EnvironmentId).ToString("D"),request.RoomId!,request.Nonce!);
    return ticket==null?Results.StatusCode(403):Results.Ok(new { admissionTicket=ticket });
});
app.MapPost("/auth/ugs/admission/verify",(AdmissionRequest request,HttpContext context,OnlineIdentityService online,AccountStore store) => {
    var token=Token(context); if(token==null || store.Resolve(token)==null) return Results.Unauthorized();
    if(!online.Options.IsConfigured) return Results.Json(new { error="online_not_configured" },statusCode:503);
    if(request.AdmissionTicket==null || request.AdmissionTicket.Length!=64 || request.PlayerId==null || request.PlayerId.Length>128 || !AccountStore.ValidRoom(request.RoomId)||!AccountStore.ValidNonce(request.Nonce)) return Results.BadRequest();
    var valid=store.VerifyAdmission(token,Guid.Parse(online.Options.ProjectId).ToString("D"),Guid.Parse(online.Options.EnvironmentId).ToString("D"),request.AdmissionTicket,request.RoomId!,request.Nonce!,request.PlayerId);
    return Results.Ok(new { authorized=valid });
});
app.MapPost("/auth/ugs/player",(AdmissionRequest request,HttpContext context,OnlineIdentityService online,AccountStore store) => {
    var token=Token(context); if(token==null || store.Resolve(token)==null) return Results.Unauthorized();
    if(!online.Options.IsConfigured) return Results.Json(new { error="online_not_configured" },statusCode:503);
    if(request.PlayerId==null || request.PlayerId.Length>128) return Results.BadRequest();
    return Results.Ok(new { authorized=store.IsActiveUgsPlayer(token,Guid.Parse(online.Options.ProjectId).ToString("D"),Guid.Parse(online.Options.EnvironmentId).ToString("D"),request.PlayerId) });
});
app.Run();
public sealed record Credentials(string? Username, string? Password);
public sealed record PasswordChange(string? CurrentPassword, string? NewPassword);

public sealed record RecoveryRequest(string? Username, string? RecoveryCode, string? NewPassword);
public sealed record RecoveryRotation(string? CurrentPassword);


public sealed record AdmissionRequest(string? RoomId,string? Nonce,string? AdmissionTicket,string? PlayerId);
