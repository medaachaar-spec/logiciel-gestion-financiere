using FinancialManagement.Application.Suppliers;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Tests;

public sealed class SupplierCreateServiceTests
{
    [Fact]
    public async Task Unauthenticated_create_is_rejected()
    {
        using var database = new PartyCrudTestDatabase();

        var result = await database.SupplierService.CreateAsync(new("Supplier"));

        Assert.False(result.Success);
        Assert.Equal(CreateSupplierFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(database.Context.Suppliers);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Effective_create_permission_requires_view_and_create(
        bool canView,
        bool canCreate)
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser($"supplier-create-{canView}-{canCreate}", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView, canCreate, canModify: false);
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierService.CreateAsync(new("Supplier"));

        Assert.False(result.Success);
        Assert.Equal(CreateSupplierFailureReason.NotAuthorized, result.FailureReason);
        Assert.Empty(database.Context.Suppliers);
    }

    [Fact]
    public async Task Employee_with_effective_create_permission_can_create()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("supplier-create-allowed", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView: true, canCreate: true, canModify: false);
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierService.CreateAsync(new("Supplier"));

        Assert.True(result.Success);
        Assert.Single(database.Context.Suppliers);
    }

    [Fact]
    public async Task Valid_create_normalizes_fields_and_sets_deterministic_timestamps()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-create-owner", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.CreateAsync(new(
            "  Atlas Supplier  ",
            "  0600000000  ",
            "  Rabat  ",
            "  Atlas SARL  ",
            "  SUP-001  ",
            "  Important  "));

        Assert.True(result.Success);
        var supplier = database.Context.Suppliers.Single(item => item.Id == result.SupplierId);
        Assert.Equal("Atlas Supplier", supplier.Name);
        Assert.Equal("0600000000", supplier.Phone);
        Assert.Equal("Rabat", supplier.Address);
        Assert.Equal("Atlas SARL", supplier.Company);
        Assert.Equal("SUP-001", supplier.Identifier);
        Assert.Equal("Important", supplier.Observation);
        Assert.Equal(database.Clock.UtcNow, supplier.CreatedAtUtc);
        Assert.Equal(database.Clock.UtcNow, supplier.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_name_is_rejected(string? name)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"invalid-supplier-{Guid.NewGuid():N}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.CreateAsync(new(name!));

        Assert.False(result.Success);
        Assert.Equal(CreateSupplierFailureReason.InvalidName, result.FailureReason);
        Assert.Empty(database.Context.Suppliers);
    }

    [Fact]
    public async Task Whitespace_optional_fields_are_stored_as_null()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-null-options", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.CreateAsync(new(
            "Supplier",
            " ",
            null,
            "\t",
            "  ",
            null));

        Assert.True(result.Success);
        var supplier = database.Context.Suppliers.Single();
        Assert.Null(supplier.Phone);
        Assert.Null(supplier.Address);
        Assert.Null(supplier.Company);
        Assert.Null(supplier.Identifier);
        Assert.Null(supplier.Observation);
    }

    [Fact]
    public async Task Duplicate_name_and_identifier_are_allowed()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-duplicates", UserRole.Responsable);
        database.AddSupplier("Duplicate", "DUP-001");
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.CreateAsync(new("Duplicate", Identifier: "DUP-001"));

        Assert.True(result.Success);
        Assert.Equal(2, database.Context.Suppliers.Count(item =>
            item.Name == "Duplicate" && item.Identifier == "DUP-001"));
    }

    [Fact]
    public async Task Create_propagates_cancellation_without_writes()
    {
        using var database = new PartyCrudTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierService.CreateAsync(new("Supplier"), cancellation.Token));
        Assert.Empty(database.Context.Suppliers);
    }
}

