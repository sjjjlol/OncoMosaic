using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OncoMosaic.Api.Migrations
{
    /// <inheritdoc />
    public partial class Ki67Measurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Dimension",
                table: "ReviewChanges",
                type: "longtext",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "Ki67Quality",
                table: "Cells",
                type: "longtext",
                nullable: false);

            migrationBuilder.AddColumn<int>(
                name: "Ki67ValidPixelCount",
                table: "Cells",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "Ki67Value",
                table: "Cells",
                type: "double",
                nullable: true);
            migrationBuilder.Sql("UPDATE `ReviewChanges` SET `Dimension` = 'identity'");
            migrationBuilder.Sql("UPDATE `Cells` SET `Ki67Quality` = 'not-measured'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Dimension",
                table: "ReviewChanges");

            migrationBuilder.DropColumn(
                name: "Ki67Quality",
                table: "Cells");

            migrationBuilder.DropColumn(
                name: "Ki67ValidPixelCount",
                table: "Cells");

            migrationBuilder.DropColumn(
                name: "Ki67Value",
                table: "Cells");
        }
    }
}
