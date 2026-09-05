using System.Configuration;
using System.Windows;
using System.Diagnostics;
using System.IO;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Desktop.Configuration;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Desktop;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ServiceProvider serviceProvider;

        try
        {
            var paths = new ApplicationPaths();
            Directory.CreateDirectory(paths.DataDirectory);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = paths.DatabasePath
            }.ToString();

            serviceProvider = BuildServiceProvider(paths, connectionString);
            _serviceProvider = serviceProvider;
            await MigrateDatabaseAsync(serviceProvider);
        }
        catch (Exception exception)
        {
            Trace.TraceError("Application data initialization failed: {0}", exception);
            MessageBox.Show(
                "Impossible d'initialiser les données de l'application.",
                "Démarrage impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        try
        {
            var currentUserSession = serviceProvider.GetRequiredService<ICurrentUserSession>();

            while (true)
            {
                if (!ShowLogin(serviceProvider))
                {
                    Shutdown();
                    return;
                }

                var shellCompleted = await ShowShellAsync(serviceProvider);
                if (!shellCompleted)
                {
                    Shutdown();
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Application startup failed: {0}", exception);
            MessageBox.Show(
                "Impossible de démarrer l'application.",
                "Démarrage impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static ServiceProvider BuildServiceProvider(
        ApplicationPaths paths,
        string connectionString)
    {
        var services = new ServiceCollection();

        services.AddSingleton(paths);
        services.AddInfrastructure(connectionString);
        services.AddTransient<LoginViewModel>();
        services.AddTransient<LoginWindow>();
        services.AddScoped<DashboardOperationCoordinator>();
        services.AddTransient<DueAlertsViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient(provider => new Lazy<DashboardViewModel>(
            () => provider.GetRequiredService<DashboardViewModel>()));
        services.AddTransient<TreasuryViewModel>();
        services.AddTransient(provider => new Lazy<TreasuryViewModel>(
            () => provider.GetRequiredService<TreasuryViewModel>()));
        services.AddTransient<ShellViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static async Task MigrateDatabaseAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private bool ShowLogin(ServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var loginWindow = scope.ServiceProvider.GetRequiredService<LoginWindow>();
        MainWindow = loginWindow;

        var loginSucceeded = loginWindow.ShowDialog() == true;
        var currentUserSession = serviceProvider.GetRequiredService<ICurrentUserSession>();

        return loginSucceeded && currentUserSession.IsAuthenticated;
    }

    private async Task<bool> ShowShellAsync(ServiceProvider serviceProvider)
    {
        using var shellScope = serviceProvider.CreateScope();
        var mainWindow = shellScope.ServiceProvider.GetRequiredService<MainWindow>();
        var logoutRequested = false;

        void HandleLogoutRequested(object? sender, EventArgs args) => logoutRequested = true;

        mainWindow.LogoutRequested += HandleLogoutRequested;
        try
        {
            var currentUserSession = serviceProvider.GetRequiredService<ICurrentUserSession>();
            if (!currentUserSession.IsAuthenticated || !await mainWindow.InitializeAsync())
            {
                return false;
            }

            if (!currentUserSession.IsAuthenticated)
            {
                return false;
            }

            MainWindow = mainWindow;
            mainWindow.ShowDialog();
            return logoutRequested && !currentUserSession.IsAuthenticated;
        }
        finally
        {
            mainWindow.LogoutRequested -= HandleLogoutRequested;
            await mainWindow.StopAsync();
        }
    }
}