public sealed class SupplierListServiceTests
{
    [Fact]
    public async Task List_requires_authentication()
    {
        using var database = new PartyCrudTestDatabase();
        database.AddSupplier("Secret");

        var result = await database.SupplierService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(SupplierListFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task List_requires_view_permission()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("supplier-list-denied", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView: false, canCreate: true, canModify: true);
        database.AddSupplier("Secret");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(SupplierListFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Employee_with_view_permission_can_list()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("supplier-list-allowed", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView: true, canCreate: false, canModify: false);
        database.AddSupplier("Visible");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Equal(["Visible"], result.Items.Select(item => item.Name));
    }

    [Theory]
    [InlineData(0, 50, SupplierListFailureReason.InvalidPageNumber)]
    [InlineData(-1, 50, SupplierListFailureReason.InvalidPageNumber)]
    [InlineData(1, 0, SupplierListFailureReason.InvalidPageSize)]
    [InlineData(1, 201, SupplierListFailureReason.InvalidPageSize)]
    [InlineData(2147483647, 200, SupplierListFailureReason.InvalidPageNumber)]
    public async Task Invalid_pagination_returns_controlled_failure(
        int pageNumber,
        int pageSize,
        SupplierListFailureReason expected)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"supplier-page-{pageNumber}-{pageSize}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.GetPageAsync(new(pageNumber, pageSize));

        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureReason);
    }

    [Fact]
    public async Task Pagination_reports_totals_and_uses_name_then_id_order()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-pagination", UserRole.Responsable);
        var zulu = database.AddSupplier("Zulu");
        var atlasOne = database.AddSupplier("Atlas");
        var atlasTwo = database.AddSupplier("Atlas");
        database.AddSupplier("Bravo");
        await database.AuthenticateAsync(responsible);

        var first = await database.SupplierService.GetPageAsync(new(PageSize: 2));
        var second = await database.SupplierService.GetPageAsync(new(PageNumber: 2, PageSize: 2));

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
        var responsible = database.AddUser("supplier-search", UserRole.Responsable);
        database.AddSupplier("Atlas Supplier", "SUP-001");
        var companyOnly = database.AddSupplier("Cedar", "SUP-XYZ");
        companyOnly.Company = "CompanyNeedle";
        database.Context.SaveChanges();
        await database.AuthenticateAsync(responsible);

        var byName = await database.SupplierService.GetPageAsync(new(SearchText: "  Atlas "));
        var byIdentifier = await database.SupplierService.GetPageAsync(new(SearchText: " XYZ "));
        var byCompany = await database.SupplierService.GetPageAsync(new(SearchText: "CompanyNeedle"));
        var whitespace = await database.SupplierService.GetPageAsync(new(SearchText: "   "));

        Assert.Equal(["Atlas Supplier"], byName.Items.Select(item => item.Name));
        Assert.Equal(["Cedar"], byIdentifier.Items.Select(item => item.Name));
        Assert.Empty(byCompany.Items);
        Assert.Equal(2, whitespace.TotalCount);
    }

    [Fact]
    public async Task List_projects_only_expected_fields_without_tracking_suppliers()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-projection", UserRole.Responsable);
        var supplier = database.AddSupplier("Projected", "SUP-P");
        supplier.Phone = "0600";
        supplier.Company = "Company";
        supplier.Address = "Hidden address";
        supplier.Observation = "Hidden note";
        database.Context.SaveChanges();
        database.Context.ChangeTracker.Clear();
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.SupplierService.GetPageAsync(new());

        var item = Assert.Single(result.Items);
        Assert.Equal(["Id", "Name", "Phone", "Company", "Identifier"],
            typeof(SupplierListItem).GetProperties().Select(property => property.Name));
        Assert.Equal("0600", item.Phone);
        Assert.Equal("Company", item.Company);
        Assert.Empty(database.Context.ChangeTracker.Entries<Supplier>());
    }

    [Fact]
    public async Task List_propagates_cancellation()
    {
        using var database = new PartyCrudTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierService.GetPageAsync(new(), cancellation.Token));
    }
}

