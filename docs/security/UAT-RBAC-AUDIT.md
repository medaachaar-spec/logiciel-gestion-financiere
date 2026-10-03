# RELEASE UAT — AUTOMATED RBAC PERMISSION MATRIX AUDIT

**RBAC AUTOMATED UAT RESULT: FAIL. No fixes applied.**

Date: 2026-09-09. Branch: release/v1.0-rc. HEAD and requested baseline: fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba. Repository: C:/Dev/logiciel-gestion-financiere. Release freeze respected.

## 1. Scope, method and exact counts

- Modules audited: Sales, Purchases, Expenses, Clients, Suppliers, CustomerPayments, SupplierPayments.
- **56 matrix scenarios** = 7 modules × 8 persisted V/C/M combinations: 000, 100, 110, 101, 111, 010, 001, 011.
- **2,451 external checks: 2,430 PASS, 21 FAIL.** These are named service outcomes and assertions, including UI properties and routes, not 2,451 independent matrix rows or logins.
- **21 unexpected successes against the requested acceptance rules:** 18 operations after employee deactivation (7 reads, 11 writes), plus 3 strict UI/backend dependency-parity successes. The latter have valid owning-module Create permission; they are distinct from module-flag bypasses.
- The 56 combinations match the implemented module truth table: **168 central-guard checks and 248 direct configurable-service checks all PASS**. No active-employee View/Create/Modify flag bypass or cross-module permission leakage was found.
- **Fresh permanent suite: 1,090 PASS / 0 FAIL / 0 SKIP.** Actual rerun, TRX start 2026-09-09 10:54:43 +01:00, finish 10:59:53 +01:00; runner duration 5m06s.

The external executable references unchanged Desktop/Infrastructure projects and resolves real production services through AddInfrastructure. It uses real authentication, persisted permissions and a unique SQLite file migrated with 001–004. Separate DI providers sharing that disposable database give employee and Responsable independent sessions. No fake authorization service, release connection string or WPF application startup is used.

WPF evidence is executed production ViewModels, ICommand.CanExecute and Shell navigation, plus static XAML binding inspection. No rendered-window or mouse-click automation is claimed. A denied backend call must return NotAuthenticated, Forbidden or NotAuthorized; a validation failure is not accepted as authorization proof. Denied operations compare all mapped application tables before/after. Positive writes use valid fixture IDs and actual persistence; payment creates include an allocation to an outstanding obligation. Preparatory compilation/invalid-payment-fixture trials are excluded; the final results.json is authoritative.

| External check group | Total | PASS | FAIL |
|---|---:|---:|---:|
| setup | 1 | 1 | 0 |
| matrix-guard | 168 | 168 | 0 |
| matrix-backend | 248 | 248 | 0 |
| WPF-viewmodel | 172 | 172 | 0 |
| WPF-command | 56 | 56 | 0 |
| WPF-navigation | 560 | 560 | 0 |
| WPF-direct-route | 532 | 532 | 0 |
| isolation-guard | 126 | 126 | 0 |
| isolation-backend | 186 | 186 | 0 |
| missing-row | 18 | 18 | 0 |
| unauthenticated | 18 | 18 | 0 |
| responsible-only | 24 | 24 | 0 |
| responsable | 85 | 85 | 0 |
| permission-input | 112 | 112 | 0 |
| session-refresh | 64 | 64 | 0 |
| session-ui | 14 | 14 | 0 |
| dependency | 26 | 26 | 0 |
| ui-backend-parity | 3 | 0 | 3 |
| ownership-characterization | 8 | 8 | 0 |
| deactivation | 29 | 11 | 18 |
| deactivation-observation | 1 | 1 | 0 |

## 2. Backend guards and implemented operation inventory

Central guard: [AuthorizationService.GetEffectivePermissionsAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Authorization/AuthorizationService.cs:36). Employee permissions are fetched AsNoTracking by (UserId, Module) on every check. Missing session/row and invalid module/action deny. View=false returns all false, including inconsistent persisted Create/Modify=true. Responsable receives all module permissions independently of employee permission rows.

[UserAdministrationService.SetPermissionAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Administration/UserAdministrationService.cs:91) normalizes Create=View && Create and Modify=View && Modify in storage. All 56 administrative-input combinations were exercised through an authenticated Responsable and verified in the database.

