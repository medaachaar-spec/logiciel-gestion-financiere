# INTEGRATION — RECOVERY V1 + DASHBOARD V2.1 / REPORTS V1

## 1. Starting integration hash

`integration` started at `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`.

## 2–8. Merge results

- Recovery merge commit: `aaa582336cba3dd23f1a6a430134aaaf9fdfd897`
- Recovery merge conflicts: **NONE**
- Dashboard/Reports merge commit: `7650a487b36389cd0afdfaeae952bd6c9da969aa`
- Dashboard/Reports merge conflicts: **NONE**
- Exact conflict files: **NONE**
- Semantic resolutions: no conflict resolution was required. Git's `ort` merge retained both accepted trees. The combined source was audited directly after each merge.

## 9. Combined `App.xaml.cs` behavior

The combined composition root retains Recovery startup behavior: it resolves `IStartupDatabaseBackupService` with `GetRequiredService`, passes it into `FinancialDatabaseStartup.InitializeAsync`, and therefore keeps mandatory PreMigration and automatic Daily backup startup behavior. It also retains Dashboard/Reports registrations for `DashboardViewModel`, `ReportsViewModel`, their lazy factories, `DueAlertsViewModel`, and the existing WPF application resources/templates. `BackupViewModel` and `IBackupDialogService` remain registered, and no Dashboard or Reports registration replaced them.

## 10. Combined infrastructure behavior

`InfrastructureServiceCollectionExtensions` retains one scoped `FinancialDashboardService` implementation exposed through both `IFinancialDashboardService` and `IFinancialReportsService`. It also retains `DatabaseBackupService` exposed through `IDatabaseBackupService` and the required `IStartupDatabaseBackupService`, plus the Recovery maintenance lock and restore test hook. This preserves both Dashboard/Reports data access and the Recovery backup path.

## 11–12. Migrations and schema drift

- Exact migrations: `001_InitialSchema`, `002_AllowAnonymousSalePayments`, `003_TreasuryOpeningBalanceSingleton`, `004_EnforceFinancialIntegerStorageClass`.
- Migration 005: **NONE**.
- `dotnet ef migrations has-pending-model-changes`: **PASS** — no changes have been made to the model since the last migration.
- No migration or schema file was changed by either merge.

## 13. Targeted test totals

- Recovery-focused selection before Dashboard merge: **254 passed, 0 failed, 0 skipped**.
- Combined Dashboard/Reports, snapshot, period, alerts, and authorization selection: **71 passed, 0 failed, 0 skipped**.
- The separate WPF graph/binding test project could not be restored because NuGet timed out downloading `SkiaSharp.Views.Desktop.Common.3.119.0`, `OpenTK.*`, and `SkiaSharp.NativeAssets.Win32.2.88.9`. It was not counted as passed.

## 14. Full-suite total

The combined core test project `tests/FinancialManagement.Tests/FinancialManagement.Tests.csproj` completed with **1109 passed, 0 failed, 0 skipped**. The separate WPF test project was not executable in this environment because its package restore could not complete.

## 15. Build

`dotnet build FinancialManagement.sln` passed with **0 warnings and 0 errors**. The main solution build compiled the application and core test projects. The WPF test-project assets were absent initially; its explicit restore was attempted and failed on the external NuGet timeouts described above.

## 16. Foundation

`scripts\\verify-foundation.cmd` was attempted after both merges. It exited during step 2, solution package restore, after repeated NuGet timeouts for the WPF graphics dependencies. It did not reach its build/test/migration stages. The direct combined build, core full suite, EF migration list, and model-drift gate all passed independently.

## 17. Vulnerabilities

Infrastructure and Desktop project vulnerability checks were attempted directly. Both were blocked by NuGet registration download timeouts; no vulnerability result was returned. No package vulnerability was reported by the failed commands.

## 18–20. Runtime proof

- Runtime Dashboard proof: **NOT RUN**. The desktop application is hardwired to the protected ProgramData database and startup automatically performs a Daily backup.
- Runtime Reports proof: **NOT RUN** for the same safety reason.
- Runtime Administration/Backup UI proof: **NOT RUN** after integration. Main application XAML compiled, and source inspection confirms Reports navigation, Administration navigation, and the Sauvegardes tab remain wired together. No runtime launch was performed because it would touch the protected UAT database/backup directory.

No destructive restore, reseed, or financial-row mutation was performed.

## 21–24. Invariants

