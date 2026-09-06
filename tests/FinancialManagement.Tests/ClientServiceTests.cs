using FinancialManagement.Application.Clients;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Tests;

public sealed class ClientCreateServiceTests
{
    [Fact]
    public async Task Unauthenticated_create_is_rejected()
    {
        using var database = new PartyCrudTestDatabase();

        var result = await database.ClientService.CreateAsync(new("Client"));

        Assert.False(result.Success);
        Assert.Equal(CreateClientFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(database.Context.Clients);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Effective_create_permission_requires_view_and_create(
        bool canView,
        bool canCreate)
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser($"client-create-{canView}-{canCreate}", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView, canCreate, canModify: false);
        await database.AuthenticateAsync(employee);

        var result = await database.ClientService.CreateAsync(new("Client"));

        Assert.False(result.Success);
        Assert.Equal(CreateClientFailureReason.NotAuthorized, result.FailureReason);
        Assert.Empty(database.Context.Clients);
    }

    [Fact]
    public async Task Employee_with_effective_create_permission_can_create()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("client-create-allowed", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView: true, canCreate: true, canModify: false);
        await database.AuthenticateAsync(employee);

        var result = await database.ClientService.CreateAsync(new("Client"));

        Assert.True(result.Success);
        Assert.Single(database.Context.Clients);
    }

    [Fact]
    public async Task Valid_create_normalizes_fields_and_sets_deterministic_timestamps()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-create-owner", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.CreateAsync(new(
            "  Atlas Client  ",
            "  0600000000  ",
            "  Rabat  ",
            "  Atlas SARL  ",
            "  CLI-001  ",
            "  Important  "));

        Assert.True(result.Success);
        var client = database.Context.Clients.Single(item => item.Id == result.ClientId);
        Assert.Equal("Atlas Client", client.Name);
        Assert.Equal("0600000000", client.Phone);
        Assert.Equal("Rabat", client.Address);
        Assert.Equal("Atlas SARL", client.Company);
        Assert.Equal("CLI-001", client.Identifier);
        Assert.Equal("Important", client.Observation);
        Assert.Equal(database.Clock.UtcNow, client.CreatedAtUtc);
        Assert.Equal(database.Clock.UtcNow, client.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_name_is_rejected(string? name)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"invalid-client-{Guid.NewGuid():N}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.CreateAsync(new(name!));

        Assert.False(result.Success);
        Assert.Equal(CreateClientFailureReason.InvalidName, result.FailureReason);
        Assert.Empty(database.Context.Clients);
    }

    [Fact]
    public async Task Whitespace_optional_fields_are_stored_as_null()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-null-options", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.CreateAsync(new(
            "Client",
            " ",
            null,
            "\t",
            "  ",
            null));

        Assert.True(result.Success);
        var client = database.Context.Clients.Single();
        Assert.Null(client.Phone);
        Assert.Null(client.Address);
        Assert.Null(client.Company);
        Assert.Null(client.Identifier);
        Assert.Null(client.Observation);
    }

    [Fact]
    public async Task Duplicate_name_and_identifier_are_allowed()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-duplicates", UserRole.Responsable);
        database.AddClient("Duplicate", "DUP-001");
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.CreateAsync(new("Duplicate", Identifier: "DUP-001"));

        Assert.True(result.Success);
        Assert.Equal(2, database.Context.Clients.Count(item =>
            item.Name == "Duplicate" && item.Identifier == "DUP-001"));
    }

    [Fact]
    public async Task Create_propagates_cancellation_without_writes()
    {
        using var database = new PartyCrudTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.ClientService.CreateAsync(new("Client"), cancellation.Token));
        Assert.Empty(database.Context.Clients);
    }
}

public sealed class ClientListServiceTests
{
    [Fact]
    public async Task List_requires_authentication()
    {
        using var database = new PartyCrudTestDatabase();
        database.AddClient("Secret");

        var result = await database.ClientService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(ClientListFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task List_requires_view_permission()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("client-list-denied", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView: false, canCreate: true, canModify: true);
        database.AddClient("Secret");
        await database.AuthenticateAsync(employee);

        var result = await database.ClientService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(ClientListFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Employee_with_view_permission_can_list()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("client-list-allowed", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView: true, canCreate: false, canModify: false);
        database.AddClient("Visible");
        await database.AuthenticateAsync(employee);

        var result = await database.ClientService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Equal(["Visible"], result.Items.Select(item => item.Name));
    }

    [Theory]
    [InlineData(0, 50, ClientListFailureReason.InvalidPageNumber)]
    [InlineData(-1, 50, ClientListFailureReason.InvalidPageNumber)]
    [InlineData(1, 0, ClientListFailureReason.InvalidPageSize)]
    [InlineData(1, 201, ClientListFailureReason.InvalidPageSize)]
    [InlineData(2147483647, 200, ClientListFailureReason.InvalidPageNumber)]
    public async Task Invalid_pagination_returns_controlled_failure(
        int pageNumber,
        int pageSize,
        ClientListFailureReason expected)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"client-page-{pageNumber}-{pageSize}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.GetPageAsync(new(pageNumber, pageSize));

        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureReason);
    }

