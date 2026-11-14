using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantio.Migrations
{
    /// <inheritdoc />
    public partial class DodajPinnedByWeekDoSetlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PinnedByWeek",
                table: "Setlists",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PinnedByWeek",
                table: "Setlists");
        }
    }
}
