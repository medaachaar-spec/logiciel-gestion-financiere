# ASTRA-003 — Dashboard common snapshot audit

## Approved implementation — current state (2026-09-08)

The lead approved the exact extension to `TreasuryService.GetSummaryAsync`. The original audit and its scope-stop record below are retained as history; this section supersedes their implementation status. **Implementation complete; final full-regression gate PASS. ASTRA-003 CLOSED and FEATURE FROZEN. IMPLEMENTATION READY FOR LEAD REVIEW: YES. Remaining technical blocker: NONE.**

### Exact changed files

Only two production files were modified:

- `src/FinancialManagement.Infrastructure/Dashboard/FinancialDashboardService.cs`: 28 added lines; deferred transaction ownership wrapper and extraction of the existing reads into private ReadSnapshotAsync. No financial query/formula was changed.
- `src/FinancialManagement.Infrastructure/Treasury/TreasuryService.cs`: GetSummaryAsync only, 8 added / 3 removed lines; begin/commit/dispose only its own standalone transaction, reuse an existing transaction without taking ownership.

Two new test files:

- `tests/FinancialManagement.Tests/FinancialDashboardSnapshotTests.cs`
- `tests/FinancialManagement.Tests/DashboardSnapshotTestDatabase.cs`

Existing tests were not edited. Application, Domain, Desktop, DbContext, migrations, schema, packages, ASTRA-004 and ASTRA-005 production code remain unchanged. No helper outside these two production files was necessary. All three reports remain local/untracked; ASTRA-004/005 reports were not edited. The four accepted source/test files were staged, committed and pushed to the feature branch; no report was staged.

### Ownership and snapshot implementation

After the original session/authorization/date validation, Dashboard takes an EF open-reference, begins `SqliteConnection.BeginTransaction(deferred: true)`, attaches it through `Database.UseTransactionAsync`, and awaits the complete existing read sequence, including the Treasury call. The first SELECT establishes the snapshot. On success the read transaction is committed; other exits dispose it without returning any partial metric result. The EF attachment is disposed before the raw transaction; the finally block releases the Dashboard's open-reference. It neither disposes the DI context/connection nor closes a caller-owned open connection.

Treasury uses `Database.CurrentTransaction` to distinguish ownership. When it is non-null, Treasury creates no transaction and never commits, rolls back, detaches, disposes or closes the outer transaction/connection. When null, its original standalone BeginTransactionAsync/CommitAsync/disposal path remains. Its standalone transaction mode is intentionally unchanged. Dashboard uses the proven deferred overload and does not reserve a writer lock.

All 26 successful Dashboard SELECTs were asserted to carry the **same DbTransaction and DbConnection**. The closed-connection case performs exactly **one open / one close** rather than the baseline's fourteen cycles. Reads remain sequential. No new SaveChanges, DML, financial double/float, money conversions, formula/date changes or WPF reconciliation were introduced. The read scope excludes UI work.

### Permanent evidence

The first focused run passed **19 tests**, 0 failed/skipped, approximately 34 seconds. The main concurrency fact itself completed in about 19 seconds:

