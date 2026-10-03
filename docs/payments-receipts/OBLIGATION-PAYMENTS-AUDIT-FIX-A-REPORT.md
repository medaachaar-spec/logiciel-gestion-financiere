# Obligation Payments Audit Fix Pack A Report

## 1. Starting audited SHA

`7567a97c4a6c4e1f47b5007c775a9d236b0cf2e0`

The worktree was clean of tracked changes before the branch was created. Existing untracked audit and recovery artifacts were left untouched. The current `integration` reference was not modified.

## 2. Fix branch

`fix/obligation-payments-audit-v1`

The branch still points at the audited SHA because this task intentionally creates no commit.

## 3. Root-cause summary

- AOP-01: raw SQL allocation totals drove status filtering, count, and pagination before authoritative validation, so a corrupt obligation could be filtered out.
- AOP-02: the shared allocation integrity reader did not validate the contributing payment's party, method, or reference.
- AOP-03: SQLite `SUM` could overflow before the application could return a controlled integrity result.
- AOP-04: header, allocation, count/page, and history reads could observe different committed states.
- AOP-06: required-party inner joins removed orphaned obligation headers before integrity validation.
- AOP-07: initial ViewModel loading applied a failed page payload as an empty successful page.

## 4. Files changed

Production:

- `src/FinancialManagement.Infrastructure/SqliteReadSnapshot.cs` (new)
- `src/FinancialManagement.Infrastructure/AllocationIntegrity.cs`
- `src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentObligationReadService.cs`
- `src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentObligationReadService.cs`
- `src/FinancialManagement.Desktop/ViewModels/PaymentManagementViewModelBase.cs`

Tests:

- `tests/FinancialManagement.Tests/PaymentObligationAuditFixTests.cs` (new)
- `tests/FinancialManagement.Tests/PaymentObligationReadServiceTests.cs`
- `tests/FinancialManagement.Tests/PaymentReadServiceTests.cs`
- `tests/FinancialManagement.Desktop.Tests/PaymentInitialLoadFailureTests.cs` (new)

Report:

- `OBLIGATION-PAYMENTS-AUDIT-FIX-A-REPORT.md` (new, untracked)

## 5. Authoritative projection design

Each page read now follows this sequence:

1. Validate authentication, authorization, and request inputs.
2. Open one deferred SQLite read transaction on the service's existing `DbContext`.
3. Select all obligation headers matching only the search and date candidate predicates.
4. Batch-load and validate allocations for every candidate target and every allocation belonging to a contributing payment.
5. Compute paid totals with bounded integer accumulation.
6. Validate headers and derive remaining amount and status from those authoritative totals.
7. Apply the status filter in memory, then count, order, and paginate the validated items.

Each detail read uses the same deferred snapshot for its header, integrity, and history reads. There is no mutable balance state, new cache, or schema change.

## 6. AOP-01 correction and proof

The raw SQL financial status predicate was removed. Status is derived only after every search/date candidate has passed authoritative integrity validation. An overallocated candidate therefore fails the whole controlled read before any status filter can omit it.

Permanent theory coverage verifies `Outstanding`, `PartiallyPaid`, and `Settled` filters for customer and supplier obligations with original amount 1,000 and allocation 1,200. All six cases return `DataIntegrityViolation` with no items.

## 7. AOP-02 correction and proof

`AllocationIntegrity` now projects and validates:

- the payment exists and has a positive amount;
- payment party equals target party;
- payment method is a defined `PaymentMethod` value;
- payment reference is nonblank;
- allocation amount and target relationship are valid;
- each target total and each contributing payment total stay within their respective amounts.

Permanent customer and supplier tests cover cross-party persisted allocations, unsupported stored methods, and blank stored references. Both page and detail reads fail with `DataIntegrityViolation`.

## 8. AOP-03 correction and proof

The obligation readers no longer issue raw SQLite `SUM` expressions for financial authority. `AllocationIntegrity.TryAccumulate` rejects an addition when `amount > maximum - current`, then performs a checked integer addition. It never uses floating point, wraps, clamps, or saturates.

Permanent customer and supplier tests prove:

- `long.MaxValue + 1` returns controlled `DataIntegrityViolation` without a SQLite exception;
- one exact `long.MaxValue` allocation against a `long.MaxValue` obligation succeeds as `Settled`.

## 9. AOP-04 snapshot design and proof

`SqliteReadSnapshot.ExecuteAsync` opens the existing SQLite connection, begins a deferred transaction, attaches it to the existing `DbContext`, runs the complete operation, commits, and closes the connection. An already active transaction is reused. No immediate or exclusive read lock is introduced.

Deterministic `DbCommandInterceptor` tests commit an allocation from a separate connection immediately after the first transactional reader is executed. Customer and supplier page tests use the `Outstanding` filter and prove one coherent pre-settlement result: count 1, one returned item, paid 0, status `Outstanding`. This excludes mixed count/page and filter/status states. Customer and supplier detail tests prove the concurrent allocation cannot appear between integrity and history and cause a false integrity failure. The tests use command boundaries and no sleeps.

## 10. AOP-06 correction and proof

Customer initial receivables, supplier purchases, and supplier initial payables now preserve their headers with left joins. Customer sales already used the required header-preserving shape. Header validation rejects missing required parties and rejects a non-null orphan customer sale `ClientId`.

