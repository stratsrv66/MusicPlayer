using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "genres",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_genres", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stored_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "albums",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    artist_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    cover_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_albums", x => x.id);
                    table.ForeignKey(
                        name: "fk_albums_stored_files_cover_id",
                        column: x => x.cover_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    username_normalized = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bio = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    avatar_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    social_links = table.Column<string>(type: "jsonb", nullable: true),
                    profile_visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_users_stored_files_avatar_file_id",
                        column: x => x.avatar_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    target_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_logs_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "follows",
                columns: table => new
                {
                    follower_id = table.Column<Guid>(type: "uuid", nullable: false),
                    followed_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follows", x => new { x.follower_id, x.followed_id });
                    table.CheckConstraint("ck_follows_no_self_follow", "follower_id <> followed_id");
                    table.ForeignKey(
                        name: "fk_follows_users_followed_id",
                        column: x => x.followed_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_follows_users_follower_id",
                        column: x => x.follower_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    cover_file_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlists", x => x.id);
                    table.ForeignKey(
                        name: "fk_playlists_stored_files_cover_file_id",
                        column: x => x.cover_file_id,
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_playlists_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_reports_users_reporter_id",
                        column: x => x.reporter_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reports_users_reviewed_by",
                        column: x => x.reviewed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tracks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    album_id = table.Column<Guid>(type: "uuid", nullable: true),
                    genre_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    artist_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    visibility = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    like_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    play_count = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    hidden_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracks", x => x.id);
                    table.CheckConstraint("ck_tracks_counters_non_negative", "like_count >= 0 AND play_count >= 0");
                    table.CheckConstraint("ck_tracks_duration_non_negative", "duration_seconds >= 0");
                    table.ForeignKey(
                        name: "fk_tracks_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "albums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tracks_genres_genre_id",
                        column: x => x.genre_id,
                        principalTable: "genres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_tracks_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_exports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    file_size = table.Column<long>(type: "bigint", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_exports", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_exports_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_settings",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    show_like_count = table.Column<bool>(type: "boolean", nullable: false),
                    show_play_count = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_settings", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist_favorites",
                columns: table => new
                {
                    playlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist_favorites", x => new { x.playlist_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_playlist_favorites_playlists_playlist_id",
                        column: x => x.playlist_id,
                        principalTable: "playlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_playlist_favorites_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist_follows",
                columns: table => new
                {
                    playlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist_follows", x => new { x.playlist_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_playlist_follows_playlists_playlist_id",
                        column: x => x.playlist_id,
                        principalTable: "playlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_playlist_follows_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    timestamp_seconds = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comments", x => x.id);
                    table.CheckConstraint("ck_comments_timestamp_non_negative", "timestamp_seconds IS NULL OR timestamp_seconds >= 0");
                    table.ForeignKey(
                        name: "fk_comments_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_comments_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "listening_histories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_position_seconds = table.Column<int>(type: "integer", nullable: false),
                    last_played_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listening_histories", x => x.id);
                    table.CheckConstraint("ck_listening_histories_position_non_negative", "last_position_seconds >= 0");
                    table.ForeignKey(
                        name: "fk_listening_histories_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_listening_histories_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "play_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    played_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_play_events", x => x.id);
                    table.CheckConstraint("ck_play_events_duration_positive", "duration_seconds > 0");
                    table.ForeignKey(
                        name: "fk_play_events_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_play_events_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "playlist_items",
                columns: table => new
                {
                    playlist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    added_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_playlist_items", x => new { x.playlist_id, x.track_id });
                    table.CheckConstraint("ck_playlist_items_position_non_negative", "position >= 0");
                    table.ForeignKey(
                        name: "fk_playlist_items_playlists_playlist_id",
                        column: x => x.playlist_id,
                        principalTable: "playlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_playlist_items_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_covers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_covers", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_covers_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_files", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_files_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_likes",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_likes", x => new { x.track_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_track_likes_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_track_likes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_metadata",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_filename = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    embedded_title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    embedded_artist = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    embedded_album = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    embedded_genre = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    embedded_year = table.Column<int>(type: "integer", nullable: true),
                    metadata_json = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_metadata", x => x.track_id);
                    table.ForeignKey(
                        name: "fk_track_metadata_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "track_tags",
                columns: table => new
                {
                    track_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_tags", x => new { x.track_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_track_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_track_tags_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "upload_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    track_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    original_filename = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    temporary_path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_upload_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_upload_operations_tracks_track_id",
                        column: x => x.track_id,
                        principalTable: "tracks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_upload_operations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_albums_cover_id",
                table: "albums",
                column: "cover_id");

            migrationBuilder.CreateIndex(
                name: "ix_albums_name",
                table: "albums",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action",
                table: "audit_logs",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_id",
                table: "audit_logs",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at",
                table: "audit_logs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_comments_author_id",
                table: "comments",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_comments_track_id_created_at",
                table: "comments",
                columns: new[] { "track_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_follows_followed_id",
                table: "follows",
                column: "followed_id");

            migrationBuilder.CreateIndex(
                name: "ix_follows_follower_id",
                table: "follows",
                column: "follower_id");

            migrationBuilder.CreateIndex(
                name: "ix_genres_name",
                table: "genres",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_genres_slug",
                table: "genres",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listening_histories_track_id",
                table: "listening_histories",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_listening_histories_user_id_last_played_at",
                table: "listening_histories",
                columns: new[] { "user_id", "last_played_at" });

            migrationBuilder.CreateIndex(
                name: "ix_listening_histories_user_id_track_id",
                table: "listening_histories",
                columns: new[] { "user_id", "track_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_play_events_played_at",
                table: "play_events",
                column: "played_at");

            migrationBuilder.CreateIndex(
                name: "ix_play_events_track_id_played_at",
                table: "play_events",
                columns: new[] { "track_id", "played_at" });

            migrationBuilder.CreateIndex(
                name: "ix_play_events_user_id_played_at",
                table: "play_events",
                columns: new[] { "user_id", "played_at" });

            migrationBuilder.CreateIndex(
                name: "ix_playlist_favorites_user_id",
                table: "playlist_favorites",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_playlist_follows_user_id",
                table: "playlist_follows",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_playlist_items_playlist_id_position",
                table: "playlist_items",
                columns: new[] { "playlist_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_playlist_items_track_id",
                table: "playlist_items",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_playlists_cover_file_id",
                table: "playlists",
                column: "cover_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_playlists_owner_id",
                table: "playlists",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_playlists_visibility_created_at",
                table: "playlists",
                columns: new[] { "visibility", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_expires_at",
                table: "refresh_tokens",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reports_reporter_id",
                table: "reports",
                column: "reporter_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_reviewed_by",
                table: "reports",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ix_reports_status_created_at",
                table: "reports",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reports_target_type_target_id",
                table: "reports",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_storage_path",
                table: "stored_files",
                column: "storage_path",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_name",
                table: "tags",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_slug",
                table: "tags",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_track_covers_track_id_size",
                table: "track_covers",
                columns: new[] { "track_id", "size" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_track_files_track_id",
                table: "track_files",
                column: "track_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_track_likes_track_id",
                table: "track_likes",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_track_likes_user_id_created_at",
                table: "track_likes",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_track_tags_tag_id",
                table: "track_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_album_id",
                table: "tracks",
                column: "album_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_created_at",
                table: "tracks",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_genre_id",
                table: "tracks",
                column: "genre_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_owner_id",
                table: "tracks",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_play_count",
                table: "tracks",
                column: "play_count");

            migrationBuilder.CreateIndex(
                name: "ix_tracks_visibility_published_at",
                table: "tracks",
                columns: new[] { "visibility", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_upload_operations_status",
                table: "upload_operations",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_upload_operations_track_id",
                table: "upload_operations",
                column: "track_id");

            migrationBuilder.CreateIndex(
                name: "ix_upload_operations_user_id_created_at",
                table: "upload_operations",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_user_exports_user_id_created_at",
                table: "user_exports",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_users_avatar_file_id",
                table: "users",
                column: "avatar_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_created_at",
                table: "users",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_username_normalized",
                table: "users",
                column: "username_normalized",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "comments");

            migrationBuilder.DropTable(
                name: "follows");

            migrationBuilder.DropTable(
                name: "listening_histories");

            migrationBuilder.DropTable(
                name: "play_events");

            migrationBuilder.DropTable(
                name: "playlist_favorites");

            migrationBuilder.DropTable(
                name: "playlist_follows");

            migrationBuilder.DropTable(
                name: "playlist_items");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "reports");

            migrationBuilder.DropTable(
                name: "track_covers");

            migrationBuilder.DropTable(
                name: "track_files");

            migrationBuilder.DropTable(
                name: "track_likes");

            migrationBuilder.DropTable(
                name: "track_metadata");

            migrationBuilder.DropTable(
                name: "track_tags");

            migrationBuilder.DropTable(
                name: "upload_operations");

            migrationBuilder.DropTable(
                name: "user_exports");

            migrationBuilder.DropTable(
                name: "user_settings");

            migrationBuilder.DropTable(
                name: "playlists");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "tracks");

            migrationBuilder.DropTable(
                name: "albums");

            migrationBuilder.DropTable(
                name: "genres");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "stored_files");
        }
    }
}
