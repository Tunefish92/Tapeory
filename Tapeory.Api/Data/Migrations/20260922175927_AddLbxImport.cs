using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tapeory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLbxImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceLbxFileId",
                table: "Templates",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TemplateConversionWarnings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TemplateConversionWarnings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateConversionWarnings_Templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "Templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Templates_SourceLbxFileId",
                table: "Templates",
                column: "SourceLbxFileId");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateConversionWarnings_TemplateId",
                table: "TemplateConversionWarnings",
                column: "TemplateId");

            migrationBuilder.AddForeignKey(
                name: "FK_Templates_UploadedFiles_SourceLbxFileId",
                table: "Templates",
                column: "SourceLbxFileId",
                principalTable: "UploadedFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Templates_UploadedFiles_SourceLbxFileId",
                table: "Templates");

            migrationBuilder.DropTable(
                name: "TemplateConversionWarnings");

            migrationBuilder.DropIndex(
                name: "IX_Templates_SourceLbxFileId",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "SourceLbxFileId",
                table: "Templates");
        }
    }
}