    [Fact]
    public async Task Pagination_reports_totals_and_uses_name_then_id_order()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-pagination", UserRole.Responsable);
        var zulu = database.AddClient("Zulu");
        var atlasOne = database.AddClient("Atlas");
        var atlasTwo = database.AddClient("Atlas");
        database.AddClient("Bravo");
        await database.AuthenticateAsync(responsible);

        var first = await database.ClientService.GetPageAsync(new(PageSize: 2));
        var second = await database.ClientService.GetPageAsync(new(PageNumber: 2, PageSize: 2));

        Assert.Equal([atlasOne.Id, atlasTwo.Id], first.Items.Select(item => item.Id));
        Assert.Equal(["Bravo", zulu.Name], second.Items.Select(item => item.Name));
        Assert.Equal(4, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(1, first.PageNumber);
        Assert.Equal(2, first.PageSize);
    }

    [Fact]
    public async Task Search_uses_trimmed_name_or_identifier_only()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-search", UserRole.Responsable);
        database.AddClient("Atlas Client", "CLI-001");
        var companyOnly = database.AddClient("Cedar", "CLI-XYZ");
        companyOnly.Company = "CompanyNeedle";
        database.Context.SaveChanges();
        await database.AuthenticateAsync(responsible);

        var byName = await database.ClientService.GetPageAsync(new(SearchText: "  Atlas "));
        var byIdentifier = await database.ClientService.GetPageAsync(new(SearchText: " XYZ "));
        var byCompany = await database.ClientService.GetPageAsync(new(SearchText: "CompanyNeedle"));
        var whitespace = await database.ClientService.GetPageAsync(new(SearchText: "   "));

        Assert.Equal(["Atlas Client"], byName.Items.Select(item => item.Name));
        Assert.Equal(["Cedar"], byIdentifier.Items.Select(item => item.Name));
        Assert.Empty(byCompany.Items);
        Assert.Equal(2, whitespace.TotalCount);
    }

    [Fact]
    public async Task List_projects_only_expected_fields_without_tracking_clients()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-projection", UserRole.Responsable);
        var client = database.AddClient("Projected", "CLI-P");
        client.Phone = "0600";
        client.Company = "Company";
        client.Address = "Hidden address";
        client.Observation = "Hidden note";
        database.Context.SaveChanges();
        database.Context.ChangeTracker.Clear();
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.ClientService.GetPageAsync(new());

        var item = Assert.Single(result.Items);
        Assert.Equal(["Id", "Name", "Phone", "Company", "Identifier"],
            typeof(ClientListItem).GetProperties().Select(property => property.Name));
        Assert.Equal("0600", item.Phone);
        Assert.Equal("Company", item.Company);
        Assert.Empty(database.Context.ChangeTracker.Entries<Client>());
    }

    [Fact]
    public async Task List_propagates_cancellation()
    {
        using var database = new PartyCrudTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.ClientService.GetPageAsync(new(), cancellation.Token));
    }
}

public sealed class ClientDetailServiceTests
{
    [Fact]
    public async Task Detail_requires_authentication()
    {
        using var database = new PartyCrudTestDatabase();

        var result = await database.ClientService.GetByIdAsync(1);

        Assert.False(result.Success);
        Assert.Equal(ClientDetailFailureReason.NotAuthenticated, result.FailureReason);
    }

    [Fact]
    public async Task Detail_requires_view_permission()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("client-detail-denied", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView: false, canCreate: true, canModify: true);
        var client = database.AddClient("Secret");
        await database.AuthenticateAsync(employee);

        var result = await database.ClientService.GetByIdAsync(client.Id);

