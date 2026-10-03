# RELEASE RECOVERY V1 REPORT

## Verdict

**RELEASE RECOVERY V1 RESULT: PASS**

## FINAL ADVERSARIAL HARDENING

### RR-DAILY-01 — invalid Daily artifact

The root cause was a filename-only completion check. After acquiring the dated cross-process guard, `DatabaseBackupService` selected the first `FinancialManagement_Daily_yyyyMMdd_*.fmbak` path and returned `Success=true, Created=false` without validating the artifact. A zero-byte, malformed, or later-corrupted file could therefore suppress the day's usable backup.

The guarded decision now enumerates every matching Daily candidate and validates each one through the existing production artifact architecture:

1. Extract the fixed, bounded artifact entries to a generated staging path.
2. Verify manifest structure, declared length, SHA-256, and archive constraints.
3. Run SQLite integrity, foreign-key, known migration-prefix, exact current-schema, schema-fingerprint, and financial-storage validation against the staged database.
4. Clean the generated staging files without modifying or deleting the source artifact.

Only a candidate that passes all checks satisfies the date. Invalid candidates remain on disk and the service attempts to create a new valid Daily artifact. If an invalid artifact exists and replacement creation fails, the result remains a failure, the existing non-blocking startup warning applies, and another attempt on the same day revalidates and retries.

The complete lookup and validation loop remains inside the exclusive `.daily-backup-yyyyMMdd.guard`. The permanent guard-order test publishes a valid artifact while representing the first process's guarded work, releases the guard, then invokes the second request. That request reacquires the guard, validates the newly published artifact, returns `Created=false`, and leaves exactly one valid Daily artifact. PreMigration names never match the Daily pattern, and a new local date uses a distinct pattern and creates a new artifact.

### RR-STARTUP-01 — required startup backup dependency

The root cause was an optional `IStartupDatabaseBackupService` parameter. Calling `FinancialDatabaseStartup.InitializeAsync(context)` on an existing schema-003 database bypassed the backup branch and still executed migration 004.

`IStartupDatabaseBackupService` is now a required, non-null dependency of both the public startup method and its internal test path. Null is rejected before the database is opened or `MigrateAsync` can run. Once supplied, any existing database with applied and pending migrations must successfully create and publish its PreMigration artifact before migration. The normal Desktop composition continues to resolve the service from dependency injection and pass it explicitly. A fresh database still skips the meaningless PreMigration step, applies its initial migrations, validates, and creates its Daily backup.

The permanent missing-dependency test begins at schema 003, passes no service, observes `ArgumentNullException`, and confirms migration history remains at 001–003 with no PreMigration or Daily artifact. Existing tests continue to prove required-backup failure also leaves migration 004 unapplied.

### Accepted old-schema restore behavior

Per lead adjudication, restore continues to accept a legitimate old-schema artifact, migrate its staging database to the current schema, validate it, and only then replace live data. It does not leave the application database at schema 003.

A new permanent regression creates an actual schema-003 PreMigration artifact through normal startup, confirms startup reaches schema 004, adds a later live marker, then restores the actual artifact through production `IDatabaseBackupService.RestoreAsync`. Restore succeeds, preserves the source artifact hash, preserves the pre-migration data, removes the later marker, clears the authenticated session, and leaves the live database safely at schema 004.

### Permanent tests added by the final fix

Ten new executed test cases cover:

1. Fresh database initialization without a PreMigration artifact.
2. Missing startup backup dependency cannot migrate schema 003.
3. A valid Daily artifact prevents duplication.
4. A zero-byte Daily artifact is ignored and replaced.
5. A malformed Daily artifact is ignored and replaced.
6. A previously valid Daily artifact corrupted later is replaced.
7. Invalid and valid Daily artifacts may coexist; the valid one satisfies the date.
8. Corrupt Daily plus replacement failure reports failure, preserves the live database, and remains eligible for a successful later retry.
9. Recheck after guard acquisition observes a newly published valid artifact and suppresses duplication.
10. An actual old-schema PreMigration artifact restores through the production path with preserved data and current schema.

The existing date-transition and PreMigration-versus-Daily test continues to prove that the next local date creates a new Daily artifact and a PreMigration artifact never counts as Daily. Existing ordering tests continue to prove backup-before-migration and fresh-database behavior.

### Final validation

