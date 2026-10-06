# OBLIGATION-CENTRIC DEV2 — MODAL CONSISTENCY FIX
REMOVE DUPLICATE "ANNULER" ACTIONS

Repository:
C:\Users\INPT\Dev\logiciel-gestion-financiere

Branch:
feature/client-requested-improvements-v1

IMPORTANT:

The previous Lot 2 UI/UX polish task has already been launched separately.

Do NOT redo that task.

This is a NEW, NARROW UI CONSISTENCY FIX only.

Do NOT commit or push.

==================================================
1. PROBLEM OBSERVED BY LEAD
==================================================

Manual visual review found a repeated UI defect:

Several create/edit modals display TWO visible "Annuler" buttons:

- one in the upper-right area of the modal;
- one in the bottom action footer.

This is redundant and visually inconsistent.

Confirmed examples from manual screenshots:

- Nouvelle dépense
- Nouveau client
- Nouveau fournisseur
- Nouvel encaissement
- Nouveau paiement fournisseur

There may be other dialogs using the same duplicated pattern.

==================================================
2. FINAL UX RULE
==================================================

For CREATE / EDIT forms:

There must be exactly ONE visible "Annuler" action.

Keep the BOTTOM action beside the primary action.

Examples:

Annuler | Enregistrer

Annuler | Vérifier et continuer

Remove the duplicate upper-right "Annuler" text button.

==================================================
3. READ-ONLY DETAILS
==================================================

For read-only/detail dialogs:

Keep exactly ONE visible action:

Fermer

preferably in the bottom action area.

Do not add a second top-right text Close/Cancel action.

If Window chrome already provides a native close button, do not add another
redundant text close unless the current validated design explicitly requires it.

==================================================
4. AUDIT SCOPE
==================================================

Inspect the actual WPF views/templates used by at least:

Expenses
Clients
Suppliers
Customer Payments
Supplier Payments

Also search for other dialogs/modals using the same duplicated Cancel pattern.

Do NOT blindly remove buttons globally.

For each dialog:

- determine which Cancel command each button uses;
- confirm both buttons are behaviorally equivalent;
- keep the bottom footer action;
- remove only the redundant upper action.

If two buttons have different semantics:
STOP and report before changing that dialog.

==================================================
5. PRESERVE BEHAVIOR
==================================================

Do NOT change:

- Cancel command behavior;
- unsaved-data behavior;
- validation;
- authorization;
- financial logic;
- payment logic;
- client/supplier logic;
- expense logic;
- navigation;
- close behavior;
- Escape-key behavior if already implemented.

The only intended behavioral change is:

TWO visible Cancel actions
→ ONE visible Cancel action.

==================================================
6. SPECIFIC LEAD DECISIONS TO PRESERVE
==================================================

Do NOT modify:

- duplicated page titles in Encaissements/Paiements;
- DatePicker appearance;
- Statut ComboBox appearance.

Those three items were explicitly accepted by the lead.

Do not revisit them.

==================================================
7. VISUAL CONSISTENCY
==================================================

After removing upper Cancel:

ensure the modal header still looks balanced.

Do NOT replace the removed button with decorative filler.

Do NOT move the primary action to the header.

Footer remains the single action zone.

Examples:

Expense:
Annuler | Enregistrer

Client:
Annuler | Enregistrer

Supplier:
Annuler | Enregistrer

Customer payment:
Annuler | Vérifier et continuer

Supplier payment:
Annuler | Vérifier et continuer

Keep current spacing/theme unless a tiny alignment adjustment is necessary.

==================================================
8. REGRESSION TESTS
==================================================

Add or update WPF regression tests where practical.

At minimum prove for the touched dialogs:

- only one visible control with content "Annuler";
- expected primary action still exists;
- Cancel command still resolves correctly.

Also check detail dialogs touched by the shared template:

- only one visible "Fermer" where applicable.

Do not create brittle pixel/screenshot tests.

Prefer structural WPF tests consistent with the existing Desktop test suite.

==================================================
9. BUILD / TEST
==================================================

Run:

dotnet build FinancialManagement.sln

Run targeted Desktop/WPF tests for:

