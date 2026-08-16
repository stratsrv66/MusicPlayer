using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistImportAndExternalIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "isrc",
                table: "track_metadata",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "match_key",
                table: "track_metadata",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source_platform",
                table: "track_metadata",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "track_number",
                table: "track_metadata",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "playlist_imports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_playlist_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    source_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    total_tracks = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resolve_on_youtube = table.Column<bool>(type: "boolean", nullable: false),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    playlist_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist_imports", x => x.id);
                    table.ForeignKey(
                        name: "fk_playlist_imports_playlists_playlist_id",
                        column: x => x.playlist_id,
                        principalTable: "playlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_playlist_imports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_external_ids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_external_ids", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_external_ids_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist_import_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    import_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    source_track_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    artist_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    album_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    track_number = table.Column<int>(type: "integer", nullable: true),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    isrc = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    cover_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    source_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    audio_source_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    audio_platform = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist_import_items", x => x.id);
                    table.CheckConstraint("ck_playlist_import_items_duration_non_negative", "duration_seconds >= 0");
                    table.ForeignKey(
                        name: "fk_playlist_import_items_playlist_imports_import_id",
                        column: x => x.import_id,
                        principalTable: "playlist_imports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_playlist_import_items_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_track_metadata_isrc",
                table: "track_metadata",
                column: "isrc");

            migrationBuilder.CreateIndex(
                name: "ix_track_metadata_match_key",
                table: "track_metadata",
                column: "match_key");

            migrationBuilder.CreateIndex(
                name: "ix_playlist_import_items_import_id_position",
                table: "playlist_import_items",
                columns: new[] { "import_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_playlist_import_items_import_id_status",
                table: "playlist_import_items",
                columns: new[] { "import_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_playlist_import_items_track_id",
                table: "playlist_import_items",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_playlist_imports_playlist_id",
                table: "playlist_imports",
                column: "playlist_id");

            migrationBuilder.CreateIndex(
                name: "ix_playlist_imports_status",
                table: "playlist_imports",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_playlist_imports_user_id_created_at",
                table: "playlist_imports",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_track_external_ids_platform_external_id",
                table: "track_external_ids",
                columns: new[] { "platform", "external_id" });

            migrationBuilder.CreateIndex(
                name: "ix_track_external_ids_track_id_platform",
                table: "track_external_ids",
                columns: new[] { "track_id", "platform" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "playlist_import_items");

            migrationBuilder.DropTable(
                name: "track_external_ids");

            migrationBuilder.DropTable(
                name: "playlist_imports");

            migrationBuilder.DropIndex(
                name: "ix_track_metadata_isrc",
                table: "track_metadata");

            migrationBuilder.DropIndex(
                name: "ix_track_metadata_match_key",
                table: "track_metadata");

            migrationBuilder.DropColumn(
                name: "isrc",
                table: "track_metadata");

            migrationBuilder.DropColumn(
                name: "match_key",
                table: "track_metadata");

            migrationBuilder.DropColumn(
                name: "source_platform",
                table: "track_metadata");

            migrationBuilder.DropColumn(
                name: "track_number",
                table: "track_metadata");
        }
    }
}
