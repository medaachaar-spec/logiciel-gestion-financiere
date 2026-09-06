using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

internal sealed class PartyCrudTestDatabase : IDisposable
{
    private const string Password = "Party CRUD test password 42!";
    private readonly string databasePath;
    private readonly ServiceProvider serviceProvider;
    private readonly IServiceScope scope;
    private readonly IPasswordHasher passwordHasher;
    private readonly IAuthenticationService authenticationService;
    private int referenceSequence;

    public PartyCrudTestDatabase()
    {
        Clock = new MutableTimeProvider(new DateTimeOffset(2026, 9, 6, 12, 0, 0, TimeSpan.Zero));
        databasePath = Path.Combine(
            Path.GetTempPath(),
            $"FinancialManagement-PartyCrud-{Guid.NewGuid():N}.db");

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(Clock);
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
        ClientService = scope.ServiceProvider.GetRequiredService<IClientService>();
        SupplierService = scope.ServiceProvider.GetRequiredService<ISupplierService>();
    }

    public FinancialManagementDbContext Context { get; }

    public IClientService ClientService { get; }

    public ISupplierService SupplierService { get; }

    public MutableTimeProvider Clock { get; }

    public User AddUser(string username, UserRole role)
    {
        var user = new User
        {
            FullName = $"Party Test {username}",
            Username = username,
            PasswordHash = passwordHasher.HashPassword(Password),
            Role = role,
            IsActive = true,
            CreatedAtUtc = Clock.GetUtcNow(),
            UpdatedAtUtc = Clock.GetUtcNow()
        };
        Context.Users.Add(user);
        Context.SaveChanges();
        return user;
    }

    public void Grant(
        User employee,
        PermissionModule module,
        bool canView,
        bool canCreate,
        bool canModify)
    {
        Context.UserPermissions.Add(new UserPermission
        {
            UserId = employee.Id,
            Module = module,
            CanView = canView,
            CanCreate = canCreate,
            CanModify = canModify
        });
        Context.SaveChanges();
    }

    public async Task AuthenticateAsync(User user)
    {
        var result = await authenticationService.AuthenticateAsync(user.Username, Password);
        Assert.True(result.Success);
    }

    public Client AddClient(
        string name,
        string? identifier = null,
        DateTimeOffset? createdAtUtc = null)
    {
        var timestamp = createdAtUtc ?? Clock.GetUtcNow();
        var client = new Client
        {
            Name = name,
            Identifier = identifier,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        Context.Clients.Add(client);
        Context.SaveChanges();
        return client;
    }

    public Supplier AddSupplier(
        string name,
        string? identifier = null,
        DateTimeOffset? createdAtUtc = null)
    {
        var timestamp = createdAtUtc ?? Clock.GetUtcNow();
        var supplier = new Supplier
        {
            Name = name,
            Identifier = identifier,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };
        Context.Suppliers.Add(supplier);
        Context.SaveChanges();
        return supplier;
    }

    public void AddClientUse(Client client, ClientUseKind useKind, User creator)
    {
        var reference = NextReference("CLI-USE");
        switch (useKind)
        {
            case ClientUseKind.Sale:
                Context.Sales.Add(new Sale
                {
                    Reference = reference,
                    SaleDate = new DateOnly(2026, 9, 6),
                    TotalAmountMinor = 100,
                    ClientId = client.Id,
                    CreatedByUserId = creator.Id,
                    CreatedAtUtc = Clock.GetUtcNow(),
                    UpdatedAtUtc = Clock.GetUtcNow()
                });
                break;
            case ClientUseKind.CustomerPayment:
                Context.CustomerPayments.Add(new CustomerPayment
                {
                    Reference = reference,
                    ClientId = client.Id,
                    PaymentDate = new DateOnly(2026, 9, 6),
                    AmountMinor = 100,
                    PaymentMethod = PaymentMethod.Cash,
                    CreatedByUserId = creator.Id,
                    CreatedAtUtc = Clock.GetUtcNow()
                });
                break;
            case ClientUseKind.InitialCustomerReceivable:
                Context.InitialCustomerReceivables.Add(new InitialCustomerReceivable
                {
                    Reference = reference,
                    ClientId = client.Id,
                    ReceivableDate = new DateOnly(2026, 9, 6),
                    InitialAmountMinor = 100,
                    CreatedByUserId = creator.Id,
                    CreatedAtUtc = Clock.GetUtcNow()
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(useKind));
        }

        Context.SaveChanges();
    }

    public void AddSupplierUse(Supplier supplier, SupplierUseKind useKind, User creator)
    {
        var reference = NextReference("SUP-USE");
        switch (useKind)
        {
            case SupplierUseKind.Purchase:
                Context.Purchases.Add(new Purchase
                {
                    Reference = reference,
                    PurchaseDate = new DateOnly(2026, 9, 6),
                    TotalAmountMinor = 100,
                    SupplierId = supplier.Id,
                    CreatedByUserId = creator.Id,
                    CreatedAtUtc = Clock.GetUtcNow(),
                    UpdatedAtUtc = Clock.GetUtcNow()
                });
                break;
            case SupplierUseKind.SupplierPayment:
                Context.SupplierPayments.Add(new SupplierPayment
                {
                    Reference = reference,
                    SupplierId = supplier.Id,
                    PaymentDate = new DateOnly(2026, 9, 6),
                    AmountMinor = 100,
                    PaymentMethod = PaymentMethod.Cash,
                    CreatedByUserId = creator.Id,
                    CreatedAtUtc = Clock.GetUtcNow()
                });
                break;
            case SupplierUseKind.InitialSupplierPayable:
                Context.InitialSupplierPayables.Add(new InitialSupplierPayable
                {
                    Reference = reference,
                    SupplierId = supplier.Id,
                    PayableDate = new DateOnly(2026, 9, 6),
                    InitialAmountMinor = 100,
                    CreatedByUserId = creator.Id,
                    CreatedAtUtc = Clock.GetUtcNow()
                });
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(useKind));
        }

        Context.SaveChanges();
    }

    public int CountClientUses(ClientUseKind useKind, long clientId) => useKind switch
    {
        ClientUseKind.Sale => Context.Sales.Count(item => item.ClientId == clientId),
        ClientUseKind.CustomerPayment => Context.CustomerPayments.Count(item => item.ClientId == clientId),
        ClientUseKind.InitialCustomerReceivable =>
            Context.InitialCustomerReceivables.Count(item => item.ClientId == clientId),
        _ => throw new ArgumentOutOfRangeException(nameof(useKind))
    };

    public int CountSupplierUses(SupplierUseKind useKind, long supplierId) => useKind switch
    {
        SupplierUseKind.Purchase => Context.Purchases.Count(item => item.SupplierId == supplierId),
        SupplierUseKind.SupplierPayment => Context.SupplierPayments.Count(item => item.SupplierId == supplierId),
        SupplierUseKind.InitialSupplierPayable =>
            Context.InitialSupplierPayables.Count(item => item.SupplierId == supplierId),
        _ => throw new ArgumentOutOfRangeException(nameof(useKind))
    };

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

    private string NextReference(string prefix) => $"{prefix}-{++referenceSequence:000}";
}

public enum ClientUseKind
{
    Sale,
    CustomerPayment,
    InitialCustomerReceivable
}

public enum SupplierUseKind
{
    Purchase,
    SupplierPayment,
    InitialSupplierPayable
}

internal sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;

    public override DateTimeOffset GetUtcNow() => UtcNow;
}