- Recovery features preserved: **YES** — mandatory startup backup, Daily backup, B02 Backup/Restore UI, and M05 salary employee enforcement remain present; Recovery selection passed 254 tests after the Recovery merge.
- Dashboard/Reports features preserved: **YES** at source/service level — Dashboard and Reports services, navigation, LiveCharts resources, period selection, snapshots, and alerts remain present; the combined core Dashboard/Reports selection passed 71 tests. WPF graph/binding runtime tests were blocked by NuGet restore.
- ProgramData financial rows changed: **NO**.
- Protected directories touched: **NO**.

## 25–29. Git state and branch protection

- Local integration HEAD: `7650a487b36389cd0afdfaeae952bd6c9da969aa`.
- `origin/integration` unchanged: **YES** — remains `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`.
- `origin/release/v1.0-rc` unchanged: **YES** — remains `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`.
- Push performed: **NO**.
- Unexpected files included: **NO**. The preserved ASTRA/UAT/RELEASE reports and `doc/` remain untracked and were not included in either merge.
- `git diff --check`: **PASS**.

## 30. FINAL INTEGRATION RESULT

**PARTIAL**

The controlled local merges completed cleanly, core build and regression validation passed, and both accepted feature sets remain present. WPF graph/binding tests, foundation completion, package vulnerability checks, and combined runtime smoke remain unverified because external NuGet downloads timed out and launching the application would touch the protected ProgramData/UAT database and automatic-backup directory.

## FINAL INTEGRATION VALIDATION

### 1. Integration identity and preservation method

- Integration HEAD: `7650a487b36389cd0afdfaeae952bd6c9da969aa`.
- Baseline: `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`.
- Recovery feature: `1cb8d7cdb3bd838480237ca0138f63d5d5a80560`.
- Dashboard/Reports feature: `360707f6cb8d5a6e4641cefd9880dc6d8d0a3f48`.
- The baseline, feature, and combined versions of every changed test file were inspected from Git objects. Every Recovery-owned test file and every Dashboard-owned test source file has the same Git blob hash in its feature commit and in combined integration. The new Desktop test project support files and project file are also preserved by the combined tree.

### 2. RECOVERY-ADDED TESTS

The Recovery delta adds **20 xUnit execution shapes** and removes one superseded baseline Fact, for a net increase of **19** core tests.

1. `DatabaseBackupServiceTests.Restore_AcceptsActualPreMigrationArtifactPreservesDataAndReachesCurrentSchema`
2. `DatabaseStartupBackupTests.Current_database_has_no_pending_migration_and_creates_no_pre_migration_backup`
3. `DatabaseStartupBackupTests.Fresh_database_migrates_without_pre_migration_backup`
4. `DatabaseStartupBackupTests.Pending_migration_creates_old_schema_backup_before_migration_then_daily_backup`
5. `DatabaseStartupBackupTests.Pre_migration_backup_failure_aborts_without_applying_pending_migration`
6. `DatabaseStartupBackupTests.Missing_startup_backup_dependency_cannot_migrate_existing_database`
7. `DatabaseStartupBackupTests.Migration_failure_keeps_pre_migration_recovery_artifact_and_fails_startup`
8. `DatabaseStartupBackupTests.Daily_backup_is_created_once_per_local_date_and_pre_migration_does_not_suppress_it`
9. `DatabaseStartupBackupTests.Concurrent_daily_backup_requests_publish_exactly_one_artifact`
10. `DatabaseStartupBackupTests.Valid_daily_artifact_satisfies_the_date_without_duplication`
11. `DatabaseStartupBackupTests.Zero_byte_or_malformed_daily_artifact_is_ignored_and_replaced(false)`
12. `DatabaseStartupBackupTests.Zero_byte_or_malformed_daily_artifact_is_ignored_and_replaced(true)`
13. `DatabaseStartupBackupTests.Previously_valid_daily_artifact_corrupted_later_is_replaced`
14. `DatabaseStartupBackupTests.Invalid_and_valid_daily_artifacts_coexist_without_duplication`
15. `DatabaseStartupBackupTests.Corrupted_daily_and_failed_replacement_remain_eligible_for_retry`
16. `DatabaseStartupBackupTests.Daily_recheck_after_guard_acquisition_observes_newly_published_valid_artifact`
17. `DatabaseStartupBackupTests.Daily_backup_guard_prevents_a_second_process_from_publishing_concurrently`
18. `DatabaseStartupBackupTests.Daily_backup_failure_is_reported_after_successful_validation_without_failing_startup`
19. `ExpenseCreateServiceTests.Salary_without_concerned_employee_is_rejected`
20. `ExpenseCreateServiceTests.Non_salary_allows_null_observation_and_no_concerned_employee`

