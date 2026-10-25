using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CutWire.Studio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asset_categories",
                columns: table => new
                {
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asset_categories", x => x.category);
                });

            migrationBuilder.CreateTable(
                name: "download_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    native = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    variant_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    progress = table.Column<double>(type: "double precision", nullable: false),
                    quota_subject = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    quota_window = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    blob = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    mime = table.Column<string>(type: "character varying(127)", maxLength: 127, nullable: true),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    file_sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    license_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    license_url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    attribution = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_detail = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_download_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_download_jobs_blobs_blob",
                        column: x => x.blob,
                        principalTable: "blobs",
                        principalColumn: "sha256",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "drift_asset_packs",
                columns: table => new
                {
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    index_json = table.Column<string>(type: "jsonb", nullable: false),
                    files_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false),
                    synced_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_drift_asset_packs", x => x.version);
                });

            migrationBuilder.CreateTable(
                name: "provider_cache",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(2200)", maxLength: 2200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_cache", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "provider_settings",
                columns: table => new
                {
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false),
                    quota_limit = table.Column<int>(type: "integer", nullable: false),
                    quota_window = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    quota_scope = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_settings", x => new { x.provider, x.type });
                });

            migrationBuilder.CreateTable(
                name: "quota_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    subject = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    window_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quota_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sfx_categories",
                columns: table => new
                {
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    label = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    sort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sfx_categories", x => x.category);
                });

            migrationBuilder.CreateIndex(
                name: "ix_download_jobs_blob",
                table: "download_jobs",
                column: "blob");

            migrationBuilder.CreateIndex(
                name: "ix_download_jobs_client_id_status",
                table: "download_jobs",
                columns: new[] { "client_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_download_jobs_expires_at",
                table: "download_jobs",
                column: "expires_at",
                filter: "expires_at is not null");

            migrationBuilder.CreateIndex(
                name: "ix_provider_cache_expires_at",
                table: "provider_cache",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_quota_events_job_id_kind",
                table: "quota_events",
                columns: new[] { "job_id", "kind" },
                unique: true,
                filter: "job_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_quota_events_subject_provider_window_start_kind",
                table: "quota_events",
                columns: new[] { "subject", "provider", "window_start", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_quota_events_window_start",
                table: "quota_events",
                column: "window_start");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asset_categories");

            migrationBuilder.DropTable(
                name: "download_jobs");

            migrationBuilder.DropTable(
                name: "drift_asset_packs");

            migrationBuilder.DropTable(
                name: "provider_cache");

            migrationBuilder.DropTable(
                name: "provider_settings");

            migrationBuilder.DropTable(
                name: "quota_events");

            migrationBuilder.DropTable(
                name: "sfx_categories");
        }
    }
}