| Module / permission enum | Read: View guard | Create: Create guard | Modify: Modify guard |
|---|---|---|---|
| Sales | [GetPageAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Sales/SaleService.cs:17) | [CreateAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Sales/SaleService.cs:115) | No update endpoint in ISaleService |
| Purchases | [GetPageAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Purchases/PurchaseService.cs:18) | [CreateAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Purchases/PurchaseService.cs:115) | No update endpoint in IPurchaseService |
| Expenses | [GetPageAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Expenses/ExpenseService.cs:126) | [CreateAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Expenses/ExpenseService.cs:18) | No update endpoint in IExpenseService |
| Clients | [GetPageAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Clients/ClientService.cs:62) | [CreateAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Clients/ClientService.cs:17) | [UpdateAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Clients/ClientService.cs:168) |
| Suppliers | [GetPageAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Suppliers/SupplierService.cs:62) | [CreateAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Suppliers/SupplierService.cs:17) | [UpdateAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Suppliers/SupplierService.cs:168) |
| CustomerPayments | [GetPageAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentService.cs:18) | [CreateAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentService.cs:305) | [CustomerAdvanceService.ApplyAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/CustomerAdvances/CustomerAdvanceService.cs:16) |
| SupplierPayments | [GetPageAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentService.cs:18) | [CreateAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentService.cs:305) | [SupplierAdvanceService.ApplyAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/SupplierAdvances/SupplierAdvanceService.cs:16) |

Additional boundaries tested in every applicable combination: Clients/Suppliers GetByIdAsync and lookup SearchAsync (party View); Expenses GetByIdAsync (View), active categories and employee lookup (Expenses Create); both payment GetByIdAsync methods (payment View), and obligation lookup for CreatePayment/ApplyAdvance (payment Create/Modify). This gives **31 configurable service-operation/purpose boundaries**, each tested in eight flag combinations. Sales/Purchases have no GetById endpoint. Client/Supplier DeleteAsync is tested separately under PermanentDelete.

## 3. Complete persisted permission matrix

A = successful direct service operation; D = explicit authorization denial; N/A = no implemented modification endpoint. Expected is Read/Create/Modify after View neutralization. Modify means party UpdateAsync or payment ApplyAsync, not editing payment metadata. All other modules have no employee permission row. Positive creates use valid fixture IDs; this allows testing the module guard independently of party lookup availability.

