using FinancialManagement.Application.Administration;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Backup;
using FinancialManagement.Application.Clients;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using FinancialManagement.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class SessionRevocationTests
{
    [Fact]
    public async Task A_Deactivation_in_another_instance_refuses_old_employee_read()
    {
        await using var fixture = await RevocationFixture.CreateAsync();

        Assert.True((await fixture.Admin<IUserAdministrationService>()
            .SetActiveAsync(fixture.EmployeeId, false)).Success);

        var result = await fixture.Employee<IClientService>().GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(ClientListFailureReason.Forbidden, result.FailureReason);
        Assert.False(fixture.Employee<ICurrentUserSession>().IsAuthenticated);
    }

    [Fact]
    public async Task B_K_Deactivation_refuses_old_employee_payment_and_persists_no_mutation()
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        var paymentsBefore = await fixture.Context.CustomerPayments.CountAsync();
        var allocationsBefore = await fixture.Context.CustomerPaymentAllocations.CountAsync();
        Assert.True((await fixture.Admin<IUserAdministrationService>()
            .SetActiveAsync(fixture.EmployeeId, false)).Success);

        var result = await fixture.Employee<ICustomerPaymentService>().CreateAsync(
            fixture.CustomerPaymentCommand());

        Assert.False(result.Success);
        Assert.Equal(CreateCustomerPaymentFailureReason.Forbidden, result.FailureReason);
        Assert.Equal(paymentsBefore, await fixture.Context.CustomerPayments.CountAsync());
        Assert.Equal(allocationsBefore, await fixture.Context.CustomerPaymentAllocations.CountAsync());
    }

    [Fact]
    public async Task K_Revocation_committed_while_payment_waits_for_writer_is_rechecked_before_mutation()
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        var employeeContext = fixture.Employee<FinancialManagementDbContext>();
        await employeeContext.Database.OpenConnectionAsync();
        await using var revoker = new SqliteConnection(
            $"Data Source={fixture.DatabasePath};Pooling=False");
        await revoker.OpenAsync();
        await using var revocation = revoker.BeginTransaction(deferred: false);
        await using (var command = revoker.CreateCommand())
        {
            command.Transaction = revocation;
            command.CommandText = "UPDATE Users SET IsActive = 0 WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", fixture.EmployeeId);
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        var pending = Task.Run(() => fixture.Employee<ICustomerPaymentService>().CreateAsync(
            fixture.CustomerPaymentCommand()));
        await Task.Delay(150);
        Assert.False(pending.IsCompleted);

        await revocation.CommitAsync();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.Success);
        Assert.Equal(CreateCustomerPaymentFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(await fixture.Context.CustomerPayments.AsNoTracking().ToArrayAsync());
        Assert.Empty(await fixture.Context.CustomerPaymentAllocations.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData("clients")]
    [InlineData("suppliers")]
    [InlineData("customer-payments")]
    [InlineData("supplier-payments")]
    public async Task C_Password_reset_in_another_instance_refuses_representative_employee_reads(
        string boundary)
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        Assert.True((await fixture.Admin<IUserAdministrationService>()
            .ResetPasswordAsync(fixture.EmployeeId, RevocationFixture.ReplacementPassword)).Success);

        var failure = boundary switch
        {
            "clients" => (await fixture.Employee<IClientService>().GetPageAsync(new())).FailureReason.ToString(),
            "suppliers" => (await fixture.Employee<ISupplierService>().GetPageAsync(new())).FailureReason.ToString(),
            "customer-payments" => (await fixture.Employee<ICustomerPaymentService>()
                .GetPageAsync(new())).FailureReason.ToString(),
            "supplier-payments" => (await fixture.Employee<ISupplierPaymentService>()
                .GetPageAsync(new())).FailureReason.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };

        Assert.Equal("Forbidden", failure);
    }

    [Fact]
    public async Task D_Password_reset_refuses_old_employee_supplier_payment_write()
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        var paymentsBefore = await fixture.Context.SupplierPayments.CountAsync();
        var allocationsBefore = await fixture.Context.SupplierPaymentAllocations.CountAsync();
        Assert.True((await fixture.Admin<IUserAdministrationService>()
            .ResetPasswordAsync(fixture.EmployeeId, RevocationFixture.ReplacementPassword)).Success);

        var result = await fixture.Employee<ISupplierPaymentService>().CreateAsync(
            fixture.SupplierPaymentCommand());

        Assert.False(result.Success);
        Assert.Equal(CreateSupplierPaymentFailureReason.Forbidden, result.FailureReason);
        Assert.Equal(paymentsBefore, await fixture.Context.SupplierPayments.CountAsync());
        Assert.Equal(allocationsBefore, await fixture.Context.SupplierPaymentAllocations.CountAsync());
    }

    [Fact]
    public async Task E_F_Reset_rejects_old_password_and_accepts_new_password()
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        Assert.True((await fixture.Admin<IUserAdministrationService>()
            .ResetPasswordAsync(fixture.EmployeeId, RevocationFixture.ReplacementPassword)).Success);
        var authentication = fixture.Employee<IAuthenticationService>();

        Assert.False((await authentication.AuthenticateAsync(
            RevocationFixture.EmployeeUsername,
            RevocationFixture.InitialPassword)).Success);
        Assert.True((await authentication.AuthenticateAsync(
            RevocationFixture.EmployeeUsername,
            RevocationFixture.ReplacementPassword)).Success);
    }

    [Fact]
    public async Task G_Persisted_role_mismatch_refuses_stale_responsible_capability()
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        await fixture.Context.Users
            .Where(user => user.Id == fixture.ResponsibleId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(user => user.Role, UserRole.Employe)
                .SetProperty(user => user.UpdatedAtUtc, RevocationFixture.ChangedUtc));

        var allowed = await fixture.StaleResponsible<IAuthorizationService>()
            .CanAccessResponsibleCapabilityAsync(ResponsibleCapability.Treasury);

        Assert.False(allowed);
        Assert.False(fixture.StaleResponsible<ICurrentUserSession>().IsAuthenticated);
    }

    [Fact]
    public async Task H_Deleted_user_is_refused_and_old_session_is_cleared()
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        await fixture.Context.UserPermissions
            .Where(permission => permission.UserId == fixture.EmployeeId)
            .ExecuteDeleteAsync();
        await fixture.Context.Users
            .Where(user => user.Id == fixture.EmployeeId)
            .ExecuteDeleteAsync();

        var result = await fixture.Employee<IClientService>().GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(ClientListFailureReason.Forbidden, result.FailureReason);
        Assert.False(fixture.Employee<ICurrentUserSession>().IsAuthenticated);
    }

    [Fact]
    public async Task I_Password_reset_refuses_old_responsible_treasury_write()
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        Assert.True((await fixture.Admin<IUserAdministrationService>()
            .ResetPasswordAsync(fixture.ResponsibleId, RevocationFixture.ReplacementPassword)).Success);
        var transfersBefore = await fixture.Context.TreasuryTransfers.CountAsync();

        var result = await fixture.StaleResponsible<ITreasuryService>().CreateTransferAsync(
            new(TreasuryAccount.Cash, TreasuryAccount.Bank, 100, null));

        Assert.False(result.Success);
        Assert.Equal(CreateTreasuryTransferFailureReason.Forbidden, result.FailureReason);
        Assert.Equal(transfersBefore, await fixture.Context.TreasuryTransfers.CountAsync());
    }

    [Fact]
    public async Task J_Password_reset_refuses_old_responsible_backup_and_restore_boundaries()
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        Assert.True((await fixture.Admin<IUserAdministrationService>()
            .ResetPasswordAsync(fixture.ResponsibleId, RevocationFixture.ReplacementPassword)).Success);
        var service = fixture.StaleResponsible<IDatabaseBackupService>();

        var backup = await service.CreateLocalBackupAsync();
        var restore = await service.RestoreAsync(new(Path.Combine(fixture.Root, "missing.fmbak")));

        Assert.False(backup.Success);
        Assert.Equal(DatabaseBackupFailureReason.Forbidden, backup.FailureReason);
        Assert.False(restore.Success);
        Assert.Equal(DatabaseBackupFailureReason.NotAuthenticated, restore.FailureReason);
        Assert.False(Directory.Exists(fixture.BackupDirectory));
    }

    [Theory]
    [InlineData("treasury")]
    [InlineData("dashboard")]
    [InlineData("reports")]
    [InlineData("administration")]
    public async Task Revoked_responsible_is_refused_at_representative_read_boundaries(string boundary)
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        Assert.True((await fixture.Admin<IUserAdministrationService>()
            .ResetPasswordAsync(fixture.ResponsibleId, RevocationFixture.ReplacementPassword)).Success);

        var failure = boundary switch
        {
            "treasury" => (await fixture.StaleResponsible<ITreasuryService>()
                .GetSummaryAsync()).FailureReason.ToString(),
            "dashboard" => (await fixture.StaleResponsible<IFinancialDashboardService>()
                .GetAsync(new(null, null, IncludeCurrentPositions: false))).FailureReason.ToString(),
            "reports" => (await fixture.StaleResponsible<IFinancialReportsService>()
                .GetAsync(new(null, null, IncludeCurrentPositions: false))).FailureReason.ToString(),
            "administration" => (await fixture.StaleResponsible<IUserAdministrationService>()
                .GetUsersAsync()).FailureReason.ToString(),
            _ => throw new ArgumentOutOfRangeException(nameof(boundary))
        };

        Assert.Equal("Forbidden", failure);
    }

    [Fact]
    public async Task L_Reactivation_does_not_revive_old_session_but_fresh_authentication_works()
    {
        await using var fixture = await RevocationFixture.CreateAsync();
        var administration = fixture.Admin<IUserAdministrationService>();
        Assert.True((await administration.SetActiveAsync(fixture.EmployeeId, false)).Success);
        Assert.True((await administration.SetActiveAsync(fixture.EmployeeId, true)).Success);

        var oldSessionRead = await fixture.Employee<IClientService>().GetPageAsync(new());
        Assert.False(oldSessionRead.Success);
        Assert.Equal(ClientListFailureReason.Forbidden, oldSessionRead.FailureReason);

        Assert.True((await fixture.Employee<IAuthenticationService>().AuthenticateAsync(
            RevocationFixture.EmployeeUsername,
            RevocationFixture.InitialPassword)).Success);
        var freshSessionRead = await fixture.Employee<IClientService>().GetPageAsync(new());
        Assert.True(freshSessionRead.Success);
    }

    private sealed class RevocationFixture : IAsyncDisposable
    {
        internal const string InitialPassword = "SEC-001 initial password 42!";
        internal const string ReplacementPassword = "SEC-001 replacement password 84!";
        internal const string EmployeeUsername = "sec001-employee";
        internal static readonly DateTimeOffset ChangedUtc =
            new(2026, 9, 12, 13, 0, 0, TimeSpan.Zero);
        private static readonly DateTimeOffset CreatedUtc =
            new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);
        private readonly ServiceProvider _adminProvider;
        private readonly ServiceProvider _employeeProvider;
        private readonly ServiceProvider _staleResponsibleProvider;
        private readonly AsyncServiceScope _adminScope;
        private readonly AsyncServiceScope _employeeScope;
        private readonly AsyncServiceScope _staleResponsibleScope;

        private RevocationFixture(
            string root,
            ServiceProvider adminProvider,
            ServiceProvider employeeProvider,
            ServiceProvider staleResponsibleProvider)
        {
            Root = root;
            DatabasePath = Path.Combine(root, "sec001.db");
            BackupDirectory = Path.Combine(root, "backups");
            _adminProvider = adminProvider;
            _employeeProvider = employeeProvider;
            _staleResponsibleProvider = staleResponsibleProvider;
            _adminScope = adminProvider.CreateAsyncScope();
            _employeeScope = employeeProvider.CreateAsyncScope();
            _staleResponsibleScope = staleResponsibleProvider.CreateAsyncScope();
            Context = Admin<FinancialManagementDbContext>();
        }

        public string Root { get; }
        public string DatabasePath { get; }
        public string BackupDirectory { get; }
        public FinancialManagementDbContext Context { get; }
        public long ResponsibleId { get; private set; }
        public long EmployeeId { get; private set; }
        public long ClientId { get; private set; }
        public long SupplierId { get; private set; }
        public long SaleId { get; private set; }
        public long PurchaseId { get; private set; }

        public T Admin<T>() where T : notnull =>
            _adminScope.ServiceProvider.GetRequiredService<T>();

        public T Employee<T>() where T : notnull =>
            _employeeScope.ServiceProvider.GetRequiredService<T>();

        public T StaleResponsible<T>() where T : notnull =>
            _staleResponsibleScope.ServiceProvider.GetRequiredService<T>();

        public static async Task<RevocationFixture> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), $"FinancialManagement-SEC001-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var databasePath = Path.Combine(root, "sec001.db");
            var backupDirectory = Path.Combine(root, "backups");
            var adminProvider = CreateProvider(databasePath, backupDirectory);
            var employeeProvider = CreateProvider(databasePath, backupDirectory);
            var staleResponsibleProvider = CreateProvider(databasePath, backupDirectory);
            var fixture = new RevocationFixture(
                root,
                adminProvider,
                employeeProvider,
                staleResponsibleProvider);

            await fixture.Context.Database.MigrateAsync();
            var hasher = fixture.Admin<IPasswordHasher>();
            var responsible = new User
            {
                FullName = "SEC-001 Responsible",
                Username = "sec001-responsible",
                PasswordHash = hasher.HashPassword(InitialPassword),
                Role = UserRole.Responsable,
                IsActive = true,
                CreatedAtUtc = CreatedUtc,
                UpdatedAtUtc = CreatedUtc
            };
            fixture.Context.Users.Add(responsible);
            await fixture.Context.SaveChangesAsync();
            fixture.ResponsibleId = responsible.Id;

            Assert.True((await fixture.Admin<IAuthenticationService>().AuthenticateAsync(
                responsible.Username,
                InitialPassword)).Success);
            var employee = await fixture.Admin<IUserAdministrationService>().CreateEmployeeAsync(
                new("SEC-001 Employee", EmployeeUsername, InitialPassword));
            Assert.True(employee.Success);
            fixture.EmployeeId = employee.UserId!.Value;
            foreach (var module in Enum.GetValues<PermissionModule>())
            {
                Assert.True((await fixture.Admin<IUserAdministrationService>().SetPermissionAsync(
                    fixture.EmployeeId,
                    new(module, true, true, true))).Success);
            }

            var client = new Client
            {
                Name = "SEC-001 Client",
                CreatedAtUtc = CreatedUtc,
                UpdatedAtUtc = CreatedUtc
            };
            var supplier = new Supplier
            {
                Name = "SEC-001 Supplier",
                CreatedAtUtc = CreatedUtc,
                UpdatedAtUtc = CreatedUtc
            };
            fixture.Context.AddRange(client, supplier);
            await fixture.Context.SaveChangesAsync();
            fixture.ClientId = client.Id;
            fixture.SupplierId = supplier.Id;

            var sale = new Sale
            {
                Reference = "SEC001-SALE",
                SaleDate = DateOnly.FromDateTime(CreatedUtc.UtcDateTime),
                DueDate = DateOnly.FromDateTime(CreatedUtc.UtcDateTime),
                TotalAmountMinor = 10_000,
                ClientId = client.Id,
                CreatedByUserId = responsible.Id,
                CreatedAtUtc = CreatedUtc,
                UpdatedAtUtc = CreatedUtc
            };
            var purchase = new Purchase
            {
                Reference = "SEC001-PURCHASE",
                PurchaseDate = DateOnly.FromDateTime(CreatedUtc.UtcDateTime),
                DueDate = DateOnly.FromDateTime(CreatedUtc.UtcDateTime),
                TotalAmountMinor = 10_000,
                SupplierId = supplier.Id,
                CreatedByUserId = responsible.Id,
                CreatedAtUtc = CreatedUtc,
                UpdatedAtUtc = CreatedUtc
            };
            fixture.Context.AddRange(sale, purchase);
            await fixture.Context.SaveChangesAsync();
            fixture.SaleId = sale.Id;
            fixture.PurchaseId = purchase.Id;

            Assert.True((await fixture.Employee<IAuthenticationService>().AuthenticateAsync(
                EmployeeUsername,
                InitialPassword)).Success);
            Assert.True((await fixture.StaleResponsible<IAuthenticationService>().AuthenticateAsync(
                responsible.Username,
                InitialPassword)).Success);
            return fixture;
        }

        public CreateCustomerPaymentCommand CustomerPaymentCommand() =>
            new(ClientId, 100, PaymentMethod.Cash, null,
                [new CustomerPaymentAllocationInput(SaleId, null, 100)]);

        public CreateSupplierPaymentCommand SupplierPaymentCommand() =>
            new(SupplierId, 100, PaymentMethod.Cash, null,
                [new SupplierPaymentAllocationInput(PurchaseId, null, 100)]);

        public async ValueTask DisposeAsync()
        {
            await _staleResponsibleScope.DisposeAsync();
            await _employeeScope.DisposeAsync();
            await _adminScope.DisposeAsync();
            await _staleResponsibleProvider.DisposeAsync();
            await _employeeProvider.DisposeAsync();
            await _adminProvider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static ServiceProvider CreateProvider(string databasePath, string backupDirectory)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(CreatedUtc));
            services.AddSingleton(new DatabaseBackupOptions(backupDirectory));
            services.AddInfrastructure($"Data Source={databasePath};Pooling=False");
            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
        }

        private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }
    }
}
