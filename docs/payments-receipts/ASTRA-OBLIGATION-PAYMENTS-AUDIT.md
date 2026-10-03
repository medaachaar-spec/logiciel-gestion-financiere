# DEV1 — Adversarial audit: obligation-centric payments and cross-cutting UX

Audit date: 2026-09-11. Read-only implementation review; no fixes, permanent tests, commits, pushes or merges.

**FINAL RECOMMENDATION: C. FIX REQUIRED BEFORE INTEGRATION**

The normal allocation arithmetic and the quick-payment success path work. The general SQL/AllocationIntegrity equivalence claim does not hold: filtering can remove corrupt obligations before validation, SQL SUM can overflow before validation, and separate reads can combine incompatible states. Persisted payment eligibility is also incompletely validated. The old advance-allocation workflow has become unreachable. These conclusions come from source review and reproducible diagnostic executions against the exact commit, not from the commit message.

## 1. Exact commit audited

`7567a97c4a6c4e1f47b5007c775a9d236b0cf2e0` — `feat: add obligation-centric payment workflows`.

Preflight ran `git status --short`, `git branch --show-current`, and `git rev-parse HEAD` before fetch/worktree creation. DEV1 had no uncommitted tracked changes. Existing untracked reports and `doc/` were left untouched. `git fetch origin` succeeded; `git cat-file -t` returned `commit`; `origin/feature/client-requested-improvements-v1` resolved to the required exact SHA.

Review checkout: `C:\Dev\audit-obligation-7567a97`, detached at the audited SHA. DEV1 stayed on `integration` at `7650a487b36389cd0afdfaeae952bd6c9da969aa` throughout.

## 2. Parent and integration references

- Exact parent: `61180ffc6c85eed0e1bd8fc8a97ba7ca53f89ebe`, verified with `git rev-parse 7567a97^`.
- Exact audited diff: that parent through `7567a97c4a6c4e1f47b5007c775a9d236b0cf2e0`.
- Integration reference: `7650a487b36389cd0afdfaeae952bd6c9da969aa`; also the merge base of integration and the feature.
- Diff and commit stat both show **22 files, 2,550 insertions, 220 deletions**.

## 3. Files reviewed

All 22 committed files were reviewed. Links point to the detached checkout, so line numbers describe the audited commit rather than DEV1's integration checkout.

| # | File | Review focus |
|---|---|---|
| 1 | [CustomerPaymentObligationRead.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Application/CustomerPayments/CustomerPaymentObligationRead.cs) | Contracts, target identity, paging, failures |
| 2 | [PaymentObligationStatus.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Application/Payments/PaymentObligationStatus.cs) | Three status values |
| 3 | [SupplierPaymentObligationRead.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Application/SupplierPayments/SupplierPaymentObligationRead.cs) | Contract symmetry |
| 4 | [Styles.xaml](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/Resources/Styles.xaml) | Navigation keyboard focus change |
| 5 | [CustomerPaymentsViewModel.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/ViewModels/CustomerPaymentsViewModel.cs) | Reader routing, mapping, write service reuse |
| 6 | [PaymentManagementViewModelBase.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/ViewModels/PaymentManagementViewModelBase.cs) | Permissions, commands, lock, refresh, advances, error paths |
| 7 | [PaymentPresentationModels.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/ViewModels/PaymentPresentationModels.cs) | Per-obligation values, full references, formatting |
| 8 | [SupplierPaymentsViewModel.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/ViewModels/SupplierPaymentsViewModel.cs) | Supplier mappings and symmetry |
| 9 | [ExpensesView.xaml](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/Views/ExpensesView.xaml) | Cancel and scoped Enter bindings |
| 10 | [InitialObligationView.xaml](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/Views/InitialObligationView.xaml) | Cancel and lookup Enter bindings |
| 11 | [PartyManagementView.xaml](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/Views/PartyManagementView.xaml) | Cancel and search Enter |
| 12 | [PaymentManagementView.xaml](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/Views/PaymentManagementView.xaml) | Obligation list/history, actions, lost advance entry point |
| 13 | [PurchasesView.xaml](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/Views/PurchasesView.xaml) | Cancel and search Enter |
| 14 | [SalesView.xaml](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Desktop/Views/SalesView.xaml) | Cancel and search Enter |
| 15 | [CustomerPaymentObligationReadService.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Infrastructure/CustomerPayments/CustomerPaymentObligationReadService.cs) | Entire new reader; abbreviated **CRead** below |
| 16 | [InfrastructureServiceCollectionExtensions.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Infrastructure/InfrastructureServiceCollectionExtensions.cs) | Scoped registrations |
| 17 | [SupplierPaymentObligationReadService.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Infrastructure/SupplierPayments/SupplierPaymentObligationReadService.cs) | Entire new reader; abbreviated **SRead** below |
| 18 | [ModalCancelConsistencyTests.cs](C:/Dev/audit-obligation-7567a97/tests/FinancialManagement.Desktop.Tests/ModalCancelConsistencyTests.cs) | Assertions and reachability gaps |
| 19 | [PaymentObligationViewTests.cs](C:/Dev/audit-obligation-7567a97/tests/FinancialManagement.Desktop.Tests/PaymentObligationViewTests.cs) | Real WPF controls vs business behavior coverage |
| 20 | [SearchEnterUxTests.cs](C:/Dev/audit-obligation-7567a97/tests/FinancialManagement.Desktop.Tests/SearchEnterUxTests.cs) | Binding/source update and harness limitations |
| 21 | [PaymentObligationReadServiceTests.cs](C:/Dev/audit-obligation-7567a97/tests/FinancialManagement.Tests/PaymentObligationReadServiceTests.cs) | All eight new backend tests |
| 22 | [PaymentReadServiceTests.cs](C:/Dev/audit-obligation-7567a97/tests/FinancialManagement.Tests/PaymentReadServiceTests.cs) | Fixture changes and SQL counter |

