using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Curatarr.Engine.Data.Migrations
{
    /// <summary>
    /// Sprint 4 #1 + Sprint 5 #6: per-library hidden items table + VirtualLibrary sync
    /// timestamp (used by the UI to tell "marked" vs "synced" when rendering card icon states).
    /// </summary>
    partial class AddHiddenItemsAndLastReconciledAt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hide-mark tracking table, one row per (library, source item) so that hiding
            // a show from "Kids Movies" never affects another library.
            migrationBuilder.CreateTable(
                name: "HiddenItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    VirtualLibraryId = table.Column<string>(type: "TEXT", nullable: false),
                    SourceItemId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HiddenItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HiddenItems_SourceItems_SourceItemId",
                        column: x => x.SourceItemId,
                        principalTable: "SourceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HiddenItems_VirtualLibraries_VirtualLibraryId",
                        column: x => x.VirtualLibraryId,
                        principalTable: "VirtualLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Unique so an item can be hidden from a library at most once (idempotent upsert
            // via INSERT OR IGNORE semantics backed by this index).
            migrationBuilder.CreateIndex(
                name: "IX_HiddenItems_OnePerLibraryAndItem",
                table: "HiddenItems",
                columns: new[] { "VirtualLibraryId", "SourceItemId" },
                unique: true);

            // Sync heartbeat: the UI compares this against VirtualLibraryItem.ApprovedAt to
            // render the check-icon states (yellow = marked, green = synced).
            migrationBuilder.AddColumn<DateTime?>(
                name: "LastReconciledAt",
                table: "VirtualLibraries",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LastReconciledAt", table: "VirtualLibraries");
            migrationBuilder.DropTable(name: "HiddenItems");
        }
    }
}
