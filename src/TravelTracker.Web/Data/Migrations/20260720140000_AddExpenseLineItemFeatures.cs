using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenseLineItemFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Attendees",
                table: "Expenses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BusinessPurpose",
                table: "Expenses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MissingReceiptAffidavit",
                table: "Expenses",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Reimbursed",
                table: "Expenses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReimbursedDate",
                table: "Expenses",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpenseSplits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpenseId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BaseAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseSplits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseSplits_Expenses_ExpenseId",
                        column: x => x.ExpenseId,
                        principalTable: "Expenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseDrafts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TripId = table.Column<int>(type: "int", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: true),
                    Vendor = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsPersonal = table.Column<bool>(type: "bit", nullable: false),
                    Attendees = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BusinessPurpose = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseDrafts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseDrafts_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseDrafts_TripId",
                table: "ExpenseDrafts",
                column: "TripId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseDrafts_UserId_TripId",
                table: "ExpenseDrafts",
                columns: new[] { "UserId", "TripId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseSplits_ExpenseId",
                table: "ExpenseSplits",
                column: "ExpenseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExpenseDrafts");

            migrationBuilder.DropTable(
                name: "ExpenseSplits");

            migrationBuilder.DropColumn(
                name: "Attendees",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "BusinessPurpose",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "MissingReceiptAffidavit",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "Reimbursed",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "ReimbursedDate",
                table: "Expenses");
        }
    }
}
