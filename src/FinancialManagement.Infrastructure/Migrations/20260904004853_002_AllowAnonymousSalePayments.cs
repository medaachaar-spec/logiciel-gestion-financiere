using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _002_AllowAnonymousSalePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "ClientId",
                table: "CustomerPayments",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "INTEGER");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TABLE IF EXISTS temp."__CustomerPayments_ClientId_DowngradeGuard";

                CREATE TEMP TABLE "__CustomerPayments_ClientId_DowngradeGuard" (
                    "IsSafe" INTEGER NOT NULL
                        CONSTRAINT "CK_CustomerPayments_ClientId_DowngradeGuard"
                        CHECK ("IsSafe" = 1)
                );

                INSERT INTO "__CustomerPayments_ClientId_DowngradeGuard" ("IsSafe")
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM "CustomerPayments"
                        WHERE "ClientId" IS NULL
                    ) THEN 0
                    ELSE 1
                END;

                DROP TABLE temp."__CustomerPayments_ClientId_DowngradeGuard";
                """);

            migrationBuilder.AlterColumn<long>(
                name: "ClientId",
                table: "CustomerPayments",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER",
                oldNullable: true);
        }
    }
}