Supporting inspection covered [AllocationIntegrity.cs](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Infrastructure/AllocationIntegrity.cs), both payment writers, both advance writers, authorization, allocation/payment/obligation EF configurations, migrations/snapshot, sale creation, party deletion, treasury, the accepted Dashboard snapshot implementation, command adapters, money parser/formatter, and existing integrity tests. These supporting files were not changed by this commit.

Diagnostic evidence is outside both worktrees at `C:\Dev\audit-obligation-evidence-7567a97`. Its standalone console program references the audited assemblies and uses the existing migrated temporary-database fixture via reflection. It invokes real read services, real authorization, real advance services and real payment view models. Interleaving probes use a second DbContext/connection and intercept exact SQL command boundaries; no timing sleeps are needed. Corruption probes modify only their synthetic databases. No permanent repository test was added. Numeric probe enums: failure 9 = DataIntegrityViolation, failure 8 = NotFound; statuses 0/1/2 = Outstanding/PartiallyPaid/Settled.

## 4. Architecture assessment and invariant ownership

The new services form read models, not a new financial ledger. Each unions document obligations with opening obligations, filters/counts/pages in SQL, then calls AllocationIntegrity for the selected target IDs. Detail additionally loads allocation history. Writers and treasury remain existing services. Scoped DI is correct.

The critical distinction is that **AllocationIntegrity rejects invalid rows; it does not ignore them or recompute a smaller balance from a valid subset**. On rejection it returns null, which the new readers map to DataIntegrityViolation.

| Invariant | DB constraints / EF model | Write services | AllocationIntegrity | New obligation reads |
|---|---|---|---|---|
| Money is positive integer minor units | Positive CHECK plus migration 004 `typeof(...) = 'integer'`; EF `long` properties. Application code does not get pre-save enforcement merely from EF configuration. | Positive inputs; bounded additions; no floating-point money | Positive allocation, payment and target amounts | Header original > 0; detail positive allocations. SQL SUM itself is unguarded |
| Allocation selects exactly one target | XOR CHECK on document/initial FKs | Rejects zero/two targets and invalid IDs | XOR validation | SQL aggregates do not validate XOR; detail relies on integrity |
| Targets/payments exist | FKs while foreign_keys is enforced; document target delete Restrict, payment delete Cascade | Existence queries | LEFT JOIN detects missing payment/target for rows in its scope | Target history uses payment INNER JOIN after integrity; required-party INNER JOIN can erase an obligation before integrity |
| Allocation <= target remaining | No aggregate CHECK; ordinary FK/CHECK-valid SQL can violate it | Current authoritative totals checked inside IMMEDIATE write transaction | Bounded per-requested-target accumulation | Checked after page selection; SQL filters use raw sums |
| Total allocated across a payment <= payment amount | No cross-row aggregate constraint | Create bounds new total; advance validates source and available amount | Reads every allocation of each contributing payment and bounds payment total | SQL status knows only target sum; page revalidation can reject contributing-payment corruption |
| Payment party matches target party | Separate FKs do not enforce equality | Both creation and advance services validate party equality | **Not checked or projected** | **Not checked**; header party name does not establish payment ownership |
| Payment method/reference eligible | Method stored as required string; reference required/unique, not non-whitespace; no allowed-method CHECK | Creation validates enum; generates reference | **Neither method nor reference checked** | Main page accepts them; detail checks enum/nonblank reference after another query |
| No duplicate target in one request | No unique payment/target index | HashSet rejects duplicate targets in one request | No deduplication; accumulates each row | History contains each allocation; later advance can legitimately append another row for the same payment/target |
| Distinct obligation identity | Separate table PKs, not globally unique IDs | Typed target fields | Separate document/initial dictionaries | `(TargetType, TargetId)` retained through union, view-model mapping, detail and quick creation |
| Authorized read/create | Not a database financial constraint | Read = View; creation = Create; advance = Modify | No independent authorization; internal helper | Readers call authorization; UI is not the security boundary |
| Common read snapshot | Not guaranteed by separate autocommit SELECTs | Writers use transactions | Helper's two SELECTs do not create a read transaction | No enclosing read transaction, unlike Dashboard |

AllocationIntegrity's scope is requested targets plus every row of contributing payments. It is not a recursive validation of every other target's complete allocation graph. It cannot distinguish an accidental duplicate imported row from a legitimate later allocation merely because the payment/target pair repeats.

## 5. SQL versus AllocationIntegrity equivalence

**Conditional arithmetic equivalence holds; unconditional semantic equivalence is disproved.**

For a fixed database state, with valid headers and a successful integrity result, SQL SUM and checked accumulation include the same allocation rows for each requested typed target. PK joins cannot multiply rows. Positive allocations bounded by a positive original <= long.MaxValue yield the same paid amount and status. On this domain there is no example of integrity silently ignoring an allocation that SQL counted.

Outside that domain:

