using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CutWire.Studio.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketplaceLayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feature_flags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    value_json = table.Column<string>(type: "jsonb", nullable: true),
                    distributions = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    min_app_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    max_app_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feature_flags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "marketplace_layouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    distributions = table.Column<List<string>>(type: "text[]", nullable: false, defaultValueSql: "'{}'"),
                    min_app_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    max_app_version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    pages_json = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_marketplace_layouts", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feature_flags_key",
                table: "feature_flags",
                column: "key");

            migrationBuilder.CreateIndex(
                name: "ix_marketplace_layouts_name",
                table: "marketplace_layouts",
                column: "name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feature_flags");

            migrationBuilder.DropTable(
                name: "marketplace_layouts");
        }
    }
}
