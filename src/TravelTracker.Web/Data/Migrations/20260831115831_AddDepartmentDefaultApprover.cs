using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDepartmentDefaultApprover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultApproverId",
                table: "Departments",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_DefaultApproverId",
                table: "Departments",
                column: "DefaultApproverId");

            migrationBuilder.AddForeignKey(
                name: "FK_Departments_AspNetUsers_DefaultApproverId",
                table: "Departments",
                column: "DefaultApproverId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Departments_AspNetUsers_DefaultApproverId",
                table: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Departments_DefaultApproverId",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "DefaultApproverId",
                table: "Departments");
        }
    }
}