- **50 independent SQLite WAL databases**, each using production AddInfrastructure, two independent providers/scopes/connections, real Responsible authentication and real ISaleService.CreateAsync. A TaskCompletionSource/interceptor pauses before SELECT 2, after SELECT 1 was consumed. Every writer committed Sale=10000 + cash payment=4000 + allocation=4000 before the reader was released. No Thread.Sleep. **50/50 completed; 0 mixed snapshots; no SQLITE_BUSY caused by the Dashboard reader.**
- In-progress response on each iteration: Sales=0, Collections=0, Receivables=0, Cash=0, Bank=0, TotalTreasury=0, Result=0. Next call: Sales=10000, Collections=4000, Receivables=6000, Cash=4000, Bank=0, TotalTreasury=4000, Result=10000. All other flow/payable fields zero as seeded.
- A permanent test-only autocommit reference reproduces the original 0/4000/6000/4000 mixed tuple under the same controlled interleaving. The original external audit still preserves the exact full production baseline reproduction/26 SQL statements. No broken production path or test switch was retained.
- Writer before SELECT 1: all metrics reflect the complete post-write state, proving first-read snapshot establishment.
- Supplier-side test: pause before purchase allocations (SELECT 11), commit a real Purchase=10000/payment=4000/allocation=4000. First response remains pre-write; next response has Purchases=10000, SupplierPayments=4000, Payables=6000, Cash=-4000, Result=-10000.
- Treasury transfer test: seed Cash=10000/Bank=20000; pause before the last underlying transfer sum (SELECT 26), commit a real Bank-to-Cash transfer=3000. First response stays 10000/20000; next becomes 13000/17000. TotalTreasury remains 30000. This checks the end of the full transaction, not just early KPI reads.
- Nine success/cancellation/query-failure combinations cover initially closed, caller EF-open, and caller raw-open connections. Cancellation/error is injected at SELECT 20, after snapshot establishment inside Treasury. Exceptions propagate, CurrentTransaction is cleared, the correct initial connection state is restored, no financial rows were written by the reader, a real writer still commits and the next Dashboard works.
- Three explicit borrowed-Treasury tests cover success, cancellation and SQL error. An uncommitted outer update remains visible only to the outer context after Treasury returns/throws, proving it neither commits nor rolls back. The same attachment/connection remains active and supports another read; the caller rolls back, then a fresh Treasury call works.
- Standalone Treasury still executes its 13 SELECTs in its own transaction, releases it and returns correct Cash/Bank/Total. Unauthenticated calls perform no reads. Invalid stored enum still throws; after test-only correction of its fixture, the next call succeeds. Existing Treasury permissions/business tests remain in the full regression suite.

### Verification and performance gate

Final restore/build/test/foundation output is captured in `C:/Users/HP/AppData/Local/Temp/astra003-final-validation.log`. A previous execution session became inaccessible after a continuation; no unobserved result is counted. Restore and build passed (0 warnings / 0 errors). The observed complete direct regression passed **1090 PASS / 0 FAIL / 0 SKIP**, 5 min 23 s. Foundation's own complete regression also passed **1090 PASS / 0 FAIL / 0 SKIP**, 4 min 30 s. **verify-foundation PASS**, no known vulnerable Infrastructure packages reported by the configured direct/transitive scan, no pending model changes, exact migrations 001–004, git diff --check PASS. Final chain exit code 0 with `ASTRA003_FINAL_VALIDATION_PASS` recorded in the log.

The configured critical scenario runs 50 independent iterations per suite. The focused run and both observed complete runs therefore validated **150 iterations total**, without counting any inaccessible execution. No existing concurrency test failed in these observed runs. The historical initialization Barrier timeout remains recorded as UNRELATED and its test/code were not modified.

Historical tiny-dataset baseline: 3.62 ms/call. Initial corrected measurements (three warmups, three batches of twenty calls on 3 sales / 2 payments / 2 allocations) were 9.27 / 10.20 / 12.64 ms/call. These cross-run samples are noisy and do not establish a percentage overhead.

A further comparison used the exact same compiled performance harness, first with the preserved pre-fix Infrastructure binary from the audit, then with the corrected binary. While the full regression was active, baseline batches averaged **93.36 / 127.91 / 88.58 ms**, and corrected batches **15.72 / 15.71 / 13.92 ms**. Host contention prevents a reliable speedup percentage; these observations show no obvious unacceptable slowdown. Reducing fourteen connection open/close cycles to one is consistent with lower connection overhead, but this sample does not isolate that cost. No implementation optimization was made from the noisy measurements.

Evidence: `C:/Users/HP/AppData/Local/Temp/astra003-performance/Perf.csproj`, `Program.cs`, `performance.log`, `baseline-comparison.log`, `corrected-comparison.log`. The harness prints a fixed `CORRECTED_BATCH` label even when run with the substituted baseline DLL; the two comparison file names identify the actual binary used. Baseline Infrastructure SHA256: `B8848ACB093F584960BB5B8B42523A80FD1B66E4EAEF6AA287A95425AF7C0FD4`; corrected: `B4464F96AD66EAE3B1676E0BDA5742CDFB9E860E0D38BD297AE5CDD7B132F225`. Baseline metadata has no ReadSnapshotAsync method; corrected metadata does. Both runs create unique disposable databases using production WAL settings and Pooling=False.

