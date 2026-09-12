using FinancialManagement.Desktop.Configuration;

namespace FinancialManagement.Desktop.Tests;

public sealed class WpfTestHostSafetyTests
{
    [Fact]
    public void Wpf_test_application_cannot_run_production_startup()
    {
        WpfTestHost.Invoke(() =>
        {
            var app = Assert.IsType<FinancialManagement.Desktop.App>(System.Windows.Application.Current);

            Assert.True(app.StartupCallbackObserved);
            Assert.False(app.RunsProductionStartup);
        });
    }

    [Fact]
    public void Production_database_path_remains_explicit_and_unchanged()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "FinancialManagement",
            "Data",
            "FinancialManagement.db");

        Assert.Equal(Path.GetFullPath(expected), new ApplicationPaths().DatabasePath);
    }
}
