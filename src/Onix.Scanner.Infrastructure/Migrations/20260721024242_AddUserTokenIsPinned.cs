using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Onix.Scanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserTokenIsPinned : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IF NOT EXISTS: on at least one environment this column was already
            // present without this migration being recorded as applied, which
            // made every migration after it (and this one) fail forever. This
            // makes it safe to run whether or not the column is already there.
            migrationBuilder.Sql(
                "ALTER TABLE user_tokens ADD COLUMN IF NOT EXISTS \"IsPinned\" boolean NOT NULL DEFAULT FALSE;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "user_tokens");
        }
    }
}
