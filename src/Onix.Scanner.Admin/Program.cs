using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Admin.Components;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Infrastructure.Data;
using Onix.Scanner.Infrastructure.Services;

// Same .env convention as the main Api project (see its Program.cs) — lets
// `dotnet run` pick up ADMIN_USERNAME/ADMIN_PASSWORD/etc. from the repo-root
// .env locally without exporting shell vars. In CI/prod these same names
// come from GitHub Secrets instead (see TODO.md).
var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env");
if (File.Exists(envPath))
{
    var envs = File.ReadAllLines(envPath)
        .Select(l => { var i = l.IndexOf('='); return i > 0 ? (l[..i].Trim(), l[(i + 1)..].Trim()) : default; })
        .Where(t => t.Item1 != null)
        .ToDictionary(t => t.Item1, t => t.Item2 is ['"', .., '"'] ? t.Item2[1..^1] : t.Item2);

    if (envs.TryGetValue("ADMIN_USERNAME", out var au))
        Environment.SetEnvironmentVariable("Admin__Username", au);
    if (envs.TryGetValue("ADMIN_PASSWORD", out var ap))
        Environment.SetEnvironmentVariable("Admin__Password", ap);
    if (envs.TryGetValue("ENCRYPTION_KEY", out var ek))
        Environment.SetEnvironmentVariable("Encryption__Key", ek);
}

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpContextAccessor();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Database=onix_scanner;Username=onix;Password=onix_dev_2024";
builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseNpgsql(connectionString, x => x.ConfigureDataSource(b =>
        b.DefaultNameTranslator = new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator())));
// AppDbContext itself is only handed out via the factory above (Blazor Server
// pattern), but ProxyRepository/TokenRepository want a plain AppDbContext
// injected — resolve one per scope from the same factory so proxy passwords
// go through the same encrypt/decrypt path as the main API instead of
// landing in the DB as plaintext if this app wrote to Proxies directly.
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

// Must match the main API's Encryption:Key — same encrypted proxy passwords
// in the same DB. TEMPORARY local-dev fallback key; real key comes from the
// same env var/secret as the API once this ever runs somewhere real.
var encryptionKeyB64 = builder.Configuration.GetValue<string>("Encryption:Key")
    ?? "Iw6FMcOfauTZJzAQyoI2Ut9sxw3egGNyJsrOazJsy7g=";
builder.Services.AddSingleton<IEncryptionService>(new AesEncryptionService(Convert.FromBase64String(encryptionKeyB64)));
builder.Services.AddScoped<ITokenRepository, TokenRepository>();
builder.Services.AddScoped<IProxyRepository, ProxyRepository>();

// Login has no 2FA behind it, so it's the one thing worth rate-limiting here
// — 5 attempts/minute per IP makes password-guessing impractical without
// touching the normal Blazor Server request flow (SignalR circuit etc).
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

// Real credentials come from config (ADMIN_USERNAME/ADMIN_PASSWORD — env var
// or the repo-root .env locally, GitHub Secrets in CI, same pattern as
// Telegram:BotToken/Encryption:Key on the main Api project — see TODO.md).
// Local-dev fallback only, never the real prod password, same risk profile
// as the "onix_dev_2024" DB password already sitting in this file below.
var adminUsername = builder.Configuration.GetValue<string>("Admin:Username") ?? "admin";
var adminPassword = builder.Configuration.GetValue<string>("Admin:Password") ?? "changeme123";

app.MapPost("/account/login", async (HttpContext ctx) =>
{
    var form = await ctx.Request.ReadFormAsync();
    var username = form["username"].ToString();
    var password = form["password"].ToString();

    // Fixed-time comparison so a login attempt can't be used to brute-force
    // the password one byte at a time via response-time differences.
    var usernameOk = username.Length == adminUsername.Length && CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(username), Encoding.UTF8.GetBytes(adminUsername));
    var passwordOk = password.Length == adminPassword.Length && CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(password), Encoding.UTF8.GetBytes(adminPassword));

    if (usernameOk && passwordOk)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, username)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return Results.Redirect("/");
    }

    return Results.Redirect("/login?error=1");
}).AllowAnonymous().RequireRateLimiting("login");

app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
