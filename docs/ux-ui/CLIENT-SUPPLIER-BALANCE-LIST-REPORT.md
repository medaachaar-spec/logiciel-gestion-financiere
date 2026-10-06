# CLIENT / SUPPLIER BALANCE LIST REPORT

## 1. Baseline

- Starting hash: `7650a487b36389cd0afdfaeae952bd6c9da969aa`
- Branch: `feature/client-requested-improvements-v1`
- `origin/integration`: `7650a487b36389cd0afdfaeae952bd6c9da969aa`

## 2. Files changed

- `src/FinancialManagement.Application/Clients/ClientList.cs`
- `src/FinancialManagement.Application/Suppliers/SupplierList.cs`
- `src/FinancialManagement.Infrastructure/CurrentPartyBalanceReader.cs` (new)
- `src/FinancialManagement.Infrastructure/Clients/ClientService.cs`
- `src/FinancialManagement.Infrastructure/Suppliers/SupplierService.cs`
- `src/FinancialManagement.Desktop/ViewModels/ClientsViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/PartyListItemViewModel.cs`
- `src/FinancialManagement.Desktop/ViewModels/PartyManagementViewModelBase.cs`
- `src/FinancialManagement.Desktop/ViewModels/SuppliersViewModel.cs`
- `src/FinancialManagement.Desktop/Views/PartyManagementView.xaml`
- `tests/FinancialManagement.Tests/ClientServiceTests.cs`
- `tests/FinancialManagement.Tests/SupplierServiceTests.cs`
- `tests/FinancialManagement.Tests/PartyCrudTestDatabase.cs`
- `tests/FinancialManagement.Desktop.Tests/FinancialManagement.Desktop.Tests.csproj`
- `tests/FinancialManagement.Desktop.Tests/PartyBalanceListViewTests.cs` (new)
- `CLIENT-SUPPLIER-BALANCE-LIST-REPORT.md` (this untracked report)

## 3. Financial read path and aggregation

- Existing balance logic reused: **YES**. The new bounded reader reuses `AllocationIntegrity.ReadCustomerAsync` and `AllocationIntegrity.ReadSupplierAsync`, the same trusted allocation/integrity semantics used by the financial paths.
- Client aggregation: eligible sales plus initial receivables, less explicit customer-payment allocations, grouped for the client IDs on the current page.
- Supplier aggregation: eligible purchases plus initial payables, less explicit supplier-payment allocations, grouped for the supplier IDs on the current page.
- N+1 avoided: **YES**. List filtering and pagination remain database-side; the reader receives the bounded page-ID set (maximum 200) and executes a constant number of set queries, never one query per client or supplier.
- Initial receivables: included as obligations under the existing allocation rules.
- Initial payables: included symmetrically under the existing allocation rules.
- Unapplied advances: not subtracted. Only explicit allocations settle obligations; advances are not auto-applied.
- Invalid over-allocation/non-positive allocation/overflow: surfaced as a data-integrity list failure instead of silently displaying a wrong balance.

## 4. UI and list behavior

- Identifier removed from visible Client list: **YES**.
- `Créance actuelle` added: **YES**.
- Identifier removed from visible Supplier list: **YES**.
- `Dette actuelle` added: **YES**.
- Identifiers remain present in the read/view models for internal commands and search.
- Money is carried in minor units, formatted by the existing `MoneyFormatter`, displayed right-aligned, and uses neutral styling. Valid zero amounts display as `0,00`.
- Search is preserved database-side, including the previously supported identifier search.
- Pagination and existing authorization/B01 visibility semantics are preserved.
- Sorting: unchanged and disabled by the existing DataGrid architecture (`CanUserSortColumns=False`). No fragile formatted-string sorting was introduced.
- Refresh: an already-loaded party page reloads on normal page re-entry; manual refresh remains available. No polling was added.

## 5. Tests and automated validation

- Focused client/supplier list tests: **28/28 PASS**.
- WPF balance-list tests: **3/3 PASS**.
- Broader targeted client/supplier, allocations, advances, payments, and authorization regression set: **314/314 PASS**.
- Full permanent solution suite: **1137/1137 PASS** (`1130` core + `7` desktop).
- Build: **PASS — 0 warnings / 0 errors**.
- Foundation: **PASS**.
- `git diff --check`: **PASS**.
- Migrations: unchanged, exactly `001`, `002`, `003`, `004`; no `005`.
- Schema drift: **NONE** — `dotnet ef migrations has-pending-model-changes` reported no model changes since the last migration.
- Vulnerabilities: **NONE** reported, including transitive packages.

Permanent coverage proves zero, fully paid, partially paid, multiple obligations/allocations, initial obligations, unapplied advances, party separation, minor-unit transport, identifier retention/search, authorization/B01 preservation, constant query count, right alignment, column replacement, and re-entry refresh.

## 6. Read-only VISUAL-UAT at 1366x768

- Clients: **PASS**. Five rows displayed; no visible `Identifiant`; `Créance actuelle` is readable and right-aligned; actions remain usable; no clipping or unnecessary horizontal scrolling.
- Displayed client balances: Al Amal Market `6 940,00`; Atlas Distribution `0,00`; Maroc Commerce `24 580,00`; Nour Emballage `0,00`; Sahara Pack `11 100,00`.
- Client reconciliation: **PASS — 42 620,00**, exactly matching the trusted Dashboard current receivable position (`4,262,000` minor units).
- Suppliers: **PASS**. Six rows displayed: the five marked VISUAL-UAT suppliers plus the pre-existing zero-balance supplier `achaar`; no visible `Identifiant`; `Dette actuelle` is readable and right-aligned; actions remain usable; no clipping or unnecessary horizontal scrolling.
- Displayed supplier balances: Atlas Fournitures `19 830,00`; Carton Plus `6 160,00`; Maghreb Packaging `6 950,00`; PackPro Maroc `0,00`; Sahara Supply `8 800,00`; achaar `0,00`.
- Supplier reconciliation: **PASS — 41 740,00**, exactly matching the trusted Dashboard current payable position (`4,174,000` minor units).
- Navigation from Suppliers back to Clients remained stable and refreshed the list.
- Application closed normally with exit code `0`.
- Lead manual visual acceptance required: **YES**.

## 7. ProgramData and Git safety

- ProgramData financial rows changed: **NO**.
- The pre- and post-run read-only audits match: VISUAL-UAT marker counts remain 5 clients, 5 suppliers, 36 sales, 27 purchases, 18 expenses, 39 customer payments, and 28 supplier payments. SQLite `quick_check` and `integrity_check` both return `ok`; trusted current positions remain unchanged.
- No source or report is staged. The implementation and this report remain uncommitted.
- The four preserved Dashboard reports remain untracked and untouched.

## 8. Final result

**FINAL TECHNICAL RESULT: PASS**

Implementation and technical/visual checks pass. Final client-facing visual acceptance remains with the lead. No commit or push was performed.
