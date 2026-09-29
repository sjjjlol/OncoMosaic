using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OncoMosaic.Api.Migrations
{
    /// <inheritdoc />
    public partial class SpectralMifSimulation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcquisitionJson",
                table: "Images",
                type: "longtext",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "AssayKey",
                table: "Images",
                type: "longtext",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "AssaySha256",
                table: "Images",
                type: "longtext",
                nullable: false);

            migrationBuilder.AddColumn<double>(
                name: "Cd3Value",
                table: "Cells",
                type: "double",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "ValidTissuePx",
                table: "AnalysisRuns",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcquisitionJson",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "AssayKey",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "AssaySha256",
                table: "Images");

            migrationBuilder.DropColumn(
                name: "Cd3Value",
                table: "Cells");

            migrationBuilder.DropColumn(
                name: "ValidTissuePx",
                table: "AnalysisRuns");
        }
    }
}
