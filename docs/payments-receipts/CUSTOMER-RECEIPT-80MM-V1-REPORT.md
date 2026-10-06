# CUSTOMER PAYMENT RECEIPT V1 — 80 MM THERMAL TICKET

## Layout correction follow-up — 11 September 2026

### Confirmed root cause

The original receipt money rows used a WPF `FlowDocument.Table` with a first `TableColumn` declared as `1*` and a second column fixed at 92 DIP. Actual WPF formatting measurement reproduced the UAT defect: the amount cell started only about 2.72 DIP after the label cell (`X=15.12` versus `X=17.84`). The supposedly flexible label column had therefore collapsed almost to zero, causing character-by-character wrapping and detached amounts. The preview window width and its disabled horizontal scrollbar were not the cause.

### Layout fix

- Removed the receipt money `Table` entirely.
- Each financial row is now its own vertically stacked `BlockUIContainer` containing a receipt-local `Grid`.
- Every row is exactly 72 mm wide (`272.126 DIP`) inside the 80 mm page and 4 mm side margins.
- The label column is `*` with a measured minimum of 112 DIP.
- The amount column is `Auto`; amount text is `NoWrap`, visible, and right aligned.
- Typical rendered rows measure 18 DIP high, confirming that labels such as `Montant vente`, `Cet encaissement`, `Total payé`, `Reste à payer`, and `Avance non affectée` no longer wrap character by character.
- Payment and obligation references are placed on their own flowing lines under `N°`, `Vente`, or `Créance initiale`.
- Multi-obligation sections remain purely vertical; no wide table was introduced.

Correction files:

- `src/FinancialManagement.Desktop/Views/CustomerPaymentReceiptDocumentFactory.cs`
- `tests/FinancialManagement.Desktop.Tests/CustomerPaymentReceiptPresentationTests.cs`
- this report

No receipt service, financial calculation, allocation ordering, database, migration, or printer transaction behavior changed.

### Runtime preview validation

The actual WPF preview window was opened by the permanent STA test host with a safe in-memory receipt fixture. Post-layout measurement confirmed:

- all money rows: 272.126 DIP / 72 mm;
- label column: never below 112 DIP;
- amount column: measured and fully inside the row;
- typical labels: single-line rendered height under 30 DIP (observed 18 DIP);
- amount: non-wrapping and right aligned;
- long payment/sale/receivable references: separate flowing paragraphs;
- multi-obligation receipt: nine stacked money rows inside width;
- advance row: inside width;
- horizontal scrollbar: disabled;
- exactly one `Fermer` action.

This is a real WPF measure/arrange/render-path validation, not a brittle screenshot assertion. The existing user-launched Tresoria process was not stopped or modified; validation builds were isolated under ignored `obj` directories. No physical printer test was performed.

## 1. Starting SHA

`4baadfe178b332befd0be4c5a8976184f32dd901` (exact `origin/integration` baseline after fetch).

## 2. Working branch

`feature/customer-payment-receipt-v1`

The branch was created from the exact required baseline. No add, commit, push, merge, rebase, reset, or clean operation was performed. Existing untracked reports were preserved.

## 3. Receipt architecture

- `ICustomerPaymentReceiptService` exposes a customer-only, read-only receipt projection.
- `CustomerPaymentReceiptService` composes the authoritative customer payment detail with authoritative obligation history details inside one SQLite read snapshot.
- Internal allocation identity links each receipt section to its exact historical position; database IDs are not present in the returned receipt model or printed output.
- Desktop presentation is split into a document factory, preview window, dialog service, and print adapter.
- Supplier payment services and view models have no receipt implementation or enabled receipt entry point.

## 4. Print technology chosen

WPF `FlowDocument` plus the standard Windows `PrintDialog`. The user explicitly selects an installed printer. There is no silent printing and no assumed default printer. A small gateway isolates the Windows print dialog so cancellation and failures are permanently testable.

## 5. 80 mm sizing strategy

- Paper width: exactly 80 mm converted to WPF device-independent units (`80 × 96 / 25.4`).
- Intended content width: exactly 72 mm.
- Margins: 4 mm on each side.
- Single flowing column, vertically paginated by the selected printer's printable height.
- Preview uses a vertically scrolling `FlowDocumentScrollViewer` with no horizontal scrollbar.
- References and customer text remain wrapping flow content; no wide receipt table is used.

## 6. Receipt fields

- `EL BARKA EMBALLAGE`
- `REÇU D'ENCAISSEMENT`
- Existing `ENC-...` payment reference as receipt number
- Payment business date and creation time
- Customer name, or the existing product wording `Client anonyme` for a persisted anonymous scenario
- Payment method (`Espèces` / `Virement`)
- Actual total payment amount
- Per allocation: business obligation type, business reference, original amount, this allocation, cumulative paid after it, and remaining after it
- Unapplied advance when positive
- Restrained `Tresoria` footer

Money values use the existing `MoneyFormatter`; no currency symbol was introduced.

## 7. Historical cumulative/remaining source

The receipt reuses `ICustomerPaymentObligationReadService`, including its accepted deterministic order:

1. payment date
2. payment creation timestamp
3. payment ID
4. allocation ID

The history item now carries allocation ID internally so a receipt can select the exact authoritative history row. It does not recalculate cumulative balances from current UI state and does not use today's current balance for historical receipts.

## 8. Multi-obligation behavior

One payment header is followed by one compact, vertically flowing section per persisted allocation, ordered by allocation ID. Each section can represent a sale or an initial customer receivable. The receipt total remains the actual payment amount.

## 9. Unapplied advance behavior

