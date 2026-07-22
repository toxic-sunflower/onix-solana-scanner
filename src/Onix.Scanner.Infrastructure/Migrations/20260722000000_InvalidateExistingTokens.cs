using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    public partial class InvalidateExistingTokens : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE users
                SET "TokenVersion" = "TokenVersion" + 1,
                    "UpdatedAt" = NOW()
                WHERE "TokenVersion" >= 0
            """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE users
                SET "TokenVersion" = "TokenVersion" - 1,
                    "UpdatedAt" = NOW()
                WHERE "TokenVersion" > 0
            """);
        }
    }
}
