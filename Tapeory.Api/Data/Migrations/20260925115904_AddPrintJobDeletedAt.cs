using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tapeory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintJobDeletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "PrintJobs",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "PrintJobs");
        }
    }
}
