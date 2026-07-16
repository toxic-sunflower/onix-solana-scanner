using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenVersionAndRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastJti",
                table: "refresh_tokens",
                type: "text",
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "idx_blacklisted_jtis_expires",
                table: "blacklisted_jtis",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "idx_blacklisted_jtis_jti",
                table: "blacklisted_jtis",
                column: "Jti",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blacklisted_jtis");

            migrationBuilder.DropColumn(
                name: "LastJti",
                table: "refresh_tokens");
        }
    }
}