- Targeted recovery/storage selection: **254 passed, 0 failed, 0 skipped**.
- Full permanent suite: **1109 passed, 0 failed, 0 skipped** (previous total: 1099; net: +10).
- `dotnet build FinancialManagement.sln --no-restore`: **PASS**, 0 warnings, 0 errors.
- `scripts\\verify-foundation.cmd`: **PASS**, including its independent **1109 passed, 0 failed, 0 skipped** full-suite run.
- Foundation vulnerable-package check: **PASS**, no vulnerable Infrastructure packages reported.
- Standalone EF migration list: exactly `001_InitialSchema`, `002_AllowAnonymousSalePayments`, `003_TreasuryOpeningBalanceSingleton`, and `004_EnforceFinancialIntegerStorageClass`.
- Pending model changes: **PASS** — no model changes since the last migration.
- `git diff --check`: **PASS**.
- Migration/schema files added or changed by this fix: **NONE**.
- Real database touched: **NO**. `C:\\ProgramData\\FinancialManagement\\Data\\FinancialManagement.db` was not opened, copied, migrated, restored, or used by tests.

### Files changed by the final fix

- `src/FinancialManagement.Infrastructure/Backup/DatabaseBackupService.cs`
- `src/FinancialManagement.Infrastructure/FinancialDatabaseStartup.cs`
- `tests/FinancialManagement.Tests/DatabaseStartupBackupTests.cs`
- `tests/FinancialManagement.Tests/DatabaseBackupServiceTests.cs`
- `tests/FinancialManagement.Tests/FinancialStorageIntegrityTests.cs` (existing startup calls adapted to the required dependency)
- `RELEASE-RECOVERY-V1-REPORT.md`

All changes remain unstaged and uncommitted on `feature/release-recovery-v1`; HEAD remains `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`. No add, commit, push, merge, reset, rebase, or clean operation was run.

### FINAL RESULT: PASS

The requested B02 backup/restore interface, B03 pre-migration safety backup, M05 salary/employee rule, and M09 daily local backup are implemented and validated. The accepted B01 cross-user-read behavior and DEV2 dashboard scope were not changed.

## 1. Git baseline and working branch

- Starting branch: `release/v1.0-rc`
- Required starting commit: `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`
- Working branch: `feature/release-recovery-v1`
- Current HEAD: `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`
- Merge base with the required baseline: `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`
- No files were staged or committed. Existing untracked reports and `doc/` were preserved.

## 2. Files changed

### Production source

- `src/FinancialManagement.Application/Backup/IStartupDatabaseBackupService.cs` (new)
- `src/FinancialManagement.Application/Backup/StartupDatabaseBackupResult.cs` (new)
- `src/FinancialManagement.Application/Expenses/CreateExpenseFailureReason.cs`
- `src/FinancialManagement.Desktop/App.xaml.cs`
- `src/FinancialManagement.Desktop/Services/BackupDialogService.cs` (new)
- `src/FinancialManagement.Desktop/Services/IBackupDialogService.cs` (new)
- `src/FinancialManagement.Desktop/ViewModels/AdministrationViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/BackupViewModel.cs` (new)
- `src/FinancialManagement.Desktop/ViewModels/ExpensesViewModel.cs`
- `src/FinancialManagement.Desktop/Views/AdministrationView.xaml`
- `src/FinancialManagement.Desktop/Views/BackupView.xaml` (new)
- `src/FinancialManagement.Desktop/Views/BackupView.xaml.cs` (new)
- `src/FinancialManagement.Desktop/Views/ExpensesView.xaml`
- `src/FinancialManagement.Domain/ExpenseCategoryDefaults.cs` (new)
- `src/FinancialManagement.Infrastructure/Backup/DatabaseBackupService.cs`
- `src/FinancialManagement.Infrastructure/Configurations/ExpenseCategoryConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Expenses/ExpenseService.cs`
- `src/FinancialManagement.Infrastructure/FinancialDatabaseStartup.cs`
- `src/FinancialManagement.Infrastructure/FinancialDatabaseStartupResult.cs` (new)
- `src/FinancialManagement.Infrastructure/InfrastructureServiceCollectionExtensions.cs`

### Tests

- `tests/FinancialManagement.Tests/DatabaseStartupBackupTests.cs` (new)
- `tests/FinancialManagement.Tests/ExpenseServiceTests.cs`

No dashboard files, migration files, payment code, delete flows, identifier logic, or unrelated CRUD behavior were changed.

## 3. B02 — usable WPF backup and restore

### UI location and design