- expense dialog
- client dialog
- supplier dialog
- customer payment dialog
- supplier payment dialog
- any shared modal component touched

Then run full solution suite.

Use the latest test total produced by the previous Lot 2 polish task as the
new baseline; do not assume the older 1147 count if that task added tests.

Run:

scripts\verify-foundation.cmd

Run:

git diff --check

Verify:

- migrations remain 001–004 only;
- no migration 005;
- no schema drift;
- no vulnerable packages.

==================================================
10. MANUAL VISUAL CHECK
==================================================

At 1366x768 inspect:

Nouvelle dépense
Nouveau client
Nouveau fournisseur
Nouvel encaissement
Nouveau paiement fournisseur

Required result:

ONE visible Annuler button per create/edit modal.

The remaining button must be in the footer/action area.

No broken spacing.
No hidden primary action.
No modal overflow introduced.

==================================================
11. GIT
==================================================

DO NOT:

git add
git commit
git push
git merge
git reset
git rebase
git clean

Keep all current Lot 2 work uncommitted.

==================================================
12. REPORT
==================================================

Create/update untracked:

MODAL-CANCEL-CONSISTENCY-REPORT.md

Report:

1. dialogs audited
2. duplicate Cancel instances found
3. duplicate Cancel instances removed
4. dialogs where semantics differed, if any
5. Expenses result
6. Clients result
7. Suppliers result
8. Customer Payments result
9. Supplier Payments result
10. read-only Fermer audit
11. files changed
12. tests added/updated
13. targeted test results
14. full-suite result
15. build
16. Foundation
17. migrations
18. schema drift
19. vulnerabilities
20. git status
21. MANUAL VISUAL ACCEPTANCE REQUIRED: YES
22. FINAL RESULT:
    PASS / PARTIAL / BLOCKED

STOP.

NO COMMIT.
NO PUSH.PAYMENTS — TECHNICAL REPORT

## 1. Starting hash

`61180ffc6c85eed0e1bd8fc8a97ba7ca53f89ebe` on `feature/client-requested-improvements-v1`.

## 2. Files changed

Application:

- `src/FinancialManagement.Application/Payments/PaymentObligationStatus.cs`
- `src/FinancialManagement.Application/CustomerPayments/CustomerPaymentObligationRead.cs`
- `src/FinancialManagement.Application/SupplierPayments/SupplierPaymentObligationRead.cs`

Infrastructure:

- `src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentObligationReadService.cs`
- `src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentObligationReadService.cs`
- `src/FinancialManagement.Infrastructure/InfrastructureServiceCollectionExtensions.cs`

Desktop:

- `src/FinancialManagement.Desktop/ViewModels/CustomerPaymentsViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/PaymentManagementViewModelBase.cs`
- `src/FinancialManagement.Desktop/ViewModels/PaymentPresentationModels.cs`
- `src/FinancialManagement.Desktop/ViewModels/SupplierPaymentsViewModel.cs`
- `src/FinancialManagement.Desktop/Views/PaymentManagementView.xaml`

Permanent tests:

- `tests/FinancialManagement.Tests/PaymentObligationReadServiceTests.cs`
- `tests/FinancialManagement.Tests/PaymentReadServiceTests.cs`
- `tests/FinancialManagement.Desktop.Tests/PaymentObligationViewTests.cs`

## 3. Existing models/services reused

The implementation reuses the persisted Sales, Purchases, InitialCustomerReceivables, InitialSupplierPayables, CustomerPayments, SupplierPayments and their explicit allocation entities. It also reuses `AllocationIntegrity`, the existing payment creation services, permission services, pagination conventions, payment methods, allocation validation, advance semantics and treasury behavior. No mutable paid/remaining balance was introduced.

## 4. Customer obligation query design

The customer reader builds one database-side union of Sales and InitialCustomerReceivables. It projects one row per obligation, derives the explicitly allocated total with correlated aggregate SQL, applies search/date/status filtering in SQL, counts the filtered result and pages it in SQL. The returned page is revalidated with the trusted customer allocation-integrity reader before presentation.

## 5. Supplier obligation query design

The supplier reader mirrors the customer design with Purchases and InitialSupplierPayables. Aggregation, filters, count and pagination remain database-side, then the bounded page is revalidated through the trusted supplier allocation-integrity reader.

