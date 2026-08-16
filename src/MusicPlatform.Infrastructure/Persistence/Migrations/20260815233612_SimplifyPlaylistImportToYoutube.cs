using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyPlaylistImportToYoutube : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_track_metadata_isrc",
                table: "track_metadata");

            migrationBuilder.DropColumn(
                name: "isrc",
                table: "track_metadata");

            migrationBuilder.DropColumn(
                name: "track_number",
                table: "track_metadata");

            migrationBuilder.DropColumn(
                name: "resolve_on_youtube",
                table: "playlist_imports");

            migrationBuilder.DropColumn(
                name: "album_name",
                table: "playlist_import_items");

            migrationBuilder.DropColumn(
                name: "audio_platform",
                table: "playlist_import_items");

            migrationBuilder.DropColumn(
                name: "audio_source_url",
                table: "playlist_import_items");

            migrationBuilder.DropColumn(
                name: "cover_url",
                table: "playlist_import_items");

            migrationBuilder.DropColumn(
                name: "isrc",
                table: "playlist_import_items");

            migrationBuilder.DropColumn(
                name: "track_number",
                table: "playlist_import_items");

            migrationBuilder.DropColumn(
                name: "year",
                table: "playlist_import_items");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "isrc",
                table: "track_metadata",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "track_number",
                table: "track_metadata",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "resolve_on_youtube",
                table: "playlist_imports",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "album_name",
                table: "playlist_import_items",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "audio_platform",
                table: "playlist_import_items",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "audio_source_url",
                table: "playlist_import_items",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cover_url",
                table: "playlist_import_items",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "isrc",
                table: "playlist_import_items",
                type: "character varying(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "track_number",
                table: "playlist_import_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "year",
                table: "playlist_import_items",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_track_metadata_isrc",
                table: "track_metadata",
                column: "isrc");
        }
    }
}