A **Sauvegardes** tab now appears in the existing Responsable-only Administration screen. It contains three focused actions:

1. Create a local backup.
2. Create a backup in an external folder selected with the native Windows folder dialog.
3. Select and restore a `.fmbak` artifact through the native Windows file dialog.

The view shows progress, the selected restore path, clear result messages, and a strong restore warning. The restore confirmation defaults to **No**.

### Local backup behavior

The local action delegates to the existing `IDatabaseBackupService`. It uses the established SQLite backup API and packed `.fmbak` artifact format, including manifest, length, SHA-256, schema, compatibility, and storage validation. The created artifact path is shown to the operator.

### External backup behavior

The operator selects an existing external destination folder. The backend canonicalizes the full path and generates the filename itself. Backup construction and validation use the same established artifact pipeline as local backups; the UI does not copy database files itself.

### Restore behavior

The file dialog accepts `.fmbak` files. Before replacement, the backend rechecks authorization and artifact compatibility, validates integrity and schema in staging, checks storage/allocation conditions, creates a pre-restore safety backup, and uses atomic replacement with rollback support. Fixed archive entries and generated staging paths prevent an archive entry from controlling extraction paths.

### Successful restore session behavior

After a successful restore, the current session is cleared, a success message is shown, and the existing authentication-invalidated flow closes the privileged shell and returns the application to login. A stale authenticated Administration screen cannot continue after the database replacement. Unexpected restore failures also fail closed by invalidating the session.

## 4. B03 — safe backup before migration

### Pending migration detection

Startup validates the configured database, then reads applied and pending EF Core migrations before invoking `MigrateAsync`.

### Pre-migration backup mechanism

For an existing database with at least one applied migration and at least one pending migration, startup creates a `FinancialManagement_PreMigration_*.fmbak` artifact through the same SQLite snapshot, packing, integrity, compatibility, and schema-validation pipeline as ordinary backups. The validation permits a known older application schema because this artifact is intentionally captured before the pending migration. A new empty database has no business data to preserve, so it skips this pre-migration artifact and proceeds through its initial migrations.

### Proof that backup happens before migration

The targeted test starts from migration `003`, leaves `004` pending, and verifies that the pre-migration artifact still contains exactly the first three applied migrations while the working database is subsequently migrated. A dedicated internal test hook also forces a failure immediately before `MigrateAsync`, proving that the recovery artifact already exists and that the database remains on `003`.

### Backup failure behavior

If pre-migration backup creation or validation fails, startup throws `PreMigrationBackupException` and never calls the migration step. The test verifies that migration `004` remains unapplied and no daily backup is published.

### Migration failure behavior

If migration or post-migration validation fails after the safety artifact was created, startup throws `DatabaseMigrationWithBackupException` carrying the recovery artifact path. `App.xaml.cs` writes that path to `Trace` and includes it in the blocking startup message so the operator can recover from the preserved artifact.

## 5. M09 — automatic local backup once per business day

After successful startup migration and validation, the application requests a daily local backup based on `TimeProvider.GetLocalNow()` and its local date. Artifacts use the `FinancialManagement_Daily_yyyyMMdd_*` prefix.

The service checks for an existing daily artifact and serializes requests in-process. A dated `.daily-backup-yyyyMMdd.guard` file opened with exclusive sharing prevents two application processes from publishing a second artifact concurrently. If another process holds the guard, startup continues and reports a non-blocking backup warning; a later startup retries. Guard files are retained and no backup artifacts are deleted.

A pre-migration backup does not count as the daily backup. When migrations are pending, the pre-migration artifact protects the old schema first, then the daily artifact records the successfully migrated and validated database. A daily backup failure is returned as startup status, logged, and shown once to the first Responsable who logs in during that run; it does not turn a successfully validated application startup into a migration failure.

## 6. M05 — salary requires a concerned employee

### Backend rule

The stable built-in salary identity is centralized as `ExpenseCategoryDefaults.SalaryId = 1` and `SalaryName = "Salaires"`. `ExpenseService` rejects creation with `ConcernedEmployeeRequired` when that category is selected without an employee. Existing checks still reject nonexistent or inactive employees. Non-salary categories continue to allow an optional employee.

### WPF behavior

When **Salaires** is selected, the form label immediately becomes **Employé concerné \*** and submission without a selected employee displays a focused validation message. Selecting an employee clears that form validation. For other expense categories, the label remains optional.

## 7. Authorization review

