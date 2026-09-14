using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Awana.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropGameIsCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_core",
                table: "games");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_core",
                table: "games",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
