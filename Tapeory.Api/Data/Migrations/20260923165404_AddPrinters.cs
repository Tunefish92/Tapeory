using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tapeory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrinters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrinterId",
                table: "PrintJobs",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Printers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Model = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConnectionType = table.Column<int>(type: "int", nullable: false),
                    Address = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Port = table.Column<int>(type: "int", nullable: false),
                    PrintServerAddress = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UsbIdentifier = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LabelMediaWidthMm = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    LabelMediaHeightMm = table.Column<decimal>(type: "decimal(6,2)", nullable: true),
                    IsDefault = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LastConnectionStatus = table.Column<int>(type: "int", nullable: false),
                    LastConnectionCheckedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    LastErrorMessage = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Printers", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_PrintJobs_PrinterId",
                table: "PrintJobs",
                column: "PrinterId");

            migrationBuilder.AddForeignKey(
                name: "FK_PrintJobs_Printers_PrinterId",
                table: "PrintJobs",
                column: "PrinterId",
                principalTable: "Printers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrintJobs_Printers_PrinterId",
                table: "PrintJobs");

            migrationBuilder.DropTable(
                name: "Printers");

            migrationBuilder.DropIndex(
                name: "IX_PrintJobs_PrinterId",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "PrinterId",
                table: "PrintJobs");
        }
    }
}