1. **Overallocated target disappears before validation.** Original 1,000; explicit allocation 1,200; payment 1,200. Both readers return DataIntegrityViolation unfiltered and in detail. Each of Outstanding, PartiallyPaid and Settled instead returns Success=true, count=0, empty items. There is no authoritative alternate status: the authoritative result is failure. SQL has hidden it. These rows are legal under the current DB's row-level constraints.
2. **Contributing payment corruption is invisible to SQL status.** Payment 600 allocated 400 to A and 400 to B; A has original 1,000. SQL labels A partial, but revalidation rejects it. This does not produce a partial page: the service fails the entire returned page. Corruption outside the selected page is not discovered by that call.
3. **Raw SUM can throw before authority.** Original long.MaxValue, allocations long.MaxValue and 1 from separate valid positive payments: both new page readers throw `SQLite Error 1: 'integer overflow'`. Their detail header query also contains the same unguarded SUM by code inspection. The intended typed integrity failure is bypassed.
4. **Eligibility missing from both paths.** A payment of party B allocated to party A's obligation is accepted by both list and detail. An invalid numeric enum value stored as `'999'` is accepted by the page but rejected by detail.
5. **State changes between SELECTs invalidate the conditional proof.** See section 13.
6. **Required-party INNER JOINs remove orphaned obligations**, even from an unfiltered list; see sections 9 and 18.

Safest architecture correction: use a single, shared, overflow-safe validated obligation projection under one SQLite read snapshot, then derive status, count and page from that validated population. Validate headers and allocation/payment eligibility for the search/date candidate set before status filtering can erase an invalid target. A conservative implementation can scan/batch only the necessary candidate headers/allocation fields, validate with checked minor-unit arithmetic, then count/select the sorted page. This has an explicit query/work tradeoff and must be measured; it is safer than duplicating incomplete validation in SQL. An optimized SQL implementation is acceptable only after equivalence, including failure semantics and overflow behavior, is proven. Do not solve this by clipping balances, using floating-point SUM, silently dropping invalid rows, or merely filtering the already returned page again. No balance table or migration is necessary for the correction.

## 6. Paid / remaining / status boundaries

For valid data both readers use this obligation's explicit allocation amounts, not payment totals. `remaining = original - paid`; the original and trusted paid are bounded before list subtraction. Detail bounds each addition and compares its final cumulative total with the trusted result.

| Case | Assessment |
|---|---|
| No allocation | Paid 0; remaining original; Outstanding |
| One or many installments | Correct per-target cumulative sum; PartiallyPaid while remaining > 0 |
| Exact settlement / zero remaining | Settled, retained in unfiltered and settled results |
| Exact long.MaxValue original/payment/allocation | Both list and detail succeeded, paid 9223372036854775807, remaining 0, in both diagnostic sides |
| Over-allocation | Rejected if reached; status filters can hide it (AOP-01) |
| Aggregate beyond long.MaxValue | Unhandled SQLite exception before validation (AOP-03) |
| Zero/corrupt original | Unfiltered page/detail rejected in both diagnostic sides; never normalized into a valid zero-value settlement |
| Negative/corrupt allocation | Unfiltered page/detail rejected in both diagnostic sides; SQL filters can exclude it before validation |
| Orphan payment | LEFT JOIN integrity validation rejected in both diagnostic sides; no silent smaller paid total |
| Ineligible source payment | Party/method checks incomplete (AOP-02) |

Diagnostic zero/negative values required disabling CHECK constraints on the temporary connection. Orphan probes disabled FK enforcement. Over-allocation, duplicated target rows, mismatched existing parties, and a numeric invalid method did not require disabling those constraints.

## 7. Multi-obligation payment

The two history queries filter the allocation's selected target FK and join one payment by its PK. A contributes only allocation A; B contributes only allocation B. Neither query substitutes `payment.AmountMinor`. Cumulative paid/history remaining are constructed independently for each target. The SQL union uses UNION ALL on different target kinds; no document/initial join fan-out exists.

The new supplier test checks a payment split between a purchase and initial payable, including the initial's exact 50,000 allocation instead of the full 250,000 payment. The customer test checks a 50,000 history allocation instead of a full 150,000 payment. However, its shared payment belongs to Atlas while its second sale belongs to Second Client: the fixture itself contains an invalid cross-party allocation and asserts it as paid. That is evidence of AOP-02, not accepted B01 cross-employee access.

Two rows for the same payment/target are not automatically a defect: diagnostics confirmed both rows contribute once, and the existing advance service legitimately creates such a second row. A unique payment/target constraint would break supported later applications; do not introduce one as an audit shortcut.

## 8. Unapplied advances

Executed on both sides: payment 1,000, allocation 700, obligation original 1,000. Detail returned paid 700, remaining 300, exactly one history row of 700. The unapplied 300 did not appear as an allocation. Calling the real existing advance service to explicitly apply 300 then returned success; detail changed to paid 1,000, remaining 0, with allocation rows 700 and 300. The source payment total stayed 1,000. No auto-application was observed.

**UX regression:** the payment detail entry point, unapplied amount display and `ShowAdvanceCommand` button are removed by the new view. Opening an obligation sets `_selectedDetail = null`; the only remaining call to populate payment detail is after an advance has already been applied. Diagnostics with responsible permissions returned CanApplyAdvancePermission=true but CanApplySelectedAdvance=false and ShowAdvanceCommand.CanExecute=false. No view contains a ShowAdvanceCommand binding. The backend supports later application but the user can no longer reach it from this payment module (AOP-05).

History follows the specified payment-date order, including later allocations of an old advance. It is not an immutable audit timeline of when allocations were recorded: later application can insert an old-payment row before newer payments. This is consistent with the requested explicit sort contract, but that distinction matters if a future requirement means allocation-event time instead.