When `UnallocatedAmountMinor` is positive, the receipt explicitly prints `Avance non affectée`. The allocated sections are not inflated to the payment total.

## 10. Reprint behavior

- Immediately after successful creation, `Voir le reçu` is available in the success state.
- In a single-obligation workflow, the newly created history row is preselected in the detail shown after creation.
- Existing payments can be reprinted by selecting their row in the obligation history and choosing `Voir le reçu sélectionné`.
- Receipt generation and reprint are read-only and do not create payments or allocations or update audit timestamps.

## 11. Print failure safety

Payment persistence completes before any receipt action is offered. Preview and printing are separate read-only actions. Print-dialog cancellation and print exceptions return controlled messages explicitly stating that the encaissement remains recorded. They never invoke payment creation, allocation, treasury, or rollback logic.

## 12. Files changed

Application:

- `src/FinancialManagement.Application/CustomerPayments/CustomerPaymentReceipt.cs` (new)
- `src/FinancialManagement.Application/CustomerPayments/CustomerPaymentObligationRead.cs`

Infrastructure:

- `src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentReceiptService.cs` (new)
- `src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentObligationReadService.cs`
- `src/FinancialManagement.Infrastructure/InfrastructureServiceCollectionExtensions.cs`

Desktop:

- `src/FinancialManagement.Desktop/Services/CustomerPaymentReceiptDialogService.cs` (new)
- `src/FinancialManagement.Desktop/Services/CustomerPaymentReceiptPrinter.cs` (new)
- `src/FinancialManagement.Desktop/Views/CustomerPaymentReceiptDocumentFactory.cs` (new)
- `src/FinancialManagement.Desktop/Views/CustomerPaymentReceiptWindow.xaml` (new)
- `src/FinancialManagement.Desktop/Views/CustomerPaymentReceiptWindow.xaml.cs` (new)
- `src/FinancialManagement.Desktop/Views/PaymentManagementView.xaml`
- `src/FinancialManagement.Desktop/ViewModels/CustomerPaymentsViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/PaymentManagementViewModelBase.cs`
- `src/FinancialManagement.Desktop/ViewModels/PaymentPresentationModels.cs`
- `src/FinancialManagement.Desktop/App.xaml.cs`
- `src/FinancialManagement.Desktop/AssemblyInfo.cs`

Tests:

- `tests/FinancialManagement.Tests/CustomerPaymentReceiptServiceTests.cs` (new)
- `tests/FinancialManagement.Desktop.Tests/CustomerPaymentReceiptPresentationTests.cs` (new)
- `tests/FinancialManagement.Tests/PaymentReadServiceTests.cs`
- `tests/FinancialManagement.Desktop.Tests/Aop05AdvanceWorkflowTests.cs`

No migration or schema file was changed.

## 13. Tests added

14 permanent tests:

- 7 service/integrity tests: partial, historical after later payments, full settlement, multi-obligation plus advance, repeated target allocation, read-only reprint, inconsistent data fail-closed.
- 7 Desktop tests: 80/72 mm sizing, long wrapping text, measured non-collapsing money rows, multi-obligation containment, preview scrolling and one `Fermer`, controlled cancellation/exception without spooling, supplier separation.

All database tests use freshly migrated SQLite files under the temporary directory.

## 14. Targeted results

- Receipt service tests: `7/7 PASS`
- Receipt Desktop presentation tests: `7/7 PASS`
- Additional affected payment/modal regression selection: `11/11 PASS`

## 15. Full-suite total

`1212/1212 PASS`

- `FinancialManagement.Tests`: 1179 passed
- `FinancialManagement.Desktop.Tests`: 33 passed

Original V1 baseline: 1198. Layout-fix baseline: 1210. New total: 1212 (+14 from the original baseline, +2 from the layout-fix baseline).

## 16. Build

`dotnet build FinancialManagement.sln`: source build `PASS`, 0 warnings, 0 errors, using an isolated `BaseOutputPath` under ignored `obj` because a user-launched Tresoria process locked the normal Debug EXE/DLL. The exact default-output invocation was attempted first and was blocked only by that file lock; the application process was intentionally not terminated.

## 17. Foundation

`scripts\verify-foundation.cmd`: `PASS` (`Verification succeeded.`), with `BaseOutputPath` redirected under ignored `obj` to avoid the active application's Debug file lock. The first vulnerability query encountered a transient NuGet timeout; the query then passed, and the complete script was rerun successfully end to end.

## 18. Migrations

Only the accepted migrations are present and listed:

- `001_InitialSchema`
- `002_AllowAnonymousSalePayments`
- `003_TreasuryOpeningBalanceSingleton`
- `004_EnforceFinancialIntegerStorageClass`

No migration 005 exists.

## 19. Schema drift

`dotnet ef migrations has-pending-model-changes`: `PASS` — no model changes since the last migration.

## 20. Vulnerabilities

`dotnet list ... package --vulnerable --include-transitive`: `PASS` — no vulnerable package reported.

## 21. ProgramData touched

**NO.** No manual application/UAT launch was performed. Automated receipt tests used temporary migrated SQLite databases only.

## 22. git diff --check

`PASS`

## 23. git status

Tracked implementation/test files are modified but unstaged. New implementation/test files and this report are untracked. Pre-existing untracked reports remain preserved. There are no staged changes and no commit.

## 24. Manual visual/print acceptance required

**YES.** Validate the preview and output with an actual configured 80 mm Windows thermal printer, including the five requested UAT scenarios and Windows print-dialog cancellation. This was intentionally not run because it would require a live printer and manual UAT against application data.

## 25. FINAL RESULT

**PASS**

Implementation and automated validation are complete. Manual visual/physical 80 mm print acceptance remains required as specified.
