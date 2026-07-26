using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTokenProxyFallbackPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProxyFallbackPolicy",
                table: "tokens",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Strict");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProxyFallbackPolicy",
                table: "tokens");
        }
    }
}
