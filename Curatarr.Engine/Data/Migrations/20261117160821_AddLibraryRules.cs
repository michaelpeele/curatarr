using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Curatarr.Engine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SetByRule",
                table: "VirtualLibraryItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SetByRule",
                table: "HiddenItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "LibraryRuleRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    VirtualLibraryId = table.Column<string>(type: "TEXT", nullable: false),
                    RanAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RuleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Approved = table.Column<int>(type: "INTEGER", nullable: false),
                    Hidden = table.Column<int>(type: "INTEGER", nullable: false),
                    Unapproved = table.Column<int>(type: "INTEGER", nullable: false),
                    Unhidden = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryRuleRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryRuleRuns_VirtualLibraries_VirtualLibraryId",
                        column: x => x.VirtualLibraryId,
                        principalTable: "VirtualLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    VirtualLibraryId = table.Column<string>(type: "TEXT", nullable: false),
                    Field = table.Column<string>(type: "TEXT", nullable: false),
                    Operator = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LibraryRules_VirtualLibraries_VirtualLibraryId",
                        column: x => x.VirtualLibraryId,
                        principalTable: "VirtualLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryRuleRuns_VirtualLibraryId",
                table: "LibraryRuleRuns",
                column: "VirtualLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryRules_VirtualLibraryId",
                table: "LibraryRules",
                column: "VirtualLibraryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LibraryRuleRuns");

            migrationBuilder.DropTable(
                name: "LibraryRules");

            migrationBuilder.DropColumn(
                name: "SetByRule",
                table: "VirtualLibraryItems");

            migrationBuilder.DropColumn(
                name: "SetByRule",
                table: "HiddenItems");
        }
    }
}