        Assert.False(result.Success);
        Assert.Equal(ClientDetailFailureReason.Forbidden, result.FailureReason);
    }

    [Fact]
    public async Task Employee_with_view_permission_can_read_detail()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("client-detail-allowed", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView: true, canCreate: false, canModify: false);
        var client = database.AddClient("Visible");
        await database.AuthenticateAsync(employee);

        var result = await database.ClientService.GetByIdAsync(client.Id);

        Assert.True(result.Success);
        Assert.Equal(client.Id, result.Item!.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Detail_rejects_invalid_id(long clientId)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"client-detail-{clientId}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.GetByIdAsync(clientId);

        Assert.Equal(ClientDetailFailureReason.InvalidClientId, result.FailureReason);
    }

    [Fact]
    public async Task Detail_returns_not_found()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-detail-missing", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.GetByIdAsync(999999);

        Assert.Equal(ClientDetailFailureReason.NotFound, result.FailureReason);
    }

    [Fact]
    public async Task Detail_projects_all_current_fields_and_timestamps_without_tracking()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-detail", UserRole.Responsable);
        var createdAt = database.Clock.UtcNow.AddDays(-2);
        var client = database.AddClient("Detail Client", "CLI-D", createdAt);
        client.Phone = "0600";
        client.Address = "Rabat";
        client.Company = "Detail Co";
        client.Observation = "Note";
        client.UpdatedAtUtc = createdAt.AddDays(1);
        database.Context.SaveChanges();
        var clientId = client.Id;
        database.Context.ChangeTracker.Clear();
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.ClientService.GetByIdAsync(clientId);

        Assert.True(result.Success);
        var item = Assert.IsType<ClientDetailItem>(result.Item);
        Assert.Equal(clientId, item.Id);
        Assert.Equal("Detail Client", item.Name);
        Assert.Equal("0600", item.Phone);
        Assert.Equal("Rabat", item.Address);
        Assert.Equal("Detail Co", item.Company);
        Assert.Equal("CLI-D", item.Identifier);
        Assert.Equal("Note", item.Observation);
        Assert.Equal(createdAt, item.CreatedAtUtc);
        Assert.Equal(createdAt.AddDays(1), item.UpdatedAtUtc);
        Assert.Empty(database.Context.ChangeTracker.Entries<Client>());
    }
}

public sealed class ClientUpdateServiceTests
{
    [Fact]
    public async Task Update_requires_authentication()
    {
        using var database = new PartyCrudTestDatabase();

        var result = await database.ClientService.UpdateAsync(new(1, "Client"));

        Assert.Equal(UpdateClientFailureReason.NotAuthenticated, result.FailureReason);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Effective_modify_permission_requires_view_and_modify(
        bool canView,
        bool canModify)
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser($"client-modify-{canView}-{canModify}", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView, canCreate: false, canModify);
        var client = database.AddClient("Original");
        await database.AuthenticateAsync(employee);

        var result = await database.ClientService.UpdateAsync(new(client.Id, "Changed"));

        Assert.Equal(UpdateClientFailureReason.NotAuthorized, result.FailureReason);
        Assert.Equal("Original", database.Context.Clients.Single(item => item.Id == client.Id).Name);
    }

    [Fact]
    public async Task Employee_with_effective_modify_permission_can_update()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("client-modify-allowed", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView: true, canCreate: false, canModify: true);
        var client = database.AddClient("Original");
        await database.AuthenticateAsync(employee);

        var result = await database.ClientService.UpdateAsync(new(client.Id, "Changed"));

        Assert.True(result.Success);
        Assert.Equal("Changed", database.Context.Clients.Single(item => item.Id == client.Id).Name);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Update_rejects_invalid_id(long clientId)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"client-update-{clientId}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.UpdateAsync(new(clientId, "Client"));

        Assert.Equal(UpdateClientFailureReason.InvalidClientId, result.FailureReason);
    }

    [Fact]
    public async Task Update_returns_not_found()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-update-missing", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.UpdateAsync(new(999999, "Client"));

        Assert.Equal(UpdateClientFailureReason.NotFound, result.FailureReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Update_rejects_invalid_name(string? name)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"client-update-name-{Guid.NewGuid():N}", UserRole.Responsable);
        var client = database.AddClient("Original");
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.UpdateAsync(new(client.Id, name!));

        Assert.Equal(UpdateClientFailureReason.InvalidName, result.FailureReason);
        Assert.Equal("Original", database.Context.Clients.Single(item => item.Id == client.Id).Name);
    }

    [Fact]
    public async Task Update_normalizes_all_fields_preserves_created_and_sets_updated_time()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-update-all", UserRole.Responsable);
        var createdAt = database.Clock.UtcNow.AddYears(-1);
        var client = database.AddClient("Original", createdAtUtc: createdAt);
        database.Clock.UtcNow = database.Clock.UtcNow.AddHours(3);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.UpdateAsync(new(
            client.Id,
            "  Updated  ",
            "  0600  ",
            "  Rabat  ",
            "  Company  ",
            "  CLI-U  ",
            "   "));

        Assert.True(result.Success);
        var updated = database.Context.Clients.Single(item => item.Id == client.Id);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal("0600", updated.Phone);
        Assert.Equal("Rabat", updated.Address);
        Assert.Equal("Company", updated.Company);
        Assert.Equal("CLI-U", updated.Identifier);
        Assert.Null(updated.Observation);
        Assert.Equal(createdAt, updated.CreatedAtUtc);
        Assert.Equal(database.Clock.UtcNow, updated.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(ClientUseKind.Sale)]
    [InlineData(ClientUseKind.CustomerPayment)]
    [InlineData(ClientUseKind.InitialCustomerReceivable)]
    public async Task Used_client_can_be_renamed(ClientUseKind useKind)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"client-used-{useKind}", UserRole.Responsable);
        var client = database.AddClient("Original");
        database.AddClientUse(client, useKind, responsible);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.UpdateAsync(new(client.Id, "Renamed"));

        Assert.True(result.Success);
        Assert.Equal("Renamed", database.Context.Clients.Single(item => item.Id == client.Id).Name);
        Assert.Equal(1, database.CountClientUses(useKind, client.Id));
    }

    [Fact]
    public async Task Update_allows_duplicate_name_and_identifier()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-update-duplicate", UserRole.Responsable);
        database.AddClient("Duplicate", "DUP");
        var client = database.AddClient("Other", "OTHER");
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.UpdateAsync(new(client.Id, "Duplicate", Identifier: "DUP"));

        Assert.True(result.Success);
        Assert.Equal(2, database.Context.Clients.Count(item => item.Name == "Duplicate" && item.Identifier == "DUP"));
    }

    [Fact]
    public async Task Update_propagates_cancellation_without_changes()
    {
        using var database = new PartyCrudTestDatabase();
        var client = database.AddClient("Original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.ClientService.UpdateAsync(new(client.Id, "Changed"), cancellation.Token));
        Assert.Equal("Original", database.Context.Clients.Single(item => item.Id == client.Id).Name);
    }
}