The critical WAL writer-completion property is proven deterministically by every concurrent iteration, independently of benchmark timing.

Final Git state: branch `fix/astra-003-dashboard-common-snapshot`; feature commit `6b06d3ed33c72eb3c0291829605ba1d3bb3c3543` pushed successfully to its matching remote branch. Parent baseline remains `50f77a0dabbd5b6e73f68c2dfdea65586be3a02f`; `origin/integration` was not pushed. The four accepted source/test files are committed, with three local reports still untracked. The existing report SHA256 values remain ASTRA-004 `76FF18ABE450FDA383D94A238156CA3B2B100F66FD00F5F13D7CD3E780094177` and ASTRA-005 `3038CE22CA6CCEF8D603A8E17C8D27FDB1B578C02B3F21020FD94A93503DE072`.

Current status: **CLOSED; implementation ready for lead review YES; remaining technical blocker NONE**. ASTRA-004 CLOSED and ASTRA-005 CLOSED remain intact.

### Local integration merge (2026-09-08)

- Frozen feature HEAD: `6b06d3ed33c72eb3c0291829605ba1d3bb3c3543`.
- Integration before merge and `origin/integration`: `50f77a0dabbd5b6e73f68c2dfdea65586be3a02f`.
- Merge command completed without conflicts with message `merge: integrate consistent dashboard read snapshot`.
- Local merge HEAD: `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`.
- Merge scope: exactly 4 files (the two production files and two dedicated tests above); all three reports excluded.
- `integration` is 2 commits ahead of `origin/integration`; **no push was performed** and `origin/integration` remains unchanged.
- Post-merge direct regression and foundation: **1090 PASS / 0 FAIL / 0 SKIP**; build 0 warnings / 0 errors; foundation PASS; no pending model changes; no known vulnerable packages; `git diff --check` PASS.
- Post-merge historical flaky test: the foundation run hit `ApplicationInitializationServiceTests.Concurrent_independent_providers_create_only_one_initial_responsible(sameUsername: True)` at its pre-bootstrap barrier. The required exact-method rerun passed both parameters (2/2), followed by a clean full `verify-foundation` run. Classification remains **UNRELATED TO ASTRA-003**; no test/code change was made.
- Final local integration status: only `ASTRA-003-REPORT.md`, `ASTRA-004-REPORT.md`, and `ASTRA-005-REPORT.md` are untracked; no staged or tracked changes.
- ASTRA-003 INTEGRATION READY FOR LEAD REVIEW: **YES**. Do not push integration or start release-freeze work until separately authorized.

### Accepted freeze and feature push

- Commit full hash: `6b06d3ed33c72eb3c0291829605ba1d3bb3c3543`.
- Commit message: `fix: enforce consistent dashboard read snapshot`.
- Staged scope: exactly 4 files (2 modified, 2 added); all three reports excluded.
- Push: `origin/fix/astra-003-dashboard-common-snapshot` succeeded. Local and remote feature HEADs are identical.
- `git status`: only `ASTRA-003-REPORT.md`, `ASTRA-004-REPORT.md`, `ASTRA-005-REPORT.md` remain untracked; no tracked modifications and no staged files.
- ASTRA-003 FEATURE FROZEN: **YES**. No merge into `integration` was performed.

### Requested implementation report checkpoints

