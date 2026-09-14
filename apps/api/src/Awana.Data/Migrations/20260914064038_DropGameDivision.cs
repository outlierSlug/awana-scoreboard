using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Awana.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropGameDivision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_games_divisions_division_id",
                table: "games");

            migrationBuilder.DropIndex(
                name: "ix_games_division_id",
                table: "games");

            migrationBuilder.DropColumn(
                name: "division_id",
                table: "games");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "division_id",
                table: "games",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_games_division_id",
                table: "games",
                column: "division_id");

            migrationBuilder.AddForeignKey(
                name: "fk_games_divisions_division_id",
                table: "games",
                column: "division_id",
                principalTable: "divisions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