## 6. Initial receivable/payable decision

Included. Existing payment flows can allocate to initial receivables and initial payables, so excluding them would hide valid obligations and histories. They use a clear origin/type label and their persisted identifier; no fake sale or purchase reference is fabricated.

## 7. Paid calculation

Paid is the sum of valid explicit allocations targeting the specific obligation. The trusted allocation-integrity result is authoritative for values presented to the desktop layer.

## 8. Remaining calculation

Remaining is the original persisted obligation amount minus valid explicit allocations to that obligation, subject to the existing integrity rules. It is derived at read time and is never persisted redundantly.

## 9. Status calculation

- paid = 0 and remaining > 0: `Outstanding` / “À régler”
- paid > 0 and remaining > 0: `PartiallyPaid` / “Partiellement réglé”
- remaining = 0: `Settled` / “Réglé”

No overdue threshold or due-date meaning is mixed into payment status.

## 10. Advance handling

Unapplied advances are not subtracted from an obligation and do not appear in its history. Only an explicit allocation to that obligation contributes to paid, remaining and history. Existing advance creation and treasury semantics are unchanged.

## 11. Multi-obligation payment handling

Each obligation row and history uses only the allocation amount targeted to that obligation. The full payment amount is never substituted when a payment is split across multiple obligations.

## 12. N+1 avoided + evidence

The main list uses bounded aggregate SQL rather than one query per obligation/payment/allocation. A command-count regression test compares a one-obligation page with a 21-obligation page and proves a constant query count, capped at eight commands. Detail performs a separate bounded read for the selected obligation and only its contributing allocations/payments.

## 13. Pagination behavior

Filtered count, ordering, skip and take remain database-side. The existing page size convention is preserved. The touched shared payment component now renders an empty result as Page 1/1 instead of Page 0/0.

## 14. Search behavior

Search remains database-side and covers party name, persisted obligation reference and the existing identifier representation. Status and date filters compose before count and pagination. Tests prove correct totals and no duplicate obligation rows after filtering.

## 15. Customer main-list redesign

The screen now presents one row per customer obligation with Date, Client, Reference/origin, Montant, Payé, Reste à payer, Statut and Actions. Numeric columns are right-aligned. Settled obligations remain visible and offer Details only; obligations with a positive remaining amount also offer Encaisser when authorized.

## 16. Customer detail/history

The obligation detail shows a compact summary (client, origin/reference, date, initial amount, total paid, current remaining and status) followed by a six-column collection history. Each row includes payment date/time, payment reference, method, “Montant affecté”, cumulative paid and remaining after that allocation.

## 17. Supplier main-list redesign

The supplier screen symmetrically presents one row per purchase/initial payable with Date, Fournisseur, Reference/origin, Montant, Payé, Reste à payer, Statut and Actions. Settled rows retain Details without an active Payer action.

## 18. Supplier detail/history

The supplier detail mirrors the customer summary and exposes only allocations contributing to the selected purchase/initial payable, with the same deterministic six-column historical timeline.

## 19. Historical remaining algorithm

Allocations are ordered by persisted payment date, then creation timestamp, payment ID and allocation ID. Starting from the original obligation amount, the reader accumulates only allocations to this obligation and calculates remaining after each row. Permanent coverage proves `500 → 2500`, `1000 → 1500`, `1200 → 300`, `300 → 0`, including deterministic same-date ordering.

## 20. Duplicate payment-detail defects fixed, if applicable

The new obligation detail replaces the touched main-screen payment-detail presentation. It no longer shows “Aucune affectation.” when contributing allocations exist and its footer contains exactly one “Fermer” action. No unrelated global modal refactor was performed.

## 21. Authorization

Existing Customer Payments and Supplier Payments permissions remain authoritative. Responsible access is preserved. View-only employees can read the list/history, including B01 cross-user reads, without creator filtering. Create remains required to execute Encaisser/Payer and is enforced by the backend in addition to the UI command state. Users without View fail closed. No configurable permission was added.

## 22. Refresh after payment

