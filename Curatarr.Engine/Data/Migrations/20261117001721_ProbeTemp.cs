using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Curatarr.Engine.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProbeTemp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_HiddenItems_SourceItemId",
                table: "HiddenItems",
                column: "SourceItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_HiddenItems_SourceItemId",
                table: "HiddenItems");
        }
    }
}