## 9. Initial receivables / payables

Both BuildQuery methods use their correct initial tables and native initial reference/date/amount fields. They do not fabricate sale or purchase references. Initial target kind and numeric ID are kept together everywhere; ID=1 in both tables is unambiguous. Separate integrity dictionaries and typed switches prevent collisions. Search matches initial reference, party name and party identifier. Inclusive FromDate/ToDate use ReceivableDate/PayableDate rather than payment date. Status calculation and history use the correct initial allocation FK.

With valid party FKs, each source row appears once; count and page operate on the same union. The permanent tests exercise partial/full initial history, including supplier/customer symmetry. They do not comprehensively exercise date boundaries or both target kinds across page transitions.

Required-party INNER JOINs are a corruption gap: customer initials and both supplier sources disappear if the required party is missing. A synthetic supplier purchase whose Supplier row was removed yielded Success=true/count=0 unfiltered and NotFound detail despite the purchase and allocation still existing. Initial rows use the same failing join architecture (code evidence; the executed orphan-party case was a purchase). See AOP-06.

## 10. Historical remaining

CRead lines 147–152 and SRead 145–150 sort in memory by PaymentDate, PaymentCreatedAtUtc, PaymentId, AllocationId, in exactly that order. SQL retrieval order is not trusted. This handles same-date/timestamp ties and multiple allocations from a payment without relying on insertion order. Imported positive IDs still produce deterministic tie ordering. AllocationId is used internally even though it is not exposed by the public history DTO.

The permanent 300,000-minor-unit scenario validates 50,000 → 250,000; 100,000 → 150,000; 120,000 → 30,000; 30,000 → 0, equivalent to the requested 3,000 currency-unit scenario. History stores each intermediate remaining, not current remaining copied into every row.

The named tie-breaking test uses increasing timestamps, so it does not actually exercise equal timestamp/payment-ID/allocation-ID tie levels. The supplementary repeated-allocation probe exercised two rows of the same payment. Missing deterministic tests for reversed insertion order and equal timestamps should be added when fixes are assigned. Concurrent history change can create a false integrity rejection (AOP-04).

## 11. Search / status / pagination

Search trims outer whitespace; blank search imposes no text predicate. References, party names and party identifiers use literal Contains, with parameterized EF queries. Numeric target IDs are not separately searchable unless present in the reference: the implemented identifier search is the party identifier. There is no documented numeric-ID search contract in the added query DTO. Search sensitivity follows SQLite/Contains behavior; no case/accent-insensitive promise is implemented.

Date filters are inclusive obligation dates, status is nullable/enum-validated, and combinations compose before count/page. PageNumber >= 1, PageSize 1–200, date order and skip <= int.MaxValue are validated. Page counts use widened arithmetic and ceiling division, so exact multiples do not create an extra page. Sorting is deterministic: business date descending, document before initial, target ID descending.

Empty results have API TotalPages=0; the UI displays Page 1 / 1 while navigation still uses numeric totals and cannot advance. On a normal first empty page this is harmless display behavior. A directly requested out-of-range page is not clamped; it can be empty with a nonzero total. UI navigation is bounded, but external writes can invalidate its page/count (AOP-04). No residual SQL status is reapplied to revalidated rows.

Settled obligations remain visible. No creator/deactivation filter has been introduced. Existing client/supplier deletion services reject InUse parties, reinforced by FK Restrict; normal deletion cannot create AOP-06. The new readers add no deletion behavior. With corrupted data, see AOP-01/AOP-06 instead of assuming constraints hold.

## 12. N+1 / query assessment

Successful ordinary reader calls have a constant number of EF SELECTs:

| Call | Responsible | Employee with View |
|---|---:|---:|
| Main page | 4: count, page, target allocations, contributing-payment allocations | 5, including permission lookup |
| Detail found | 4: header, target allocations, contributing-payment allocations, history | 5, including permission lookup |

Early authorization/validation/not-found/failure paths issue fewer commands. Empty collection translation may avoid work, so these are the normal successful-path counts, not an invariant that every request must issue four. SQL interception with real allocations measured **4 page and 4 detail commands on each side**. UI initialization separately reads module permissions; the coordinator serializes in-process DbContext use but is not a database transaction.

Captured EXPLAIN QUERY PLAN results use IX_*PaymentAllocations_{SaleId/PurchaseId/Initial...Id} for correlated sums and target reads, payment-ID allocation indexes for contributing-payment reads, and PK lookups for headers. Date indexes support document scanning/order; initial ordering uses a temporary B-tree. Partial-status filtering repeats correlated sum evaluation (the plan shows repeated scalar subqueries). Thus constant round trips do not mean constant CPU, rows read, or memory. Work scales with candidate obligations, their allocations and the allocations of contributing payments. Detail history is not paginated and is sorted in memory.

Contains search is not a prefix index seek; existing unique reference/name/identifier indexes do not make substring search cheap. No representative large-data latency defect was demonstrated. The 1-versus-21-row permanent counter test only uses unpaid customer sales, fixed page size, no initial rows and no supplier/detail cases. It is a valid narrow round-trip test, not a performance benchmark. **No schema/index migration is recommended.**

## 13. Concurrency / snapshot consistency

There is no encompassing read transaction in either new reader or AllocationIntegrity. Deterministic second-connection probes on both sides reproduced:

| Commit boundary | Returned result |
|---|---|
| After filtered count, before page: settle sole Outstanding target | Success=true, TotalCount=1, Items=[] |
| After page, before target revalidation: settle the page's Outstanding target | Success=true, count=1, one **Settled** row under requested Outstanding filter |
| After detail integrity, before history: add valid 500 allocation | DataIntegrityViolation on otherwise valid data because trustedPaid != history cumulative |
| Same detail boundary, unrelated target allocation | Detail remains successful |

The probe commits valid allocation rows on an independent connection, equivalent to the persisted state change produced by a concurrent explicit advance. No corrupt input is required. The second case is not merely a coherent older snapshot; the filter and displayed status disagree within one response. The third falsely reports financial corruption. This justifies a common read snapshot (MEDIUM, AOP-04), without requiring stronger isolation for all application operations.

The accepted [Dashboard implementation](C:/Dev/audit-obligation-7567a97/src/FinancialManagement.Infrastructure/Dashboard/FinancialDashboardService.cs:70) already opens the connection, begins a deferred SQLite transaction, attaches it to EF and reads all constituent values before ending that snapshot. Reuse that pattern for each obligation page/detail including integrity queries. It avoids reserving a writer merely for a read. A snapshot alone does not solve raw SQL filtering or missing eligibility checks.

## 14. Authorization assessment

No new authorization bypass was demonstrated. List/detail invoke ValidateReadAsync and require the relevant payment module's View permission. AuthorizationService returns no permissions if stored CanView=false, even if CanCreate=true. Responsible receives access. Creation services independently demand Create; advance services demand Modify. Both writer services validate request fields and target ownership after authorization.

The permanent View-only test calls real list/detail services for obligations created by another employee and then attempts both real creation services, asserting Forbidden. This implements accepted **B01**. It must not be replaced with creator-only filtering. Cross-party allocation corruption in AOP-02 is a separate financial relation, not B01.

UI CanCreatePayment also depends on party-module View, an existing dependency for party lookup. Row payment actions may remain visible but disabled with View=true/Create=false. Cached UI permission state cannot authorize backend creation after permission revocation: backend checks stored employee permissions again. AsyncRelayCommand checks CanExecute on direct ExecuteAsync; RelayCommand itself does not enforce its predicate on direct Execute, so opening/changing a form is not an authorization boundary. ConfirmCreate and the backend still enforce payment creation rights. Session/version guards suppress late results after navigation/session changes. In-flight read authorization is checked at entry, not continually during each SELECT; no changed behavior introducing cross-user access was found.

## 15. Preselect / lock assessment

ShowCreateForObligation checks positive party ID, positive remaining, CanCreatePayment and workflow/busy state. It transfers the typed target, party, full reference, original and remaining into a one-obligation form. Diagnostics on both sides observed IsTargetObligationLocked=true, CanChangeCreateTarget=false, exactly one obligation and disabled clear-party command.

The view disables party selection and obligation search in that flow. Direct property/RelayCommand manipulation can defeat UX-only restrictions, as expected; no financial guarantee depends on that lock. Create services independently validate party existence, target existence, target party equality, positive amounts, duplicate request targets, total allocation <= payment and allocation <= current trusted remaining. Writers reserve SQLite's writer transaction before reading availability. A stale quick-form target settled by another payment was rejected; diagnostic payment count remained one with no extra payment/history opened.

## 16. Refresh assessment

Normal quick-flow execution was verified with real view models and services on both sides: original 1,000 minor, payment/allocation 500; simultaneous invocations of the same confirmation command produced exactly one payment. The main row and opened detail both displayed paid 5,00, remaining 5,00, one history row, and a success message. No restart or polling was involved.

On creation failure the code exits before refresh and preserves the editable form. On success it closes/reset the form, refreshes page 1 with applied filters, then opens the obligation detail when exactly one target was allocated. Multi-obligation creation refreshes the list but does not arbitrarily choose a detail; opening either target retrieves its own current history.

Failure-path limitations from source review:

- A returned page-refresh failure after successful creation shows a specific 'payment recorded, list not refreshed' ListErrorMessage.
- A thrown refresh/detail exception shares the broad creation catch, which sets 'Impossible d'enregistrer le paiement' even after successful commit. The success message has already been set and the form has closed; WorkflowErrorMessage is displayed inside modal workflows rather than the main list. A failed detail Result after commit is silently ignored. This broad catch/error-routing limitation is inherited from the parent; it was not injected dynamically in this audit. Separate commit success from refresh failure when addressing the workflow, preserving a clear recorded-payment state.
- Initial load applies failed page results as empty success (AOP-07), reproduced with real view models. Manual refresh correctly shows the integrity error instead.

Command-level busy protection prevents double-click re-entry in the tested flow. Services do not provide a global idempotency key: two separate authorized calls with sufficient remaining are two payments. No new idempotency claim is made. Financial bounds still prevent settling an already settled stale target again.

## 17. Treasury / result invariants

The new readers and AllocationIntegrity contain only query operations and local projection calculations: no Add/Update/Remove, SaveChanges, ExecuteUpdate/Delete, treasury calls or persisted balances. Reading does not move treasury. New scoped registrations introduce no background write.

Payment creation still writes the payment and allocations atomically. Treasury derives customer inflows/supplier outflows from payment totals by payment method, independently of allocated amounts. An explicit advance application writes allocation rows only and must not create another cash movement. Sale/purchase creation may create an explicitly requested initial payment; the document itself is not a treasury flow. Dashboard/result services and their financial formulas are unchanged by this diff. No mutable Paid/Remaining column/table or new cached financial balance is introduced.

