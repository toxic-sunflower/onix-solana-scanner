using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='Is2FAEnabled') THEN
                        ALTER TABLE users ADD COLUMN "Is2FAEnabled" boolean NOT NULL DEFAULT false;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='TokenVersion') THEN
                        ALTER TABLE users ADD COLUMN "TokenVersion" integer NOT NULL DEFAULT 0;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='TwoFactorBackupCodes') THEN
                        ALTER TABLE users ADD COLUMN "TwoFactorBackupCodes" text NULL;
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='TwoFactorSecret') THEN
                        ALTER TABLE users ADD COLUMN "TwoFactorSecret" text NULL;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.CreateTable(
                name: "blacklisted_jtis",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Jti = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blacklisted_jtis", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "uuid_generate_v4()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DeviceName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IpAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    LastJti = table.Column<string>(type: "text", nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "idx_blacklisted_jtis_expires",
                table: "blacklisted_jtis",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "idx_blacklisted_jtis_jti",
                table: "blacklisted_jtis",
                column: "Jti",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_refresh_tokens_hash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blacklisted_jtis");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='Is2FAEnabled') THEN
                        ALTER TABLE users DROP COLUMN "Is2FAEnabled";
                    END IF;
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='TokenVersion') THEN
                        ALTER TABLE users DROP COLUMN "TokenVersion";
                    END IF;
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='TwoFactorBackupCodes') THEN
                        ALTER TABLE users DROP COLUMN "TwoFactorBackupCodes";
                    END IF;
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='users' AND column_name='TwoFactorSecret') THEN
                        ALTER TABLE users DROP COLUMN "TwoFactorSecret";
                    END IF;
                END
                $$;
                """);
        }
    }
}
