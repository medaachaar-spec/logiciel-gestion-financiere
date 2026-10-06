# AOP-05 advance allocation UI fix report

Date: 2026-09-11

## 1. Starting SHA

`a9a04cd911c809c70eecc33cc6ad5febbf2dd440`

The SHA exactly matches `origin/fix/obligation-payments-audit-v1` after `git fetch origin`.

## 2. Working branch

`fix/advance-allocation-ui-v1`

The branch was created directly from the required Fix Pack A commit. No merge, rebase, reset, commit, or push was performed.

## 3. Old workflow investigation

At checkpoint `61180ffc6c85eed0e1bd8fc8a97ba7ca53f89ebe`, the main screen was payment-centric. A user opened a payment row, `OpenDetailAsync` loaded `PaymentDetailData` into `_selectedDetail`, and the payment-detail footer exposed `ShowAdvanceCommand` when `CanApplySelectedAdvance` was true. The dialog then derived the source payment id, party id, and available amount from `_selectedDetail`, loaded same-party eligible targets, and called the existing advance service.

## 4. Root cause

The obligation-centric redesign changed the main rows and Details workflow to load `PaymentObligationDetailData`. `ApplyObligationDetail` intentionally clears `_selectedDetail`, and the old payment-detail footer action was removed. `ShowAdvanceCommand`, its ViewModel state, the advance dialog, obligation lookup, and backend services remained, but no current UI path could populate a source payment and invoke the command. This was a UI reachability regression, not missing financial behavior.

## 5. UX route chosen

A permission-scoped secondary action, **Affecter une avance**, now appears beside the existing new-payment action. It opens a dedicated source-payment selector, then reuses the existing **Affecter une avance** allocation dialog. The obligation-centric main list remains unchanged.

## 6. Source-payment selection design

The selector searches by payment reference or party and displays:

- payment reference;
- party;
- payment date;
- payment method;
- original payment amount;
- already allocated amount;
- available amount.

It reuses `ICustomerPaymentService.GetPageAsync` / `ISupplierPaymentService.GetPageAsync`, scans their validated pages, and presents only named-party payments with `UnallocatedAmountMinor > 0`. Selecting a row performs an authoritative `GetByIdAsync` refresh before opening allocation. A stale or exhausted source remains in the selector with controlled feedback and cannot reach the target step.

## 7. Customer workflow

TECHNICAL PASS. The customer route loads eligible existing encaissements, validates the selected source, constrains target lookup to the source client with `ApplyAdvance` purpose, displays the available amount, and invokes `ICustomerAdvanceService.ApplyAsync`. The automated presentation workflow proves a 1,000.00 payment with 700.00 already allocated can explicitly allocate its remaining 300.00 and refresh the list/detail to Paid 1,000.00, Remaining 0.00, Settled, with two history rows.

## 8. Supplier workflow

TECHNICAL PASS. The shared route is exercised through `SupplierPaymentsViewModel`, loads an existing supplier payment with 300.00 available, validates its source detail, constrains lookup to that supplier, and opens the same allocation dialog with supplier-specific wording.

## 9. Permission behavior

PASS. Route visibility and command execution use the existing `CanApplyAdvancePermission`, which remains derived from payment View + Modify permissions. A permanent test proves a user without Modify cannot open or execute the route. `CustomerAdvanceService` and `SupplierAdvanceService` remain authoritative and continue to demand `PermissionAction.Modify`; Create semantics were not substituted.

## 10. Party safety

PASS. UI target lookup receives the party id from the freshly reloaded source payment detail. Existing AOP-02 `AllocationIntegrity` and advance-service party checks remain unchanged and authoritative. The existing real-SQLite tests for cross-client and cross-supplier attempts pass.

## 11. Backend services reused

YES:

- `ICustomerPaymentService` / `ISupplierPaymentService` for source enumeration and detail revalidation;
- `ICustomerPaymentObligationLookupService` / `ISupplierPaymentObligationLookupService` with `ApplyAdvance` purpose;
- `ICustomerAdvanceService.ApplyAsync` / `ISupplierAdvanceService.ApplyAsync` for the write;
- obligation read services for immediate list/detail refresh.

No new financial write service, calculation, persisted balance, table, or migration was introduced.

## 12. No-new-payment proof

PASS. Existing real-SQLite `AdvanceServiceTests` verify the source payment record is unchanged and payment count does not increase across advance application. The new customer presentation test also proves the create-payment service is never invoked and the 1,000.00 source amount/count remain unchanged.

## 13. No-double-treasury proof

PASS. Existing `AdvanceServiceTests` and `TreasuryServiceTests` ran 86/86 and prove explicit advance allocation creates only allocation rows: payment totals/counts and treasury opening/transfer/payment-derived amounts are unchanged. No treasury service is called by the restored UI workflow.

## 14. Repeated payment-target proof