| Module | View | Create | Modify | Read | Create op | Modify op | Expected R/C/M | Result |
|---|---:|---:|---:|---|---|---|---|---|
| Sales | 0 | 0 | 0 | D | D | N/A | D/D/N/A | PASS |
| Sales | 1 | 0 | 0 | A | D | N/A | A/D/N/A | PASS |
| Sales | 1 | 1 | 0 | A | A | N/A | A/A/N/A | PASS |
| Sales | 1 | 0 | 1 | A | D | N/A | A/D/N/A | PASS |
| Sales | 1 | 1 | 1 | A | A | N/A | A/A/N/A | PASS |
| Sales | 0 | 1 | 0 | D | D | N/A | D/D/N/A | PASS |
| Sales | 0 | 0 | 1 | D | D | N/A | D/D/N/A | PASS |
| Sales | 0 | 1 | 1 | D | D | N/A | D/D/N/A | PASS |
| Purchases | 0 | 0 | 0 | D | D | N/A | D/D/N/A | PASS |
| Purchases | 1 | 0 | 0 | A | D | N/A | A/D/N/A | PASS |
| Purchases | 1 | 1 | 0 | A | A | N/A | A/A/N/A | PASS* |
| Purchases | 1 | 0 | 1 | A | D | N/A | A/D/N/A | PASS |
| Purchases | 1 | 1 | 1 | A | A | N/A | A/A/N/A | PASS* |
| Purchases | 0 | 1 | 0 | D | D | N/A | D/D/N/A | PASS |
| Purchases | 0 | 0 | 1 | D | D | N/A | D/D/N/A | PASS |
| Purchases | 0 | 1 | 1 | D | D | N/A | D/D/N/A | PASS |
| Expenses | 0 | 0 | 0 | D | D | N/A | D/D/N/A | PASS |
| Expenses | 1 | 0 | 0 | A | D | N/A | A/D/N/A | PASS |
| Expenses | 1 | 1 | 0 | A | A | N/A | A/A/N/A | PASS |
| Expenses | 1 | 0 | 1 | A | D | N/A | A/D/N/A | PASS |
| Expenses | 1 | 1 | 1 | A | A | N/A | A/A/N/A | PASS |
| Expenses | 0 | 1 | 0 | D | D | N/A | D/D/N/A | PASS |
| Expenses | 0 | 0 | 1 | D | D | N/A | D/D/N/A | PASS |
| Expenses | 0 | 1 | 1 | D | D | N/A | D/D/N/A | PASS |
| Clients | 0 | 0 | 0 | D | D | D | D/D/D | PASS |
| Clients | 1 | 0 | 0 | A | D | D | A/D/D | PASS |
| Clients | 1 | 1 | 0 | A | A | D | A/A/D | PASS |
| Clients | 1 | 0 | 1 | A | D | A | A/D/A | PASS |
| Clients | 1 | 1 | 1 | A | A | A | A/A/A | PASS |
| Clients | 0 | 1 | 0 | D | D | D | D/D/D | PASS |
| Clients | 0 | 0 | 1 | D | D | D | D/D/D | PASS |
| Clients | 0 | 1 | 1 | D | D | D | D/D/D | PASS |
| Suppliers | 0 | 0 | 0 | D | D | D | D/D/D | PASS |
| Suppliers | 1 | 0 | 0 | A | D | D | A/D/D | PASS |
| Suppliers | 1 | 1 | 0 | A | A | D | A/A/D | PASS |
| Suppliers | 1 | 0 | 1 | A | D | A | A/D/A | PASS |
| Suppliers | 1 | 1 | 1 | A | A | A | A/A/A | PASS |
| Suppliers | 0 | 1 | 0 | D | D | D | D/D/D | PASS |
| Suppliers | 0 | 0 | 1 | D | D | D | D/D/D | PASS |
| Suppliers | 0 | 1 | 1 | D | D | D | D/D/D | PASS |
| CustomerPayments | 0 | 0 | 0 | D | D | D | D/D/D | PASS |
| CustomerPayments | 1 | 0 | 0 | A | D | D | A/D/D | PASS |
| CustomerPayments | 1 | 1 | 0 | A | A | D | A/A/D | PASS* |
| CustomerPayments | 1 | 0 | 1 | A | D | A | A/D/A | PASS |
| CustomerPayments | 1 | 1 | 1 | A | A | A | A/A/A | PASS* |
| CustomerPayments | 0 | 1 | 0 | D | D | D | D/D/D | PASS |
| CustomerPayments | 0 | 0 | 1 | D | D | D | D/D/D | PASS |
| CustomerPayments | 0 | 1 | 1 | D | D | D | D/D/D | PASS |
| SupplierPayments | 0 | 0 | 0 | D | D | D | D/D/D | PASS |
| SupplierPayments | 1 | 0 | 0 | A | D | D | A/D/D | PASS |
| SupplierPayments | 1 | 1 | 0 | A | A | D | A/A/D | PASS* |
| SupplierPayments | 1 | 0 | 1 | A | D | A | A/D/A | PASS |
| SupplierPayments | 1 | 1 | 1 | A | A | A | A/A/A | PASS* |
| SupplierPayments | 0 | 1 | 0 | D | D | D | D/D/D | PASS |
| SupplierPayments | 0 | 0 | 1 | D | D | D | D/D/D | PASS |
| SupplierPayments | 0 | 1 | 1 | D | D | D | D/D/D | PASS |

*PASS is scoped to configurable module guards, not the overall acceptance result: dependency-parity finding B2 still applies. There are **24 N/A modification cells** (3 modules × 8 combinations). Successful edits of Sales/Purchases/Expenses cannot be claimed; their permission bits, but no edit operation, are tested.

## 4. WPF visibility, enabling and direct routes

Executed fresh ViewModels and Shells for all 56 combinations: 560 navigation visibility checks, 532 denied direct-route checks, 56 create-command enabling checks. The 172 ViewModel property checks also include dependency cases.

