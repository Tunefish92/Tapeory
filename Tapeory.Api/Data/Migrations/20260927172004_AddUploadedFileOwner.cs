using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tapeory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadedFileOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "UploadedFiles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_OwnerUserId",
                table: "UploadedFiles",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_UploadedFiles_Users_OwnerUserId",
                table: "UploadedFiles",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UploadedFiles_Users_OwnerUserId",
                table: "UploadedFiles");

            migrationBuilder.DropIndex(
                name: "IX_UploadedFiles_OwnerUserId",
                table: "UploadedFiles");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "UploadedFiles");
        }
    }
}