After successful existing single-obligation payment creation, the current obligation page reloads immediately and the selected obligation detail/history is reloaded. No restart or polling is required. The selected party and obligation are preselected and locked in the quick workflow; backend validation remains authoritative.

## 23. Targeted tests

- New obligation reader tests: 8/8 PASS.
- New WPF obligation view tests: 2/2 PASS.
- Broader targeted core set: 309/309 PASS.
- Broader targeted desktop set: 5/5 PASS.

Coverage includes zero/partial/full installments, one row per obligation, multiple obligations, multi-allocation payments, unapplied advances, initial obligations, exact historical remaining, stable ordering, search/status/pagination, no duplicates, constant query count, B01 cross-user reads and fail-closed authorization.

## 24. Full suite

PASS — 1147/1147 total:

- Core: 1138/1138 PASS.
- Desktop: 9/9 PASS.

Previous baseline was 1137/1137.

## 25. Build

PASS — `dotnet build FinancialManagement.sln`: 0 warnings, 0 errors.

## 26. Foundation

PASS — `scripts\verify-foundation.cmd`, including build, full tests, migration/model checks and package vulnerability audit.

## 27. Migrations

Unchanged. Exactly 001, 002, 003 and 004 remain. No migration 005 was created.

## 28. Schema drift

None. The Foundation pending-model check passed.

## 29. Vulnerabilities

None reported by the Foundation vulnerable-package audit.

## 30. VISUAL-UAT read-only customer proof

NOT COMPLETED. The application was launched against the existing VISUAL-UAT/ProgramData data and the user authenticated manually. Before customer navigation could be inspected, the native Windows control surface reset and thereafter exposed only the in-app browser, while the WPF process remained running. No customer payment action was invoked. Automated WPF structure tests prove the eight-column obligation list, right-aligned financial columns, six-column history and one “Fermer”, but these are not represented as a substitute for real-data visual acceptance.

## 31. VISUAL-UAT read-only supplier proof

NOT COMPLETED for the same tooling reason. No supplier payment action was invoked. Supplier reader semantics and the symmetric WPF presentation are covered by permanent automated tests, but real-data visual inspection remains outstanding.

## 32. ProgramData financial rows changed

NO. A read-only SQLite snapshot before the visual session and a second read-only snapshot after stopping it matched exactly:

- Sales: `36:17950000:36`
- Purchases: `27:9900000:27`
- CustomerPayments: `39:13688000:39`
- CustomerPaymentAllocations: `39:13688000:39`
- SupplierPayments: `28:5726000:28`
- SupplierPaymentAllocations: `28:5726000:28`
- InitialCustomerReceivables: `0:0:0`
- InitialSupplierPayables: `0:0:0`
- Expenses: `18:1567000:18`
- TreasuryTransfers: `0:0:0`

Both `PRAGMA quick_check` and `PRAGMA integrity_check` returned `ok`.

## 33. Git status

All Lot 2 source and permanent-test changes remain unstaged and uncommitted on `feature/client-requested-improvements-v1`. This report is untracked. The five pre-existing untracked reports remain preserved. `git diff --check` passes. No add, commit, push, merge, reset, rebase or clean was performed.

## 34. MANUAL VISUAL ACCEPTANCE REQUIRED

YES. Lead acceptance is explicitly required, and the read-only customer/supplier visual pass must be repeated because the native UI control channel became unavailable.

## 35. FINAL TECHNICAL RESULT

PARTIAL.

Implementation, permanent automated coverage, full suite, build, Foundation, migration/model/vulnerability checks and ProgramData non-mutation all pass. The only incomplete requested validation is the real-data, read-only WPF visual inspection for customer and supplier obligation screens/details.

---

# FINAL UI/UX POLISH — LEAD-ADJUSTED SCOPE

This section supersedes the earlier validation totals and ProgramData snapshot where they differ.

## 1. Title presentation unchanged

YES. The shell and page-content titles remain exactly “Encaissements clients” and “Paiements fournisseurs”. No title binding, wording or layout was changed.

## 2. DatePicker presentation unchanged

YES. The Date début and Date fin controls retain their existing width, style, empty-date presentation, calendar button and bindings. A presentation test now pins both controls to the existing `InputDatePickerStyle`.

## 3. Statut ComboBox unchanged