The superseded baseline Fact is `ExpenseCreateServiceTests.Null_observation_and_no_concerned_employee_are_accepted`. Recovery replaced its ambiguous category setup with the two explicit salary/non-salary cases above. Changes in `FinancialStorageIntegrityTests` adapt existing startup tests to the mandatory backup dependency and do not add, remove, or reduce an xUnit execution shape.

**Recovery-added tests missing from integration: NONE.** All 20 added execution shapes are present, and all four Recovery-owned test files are byte-for-byte identical to the accepted Recovery commit.

### 3. DASHBOARD-ADDED TESTS

The Dashboard/Reports delta adds **21 xUnit execution shapes**: **17 core** and **4 Desktop/WPF**.

Core/Application/Infrastructure:

1. `FinancialDashboardServiceTests.Selected_and_comparison_months_return_real_daily_series_with_zero_filled_days`
2. `FinancialDashboardServiceTests.Reports_comparison_includes_all_six_flows_across_different_month_lengths`
3. `FinancialDashboardServiceTests.Reports_annual_evolution_is_sql_aggregated_into_january_through_december`
4. `FinancialDashboardServiceTests.Employee_is_forbidden_from_reports_even_with_all_configurable_permissions(false)`
5. `FinancialDashboardServiceTests.Employee_is_forbidden_from_reports_even_with_all_configurable_permissions(true)`
6. `FinancialDashboardServiceTests.Top_outstanding_parties_are_aggregated_and_limited_without_changing_stock_totals`
7. `FinancialDashboardServiceTests.Comparison_percentage_is_neutral_for_zero_denominator_and_handles_negative_result(0, 0, null)`
8. `FinancialDashboardServiceTests.Comparison_percentage_is_neutral_for_zero_denominator_and_handles_negative_result(1000, 0, null)`
9. `FinancialDashboardServiceTests.Comparison_percentage_is_neutral_for_zero_denominator_and_handles_negative_result(1200, 1000, 20.0)`
10. `FinancialDashboardServiceTests.Comparison_percentage_is_neutral_for_zero_denominator_and_handles_negative_result(-200, -100, -100.0)`
11. `FinancialPeriodSelectionTests.Approved_presets_explicitly_exclude_weekly_mode`
12. `FinancialPeriodSelectionTests.Presets_resolve_against_the_current_local_business_date(Today, 2026-09-09, 2026-09-09, Daily)`
13. `FinancialPeriodSelectionTests.Presets_resolve_against_the_current_local_business_date(CurrentMonth, 2026-09-01, 2026-09-30, Daily)`
14. `FinancialPeriodSelectionTests.Presets_resolve_against_the_current_local_business_date(CurrentYear, 2026-01-01, 2026-12-31, Monthly)`
15. `FinancialPeriodSelectionTests.Custom_range_uses_daily_through_62_days_then_monthly(2026-01-01, 2026-03-03, Daily)`
16. `FinancialPeriodSelectionTests.Custom_range_uses_daily_through_62_days_then_monthly(2026-01-01, 2026-03-04, Monthly)`
17. `FinancialPeriodSelectionTests.Custom_range_requires_ordered_explicit_dates`

Desktop/WPF:

18. `DashboardReportsGraphStateTests.PersistedFlows_DriveDashboardAndReportsGraphStates_AndEmptyPeriodsRemainEmpty`
19. `DashboardReportsGraphStateTests.InverseBooleanConverter_ProducesVisibilityForVisibilityTargets(true, Collapsed)`
20. `DashboardReportsGraphStateTests.InverseBooleanConverter_ProducesVisibilityForVisibilityTargets(false, Visible)`
21. `DueAlertsViewBindingTests.NonEmptyReadonlyAlertRows_RenderWithoutDispatcherBindingExceptions`

The one-line `AuthorizationServiceTests` change is an import needed by the feature tree and does not add or remove a test. The two Desktop test source files were absent at baseline and are identical between the accepted Dashboard commit and combined integration.

**Dashboard-added tests missing from integration: NONE.** All 21 added execution shapes are present.

### 4. COMBINED-PRESENT TESTS

The combined-present feature set is the exact union of the **20 Recovery execution shapes in section 2** and the **21 Dashboard/Reports execution shapes in section 3**: **41/41 present, 0 missing**. Git blob identity proves that no method body, Fact attribute, Theory attribute, or InlineData row from either accepted feature was lost during merge.

