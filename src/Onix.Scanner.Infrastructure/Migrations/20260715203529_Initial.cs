using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:uuid-ossp", ",,");

            migrationBuilder.CreateTable(
                name: "proxies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    Type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "HTTP"),
                    Host = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: true),
                    Password = table.Column<string>(type: "text", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    LastCheckAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Disabled"),
                    LatencyMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_proxies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "spread_ticks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    BingxAskPrice = table.Column<decimal>(type: "numeric(38,18)", nullable: false),
                    JupiterBuyPrice = table.Column<decimal>(type: "numeric(38,18)", nullable: false),
                    SpreadPct = table.Column<decimal>(type: "numeric(20,10)", nullable: false),
                    BingxReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    JupiterReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BingxLatencyMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    JupiterLatencyMs = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ProxyId = table.Column<Guid>(type: "uuid", nullable: true),
                    QualityStatus = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "Valid")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_spread_ticks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    Symbol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SolanaMint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BingxSymbol = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    JupiterInputMint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Decimals = table.Column<int>(type: "integer", nullable: false),
                    QuoteAmount = table.Column<decimal>(type: "numeric(38,18)", nullable: false, defaultValue: 0.01m),
                    BingxUrl = table.Column<string>(type: "text", nullable: false),
                    JupiterUrl = table.Column<string>(type: "text", nullable: false),
                    SolscanUrl = table.Column<string>(type: "text", nullable: false),
                    ProxyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TelegramEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Disabled"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_preferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimalSpreadPct = table.Column<decimal>(type: "numeric(10,4)", nullable: false, defaultValue: 5.0m),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 300),
                    Timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "UTC"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_preferences", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    TelegramId = table.Column<long>(type: "bigint", nullable: true),
                    MinimalSpreadPct = table.Column<decimal>(type: "numeric(10,4)", nullable: false, defaultValue: 5.0m),
                    TelegramNotificationsEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CooldownSeconds = table.Column<int>(type: "integer", nullable: false, defaultValue: 300),
                    Timezone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "UTC"),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "User"),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    TelegramEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    AlertThresholdPct = table.Column<decimal>(type: "numeric(10,4)", nullable: false, defaultValue: 5.0m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => new { x.UserId, x.TokenId });
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    TelegramId = table.Column<long>(type: "bigint", nullable: false),
                    TelegramUsername = table.Column<string>(type: "text", nullable: true),
                    DisplayName = table.Column<string>(type: "text", nullable: true),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    AuthToken = table.Column<string>(type: "text", nullable: true),
                    AuthTokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "User"),
                    ChatId = table.Column<long>(type: "bigint", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_ticks_quality",
                table: "spread_ticks",
                column: "QualityStatus");

            migrationBuilder.CreateIndex(
                name: "idx_ticks_token_time",
                table: "spread_ticks",
                columns: new[] { "TokenId", "CalculatedAt" });

            migrationBuilder.CreateIndex(
                name: "idx_tokens_enabled",
                table: "tokens",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "idx_tokens_solana_mint",
                table: "tokens",
                column: "SolanaMint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_tokens_symbol",
                table: "tokens",
                column: "Symbol");

            migrationBuilder.CreateIndex(
                name: "idx_user_settings_telegram_id",
                table: "user_settings",
                column: "TelegramId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_users_auth_token",
                table: "users",
                column: "AuthToken");

            migrationBuilder.CreateIndex(
                name: "idx_users_telegram_id",
                table: "users",
                column: "TelegramId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "proxies");

            migrationBuilder.DropTable(
                name: "spread_ticks");

            migrationBuilder.DropTable(
                name: "tokens");

            migrationBuilder.DropTable(
                name: "user_preferences");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