| Items | Result |
|---|---|
| 1. Scope extension | Explicitly approved by lead; Treasury GetSummaryAsync ownership only |
| 2–3. Production/test files | Exact two modified production files and two added test files listed above |
| 4–5. Baseline mixed result / preservation | Original full production reproduction retained in historical audit/log; permanent autocommit reference asserts Sales=0, Collected=4000, Receivables=6000, Cash=4000 |
| 6–8. Mechanism / deferred / owner | SqliteConnection.BeginTransaction(deferred:true) + EF UseTransactionAsync; Dashboard owns attachment, transaction and balanced connection reference |
| 9–11. Treasury borrowed / standalone / nesting | Borrowed owner remains active after success/cancellation/error; standalone owns its original transaction; nested transaction error eliminated |
| 12–13. Whole scope / establishment | All 26 SELECTs use identical connection/transaction; first SELECT establishes snapshot; writer-before-first-read sees all new state |
| 14–16. Writer / busy / operation | Real two-provider ISaleService.CreateAsync commits inside paused read lifetime; no Dashboard-induced SQLITE_BUSY; Sale + payment + allocation |
| 17–20. Iterations / mixtures / first / second response | 50 independent iterations per concurrency test execution, 0 mixtures; first response all pre-state, second all post-state as detailed above |
| 21–28. Metric consistency | Sales/collections/receivables via Sale proof; purchases/payments/payables via Purchase proof; Cash/Bank/Treasury via transfer proof; Result remains checked Sales-Purchases-Expenses |
| 29–30. Extra concurrent writers | Real Treasury transfer at query 26 and real Purchase+payment at query 11; both pass |
| 31–33. Cancellation / failure / borrowed failure | Nine Dashboard outcome/open-owner cases plus three borrowed Treasury outcome cases; no partial result, leaked transaction, ownership violation or unusable next call |
| 34–37. Formulas / dates / read-only / sequencing | Unchanged; sequential awaited SELECTs; no SaveChanges/DML in Dashboard |
| 38–39. Performance | Historical baseline and matched-harness measurements above; noisy, no optimization or reliable percentage claim |
| 40–41. ASTRA-004/005 | No production or existing test changes; remain CLOSED |
| 42. Historical intermittent test | UNRELATED barrier timeout before BootstrapResponsibleAsync; unchanged; no recurrence in either observed complete 1090-test run |
| 43–48. Desktop / Application / Domain / DbContext / schema / packages | All NO |
| 49–52. PASS / FAIL / SKIP / build | Direct and foundation full runs each 1090 / 0 / 0; both builds 0 warnings / 0 errors |
| 53–56. Foundation / vulnerabilities / pending model / migrations | PASS / none reported by configured direct-transitive scan / none / exact 001–004 (full IDs in historical inventory, unchanged) |
| 57–58. Diff / reports | git diff --check PASS; all three reports local/untracked; ASTRA-004/005 report contents unchanged |
| 59–61. Status / blocker / review readiness | CLOSED and frozen; no remaining technical blocker; ASTRA-003 IMPLEMENTATION READY FOR LEAD REVIEW: YES |

## Historical audit before scope approval

Audit date: 2026-09-08. At this stage ASTRA-003 was OPEN and implementation was not yet applied.

The current baseline deterministically returns a mixed Dashboard snapshot. A deferred SQLite transaction solves the underlying read-consistency problem on the installed stack, but a Dashboard-only wrapper fails because `TreasuryService.GetSummaryAsync` unconditionally starts a second transaction. The requested section 28 requires stopping before expanding the implementation scope. The proposed extension is confined to transaction ownership in that Treasury method, in addition to the Dashboard service and tests. No production/test source has been changed.

## Baseline and workspace

- Initial branch: `integration`.
- HEAD and `origin/integration`, before and after fetch: `50f77a0dabbd5b6e73f68c2dfdea65586be3a02f`.
- Created branch: `fix/astra-003-dashboard-common-snapshot`; HEAD remains the same baseline.
- Initial tracked tree clean, only ASTRA-004/005 reports untracked.
- This report is the only repository addition. Both existing reports remain untouched and untracked.
- No staging, commit, push, merge, reset, rebase, amend or clean.

## Exact implementation inventory

All following paths are relative to `C:/Dev/logiciel-gestion-financiere`.