| Surface | Current behavior / source |
|---|---|
| Navigation | [ShellViewModel.InitializeAsync / NavigateToAsync](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/ViewModels/ShellViewModel.cs:201) checks View or a Responsable capability. [NavigationButtonStyle](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/Resources/Styles.xaml:219) collapses IsVisible=false. Hidden-route attempts also recheck authorization and deny. |
| Sales | CanViewSales / CanCreateSales; [New Sale visibility](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/Views/SalesView.xaml:56) and command enforce effective Create. Client lookup separately requires Clients View. No edit action. |
| Purchases | CanViewPurchases / CanCreatePurchases control reading and create visibility; [CanStartPurchaseCreation](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/ViewModels/PurchasesViewModel.cs:338) and the command additionally require Suppliers View. No edit action. |
| Expenses | CanViewExpenses / CanCreateExpenses; [New Expense visibility](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/Views/ExpensesView.xaml:56). Category and employee lookup require Expenses Create at backend. No edit action. |
| Clients / Suppliers | [PartyManagementViewModelBase](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/ViewModels/PartyManagementViewModelBase.cs:1136) derives CanView/Create/Modify/Delete. [Edit/Delete visibility](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/Views/PartyManagementView.xaml:182) hides unauthorized actions; commands also check permission, busy and form state. |
| Both payment modules | [PaymentManagementViewModelBase](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/ViewModels/PaymentManagementViewModelBase.cs:907) derives View, Create = payment View+Create AND party View, Advance = payment View+Modify. [New Payment visibility](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Desktop/Views/PaymentManagementView.xaml:65) binds the former. Advance commands also require an eligible selected payment with available advance. |

No UI hiding is used as backend-security proof. All implemented configurable service-operation/purpose boundaries were directly invoked without WPF.

## 5. Cross-module isolation and Responsable-only capabilities

All **42 directed distinct-module pairs** were tested with source V=C=M=1 and no target permission row: 126 central checks and 186 target service calls denied. This covers Sales → Clients, SupplierPayments → Purchases and CustomerPayments → Sales. Clients View without Clients Create is covered by matrix 100. Missing-row and logged-out cases deny all 18 primary read/create/implemented-modify operations. Fresh permanent AuthorizationServiceTests also cover user-to-user isolation and invalid enums.

| Capability | Employee with all 21 configurable flags enabled | Active Responsable |
|---|---|---|
| GlobalDashboard | Capability, Dashboard.GetAsync and due alerts denied | Capability and dashboard successful |
| Treasury | Capability, summary, opening balance and transfer denied | Capability, summary, opening and transfer successful |
| Administration | Capability; list/create employee, activation, permission changes, password reset; initial receivable/payable list/create all denied | Capability/list, permission changes and initial-obligation creation successful |
| BackupRestore | Capability, local backup, external backup and restore denied before artifact access | Local backup successful in harness; fresh permanent suite verifies external backup and successful restore/session clearing |
| PermanentDelete | Capability and valid unused client/supplier deletions denied | Both unused parties deleted; no general financial-delete service endpoint exists |

Responsable coverage totals 85 external checks, including no permission rows, explicitly false Responsable permission rows and all ten navigation items. Existing business constraints remain unchanged. [Administration actor validation](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Administration/UserAdministrationService.cs:184) revalidates persisted IsActive and Responsable role. The central capability guard uses authenticated session role.

## 6. Dependencies and strict UI/backend parity

| Configuration | UI / lookup | Direct backend | Classification |
|---|---|---|---|
| Sales V+C, Clients View=false | Sales creation available; client lookup denied | Fully paid anonymous sale succeeds, including its initial payment without CustomerPayments permission | Intended behavior confirmed |
| Sales V+C, Clients View=true | Client lookup succeeds; ViewModel exposes it | Sale creation remains allowed | PASS |
| Purchases V+C, Suppliers View=false | Supplier lookup denied; creation command unavailable | CreateAsync with existing SupplierId succeeds | B2 parity gap |
| Purchases V+C, Suppliers View=true | Lookup and creation command available | CreateAsync succeeds | PASS |
| CustomerPayments V+C, Clients View=false | New Payment hidden; client lookup denied | CreateAsync with known ClientId and valid allocation succeeds | B2 parity gap |
| SupplierPayments V+C, Suppliers View=false | New Payment hidden; supplier lookup denied | CreateAsync with known SupplierId and valid allocation succeeds | B2 parity gap |
| Either payment V+C and party View=true | New Payment available | CreateAsync succeeds | PASS |
| Either payment V+M, party View=false | Advance permission available subject to selection | ApplyAsync and obligation lookup succeed for eligible known IDs | Current advance behavior characterized |

