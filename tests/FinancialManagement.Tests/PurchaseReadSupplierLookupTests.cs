using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Purchases;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class PurchaseReadServiceTests
{
    [Fact]
    public async Task ResponsableCanReadPurchasesWithExactSupplierValues()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Atlas Supplier", "SUP-ATLAS");
        database.AddPurchase(
            responsible,
            supplier,
            "ACH-KNOWN",
            new DateOnly(2026, 9, 1),
            9_000_000_000,
            new DateOnly(2026, 9, 30),
            "Known observation");
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.PurchaseService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Equal(PurchaseListFailureReason.None, result.FailureReason);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        var item = Assert.Single(result.Items);
        Assert.Equal(supplier.Id, item.SupplierId);
        Assert.Equal("Atlas Supplier", item.SupplierName);
        Assert.Equal("ACH-KNOWN", item.Reference);
        Assert.Equal(new DateOnly(2026, 9, 1), item.PurchaseDate);
        Assert.Equal(9_000_000_000, item.TotalAmountMinor);
        Assert.Equal(new DateOnly(2026, 9, 30), item.DueDate);
        Assert.Equal("Known observation", item.Observation);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task EmployeeWithPurchasesViewCanReadPurchasesCreatedByAnotherUser()
    {
        using var database = new PurchaseReadTestDatabase();
        var owner = database.AddUser("owner", UserRole.Responsable);
        var employee = database.AddUser("purchase-reader", UserRole.Employe);
        var supplier = database.AddSupplier("Known Supplier");
        database.Grant(employee, PermissionModule.Purchases, canView: true);
        database.AddPurchase(owner, supplier, "ACH-OWNER", new DateOnly(2026, 9, 1), 1_000);
        await database.AuthenticateAsync(employee);

        var result = await database.PurchaseService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Single(result.Items);
        Assert.Equal("ACH-OWNER", result.Items[0].Reference);
    }

    [Fact]
    public async Task EmployeeWithoutPurchasesViewIsForbiddenWithoutData()
    {
        using var database = new PurchaseReadTestDatabase();
        var owner = database.AddUser("owner", UserRole.Responsable);
        var employee = database.AddUser("purchase-denied", UserRole.Employe);
        var supplier = database.AddSupplier("Known Supplier");
        database.Grant(employee, PermissionModule.Purchases, canView: false);
        database.AddPurchase(owner, supplier, "ACH-SECRET", new DateOnly(2026, 9, 1), 1_000);
        await database.AuthenticateAsync(employee);

        var result = await database.PurchaseService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(PurchaseListFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task UnauthenticatedPurchaseReadIsRejectedWithoutData()
    {
        using var database = new PurchaseReadTestDatabase();
        var owner = database.AddUser("owner", UserRole.Responsable);
        var supplier = database.AddSupplier("Known Supplier");
        database.AddPurchase(owner, supplier, "ACH-SECRET", new DateOnly(2026, 9, 1), 1_000);

        var result = await database.PurchaseService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(PurchaseListFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task PaginationUsesStableDateAndIdDescendingOrderWithoutGaps()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Known Supplier");
        var first = database.AddPurchase(responsible, supplier, "ACH-1", new DateOnly(2026, 9, 2), 101);
        var second = database.AddPurchase(responsible, supplier, "ACH-2", new DateOnly(2026, 9, 2), 102);
        var third = database.AddPurchase(responsible, supplier, "ACH-3", new DateOnly(2026, 9, 3), 103);
        var fourth = database.AddPurchase(responsible, supplier, "ACH-4", new DateOnly(2026, 9, 1), 104);
        var fifth = database.AddPurchase(responsible, supplier, "ACH-5", new DateOnly(2026, 9, 1), 105);
        await database.AuthenticateAsync(responsible);

        var page1 = await database.PurchaseService.GetPageAsync(new(PageNumber: 1, PageSize: 2));
        var page2 = await database.PurchaseService.GetPageAsync(new(PageNumber: 2, PageSize: 2));
        var page3 = await database.PurchaseService.GetPageAsync(new(PageNumber: 3, PageSize: 2));

        Assert.Equal([third.Id, second.Id], page1.Items.Select(item => item.Id));
        Assert.Equal([first.Id, fifth.Id], page2.Items.Select(item => item.Id));
        Assert.Equal([fourth.Id], page3.Items.Select(item => item.Id));
        Assert.All([page1, page2, page3], page => Assert.Equal(5, page.TotalCount));
        Assert.All([page1, page2, page3], page => Assert.Equal(3, page.TotalPages));
        Assert.Equal(
            5,
            page1.Items.Concat(page2.Items).Concat(page3.Items).Select(item => item.Id).Distinct().Count());
    }

    [Fact]
    public async Task DateFiltersAreInclusiveIndividuallyAndTogether()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Known Supplier");
        database.AddPurchase(responsible, supplier, "ACH-D1", new DateOnly(2026, 9, 1), 100);
        database.AddPurchase(responsible, supplier, "ACH-D2", new DateOnly(2026, 9, 2), 200);
        database.AddPurchase(responsible, supplier, "ACH-D3", new DateOnly(2026, 9, 3), 300);
        await database.AuthenticateAsync(responsible);

        var from = await database.PurchaseService.GetPageAsync(new(FromDate: new DateOnly(2026, 9, 2)));
        var to = await database.PurchaseService.GetPageAsync(new(ToDate: new DateOnly(2026, 9, 2)));
        var exact = await database.PurchaseService.GetPageAsync(new(
            FromDate: new DateOnly(2026, 9, 2),
            ToDate: new DateOnly(2026, 9, 2)));

        Assert.Equal(["ACH-D3", "ACH-D2"], from.Items.Select(item => item.Reference));
        Assert.Equal(["ACH-D2", "ACH-D1"], to.Items.Select(item => item.Reference));
        Assert.Equal(["ACH-D2"], exact.Items.Select(item => item.Reference));
    }

    [Fact]
    public async Task InvalidDateRangeReturnsControlledFailure()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.PurchaseService.GetPageAsync(new(
            FromDate: new DateOnly(2026, 9, 2),
            ToDate: new DateOnly(2026, 9, 1)));

        Assert.False(result.Success);
        Assert.Equal(PurchaseListFailureReason.InvalidDateRange, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData(0, 50, PurchaseListFailureReason.InvalidPageNumber)]
    [InlineData(1, 0, PurchaseListFailureReason.InvalidPageSize)]
    [InlineData(1, 201, PurchaseListFailureReason.InvalidPageSize)]
    [InlineData(int.MaxValue, 200, PurchaseListFailureReason.InvalidPageNumber)]
    public async Task InvalidPaginationReturnsControlledFailure(
        int pageNumber,
        int pageSize,
        PurchaseListFailureReason expected)
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.PurchaseService.GetPageAsync(new(pageNumber, pageSize));

        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchUsesTrimmedReferenceOrSupplierNameOnly()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var atlas = database.AddSupplier("Atlas Supplier", "SUP-001");
        var cedar = database.AddSupplier("Cedar Supplier", "SUP-XYZ");
        database.AddPurchase(responsible, atlas, "ACH-ALPHA", new DateOnly(2026, 9, 1), 100);
        database.AddPurchase(
            responsible,
            cedar,
            "ACH-BETA",
            new DateOnly(2026, 9, 2),
            200,
            observation: "HiddenNeedle");
        await database.AuthenticateAsync(responsible);

        var byReference = await database.PurchaseService.GetPageAsync(new(SearchText: "  ALPHA  "));
        var bySupplier = await database.PurchaseService.GetPageAsync(new(SearchText: "Cedar"));
        var whitespace = await database.PurchaseService.GetPageAsync(new(SearchText: "   "));
        var byObservation = await database.PurchaseService.GetPageAsync(new(SearchText: "HiddenNeedle"));
        var byIdentifier = await database.PurchaseService.GetPageAsync(new(SearchText: "SUP-XYZ"));

        Assert.Equal(["ACH-ALPHA"], byReference.Items.Select(item => item.Reference));
        Assert.Equal(["ACH-BETA"], bySupplier.Items.Select(item => item.Reference));
        Assert.Equal(2, whitespace.TotalCount);
        Assert.Empty(byObservation.Items);
        Assert.Empty(byIdentifier.Items);
    }

    [Fact]
    public async Task PurchaseReadHonorsCancellationBeforeQuery()
    {
        using var database = new PurchaseReadTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.PurchaseService.GetPageAsync(new(), cancellation.Token));
    }

    [Fact]
    public async Task PurchaseReadDoesNotMutateOrTrackEntities()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Unchanged Supplier");
        var purchase = database.AddPurchase(
            responsible,
            supplier,
            "ACH-UNCHANGED",
            new DateOnly(2026, 9, 1),
            500,
            observation: "Original");
        var updatedAtBefore = purchase.UpdatedAtUtc;
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.PurchaseService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        var persisted = await database.Context.Purchases.AsNoTracking().SingleAsync();
        Assert.Equal("Original", persisted.Observation);
        Assert.Equal(updatedAtBefore, persisted.UpdatedAtUtc);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }
}

