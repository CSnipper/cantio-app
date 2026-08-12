using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cantio.Migrations
{
    /// <inheritdoc />
    public partial class DodajUpdatedAtDoSong : Migration
    {
        /// <inheritdoc />
        /// <remarks>
        /// Backfill DATĄ MIGRACJI, nie zerem. Wartość zerowa (0001-01-01) wyglądałaby przy
        /// porównaniach różnicowych jak „pieśń nigdy nie zmieniona”, a Pilot ma tę wartość
        /// zapamiętywać jako bazę <c>baseUpdatedAt</c> — jedna data dla całej istniejącej
        /// biblioteki jest poprawna: to moment, od którego znacznik w ogóle istnieje.
        /// Migracja jest NIENISZCZĄCA (samo ADD COLUMN).
        /// </remarks>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Songs",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(2026, 8, 12, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Songs");
        }
    }
}