Party lookup services require their own party View. Payment obligation lookups instead require payment Create or Modify according to purpose; returning obligations does not grant the standalone Sales/Purchases/initial-obligation APIs. Lists/details may include related party names or allocation information under the owning module View. Linked initial payments belong to an authorized Sale/Purchase transaction and do not grant access to the standalone payment service.

**B2 — BLOCKER against the request's literal section 10 UI/backend parity criterion:** three UI-unavailable creation workflows succeed directly. Each caller has effective Create on the owning module, so this is not false-View bypass or cross-module permission leakage. Current code treats party View as a UI lookup dependency only. Because the request also asks to preserve intentional dependencies, release-owner adjudication of this policy distinction is needed. No extra party permission rule was invented or implemented. The report does not silently treat these three successes as equivalent to missing owning-module authorization.

## 7. Session permission refresh and deactivation

For every module: employee logs in with full permission; an independent Responsable provider revokes through SetPermissionAsync; existing employee read/create/implemented-modify calls immediately deny; new login also denies. Responsable grants again; the existing session immediately succeeds, and new login succeeds. **Backend module permissions take effect on the next check; relogin is not required.** All 64 refresh checks pass.

The existing Shell item remains visible after revocation in seven observed cases, but direct navigation rechecks and denies in all seven. Shell initialization is one-shot. UI permission properties are loaded during page initialization/refresh, not pushed when another session changes storage. Rebuilding the shell, normally via logout/login, updates the whole menu. No global live-refresh promise is inferred; already displayed data may remain until the page is cleared/refreshed. Cached menu state is NON-BLOCKER here because fresh service calls enforce revoked flags.

**B1 — BLOCKER: deactivation leaves an existing employee session usable.** With all module flags enabled, the independent Responsable sets IsActive=false; a no-tracking persisted re-read confirms it. The employee retains authenticated identity. All 18 primary operations succeed: seven lists, seven creates, four implemented modifications. Eleven calls demonstrably change the disposable database. A new login with the same correct password is denied and clears the session; subsequent reads deny.

Root cause: [AuthenticationService](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Authentication/AuthenticationService.cs:24) checks activity at login; [CurrentUserSession](C:/Dev/logiciel-gestion-financiere/src/FinancialManagement.Infrastructure/Authentication/CurrentUserSession.cs:8) defines authentication by stored UserId; AuthorizationService reads employee permission rows without revalidating user activity. No explicit documented V1 exception permitting deactivated employees to continue was found. This is retained-session access, not an inactive-user login bypass. Two providers/processes sharing SQLite reproduce it; a single process that logs out before switching users naturally clears its singleton session. Classification follows the supplied deactivated-user acceptance rule; no session policy is redesigned.

## 8. Modify ownership and same-day rules

| Operation | Current implementation and evidence |
|---|---|
| Sales / Purchases / Expenses editing | No service/application edit endpoint or WPF edit action. Own+today+Modify, other-owner and outside-window edits are N/A; the bit alone cannot prove a successful edit. |
| Clients / Suppliers UpdateAsync | Effective Modify guard. Party entities have no CreatedByUserId; services impose no ownership or same-day restriction. All eight permission combinations target valid existing parties. |
| Both advance applications | Effective payment Modify guard; no creator/day restriction in ApplyAsync. Six actual writes characterize own/today, another creator/today and own/yesterday for both types, all successful. Two Modify=false cases deny. |

The eight ownership cases characterize current V1; they do not prove a global same-day enforcement rule. Advance application adds allocations and does not edit original payment amount/date/creator. Architecture mentions general employee ownership/day constraints, while the UX matrix specifically attaches them to Sales/Purchases/Expenses editing. The broader wording versus advance behavior is an OBSERVATION requiring policy clarification if allocations were intended to be included. Unsupported editing is a coverage limitation, not a proven edit bypass. No ownership/day rule was changed.

Policy sources inspected: [Architecture technique V1.0, sections 6.2 and 7](C:/Dev/logiciel-gestion-financiere/doc/Architecture_Technique_V1.0_VALIDE_Logiciel_Gestion_Financiere.pdf) and [UX/UI V1.0, sections 11.2 and 12](C:/Dev/logiciel-gestion-financiere/doc/Conception_UX_UI_V1.0_VALIDE_Logiciel_Gestion_Financiere.pdf). Older functional/model documents were also inspected. No live-revocation/relogin exception was found. These are existing local/untracked inputs.

## 9. Classification and exact failed-case ledger

