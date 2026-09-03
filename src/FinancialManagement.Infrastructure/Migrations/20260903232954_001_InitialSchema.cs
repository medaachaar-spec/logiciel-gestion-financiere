using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FinancialManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _001_InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApplicationSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CompanyName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    Company = table.Column<string>(type: "TEXT", nullable: true),
                    Identifier = table.Column<string>(type: "TEXT", nullable: true),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Phone = table.Column<string>(type: "TEXT", nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    Company = table.Column<string>(type: "TEXT", nullable: true),
                    Identifier = table.Column<string>(type: "TEXT", nullable: true),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPayments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    ClientId = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: false),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPayments", x => x.Id);
                    table.CheckConstraint("CK_CustomerPayments_AmountMinor_Positive", "\"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_CustomerPayments_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPayments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    ExpenseDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ExpenseCategoryId = table.Column<long>(type: "INTEGER", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: false),
                    ConcernedEmployeeUserId = table.Column<long>(type: "INTEGER", nullable: true),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                    table.CheckConstraint("CK_Expenses_AmountMinor_Positive", "\"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_Expenses_ExpenseCategories_ExpenseCategoryId",
                        column: x => x.ExpenseCategoryId,
                        principalTable: "ExpenseCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Users_ConcernedEmployeeUserId",
                        column: x => x.ConcernedEmployeeUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Expenses_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InitialCustomerReceivables",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    ClientId = table.Column<long>(type: "INTEGER", nullable: false),
                    ReceivableDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    InitialAmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitialCustomerReceivables", x => x.Id);
                    table.CheckConstraint("CK_InitialCustomerReceivables_InitialAmountMinor_Positive", "\"InitialAmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_InitialCustomerReceivables_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitialCustomerReceivables_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InitialSupplierPayables",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<long>(type: "INTEGER", nullable: false),
                    PayableDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    InitialAmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitialSupplierPayables", x => x.Id);
                    table.CheckConstraint("CK_InitialSupplierPayables_InitialAmountMinor_Positive", "\"InitialAmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_InitialSupplierPayables_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitialSupplierPayables_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Purchases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    PurchaseDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TotalAmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<long>(type: "INTEGER", nullable: false),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Purchases", x => x.Id);
                    table.CheckConstraint("CK_Purchases_TotalAmountMinor_Positive", "\"TotalAmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_Purchases_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Purchases_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    SaleDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TotalAmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    ClientId = table.Column<long>(type: "INTEGER", nullable: true),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.CheckConstraint("CK_Sales_TotalAmountMinor_Positive", "\"TotalAmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_Sales_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPayments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    SupplierId = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", nullable: false),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                    table.CheckConstraint("CK_SupplierPayments_AmountMinor_Positive", "\"AmountMinor\" > 0");
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreasuryOpeningBalance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OpeningDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CashAmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    BankAmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasuryOpeningBalance", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreasuryOpeningBalance_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreasuryTransfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", nullable: false),
                    TransferDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    FromAccount = table.Column<string>(type: "TEXT", nullable: false),
                    ToAccount = table.Column<string>(type: "TEXT", nullable: false),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    Observation = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreasuryTransfers", x => x.Id);
                    table.CheckConstraint("CK_TreasuryTransfers_AmountMinor_Positive", "\"AmountMinor\" > 0");
                    table.CheckConstraint("CK_TreasuryTransfers_DistinctAccounts", "\"FromAccount\" <> \"ToAccount\"");
                    table.ForeignKey(
                        name: "FK_TreasuryTransfers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<long>(type: "INTEGER", nullable: false),
                    Module = table.Column<string>(type: "TEXT", nullable: false),
                    CanView = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanCreate = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanModify = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPermissions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerPaymentId = table.Column<long>(type: "INTEGER", nullable: false),
                    SaleId = table.Column<long>(type: "INTEGER", nullable: true),
                    InitialCustomerReceivableId = table.Column<long>(type: "INTEGER", nullable: true),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerPaymentAllocations", x => x.Id);
                    table.CheckConstraint("CK_CustomerPaymentAllocations_AmountMinor_Positive", "\"AmountMinor\" > 0");
                    table.CheckConstraint("CK_CustomerPaymentAllocations_Target_Xor", "(\"SaleId\" IS NOT NULL AND \"InitialCustomerReceivableId\" IS NULL) OR (\"SaleId\" IS NULL AND \"InitialCustomerReceivableId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CustomerPaymentAllocations_CustomerPayments_CustomerPaymentId",
                        column: x => x.CustomerPaymentId,
                        principalTable: "CustomerPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CustomerPaymentAllocations_InitialCustomerReceivables_InitialCustomerReceivableId",
                        column: x => x.InitialCustomerReceivableId,
                        principalTable: "InitialCustomerReceivables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerPaymentAllocations_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SupplierPaymentId = table.Column<long>(type: "INTEGER", nullable: false),
                    PurchaseId = table.Column<long>(type: "INTEGER", nullable: true),
                    InitialSupplierPayableId = table.Column<long>(type: "INTEGER", nullable: true),
                    AmountMinor = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPaymentAllocations", x => x.Id);
                    table.CheckConstraint("CK_SupplierPaymentAllocations_AmountMinor_Positive", "\"AmountMinor\" > 0");
                    table.CheckConstraint("CK_SupplierPaymentAllocations_Target_Xor", "(\"PurchaseId\" IS NOT NULL AND \"InitialSupplierPayableId\" IS NULL) OR (\"PurchaseId\" IS NULL AND \"InitialSupplierPayableId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_InitialSupplierPayables_InitialSupplierPayableId",
                        column: x => x.InitialSupplierPayableId,
                        principalTable: "InitialSupplierPayables",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_Purchases_PurchaseId",
                        column: x => x.PurchaseId,
                        principalTable: "Purchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ExpenseCategories",
                columns: new[] { "Id", "CreatedAtUtc", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Salaires" },
                    { 2L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Transport" },
                    { 3L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Loyer" },
                    { 4L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Factures" },
                    { 5L, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "Divers" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentAllocations_CustomerPaymentId",
                table: "CustomerPaymentAllocations",
                column: "CustomerPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentAllocations_InitialCustomerReceivableId",
                table: "CustomerPaymentAllocations",
                column: "InitialCustomerReceivableId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPaymentAllocations_SaleId",
                table: "CustomerPaymentAllocations",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_ClientId",
                table: "CustomerPayments",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_CreatedByUserId",
                table: "CustomerPayments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_PaymentDate",
                table: "CustomerPayments",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerPayments_Reference",
                table: "CustomerPayments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Name",
                table: "ExpenseCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ConcernedEmployeeUserId",
                table: "Expenses",
                column: "ConcernedEmployeeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CreatedByUserId",
                table: "Expenses",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseCategoryId",
                table: "Expenses",
                column: "ExpenseCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_ExpenseDate",
                table: "Expenses",
                column: "ExpenseDate");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_Reference",
                table: "Expenses",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitialCustomerReceivables_ClientId",
                table: "InitialCustomerReceivables",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialCustomerReceivables_CreatedByUserId",
                table: "InitialCustomerReceivables",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialCustomerReceivables_DueDate",
                table: "InitialCustomerReceivables",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_InitialCustomerReceivables_Reference",
                table: "InitialCustomerReceivables",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitialSupplierPayables_CreatedByUserId",
                table: "InitialSupplierPayables",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InitialSupplierPayables_DueDate",
                table: "InitialSupplierPayables",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_InitialSupplierPayables_Reference",
                table: "InitialSupplierPayables",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitialSupplierPayables_SupplierId",
                table: "InitialSupplierPayables",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_CreatedByUserId",
                table: "Purchases",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_DueDate",
                table: "Purchases",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_PurchaseDate",
                table: "Purchases",
                column: "PurchaseDate");

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_Reference",
                table: "Purchases",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_SupplierId",
                table: "Purchases",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_ClientId",
                table: "Sales",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CreatedByUserId",
                table: "Sales",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_DueDate",
                table: "Sales",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_Reference",
                table: "Sales",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SaleDate",
                table: "Sales",
                column: "SaleDate");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_InitialSupplierPayableId",
                table: "SupplierPaymentAllocations",
                column: "InitialSupplierPayableId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_PurchaseId",
                table: "SupplierPaymentAllocations",
                column: "PurchaseId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_SupplierPaymentId",
                table: "SupplierPaymentAllocations",
                column: "SupplierPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_CreatedByUserId",
                table: "SupplierPayments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_PaymentDate",
                table: "SupplierPayments",
                column: "PaymentDate");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_Reference",
                table: "SupplierPayments",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierId",
                table: "SupplierPayments",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryOpeningBalance_CreatedByUserId",
                table: "TreasuryOpeningBalance",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryOpeningBalance_OpeningDate",
                table: "TreasuryOpeningBalance",
                column: "OpeningDate");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryTransfers_CreatedByUserId",
                table: "TreasuryTransfers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryTransfers_Reference",
                table: "TreasuryTransfers",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreasuryTransfers_TransferDate",
                table: "TreasuryTransfers",
                column: "TransferDate");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_UserId_Module",
                table: "UserPermissions",
                columns: new[] { "UserId", "Module" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationSettings");

            migrationBuilder.DropTable(
                name: "CustomerPaymentAllocations");

            migrationBuilder.DropTable(
                name: "Expenses");

            migrationBuilder.DropTable(
                name: "SupplierPaymentAllocations");

            migrationBuilder.DropTable(
                name: "TreasuryOpeningBalance");

            migrationBuilder.DropTable(
                name: "TreasuryTransfers");

            migrationBuilder.DropTable(
                name: "UserPermissions");

            migrationBuilder.DropTable(
                name: "CustomerPayments");

            migrationBuilder.DropTable(
                name: "InitialCustomerReceivables");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "ExpenseCategories");

            migrationBuilder.DropTable(
                name: "InitialSupplierPayables");

            migrationBuilder.DropTable(
                name: "Purchases");

            migrationBuilder.DropTable(
                name: "SupplierPayments");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
