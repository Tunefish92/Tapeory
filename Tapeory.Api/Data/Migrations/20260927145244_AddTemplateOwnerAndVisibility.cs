using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tapeory.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTemplateOwnerAndVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "Templates",
                type: "tinyint(1)",
                nullable: false,
                // Templates from before accounts existed stay visible to everyone; new ones are
                // private unless made public (the app always sets the value).
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "Templates",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Templates_OwnerUserId",
                table: "Templates",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Templates_Users_OwnerUserId",
                table: "Templates",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Templates_Users_OwnerUserId",
                table: "Templates");

            migrationBuilder.DropIndex(
                name: "IX_Templates_OwnerUserId",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "Templates");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Templates");
        }
    }
}
