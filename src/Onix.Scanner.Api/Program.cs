using System.Security.Claims;
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
using Onix.Scanner.Api.Hubs;
using Onix.Scanner.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddControllers();

var encryptionKey = Convert.FromBase64String(
    builder.Configuration.GetValue<string>("Encryption:Key")
    ?? throw new InvalidOperationException("Encryption:Key is required"));
if (encryptionKey.Length != 16 && encryptionKey.Length != 24 && encryptionKey.Length != 32)
    throw new InvalidOperationException("Encryption:Key must decode to 16, 24, or 32 bytes");

var jwtKey = encryptionKey.Length == 16 || encryptionKey.Length == 24
    ? encryptionKey : encryptionKey[..16];

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
                var authHeader = context.Request.Headers["X-Auth-Token"].FirstOrDefault();
                if (!string.IsNullOrEmpty(authHeader))
                {
                    context.Token = authHeader;
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

builder.Services.AddSingleton<ITokenSnapshotPool, TokenSnapshotPool>();
builder.Services.AddScoped<ITokenRepository, TokenRepository>();
builder.Services.AddScoped<IProxyRepository, ProxyRepository>();
builder.Services.AddScoped<ISpreadTickRepository, SpreadTickRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserSettingsRepository, UserSettingsRepository>();

builder.Services.AddHostedService<MigratorService>();
builder.Services.AddHostedService<BingXConnectorService>();
builder.Services.AddHostedService<SpreadEngineService>();
builder.Services.AddHostedService<JupiterWorkerService>();
builder.Services.AddSingleton<TelegramNotificationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramNotificationService>());
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

app.MapGet("/api/v1/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }))
    .RequireRateLimiting("public");
app.MapControllers();
app.MapHub<SpreadHub>("/hubs/spread");

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