public sealed class ClientDeleteServiceTests
{
    [Fact]
    public async Task Delete_requires_authentication()
    {
        using var database = new PartyCrudTestDatabase();
        var client = database.AddClient("Client");

        var result = await database.ClientService.DeleteAsync(client.Id);

        Assert.Equal(DeleteClientFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Single(database.Context.Clients);
    }

    [Fact]
    public async Task Employee_with_modify_is_still_refused_permanent_delete()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("client-delete-employee", UserRole.Employe);
        database.Grant(employee, PermissionModule.Clients, canView: true, canCreate: true, canModify: true);
        var client = database.AddClient("Client");
        await database.AuthenticateAsync(employee);

        var result = await database.ClientService.DeleteAsync(client.Id);

        Assert.Equal(DeleteClientFailureReason.NotAuthorized, result.FailureReason);
        Assert.True(database.Context.Clients.Any(item => item.Id == client.Id));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Delete_rejects_invalid_id(long clientId)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"client-delete-{clientId}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.DeleteAsync(clientId);

        Assert.Equal(DeleteClientFailureReason.InvalidClientId, result.FailureReason);
    }

    [Fact]
    public async Task Delete_returns_not_found()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-delete-missing", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.DeleteAsync(999999);

        Assert.Equal(DeleteClientFailureReason.NotFound, result.FailureReason);
    }

    [Fact]
    public async Task Responsable_can_delete_unused_client()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("client-delete-owner", UserRole.Responsable);
        var client = database.AddClient("Unused");
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.DeleteAsync(client.Id);

        Assert.True(result.Success);
        Assert.Equal(client.Id, result.ClientId);
        Assert.False(database.Context.Clients.Any(item => item.Id == client.Id));
    }

    [Theory]
    [InlineData(ClientUseKind.Sale)]
    [InlineData(ClientUseKind.CustomerPayment)]
    [InlineData(ClientUseKind.InitialCustomerReceivable)]
    public async Task Used_client_is_not_deleted_and_fk_protection_remains(
        ClientUseKind useKind)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"client-delete-used-{useKind}", UserRole.Responsable);
        var client = database.AddClient("Used");
        database.AddClientUse(client, useKind, responsible);
        await database.AuthenticateAsync(responsible);

        var result = await database.ClientService.DeleteAsync(client.Id);

        Assert.Equal(DeleteClientFailureReason.InUse, result.FailureReason);
        Assert.True(database.Context.Clients.Any(item => item.Id == client.Id));
        Assert.Equal(1, database.CountClientUses(useKind, client.Id));

        database.Context.ChangeTracker.Clear();
        var directDelete = database.Context.Clients.Single(item => item.Id == client.Id);
        database.Context.Clients.Remove(directDelete);
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();
        Assert.True(database.Context.Clients.Any(item => item.Id == client.Id));
        Assert.Equal(1, database.CountClientUses(useKind, client.Id));
    }

    [Fact]
    public async Task Delete_propagates_cancellation_without_changes()
    {
        using var database = new PartyCrudTestDatabase();
        var client = database.AddClient("Client");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.ClientService.DeleteAsync(client.Id, cancellation.Token));
        Assert.True(database.Context.Clients.Any(item => item.Id == client.Id));
    }
}