public sealed class SupplierLookupServiceTests
{
    [Fact]
    public async Task ResponsableCanLookupSuppliersWithDistinguishingIdentifier()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Atlas Supplier", "SUP-ATLAS");
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookupService.SearchAsync(new());

        Assert.True(result.Success);
        Assert.Equal(SupplierLookupFailureReason.None, result.FailureReason);
        var item = Assert.Single(result.Items);
        Assert.Equal(supplier.Id, item.SupplierId);
        Assert.Equal("Atlas Supplier", item.DisplayName);
        Assert.Equal("SUP-ATLAS", item.Identifier);
        Assert.Equal(20, result.Limit);
    }

    [Fact]
    public async Task EmployeeWithSuppliersViewCanUseLookup()
    {
        using var database = new PurchaseReadTestDatabase();
        var employee = database.AddUser("supplier-reader", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView: true);
        database.AddSupplier("Visible Supplier");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierLookupService.SearchAsync(new());

        Assert.True(result.Success);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task EmployeeWithoutSuppliersViewIsForbiddenWithoutData()
    {
        using var database = new PurchaseReadTestDatabase();
        var employee = database.AddUser("supplier-denied", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView: false);
        database.AddSupplier("Secret Supplier");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierLookupService.SearchAsync(new());

        Assert.False(result.Success);
        Assert.Equal(SupplierLookupFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task UnauthenticatedSupplierLookupIsRejectedWithoutData()
    {
        using var database = new PurchaseReadTestDatabase();
        database.AddSupplier("Secret Supplier");

        var result = await database.SupplierLookupService.SearchAsync(new());

        Assert.False(result.Success);
        Assert.Equal(SupplierLookupFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task LookupSearchUsesTrimmedNameOrIdentifierOnly()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        database.AddSupplier("Atlas Supplier", "SUP-001", company: "CompanyNeedle");
        database.AddSupplier("Cedar Supplier", "SUP-XYZ");
        await database.AuthenticateAsync(responsible);

        var byName = await database.SupplierLookupService.SearchAsync(new("  Atlas  "));
        var byIdentifier = await database.SupplierLookupService.SearchAsync(new("  XYZ  "));
        var byCompany = await database.SupplierLookupService.SearchAsync(new("CompanyNeedle"));

        Assert.Equal(["Atlas Supplier"], byName.Items.Select(item => item.DisplayName));
        Assert.Equal(["Cedar Supplier"], byIdentifier.Items.Select(item => item.DisplayName));
        Assert.Empty(byCompany.Items);
    }

    [Fact]
    public async Task EmptySearchReturnsStableNameAndIdOrder()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var zulu = database.AddSupplier("Zulu");
        var firstAtlas = database.AddSupplier("Atlas");
        var secondAtlas = database.AddSupplier("Atlas");
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookupService.SearchAsync(new("   "));

        Assert.Equal(
            [firstAtlas.Id, secondAtlas.Id, zulu.Id],
            result.Items.Select(item => item.SupplierId));
    }

    [Fact]
    public async Task LookupRespectsRequestedLimitAndMaximum()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        database.AddSuppliers(55);
        await database.AuthenticateAsync(responsible);

        var limited = await database.SupplierLookupService.SearchAsync(new(Limit: 3));
        var maximum = await database.SupplierLookupService.SearchAsync(new(Limit: 50));

        Assert.Equal(3, limited.Items.Count);
        Assert.Equal(50, maximum.Items.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(51)]
    public async Task InvalidLookupLimitReturnsControlledFailure(int limit)
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookupService.SearchAsync(new(Limit: limit));

        Assert.False(result.Success);
        Assert.Equal(SupplierLookupFailureReason.InvalidLimit, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void LookupDtoExposesOnlySelectionFields()
    {
        Assert.Equal(
            [nameof(SupplierLookupItem.SupplierId), nameof(SupplierLookupItem.DisplayName), nameof(SupplierLookupItem.Identifier)],
            typeof(SupplierLookupItem).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task SupplierLookupHonorsCancellationBeforeQuery()
    {
        using var database = new PurchaseReadTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierLookupService.SearchAsync(new(), cancellation.Token));
    }

    [Fact]
    public async Task SupplierLookupDoesNotMutateOrTrackEntities()
    {
        using var database = new PurchaseReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Unchanged Supplier", "SUP-UNCHANGED");
        var updatedAtBefore = supplier.UpdatedAtUtc;
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.SupplierLookupService.SearchAsync(new());

        Assert.True(result.Success);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        var persisted = await database.Context.Suppliers.AsNoTracking().SingleAsync();
        Assert.Equal("Unchanged Supplier", persisted.Name);
        Assert.Equal(updatedAtBefore, persisted.UpdatedAtUtc);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task PurchasesAndSuppliersViewPermissionsRemainIndependent()
    {
        using var database = new PurchaseReadTestDatabase();
        var owner = database.AddUser("owner", UserRole.Responsable);
        var purchaseReader = database.AddUser("purchase-reader", UserRole.Employe);
        var supplierReader = database.AddUser("supplier-reader", UserRole.Employe);
        var supplier = database.AddSupplier("Lookup Supplier");
        database.Grant(purchaseReader, PermissionModule.Purchases, canView: true);
        database.Grant(supplierReader, PermissionModule.Suppliers, canView: true);
        database.AddPurchase(owner, supplier, "ACH-PERMISSIONS", new DateOnly(2026, 9, 1), 1_000);

        await database.AuthenticateAsync(purchaseReader);
        var purchasesAllowed = await database.PurchaseService.GetPageAsync(new());
        var suppliersDenied = await database.SupplierLookupService.SearchAsync(new());

        Assert.True(purchasesAllowed.Success);
        Assert.Equal(SupplierLookupFailureReason.Forbidden, suppliersDenied.FailureReason);
        Assert.Empty(suppliersDenied.Items);

        await database.AuthenticateAsync(supplierReader);
        var suppliersAllowed = await database.SupplierLookupService.SearchAsync(new());
        var purchasesDenied = await database.PurchaseService.GetPageAsync(new());

        Assert.True(suppliersAllowed.Success);
        Assert.Equal(PurchaseListFailureReason.Forbidden, purchasesDenied.FailureReason);
        Assert.Empty(purchasesDenied.Items);
    }
}

internal sealed class PurchaseReadTestDatabase : IDisposable
{
    private const string Password = "Purchase read test password 42!";
    private static readonly DateTimeOffset TestUtc =
        new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    private readonly string databasePath;
    private readonly ServiceProvider serviceProvider;
    private readonly IServiceScope scope;
    private readonly IPasswordHasher passwordHasher;
    private readonly IAuthenticationService authenticationService;

    public PurchaseReadTestDatabase()
    {
        databasePath = Path.Combine(
            Path.GetTempPath(),
            $"FinancialManagement-PurchaseRead-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddInfrastructure($"Data Source={databasePath};Pooling=False");
        serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        scope = serviceProvider.CreateScope();
        Context = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        Context.Database.Migrate();
        passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        authenticationService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        PurchaseService = scope.ServiceProvider.GetRequiredService<IPurchaseService>();
        SupplierLookupService = scope.ServiceProvider.GetRequiredService<ISupplierLookupService>();
    }

    public FinancialManagementDbContext Context { get; }

    public IPurchaseService PurchaseService { get; }

    public ISupplierLookupService SupplierLookupService { get; }

    public User AddUser(string username, UserRole role)
    {
        var user = new User
        {
            FullName = $"Purchase Read Test {username}",
            Username = username,
            PasswordHash = passwordHasher.HashPassword(Password),
            Role = role,
            IsActive = true,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Users.Add(user);
        Context.SaveChanges();
        return user;
    }

    public Supplier AddSupplier(
        string name,
        string? identifier = null,
        string? company = null)
    {
        var supplier = new Supplier
        {
            Name = name,
            Identifier = identifier,
            Company = company,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Suppliers.Add(supplier);
        Context.SaveChanges();
        return supplier;
    }

    public void AddSuppliers(int count)
    {
        Context.Suppliers.AddRange(Enumerable.Range(1, count).Select(index => new Supplier
        {
            Name = $"Supplier {index:000}",
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        }));
        Context.SaveChanges();
    }

    public Purchase AddPurchase(
        User creator,
        Supplier supplier,
        string reference,
        DateOnly purchaseDate,
        long amountMinor,
        DateOnly? dueDate = null,
        string? observation = null)
    {
        var purchase = new Purchase
        {
            Reference = reference,
            PurchaseDate = purchaseDate,
            TotalAmountMinor = amountMinor,
            SupplierId = supplier.Id,
            DueDate = dueDate,
            Observation = observation,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Purchases.Add(purchase);
        Context.SaveChanges();
        return purchase;
    }

    public void Grant(User employee, PermissionModule module, bool canView)
    {
        Context.UserPermissions.Add(new UserPermission
        {
            UserId = employee.Id,
            Module = module,
            CanView = canView,
            CanCreate = false,
            CanModify = false
        });
        Context.SaveChanges();
    }

    public async Task AuthenticateAsync(User user)
    {
        var result = await authenticationService.AuthenticateAsync(user.Username, Password);
        Assert.True(result.Success);
    }

    public void Dispose()
    {
        Context.Database.CloseConnection();
        scope.Dispose();
        serviceProvider.Dispose();
        SqliteConnection.ClearAllPools();

        foreach (var path in new[]
        {
            databasePath,
            $"{databasePath}-wal",
            $"{databasePath}-shm",
            $"{databasePath}-journal"
        })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
