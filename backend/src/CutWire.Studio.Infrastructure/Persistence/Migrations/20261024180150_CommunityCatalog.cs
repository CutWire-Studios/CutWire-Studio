using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using NpgsqlTypes;

#nullable disable

namespace CutWire.Studio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CommunityCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "download_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    install_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rolled_up_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_download_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_download_daily",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    downloads = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_download_daily", x => new { x.item_id, x.day });
                });

            migrationBuilder.CreateTable(
                name: "item_flags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    @params = table.Column<JsonElement>(name: "params", type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_flags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    blob = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    bytes = table.Column<int>(type: "integer", nullable: false),
                    dhash = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_media", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_media_blobs_blob",
                        column: x => x.blob,
                        principalTable: "blobs",
                        principalColumn: "sha256",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_tags",
                columns: table => new
                {
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_tags", x => new { x.item_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_item_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    changelog = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    package_blob = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    format = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    min_app_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    manifest = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_versions_blobs_package_blob",
                        column: x => x.package_blob,
                        principalTable: "blobs",
                        principalColumn: "sha256",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    license = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    visibility_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    score_up = table.Column<double>(type: "double precision", nullable: false),
                    score_down = table.Column<double>(type: "double precision", nullable: false),
                    wilson_score = table.Column<double>(type: "double precision", nullable: false),
                    hot_score = table.Column<double>(type: "double precision", nullable: false),
                    downloads_total = table.Column<long>(type: "bigint", nullable: false),
                    kind_meta = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    search_tags = table.Column<string>(type: "text", nullable: false),
                    search_extra = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    search_vector = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: true, computedColumnSql: "setweight(to_tsvector('english', coalesce(title, '')), 'A') || setweight(to_tsvector('english', coalesce(search_tags, '')), 'B') || setweight(to_tsvector('english', coalesce(summary, '')), 'C') || setweight(to_tsvector('english', coalesce(description, '') || ' ' || coalesce(search_extra, '')), 'D')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_items", x => x.id);
                    table.CheckConstraint("ck_items_slug_format", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.ForeignKey(
                        name: "fk_items_item_versions_current_version_id",
                        column: x => x.current_version_id,
                        principalTable: "item_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_items_user_profiles_owner_id",
                        column: x => x.owner_id,
                        principalTable: "user_profiles",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_download_events_item_id_at",
                table: "download_events",
                columns: new[] { "item_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_download_events_pending",
                table: "download_events",
                column: "id",
                filter: "rolled_up_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_download_events_version_id",
                table: "download_events",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_flags_item_id",
                table: "item_flags",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_flags_resolved_at",
                table: "item_flags",
                column: "resolved_at",
                filter: "resolved_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_item_media_blob",
                table: "item_media",
                column: "blob");

            migrationBuilder.CreateIndex(
                name: "ix_item_media_item_id_role",
                table: "item_media",
                columns: new[] { "item_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_tags_tag_id",
                table: "item_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_versions_item_id_version",
                table: "item_versions",
                columns: new[] { "item_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_versions_package_blob",
                table: "item_versions",
                column: "package_blob");

            migrationBuilder.CreateIndex(
                name: "ix_items_current_version_id",
                table: "items",
                column: "current_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_items_kind_slug",
                table: "items",
                columns: new[] { "kind", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_items_owner_id_status",
                table: "items",
                columns: new[] { "owner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_items_search_vector",
                table: "items",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_items_status_downloads_total",
                table: "items",
                columns: new[] { "status", "downloads_total" });

            migrationBuilder.CreateIndex(
                name: "ix_items_status_hot_score",
                table: "items",
                columns: new[] { "status", "hot_score" });

            migrationBuilder.CreateIndex(
                name: "ix_items_status_published_at",
                table: "items",
                columns: new[] { "status", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_items_status_wilson_score",
                table: "items",
                columns: new[] { "status", "wilson_score" });

            migrationBuilder.CreateIndex(
                name: "ix_items_title",
                table: "items",
                column: "title")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_tags_kind_slug",
                table: "tags",
                columns: new[] { "kind", "slug" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_download_events_item_versions_version_id",
                table: "download_events",
                column: "version_id",
                principalTable: "item_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_download_events_items_item_id",
                table: "download_events",
                column: "item_id",
                principalTable: "items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_download_daily_items_item_id",
                table: "item_download_daily",
                column: "item_id",
                principalTable: "items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_flags_items_item_id",
                table: "item_flags",
                column: "item_id",
                principalTable: "items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_media_items_item_id",
                table: "item_media",
                column: "item_id",
                principalTable: "items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_tags_items_item_id",
                table: "item_tags",
                column: "item_id",
                principalTable: "items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_item_versions_items_item_id",
                table: "item_versions",
                column: "item_id",
                principalTable: "items",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_items_item_versions_current_version_id",
                table: "items");

            migrationBuilder.DropTable(
                name: "download_events");

            migrationBuilder.DropTable(
                name: "item_download_daily");

            migrationBuilder.DropTable(
                name: "item_flags");

            migrationBuilder.DropTable(
                name: "item_media");

            migrationBuilder.DropTable(
                name: "item_tags");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "item_versions");

            migrationBuilder.DropTable(
                name: "items");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");
        }
    }
}
