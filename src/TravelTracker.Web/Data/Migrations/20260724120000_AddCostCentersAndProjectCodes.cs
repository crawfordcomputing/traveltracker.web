using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCostCentersAndProjectCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Free-text cost center / project code become FK-linked reference data.
            // Pre-release app (dev/eval): the old free-text values are dropped rather
            // than migrated — there is no production data to preserve.
            migrationBuilder.DropColumn(
                name: "CostCenter",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ProjectCode",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DefaultCostCenter",
                table: "AspNetUsers");

            migrationBuilder.CreateTable(
                name: "CostCenters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostCenters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectCodes", x => x.Id);
                });

            migrationBuilder.AddColumn<int>(
                name: "DefaultCostCenterId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CostCenterId",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProjectCodeId",
                table: "Trips",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CostCenters_Code",
                table: "CostCenters",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectCodes_Code",
                table: "ProjectCodes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DefaultCostCenterId",
                table: "AspNetUsers",
                column: "DefaultCostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_CostCenterId",
                table: "Trips",
                column: "CostCenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_ProjectCodeId",
                table: "Trips",
                column: "ProjectCodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_CostCenters_DefaultCostCenterId",
                table: "AspNetUsers",
                column: "DefaultCostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_CostCenters_CostCenterId",
                table: "Trips",
                column: "CostCenterId",
                principalTable: "CostCenters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_ProjectCodes_ProjectCodeId",
                table: "Trips",
                column: "ProjectCodeId",
                principalTable: "ProjectCodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_CostCenters_DefaultCostCenterId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Trips_CostCenters_CostCenterId",
                table: "Trips");

            migrationBuilder.DropForeignKey(
                name: "FK_Trips_ProjectCodes_ProjectCodeId",
                table: "Trips");

            migrationBuilder.DropTable(
                name: "CostCenters");

            migrationBuilder.DropTable(
                name: "ProjectCodes");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DefaultCostCenterId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_Trips_CostCenterId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_ProjectCodeId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DefaultCostCenterId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CostCenterId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "ProjectCodeId",
                table: "Trips");

            migrationBuilder.AddColumn<string>(
                name: "DefaultCostCenter",
                table: "AspNetUsers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CostCenter",
                table: "Trips",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectCode",
                table: "Trips",
                type: "nvarchar(60)",
                maxLength: 60,
                nullable: true);
        }
    }
}
