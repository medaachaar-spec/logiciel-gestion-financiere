using System.IO;

namespace FinancialManagement.Desktop.Configuration;

public sealed class ApplicationPaths
{
    private const string ApplicationDirectoryName = "FinancialManagement";
    private const string DataDirectoryName = "Data";
    private const string DatabaseFileName = "FinancialManagement.db";

    public ApplicationPaths()
    {
        var commonApplicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData);

        if (string.IsNullOrWhiteSpace(commonApplicationData))
        {
            throw new InvalidOperationException(
                "The common application data directory is unavailable.");
        }

        ApplicationDataRoot = Path.GetFullPath(
            Path.Combine(commonApplicationData, ApplicationDirectoryName));
        DataDirectory = Path.Combine(ApplicationDataRoot, DataDirectoryName);
        DatabasePath = Path.Combine(DataDirectory, DatabaseFileName);
    }

    public string ApplicationDataRoot { get; }

    public string DataDirectory { get; }

    public string DatabasePath { get; }
}
