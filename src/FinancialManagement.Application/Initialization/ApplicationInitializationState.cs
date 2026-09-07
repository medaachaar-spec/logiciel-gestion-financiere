namespace FinancialManagement.Application.Initialization;

/// <summary>Derived from canonical data; optional initial obligations do not gate completion.</summary>
public sealed record ApplicationInitializationState(
    bool RequiresBootstrap, bool HasResponsibleUser, bool HasOpeningBalances)
{
    // HasResponsibleUser means an active Responsable able to complete setup.
    public bool IsInitializationComplete => !RequiresBootstrap && HasResponsibleUser && HasOpeningBalances;
}
