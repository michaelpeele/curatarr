using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Curatarr.Engine.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AirDate",
                table: "SourceItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AirEpisodeNumber",
                table: "SourceItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AirIsSeasonPremiere",
                table: "SourceItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AirSeasonNumber",
                table: "SourceItems",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DateDetailsParsedAt",
                table: "SourceItems",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleaseDate",
                table: "SourceItems",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AirDate",
                table: "SourceItems");

            migrationBuilder.DropColumn(
                name: "AirEpisodeNumber",
                table: "SourceItems");

            migrationBuilder.DropColumn(
                name: "AirIsSeasonPremiere",
                table: "SourceItems");

            migrationBuilder.DropColumn(
                name: "AirSeasonNumber",
                table: "SourceItems");

            migrationBuilder.DropColumn(
                name: "DateDetailsParsedAt",
                table: "SourceItems");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "SourceItems");
        }
    }
}