PASS. Existing real-SQLite coverage applies one payment to the same target in multiple stages and passed unchanged. The new UI workflow test models the accepted 700.00 then 300.00 same-payment/same-sale case and verifies both history rows. No payment-target uniqueness constraint was added.

## 15. Stale/concurrent target result

PASS. A stale source with no remaining amount is rejected before target selection with controlled feedback. Existing real-SQLite tests cover over-available amounts, settled/outstanding changes, missing targets, cross-party targets, corrupt sources, permission denial, and concurrent source/target contention without partial writes. The unchanged allocation concurrency theory passed 40/40 in isolated verification.

## 16. Refresh behavior

PASS. After a successful apply, the current obligation page reloads immediately. The first affected target is then re-read through the obligation detail service, and the obligation Details modal opens with refreshed Paid, Remaining, Status, and full chronological history. This replaces the obsolete post-redesign behavior that reloaded payment detail into an obligation-detail UI.

## 17. Files changed

- `src/FinancialManagement.Desktop/ViewModels/CustomerPaymentsViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/PaymentManagementViewModelBase.cs`
- `src/FinancialManagement.Desktop/ViewModels/PaymentPresentationModels.cs`
- `src/FinancialManagement.Desktop/ViewModels/SupplierPaymentsViewModel.cs`
- `src/FinancialManagement.Desktop/Views/PaymentManagementView.xaml`
- `tests/FinancialManagement.Desktop.Tests/Aop05AdvanceWorkflowTests.cs` (untracked permanent test)
- `AOP-05-ADVANCE-UI-FIX-REPORT.md` (this untracked report)

No Application, Infrastructure, Domain, schema, migration, Dashboard, Reports, Backup/Restore, or unrelated UI file changed.

## 18. Tests added

Five permanent Desktop tests in `Aop05AdvanceWorkflowTests.cs`:

1. visible permission-scoped route, required source columns, Enter-search, and one Annuler per dialog;
2. customer source selection, explicit apply, same payment/target history, and immediate list/detail refresh;
3. supplier workflow symmetry and supplier-specific target language;
4. unauthorized Modify denial;
5. stale/exhausted source rejection before allocation.

## 19. Targeted results

- New AOP-05 Desktop tests: 5/5 PASS.
- Existing real-SQLite `AdvanceServiceTests` + `TreasuryServiceTests`: 86/86 PASS.
- Unchanged allocation concurrency theory isolated rerun: 40/40 PASS.

## 20. Full-suite total

PASS: **1188/1188** (1166 core + 22 Desktop), 0 failed, 0 skipped.

The first full run had one unrelated transaction-gate timeout in an unchanged `AllocationIntegrityTests` theory case (1187/1188). That theory immediately passed 40/40 in isolation, and the complete rerun passed 1188/1188. No timeout, assertion, or concurrency test was weakened.

## 21. Build

PASS: `dotnet build FinancialManagement.sln` completed with 0 warnings and 0 errors.

An earlier Desktop-only compile attempt was blocked solely by a running `FinancialManagement.Desktop` process locking output DLLs. After closing that repository application process, the Desktop build and required full solution build both passed with 0 warnings and 0 errors.

## 22. Foundation

PASS: `scripts\verify-foundation.cmd` exited 0 with `Verification succeeded.` and 1188/1188 tests.

The first Foundation attempt encountered the same unchanged transaction-gate timeout; the unmodified isolated theory and full suite had already passed, and the complete Foundation rerun passed. No concurrency setting or test was altered.

## 23. Migrations

UNCHANGED: 001–004 only. No migration 005 exists.

## 24. Schema drift

NONE. EF reports: `No changes have been made to the model since the last migration.`

## 25. Vulnerabilities

NONE. Foundation reports no vulnerable direct or transitive Infrastructure package.

## 26. ProgramData touched

NO. Automated and adversarial tests used their existing temporary migrated SQLite databases. No command opened or modified the live ProgramData database, and manual UAT did not run.

## 27. git diff --check

PASS.

## 28. git status

Expected uncommitted state on `fix/advance-allocation-ui-v1` at `a9a04cd911c809c70eecc33cc6ad5febbf2dd440`:

- five modified tracked Desktop files listed above;
- one untracked permanent AOP-05 Desktop test;
- this untracked AOP-05 report;
- eight previously preserved untracked reports/artifacts.

Nothing is staged. No commit or push was performed.

## 29. Manual visual result

PARTIAL. The Computer Use capability exposed no native Windows applications (`apps: []`; only the in-app browser surface was available), so the requested 1366x768 customer/supplier visual workflows could not be executed. No visual success is fabricated. Manual checks remain required for route clarity, source selection, available amount, both target flows, immediate refresh, one Annuler, button fit, and overflow.

## 30. AOP-05 STATUS

PARTIAL. The reachability regression is technically fixed and all automated functional/security/data-safety gates pass; native WPF visual acceptance remains outstanding.

## 31. FINAL RESULT

PARTIAL. Implementation and automated validation pass. Only the unavailable manual visual validation prevents a full PASS.
