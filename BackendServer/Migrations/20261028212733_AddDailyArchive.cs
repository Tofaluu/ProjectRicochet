using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendServer.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_archive",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false),
                    GuestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpenedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BestMoves = table.Column<int>(type: "integer", nullable: false),
                    BestSolution = table.Column<byte[]>(type: "bytea", nullable: true),
                    SolvedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SawSolutionUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_archive", x => new { x.Number, x.GuestId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_archive_GuestId",
                table: "daily_archive",
                column: "GuestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_archive");
        }
    }
}
