using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlacklistedTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blacklisted_tokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blacklisted_tokens", x => new { x.UserId, x.TokenId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blacklisted_tokens");
        }
    }
}
