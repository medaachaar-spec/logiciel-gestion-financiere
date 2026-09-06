using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Backup;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using FinancialManagement.Infrastructure.Authentication;
using FinancialManagement.Infrastructure.Authorization;
using FinancialManagement.Infrastructure.Backup;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed partial class DatabaseBackupServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Backup_RejectsSessionChangeWhileWaitingForMaintenanceLock(bool anotherResponsible)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var session = (CurrentUserSession)fixture.Session;

        await fixture.MaintenanceLock.Semaphore.WaitAsync();
        Task<CreateDatabaseBackupResult> pending;
        try
        {
            pending = fixture.Service.CreateLocalBackupAsync();
            Assert.False(pending.IsCompleted); // The held semaphore is the first incomplete await.
            var otherId = session.UserId!.Value + 1;
            session.Clear();
            if (anotherResponsible)
                session.SetUser(new AuthenticatedUser(otherId, "Other responsible", "other", UserRole.Responsable));
        }
        finally
        {
            fixture.MaintenanceLock.Semaphore.Release();
        }

        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        var publishedArtifacts = Directory.Exists(fixture.BackupDirectory)
            ? Directory.GetFiles(fixture.BackupDirectory, "*.fmbak").Length
            : 0;
        output.WriteLine(
            $"anotherResponsible={anotherResponsible}; Success={result.Success}; " +
            $"BackupPath={result.BackupPath}; PublishedArtifacts={publishedArtifacts}");

        Assert.False(result.Success);
        Assert.Equal(
            anotherResponsible ? DatabaseBackupFailureReason.Forbidden : DatabaseBackupFailureReason.NotAuthenticated,
            result.FailureReason);
        Assert.Null(result.BackupPath);
        Assert.Equal(0, publishedArtifacts);
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Fact]
    public async Task Backup_SameAuthorizedUserAfterLockWaitSucceedsAndArtifactIsRestorable()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);

        await fixture.MaintenanceLock.Semaphore.WaitAsync();
        Task<CreateDatabaseBackupResult> pending;
        try
        {
            pending = fixture.Service.CreateLocalBackupAsync();
            Assert.False(pending.IsCompleted);
        }
        finally
        {
            fixture.MaintenanceLock.Semaphore.Release();
        }

        var backup = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(backup.Success);
        Assert.True(File.Exists(backup.BackupPath));
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);

        var restore = await fixture.Service.RestoreAsync(new(backup.BackupPath!)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(restore.Success);
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Theory]
    [InlineData("deny")]
    [InlineData("exception")]
    [InlineData("cancel")]
    [InlineData("authorization-canceled")]
    [InlineData("clear")]
    [InlineData("switch-responsable")]
    [InlineData("switch-employee")]
    public async Task Backup_PostLockAuthorizationFailureDoesNotPublishAndReleasesLock(string fault)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        using var cancellation = new CancellationTokenSource();
        var probe = new MaintenanceAuthorizationProbe(fixture.Services.GetRequiredService<IAuthorizationService>(), call =>
            call == 2 ? ChangeAuthorization(fixture, fault, cancellation) : null);
        var service = WithAuthorization(fixture, probe);

        await fixture.MaintenanceLock.Semaphore.WaitAsync();
        Task<CreateDatabaseBackupResult> pending;
        try
        {
            pending = service.CreateLocalBackupAsync(cancellation.Token);
            Assert.False(pending.IsCompleted);
            Assert.Equal(1, probe.Calls);
        }
        finally
        {
            fixture.MaintenanceLock.Semaphore.Release();
        }

        if (fault is "cancel" or "authorization-canceled")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pending.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        else
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(result.Success);
            Assert.Equal(
                fault == "clear" ? DatabaseBackupFailureReason.NotAuthenticated : DatabaseBackupFailureReason.Forbidden,
                result.FailureReason);
            Assert.Null(result.BackupPath);
        }

        Assert.Equal(2, probe.Calls);
        AssertNoPublishedOrTemporaryBackupArtifacts(fixture);
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);

        await fixture.AuthenticateAsync("responsable", "Password-123!");
        var next = await fixture.Service.CreateLocalBackupAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(next.Success);
        Assert.True(File.Exists(next.BackupPath));
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Fact]
    public async Task Backup_CancellationWhileWaitingDoesNotReleaseAnotherOwnersLock()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        using var cancellation = new CancellationTokenSource();

        await fixture.MaintenanceLock.Semaphore.WaitAsync();
        try
        {
            var pending = fixture.Service.CreateLocalBackupAsync(cancellation.Token);
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pending.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(0, fixture.MaintenanceLock.Semaphore.CurrentCount);
            AssertNoPublishedOrTemporaryBackupArtifacts(fixture);
        }
        finally
        {
            fixture.MaintenanceLock.Semaphore.Release();
        }

        var next = await fixture.Service.CreateLocalBackupAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(next.Success);
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Theory]
    [InlineData("deny")]
    [InlineData("exception")]
    [InlineData("cancel")]
    [InlineData("authorization-canceled")]
    [InlineData("clear")]
    [InlineData("switch-responsable")]
    [InlineData("switch-employee")]
    public async Task Backup_FinalAuthorizationFailureCleansTemporaryArtifactsAndDoesNotPublish(string fault)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        using var cancellation = new CancellationTokenSource();
        var partialObservedAtGate = false;
        var workspaceObservedAtGate = false;
        var probe = new MaintenanceAuthorizationProbe(fixture.Services.GetRequiredService<IAuthorizationService>(), call =>
        {
            if (call != 3)
                return null;

            partialObservedAtGate = Directory.GetFiles(fixture.BackupDirectory, ".fmbackup-*.partial").Length == 1;
            workspaceObservedAtGate = Directory.GetDirectories(fixture.BackupDirectory, ".fmbackup-*.tmp").Length == 1;
            return ChangeAuthorization(fixture, fault, cancellation);
        });

        var pending = WithAuthorization(fixture, probe).CreateLocalBackupAsync(cancellation.Token);
        if (fault is "cancel" or "authorization-canceled")
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pending.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        else
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(result.Success);
            Assert.Equal(
                fault == "clear" ? DatabaseBackupFailureReason.NotAuthenticated : DatabaseBackupFailureReason.Forbidden,
                result.FailureReason);
            Assert.Null(result.BackupPath);
        }

        Assert.Equal(3, probe.Calls);
        Assert.True(partialObservedAtGate);
        Assert.True(workspaceObservedAtGate);
        AssertNoPublishedOrTemporaryBackupArtifacts(fixture);
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Fact]
    public async Task Backup_QueuedBehindAnotherBackupRejectsInvalidatedInitiatingSession()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var secondSession = new CurrentUserSession();
        secondSession.SetUser(new AuthenticatedUser(42, "Second responsible", "second", UserRole.Responsable));
        var secondAuthorization = new AuthorizationService(fixture.Context, secondSession);
        var secondService = WithSession(fixture, secondSession, secondAuthorization);
        Task<CreateDatabaseBackupResult>? queued = null;
        var queuedWasWaiting = false;
        var firstProbe = new MaintenanceAuthorizationProbe(
            fixture.Services.GetRequiredService<IAuthorizationService>(),
            call =>
            {
                if (call != 3)
                    return null;

                queued = secondService.CreateLocalBackupAsync();
                queuedWasWaiting = !queued.IsCompleted;
                secondSession.Clear();
                return null;
            });

        var first = await WithAuthorization(fixture, firstProbe)
            .CreateLocalBackupAsync()
            .WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(first.Success);
        Assert.True(queuedWasWaiting);
        Assert.NotNull(queued);

        var second = await queued.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(second.Success);
        Assert.Equal(DatabaseBackupFailureReason.NotAuthenticated, second.FailureReason);
        Assert.Null(second.BackupPath);
        Assert.Single(Directory.GetFiles(fixture.BackupDirectory, "*.fmbak"));
        AssertNoTemporaryBackupArtifacts(fixture);
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_RejectsSessionChangeWhileWaitingForMaintenanceLock(bool anotherResponsible)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var backup = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var hashBefore = await HashFileAsync(fixture.LivePath);
        var swapped = false;
        fixture.RestoreTestHook.AfterAtomicReplace = _ => swapped = true;
        var session = (CurrentUserSession)fixture.Session;

        await fixture.MaintenanceLock.Semaphore.WaitAsync();
        Task<RestoreDatabaseBackupResult> pending;
        try
        {
            pending = fixture.Service.RestoreAsync(new(backup.BackupPath!));
            Assert.False(pending.IsCompleted); // The held semaphore is the first incomplete await.
            var otherId = session.UserId!.Value + 1;
            session.Clear();
            if (anotherResponsible)
                session.SetUser(new AuthenticatedUser(otherId, "Other responsible", "other", UserRole.Responsable));
        }
        finally
        {
            fixture.MaintenanceLock.Semaphore.Release();
        }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
        var hashAfter = await HashFileAsync(fixture.LivePath);
        output.WriteLine($"anotherResponsible={anotherResponsible}; Success={result.Success}; PreRestore={result.PreRestoreBackupPath is not null}; swap={swapped}; liveHashChanged={!hashBefore.SequenceEqual(hashAfter)}");
        Assert.False(result.Success);
        Assert.Equal(anotherResponsible ? DatabaseBackupFailureReason.Forbidden : DatabaseBackupFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Null(result.PreRestoreBackupPath);
        Assert.False(swapped);
        Assert.Equal(hashBefore, await HashFileAsync(fixture.LivePath));
        await fixture.AssertLiveMarkerAsync();
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Fact]
    public async Task Restore_SameAuthorizedUserAfterLockWaitSucceeds()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var backup = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        await fixture.MaintenanceLock.Semaphore.WaitAsync();
        Task<RestoreDatabaseBackupResult> pending;
        try
        {
            pending = fixture.Service.RestoreAsync(new(backup.BackupPath!));
            Assert.False(pending.IsCompleted);
        }
        finally { fixture.MaintenanceLock.Semaphore.Release(); }
        Assert.True((await pending.WaitAsync(TimeSpan.FromSeconds(30))).Success);
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Theory]
    [InlineData("deny")]
    [InlineData("exception")]
    [InlineData("cancel")]
    [InlineData("authorization-canceled")]
    [InlineData("clear")]
    [InlineData("switch-responsable")]
    [InlineData("switch-employee")]
    public async Task Restore_PostLockAuthorizationFailurePreservesLiveAndReleasesLock(string fault)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var backup = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var liveHash = await HashFileAsync(fixture.LivePath);
        var sourceHash = await HashFileAsync(backup.BackupPath!);
        var filesBefore = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var swap = false;
        fixture.RestoreTestHook.AfterAtomicReplace = _ => swap = true;
        using var cancellation = new CancellationTokenSource();
        var probe = new MaintenanceAuthorizationProbe(fixture.Services.GetRequiredService<IAuthorizationService>(), call =>
            call == 2 ? ChangeAuthorization(fixture, fault, cancellation) : null);
        var service = WithAuthorization(fixture, probe);

        await fixture.MaintenanceLock.Semaphore.WaitAsync();
        Task<RestoreDatabaseBackupResult> pending;
        try
        {
            pending = service.RestoreAsync(new(backup.BackupPath!), cancellation.Token);
            Assert.False(pending.IsCompleted);
            Assert.Equal(1, probe.Calls); // Initial authorization passed; now held at WaitAsync.
        }
        finally { fixture.MaintenanceLock.Semaphore.Release(); }

        if (fault is "cancel" or "authorization-canceled")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
        else
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(result.Success);
            Assert.Equal(fault == "clear" ? DatabaseBackupFailureReason.NotAuthenticated : DatabaseBackupFailureReason.Forbidden, result.FailureReason);
            Assert.Null(result.PreRestoreBackupPath);
            Assert.False(result.RestartRequired);
        }
        Assert.Equal(2, probe.Calls);
        Assert.False(swap);
        Assert.Equal(liveHash, await HashFileAsync(fixture.LivePath));
        Assert.Equal(sourceHash, await HashFileAsync(backup.BackupPath!));
        Assert.Equal(filesBefore, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray());
        await fixture.AssertLiveMarkerAsync();
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
        // Exercise the next actual Restore, not just CurrentCount or a mock lease.
        await fixture.AuthenticateAsync("responsable", "Password-123!");
        Assert.True((await fixture.Service.RestoreAsync(new(backup.BackupPath!)).WaitAsync(TimeSpan.FromSeconds(30))).Success);
        Assert.True(swap);
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Fact]
    public async Task Restore_CancellationWhileWaitingDoesNotReleaseAnotherOwnersLock()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var backup = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var hash = await HashFileAsync(fixture.LivePath);
        var swapped = false;
        fixture.RestoreTestHook.AfterAtomicReplace = _ => swapped = true;
        using var cancellation = new CancellationTokenSource();
        await fixture.MaintenanceLock.Semaphore.WaitAsync();
        try
        {
            var pending = fixture.Service.RestoreAsync(new(backup.BackupPath!), cancellation.Token);
            Assert.False(pending.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal(0, fixture.MaintenanceLock.Semaphore.CurrentCount);
        }
        finally { fixture.MaintenanceLock.Semaphore.Release(); }
        Assert.False(swapped);
        Assert.Equal(hash, await HashFileAsync(fixture.LivePath));
        await fixture.AssertLiveMarkerAsync();
        Assert.Single(Directory.GetFiles(fixture.BackupDirectory));
        Assert.Empty(Directory.GetFiles(fixture.Root, "FinancialManagement_Restore*"));
        Assert.True((await fixture.Service.RestoreAsync(new(backup.BackupPath!)).WaitAsync(TimeSpan.FromSeconds(30))).Success);
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    // 3: before PreRestore, 4: after PreRestore/before live maintenance,
    // 5: commit gate immediately before File.Replace. Faults are injected through
    // the existing authorization interface; no production timing hook is added.
    [Theory]
    [InlineData(3, "clear")]
    [InlineData(3, "switch-responsable")]
    [InlineData(3, "deny")]
    [InlineData(3, "exception")]
    [InlineData(3, "cancel")]
    [InlineData(4, "clear")]
    [InlineData(4, "switch-responsable")]
    [InlineData(4, "deny")]
    [InlineData(4, "exception")]
    [InlineData(4, "cancel")]
    [InlineData(5, "clear")]
    [InlineData(5, "switch-responsable")]
    [InlineData(5, "deny")]
    [InlineData(5, "exception")]
    [InlineData(5, "cancel")]
    public async Task Restore_LateAuthorizationFailurePreventsSwap(int gate, string fault)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var backup = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var liveHash = await HashFileAsync(fixture.LivePath);
        var sourceHash = await HashFileAsync(backup.BackupPath!);
        var swap = false;
        fixture.RestoreTestHook.AfterAtomicReplace = _ => swap = true;
        using var cancellation = new CancellationTokenSource();
        var stagingObservedAtGate = false;
        int? safetyCountAtGate = null;
        var probe = new MaintenanceAuthorizationProbe(fixture.Services.GetRequiredService<IAuthorizationService>(), call =>
        {
            if (call != gate) return null;
            stagingObservedAtGate = Directory.GetFiles(fixture.Root, "FinancialManagement_RestoreStaging*").Length > 0;
            safetyCountAtGate = Directory.GetFiles(fixture.BackupDirectory, "*PreRestore*.fmbak").Length;
            return ChangeAuthorization(fixture, fault, cancellation);
        });
        var pending = WithAuthorization(fixture, probe).RestoreAsync(new(backup.BackupPath!), cancellation.Token);
        if (fault == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
        else
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(result.Success);
            Assert.False(result.RestartRequired);
            Assert.Equal(fault == "clear" ? DatabaseBackupFailureReason.NotAuthenticated : DatabaseBackupFailureReason.Forbidden, result.FailureReason);
            if (gate == 3) Assert.Null(result.PreRestoreBackupPath);
            else Assert.True(File.Exists(result.PreRestoreBackupPath));
        }
        Assert.Equal(gate, probe.Calls);
        Assert.True(stagingObservedAtGate);
        Assert.Equal(gate == 3 ? 0 : 1, safetyCountAtGate);
        Assert.False(swap);
        Assert.Equal(liveHash, await HashFileAsync(fixture.LivePath));
        Assert.Equal(sourceHash, await HashFileAsync(backup.BackupPath!));
        await fixture.AssertLiveMarkerAsync();
        Assert.Empty(Directory.GetFiles(fixture.Root, "FinancialManagement_Restore*"));
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    [Fact]
    public async Task Restore_QueuedBehindAnotherRestoreRejectsInvalidatedSession()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var backup = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        Task<RestoreDatabaseBackupResult>? queued = null;
        var swaps = 0;
        fixture.RestoreTestHook.AfterAtomicReplace = _ =>
        {
            swaps++;
            Assert.Equal(0, fixture.MaintenanceLock.Semaphore.CurrentCount);
            queued = fixture.Service.RestoreAsync(new(backup.BackupPath!));
            Assert.False(queued.IsCompleted);
            ((CurrentUserSession)fixture.Session).Clear();
        };
        var first = await fixture.Service.RestoreAsync(new(backup.BackupPath!)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(first.Success);
        Assert.NotNull(queued);
        var second = await queued.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.False(second.Success);
        Assert.Equal(DatabaseBackupFailureReason.NotAuthenticated, second.FailureReason);
        Assert.Null(second.PreRestoreBackupPath);
        Assert.Equal(1, swaps);
        Assert.Single(Directory.GetFiles(fixture.BackupDirectory, "*PreRestore*.fmbak"));
        Assert.Equal(1, fixture.MaintenanceLock.Semaphore.CurrentCount);
    }

    private static bool? ChangeAuthorization(BackupFixture fixture, string fault, CancellationTokenSource cancellation)
    {
        var session = (CurrentUserSession)fixture.Session;
        switch (fault)
        {
            case "deny": return false;
            case "exception": throw new InvalidOperationException("Controlled authorization failure");
            case "authorization-canceled": throw new OperationCanceledException("Controlled authorization cancellation");
            case "cancel": cancellation.Cancel(); return true;
            case "clear": session.Clear(); return true;
            case "switch-responsable":
            case "switch-employee":
                var otherId = session.UserId!.Value + 1;
                session.Clear();
                session.SetUser(new AuthenticatedUser(otherId, "Other user", "other",
                    fault == "switch-responsable" ? UserRole.Responsable : UserRole.Employe));
                return true; // Even a true capability result cannot authorize a different principal.
            default: throw new ArgumentOutOfRangeException(nameof(fault));
        }
    }

    private static IDatabaseBackupService WithAuthorization(BackupFixture fixture, IAuthorizationService authorization) =>
        WithSession(fixture, (CurrentUserSession)fixture.Session, authorization);

    private static IDatabaseBackupService WithSession(
        BackupFixture fixture,
        CurrentUserSession session,
        IAuthorizationService authorization) =>
        new DatabaseBackupService(fixture.Context, session, authorization,
            fixture.Services.GetRequiredService<DatabaseBackupOptions>(), fixture.MaintenanceLock,
            fixture.RestoreTestHook, fixture.Services.GetRequiredService<TimeProvider>());

    private static void AssertNoPublishedOrTemporaryBackupArtifacts(BackupFixture fixture)
    {
        if (!Directory.Exists(fixture.BackupDirectory))
            return;

        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory, "*.fmbak"));
        AssertNoTemporaryBackupArtifacts(fixture);
    }

    private static void AssertNoTemporaryBackupArtifacts(BackupFixture fixture)
    {
        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory, ".fmbackup-*.partial"));
        Assert.Empty(Directory.GetDirectories(fixture.BackupDirectory, ".fmbackup-*.tmp"));
    }

    private sealed class MaintenanceAuthorizationProbe(IAuthorizationService inner, Func<int, bool?> beforeCheck) : IAuthorizationService
    {
        public int Calls { get; private set; }
        public Task<bool> CanAsync(PermissionModule module, PermissionAction action, CancellationToken cancellationToken = default) =>
            inner.CanAsync(module, action, cancellationToken);
        public Task<EffectiveModulePermission> GetEffectivePermissionsAsync(PermissionModule module, CancellationToken cancellationToken = default) =>
            inner.GetEffectivePermissionsAsync(module, cancellationToken);
        public bool CanAccessResponsibleCapability(ResponsibleCapability capability)
        {
            Assert.Equal(ResponsibleCapability.BackupRestore, capability);
            return beforeCheck(++Calls) ?? inner.CanAccessResponsibleCapability(capability);
        }
    }
}
