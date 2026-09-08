using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinancialManagement.Infrastructure.Migrations;

/// <summary>
/// Frozen 003 DDL plus separate monetary storage checks. EF owns the transaction,
/// including migration history. Never use AddCheckConstraint here: SQLite's
/// automatic rebuild emits transaction-suppressed foreign_keys pragmas.
/// </summary>
public partial class _004_EnforceFinancialIntegerStorageClass : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(BuildSql(enforceStorage: true));

    // Down deliberately restores weaker storage semantics; no financial conversion.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(BuildSql(enforceStorage: false));

    internal static string BuildSql(bool enforceStorage)
    {
        var sql = new StringBuilder("""
            CREATE TEMP TABLE "__Astra005Guard" (
                "Value" BLOB,
                CONSTRAINT "CK_Astra005Guard_Integer" CHECK(typeof("Value") = 'integer')
            );

            """);
        AppendStorageGuard(sql);
        sql.AppendLine("PRAGMA defer_foreign_keys = ON;");
        // Preserve AUTOINCREMENT high-water marks, including deleted highest IDs.
        sql.AppendLine("""
            CREATE TEMP TABLE "__Astra005Sequence" ("Name" TEXT PRIMARY KEY, "Value" BLOB);
            """);
        foreach (var table in Tables)
            sql.AppendLine($"INSERT INTO \"__Astra005Sequence\" SELECT name, seq FROM sqlite_sequence WHERE name = '{table.Name}';");

        // Replacement children reference replacement parents, never old parents.
        // Otherwise DROP on a payment silently cascades even with deferred FKs.
        foreach (var table in Tables)
        {
            var ddl = table.Ddl.Replace($"CREATE TABLE \"{table.Name}\"", $"CREATE TABLE \"__Astra005_{table.Name}\"");
            foreach (var parent in Tables)
                ddl = ddl.Replace($"REFERENCES \"{parent.Name}\"", $"REFERENCES \"__Astra005_{parent.Name}\"");
            if (enforceStorage)
            {
                var checks = string.Join("", table.MoneyColumns.Select(column =>
                    $",\n    CONSTRAINT \"CK_{table.Name}_{column}_IntegerStorage\" CHECK(typeof(\"{column}\") = 'integer')"));
                ddl = ddl.Replace("\n);", checks + "\n);");
            }
            sql.AppendLine(ddl);
            sql.AppendLine($"INSERT INTO \"__Astra005_{table.Name}\" ({table.Columns}) SELECT {table.Columns} FROM \"{table.Name}\";");
        }

        // Only the two allocation tables have incoming edges to financial parents.
        // Drop old children first; replacement children point only to new parents.
        foreach (var table in Tables.Reverse())
            sql.AppendLine($"DROP TABLE \"{table.Name}\";");
        foreach (var table in Tables)
            sql.AppendLine($"ALTER TABLE \"__Astra005_{table.Name}\" RENAME TO \"{table.Name}\";");
        foreach (var table in Tables)
        {
            sql.AppendLine(table.IndexSql);
            sql.AppendLine($"UPDATE sqlite_sequence SET seq = (SELECT \"Value\" FROM \"__Astra005Sequence\" WHERE \"Name\" = '{table.Name}') WHERE name = '{table.Name}' AND EXISTS (SELECT 1 FROM \"__Astra005Sequence\" WHERE \"Name\" = '{table.Name}');");
        }
        sql.AppendLine("""
            CREATE UNIQUE INDEX "UX_TreasuryOpeningBalance_Singleton" ON "TreasuryOpeningBalance" ((1));
            DROP TABLE "__Astra005Sequence";
            """);
        AppendStorageGuard(sql);
        // A SELECT-only PRAGMA cannot abort. Feed any FK violation into the guard.
        sql.AppendLine("""
            INSERT INTO "__Astra005Guard" ("Value") SELECT 'foreign key violation' FROM pragma_foreign_key_check LIMIT 1;
            DROP TABLE "__Astra005Guard";
            """);
        // No BEGIN/COMMIT or suppressed operation: history stays in EF's transaction.
        return sql.ToString();
    }

    private static void AppendStorageGuard(StringBuilder sql)
    {
        foreach (var table in Tables)
        foreach (var column in table.MoneyColumns)
            sql.AppendLine($"INSERT INTO \"__Astra005Guard\" (\"Value\") SELECT \"{column}\" FROM \"{table.Name}\" WHERE typeof(\"{column}\") <> 'integer' LIMIT 1;");
    }

    private sealed record Table(string Name, string Columns, string[] MoneyColumns, string Ddl, string IndexSql);

    // Canonical 003 metadata captured before implementation; independent of future models.
    // Topological order: all financial parents precede both allocation children.
    private static readonly Table[] Tables =
    [
        new("CustomerPayments", "\"Id\", \"AmountMinor\", \"ClientId\", \"CreatedAtUtc\", \"CreatedByUserId\", \"Observation\", \"PaymentDate\", \"PaymentMethod\", \"Reference\"", ["AmountMinor"],
            """
            CREATE TABLE "CustomerPayments" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomerPayments" PRIMARY KEY AUTOINCREMENT,
                "AmountMinor" INTEGER NOT NULL,
                "ClientId" INTEGER NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "CreatedByUserId" INTEGER NOT NULL,
                "Observation" TEXT NULL,
                "PaymentDate" TEXT NOT NULL,
                "PaymentMethod" TEXT NOT NULL,
                "Reference" TEXT NOT NULL,
                CONSTRAINT "CK_CustomerPayments_AmountMinor_Positive" CHECK ("AmountMinor" > 0),
                CONSTRAINT "FK_CustomerPayments_Clients_ClientId" FOREIGN KEY ("ClientId") REFERENCES "Clients" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_CustomerPayments_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_CustomerPayments_ClientId" ON "CustomerPayments" ("ClientId");
            CREATE INDEX "IX_CustomerPayments_CreatedByUserId" ON "CustomerPayments" ("CreatedByUserId");
            CREATE INDEX "IX_CustomerPayments_PaymentDate" ON "CustomerPayments" ("PaymentDate");
            CREATE UNIQUE INDEX "IX_CustomerPayments_Reference" ON "CustomerPayments" ("Reference");
            """),
        new("Expenses", "\"Id\", \"AmountMinor\", \"ConcernedEmployeeUserId\", \"CreatedAtUtc\", \"CreatedByUserId\", \"ExpenseCategoryId\", \"ExpenseDate\", \"Observation\", \"PaymentMethod\", \"Reference\", \"UpdatedAtUtc\"", ["AmountMinor"],
            """
            CREATE TABLE "Expenses" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Expenses" PRIMARY KEY AUTOINCREMENT,
                "AmountMinor" INTEGER NOT NULL,
                "ConcernedEmployeeUserId" INTEGER NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "CreatedByUserId" INTEGER NOT NULL,
                "ExpenseCategoryId" INTEGER NOT NULL,
                "ExpenseDate" TEXT NOT NULL,
                "Observation" TEXT NULL,
                "PaymentMethod" TEXT NOT NULL,
                "Reference" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                CONSTRAINT "CK_Expenses_AmountMinor_Positive" CHECK ("AmountMinor" > 0),
                CONSTRAINT "FK_Expenses_ExpenseCategories_ExpenseCategoryId" FOREIGN KEY ("ExpenseCategoryId") REFERENCES "ExpenseCategories" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_Expenses_Users_ConcernedEmployeeUserId" FOREIGN KEY ("ConcernedEmployeeUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_Expenses_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_Expenses_ConcernedEmployeeUserId" ON "Expenses" ("ConcernedEmployeeUserId");
            CREATE INDEX "IX_Expenses_CreatedByUserId" ON "Expenses" ("CreatedByUserId");
            CREATE INDEX "IX_Expenses_ExpenseCategoryId" ON "Expenses" ("ExpenseCategoryId");
            CREATE INDEX "IX_Expenses_ExpenseDate" ON "Expenses" ("ExpenseDate");
            CREATE UNIQUE INDEX "IX_Expenses_Reference" ON "Expenses" ("Reference");
            """),
        new("InitialCustomerReceivables", "\"Id\", \"ClientId\", \"CreatedAtUtc\", \"CreatedByUserId\", \"DueDate\", \"InitialAmountMinor\", \"Observation\", \"ReceivableDate\", \"Reference\"", ["InitialAmountMinor"],
            """
            CREATE TABLE "InitialCustomerReceivables" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_InitialCustomerReceivables" PRIMARY KEY AUTOINCREMENT,
                "ClientId" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "CreatedByUserId" INTEGER NOT NULL,
                "DueDate" TEXT NULL,
                "InitialAmountMinor" INTEGER NOT NULL,
                "Observation" TEXT NULL,
                "ReceivableDate" TEXT NOT NULL,
                "Reference" TEXT NOT NULL,
                CONSTRAINT "CK_InitialCustomerReceivables_InitialAmountMinor_Positive" CHECK ("InitialAmountMinor" > 0),
                CONSTRAINT "FK_InitialCustomerReceivables_Clients_ClientId" FOREIGN KEY ("ClientId") REFERENCES "Clients" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_InitialCustomerReceivables_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_InitialCustomerReceivables_ClientId" ON "InitialCustomerReceivables" ("ClientId");
            CREATE INDEX "IX_InitialCustomerReceivables_CreatedByUserId" ON "InitialCustomerReceivables" ("CreatedByUserId");
            CREATE INDEX "IX_InitialCustomerReceivables_DueDate" ON "InitialCustomerReceivables" ("DueDate");
            CREATE UNIQUE INDEX "IX_InitialCustomerReceivables_Reference" ON "InitialCustomerReceivables" ("Reference");
            """),
        new("InitialSupplierPayables", "\"Id\", \"CreatedAtUtc\", \"CreatedByUserId\", \"DueDate\", \"InitialAmountMinor\", \"Observation\", \"PayableDate\", \"Reference\", \"SupplierId\"", ["InitialAmountMinor"],
            """
            CREATE TABLE "InitialSupplierPayables" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_InitialSupplierPayables" PRIMARY KEY AUTOINCREMENT,
                "CreatedAtUtc" TEXT NOT NULL,
                "CreatedByUserId" INTEGER NOT NULL,
                "DueDate" TEXT NULL,
                "InitialAmountMinor" INTEGER NOT NULL,
                "Observation" TEXT NULL,
                "PayableDate" TEXT NOT NULL,
                "Reference" TEXT NOT NULL,
                "SupplierId" INTEGER NOT NULL,
                CONSTRAINT "CK_InitialSupplierPayables_InitialAmountMinor_Positive" CHECK ("InitialAmountMinor" > 0),
                CONSTRAINT "FK_InitialSupplierPayables_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_InitialSupplierPayables_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_InitialSupplierPayables_CreatedByUserId" ON "InitialSupplierPayables" ("CreatedByUserId");
            CREATE INDEX "IX_InitialSupplierPayables_DueDate" ON "InitialSupplierPayables" ("DueDate");
            CREATE UNIQUE INDEX "IX_InitialSupplierPayables_Reference" ON "InitialSupplierPayables" ("Reference");
            CREATE INDEX "IX_InitialSupplierPayables_SupplierId" ON "InitialSupplierPayables" ("SupplierId");
            """),
        new("Purchases", "\"Id\", \"CreatedAtUtc\", \"CreatedByUserId\", \"DueDate\", \"Observation\", \"PurchaseDate\", \"Reference\", \"SupplierId\", \"TotalAmountMinor\", \"UpdatedAtUtc\"", ["TotalAmountMinor"],
            """
            CREATE TABLE "Purchases" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Purchases" PRIMARY KEY AUTOINCREMENT,
                "CreatedAtUtc" TEXT NOT NULL,
                "CreatedByUserId" INTEGER NOT NULL,
                "DueDate" TEXT NULL,
                "Observation" TEXT NULL,
                "PurchaseDate" TEXT NOT NULL,
                "Reference" TEXT NOT NULL,
                "SupplierId" INTEGER NOT NULL,
                "TotalAmountMinor" INTEGER NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                CONSTRAINT "CK_Purchases_TotalAmountMinor_Positive" CHECK ("TotalAmountMinor" > 0),
                CONSTRAINT "FK_Purchases_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_Purchases_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_Purchases_CreatedByUserId" ON "Purchases" ("CreatedByUserId");
            CREATE INDEX "IX_Purchases_DueDate" ON "Purchases" ("DueDate");
            CREATE INDEX "IX_Purchases_PurchaseDate" ON "Purchases" ("PurchaseDate");
            CREATE UNIQUE INDEX "IX_Purchases_Reference" ON "Purchases" ("Reference");
            CREATE INDEX "IX_Purchases_SupplierId" ON "Purchases" ("SupplierId");
            """),
        new("Sales", "\"Id\", \"ClientId\", \"CreatedAtUtc\", \"CreatedByUserId\", \"DueDate\", \"Observation\", \"Reference\", \"SaleDate\", \"TotalAmountMinor\", \"UpdatedAtUtc\"", ["TotalAmountMinor"],
            """
            CREATE TABLE "Sales" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Sales" PRIMARY KEY AUTOINCREMENT,
                "ClientId" INTEGER NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "CreatedByUserId" INTEGER NOT NULL,
                "DueDate" TEXT NULL,
                "Observation" TEXT NULL,
                "Reference" TEXT NOT NULL,
                "SaleDate" TEXT NOT NULL,
                "TotalAmountMinor" INTEGER NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                CONSTRAINT "CK_Sales_TotalAmountMinor_Positive" CHECK ("TotalAmountMinor" > 0),
                CONSTRAINT "FK_Sales_Clients_ClientId" FOREIGN KEY ("ClientId") REFERENCES "Clients" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_Sales_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_Sales_ClientId" ON "Sales" ("ClientId");
            CREATE INDEX "IX_Sales_CreatedByUserId" ON "Sales" ("CreatedByUserId");
            CREATE INDEX "IX_Sales_DueDate" ON "Sales" ("DueDate");
            CREATE UNIQUE INDEX "IX_Sales_Reference" ON "Sales" ("Reference");
            CREATE INDEX "IX_Sales_SaleDate" ON "Sales" ("SaleDate");
            """),
        new("SupplierPayments", "\"Id\", \"AmountMinor\", \"CreatedAtUtc\", \"CreatedByUserId\", \"Observation\", \"PaymentDate\", \"PaymentMethod\", \"Reference\", \"SupplierId\"", ["AmountMinor"],
            """
            CREATE TABLE "SupplierPayments" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SupplierPayments" PRIMARY KEY AUTOINCREMENT,
                "AmountMinor" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "CreatedByUserId" INTEGER NOT NULL,
                "Observation" TEXT NULL,
                "PaymentDate" TEXT NOT NULL,
                "PaymentMethod" TEXT NOT NULL,
                "Reference" TEXT NOT NULL,
                "SupplierId" INTEGER NOT NULL,
                CONSTRAINT "CK_SupplierPayments_AmountMinor_Positive" CHECK ("AmountMinor" > 0),
                CONSTRAINT "FK_SupplierPayments_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SupplierPayments_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_SupplierPayments_CreatedByUserId" ON "SupplierPayments" ("CreatedByUserId");
            CREATE INDEX "IX_SupplierPayments_PaymentDate" ON "SupplierPayments" ("PaymentDate");
            CREATE UNIQUE INDEX "IX_SupplierPayments_Reference" ON "SupplierPayments" ("Reference");
            CREATE INDEX "IX_SupplierPayments_SupplierId" ON "SupplierPayments" ("SupplierId");
            """),
        new("TreasuryOpeningBalance", "\"Id\", \"BankAmountMinor\", \"CashAmountMinor\", \"CreatedAtUtc\", \"CreatedByUserId\", \"OpeningDate\"", ["BankAmountMinor", "CashAmountMinor"],
            """
            CREATE TABLE "TreasuryOpeningBalance" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TreasuryOpeningBalance" PRIMARY KEY AUTOINCREMENT,
                "BankAmountMinor" INTEGER NOT NULL,
                "CashAmountMinor" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "CreatedByUserId" INTEGER NOT NULL,
                "OpeningDate" TEXT NOT NULL,
                CONSTRAINT "FK_TreasuryOpeningBalance_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_TreasuryOpeningBalance_CreatedByUserId" ON "TreasuryOpeningBalance" ("CreatedByUserId");
            CREATE INDEX "IX_TreasuryOpeningBalance_OpeningDate" ON "TreasuryOpeningBalance" ("OpeningDate");
            """),
        new("TreasuryTransfers", "\"Id\", \"AmountMinor\", \"CreatedAtUtc\", \"CreatedByUserId\", \"FromAccount\", \"Observation\", \"Reference\", \"ToAccount\", \"TransferDate\"", ["AmountMinor"],
            """
            CREATE TABLE "TreasuryTransfers" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TreasuryTransfers" PRIMARY KEY AUTOINCREMENT,
                "AmountMinor" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "CreatedByUserId" INTEGER NOT NULL,
                "FromAccount" TEXT NOT NULL,
                "Observation" TEXT NULL,
                "Reference" TEXT NOT NULL,
                "ToAccount" TEXT NOT NULL,
                "TransferDate" TEXT NOT NULL,
                CONSTRAINT "CK_TreasuryTransfers_AmountMinor_Positive" CHECK ("AmountMinor" > 0),
                CONSTRAINT "CK_TreasuryTransfers_DistinctAccounts" CHECK ("FromAccount" <> "ToAccount"),
                CONSTRAINT "FK_TreasuryTransfers_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_TreasuryTransfers_CreatedByUserId" ON "TreasuryTransfers" ("CreatedByUserId");
            CREATE UNIQUE INDEX "IX_TreasuryTransfers_Reference" ON "TreasuryTransfers" ("Reference");
            CREATE INDEX "IX_TreasuryTransfers_TransferDate" ON "TreasuryTransfers" ("TransferDate");
            """),
        new("CustomerPaymentAllocations", "\"Id\", \"AmountMinor\", \"CreatedAtUtc\", \"CustomerPaymentId\", \"InitialCustomerReceivableId\", \"SaleId\"", ["AmountMinor"],
            """
            CREATE TABLE "CustomerPaymentAllocations" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomerPaymentAllocations" PRIMARY KEY AUTOINCREMENT,
                "AmountMinor" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "CustomerPaymentId" INTEGER NOT NULL,
                "InitialCustomerReceivableId" INTEGER NULL,
                "SaleId" INTEGER NULL,
                CONSTRAINT "CK_CustomerPaymentAllocations_AmountMinor_Positive" CHECK ("AmountMinor" > 0),
                CONSTRAINT "CK_CustomerPaymentAllocations_Target_Xor" CHECK (("SaleId" IS NOT NULL AND "InitialCustomerReceivableId" IS NULL) OR ("SaleId" IS NULL AND "InitialCustomerReceivableId" IS NOT NULL)),
                CONSTRAINT "FK_CustomerPaymentAllocations_CustomerPayments_CustomerPaymentId" FOREIGN KEY ("CustomerPaymentId") REFERENCES "CustomerPayments" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_CustomerPaymentAllocations_InitialCustomerReceivables_InitialCustomerReceivableId" FOREIGN KEY ("InitialCustomerReceivableId") REFERENCES "InitialCustomerReceivables" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_CustomerPaymentAllocations_Sales_SaleId" FOREIGN KEY ("SaleId") REFERENCES "Sales" ("Id") ON DELETE RESTRICT
            );
            """,
            """
            CREATE INDEX "IX_CustomerPaymentAllocations_CustomerPaymentId" ON "CustomerPaymentAllocations" ("CustomerPaymentId");
            CREATE INDEX "IX_CustomerPaymentAllocations_InitialCustomerReceivableId" ON "CustomerPaymentAllocations" ("InitialCustomerReceivableId");
            CREATE INDEX "IX_CustomerPaymentAllocations_SaleId" ON "CustomerPaymentAllocations" ("SaleId");
            """),
        new("SupplierPaymentAllocations", "\"Id\", \"AmountMinor\", \"CreatedAtUtc\", \"InitialSupplierPayableId\", \"PurchaseId\", \"SupplierPaymentId\"", ["AmountMinor"],
            """
            CREATE TABLE "SupplierPaymentAllocations" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_SupplierPaymentAllocations" PRIMARY KEY AUTOINCREMENT,
                "AmountMinor" INTEGER NOT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "InitialSupplierPayableId" INTEGER NULL,
                "PurchaseId" INTEGER NULL,
                "SupplierPaymentId" INTEGER NOT NULL,
                CONSTRAINT "CK_SupplierPaymentAllocations_AmountMinor_Positive" CHECK ("AmountMinor" > 0),
                CONSTRAINT "CK_SupplierPaymentAllocations_Target_Xor" CHECK (("PurchaseId" IS NOT NULL AND "InitialSupplierPayableId" IS NULL) OR ("PurchaseId" IS NULL AND "InitialSupplierPayableId" IS NOT NULL)),
                CONSTRAINT "FK_SupplierPaymentAllocations_InitialSupplierPayables_InitialSupplierPayableId" FOREIGN KEY ("InitialSupplierPayableId") REFERENCES "InitialSupplierPayables" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SupplierPaymentAllocations_Purchases_PurchaseId" FOREIGN KEY ("PurchaseId") REFERENCES "Purchases" ("Id") ON DELETE RESTRICT,
                CONSTRAINT "FK_SupplierPaymentAllocations_SupplierPayments_SupplierPaymentId" FOREIGN KEY ("SupplierPaymentId") REFERENCES "SupplierPayments" ("Id") ON DELETE CASCADE
            );
            """,
            """
            CREATE INDEX "IX_SupplierPaymentAllocations_InitialSupplierPayableId" ON "SupplierPaymentAllocations" ("InitialSupplierPayableId");
            CREATE INDEX "IX_SupplierPaymentAllocations_PurchaseId" ON "SupplierPaymentAllocations" ("PurchaseId");
            CREATE INDEX "IX_SupplierPaymentAllocations_SupplierPaymentId" ON "SupplierPaymentAllocations" ("SupplierPaymentId");
            """),
    ];
}