| File | Methods / responsibility |
|---|---|
| src/FinancialManagement.Application/Dashboard/IFinancialDashboardService.cs | GetAsync contract, optional CancellationToken |
| src/FinancialManagement.Application/Dashboard/FinancialDashboardQuery.cs | Nullable DateOnly StartDate / EndDate |
| src/FinancialManagement.Application/Dashboard/FinancialDashboardResult.cs | Succeeded / Failed; 11 financial metrics, filter echoes, Treasury initialization flag/date |
| src/FinancialManagement.Application/Dashboard/FinancialDashboardFailureReason.cs | None, NotAuthenticated, Forbidden, InvalidDateRange |
| src/FinancialManagement.Infrastructure/Dashboard/FinancialDashboardService.cs | GetAsync; SumSalesAsync, SumPurchasesAsync, SumCustomerCollectionsAsync, SumSupplierPaymentsAsync, SumExpensesAsync; CalculateCustomerReceivablesAsync, CalculateSupplierPayablesAsync, CalculateOutstanding; ApplyDateFilter / BuildComparison |
| src/FinancialManagement.Infrastructure/Treasury/TreasuryService.cs | GetSummaryAsync, EnsurePersistedEnumsAreValidAsync and underlying sums; owns its own transaction; included in every successful Dashboard response |
| src/FinancialManagement.Infrastructure/Authorization/AuthorizationService.cs | CanAccessResponsibleCapability: session/role only, no SQL for Dashboard authorization |
| src/FinancialManagement.Infrastructure/InfrastructureServiceCollectionExtensions.cs | AddInfrastructure: scoped DbContext, Dashboard, Treasury, authorization; singleton current session |
| src/FinancialManagement.Infrastructure/SqlitePragmaConnectionInterceptor.cs | ConnectionOpened / ConnectionOpenedAsync: foreign_keys ON, WAL, synchronous FULL, busy_timeout 5000 |
| src/FinancialManagement.Infrastructure/FinancialManagementDbContext.cs | Shared scoped EF context and DbSets, unchanged |
| src/FinancialManagement.Desktop/App.xaml.cs | Dashboard registrations; ShowShellAsync creates shell scope and waits for StopAsync before disposing it |
| src/FinancialManagement.Desktop/ViewModels/DashboardViewModel.cs | LoadAsync, RefreshAsync, LoadInitialAsync, LoadCoreAsync, ApplyResult, TryCreateQuery, Stop / StopAsync |
| src/FinancialManagement.Desktop/Services/DashboardOperationCoordinator.cs | RunAsync serializes UI dashboard/related work through a SemaphoreSlim |
| src/FinancialManagement.Desktop/Views/DashboardView.xaml | Bindings and Dashboard cards |
| src/FinancialManagement.Desktop/Views/DashboardView.xaml.cs | DashboardView_OnLoaded invokes ViewModel.LoadAsync once per VM initialization |
| src/FinancialManagement.Desktop/ViewModels/DashboardMetricViewModel.cs | Formatted display values only |
| tests/FinancialManagement.Tests/FinancialDashboardServiceTests.cs | Existing semantics, permissions, date bounds, overallocations, pre-cancellation, overflow, read-only assertions; file SQLite with production AddInfrastructure settings |

The WPF ViewModel calls one backend GetAsync for all cards. It applies one result after cancellation/session checks. Its coordinator prevents competing operations on its shared scope but cannot prevent another provider/process writing SQLite. Due Alerts is separately loaded after the initial Dashboard call and is not part of FinancialDashboardResult. No WPF reconciliation/change is needed.

## Business formulas and date semantics (unchanged)

Period comparisons are inclusive; either boundary can be null. A reversed bounded interval is rejected before financial reads.

| Metric | Existing calculation |
|---|---|
| Sales | SUM Sales.TotalAmountMinor using SaleDate within selected period |
| Purchases | SUM Purchases.TotalAmountMinor using PurchaseDate within period |
| Customer collections | SUM CustomerPayments.AmountMinor using PaymentDate within period |
| Supplier payments | SUM SupplierPayments.AmountMinor using PaymentDate within period |
| Expenses | SUM Expenses.AmountMinor using ExpenseDate within period |
| Result | checked(Sales - Purchases - Expenses); excludes collections/payments and Treasury transfers |
| Current customer receivables | Sum over all Sales of TotalAmountMinor minus allocations by SaleId, plus all InitialCustomerReceivables.InitialAmountMinor minus allocations by initial receivable ID |
| Current supplier payables | Sum over all Purchases of TotalAmountMinor minus allocations by PurchaseId, plus all InitialSupplierPayables.InitialAmountMinor minus allocations by initial payable ID |
| Current Cash | Opening Cash (or 0) + all cash customer payments - all cash supplier payments - all cash expenses - cash-to-bank transfers + bank-to-cash transfers |
| Current Bank | Opening Bank (or 0) + all bank customer payments - all bank supplier payments - all bank expenses + cash-to-bank transfers - bank-to-cash transfers |
| Current Total Treasury | checked(Cash + Bank) |

