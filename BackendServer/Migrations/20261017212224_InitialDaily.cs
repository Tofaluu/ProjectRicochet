using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BackendServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialDaily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_entries",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false),
                    GuestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpenedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BestMoves = table.Column<int>(type: "integer", nullable: false),
                    BestSeconds = table.Column<double>(type: "double precision", nullable: false),
                    BestSolution = table.Column<byte[]>(type: "bytea", nullable: true),
                    SolvedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_entries", x => new { x.Number, x.GuestId });
                });

            migrationBuilder.CreateTable(
                name: "daily_players",
                columns: table => new
                {
                    GuestId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_players", x => x.GuestId);
                });

            migrationBuilder.CreateTable(
                name: "daily_puzzles",
                columns: table => new
                {
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Board = table.Column<byte[]>(type: "bytea", nullable: false),
                    Shortest = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_puzzles", x => x.Number);
                });

            migrationBuilder.CreateIndex(
                name: "IX_daily_entries_GuestId",
                table: "daily_entries",
                column: "GuestId");

            migrationBuilder.CreateIndex(
                name: "IX_daily_entries_Number_BestMoves_BestSeconds",
                table: "daily_entries",
                columns: new[] { "Number", "BestMoves", "BestSeconds" });

            migrationBuilder.CreateIndex(
                name: "IX_daily_puzzles_Date",
                table: "daily_puzzles",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_entries");

            migrationBuilder.DropTable(
                name: "daily_players");

            migrationBuilder.DropTable(
                name: "daily_puzzles");
        }
    }
}