| ID | Severity | Finding | Failed checks |
|---|---|---|---:|
| B1 | BLOCKER | Retained employee read/write access after persisted deactivation | 18 |
| B2 | BLOCKER under strict section 10 criterion | Three UI-dependent creation restrictions absent at corresponding backend boundary | 3 |
| N1 | NON-BLOCKER | Cached menu visibility after module permission change; backend and direct-route rechecks enforce new flags | 0 |
| O1 | OBSERVATION | Three Modify flags have no executable edit feature; 24 matrix modification cells N/A | 0 |
| O2 | OBSERVATION | Advance allocation has no owner/day restriction; broader policy applicability not assumed | 0 |
| O3 | OBSERVATION | Lookups and linked initial payments use operation-specific guards; standalone isolation passes | 0 |

| Group | Case | Expected | Observed |
|---|---|---|---|
| ui-backend-parity | purchase-no-supplier-view | DENY under audit section 10; current module guard allows | ALLOW |
| ui-backend-parity | CustomerPayments/no-party-view | DENY under audit section 10; current module guard allows | ALLOW |
| ui-backend-parity | SupplierPayments/no-party-view | DENY under audit section 10; current module guard allows | ALLOW |
| deactivation | Sales/existing-read | DENY | ALLOW |
| deactivation | Sales/existing-create | DENY | ALLOW; DATABASE CHANGED |
| deactivation | Purchases/existing-read | DENY | ALLOW |
| deactivation | Purchases/existing-create | DENY | ALLOW; DATABASE CHANGED |
| deactivation | Expenses/existing-read | DENY | ALLOW |
| deactivation | Expenses/existing-create | DENY | ALLOW; DATABASE CHANGED |
| deactivation | Clients/existing-read | DENY | ALLOW |
| deactivation | Clients/existing-create | DENY | ALLOW; DATABASE CHANGED |
| deactivation | Clients/existing-modify | DENY | ALLOW; DATABASE CHANGED |
| deactivation | Suppliers/existing-read | DENY | ALLOW |
| deactivation | Suppliers/existing-create | DENY | ALLOW; DATABASE CHANGED |
| deactivation | Suppliers/existing-modify | DENY | ALLOW; DATABASE CHANGED |
| deactivation | CustomerPayments/existing-read | DENY | ALLOW |
| deactivation | CustomerPayments/existing-create | DENY | ALLOW; DATABASE CHANGED |
| deactivation | CustomerPayments/existing-modify | DENY | ALLOW; DATABASE CHANGED |
| deactivation | SupplierPayments/existing-read | DENY | ALLOW |
| deactivation | SupplierPayments/existing-create | DENY | ALLOW; DATABASE CHANGED |
| deactivation | SupplierPayments/existing-modify | DENY | ALLOW; DATABASE CHANGED |

Exactly 21 failed checks are listed. No exception or invalid-input failure remains in the final run. The 26 dependency-characterization checks describe existing code correctly; three extra strict-parity assertions fail for the corresponding observed creations. The two layers distinguish implementation from acceptance requirements. Each such success is counted once in the 21 unexpected-success total.

## 10. Evidence, safety, repeatability and Git

Authoritative artifacts outside the repository:

- [External harness source](C:/Users/HP/AppData/Local/Temp/FinancialManagement-RbacAudit-4c45e2f72c08452ea1f7b0dc4e314205/Program.cs) and [project](C:/Users/HP/AppData/Local/Temp/FinancialManagement-RbacAudit-4c45e2f72c08452ea1f7b0dc4e314205/Audit.csproj).
- [Final outcomes, counts and 56 matrix rows](C:/Users/HP/AppData/Local/Temp/FinancialManagement-RbacAudit-4c45e2f72c08452ea1f7b0dc4e314205/results.json).
- [Fresh permanent-test TRX](C:/Users/HP/AppData/Local/Temp/FinancialManagement-RbacAudit-4c45e2f72c08452ea1f7b0dc4e314205/baseline.trx).

Commands actually run:

```powershell
dotnet test tests/FinancialManagement.Tests/FinancialManagement.Tests.csproj --logger 'trx;LogFileName=baseline.trx' --results-directory 'C:/Users/HP/AppData/Local/Temp/FinancialManagement-RbacAudit-4c45e2f72c08452ea1f7b0dc4e314205' --nologo
dotnet run --project 'C:/Users/HP/AppData/Local/Temp/FinancialManagement-RbacAudit-4c45e2f72c08452ea1f7b0dc4e314205/Audit.csproj' -- 'C:/Users/HP/AppData/Local/Temp/FinancialManagement-RbacAudit-4c45e2f72c08452ea1f7b0dc4e314205'
git status --short
git diff --check
```