- The new tab is hosted under the existing Responsable-only Administration route.
- `BackupViewModel` checks for an authenticated Responsable before opening dialogs or starting an action.
- `IDatabaseBackupService` remains the authoritative backend gate and still requires `ResponsibleCapability.BackupRestore` for user-initiated backup and restore operations.
- Existing tests confirm that unauthenticated callers and Employee-role callers are denied, even when an Employee session is given all permission flags.
- Startup-only backup operations run before login through the separate `IStartupDatabaseBackupService`; no user-facing route exposes that interface.
- No reauthentication, session revocation policy, or B01 read behavior was added or changed.
- Backup metadata contains no plaintext password or financial record content. No new secret-bearing logs were introduced.

## 8. Tests added and updated

The initial implementation added eight disposable-database tests in `DatabaseStartupBackupTests`:

1. A current database with no pending migration creates no pre-migration backup.
2. A pending migration creates an old-schema artifact before migration and a daily artifact afterward.
3. Pre-migration backup failure aborts before applying the migration.
4. A migration failure retains the recovery artifact and leaves the old schema in place.
5. Daily backup occurs once on the same local date, occurs again on the next date, and is independent of pre-migration backup.
6. Concurrent requests in one process publish one daily artifact.
7. The filesystem guard prevents a second process from publishing concurrently and permits a later retry.
8. Daily backup failure is reported without undoing successful startup validation.

The final adversarial hardening adds nine executed cases to this class and one accepted-restore regression to `DatabaseBackupServiceTests`, as detailed in **FINAL ADVERSARIAL HARDENING** above.

`ExpenseServiceTests` now explicitly proves that salary without an employee is rejected, salary with a valid active employee succeeds and has the expected treasury effect, invalid employees remain rejected, and a non-salary expense may omit the employee.

Every added test uses a disposable GUID-named SQLite database and cleans up its temporary root. No test points to the protected production database.

## 9. Validation evidence

- Previous full-suite baseline: **1090 passed**.
- Pre-hardening full-suite total: **1099 passed, 0 failed, 0 skipped**.
- Final hardened full-suite total: **1109 passed, 0 failed, 0 skipped**.
- Final targeted recovery/storage selection: **254 passed, 0 failed, 0 skipped**.
- Targeted startup backup tests: **8 passed, 0 failed, 0 skipped**.
- Earlier targeted startup/salary run: **31 passed, 0 failed, 0 skipped**.
- Existing backup/restore regression selection: **175 passed, 0 failed, 0 skipped**.
- Final build: **PASS**, 0 warnings, 0 errors.
- `scripts\\verify-foundation.cmd`: **PASS**, including an independent full-suite run of **1109 passed, 0 failed, 0 skipped** after the final adversarial hardening.
- EF migration list: exactly `001_InitialSchema`, `002_AllowAnonymousSalePayments`, `003_TreasuryOpeningBalanceSingleton`, and `004_EnforceFinancialIntegerStorageClass`.
- EF pending model changes gate: **PASS** — no model changes since the last migration.
- Vulnerable package check in foundation verification: no vulnerable packages reported.
- `git diff --check`: **PASS**.

No migration or schema change was added for this work.

## 10. Protected database and production scope

- Production/release database touched: **NO**.
- `C:\\ProgramData\\FinancialManagement\\Data\\FinancialManagement.db` was not opened, migrated, copied, restored, or used by tests.
- Production source files changed: **20**.
- Test files changed or added: **4**.
- Manual application launch was intentionally omitted because the desktop composition root targets the protected ProgramData database. WPF XAML compilation and the complete application build passed.

## 11. Git working status

All implementation edits remain unstaged and uncommitted on `feature/release-recovery-v1`. The required report is also untracked. Pre-existing untracked reports and `doc/` remain present. No add, commit, push, merge, rebase, reset, or clean operation was run.

## 12. Known limitations

- WPF layout and interaction were validated through XAML compilation, command/view-model behavior, and source review rather than a manual launch against the protected ProgramData path.
- The task does not add a backup retention or pruning policy; existing and newly created backup artifacts are preserved.
- If the daily guard is temporarily held by another application process, that startup emits a warning and skips publication. A later startup retries for the same local date.
- The daily-backup warning is shown to the first Responsable who logs in during the affected application run. If no Responsable logs in before exit, the failure remains in `Trace`, and the next startup retries.

## 13. Final result

**RELEASE RECOVERY V1 RESULT: PASS**
