using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTwoFactorResetCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TwoFactorResetCode",
                table: "users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TwoFactorResetCode",
                table: "users");
        }
    }
}
