namespace FinancialManagement.Infrastructure.Backup;

internal sealed class DatabaseMaintenanceLock
{
    public SemaphoreSlim Semaphore { get; } = new(1, 1);
}