## 18. Customer / supplier symmetry

Target kinds, source/payment/allocation tables, date fields, search, status, arithmetic, ordering, authorization modules and history mappings are mirrored correctly. Normal supplier/client outcomes and the identified SQL/eligibility/advance regressions were reproduced symmetrically.

The justified business difference is anonymous sales: customer document headers LEFT JOIN clients and allow ClientId=null; normal anonymous sales are fully paid by sale creation, and quick payment requires a positive party. Supplier obligations and initials require a party. However, using INNER JOIN for those required relations turns corruption into missing obligations rather than an explicit integrity failure (AOP-06). Customer sale with a non-null orphan ClientId instead fails its header validation; supplier purchase disappears. This failure-mode asymmetry is not required by the business rule.

## 19. Cross-cutting UX regression assessment

Cancel cleanup removes top duplicates while retaining the identical footer commands and primary actions for expense, party, sales, purchases, initial obligation, payment creation and advance forms. Read-only obligation detail retains one Fermer. No cancellation capability was lost from the forms themselves; the lost *entry point* to advances is a separate regression.

Enter bindings are local to search TextBoxes. They invoke the same ICommand as their corresponding buttons, and their Text bindings use UpdateSourceTrigger=PropertyChanged. No root/global Enter handler or code-behind business logic was added. Amount, observation, party name/phone/password entry behavior is not changed by this diff. Filter pagination follows the existing command, not duplicated search logic.

Payment labels are contextual. Financial formatting uses unchanged MoneyFormatter: decimal minor units / 100 with French N2 formatting. It neither changes stored values nor rounds integer minor units beyond their two displayed decimals. MoneyParser performs bounded decimal parsing, preserving formatter round-trip even near long limits. Model references remain full; truncation/tooltip are presentation concerns. History timestamp combines payment business date with creation-time local hour, not allocation-created time. Navigation focus switched from an always-focused border trigger to WPF FocusVisualStyle; the permanent test checks the style structure. No additional financial effect was found in cosmetic changes.

## 20. Migrations / model safety

No migration, configuration, DbContext, domain entity or model-snapshot file changed in the audited diff. Comparison against integration also found no migration/configuration/domain changes. New DTOs are not entities and are not registered in DbContext. Only two scoped read services were added to DI. Migration 005 does not exist. No schema change is proposed as an audit fix.

## 21. Test quality and required additions

The eight new backend tests use migrated SQLite with real readers/authorization/writers rather than mocked balances. They meaningfully check original/paid/remaining, shared allocations, initial history, B01 and View-only creation denial. The SQL interceptor counts sync/async reader and scalar calls with thread-safe increments; reset separates seeding from measured calls. It does not count non-query writes, so it does not independently prove read-only behavior.

Key gaps:

- The customer multi-obligation fixture explicitly pays a second client's sale with the first client's payment (lines 16–37), then asserts that sale's paid amount. Fix the fixture to use one party and add a separate corruption rejection test; do not conflate it with B01.
- No new test exercises SQL-vs-integrity failure equivalence, over-allocation hidden by all status filters, SUM overflow, invalid payment eligibility, orphaned required parties, or inter-query commits.
- The named historical tie test uses increasing timestamps; it misses deeper ties and reverse insertion order.
- Filter coverage does not exercise real date bounds, combined statuses/dates, empty results, last page/exact multiples, supplier filters, or typed initial/document collisions across pages thoroughly.
- Query count grows from 1 to 21 unpaid customer rows only; no allocations, initial rows, changed page sizes, supplier/detail scenarios or execution-plan/large-data assertions.
- WPF tests instantiate real views and inspect bindings/styles/visual trees; they are not mere XAML string matching. They still assert structure rather than complete workflows. The Enter harness explicitly executes ICommand and sets its own page to 1, so it does not prove physical key routing or every actual view model's pagination.
- ModalCancelConsistencyTests proves the advance dialog contains an apply button but never proves the user can open that dialog. There are no permanent quick-flow view-model tests for preselection, permissions, duplicate execution or refresh failures.

Before integration, add permanent symmetric regressions for AOP-01 through AOP-06 and an initial-load error assertion for AOP-07; add quick-flow success/stale/double-click and accessible explicit advance application cases. Cover valid repeated payment/target rows so an attempted duplicate fix does not break advances. Extend date/page/ordering cases where the reader is revised. **None were added in this audit.** External diagnostic probes do not substitute for permanent regressions.

## 22. Findings table

Severity distinguishes corruption-dependent read failures from ordinary writer behavior. No new unauthorized write, actual treasury mutation on read, or schema corruption was demonstrated; no BLOCKER is asserted. CRead/SRead aliases refer to the absolute file links in section 3; VM is PaymentManagementViewModelBase from that same table.

