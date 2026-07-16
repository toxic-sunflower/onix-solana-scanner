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
                name: "DeviceName",
                table: "refresh_tokens",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "refresh_tokens",
                type: "character varying(45)",
                maxLength: 45,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUsedAt",
                table: "refresh_tokens",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeviceName",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "LastUsedAt",
                table: "refresh_tokens");
        }
    }
}
