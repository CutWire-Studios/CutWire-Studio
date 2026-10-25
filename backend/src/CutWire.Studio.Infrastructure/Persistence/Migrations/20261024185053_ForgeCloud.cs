using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CutWire.Studio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ForgeCloud : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_usage",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    sub = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    neurons = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ai_usage", x => new { x.day, x.sub });
                });

            migrationBuilder.CreateTable(
                name: "forge_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    doc_json = table.Column<string>(type: "text", nullable: false),
                    thumb_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forge_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_forge_documents_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_forge_documents_blobs_thumb_sha256",
                        column: x => x.thumb_sha256,
                        principalTable: "blobs",
                        principalColumn: "sha256",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "theme_drafts",
                columns: table => new
                {
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    doc_json = table.Column<string>(type: "text", nullable: false),
                    updated_at_ms = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_theme_drafts", x => new { x.owner_id, x.id });
                    table.ForeignKey(
                        name: "fk_theme_drafts_asp_net_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "forge_doc_assets",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    blob_sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forge_doc_assets", x => new { x.document_id, x.blob_sha256 });
                    table.ForeignKey(
                        name: "fk_forge_doc_assets_blobs_blob_sha256",
                        column: x => x.blob_sha256,
                        principalTable: "blobs",
                        principalColumn: "sha256",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_forge_doc_assets_forge_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "forge_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_forge_doc_assets_blob_sha256",
                table: "forge_doc_assets",
                column: "blob_sha256");

            migrationBuilder.CreateIndex(
                name: "ix_forge_documents_owner_id_local_id",
                table: "forge_documents",
                columns: new[] { "owner_id", "local_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_forge_documents_owner_id_updated_at",
                table: "forge_documents",
                columns: new[] { "owner_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_forge_documents_thumb_sha256",
                table: "forge_documents",
                column: "thumb_sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_usage");

            migrationBuilder.DropTable(
                name: "forge_doc_assets");

            migrationBuilder.DropTable(
                name: "theme_drafts");

            migrationBuilder.DropTable(
                name: "forge_documents");
        }
    }
}