Current stocks are unfiltered by Dashboard dates. OpeningDate is returned as metadata; it is not a cutoff for these sums. Initial obligations are stocks, not period flows. Advances are counted as payment flows when received/paid; allocation changes outstanding balances without counting another cash flow. Long minor units and checked CLR arithmetic remain unchanged; SQLite SUM overflow remains a technical error. CalculateOutstanding throws if allocation exceeds obligation; no clamping or repair. Dashboard does not directly call AllocationIntegrity; the existing aggregate/read guards elsewhere and Dashboard's own over-allocation behavior must be preserved, not overstated as newly added protections.

## Exact baseline read sequence

The probe captured **26 financial SELECTs**, in order, for a successful valid-data Dashboard response. Authorization uses no SQL here. Invalid enum data can short-circuit Treasury's validation and throw before completing this list.

1. Sales period sum.
2. Purchases period sum.
3. CustomerPayments period sum.
4. SupplierPayments period sum.
5. Expenses period sum.
6. All Sales IDs/amounts.
7. Customer allocations grouped by SaleId.
8. All InitialCustomerReceivables IDs/amounts.
9. Customer allocations grouped by initial receivable ID.
10. All Purchases IDs/amounts.
11. Supplier allocations grouped by PurchaseId.
12. All InitialSupplierPayables IDs/amounts.
13. Supplier allocations grouped by initial payable ID.
14. Invalid customer payment method EXISTS.
15. Invalid supplier payment method EXISTS.
16. Invalid expense payment method EXISTS.
17. Invalid Treasury transfer accounts EXISTS.
18. TreasuryOpeningBalance single row (LIMIT 2 for cardinality validation).
19. Cash customer payments sum.
20. Bank customer payments sum.
21. Cash supplier payments sum.
22. Bank supplier payments sum.
23. Cash expenses sum.
24. Bank expenses sum.
25. Cash-to-bank transfer sum.
26. Bank-to-cash transfer sum.

One scoped DbContext and its SqliteConnection object are used, including the injected TreasuryService. They do **not** imply one continuously open SQLite snapshot: EF opened/closed the connection **14 times** in the probe. Reads 1–13 had no DbTransaction; Treasury held its separate transaction for reads 14–26. With probe/existing Dashboard fixture Pooling=False, connection closure also closes the physical handle. A pooling-enabled connection also does not preserve a read transaction across closed EF operations.

Every query is awaited sequentially. A representative first query is `SELECT COALESCE(SUM("s"."TotalAmountMinor"), 0) FROM "Sales" AS "s"`. The complete 26 generated SQL statements are preserved in the external evidence log.

## Deterministic baseline reproduction

External disposable harness (outside repository):

- `C:/Users/HP/AppData/Local/Temp/astra003-audit/Probe.csproj`
- `C:/Users/HP/AppData/Local/Temp/astra003-audit/Program.cs`
- `C:/Users/HP/AppData/Local/Temp/astra003-audit/probe-output.log`

The probe references the actual Infrastructure project, uses two independent AddInfrastructure providers/scopes/connections and real authentication. It creates/migrates a unique temporary database, seeds only a responsible user and client, and never opens a canonical/user database. A DbCommandInterceptor pauses at the second Dashboard SELECT. A TaskCompletionSource releases a second task after the first SELECT has completed; this task invokes **ISaleService.CreateAsync** with total 10000 and initial cash payment 4000. The production service commits Sale + CustomerPayment + allocation atomically. Only after successful commit does the interceptor release the remaining Dashboard reads. No Thread.Sleep or timing-based interleaving.

Amounts below are integer minor units.

| State | Sales | Collected | Receivables | Cash | Bank | Treasury total | Result |
|---|---:|---:|---:|---:|---:|---:|---:|
| Before writer | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| After writer, new complete Dashboard call | 10000 | 4000 | 6000 | 4000 | 0 | 4000 | 10000 |
| **Observed interleaved response** | **0** | **4000** | **6000** | **4000** | **0** | **4000** | **0** |

The exact mixed response is asserted by the probe. Risk **CONFIRMED** on baseline 50f77a0, not inferred from historical implementation. Treasury's own Cash+Bank arithmetic remains internally consistent in this response; its state is nevertheless newer than the Sales KPI.

## Installed SQLite/EF behavior proven locally