Permanent tests remove the related party from a temporary migrated database and verify `DataIntegrityViolation`, rather than an empty successful page or `NotFound`, for all four required cases.

## 11. AOP-07 correction and proof

Initial ViewModel loading now checks `payload.Result.Success` before `ApplyPage` and routes failures through the existing `HandleFailure` path. Authorization failures remain fail-closed.

The permanent desktop test returns `DataIntegrityViolation` from the obligation reader and proves both initial load and manual refresh show `Les données d'affectation sont incohérentes.`, keep the list empty, and do not display the ordinary empty-state presentation.

## 12. Anonymous-sale handling

Customer payment party compatibility uses equality of nullable party identifiers. A sale with `ClientId = null` and its legitimate anonymous payment with `ClientId = null` remains valid. A non-null sale client remains required and must resolve to a nonblank client header. A permanent anonymous sale/payment test succeeds as settled.

## 13. Repeated payment-target allocation preservation

No uniqueness constraint or duplicate-row rejection was added. Permanent customer and supplier tests allocate 400 and then 600 from one payment to the same target and verify a paid total of 1,000 with both history rows preserved.

## 14. New query architecture

For an ordinary page, the measured shape is three batched reads:

1. candidate obligation headers;
2. allocation, payment, and target rows for all candidates;
3. all allocation rows for contributing payments.

Status, count, ordering, and pagination then run over the validated in-memory projection. Detail uses a header read, batched integrity reads, and a history read within one snapshot.

## 15. N+1 assessment

The query-count regression measures one candidate and twenty-one candidates. Both execute exactly three page read commands. Query count does not grow one query per obligation. This V1 intentionally validates all search/date candidates before pagination so no invalid candidate can escape because it falls outside the final page.

## 16. Test fixture correction

The existing valid customer multi-obligation test now uses two sales belonging to Atlas for the shared Atlas payment. Cross-party corruption is covered separately and is expected to fail.

## 17. Tests added

Permanent coverage now includes:

- every status filter hiding over-allocation, both sides;
- cross-party payments, both sides;
- invalid stored method and blank reference, both sides;
- overflow beyond and exact equality at `long.MaxValue`, both sides;
- deterministic concurrent page settlement and detail allocation, both sides;
- all four required-party orphan cases;
- legitimate anonymous sale/payment;
- repeated payment-target allocation rows, both sides;
- typed document/initial ID collisions, both sides;
- initial ViewModel failure and matching manual refresh behavior;
- explicit three-query batching at one and twenty-one candidates.

Existing coverage remains for same-party multi-obligation payments, advances, B01 cross-user reads, authorization, history progression, search/status/pagination, initial balances, and result semantics.

## 18. Targeted results

- Obligation backend filter: **74 passed, 0 failed, 0 skipped**.
- AOP-07 desktop filter: **1 passed, 0 failed, 0 skipped**.

## 19. Full-suite result

`dotnet test FinancialManagement.sln --no-build` passed normally and unfiltered:

- `FinancialManagement.Tests`: **1,166 passed, 0 failed, 0 skipped** in 3m25s.
- `FinancialManagement.Desktop.Tests`: **17 passed, 0 failed, 0 skipped** in 17s.

No concurrency timeout occurred. Foundation repeated the suites successfully: 1,166 backend tests in 3m30s and 17 desktop tests in 14s.

## 20. Build result

`dotnet build FinancialManagement.sln` passed with **0 warnings and 0 errors**.

## 21. Foundation

`scripts\verify-foundation.cmd`: **PASS** (`Verification succeeded.`).

## 22. Migrations

Only migrations 001–004 are present:

- `001_InitialSchema`
- `002_AllowAnonymousSalePayments`
- `003_TreasuryOpeningBalanceSingleton`
- `004_EnforceFinancialIntegerStorageClass`

No migration 005 was created.

## 23. Schema drift

`dotnet ef migrations has-pending-model-changes`: **no pending model changes**.

## 24. Vulnerabilities

`dotnet list ... package --vulnerable --include-transitive`: **no vulnerable Infrastructure packages found from the configured sources**.

## 25. ProgramData touched

**NO.** All corruption and concurrency tests used temporary synthetic migrated databases. `C:\ProgramData\FinancialManagement` was not accessed or modified.

## 26. Git diff check

`git diff --check`: **PASS** with no output.

## 27. Git status

Tracked modifications are limited to the six existing production/test files listed above. The three new implementation/test files and this report are untracked. Pre-existing untracked audit, recovery, UAT, and `doc/` artifacts remain untouched. No files were staged, committed, pushed, merged, rebased, reset, or cleaned.

Branch: `fix/obligation-payments-audit-v1`

HEAD: `7567a97c4a6c4e1f47b5007c775a9d236b0cf2e0`

## 28. AOP-05 intentionally deferred

**YES.** Advance UI reachability was not changed and remains assigned to DEV2.

## 29. Remaining audit findings

AOP-05 remains open by explicit task scope. No assigned Pack A finding remains unresolved. Broader advance-entry and quick-flow UX/test recommendations from the audit remain outside this backend/read-integrity pack.

## 30. Final result

**PASS**

The requested AOP-01, AOP-02, AOP-03, AOP-04, AOP-06, and AOP-07 corrections are implemented, permanently covered, and fully validated. No commit or push was performed.
