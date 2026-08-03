using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantio.Migrations
{
    /// <inheritdoc />
    public partial class AddUpdatedAtToSetlist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Setlists",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Setlists");
        }
    }
}