- EF Core / Microsoft.Data.Sqlite assembly versions: **10.0.11.0**.
- SQLite runtime: **3.53.3**.
- Read-back PRAGMA values: journal_mode=wal; foreign_keys=1; synchronous=2 (FULL); busy_timeout=5000.
- The probe's extra Default Timeout=1 is only to bound its deliberate writer-lock contention test; WAL/FK/synchronous settings come from the production interceptor.
- Default `Database.BeginTransactionAsync()` blocked a second writer's BeginTransaction(deferred:false), producing **SQLITE_BUSY**. This is unsuitable for the requested non-write-locking Dashboard solution.
- `SqliteConnection.BeginTransaction(deferred:true)` attached through `Database.UseTransactionAsync` allowed the real production Sale writer to commit while the reader transaction remained active: approximately **18.67 ms** in this small sample.
- Reader first selected Sales=10000; writer then committed another Sale=20000/payment=8000; subsequent EF reads in the original transaction still returned Sales=10000 and Payments=4000. After disposal, a new Dashboard returned Sales=30000.
- A separate deferred transaction was begun without any SELECT; another writer committed Sale=1000 before its first SELECT; this first SELECT saw Sales=31000. Snapshot establishment is at the first read, not merely BEGIN deferred.
- These experiments prove the relevant mechanism using installed binaries rather than relying only on generic documentation.

## Scope blocker and concrete minimal proposal

Wrapping the **unchanged production Dashboard** with an externally opened deferred transaction and UseTransactionAsync reproduced this exact error at Treasury's nested BeginTransactionAsync:

> The connection is already in a transaction and cannot participate in another transaction.

The exception is asserted by the disposable probe; the next Dashboard call succeeds after disposal. A Dashboard-only wrapper cannot cover all 26 reads while Treasury continues unconditionally owning a new transaction.

Proposed production changes (not applied):

1. `src/FinancialManagement.Infrastructure/Dashboard/FinancialDashboardService.cs`: after existing authorization/date checks, explicitly open the connection, begin a deferred SQLite transaction, attach it to EF, await all existing financial reads including Treasury, commit only after a complete result, and dispose/detach/close in all exit paths. Keep formulas and queries sequential and unchanged. Close only the open reference owned by this call.
2. **Additional file requiring scope extension:** `src/FinancialManagement.Infrastructure/Treasury/TreasuryService.cs`, **GetSummaryAsync only**: reuse an existing EF transaction when supplied by Dashboard; begin/commit/dispose its own existing transaction only when it owns one. Never commit/dispose Dashboard's transaction. Standalone Treasury formulas, authorization, enum guards and write methods stay unchanged. No DTO, DI, Desktop, schema, package or money-type change is required.
3. Tests: retain a test-only baseline reference or recorded reproduction, then exercise corrected production Dashboard with the same barriers for 50 iterations; a second call must see the complete post-write state. Also force a Treasury transfer within Treasury's underlying reads, test mid-query cancellation/error cleanup, and verify standalone/borrowed Treasury transaction ownership. Run the full requested regression afterwards.

This is a transaction-composition change, not a metrics redesign. Alternatives that duplicate Treasury formulas in Dashboard, suppress transaction starts through a production interceptor, or acquire a writer lock are not proposed.

The user instruction explicitly states in section 28: **“If broader changes become necessary: STOP and explain before expanding scope.”** Work is stopped before editing Treasury or Dashboard pending this narrowly defined extension. This stop comes from the task instruction, not an inferred skill approval requirement.

## Historical intermittent test

Identified exactly from prior observed output and current source:

`ApplicationInitializationServiceTests.Concurrent_independent_providers_create_only_one_initial_responsible(sameUsername: True)`.

The earlier failure was `Assert.True` at line 120: `start.SignalAndWait(TimeSpan.FromSeconds(15))` returned false, **before BootstrapResponsibleAsync was called**. It involved a Barrier inside two Task.Run delegates, not any Dashboard read. Classification: **UNRELATED to ASTRA-003; synchronization timeout in the test harness**. Scheduling/resource contention is a possible reason for the missing participant, but precise host scheduling causality was not recorded and is not claimed proven. Subsequent complete suites passed. Test and initialization production code remain unchanged. No failure was encountered by the current audit probe.

## Performance and remaining validation