### 5. Exact explanation of the previous 1109 core count

The validated baseline at `fea6f90` contains **1090** core tests. Recovery added 20 execution shapes while replacing one baseline Fact, so its net delta is **+19** and its expected core total is exactly **1109**. Dashboard/Reports adds **+17** core cases and **+4** WPF cases. Therefore the correct combined totals are:

- Core: `1090 + 19 + 17 = 1126`.
- Desktop/WPF: `4`.
- Complete solution: `1126 + 4 = 1130`.

The prior 1109 core result exactly matched the Recovery-only compiled test inventory. It was a stale-build result, not a loss of test source during merge. A forced fresh build of `FinancialManagement.Tests.csproj` at integration HEAD immediately discovered and passed **1126** tests, and the later full solution and Foundation runs repeated **1126**. The feature-to-combined blob comparisons independently show all Dashboard core test additions in the combined source.

### 6. Restore, WPF, and complete suite

- NuGet sources: only official `nuget.org` is configured and enabled (`https://api.nuget.org/v3/index.json`).
- Restore: **PASS after network recovery**. The first normal restore and its requested retry encountered unexpected transport EOFs while downloading official SkiaSharp packages and remained in NuGet retry loops. A later solution-wide package vulnerability command completed restoration of both Desktop projects in 2.16 minutes. The subsequent exact solution build and Foundation restore confirmed all projects were up to date.
- WPF tests: **4 passed, 0 failed, 0 skipped**. This includes the populated DueAlerts binding regression, real populated/empty Dashboard and Reports graph states, and both visibility-converter execution cases.
- Core/Application/Infrastructure tests: **1126 passed, 0 failed, 0 skipped**.
- Complete combined total: **1130 passed, 0 failed, 0 skipped**.

### 7. Build and Foundation

- `dotnet build FinancialManagement.sln`: **PASS — 0 warnings, 0 errors** after the official package restore completed.
- `scripts\\verify-foundation.cmd`: **PASS** and reached its normal `Verification succeeded.` state.
- Foundation step 1, tool restore: **PASS** (`dotnet-ef` 10.0.11).
- Foundation step 2, solution restore: **PASS**, all projects up to date.
- Foundation step 3, solution build: **PASS — 0 warnings, 0 errors**.
- Foundation step 4, solution tests: **PASS — 1126 core + 4 WPF, 0 failed, 0 skipped**.
- Foundation step 5, migrations list: **PASS — 001 through 004 only**.
- Foundation step 6, pending model changes: **PASS — no changes have been made to the model since the last migration**.
- Foundation step 7, Infrastructure direct/transitive vulnerability query: **PASS — no vulnerable packages reported**.

### 8. Vulnerabilities and schema

- Independent `dotnet list FinancialManagement.sln package --vulnerable --include-transitive`: **PASS**. Domain, Application, Infrastructure, Desktop, core tests, and Desktop tests each report no vulnerable package from the current official source.
- Migrations: `20260903232954_001_InitialSchema`, `20260904004853_002_AllowAnonymousSalePayments`, `20260904111121_003_TreasuryOpeningBalanceSingleton`, `20260908072131_004_EnforceFinancialIntegerStorageClass`.
- Migration 005: **NONE**.
- Schema drift: **NONE**.

### 9. Git and runtime safety

- `git diff --check`: **PASS**.
- ProgramData touched: **NO**.
- Existing runtime override: **NONE**. `ApplicationPaths` constructs the production data root directly from `Environment.SpecialFolder.CommonApplicationData`, and `App.OnStartup` constructs `ApplicationPaths` directly. No supported command-line, environment-variable, or development data-root override exists.
- Runtime strategy: **DEFERRED TO DEV2 VISUAL-UAT ENVIRONMENT**.
- `origin/integration` changed: **NO**; it remains `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`.
- `origin/release/v1.0-rc` changed: **NO**; it remains `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`.
- Push performed: **NO**.
- Existing untracked reports and `doc/` were preserved.

### 10. FINAL RESULT

**PASS**

All permanent Recovery and Dashboard/Reports tests are preserved, all 1130 executable tests pass with zero failures and skips, the full solution builds at 0 warnings/0 errors, Foundation reaches normal success, vulnerability checks pass across all solution projects, migrations remain 001–004, no model drift exists, and Git whitespace validation passes. Runtime smoke is deferred to DEV2 because DEV1 has no supported isolated data-root mechanism.
