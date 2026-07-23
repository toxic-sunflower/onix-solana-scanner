using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Onix.Scanner.Api.Auth;
using Onix.Scanner.Core.Contracts;
using Onix.Scanner.Core;
using Onix.Scanner.Infrastructure.Data;
using Onix.Scanner.Infrastructure;
using Onix.Scanner.Infrastructure.Services;
using Onix.Scanner.Api.Services;

var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "..", ".env");
if (File.Exists(envPath))
{
    var envs = File.ReadAllLines(envPath)
        .Select(l => { var i = l.IndexOf('='); return i > 0 ? (l[..i].Trim(), l[(i+1)..].Trim()) : default; })
        .Where(t => t.Item1 != null)
        .ToDictionary(t => t.Item1, t => t.Item2 is ['"', .., '"'] ? t.Item2[1..^1] : t.Item2);

    foreach (var (k, v) in envs)
        Environment.SetEnvironmentVariable(k, v);

    if (envs.TryGetValue("TELEGRAM_BOT_TOKEN", out var tgt))
        Environment.SetEnvironmentVariable("Telegram__BotToken", tgt);
    if (envs.TryGetValue("TELEGRAM_BOT_USERNAME", out var tgu))
        Environment.SetEnvironmentVariable("Telegram__BotUsername", tgu);
    if (envs.TryGetValue("TELEGRAM_OAUTH_CLIENT_ID", out var tcid))
        Environment.SetEnvironmentVariable("Telegram__OpenId__ClientId", tcid);
    if (envs.TryGetValue("TELEGRAM_OAUTH_CLIENT_SECRET", out var tcs))
        Environment.SetEnvironmentVariable("Telegram__OpenId__ClientSecret", tcs);
    if (envs.TryGetValue("TELEGRAM_OAUTH_REDIRECT_URI", out var truri))
        Environment.SetEnvironmentVariable("Telegram__OpenId__RedirectUri", truri);
    if (envs.TryGetValue("TELEGRAM_WEBHOOK_SECRET", out var twhs))
        Environment.SetEnvironmentVariable("Telegram__WebhookSecret", twhs);
    if (envs.TryGetValue("ENCRYPTION_KEY", out var ek))
        Environment.SetEnvironmentVariable("Encryption__Key", ek);
    if (envs.TryGetValue("APP_URL", out var au))
        Environment.SetEnvironmentVariable("App__Url", au);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var encryptionKey = Convert.FromBase64String(
    builder.Configuration.GetValue<string>("Encryption:Key")
    ?? throw new InvalidOperationException("Encryption:Key is required"));
if (encryptionKey.Length != 16 && encryptionKey.Length != 24 && encryptionKey.Length != 32)
    throw new InvalidOperationException("Encryption:Key must decode to 16, 24, or 32 bytes");

var jwtKey = SHA256.HashData(encryptionKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
        };

        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                // Standard Authorization: Bearer header (used by regular API calls)
                var auth = context.Request.Headers.Authorization.FirstOrDefault();
                if (!string.IsNullOrEmpty(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = auth["Bearer ".Length..].Trim();
                    return Task.CompletedTask;
                }

                // Custom X-Auth-Token header (used by authFetch)
                var xauth = context.Request.Headers["X-Auth-Token"].FirstOrDefault();
                if (!string.IsNullOrEmpty(xauth))
                {
                    context.Token = xauth;
                    return Task.CompletedTask;
                }

                // Query string token (used by SSE — EventSource can't set custom headers)
                var qtoken = context.Request.Query["access_token"].FirstOrDefault();
                if (!string.IsNullOrEmpty(qtoken))
                {
                    context.Token = qtoken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("public", opt =>
    {
        opt.PermitLimit = 100;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit = 10;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default is required");

builder.Services.AddDbContextFactory<AppDbContext>(o =>
    o.UseNpgsql(connectionString, x => x.ConfigureDataSource(b =>
        b.DefaultNameTranslator = new Npgsql.NameTranslation.NpgsqlSnakeCaseNameTranslator())));
builder.Services.AddNpgsqlDataSource(connectionString);

builder.Services.AddSingleton<IEncryptionService>(
    new AesEncryptionService(encryptionKey));
builder.Services.AddSingleton(new JwtTokenService(jwtKey));
// oauth.telegram.org responds gzip-compressed; the default HttpClientHandler
// doesn't auto-decompress, which fed the JWKS/discovery JSON parser raw
// compressed bytes and blew up with a JsonException.
builder.Services.AddHttpClient<TelegramOpenIdValidator>()
    .ConfigurePrimaryHttpMessageHandler(() =>
        new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
builder.Services.AddHttpClient<TelegramOAuthClient>()
    .ConfigurePrimaryHttpMessageHandler(() =>
        new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });

builder.Services.AddSingleton<SseBroadcaster>();
builder.Services.AddSingleton<ITokenSnapshotPool, TokenSnapshotPool>();
builder.Services.AddScoped<ITokenRepository, TokenRepository>();
builder.Services.AddScoped<IProxyRepository, ProxyRepository>();
builder.Services.AddScoped<ISpreadTickRepository, SpreadTickRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserSettingsRepository, UserSettingsRepository>();

builder.Services.AddHostedService<MigratorService>();
builder.Services.AddHostedService<SnapshotWarmupService>();
builder.Services.AddHostedService<BingXConnectorService>();
builder.Services.AddHostedService<SpreadEngineService>();
builder.Services.AddHostedService<JupiterWorkerService>();
builder.Services.AddHttpClient();
builder.Services.AddSingleton<LocalizationService>();
builder.Services.AddSingleton<TelegramNotificationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramNotificationService>());
builder.Services.AddHostedService<TokenSyncService>();
builder.Services.AddHostedService<AggregationService>();
builder.Services.AddHostedService<RetentionService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
