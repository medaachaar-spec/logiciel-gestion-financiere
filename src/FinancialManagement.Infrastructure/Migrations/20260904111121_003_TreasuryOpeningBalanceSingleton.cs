using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _003_TreasuryOpeningBalanceSingleton : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX UX_TreasuryOpeningBalance_Singleton " +
                "ON TreasuryOpeningBalance ((1));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX UX_TreasuryOpeningBalance_Singleton;");
        }
    }
}