YES. Its existing `InputComboBoxStyle`, items, selection binding and behavior are unchanged. The Reports style was not applied.

## 4. Money-formatting fix

All Lot 2 monetary presentation properties already route through the accepted `MoneyFormatter.FormatMinor`; no competing formatter was added. Permanent presentation coverage now proves grouped French output for `4 300,00` and `24 580,00` using the culture's non-breaking group separator. Main-list, detail-summary and history bindings continue to use the preformatted properties; internal minor-unit values and currency presentation are unchanged.

## 5. Contextual Client/Fournisseur label

The shared detail view now binds its party label to `DetailPartyLabel`. The existing customer ViewModel context exposes “Client” and the supplier context exposes “Fournisseur”; the shared view was not duplicated.

## 6. Detail modal adaptive sizing

The detail dialog is vertically centered and content-sized. Its history row is `Auto`, so one/few payments no longer reserve the former star-sized empty area. The history grid grows with its rows, caps at 266 px and then uses its single internal vertical scrollbar. The dialog retains its 570 px cap for the 1366×768 viewport.

## 7. History grid styling

A local-only history DataGrid style adds a soft surface, restrained background header, semibold header typography, 38 px compact rows, 9 px cell padding, subtle horizontal separators, no vertical grid lines and right-aligned monetary columns/headers. It remains read-only and the six accepted columns are unchanged. No global DataGrid redesign was performed.

## 8. Reference ellipsis/tooltips

Main obligation references and history payment references remain single-line, retain `CharacterEllipsis`, and expose their complete bound text through a ToolTip. Persisted reference values are untouched and no wrapping or wide forced column was introduced.

## 9. Main-grid alignment

The eight accepted columns remain unchanged. Date, party/reference proportions, fixed monetary widths, status width and action width were balanced for the target viewport. Montant, Payé and Reste à payer now have coherently right-aligned headers as well as right-aligned values. “Partiellement réglé” retains sufficient width.

## 10. Action-button alignment

Détails and Encaisser/Payer keep their commands and visibility rules. They now use fixed consistent widths, 28 px height, balanced spacing and vertical centering. A settled row still shows only a centered Détails action.

## 11. Sidebar-focus decision

The blue outline was a custom `IsKeyboardFocused` template trigger, so it appeared after mouse clicks as well as keyboard navigation. That trigger was replaced with WPF's dedicated `FocusVisualStyle`: mouse selection keeps the established selected-navigation surface/indicator without the extra outline, while keyboard focus retains a visible two-pixel primary-color outline. No sidebar layout or selected-state redesign was made.

## 12. Exact multi-installment customer fixture found

YES, by strict read-only SQLite inspection:

- Client: Nour Emballage
- Obligation: `VTE-20260906-A07E8F4409AB`
- Original amount: 3 000,00
- Four explicit allocations totaling 3 000,00

## 13. Customer multi-installment visual result

Database-side proof is exact and ordered:

1. `ENC-20260907-3670064876C8` — Cash — affected 500,00 — cumulative 500,00 — remaining 2 500,00
2. `ENC-20260908-61EA90C96D6B` — BankTransfer — affected 1 000,00 — cumulative 1 500,00 — remaining 1 500,00
3. `ENC-20260909-B3515E94AA38` — Cash — affected 1 200,00 — cumulative 2 700,00 — remaining 300,00
4. `ENC-20260910-84B9401C4C79` — BankTransfer — affected 300,00 — cumulative 3 000,00 — remaining 0,00

REAL WPF VISUAL RESULT: NOT COMPLETED. The native Windows Computer Use surface remains unavailable after its earlier reset, so the real dialog could not be navigated and observed honestly. Automated presentation tests are green but are not claimed as a substitute.

## 14. Supplier multi-payment fixture found

YES, by strict read-only SQLite inspection:

- Supplier: Maghreb Packaging
- Obligation: `ACH-20260908-25CBA7ED4D94`
- Original amount: 4 500,00
- Two explicit allocations totaling 2 200,00

## 15. Supplier multi-payment visual result

Database-side proof is exact and ordered:

