using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantio.Migrations
{
    /// <inheritdoc />
    public partial class DodajSeasonKeyDoSetlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeasonKey",
                table: "Setlists",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeasonKey",
                table: "Setlists");
        }
    }
}
