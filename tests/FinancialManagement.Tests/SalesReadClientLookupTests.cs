using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class SaleReadServiceTests
{
    [Fact]
    public async Task ResponsableCanReadKnownAndAnonymousSalesWithExactValues()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Atlas Client", "CLI-ATLAS");
        database.AddSale(
            responsible,
            "VTE-KNOWN",
            new DateOnly(2026, 9, 1),
            9_000_000_000,
            client.Id,
            new DateOnly(2026, 9, 30),
            "Known observation");
        database.AddSale(
            responsible,
            "VTE-ANONYMOUS",
            new DateOnly(2026, 9, 2),
            125_075);
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.SaleService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Equal(SaleListFailureReason.None, result.FailureReason);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(1, result.TotalPages);
        var anonymous = Assert.Single(result.Items, item => item.Reference == "VTE-ANONYMOUS");
        Assert.Null(anonymous.ClientId);
        Assert.Null(anonymous.ClientName);
        var known = Assert.Single(result.Items, item => item.Reference == "VTE-KNOWN");
        Assert.Equal(client.Id, known.ClientId);
        Assert.Equal("Atlas Client", known.ClientName);
        Assert.Equal(9_000_000_000, known.TotalAmountMinor);
        Assert.Equal(new DateOnly(2026, 9, 30), known.DueDate);
        Assert.Equal("Known observation", known.Observation);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task EmployeeWithSalesViewCanReadSalesCreatedByAnotherUser()
    {
        using var database = new SalesReadTestDatabase();
        var owner = database.AddUser("owner", UserRole.Responsable);
        var employee = database.AddUser("sales-reader", UserRole.Employe);
        database.Grant(employee, PermissionModule.Sales, canView: true);
        database.AddSale(owner, "VTE-OWNER", new DateOnly(2026, 9, 1), 1_000);
        await database.AuthenticateAsync(employee);

        var result = await database.SaleService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Single(result.Items);
        Assert.Equal("VTE-OWNER", result.Items[0].Reference);
    }

    [Fact]
    public async Task EmployeeWithoutSalesViewIsForbiddenWithoutData()
    {
        using var database = new SalesReadTestDatabase();
        var owner = database.AddUser("owner", UserRole.Responsable);
        var employee = database.AddUser("sales-denied", UserRole.Employe);
        database.Grant(employee, PermissionModule.Sales, canView: false);
        database.AddSale(owner, "VTE-SECRET", new DateOnly(2026, 9, 1), 1_000);
        await database.AuthenticateAsync(employee);

        var result = await database.SaleService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(SaleListFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task UnauthenticatedSalesReadIsRejectedWithoutData()
    {
        using var database = new SalesReadTestDatabase();
        var owner = database.AddUser("owner", UserRole.Responsable);
        database.AddSale(owner, "VTE-SECRET", new DateOnly(2026, 9, 1), 1_000);

        var result = await database.SaleService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(SaleListFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task PaginationUsesStableDateAndIdDescendingOrderWithoutGaps()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var first = database.AddSale(responsible, "VTE-1", new DateOnly(2026, 9, 2), 101);
        var second = database.AddSale(responsible, "VTE-2", new DateOnly(2026, 9, 2), 102);
        var third = database.AddSale(responsible, "VTE-3", new DateOnly(2026, 9, 3), 103);
        var fourth = database.AddSale(responsible, "VTE-4", new DateOnly(2026, 9, 1), 104);
        var fifth = database.AddSale(responsible, "VTE-5", new DateOnly(2026, 9, 1), 105);
        await database.AuthenticateAsync(responsible);

        var page1 = await database.SaleService.GetPageAsync(new(PageNumber: 1, PageSize: 2));
        var page2 = await database.SaleService.GetPageAsync(new(PageNumber: 2, PageSize: 2));
        var page3 = await database.SaleService.GetPageAsync(new(PageNumber: 3, PageSize: 2));

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
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        database.AddSale(responsible, "VTE-D1", new DateOnly(2026, 9, 1), 100);
        database.AddSale(responsible, "VTE-D2", new DateOnly(2026, 9, 2), 200);
        database.AddSale(responsible, "VTE-D3", new DateOnly(2026, 9, 3), 300);
        await database.AuthenticateAsync(responsible);

        var from = await database.SaleService.GetPageAsync(new(FromDate: new DateOnly(2026, 9, 2)));
        var to = await database.SaleService.GetPageAsync(new(ToDate: new DateOnly(2026, 9, 2)));
        var exact = await database.SaleService.GetPageAsync(new(
            FromDate: new DateOnly(2026, 9, 2),
            ToDate: new DateOnly(2026, 9, 2)));

        Assert.Equal(["VTE-D3", "VTE-D2"], from.Items.Select(item => item.Reference));
        Assert.Equal(["VTE-D2", "VTE-D1"], to.Items.Select(item => item.Reference));
        Assert.Equal(["VTE-D2"], exact.Items.Select(item => item.Reference));
    }

    [Fact]
    public async Task InvalidDateRangeReturnsControlledFailure()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SaleService.GetPageAsync(new(
            FromDate: new DateOnly(2026, 9, 2),
            ToDate: new DateOnly(2026, 9, 1)));

        Assert.False(result.Success);
        Assert.Equal(SaleListFailureReason.InvalidDateRange, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData(0, 50, SaleListFailureReason.InvalidPageNumber)]
    [InlineData(1, 0, SaleListFailureReason.InvalidPageSize)]
    [InlineData(1, 201, SaleListFailureReason.InvalidPageSize)]
    [InlineData(int.MaxValue, 200, SaleListFailureReason.InvalidPageNumber)]
    public async Task InvalidPaginationReturnsControlledFailure(
        int pageNumber,
        int pageSize,
        SaleListFailureReason expected)
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SaleService.GetPageAsync(new(pageNumber, pageSize));

        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SearchUsesTrimmedReferenceOrClientNameButNotObservation()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var atlas = database.AddClient("Atlas Client", "CLI-001");
        var cedar = database.AddClient("Cedar Client", "CLI-002");
        database.AddSale(responsible, "VTE-ALPHA", new DateOnly(2026, 9, 1), 100, atlas.Id);
        database.AddSale(
            responsible,
            "VTE-BETA",
            new DateOnly(2026, 9, 2),
            200,
            cedar.Id,
            observation: "HiddenNeedle");
        database.AddSale(responsible, "VTE-ANON", new DateOnly(2026, 9, 3), 300);
        await database.AuthenticateAsync(responsible);

        var byReference = await database.SaleService.GetPageAsync(new(SearchText: "  ALPHA  "));
        var byClient = await database.SaleService.GetPageAsync(new(SearchText: "Cedar"));
        var whitespace = await database.SaleService.GetPageAsync(new(SearchText: "   "));
        var observation = await database.SaleService.GetPageAsync(new(SearchText: "HiddenNeedle"));

        Assert.Equal(["VTE-ALPHA"], byReference.Items.Select(item => item.Reference));
        Assert.Equal(["VTE-BETA"], byClient.Items.Select(item => item.Reference));
        Assert.Equal(3, whitespace.TotalCount);
        Assert.Empty(observation.Items);
    }

    [Fact]
    public async Task SalesReadHonorsCancellationBeforeQuery()
    {
        using var database = new SalesReadTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SaleService.GetPageAsync(new(), cancellation.Token));
    }

    [Fact]
    public async Task SalesReadDoesNotMutateOrTrackEntities()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var sale = database.AddSale(
            responsible,
            "VTE-UNCHANGED",
            new DateOnly(2026, 9, 1),
            500,
            observation: "Original");
        var updatedAtBefore = sale.UpdatedAtUtc;
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.SaleService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        var persisted = await database.Context.Sales.AsNoTracking().SingleAsync();
        Assert.Equal("Original", persisted.Observation);
        Assert.Equal(updatedAtBefore, persisted.UpdatedAtUtc);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }
}

