using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEvalBatchId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EvalBatchId",
                table: "Trips",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvalBatchId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                nullable: true);

            // Filtered indexes: only eval-tagged rows are indexed, so teardown's
            // "WHERE EvalBatchId = @batch" is cheap and real-world queries are
            // unaffected.
            migrationBuilder.CreateIndex(
                name: "IX_Trips_EvalBatchId",
                table: "Trips",
                column: "EvalBatchId",
                filter: "[EvalBatchId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_EvalBatchId",
                table: "AspNetUsers",
                column: "EvalBatchId",
                filter: "[EvalBatchId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_EvalBatchId",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_EvalBatchId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "EvalBatchId",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "EvalBatchId",
                table: "AspNetUsers");
        }
    }
}
