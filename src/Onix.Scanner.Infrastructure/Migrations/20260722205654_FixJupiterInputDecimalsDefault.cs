using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixJupiterInputDecimalsDefault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "JupiterInputDecimals",
                table: "tokens",
                type: "integer",
                nullable: false,
                defaultValue: 9,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 6);

            // Backfill: the previous migration defaulted every existing row to 6
            // (assuming a USDC-like base asset), but tokens quoted against wrapped
            // SOL (9 decimals) got their Jupiter Quote API amounts computed 1000x
            // off, breaking price lookups entirely. Only touches rows using the
            // well-known wrapped SOL mint — anything explicitly configured
            // otherwise is left alone.
            migrationBuilder.Sql("""
                UPDATE tokens
                SET "JupiterInputDecimals" = 9
                WHERE "JupiterInputMint" = 'So11111111111111111111111111111111111111112'
                  AND "JupiterInputDecimals" = 6
            """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "JupiterInputDecimals",
                table: "tokens",
                type: "integer",
                nullable: false,
                defaultValue: 6,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 9);
        }
    }
}