public sealed class ClientLookupServiceTests
{
    [Fact]
    public async Task ResponsableCanLookupClientsWithDistinguishingIdentifier()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Atlas Client", "CLI-ATLAS");
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientLookupService.SearchAsync(new());

        Assert.True(result.Success);
        Assert.Equal(ClientLookupFailureReason.None, result.FailureReason);
        var item = Assert.Single(result.Items);
        Assert.Equal(client.Id, item.ClientId);
        Assert.Equal("Atlas Client", item.DisplayName);
        Assert.Equal("CLI-ATLAS", item.Identifier);
        Assert.Equal(20, result.Limit);
    }

    [Fact]
    public async Task EmployeeWithClientsViewCanUseLookup()
    {
        using var database = new SalesReadTestDatabase();
        var employee = database.AddUser("client-reader", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView: true);
        database.AddClient("Visible Client");
        await database.AuthenticateAsync(employee);

        var result = await database.ClientLookupService.SearchAsync(new());

        Assert.True(result.Success);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task EmployeeWithoutClientsViewIsForbiddenWithoutData()
    {
        using var database = new SalesReadTestDatabase();
        var employee = database.AddUser("client-denied", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView: false);
        database.AddClient("Secret Client");
        await database.AuthenticateAsync(employee);

        var result = await database.ClientLookupService.SearchAsync(new());

        Assert.False(result.Success);
        Assert.Equal(ClientLookupFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task UnauthenticatedClientLookupIsRejectedWithoutData()
    {
        using var database = new SalesReadTestDatabase();
        database.AddClient("Secret Client");

        var result = await database.ClientLookupService.SearchAsync(new());

        Assert.False(result.Success);
        Assert.Equal(ClientLookupFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task LookupSearchUsesTrimmedNameOrIdentifierOnly()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        database.AddClient("Atlas Client", "CLI-001", company: "CompanyNeedle");
        database.AddClient("Cedar Client", "CLI-XYZ");
        await database.AuthenticateAsync(responsible);

        var byName = await database.ClientLookupService.SearchAsync(new("  Atlas  "));
        var byIdentifier = await database.ClientLookupService.SearchAsync(new("  XYZ  "));
        var byCompany = await database.ClientLookupService.SearchAsync(new("CompanyNeedle"));

        Assert.Equal(["Atlas Client"], byName.Items.Select(item => item.DisplayName));
        Assert.Equal(["Cedar Client"], byIdentifier.Items.Select(item => item.DisplayName));
        Assert.Empty(byCompany.Items);
    }

    [Fact]
    public async Task EmptySearchReturnsStableNameAndIdOrder()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var zulu = database.AddClient("Zulu");
        var firstAtlas = database.AddClient("Atlas");
        var secondAtlas = database.AddClient("Atlas");
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientLookupService.SearchAsync(new("   "));

        Assert.Equal(
            [firstAtlas.Id, secondAtlas.Id, zulu.Id],
            result.Items.Select(item => item.ClientId));
    }

    [Fact]
    public async Task LookupRespectsRequestedLimitAndMaximum()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        database.AddClients(55);
        await database.AuthenticateAsync(responsible);

        var limited = await database.ClientLookupService.SearchAsync(new(Limit: 3));
        var maximum = await database.ClientLookupService.SearchAsync(new(Limit: 50));

        Assert.Equal(3, limited.Items.Count);
        Assert.Equal(50, maximum.Items.Count);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(51)]
    public async Task InvalidLookupLimitReturnsControlledFailure(int limit)
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientLookupService.SearchAsync(new(Limit: limit));

        Assert.False(result.Success);
        Assert.Equal(ClientLookupFailureReason.InvalidLimit, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public void LookupDtoExposesOnlySelectionFields()
    {
        Assert.Equal(
            [nameof(ClientLookupItem.ClientId), nameof(ClientLookupItem.DisplayName), nameof(ClientLookupItem.Identifier)],
            typeof(ClientLookupItem).GetProperties().Select(property => property.Name));
    }

    [Fact]
    public async Task ClientLookupHonorsCancellationBeforeQuery()
    {
        using var database = new SalesReadTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.ClientLookupService.SearchAsync(new(), cancellation.Token));
    }

    [Fact]
    public async Task ClientLookupDoesNotMutateOrTrackEntities()
    {
        using var database = new SalesReadTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Unchanged Client", "CLI-UNCHANGED");
        var updatedAtBefore = client.UpdatedAtUtc;
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.ClientLookupService.SearchAsync(new());

        Assert.True(result.Success);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        var persisted = await database.Context.Clients.AsNoTracking().SingleAsync();
        Assert.Equal("Unchanged Client", persisted.Name);
        Assert.Equal(updatedAtBefore, persisted.UpdatedAtUtc);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SalesAndClientsViewPermissionsRemainIndependent()
    {
        using var database = new SalesReadTestDatabase();
        var owner = database.AddUser("owner", UserRole.Responsable);
        var salesReader = database.AddUser("sales-reader", UserRole.Employe);
        var clientReader = database.AddUser("client-reader", UserRole.Employe);
        database.Grant(salesReader, PermissionModule.Sales, canView: true);
        database.Grant(clientReader, PermissionModule.Clients, canView: true);
        database.AddClient("Lookup Client");
        database.AddSale(owner, "VTE-PERMISSIONS", new DateOnly(2026, 9, 1), 1_000);

        await database.AuthenticateAsync(salesReader);
        var salesAllowed = await database.SaleService.GetPageAsync(new());
        var clientsDenied = await database.ClientLookupService.SearchAsync(new());

        Assert.True(salesAllowed.Success);
        Assert.Equal(ClientLookupFailureReason.Forbidden, clientsDenied.FailureReason);
        Assert.Empty(clientsDenied.Items);

        await database.AuthenticateAsync(clientReader);
        var clientsAllowed = await database.ClientLookupService.SearchAsync(new());
        var salesDenied = await database.SaleService.GetPageAsync(new());

        Assert.True(clientsAllowed.Success);
        Assert.Equal(SaleListFailureReason.Forbidden, salesDenied.FailureReason);
        Assert.Empty(salesDenied.Items);
    }
}

internal sealed class SalesReadTestDatabase : IDisposable
{
    private const string Password = "Sales read test password 42!";
    private static readonly DateTimeOffset TestUtc =
        new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    private readonly string databasePath;
    private readonly ServiceProvider serviceProvider;
    private readonly IServiceScope scope;
    private readonly IPasswordHasher passwordHasher;
    private readonly IAuthenticationService authenticationService;

    public SalesReadTestDatabase()
    {
        databasePath = Path.Combine(
            Path.GetTempPath(),
            $"FinancialManagement-SalesRead-{Guid.NewGuid():N}.db");
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
        SaleService = scope.ServiceProvider.GetRequiredService<ISaleService>();
        ClientLookupService = scope.ServiceProvider.GetRequiredService<IClientLookupService>();
    }

    public FinancialManagementDbContext Context { get; }

    public ISaleService SaleService { get; }

    public IClientLookupService ClientLookupService { get; }

    public User AddUser(string username, UserRole role)
    {
        var user = new User
        {
            FullName = $"Sales Read Test {username}",
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

    public Client AddClient(
        string name,
        string? identifier = null,
        string? company = null)
    {
        var client = new Client
        {
            Name = name,
            Identifier = identifier,
            Company = company,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Clients.Add(client);
        Context.SaveChanges();
        return client;
    }

    public void AddClients(int count)
    {
        Context.Clients.AddRange(Enumerable.Range(1, count).Select(index => new Client
        {
            Name = $"Client {index:000}",
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        }));
        Context.SaveChanges();
    }

    public Sale AddSale(
        User creator,
        string reference,
        DateOnly saleDate,
        long amountMinor,
        long? clientId = null,
        DateOnly? dueDate = null,
        string? observation = null)
    {
        var sale = new Sale
        {
            Reference = reference,
            SaleDate = saleDate,
            TotalAmountMinor = amountMinor,
            ClientId = clientId,
            DueDate = dueDate,
            Observation = observation,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Sales.Add(sale);
        Context.SaveChanges();
        return sale;
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