| ID | Severity | File / location | Issue | Evidence | Impact | Recommended remediation |
|---|---|---|---|---|---|---|
| AOP-01 | HIGH | CRead 43–65, 272–281; SRead 42–63, 268–277 | Raw status filtering hides obligations before authoritative rejection | Original 1,000 / allocation 1,200: unfiltered/detail fail, all three status-filtered calls succeed empty, both sides | Missing obligations and inconsistent integrity/failure semantics | Validate the candidate population before status/count/page from one authoritative projection; fail visibly on invalid candidates; section 5 |
| AOP-02 | HIGH | CRead 63–85, 129–162; SRead 61–83, 127–160; AllocationIntegrity 14–31, 118–123 | Persisted source-payment eligibility is incomplete | Different existing party's 1,000 payment yields paid=1,000/remaining=0/Settled; `'999'` method accepted by list but rejected in detail; both sides | Plausible false settlement in imported/corrupt data; list/detail disagree | Include and validate payment/target party association, supported method and nonblank reference in the shared authority used by all read paths, respecting legitimate anonymous-sale cases. Writer guards already reject mismatched new requests |
| AOP-03 | MEDIUM | CRead 240–243, 264–267, 113; SRead 236–239, 260–263, 111 | SQL SUM overflows before typed integrity handling | long.MaxValue + 1 produces SQLite integer-overflow exception in both page readers | Reader availability/error-contract failure; UI retains stale data or generic error | Remove unsafe raw sums from authority path; compute bounded checked totals. Map malformed storage failures to a controlled read failure; do not cast to floating point |
| AOP-04 | MEDIUM | CRead 46–65, 123–180; SRead 44–63, 121–178 | Count/page/integrity/history do not share a snapshot | Count 1/empty page; Settled row under Outstanding filter; valid detail falsely rejected after concurrent allocation, both sides | Contradictory filters/counts and false corruption messages | Enclose each complete page/detail read in a common deferred SQLite snapshot, using accepted Dashboard pattern |
| AOP-05 | MEDIUM | PaymentManagementView.xaml 577–586; VM 797–798, 1495–1510, 1697–1700 | Existing explicit advance workflow is unreachable | ShowAdvanceCommand has no view binding; responsible can modify but CanApplySelectedAdvance=false after detail; backend application succeeds | Users cannot allocate existing unapplied money through this module; loss of supported operation | Restore an accessible source-payment/available-advance selection route alongside obligation UI, using existing ApplyAsync; do not create a replacement cash payment |
| AOP-06 | HIGH | CRead 247; SRead 219, 243 | INNER JOIN removes obligations with an orphan required party | Synthetic supplier purchase persists, but unfiltered count=0 and detail NotFound; identical join structure for initials | Missing financial obligations in corrupted/legacy data rather than a visible integrity failure | Preserve headers with LEFT JOIN and reject non-null missing/invalid required party before success; combine with pre-filter validation |
| AOP-07 | MEDIUM | VM 1004, 1659–1672 | Initial load treats failed read result as successful empty page | Real VM: ShowEmptyState=true, ListErrorMessage=null, TotalCount=0 on DataIntegrityViolation; manual refresh shows error | Masks integrity failure as absence of obligations | Check payload.Result.Success and route through HandleFailure before ApplyPage; preserve authorization fail-closed behavior. **Inherited parent defect, newly exercised with obligation reader** |

AOP-02's underlying helper omission predates this commit; these new readers consume it as their trust boundary and the new fixture normalizes invalid cross-party data. AOP-07's initialization bug also predates the commit. AOP-01/03/04/05/06 are present in the newly added reader or changed workflow. This attribution matters when assigning fixes; it does not make the demonstrated new obligation UI safe to integrate unchanged.

Smallest coherent correction set: fix the shared eligibility/read projection and snapshot once for both sides; restore the existing advance operation's reachable UI route; handle failed initial results. Do not alter treasury formulas or persist derived balances. Retain typed target keys and legitimate repeated advance allocation rows.

## 23. Build result

Environment: Windows, .NET SDK **10.0.302**. In detached exact-commit worktree:

`dotnet build FinancialManagement.sln`

**PASS — 0 warnings, 0 errors**, elapsed 37.29 seconds including first restore/build. Foundation's later build also passed with 0 warnings/errors. The standalone diagnostic program emitted its own EF1002 advisory about controlled synthetic SQL corruption; it is outside the solution and does not change the audited solution's warning totals.

## 24. Full test result

First full execution: `dotnet test FinancialManagement.sln --no-build --logger 'trx;LogFilePrefix=exact-feature'`.

| Assembly | Passed | Failed | Skipped | Total |
|---|---:|---:|---:|---:|
| FinancialManagement.Tests | 1,137 | 1 | 0 | 1,138 |
| FinancialManagement.Desktop.Tests | 16 | 0 | 0 | 16 |
| **Total** | **1,153** | **1** | **0** | **1,154** |

Failure: [UserAdministrationServiceTests.cs:201](C:/Dev/audit-obligation-7567a97/tests/FinancialManagement.Tests/UserAdministrationServiceTests.cs:201), `Concurrent_employee_creations_with_same_username_return_controlled_results`: `Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(15)))` failed. This test file/service is unchanged in the audited diff. Main assembly duration 3m41s; desktop 16s.

Foundation separately reran the full suite and again produced 1,153 passed / 1 failed / 0 skipped / 1,154 total, with a **different** existing timeout (section 25). Thus the expected test count is confirmed; the advertised fully green baseline was not reproduced by either ordinary full run.

Focused diagnostic rerun of the two concurrency test families, with xUnit.MaxParallelThreads=1 and xUnit.ParallelizeTestCollections=false, passed **41/41**, no skips, 22 seconds. No desktop tests matched that filter. This supports a timing/parallel-execution explanation but does not erase either full-run failure or prove the host-level cause. No test was changed or excluded from either full run.

Evidence: [first main TRX](C:/Dev/audit-obligation-7567a97/tests/FinancialManagement.Tests/TestResults/exact-feature_net10.0_20260911092202.trx), [first desktop TRX](C:/Dev/audit-obligation-7567a97/tests/FinancialManagement.Desktop.Tests/TestResults/exact-feature_net10.0_20260911091837.trx), [targeted rerun output](C:/Dev/audit-obligation-evidence-7567a97/timeout-recheck.txt).

