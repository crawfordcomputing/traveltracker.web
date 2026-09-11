using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TravelTracker.Web.Data.Migrations
{
    // Adds the immutable trip reference code (ADR-0004). Hand-authored: the column is
    // added nullable, existing rows are backfilled in T-SQL with the same alphabet and
    // TT-YYYY-XXX-XXX shape the app generates, then the column is tightened to NOT
    // NULL and unique-indexed. Companion .Designer.cs carries the [Migration] attribute.
    /// <inheritdoc />
    public partial class AddTripCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Trips",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // Crockford Base32 (no I, L, O, U). CRYPT_GEN_RANDOM gives crypto-random
            // bytes; byte % 32 is unbiased because 256 divides evenly by 32. Year comes
            // from CreatedAt, matching Domain/TripCode. Row-by-row is fine at this scale.
            migrationBuilder.Sql(@"
DECLARE @a char(32) = '0123456789ABCDEFGHJKMNPQRSTVWXYZ';
DECLARE @id int, @y int, @code nvarchar(20), @rand nvarchar(6), @b varbinary(6), @i int;
DECLARE c CURSOR LOCAL FAST_FORWARD FOR
    SELECT Id, YEAR(CreatedAt) FROM Trips WHERE Code IS NULL;
OPEN c; FETCH NEXT FROM c INTO @id, @y;
WHILE @@FETCH_STATUS = 0
BEGIN
    WHILE 1 = 1
    BEGIN
        SET @b = CRYPT_GEN_RANDOM(6);
        SET @rand = N'';
        SET @i = 1;
        WHILE @i <= 6
        BEGIN
            SET @rand += SUBSTRING(@a, CAST(SUBSTRING(@b, @i, 1) AS int) % 32 + 1, 1);
            SET @i += 1;
        END
        SET @code = CONCAT(N'TT-', @y, N'-', LEFT(@rand, 3), N'-', RIGHT(@rand, 3));
        IF NOT EXISTS (SELECT 1 FROM Trips WHERE Code = @code) BREAK;
    END
    UPDATE Trips SET Code = @code WHERE Id = @id;
    FETCH NEXT FROM c INTO @id, @y;
END
CLOSE c; DEALLOCATE c;
");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Trips",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Trips_Code",
                table: "Trips",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_Code",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Trips");
        }
    }
}
