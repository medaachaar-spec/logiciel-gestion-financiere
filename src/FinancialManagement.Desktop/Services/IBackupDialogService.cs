namespace FinancialManagement.Desktop.Services;

public interface IBackupDialogService
{
    string? SelectExternalDirectory();

    string? SelectBackupFile();

    bool ConfirmRestore(string backupFilePath);

    void ShowRestoreSucceeded();
}
