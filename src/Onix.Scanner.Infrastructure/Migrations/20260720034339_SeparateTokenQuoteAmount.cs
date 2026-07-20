using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeparateTokenQuoteAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuoteAmount",
                table: "tokens");

            migrationBuilder.CreateTable(
                name: "token_quote_amounts",
                columns: table => new
                {
                    TokenId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuoteAmount = table.Column<decimal>(type: "numeric(38,18)", nullable: false, defaultValue: 0.01m)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_token_quote_amounts", x => x.TokenId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "token_quote_amounts");

            migrationBuilder.AddColumn<decimal>(
                name: "QuoteAmount",
                table: "tokens",
                type: "numeric(38,18)",
                nullable: false,
                defaultValue: 0.01m);
        }
    }
}
