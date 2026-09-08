using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace FinancialManagement.Tests;

internal sealed class FinancialStorageTestDatabase : IAsyncDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Astra005-{Guid.NewGuid():N}.db");
    public FinancialManagementDbContext Context { get; }
    public string[] Migrations => Context.Database.GetMigrations().ToArray();

    public FinancialStorageTestDatabase(params IInterceptor[] interceptors)
    {
        Context = new(new DbContextOptionsBuilder<FinancialManagementDbContext>()
            .UseSqlite($"Data Source={Path};Foreign Keys=True;Pooling=False")
            .AddInterceptors(interceptors).Options);
    }

    public async Task LegacyAsync()
    {
        await Context.GetService<IMigrator>().MigrateAsync(Migrations[2]);
        await SeedAsync(Context);
        Context.ChangeTracker.Clear();
    }

    internal static async Task SeedAsync(FinancialManagementDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var date = new DateOnly(2026, 1, 1);
        if (!await db.Users.AnyAsync())
        {
            db.Users.Add(new User { Id = 1, FullName = "Storage owner", Username = "storage", PasswordHash = "unused",
                Role = UserRole.Responsable, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now });
            await db.SaveChangesAsync();
        }
        var owner = await db.Users.Select(u => u.Id).FirstAsync();
        db.Clients.Add(new Client { Id = 1, Name = "Storage client", CreatedAtUtc = now, UpdatedAtUtc = now });
        db.Suppliers.Add(new Supplier { Id = 1, Name = "Storage supplier", CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync();
        db.AddRange(
            new Sale { Id = 1, Reference = "S-1", ClientId = 1, TotalAmountMinor = 1000, SaleDate = date, DueDate = date, CreatedByUserId = owner, CreatedAtUtc = now, UpdatedAtUtc = now },
            new Purchase { Id = 1, Reference = "P-1", SupplierId = 1, TotalAmountMinor = 1000, PurchaseDate = date, DueDate = date, CreatedByUserId = owner, CreatedAtUtc = now, UpdatedAtUtc = now },
            new Expense { Id = 1, Reference = "E-1", AmountMinor = 1000, ExpenseCategoryId = 1, PaymentMethod = PaymentMethod.Cash, ExpenseDate = date, CreatedByUserId = owner, CreatedAtUtc = now, UpdatedAtUtc = now },
            new CustomerPayment { Id = 1, Reference = "CP-1", ClientId = 1, AmountMinor = 1000, PaymentMethod = PaymentMethod.Cash, PaymentDate = date, CreatedByUserId = owner, CreatedAtUtc = now },
            new SupplierPayment { Id = 1, Reference = "SP-1", SupplierId = 1, AmountMinor = 1000, PaymentMethod = PaymentMethod.Cash, PaymentDate = date, CreatedByUserId = owner, CreatedAtUtc = now },
            new InitialCustomerReceivable { Id = 1, Reference = "IC-1", ClientId = 1, InitialAmountMinor = 1000, ReceivableDate = date, DueDate = date, CreatedByUserId = owner, CreatedAtUtc = now },
            new InitialSupplierPayable { Id = 1, Reference = "IS-1", SupplierId = 1, InitialAmountMinor = 1000, PayableDate = date, DueDate = date, CreatedByUserId = owner, CreatedAtUtc = now },
            new TreasuryOpeningBalance { Id = 1, CashAmountMinor = -1000, BankAmountMinor = 2000, OpeningDate = date, CreatedByUserId = owner, CreatedAtUtc = now },
            new TreasuryTransfer { Id = 1, Reference = "T-1", AmountMinor = 1000, FromAccount = TreasuryAccount.Cash, ToAccount = TreasuryAccount.Bank, TransferDate = date, CreatedByUserId = owner, CreatedAtUtc = now });
        await db.SaveChangesAsync();
        db.AddRange(
            new CustomerPaymentAllocation { Id = 1, CustomerPaymentId = 1, SaleId = 1, AmountMinor = 100, CreatedAtUtc = now },
            new CustomerPaymentAllocation { Id = 2, CustomerPaymentId = 1, InitialCustomerReceivableId = 1, AmountMinor = 100, CreatedAtUtc = now },
            new SupplierPaymentAllocation { Id = 1, SupplierPaymentId = 1, PurchaseId = 1, AmountMinor = 100, CreatedAtUtc = now },
            new SupplierPaymentAllocation { Id = 2, SupplierPaymentId = 1, InitialSupplierPayableId = 1, AmountMinor = 100, CreatedAtUtc = now });
        await db.SaveChangesAsync();
    }

    public async Task<string[]> QueryAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={Path};Foreign Keys=True;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "<null>" : Convert.ToString(reader.GetValue(i), System.Globalization.CultureInfo.InvariantCulture))));
        return rows.ToArray();
    }

    public async Task UpdateAsync(string table, string column, object value, bool bypassChecks = false)
    {
        await using var connection = new SqliteConnection($"Data Source={Path};Foreign Keys=True;Pooling=False");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        if (bypassChecks)
        {
            command.CommandText = "PRAGMA ignore_check_constraints=ON;";
            await command.ExecuteNonQueryAsync();
        }
        command.CommandText = $"UPDATE \"{table}\" SET \"{column}\"=$value WHERE Id=1;";
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<string[]> ValuesAsync()
    {
        var rows = new List<string>();
        // Every persisted field, not just money: catch cascade losses and collateral changes.
        foreach (var table in await QueryAsync("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name"))
        {
            var columns = await QueryAsync($"SELECT name FROM pragma_table_info('{table}') ORDER BY name");
            rows.AddRange((await QueryAsync($"SELECT {string.Join(",", columns.Select(c => $"quote(\"{c}\")"))} FROM \"{table}\" ORDER BY Id")).Select(r => table + "|" + r));
        }
        return rows.ToArray();
    }

    public async Task<string[]> MetadataAsync()
    {
        var rows = new List<string>();
        foreach (var table in await QueryAsync("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name"))
        {
            rows.AddRange((await QueryAsync($"SELECT name,type,\"notnull\",dflt_value,pk FROM pragma_table_info('{table}') ORDER BY name")).Select(r => "COL|" + table + "|" + r));
            rows.AddRange((await QueryAsync($"SELECT \"table\",\"from\",\"to\",on_update,on_delete FROM pragma_foreign_key_list('{table}') ORDER BY \"from\"")).Select(r => "FK|" + table + "|" + r));
            foreach (var index in await QueryAsync($"SELECT name FROM pragma_index_list('{table}') ORDER BY name"))
            {
                rows.AddRange((await QueryAsync($"SELECT name,\"unique\",origin,partial FROM pragma_index_list('{table}') WHERE name='{index}'")).Select(r => "INDEX|" + r));
                rows.AddRange((await QueryAsync($"SELECT name,desc,coll,key FROM pragma_index_xinfo('{index}') ORDER BY seqno")).Select(r => "KEY|" + index + "|" + r));
            }
        }
        return rows.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm", ".fmbak" })
            File.Delete(Path + suffix);
    }
}
