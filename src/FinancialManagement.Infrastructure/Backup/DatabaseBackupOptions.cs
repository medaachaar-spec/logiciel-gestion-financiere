namespace FinancialManagement.Infrastructure.Backup;

public sealed class DatabaseBackupOptions
{
    public DatabaseBackupOptions()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FinancialManagement",
            "Backups"))
    {
    }

    public DatabaseBackupOptions(string localBackupDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localBackupDirectory);
        LocalBackupDirectory = Path.GetFullPath(localBackupDirectory);
    }

    public string LocalBackupDirectory { get; }
}
