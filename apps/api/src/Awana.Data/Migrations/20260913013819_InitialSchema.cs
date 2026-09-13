using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Awana.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "churches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_churches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "divisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    church_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    slug = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_divisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_divisions_churches_church_id",
                        column: x => x.church_id,
                        principalTable: "churches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scoring_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    church_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    config_json = table.Column<string>(type: "jsonb", nullable: false),
                    schema_version = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scoring_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_scoring_profiles_churches_church_id",
                        column: x => x.church_id,
                        principalTable: "churches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    church_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    google_subject = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    role = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_churches_church_id",
                        column: x => x.church_id,
                        principalTable: "churches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    church_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    division_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_core = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_games", x => x.id);
                    table.ForeignKey(
                        name: "fk_games_churches_church_id",
                        column: x => x.church_id,
                        principalTable: "churches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_games_divisions_division_id",
                        column: x => x.division_id,
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    church_id = table.Column<Guid>(type: "uuid", nullable: false),
                    division_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    text_on_color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_teams_churches_church_id",
                        column: x => x.church_id,
                        principalTable: "churches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_teams_divisions_division_id",
                        column: x => x.division_id,
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    church_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_logs_churches_church_id",
                        column: x => x.church_id,
                        principalTable: "churches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_audit_logs_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    church_id = table.Column<Guid>(type: "uuid", nullable: false),
                    division_id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    public_slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    scoring_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scoring_config_json = table.Column<string>(type: "jsonb", nullable: true),
                    scoring_config_schema_version = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sessions_churches_church_id",
                        column: x => x.church_id,
                        principalTable: "churches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessions_divisions_division_id",
                        column: x => x.division_id,
                        principalTable: "divisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessions_scoring_profiles_scoring_profile_id",
                        column: x => x.scoring_profile_id,
                        principalTable: "scoring_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sessions_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "point_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    church_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points = table.Column<decimal>(type: "numeric(9,3)", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_point_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_point_adjustments_churches_church_id",
                        column: x => x.church_id,
                        principalTable: "churches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_point_adjustments_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_point_adjustments_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_point_adjustments_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_point_adjustments_users_voided_by_user_id",
                        column: x => x.voided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rounds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    church_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_number = table.Column<int>(type: "integer", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    point_multiplier = table.Column<decimal>(type: "numeric(9,3)", nullable: false),
                    client_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rounds", x => x.id);
                    table.ForeignKey(
                        name: "fk_rounds_churches_church_id",
                        column: x => x.church_id,
                        principalTable: "churches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rounds_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rounds_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_rounds_users_recorded_by_user_id",
                        column: x => x.recorded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rounds_users_voided_by_user_id",
                        column: x => x.voided_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_teams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    headcount = table.Column<int>(type: "integer", nullable: true),
                    headcount_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_teams_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_session_teams_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "round_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    team_id = table.Column<Guid>(type: "uuid", nullable: false),
                    place = table.Column<int>(type: "integer", nullable: true),
                    is_disqualified = table.Column<bool>(type: "boolean", nullable: false),
                    points_awarded = table.Column<decimal>(type: "numeric(9,3)", nullable: false),
                    bonus_points = table.Column<decimal>(type: "numeric(9,3)", nullable: false),
                    bonus_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_round_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_round_results_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_round_results_teams_team_id",
                        column: x => x.team_id,
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_user_id",
                table: "audit_logs",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_church_id_created_at",
                table: "audit_logs",
                columns: new[] { "church_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_churches_slug",
                table: "churches",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_divisions_church_id_slug",
                table: "divisions",
                columns: new[] { "church_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_games_church_id_name",
                table: "games",
                columns: new[] { "church_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_games_division_id",
                table: "games",
                column: "division_id");

            migrationBuilder.CreateIndex(
                name: "ix_point_adjustments_church_id_session_id",
                table: "point_adjustments",
                columns: new[] { "church_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_point_adjustments_created_by_user_id",
                table: "point_adjustments",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_point_adjustments_session_id",
                table: "point_adjustments",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_point_adjustments_team_id",
                table: "point_adjustments",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_point_adjustments_voided_by_user_id",
                table: "point_adjustments",
                column: "voided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_round_results_round_id_team_id",
                table: "round_results",
                columns: new[] { "round_id", "team_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_round_results_team_id",
                table: "round_results",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_rounds_church_id_session_id",
                table: "rounds",
                columns: new[] { "church_id", "session_id" });

            migrationBuilder.CreateIndex(
                name: "ix_rounds_game_id",
                table: "rounds",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "ix_rounds_recorded_by_user_id",
                table: "rounds",
                column: "recorded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_rounds_session_id_client_request_id",
                table: "rounds",
                columns: new[] { "session_id", "client_request_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rounds_session_id_round_number",
                table: "rounds",
                columns: new[] { "session_id", "round_number" },
                unique: true,
                filter: "voided_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_rounds_voided_by_user_id",
                table: "rounds",
                column: "voided_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_scoring_profiles_church_id_name",
                table: "scoring_profiles",
                columns: new[] { "church_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_session_teams_session_id_team_id",
                table: "session_teams",
                columns: new[] { "session_id", "team_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_session_teams_team_id",
                table: "session_teams",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_church_id_division_id_date",
                table: "sessions",
                columns: new[] { "church_id", "division_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_church_id_status",
                table: "sessions",
                columns: new[] { "church_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_created_by_user_id",
                table: "sessions",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_division_id",
                table: "sessions",
                column: "division_id");

            migrationBuilder.CreateIndex(
                name: "ix_sessions_public_slug",
                table: "sessions",
                column: "public_slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sessions_scoring_profile_id",
                table: "sessions",
                column: "scoring_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_teams_church_id_division_id_name",
                table: "teams",
                columns: new[] { "church_id", "division_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_division_id",
                table: "teams",
                column: "division_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_church_id_email",
                table: "users",
                columns: new[] { "church_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_google_subject",
                table: "users",
                column: "google_subject",
                unique: true,
                filter: "google_subject IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "point_adjustments");

            migrationBuilder.DropTable(
                name: "round_results");

            migrationBuilder.DropTable(
                name: "session_teams");

            migrationBuilder.DropTable(
                name: "rounds");

            migrationBuilder.DropTable(
                name: "teams");

            migrationBuilder.DropTable(
                name: "games");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "divisions");

            migrationBuilder.DropTable(
                name: "scoring_profiles");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "churches");
        }
    }
}
