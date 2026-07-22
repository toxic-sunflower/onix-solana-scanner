using Microsoft.EntityFrameworkCore;
using Onix.Scanner.Shared;
using Onix.Scanner.Shared.Models;

namespace Onix.Scanner.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public DbSet<Token> Tokens => Set<Token>();
    public DbSet<Proxy> Proxies => Set<Proxy>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<UserPreferences> UserPreferences => Set<UserPreferences>();
    public DbSet<UserToken> UserTokens => Set<UserToken>();
    public DbSet<SpreadTick> SpreadTicks => Set<SpreadTick>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<BlacklistedJti> BlacklistedJtis => Set<BlacklistedJti>();
    public DbSet<LoginToken> LoginTokens => Set<LoginToken>();
    public DbSet<TokenQuoteAmount> TokenQuoteAmounts => Set<TokenQuoteAmount>();

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<Token>(e =>
        {
            e.ToTable("tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuid_generate_v4()");
            e.Property(x => x.Symbol).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.SolanaMint).HasMaxLength(64).IsRequired();
            e.Property(x => x.BingxSymbol).HasMaxLength(50).IsRequired();
            e.Property(x => x.JupiterInputMint).HasMaxLength(64).IsRequired();
            e.Property(x => x.JupiterInputDecimals).HasDefaultValue(6);
            e.Property(x => x.BingxUrl).IsRequired();
            e.Property(x => x.JupiterUrl).IsRequired();
            e.Property(x => x.SolscanUrl).IsRequired();
            e.Property(x => x.Enabled).HasDefaultValue(true);
            e.Property(x => x.TelegramEnabled).HasDefaultValue(true);
            e.Property(x => x.IsAvailableOnCex).HasDefaultValue(false);
            e.Property(x => x.Status).HasMaxLength(20).HasConversion<string>().HasDefaultValue(TokenHealthStatus.Disabled);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.Symbol).HasDatabaseName("idx_tokens_symbol");
            e.HasIndex(x => x.Enabled).HasDatabaseName("idx_tokens_enabled");
            e.HasIndex(x => x.SolanaMint).IsUnique().HasDatabaseName("idx_tokens_solana_mint");
        });

        modelBuilder.Entity<TokenQuoteAmount>(e =>
        {
            e.ToTable("token_quote_amounts");
            e.HasKey(x => x.TokenId);
            e.Property(x => x.QuoteAmount).HasColumnType("numeric(38,18)").HasDefaultValue(0.01m);
        });

        modelBuilder.Entity<Proxy>(e =>
        {
            e.ToTable("proxies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuid_generate_v4()");
            e.Property(x => x.Type).HasMaxLength(10).HasDefaultValue("HTTP");
            e.Property(x => x.Host).HasMaxLength(255).IsRequired();
            e.Property(x => x.Enabled).HasDefaultValue(true);
            e.Property(x => x.Status).HasMaxLength(20).HasConversion<string>().HasDefaultValue(ProxyStatus.Disabled);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuid_generate_v4()");
                e.Property(x => x.TelegramId).IsRequired();
            e.Property(x => x.Status).HasMaxLength(10).HasDefaultValue("new");
            e.Property(x => x.Role).HasMaxLength(20).HasConversion<string>().HasDefaultValue(UserRole.User);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.TelegramId).IsUnique().HasDatabaseName("idx_users_telegram_id");
            e.HasIndex(x => x.AuthToken).HasDatabaseName("idx_users_auth_token");
        });

        modelBuilder.Entity<UserSettings>(e =>
        {
            e.ToTable("user_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuid_generate_v4()");
            e.Property(x => x.MinimalSpreadPct).HasColumnType("numeric(10,4)").HasDefaultValue(5.0m);
            e.Property(x => x.TelegramNotificationsEnabled).HasDefaultValue(true);
            e.Property(x => x.CooldownSeconds).HasDefaultValue(300);
            e.Property(x => x.Timezone).HasMaxLength(50).HasDefaultValue("UTC");
            e.Property(x => x.Role).HasMaxLength(20).HasConversion<string>().HasDefaultValue(UserRole.User);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.TelegramId).IsUnique().HasDatabaseName("idx_user_settings_telegram_id");
        });

        modelBuilder.Entity<UserPreferences>(e =>
        {
            e.ToTable("user_preferences");
            e.HasKey(x => x.UserId);
            e.Property(x => x.MinimalSpreadPct).HasColumnType("numeric(10,4)").HasDefaultValue(5.0m);
            e.Property(x => x.CooldownSeconds).HasDefaultValue(300);
            e.Property(x => x.Timezone).HasMaxLength(50).HasDefaultValue("UTC");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("NOW()");
        });

        modelBuilder.Entity<UserToken>(e =>
        {
            e.ToTable("user_tokens");
            e.HasKey(x => new { x.UserId, x.TokenId });
            e.Property(x => x.TelegramEnabled).HasDefaultValue(true);
            e.Property(x => x.AlertThresholdPct).HasColumnType("numeric(10,4)").HasDefaultValue(5.0m);
            e.Property(x => x.IsPinned).HasDefaultValue(false);
            e.Property(x => x.IsArmed).HasDefaultValue(true);
        });

        modelBuilder.Entity<SpreadTick>(e =>
        {
            e.ToTable("spread_ticks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedOnAdd();
            e.Property(x => x.BingxAskPrice).HasColumnType("numeric(38,18)").IsRequired();
            e.Property(x => x.JupiterBuyPrice).HasColumnType("numeric(38,18)").IsRequired();
            e.Property(x => x.SpreadPct).HasColumnType("numeric(20,10)").IsRequired();
            e.Property(x => x.BingxLatencyMs).HasDefaultValue(0);
            e.Property(x => x.JupiterLatencyMs).HasDefaultValue(0);
            e.Property(x => x.QualityStatus).HasMaxLength(10).HasConversion<string>().HasDefaultValue(QualityStatus.Valid);
            e.HasIndex(x => new { x.TokenId, x.CalculatedAt }).HasDatabaseName("idx_ticks_token_time");
            e.HasIndex(x => x.QualityStatus).HasDatabaseName("idx_ticks_quality");
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuid_generate_v4()");
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.DeviceName).HasMaxLength(200);
            e.Property(x => x.IpAddress).HasMaxLength(45);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("idx_refresh_tokens_hash");
            e.HasIndex(x => x.UserId).HasDatabaseName("idx_refresh_tokens_user_id");
        });

        modelBuilder.Entity<BlacklistedJti>(e =>
        {
            e.ToTable("blacklisted_jtis");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuid_generate_v4()");
            e.Property(x => x.Jti).HasMaxLength(64).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.Jti).IsUnique().HasDatabaseName("idx_blacklisted_jtis_jti");
            e.HasIndex(x => x.ExpiresAt).HasDatabaseName("idx_blacklisted_jtis_expires");
        });

        modelBuilder.Entity<LoginToken>(e =>
        {
            e.ToTable("login_tokens");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("uuid_generate_v4()");
            e.Property(x => x.Token).HasMaxLength(64).IsRequired();
            e.Property(x => x.UserId).IsRequired();
            e.Property(x => x.ExpiresAt).IsRequired();
            e.Property(x => x.IsUsed).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("NOW()");
            e.HasIndex(x => x.Token).IsUnique().HasDatabaseName("idx_login_tokens_token");
            e.HasIndex(x => x.UserId).HasDatabaseName("idx_login_tokens_user_id");
            e.HasIndex(x => x.ExpiresAt).HasDatabaseName("idx_login_tokens_expires");
        });
    }
}