1. `DEC-20260909-950A2F08FDA1` — Cash — affected 1 000,00 — cumulative 1 000,00 — remaining 3 500,00
2. `DEC-20260910-753AD36AFB56` — BankTransfer — affected 1 200,00 — cumulative 2 200,00 — remaining 2 300,00

The final remainder matches the obligation summary mathematically. REAL WPF VISUAL RESULT: NOT COMPLETED for the same native-control limitation; no supplier payment was created by this task.

## 16. Files changed by this polish

- `src/FinancialManagement.Desktop/Resources/Styles.xaml`
- `src/FinancialManagement.Desktop/ViewModels/PaymentManagementViewModelBase.cs`
- `src/FinancialManagement.Desktop/Views/PaymentManagementView.xaml`
- `tests/FinancialManagement.Desktop.Tests/PaymentObligationViewTests.cs`
- this untracked report

No financial/query/service/schema file was modified by the polish.

## 17. Targeted tests

- UI/UX polish presentation tests: 4/4 PASS.
- Lot 2 obligation read tests: 8/8 PASS.
- Full Desktop WPF project: 11/11 PASS.

Coverage pins grouped money, frozen DatePicker/ComboBox styles, contextual-label binding, adaptive modal cap, history styling/columns, ellipsis/ToolTip behavior, numeric cell/header alignment, one Fermer and keyboard-only focus visual structure.

## 18. Full-suite total

PASS — 1149/1149 total:

- Core: 1138/1138.
- Desktop: 11/11.

## 19. Build

PASS — 0 warnings, 0 errors. An initial attempt encountered only the already-running visual-UAT executable lock; after verifying and closing that exact repository executable, the clean build passed.

## 20. Foundation

PASS on the final sources/tests — build 0/0, core 1138/1138, Desktop 11/11, migrations/model and vulnerable-package checks all passed.

## 21. Migrations

Exactly 001, 002, 003 and 004. No migration 005.

## 22. Schema drift

None. `dotnet ef migrations has-pending-model-changes` reports no model changes.

## 23. Vulnerabilities

None reported, including transitive packages for Infrastructure.

## 24. ProgramData financial rows changed

YES relative to the prior recorded Lot 2 snapshot; therefore the requested `NO` assertion cannot be made. The final read-only audit found two additional records that were not created by any command or UI action in this polish task:

- Customer payment ID 40, `ENC-20260911-EA716C75CC01`, created `2026-09-10 23:28:57 UTC` by user `Mohamed`: 1 000,00 Cash, with 700,00 allocated to `VTE-20260910-FC9DAEEA95C7` and 300,00 unapplied.
- Supplier payment ID 29, `DEC-20260911-82495D2A67A5`, created `2026-09-10 23:32:05 UTC` by user `Mohamed`: 725,00 Cash, fully allocated to `ACH-20260224-1654E16A7DFE`.

Snapshot differences versus the earlier report:

- CustomerPayments: `39:13688000:39` → `40:13788000:40`
- CustomerPaymentAllocations: `39:13688000:39` → `40:13758000:40`
- SupplierPayments: `28:5726000:28` → `29:5798500:29`
- SupplierPaymentAllocations: `28:5726000:28` → `29:5798500:29`

Sales, Purchases, initial obligations, Expenses and TreasuryTransfers remain unchanged. Final `quick_check` and `integrity_check` both return `ok`. No rollback, delete or correction was attempted.

## 25. Git status

Branch and HEAD remain `feature/client-requested-improvements-v1` at `61180ffc6c85eed0e1bd8fc8a97ba7ca53f89ebe`. All Lot 2/polish changes remain unstaged and uncommitted; preserved reports remain untracked. `git diff --check` passes. No add, commit, push, merge, reset, rebase or clean was performed.

## 26. MANUAL VISUAL ACCEPTANCE REQUIRED

YES. Real WPF customer/supplier main pages and one-/multi-payment detail dialogs still require lead visual acceptance at 1366×768. The three frozen title/DatePicker/Statut areas were not altered in source.

## 27. FINAL TECHNICAL RESULT

PARTIAL.

The scoped presentation implementation and every automated technical gate pass. The result cannot be marked fully PASS because real WPF visual proof was unavailable and the final ProgramData snapshot differs from the previously recorded snapshot due to two independently created payment records.
