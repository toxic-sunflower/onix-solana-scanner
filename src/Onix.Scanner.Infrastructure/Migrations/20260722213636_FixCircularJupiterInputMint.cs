using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixCircularJupiterInputMint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            // TokenSyncService used to set JupiterInputMint = the token's own
            // mint (copy-paste bug), so every synced token had
            // JupiterInputMint == SolanaMint — Jupiter's Quote API rejects
            // that outright ("Input and output mints are not allowed to be
            // equal" / CIRCULAR_ARBITRAGE_IS_DISABLED), which is why prices
            // stopped showing entirely. Repoints affected rows at USDC (the
            // base asset JupiterUrl already assumed all along).
            migrationBuilder.Sql("""
                UPDATE tokens
                SET "JupiterInputMint" = 'EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v',
                    "JupiterInputDecimals" = 6
                WHERE "JupiterInputMint" = "SolanaMint"
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
                defaultValue: 9,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 6);
        }
    }
}
