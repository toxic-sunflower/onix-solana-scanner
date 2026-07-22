using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProxyErrorAlertRearm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArmed",
                table: "user_tokens",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSignalAt",
                table: "user_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "JupiterInputDecimals",
                table: "tokens",
                type: "integer",
                nullable: false,
                defaultValue: 6);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsArmed",
                table: "user_tokens");

            migrationBuilder.DropColumn(
                name: "LastSignalAt",
                table: "user_tokens");

            migrationBuilder.DropColumn(
                name: "JupiterInputDecimals",
                table: "tokens");
        }
    }
}
