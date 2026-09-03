using System.Reflection;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Tests;

public sealed class DomainModelTests
{
    private static readonly Type[] EntityTypes = typeof(User).Assembly
        .GetTypes()
        .Where(type => type.IsClass && type.IsPublic && type.Namespace == typeof(User).Namespace)
        .ToArray();

    [Fact]
    public void UserRole_contains_only_validated_roles()
    {
        Assert.Equal(
            [nameof(UserRole.Responsable), nameof(UserRole.Employe)],
            Enum.GetNames<UserRole>());
    }

    [Fact]
    public void PermissionModule_contains_only_validated_modules()
    {
        Assert.Equal(
            [
                nameof(PermissionModule.Sales),
                nameof(PermissionModule.Purchases),
                nameof(PermissionModule.Expenses),
                nameof(PermissionModule.Clients),
                nameof(PermissionModule.Suppliers),
                nameof(PermissionModule.CustomerPayments),
                nameof(PermissionModule.SupplierPayments)
            ],
            Enum.GetNames<PermissionModule>());
    }

    [Fact]
    public void PaymentMethod_contains_only_validated_methods()
    {
        Assert.Equal(
            [nameof(PaymentMethod.Cash), nameof(PaymentMethod.BankTransfer)],
            Enum.GetNames<PaymentMethod>());
    }

    [Fact]
    public void TreasuryAccount_contains_only_validated_accounts()
    {
        Assert.Equal(
            [nameof(TreasuryAccount.Cash), nameof(TreasuryAccount.Bank)],
            Enum.GetNames<TreasuryAccount>());
    }

    [Fact]
    public void Domain_contains_exactly_the_validated_entities()
    {
        string[] expectedEntityNames =
        [
            nameof(ApplicationSettings),
            nameof(Client),
            nameof(CustomerPayment),
            nameof(CustomerPaymentAllocation),
            nameof(Expense),
            nameof(ExpenseCategory),
            nameof(InitialCustomerReceivable),
            nameof(InitialSupplierPayable),
            nameof(Purchase),
            nameof(Sale),
            nameof(Supplier),
            nameof(SupplierPayment),
            nameof(SupplierPaymentAllocation),
            nameof(TreasuryOpeningBalance),
            nameof(TreasuryTransfer),
            nameof(User),
            nameof(UserPermission)
        ];

        var actualEntityNames = EntityTypes
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedEntityNames.Order(StringComparer.Ordinal), actualEntityNames);
    }

    [Fact]
    public void Monetary_properties_use_only_minor_units_stored_as_long()
    {
        var properties = EntityTypes
            .SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            .ToArray();
        var monetaryProperties = properties
            .Where(property => property.Name.EndsWith("AmountMinor", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(monetaryProperties);
        Assert.All(monetaryProperties, property => Assert.Equal(typeof(long), property.PropertyType));
        Assert.DoesNotContain(
            properties,
            property => IsForbiddenNumericType(property.PropertyType));
    }

    private static bool IsForbiddenNumericType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType == typeof(decimal)
            || underlyingType == typeof(double)
            || underlyingType == typeof(float);
    }
}
