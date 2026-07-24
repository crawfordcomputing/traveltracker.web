using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTravelerProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultCostCenter", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactName", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmergencyContactPhone", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FrequentFlyerNumbers", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HotelPreference", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KnownTravelerNumberProtected", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MealPreference", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MobileNumber", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Nationality", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "PassportExpiry", table: "AspNetUsers",
                type: "date", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PassportNumberProtected", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReimbursementMethod", table: "AspNetUsers",
                type: "int", nullable: false, defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SeatPreference", table: "AspNetUsers",
                type: "nvarchar(max)", nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DefaultCostCenter", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "EmergencyContactName", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "EmergencyContactPhone", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "FrequentFlyerNumbers", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "HotelPreference", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "KnownTravelerNumberProtected", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "MealPreference", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "MobileNumber", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "Nationality", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "PassportExpiry", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "PassportNumberProtected", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "ReimbursementMethod", table: "AspNetUsers");
            migrationBuilder.DropColumn(name: "SeatPreference", table: "AspNetUsers");
        }
    }
}
