namespace FinancialManagement.Infrastructure.Backup;

internal sealed class DatabaseRestoreTestHook
{
    public Action<string>? AfterAtomicReplace { get; set; }

    public void InvokeAfterAtomicReplace(string liveDatabasePath) =>
        AfterAtomicReplace?.Invoke(liveDatabasePath);
}