public sealed class SupplierDetailServiceTests
{
    [Fact]
    public async Task Detail_requires_authentication()
    {
        using var database = new PartyCrudTestDatabase();

        var result = await database.SupplierService.GetByIdAsync(1);

        Assert.False(result.Success);
        Assert.Equal(SupplierDetailFailureReason.NotAuthenticated, result.FailureReason);
    }

    [Fact]
    public async Task Detail_requires_view_permission()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("supplier-detail-denied", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView: false, canCreate: true, canModify: true);
        var supplier = database.AddSupplier("Secret");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierService.GetByIdAsync(supplier.Id);

        Assert.False(result.Success);
        Assert.Equal(SupplierDetailFailureReason.Forbidden, result.FailureReason);
    }

    [Fact]
    public async Task Employee_with_view_permission_can_read_detail()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("supplier-detail-allowed", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView: true, canCreate: false, canModify: false);
        var supplier = database.AddSupplier("Visible");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierService.GetByIdAsync(supplier.Id);

        Assert.True(result.Success);
        Assert.Equal(supplier.Id, result.Item!.Id);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Detail_rejects_invalid_id(long supplierId)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"supplier-detail-{supplierId}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.GetByIdAsync(supplierId);

        Assert.Equal(SupplierDetailFailureReason.InvalidSupplierId, result.FailureReason);
    }

    [Fact]
    public async Task Detail_returns_not_found()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-detail-missing", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.GetByIdAsync(999999);

        Assert.Equal(SupplierDetailFailureReason.NotFound, result.FailureReason);
    }

    [Fact]
    public async Task Detail_projects_all_current_fields_and_timestamps_without_tracking()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-detail", UserRole.Responsable);
        var createdAt = database.Clock.UtcNow.AddDays(-2);
        var supplier = database.AddSupplier("Detail Supplier", "SUP-D", createdAt);
        supplier.Phone = "0600";
        supplier.Address = "Rabat";
        supplier.Company = "Detail Co";
        supplier.Observation = "Note";
        supplier.UpdatedAtUtc = createdAt.AddDays(1);
        database.Context.SaveChanges();
        var supplierId = supplier.Id;
        database.Context.ChangeTracker.Clear();
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.SupplierService.GetByIdAsync(supplierId);

        Assert.True(result.Success);
        var item = Assert.IsType<SupplierDetailItem>(result.Item);
        Assert.Equal(supplierId, item.Id);
        Assert.Equal("Detail Supplier", item.Name);
        Assert.Equal("0600", item.Phone);
        Assert.Equal("Rabat", item.Address);
        Assert.Equal("Detail Co", item.Company);
        Assert.Equal("SUP-D", item.Identifier);
        Assert.Equal("Note", item.Observation);
        Assert.Equal(createdAt, item.CreatedAtUtc);
        Assert.Equal(createdAt.AddDays(1), item.UpdatedAtUtc);
        Assert.Empty(database.Context.ChangeTracker.Entries<Supplier>());
    }
}

public sealed class SupplierUpdateServiceTests
{
    [Fact]
    public async Task Update_requires_authentication()
    {
        using var database = new PartyCrudTestDatabase();

        var result = await database.SupplierService.UpdateAsync(new(1, "Supplier"));

        Assert.Equal(UpdateSupplierFailureReason.NotAuthenticated, result.FailureReason);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Effective_modify_permission_requires_view_and_modify(
        bool canView,
        bool canModify)
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser($"supplier-modify-{canView}-{canModify}", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView, canCreate: false, canModify);
        var supplier = database.AddSupplier("Original");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierService.UpdateAsync(new(supplier.Id, "Changed"));

        Assert.Equal(UpdateSupplierFailureReason.NotAuthorized, result.FailureReason);
        Assert.Equal("Original", database.Context.Suppliers.Single(item => item.Id == supplier.Id).Name);
    }