## 25. Foundation result

Executed the repository's unchanged `scripts\verify-foundation.cmd` in the exact detached worktree. **FAIL, exit 1 at step 4/7 (full tests)**. Tool restore, restore and build passed. Main tests: 1,137 passed, 1 failed, 0 skipped, 1,138 total, duration 3m35s. Desktop: 16/16, 15 seconds.

Failure: `AllocationIntegrityTests.Concurrent_writers_serialize_validation_and_commit_valid_aggregates(customer: False, initial: True, mode: "mixed", compatible: False)`. Timeout in [AllocationIntegrityTests.TransactionGate at line 323](C:/Dev/audit-obligation-7567a97/tests/FinancialManagement.Tests/AllocationIntegrityTests.cs:323), from SupplierAdvanceService's transaction-start interception. The test and service are unchanged in this diff. The complete family subsequently passed in the focused rerun.

Because the script exits on test failure, steps 5–7 were run separately (and vulnerability scanning expanded to the solution). They must not be reported as an end-to-end Foundation PASS. [Full Foundation output](C:/Dev/audit-obligation-evidence-7567a97/foundation-output.txt).

## 26. Migrations

`dotnet ef migrations list --project src/FinancialManagement.Infrastructure --no-build` succeeded and listed exactly:

1. `20260903232954_001_InitialSchema`
2. `20260904004853_002_AllowAnonymousSalePayments`
3. `20260904111121_003_TreasuryOpeningBalanceSingleton`
4. `20260908072131_004_EnforceFinancialIntegerStorageClass`

No migration 005. The CLI marked these Pending against the isolated design-time database; this is not model drift or a claim about the user's live database. Diagnostic fixtures migrated their own temporary databases. No migration was applied to DEV1's application data.

## 27. Schema drift

`dotnet ef migrations has-pending-model-changes --project src/FinancialManagement.Infrastructure --no-build`: exit 0, **“No changes have been made to the model since the last migration.”** Source diff confirms unchanged schema/model files. No mutable obligation balance was added.

## 28. Vulnerabilities

`dotnet list FinancialManagement.sln package --vulnerable --include-transitive`: exit 0. NuGet source `https://api.nuget.org/v3/index.json`; **no known vulnerable packages reported for all six projects**, including Infrastructure and Desktop, using the source's current advisories on the audit date. This is dependency-advisory coverage, not a claim of exhaustive application security verification.

## 29. Git status / evidence preservation

`git diff --check 61180ffc6c85eed0e1bd8fc8a97ba7ca53f89ebe 7567a97c4a6c4e1f47b5007c775a9d236b0cf2e0` passed with no output. Worktree `git diff --check`, `git diff --name-only HEAD` and tracked status were empty. The detached worktree remained at the exact feature SHA.

DEV1 remains `integration` at `7650a487b36389cd0afdfaeae952bd6c9da969aa`, with no tracked modifications. Existing untracked paths were ASTRA-003-REPORT.md, ASTRA-004-REPORT.md, ASTRA-005-REPORT.md, INTEGRATION-RECOVERY-DASHBOARD-V1-REPORT.md, RELEASE-RECOVERY-ASTRA-AUDIT.md, RELEASE-RECOVERY-V1-REPORT.md, UAT-RBAC-AUDIT.md, UAT-V1-COMPLETENESS-AUDIT.md, UAT-V1-REPORT.md and doc/. This audit adds only **untracked ASTRA-OBLIGATION-PAYMENTS-AUDIT.md** to DEV1. No staging, commit, push, merge, reset, stash, clean or branch checkout occurred there.

Retained reproduction artifacts outside DEV1:

- [Diagnostic program](C:/Dev/audit-obligation-evidence-7567a97/Program.cs) and [project](C:/Dev/audit-obligation-evidence-7567a97/Probe.csproj).
- [Core adversarial outputs](C:/Dev/audit-obligation-evidence-7567a97/probe-output.txt).
- [Boundary, orphan, snapshot and real-VM outputs](C:/Dev/audit-obligation-evidence-7567a97/extra-output.txt).
- [Quick-flow, double-command and stale-target outputs](C:/Dev/audit-obligation-evidence-7567a97/workflow-output.txt).
- Captured customer/supplier SQL at `C:\Dev\audit-obligation-evidence-7567a97\bin\Debug\net10.0\Customer-page-sql.txt` and `Supplier-page-sql.txt`.
- EXPLAIN plans at `C:\Dev\audit-obligation-evidence-7567a97\bin\Debug\net10.0-windows10.0.19041\Customer-query-plans.txt` and `Supplier-query-plans.txt`.

Reproduction commands from the evidence directory: `dotnet run --project Probe.csproj`, `dotnet run --project Probe.csproj -- extra`, and `dotnet run --project Probe.csproj -- workflow`. These create and dispose synthetic fixture databases only. The detached checkout/evidence are retained so the lead can inspect exact source and rerun probes.

## 30. Final recommendation

**C. FIX REQUIRED BEFORE INTEGRATION**

Resolve the demonstrated read-integrity/filter/eligibility gaps and restore reachable explicit advance application; cover the corrections with symmetric permanent regressions. Re-establish a green ordinary full suite/Foundation baseline or explicitly resolve the existing concurrency-test timing issue. The positive build, unchanged schema, working normal quick-payment flow and known-dependency scan do not override the adversarial failures.

No fixes implemented. No permanent tests added. No commit. No push. No merge.
