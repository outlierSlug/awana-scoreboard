using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Awana.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScoringProfileSeedKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "seed_key",
                table: "scoring_profiles",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_scoring_profiles_church_id_seed_key",
                table: "scoring_profiles",
                columns: new[] { "church_id", "seed_key" },
                unique: true,
                filter: "seed_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_scoring_profiles_church_id_seed_key",
                table: "scoring_profiles");

            migrationBuilder.DropColumn(
                name: "seed_key",
                table: "scoring_profiles");
        }
    }
}
