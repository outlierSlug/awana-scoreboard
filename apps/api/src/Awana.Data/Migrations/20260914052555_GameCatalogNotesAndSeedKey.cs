using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Awana.Data.Migrations
{
    /// <inheritdoc />
    public partial class GameCatalogNotesAndSeedKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "games",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "seed_key",
                table: "games",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_games_church_id_seed_key",
                table: "games",
                columns: new[] { "church_id", "seed_key" },
                unique: true,
                filter: "seed_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_games_church_id_seed_key",
                table: "games");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "games");

            migrationBuilder.DropColumn(
                name: "seed_key",
                table: "games");
        }
    }
}