After three warmups, twenty unchanged-production Dashboard calls on a tiny dataset (3 sales, 2 payments, 2 allocations) averaged **3.62 ms/call**. This is a small baseline observation, not a representative large-dataset benchmark or an overhead comparison. Corrected production performance is not measured because the production fix is not applied. The 18.67 ms writer sample above demonstrates writer commit during a deferred reader, not a benchmark guarantee.

The audit probe completed with exit code 0 (`AUDIT_PROBE_PASS`). `dotnet ef migrations has-pending-model-changes --project src/FinancialManagement.Infrastructure --no-build`: **no changes**. `git diff --check`: **PASS**. The requested post-implementation full regression has not been run; there is no implemented fix to validate yet. The last officially validated baseline remains **1071 PASS / 0 FAIL / 0 SKIP**, build **0 warnings / 0 errors**, foundation **PASS**, no reported vulnerable packages. Those baseline results are historical and are not presented as new audit-run results.

Migrations remain exactly:

1. `20260903232954_001_InitialSchema`
2. `20260904004853_002_AllowAnonymousSalePayments`
3. `20260904111121_003_TreasuryOpeningBalanceSingleton`
4. `20260908072131_004_EnforceFinancialIntegerStorageClass`

## Requested 61-item report status

| Item(s) | Result |
|---|---|
| 1–2. Baseline / branch | 50f77a0dabbd5b6e73f68c2dfdea65586be3a02f / fix/astra-003-dashboard-common-snapshot |
| 3–6. Files / metrics / formulas / dates | Inventoried above; all production unchanged; flows period-filtered, stocks current |
| 7–10. Reads / lifetime / connections / transactions | 26 ordered SELECTs; same scoped context; 14 open/close cycles; first 13 outside transaction, final 13 Treasury transaction |
| 11–16. Risk / reproduction / states / writer | YES; exact pre/post/mixed values above; real atomic Sale + cash payment + allocation |
| 17–19. Versions / WAL / snapshot establishment | EF+provider 10.0.11; SQLite 3.53.3; WAL verified; first-read snapshot establishment proven |
| 20–24. Mechanism / blocking / whole scope / sequential / read-only | Deferred SqliteTransaction + UseTransactionAsync proven in probe; writer committed; whole production wrapping blocked by Treasury nested transaction; existing queries sequential and read-only |
| 25–26. Cancellation / failure cleanup | Current pre-cancellation test exists; nested-wrapper failure cleanup verified in probe; corrected mid-query cancellation/error cleanup tests pending implementation |
| 27–30. Post-fix concurrency / iterations / mixed count / next call | Not yet implemented; one deterministic baseline reproduction; deferred provider experiment retains pre-state and new call sees post-state; no 50-iteration production claim |
| 31–32. Treasury / receivables / payables | Formulas documented; Treasury already has its own snapshot, but not shared with earlier Dashboard reads; outstanding reads need the same common snapshot |
| 33–34. ASTRA-004 / ASTRA-005 | Untouched; no guard, allocation, storage, startup, Restore or schema changes |
| 35–36. Previous intermittent test | Identified; UNRELATED synchronization timeout before bootstrap, not Dashboard; unchanged |
| 37–38. Performance | Baseline tiny-dataset 3.62 ms average; corrected production not measured |
| 39–40. Production / test files changed | NONE / NONE; only external disposable probe and this local report created |
| 41–46. Desktop / Application / Domain / DbContext / schema / packages | NO changes in any category |
| 47–52. Tests / failures / skips / build / foundation / vulnerabilities | Historical official baseline 1071/0/0, build 0/0, foundation PASS, no known vulnerabilities reported; new post-fix regression pending |
| 53–55. Pending model / migrations / diff check | None / exact 001–004 / PASS |
| 56–58. Reports / ASTRA-004 / ASTRA-005 | Three reports local/untracked; ASTRA-004 CLOSED; ASTRA-005 CLOSED |
| 59. ASTRA-003 STATUS | **OPEN** |
| 60. Remaining blocker | Scope extension to TreasuryService.GetSummaryAsync transaction ownership, required by nested-transaction proof |
| 61. IMPLEMENTATION READY FOR LEAD REVIEW | **NO — audit and proposed scope are ready; implementation awaits scope decision** |
