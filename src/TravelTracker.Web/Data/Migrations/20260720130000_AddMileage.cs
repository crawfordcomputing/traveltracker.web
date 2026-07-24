using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMileage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MileageRates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Jurisdiction = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    VehicleType = table.Column<int>(type: "int", nullable: false),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MileageRates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MileageEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Distance = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Unit = table.Column<int>(type: "int", nullable: false),
                    IsRoundTrip = table.Column<bool>(type: "bit", nullable: false),
                    CommuteDeduction = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    Rate = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Jurisdiction = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    VehicleType = table.Column<int>(type: "int", nullable: false),
                    RateId = table.Column<int>(type: "int", nullable: true),
                    Purpose = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MileageEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MileageEntries_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MileageEntries_MileageRates_RateId",
                        column: x => x.RateId,
                        principalTable: "MileageRates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MileageEntries_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MileageWaypoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MileageEntryId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MileageWaypoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MileageWaypoints_MileageEntries_MileageEntryId",
                        column: x => x.MileageEntryId,
                        principalTable: "MileageEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MileageEntries_CreatedById",
                table: "MileageEntries",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_MileageEntries_RateId",
                table: "MileageEntries",
                column: "RateId");

            migrationBuilder.CreateIndex(
                name: "IX_MileageEntries_TripId",
                table: "MileageEntries",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_MileageRates_Jurisdiction_VehicleType_Unit_EffectiveDate",
                table: "MileageRates",
                columns: new[] { "Jurisdiction", "VehicleType", "Unit", "EffectiveDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MileageWaypoints_MileageEntryId",
                table: "MileageWaypoints",
                column: "MileageEntryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MileageWaypoints");

            migrationBuilder.DropTable(
                name: "MileageEntries");

            migrationBuilder.DropTable(
                name: "MileageRates");
        }
    }
}
