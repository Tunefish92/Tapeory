using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tapeory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintOptionsAndPrintServerQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CutMode",
                table: "PrintJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Quality",
                table: "PrintJobs",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "QueueName",
                table: "Printers",
                type: "varchar(127)",
                maxLength: 127,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CutMode",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "Quality",
                table: "PrintJobs");

            migrationBuilder.DropColumn(
                name: "QueueName",
                table: "Printers");
        }
    }
}