    [Fact]
    public async Task Employee_with_effective_modify_permission_can_update()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("supplier-modify-allowed", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView: true, canCreate: false, canModify: true);
        var supplier = database.AddSupplier("Original");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierService.UpdateAsync(new(supplier.Id, "Changed"));

        Assert.True(result.Success);
        Assert.Equal("Changed", database.Context.Suppliers.Single(item => item.Id == supplier.Id).Name);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Update_rejects_invalid_id(long supplierId)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"supplier-update-{supplierId}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.UpdateAsync(new(supplierId, "Supplier"));

        Assert.Equal(UpdateSupplierFailureReason.InvalidSupplierId, result.FailureReason);
    }

    [Fact]
    public async Task Update_returns_not_found()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-update-missing", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.UpdateAsync(new(999999, "Supplier"));

        Assert.Equal(UpdateSupplierFailureReason.NotFound, result.FailureReason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Update_rejects_invalid_name(string? name)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"supplier-update-name-{Guid.NewGuid():N}", UserRole.Responsable);
        var supplier = database.AddSupplier("Original");
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.UpdateAsync(new(supplier.Id, name!));

        Assert.Equal(UpdateSupplierFailureReason.InvalidName, result.FailureReason);
        Assert.Equal("Original", database.Context.Suppliers.Single(item => item.Id == supplier.Id).Name);
    }

    [Fact]
    public async Task Update_normalizes_all_fields_preserves_created_and_sets_updated_time()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-update-all", UserRole.Responsable);
        var createdAt = database.Clock.UtcNow.AddYears(-1);
        var supplier = database.AddSupplier("Original", createdAtUtc: createdAt);
        database.Clock.UtcNow = database.Clock.UtcNow.AddHours(3);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.UpdateAsync(new(
            supplier.Id,
            "  Updated  ",
            "  0600  ",
            "  Rabat  ",
            "  Company  ",
            "  SUP-U  ",
            "   "));

        Assert.True(result.Success);
        var updated = database.Context.Suppliers.Single(item => item.Id == supplier.Id);
        Assert.Equal("Updated", updated.Name);
        Assert.Equal("0600", updated.Phone);
        Assert.Equal("Rabat", updated.Address);
        Assert.Equal("Company", updated.Company);
        Assert.Equal("SUP-U", updated.Identifier);
        Assert.Null(updated.Observation);
        Assert.Equal(createdAt, updated.CreatedAtUtc);
        Assert.Equal(database.Clock.UtcNow, updated.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(SupplierUseKind.Purchase)]
    [InlineData(SupplierUseKind.SupplierPayment)]
    [InlineData(SupplierUseKind.InitialSupplierPayable)]
    public async Task Used_supplier_can_be_renamed(SupplierUseKind useKind)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"supplier-used-{useKind}", UserRole.Responsable);
        var supplier = database.AddSupplier("Original");
        database.AddSupplierUse(supplier, useKind, responsible);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.UpdateAsync(new(supplier.Id, "Renamed"));

        Assert.True(result.Success);
        Assert.Equal("Renamed", database.Context.Suppliers.Single(item => item.Id == supplier.Id).Name);
        Assert.Equal(1, database.CountSupplierUses(useKind, supplier.Id));
    }

    [Fact]
    public async Task Update_allows_duplicate_name_and_identifier()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-update-duplicate", UserRole.Responsable);
        database.AddSupplier("Duplicate", "DUP");
        var supplier = database.AddSupplier("Other", "OTHER");
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.UpdateAsync(new(supplier.Id, "Duplicate", Identifier: "DUP"));

        Assert.True(result.Success);
        Assert.Equal(2, database.Context.Suppliers.Count(item => item.Name == "Duplicate" && item.Identifier == "DUP"));
    }

    [Fact]
    public async Task Update_propagates_cancellation_without_changes()
    {
        using var database = new PartyCrudTestDatabase();
        var supplier = database.AddSupplier("Original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierService.UpdateAsync(new(supplier.Id, "Changed"), cancellation.Token));
        Assert.Equal("Original", database.Context.Suppliers.Single(item => item.Id == supplier.Id).Name);
    }
}

