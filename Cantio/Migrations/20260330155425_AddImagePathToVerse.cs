using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantio.Migrations
{
    /// <inheritdoc />
    public partial class AddImagePathToVerse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "Verses",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "Verses");
        }
    }
}
