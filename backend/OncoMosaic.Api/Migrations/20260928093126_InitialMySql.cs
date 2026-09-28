using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OncoMosaic.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialMySql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Images",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    Description = table.Column<string>(type: "longtext", nullable: false),
                    FileKey = table.Column<string>(type: "longtext", nullable: false),
                    PreviewKey = table.Column<string>(type: "longtext", nullable: false),
                    Sha256 = table.Column<string>(type: "longtext", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    BandCount = table.Column<int>(type: "int", nullable: false),
                    WavelengthsJson = table.Column<string>(type: "longtext", nullable: false),
                    PixelSizeUm = table.Column<double>(type: "double", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Images_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Rois",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ImageId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false),
                    X = table.Column<int>(type: "int", nullable: false),
                    Y = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<int>(type: "int", nullable: false),
                    Height = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    RegionTag = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rois", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rois_Images_ImageId",
                        column: x => x.ImageId,
                        principalTable: "Images",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AnalysisRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    RoiId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Status = table.Column<string>(type: "longtext", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    ModelVersion = table.Column<string>(type: "longtext", nullable: false),
                    AlgorithmVersion = table.Column<string>(type: "longtext", nullable: false),
                    ThresholdsJson = table.Column<string>(type: "longtext", nullable: false),
                    ErrorCode = table.Column<string>(type: "longtext", nullable: true),
                    ErrorMessage = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FinishedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalysisRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnalysisRuns_Rois_RoiId",
                        column: x => x.RoiId,
                        principalTable: "Rois",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Artifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    FileKey = table.Column<string>(type: "longtext", nullable: false),
                    Sha256 = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Artifacts_AnalysisRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Cells",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LocalIndex = table.Column<int>(type: "int", nullable: false),
                    X = table.Column<double>(type: "double", nullable: false),
                    Y = table.Column<double>(type: "double", nullable: false),
                    AreaPx = table.Column<int>(type: "int", nullable: false),
                    DapiValue = table.Column<double>(type: "double", nullable: false),
                    PanckValue = table.Column<double>(type: "double", nullable: false),
                    Cd8Value = table.Column<double>(type: "double", nullable: false),
                    QualityFlag = table.Column<string>(type: "longtext", nullable: false),
                    ContourJson = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cells", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cells_AnalysisRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReviewRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    RunId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewRevisions_AnalysisRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "AnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ReviewChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    RevisionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CellId = table.Column<Guid>(type: "char(36)", nullable: false),
                    NewLabel = table.Column<string>(type: "longtext", nullable: false),
                    Reason = table.Column<string>(type: "longtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewChanges_Cells_CellId",
                        column: x => x.CellId,
                        principalTable: "Cells",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReviewChanges_ReviewRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "ReviewRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_RoiId",
                table: "AnalysisRuns",
                column: "RoiId");

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_RunId_Kind",
                table: "Artifacts",
                columns: new[] { "RunId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cells_RunId_LocalIndex",
                table: "Cells",
                columns: new[] { "RunId", "LocalIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Images_ProjectId",
                table: "Images",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewChanges_CellId",
                table: "ReviewChanges",
                column: "CellId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewChanges_RevisionId",
                table: "ReviewChanges",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRevisions_RunId_Version",
                table: "ReviewRevisions",
                columns: new[] { "RunId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rois_ImageId",
                table: "Rois",
                column: "ImageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Artifacts");

            migrationBuilder.DropTable(
                name: "ReviewChanges");

            migrationBuilder.DropTable(
                name: "Cells");

            migrationBuilder.DropTable(
                name: "ReviewRevisions");

            migrationBuilder.DropTable(
                name: "AnalysisRuns");

            migrationBuilder.DropTable(
                name: "Rois");

            migrationBuilder.DropTable(
                name: "Images");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