public sealed class SupplierDeleteServiceTests
{
    [Fact]
    public async Task Delete_requires_authentication()
    {
        using var database = new PartyCrudTestDatabase();
        var supplier = database.AddSupplier("Supplier");

        var result = await database.SupplierService.DeleteAsync(supplier.Id);

        Assert.Equal(DeleteSupplierFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Single(database.Context.Suppliers);
    }

    [Fact]
    public async Task Employee_with_modify_is_still_refused_permanent_delete()
    {
        using var database = new PartyCrudTestDatabase();
        var employee = database.AddUser("supplier-delete-employee", UserRole.Employe);
        database.Grant(employee, PermissionModule.Suppliers, canView: true, canCreate: true, canModify: true);
        var supplier = database.AddSupplier("Supplier");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierService.DeleteAsync(supplier.Id);

        Assert.Equal(DeleteSupplierFailureReason.NotAuthorized, result.FailureReason);
        Assert.True(database.Context.Suppliers.Any(item => item.Id == supplier.Id));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task Delete_rejects_invalid_id(long supplierId)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"supplier-delete-{supplierId}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.DeleteAsync(supplierId);

        Assert.Equal(DeleteSupplierFailureReason.InvalidSupplierId, result.FailureReason);
    }

    [Fact]
    public async Task Delete_returns_not_found()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-delete-missing", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.DeleteAsync(999999);

        Assert.Equal(DeleteSupplierFailureReason.NotFound, result.FailureReason);
    }

    [Fact]
    public async Task Responsable_can_delete_unused_supplier()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("supplier-delete-owner", UserRole.Responsable);
        var supplier = database.AddSupplier("Unused");
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.DeleteAsync(supplier.Id);

        Assert.True(result.Success);
        Assert.Equal(supplier.Id, result.SupplierId);
        Assert.False(database.Context.Suppliers.Any(item => item.Id == supplier.Id));
    }

    [Theory]
    [InlineData(SupplierUseKind.Purchase)]
    [InlineData(SupplierUseKind.SupplierPayment)]
    [InlineData(SupplierUseKind.InitialSupplierPayable)]
    public async Task Used_supplier_is_not_deleted_and_fk_protection_remains(
        SupplierUseKind useKind)
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser($"supplier-delete-used-{useKind}", UserRole.Responsable);
        var supplier = database.AddSupplier("Used");
        database.AddSupplierUse(supplier, useKind, responsible);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierService.DeleteAsync(supplier.Id);

        Assert.Equal(DeleteSupplierFailureReason.InUse, result.FailureReason);
        Assert.True(database.Context.Suppliers.Any(item => item.Id == supplier.Id));
        Assert.Equal(1, database.CountSupplierUses(useKind, supplier.Id));

        database.Context.ChangeTracker.Clear();
        var directDelete = database.Context.Suppliers.Single(item => item.Id == supplier.Id);
        database.Context.Suppliers.Remove(directDelete);
        await Assert.ThrowsAsync<DbUpdateException>(() => database.Context.SaveChangesAsync());
        database.Context.ChangeTracker.Clear();
        Assert.True(database.Context.Suppliers.Any(item => item.Id == supplier.Id));
        Assert.Equal(1, database.CountSupplierUses(useKind, supplier.Id));
    }

    [Fact]
    public async Task Delete_propagates_cancellation_without_changes()
    {
        using var database = new PartyCrudTestDatabase();
        var supplier = database.AddSupplier("Supplier");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierService.DeleteAsync(supplier.Id, cancellation.Token));
        Assert.True(database.Context.Suppliers.Any(item => item.Id == supplier.Id));
    }
}