The diagnostic harness exits 0 after writing its ledger even when assertions fail; the acceptance result comes from results.json. The permanent runner exited 0 with 1,090 passed tests.

Permanent suites reused include authentication/authorization, all seven module services, sales/client and purchase/supplier lookups, payment reads/obligation lookups, advances, treasury/dashboard/alerts, administration and backup/restore. In particular DatabaseBackupServiceTests.Restore_ReplacesWholeDatabasePreservesSafetyBackupAndClearsSession freshly passed; a further restore of the harness fixture was unnecessary.

Final disposable database: C:\Users\HP\AppData\Local\Temp\FinancialManagement-RbacAudit-4c45e2f72c08452ea1f7b0dc4e314205\run-4e2cfb5c03334df996c55a307f332151\audit.db. Migrations applied: 20260903232954_001_InitialSchema; 20260904004853_002_AllowAnonymousSalePayments; 20260904111121_003_TreasuryOpeningBalanceSingleton; 20260908072131_004_EnforceFinancialIntegerStorageClass. No schema/migration changes.

Automatic approval review rejected the bounded temporary-run-directory cleanup with “blocked by policy.” No deletion was executed; disposable run databases/backups and evidence remain under C:/Users/HP/AppData/Local/Temp/FinancialManagement-RbacAudit-4c45e2f72c08452ea1f7b0dc4e314205. No alternate deletion workaround was attempted.

**Production source/configuration files modified: NONE. Permanent test/source files added: NONE. Release DB modified: NO.** C:/ProgramData/FinancialManagement/Data/FinancialManagement.db was not used, opened or migrated by this audit. All harness connection strings and backup destinations point inside its unique temporary root; permanent tests use their existing disposable/in-memory fixtures. Normal ignored dotnet build outputs were generated; the tracked tree remains clean.

Initial untracked ASTRA-003-REPORT.md, ASTRA-004-REPORT.md, ASTRA-005-REPORT.md, UAT-V1-REPORT.md and doc/ were preserved. The only new repository artifact is this requested local/untracked report. Branch/HEAD unchanged; no staged changes. No git add, commit, push, merge or clean.

Final Git status:

```text
?? ASTRA-003-REPORT.md
?? ASTRA-004-REPORT.md
?? ASTRA-005-REPORT.md
?? UAT-RBAC-AUDIT.md
?? UAT-V1-REPORT.md
?? doc/
```

## 11. Requested final checklist

| Item | Result |
|---|---|
| 1–3. Modules / combinations / total matrix scenarios | 7 / 8 / 56 |
| 4–6. Backend read/create/modify guards | 31 implemented configurable boundaries, eight combinations each; inventory above |
| 7. WPF visibility | ViewModels, creation commands and shell executed; XAML inspected |
| 8. View=false neutralization | PASS, invalid persisted combinations and administrative normalization included |
| 9. Cross-module isolation | PASS, all 42 directed distinct-module pairs |
| 10. Responsable-only capabilities | PASS, all five capabilities and exposed backend actions |
| 11–13. Anonymous sale / client / supplier dependencies | Verified; B2 separately records three strict UI/backend parity gaps |
| 14. Permission refresh | Backend on next check; no relogin for flags; menu can remain cached |
| 15. Ownership/date | No transaction edit endpoint; party updates have no owner/day rule; advances characterized |
| 16. Deactivated employee | New login denied; existing session remains usable: B1 |
| 17–18. Bypass attempts / unexpected successes | 21 acceptance-rule successes: 18 deactivation + 3 strict parity |
| 19. Blockers | B1 and B2; B2's strict section 10 interpretation is explicit |
| 20–21. Non-blockers / observations | N1; O1–O3 |
| 22. Production files modified | NONE (source/configuration/migrations) |
| 23. Release DB modified | NO |
| 24. Git | Tracked and staged trees clean; only requested report newly untracked |
| 25. RBAC AUTOMATED UAT RESULT | FAIL |

Audit stopped after this report. No real UAT permissions changed; no fixes applied.

