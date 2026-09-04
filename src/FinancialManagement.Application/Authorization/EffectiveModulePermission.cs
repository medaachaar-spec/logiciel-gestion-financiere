namespace FinancialManagement.Application.Authorization;

public sealed record EffectiveModulePermission(
    bool CanView,
    bool CanCreate,
    bool CanModify);
