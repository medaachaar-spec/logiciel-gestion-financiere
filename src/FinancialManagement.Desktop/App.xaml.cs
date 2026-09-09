using System.Configuration;
using System.Windows;
using System.Diagnostics;
using System.IO;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Backup;
using FinancialManagement.Application.Initialization;
using FinancialManagement.Desktop.Configuration;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Domain.Enums;
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
        FinancialDatabaseStartupResult startupResult;

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
            startupResult = await MigrateDatabaseAsync(serviceProvider);
            if (startupResult.DailyBackup is { Success: false } dailyBackup)
            {
                Trace.TraceWarning(
                    "Automatic daily backup failed for business date {0}: {1}.",
                    dailyBackup.LocalBusinessDate,
                    dailyBackup.FailureReason);
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Application data initialization failed: {0}", exception);
            if (exception is DatabaseMigrationWithBackupException migrationFailure)
            {
                Trace.TraceError(
                    "Migration recovery backup is available at {0}.",
                    migrationFailure.RecoveryBackupPath);
            }
            MessageBox.Show(
                GetDataInitializationErrorMessage(exception),
                "Démarrage impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        try
        {
            while (true)
            {
                var initializationState = await GetInitializationStateAsync(serviceProvider);
                if (initializationState.RequiresBootstrap)
                {
                    if (!await ShowFirstRunAsync(serviceProvider))
                    {
                        Shutdown();
                        return;
                    }

                    continue;
                }

                if (!ShowLogin(serviceProvider))
                {
                    Shutdown();
                    return;
                }

                var currentUserSession = serviceProvider.GetRequiredService<ICurrentUserSession>();
                if (startupResult.DailyBackup is { Success: false } &&
                    currentUserSession.Role == UserRole.Responsable)
                {
                    MessageBox.Show(
                        "La sauvegarde automatique du jour n'a pas pu être créée. "
                            + "Les données restent disponibles ; créez une sauvegarde depuis Administration.",
                        "Sauvegarde automatique",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    startupResult = startupResult with { DailyBackup = null };
                }
                initializationState = await GetInitializationStateAsync(serviceProvider);
                if (!initializationState.IsInitializationComplete)
                {
                    if (currentUserSession.Role != UserRole.Responsable)
                    {
                        MessageBox.Show(
                            "L'initialisation doit être terminée par un Responsable avant d'accéder à l'application.",
                            "Configuration requise",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        Logout(serviceProvider);
                        continue;
                    }

                    if (!await ShowInitializationAsync(serviceProvider))
                    {
                        Logout(serviceProvider);
                        continue;
                    }
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
        services.AddTransient<BootstrapViewModel>();
        services.AddTransient<FirstRunWindow>();
        services.AddTransient<InitializationViewModel>();
        services.AddTransient<InitializationWindow>();
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
        services.AddTransient<SalesViewModel>();
        services.AddTransient(provider => new Lazy<SalesViewModel>(
            () => provider.GetRequiredService<SalesViewModel>()));
        services.AddTransient<PurchasesViewModel>();
        services.AddTransient(provider => new Lazy<PurchasesViewModel>(
            () => provider.GetRequiredService<PurchasesViewModel>()));
        services.AddTransient<ExpensesViewModel>();
        services.AddTransient(provider => new Lazy<ExpensesViewModel>(
            () => provider.GetRequiredService<ExpensesViewModel>()));
        services.AddTransient<ClientsViewModel>();
        services.AddTransient(provider => new Lazy<ClientsViewModel>(
            () => provider.GetRequiredService<ClientsViewModel>()));
        services.AddTransient<SuppliersViewModel>();
        services.AddTransient(provider => new Lazy<SuppliersViewModel>(
            () => provider.GetRequiredService<SuppliersViewModel>()));
        services.AddTransient<CustomerPaymentsViewModel>();
        services.AddTransient(provider => new Lazy<CustomerPaymentsViewModel>(
            () => provider.GetRequiredService<CustomerPaymentsViewModel>()));
        services.AddTransient<SupplierPaymentsViewModel>();
        services.AddTransient(provider => new Lazy<SupplierPaymentsViewModel>(
            () => provider.GetRequiredService<SupplierPaymentsViewModel>()));
        services.AddTransient<InitialCustomerReceivablesViewModel>();
        services.AddTransient<InitialSupplierPayablesViewModel>();
        services.AddSingleton<IBackupDialogService, BackupDialogService>();
        services.AddTransient<BackupViewModel>();
        services.AddTransient<AdministrationViewModel>();
        services.AddTransient(provider => new Lazy<AdministrationViewModel>(
            () => provider.GetRequiredService<AdministrationViewModel>()));
        services.AddTransient<ShellViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static async Task<FinancialDatabaseStartupResult> MigrateDatabaseAsync(
        ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        var backupService = scope.ServiceProvider.GetRequiredService<IStartupDatabaseBackupService>();
        return await FinancialDatabaseStartup.InitializeAsync(dbContext, backupService);
    }

    private static string GetDataInitializationErrorMessage(Exception exception) => exception switch
    {
        FinancialStorageIntegrityException =>
            "La base de données contient des données financières dans un format invalide. "
                + "Le démarrage a été bloqué afin d’éviter une altération des montants.",
        PreMigrationBackupException =>
            "La mise à jour a été arrêtée car la sauvegarde de sécurité préalable n'a pas pu être créée. "
                + "La base n'a pas été migrée.",
        DatabaseMigrationWithBackupException migrationFailure =>
            "La mise à jour de la base n'a pas pu être terminée. Une sauvegarde de récupération est disponible ici :"
                + Environment.NewLine + migrationFailure.RecoveryBackupPath,
        _ => "Impossible d'initialiser les données de l'application."
    };

    private bool ShowLogin(ServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var loginWindow = scope.ServiceProvider.GetRequiredService<LoginWindow>();
        MainWindow = loginWindow;

        var loginSucceeded = loginWindow.ShowDialog() == true;
        var currentUserSession = serviceProvider.GetRequiredService<ICurrentUserSession>();

        return loginSucceeded && currentUserSession.IsAuthenticated;
    }

    private static async Task<ApplicationInitializationState> GetInitializationStateAsync(
        ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<IApplicationInitializationService>()
            .GetStateAsync();
    }

    private async Task<bool> ShowFirstRunAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var window = scope.ServiceProvider.GetRequiredService<FirstRunWindow>();
        MainWindow = window;
        var completed = window.ShowDialog() == true;
        await window.StopAsync();
        return completed;
    }

    private async Task<bool> ShowInitializationAsync(ServiceProvider serviceProvider)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var window = scope.ServiceProvider.GetRequiredService<InitializationWindow>();
        if (await window.PrepareAsync())
        {
            await window.StopAsync();
            return true;
        }

        var currentUserSession = serviceProvider.GetRequiredService<ICurrentUserSession>();
        if (!currentUserSession.IsAuthenticated || currentUserSession.Role != UserRole.Responsable)
        {
            await window.StopAsync();
            return false;
        }

        MainWindow = window;
        var completed = window.ShowDialog() == true;
        await window.StopAsync();
        return completed;
    }

    private static void Logout(ServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuthenticationService>().Logout();
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
