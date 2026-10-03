# ASTRA-005 — SQLite integer storage-class integrity audit

## Atomic redesign and implementation — current review (2026-09-08)

This section supersedes the earlier STOP report and the historical audit retained below. The rejected generated scaffold was never executed. Its replacement was exercised only on disposable test/performance databases. **Final validation gate: PASS. ASTRA-005 STATUS: CLOSED — accepted and frozen. IMPLEMENTATION READY FOR LEAD REVIEW: YES.**

### Baseline, scope and implementation

- Branch: `fix/astra-005-sqlite-integer-affinity`.
- HEAD and `origin/integration`: `6dbdfd84c973e067122a76101aea2f7073ae6d53`.
- Migration: `20260908072131_004_EnforceFinancialIntegerStorageClass`.
- Main migration source SHA256: `7D853D9C963B4465D94CE95D3B543E56831FEBF6D8343D9F2FC9975BE53DE024`.
- Reviewed Up SQL operation SHA256 (LF-normalized): `352673232CDDE978D8C99B279435B991D14B56D21919C16A12CC6354B992A526`.
- EF Core / Microsoft.Data.Sqlite: 10.0.11; actual SQLite: 3.53.3.
- No Domain, Application, DbContext layout, packages, ASTRA-003 implementation/tests, or ASTRA-004 allocation implementation/tests were changed. Existing migration-count assertions were updated from 3 to 4; their behavioral assertions were retained.
- Both reports remain local/untracked. ASTRA-004 report was not edited. The accepted source/test scope was subsequently frozen in the commit recorded below; neither report was staged.

### Exact current source/test file changes

Paths relative to `C:/Dev/logiciel-gestion-financiere` (new files remain untracked):

- `src/FinancialManagement.Desktop/App.xaml.cs`
- `src/FinancialManagement.Infrastructure/Backup/DatabaseBackupService.cs`
- `src/FinancialManagement.Infrastructure/Backup/DatabaseSchemaValidator.cs`
- `src/FinancialManagement.Infrastructure/Configurations/CustomerPaymentAllocationConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/CustomerPaymentConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/ExpenseConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/InitialCustomerReceivableConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/InitialSupplierPayableConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/PurchaseConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/SaleConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/SupplierPaymentAllocationConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/SupplierPaymentConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/TreasuryOpeningBalanceConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Configurations/TreasuryTransferConfiguration.cs`
- `src/FinancialManagement.Infrastructure/Migrations/FinancialManagementDbContextModelSnapshot.cs`
- `tests/FinancialManagement.Tests/DatabaseBackupSchemaTests.cs`
- `tests/FinancialManagement.Tests/DatabaseBackupServiceTests.cs`
- `tests/FinancialManagement.Tests/FinancialManagementMigrationTests.cs`
- `src/FinancialManagement.Infrastructure/FinancialDatabaseStartup.cs`
- `src/FinancialManagement.Infrastructure/FinancialStorageValidator.cs`
- `src/FinancialManagement.Infrastructure/Migrations/20260908072131_004_EnforceFinancialIntegerStorageClass.Designer.cs`
- `src/FinancialManagement.Infrastructure/Migrations/20260908072131_004_EnforceFinancialIntegerStorageClass.cs`
- `tests/FinancialManagement.Tests/DatabaseBackupFinancialStorageTests.cs`
- `tests/FinancialManagement.Tests/FinancialStorageIntegrityTests.cs`
- `tests/FinancialManagement.Tests/FinancialStorageTestDatabase.cs`

### Blockers and transaction design

Blocker A was loss of the unmodelled expression index `UX_TreasuryOpeningBalance_Singleton`. Blocker B was the automatic SQLite rebuild's transaction suppression. Inspection of the installed provider's actual `IMigrationsSqlGenerator.Generate` output showed twelve `AddCheckConstraintOperation` inputs and two generated `MigrationCommand` instances with `TransactionSuppressed = true`: `PRAGMA foreign_keys = 0;` and `PRAGMA foreign_keys = 1;`. The executor/script generator commits around these commands. Merely adding a final CREATE INDEX would not repair atomicity.

The replacement emits one ordinary, non-suppressed EF `SqlOperation` for Up and one for Down. **EF owns the transaction, including migration-history insertion/deletion.** The migration SQL does not issue BEGIN, COMMIT, or disable foreign keys. No nested transaction is introduced.

The foreign-key strategy was proven before executing redesigned 004. A disposable SQLite prototype first demonstrated the trap: with deferred FKs, dropping a payment parent still cascaded away its allocation (remaining rows = 0). The safe prototype created replacement children referencing replacement parents, copied rows, dropped old children before old parents, then renamed the replacements. Commit preserved the allocation and reported zero FK violations. An injected SQL error rolled back the whole prototype, restoring the original allocation and leaving no replacement tables. Permanent tests subsequently proved this on the full 003 financial graph in both directions.

The controlled sequence, all inside EF's transaction, is:

1. Create a temporary guard with `Value BLOB CHECK(typeof(Value)='integer')`.
2. For all 12 columns, select only physically non-integer source values into the guard. BLOB has no numeric affinity: REAL 100.5 and TEXT abc both cause SQLite extended error 275. No EF monetary materialization is used.
3. Enable `PRAGMA defer_foreign_keys = ON`; foreign_keys stays ON.
4. Preserve the original AUTOINCREMENT high-water metadata.
5. Create/copy the nine replacement parent tables followed by both allocation tables. Replacement allocation FKs point to the replacement financial parents, not the old tables. All monetary copies are explicit same-column SELECTs.
6. Drop the two original allocation tables first, then the original parents in reverse dependency order. This prevents cascade loss.
7. Rename parents before replacement allocations. SQLite updates their temporary parent references to the final canonical names.
8. Recreate 42 modelled indexes plus the exact unique singleton expression index; restore AUTOINCREMENT high-water marks.
9. Repeat all 12 raw storage assertions.
10. Insert a non-integer diagnostic value into the same guard if `pragma_foreign_key_check` returns a violation. This deliberately aborts; it is not a SELECT-only assertion. Drop temporary guard/sequence tables.
11. EF records migration history and commits once.

Down uses the same ordering and atomicity, recreates the same singleton/index/FK/business constraints, and omits only the 12 storage CHECKs. It returns to weaker storage guarantees without transforming any amount. Both directions reject malformed input rather than repair it.

### Final script acceptance

Generated with normal `dotnet ef migrations script` tooling, not executed against any canonical/user database:

| Gate | Up | Down |
|---|---:|---:|
| BEGIN TRANSACTION | 1 | 1 |
| COMMIT | 1 | 1 |
| History operation before COMMIT | YES | YES |
| Intermediate COMMIT | NO | NO |
| foreign_keys disabled | NO | NO |
| Singleton explicitly recreated | YES | YES |
| Other indexes recreated | 42 | 42 |
| Foreign keys preserved | 23 | 23 |
| Existing business CHECKs preserved | 13 | 13 |
| Financial storage CHECKs in resulting DDL | 12 | 0 |
| Raw pre/post storage predicates | 24 | 24 |
| Financial CAST/ROUND/default substitution | NONE | NONE |

Up script: `C:/Users/HP/AppData/Local/Temp/astra005-redesign/final-up.sql`; SHA256 `E2306EA68455830D772ED28B8CB2B9553C25E76E96DBC0C96795845BAB2F5BD2`.
Down script: `C:/Users/HP/AppData/Local/Temp/astra005-redesign/final-down.sql`; SHA256 `7EC99CA1490BA3C6CFB804058407F8183E056EB48A8F5ECD58D27AA1B690B85B`.

### Exact storage coverage

All twelve fields remain non-null C# `long`, SQLite `INTEGER`, without defaults. IDs/FKs were not hardened. Positivity rules are separate and unchanged; both opening amounts still allow negative integers.

| Table | Column | Added CHECK name | Coverage |
|---|---|---|---|
| CustomerPayments | AmountMinor | CK_CustomerPayments_AmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| Expenses | AmountMinor | CK_Expenses_AmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| InitialCustomerReceivables | InitialAmountMinor | CK_InitialCustomerReceivables_InitialAmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| InitialSupplierPayables | InitialAmountMinor | CK_InitialSupplierPayables_InitialAmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| Purchases | TotalAmountMinor | CK_Purchases_TotalAmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| Sales | TotalAmountMinor | CK_Sales_TotalAmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| SupplierPayments | AmountMinor | CK_SupplierPayments_AmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| TreasuryOpeningBalance | BankAmountMinor | CK_TreasuryOpeningBalance_BankAmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| TreasuryOpeningBalance | CashAmountMinor | CK_TreasuryOpeningBalance_CashAmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| TreasuryTransfers | AmountMinor | CK_TreasuryTransfers_AmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| CustomerPaymentAllocations | AmountMinor | CK_CustomerPaymentAllocations_AmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |
| SupplierPaymentAllocations | AmountMinor | CK_SupplierPaymentAllocations_AmountMinor_IntegerStorage | Model / Up / preflight / postcheck / raw validator / permanent tests |

### Migration-003 index inventory (preserved in Up and Down)

The inventory includes migration 003's raw expression index. All listed indexes have SQLite origin `c` (explicit CREATE INDEX), non-partial, ascending BINARY keys. The rowid PRIMARY KEY is inline INTEGER PRIMARY KEY AUTOINCREMENT and has no separate implicit index. Before/after tests compare actual PRAGMA metadata, including nullability, defaults, PK, FK actions, index uniqueness, origin, key order and collation.

| Index | Table | Unique | Columns / expression | Origin |
|---|---|---|---|---|
| IX_CustomerPayments_ClientId | CustomerPayments | NO | ClientId | c |
| IX_CustomerPayments_CreatedByUserId | CustomerPayments | NO | CreatedByUserId | c |
| IX_CustomerPayments_PaymentDate | CustomerPayments | NO | PaymentDate | c |
| IX_CustomerPayments_Reference | CustomerPayments | YES | Reference | c |
| IX_Expenses_ConcernedEmployeeUserId | Expenses | NO | ConcernedEmployeeUserId | c |
| IX_Expenses_CreatedByUserId | Expenses | NO | CreatedByUserId | c |
| IX_Expenses_ExpenseCategoryId | Expenses | NO | ExpenseCategoryId | c |
| IX_Expenses_ExpenseDate | Expenses | NO | ExpenseDate | c |
| IX_Expenses_Reference | Expenses | YES | Reference | c |
| IX_InitialCustomerReceivables_ClientId | InitialCustomerReceivables | NO | ClientId | c |
| IX_InitialCustomerReceivables_CreatedByUserId | InitialCustomerReceivables | NO | CreatedByUserId | c |
| IX_InitialCustomerReceivables_DueDate | InitialCustomerReceivables | NO | DueDate | c |
| IX_InitialCustomerReceivables_Reference | InitialCustomerReceivables | YES | Reference | c |
| IX_InitialSupplierPayables_CreatedByUserId | InitialSupplierPayables | NO | CreatedByUserId | c |
| IX_InitialSupplierPayables_DueDate | InitialSupplierPayables | NO | DueDate | c |
| IX_InitialSupplierPayables_Reference | InitialSupplierPayables | YES | Reference | c |
| IX_InitialSupplierPayables_SupplierId | InitialSupplierPayables | NO | SupplierId | c |
| IX_Purchases_CreatedByUserId | Purchases | NO | CreatedByUserId | c |
| IX_Purchases_DueDate | Purchases | NO | DueDate | c |
| IX_Purchases_PurchaseDate | Purchases | NO | PurchaseDate | c |
| IX_Purchases_Reference | Purchases | YES | Reference | c |
| IX_Purchases_SupplierId | Purchases | NO | SupplierId | c |
| IX_Sales_ClientId | Sales | NO | ClientId | c |
| IX_Sales_CreatedByUserId | Sales | NO | CreatedByUserId | c |
| IX_Sales_DueDate | Sales | NO | DueDate | c |
| IX_Sales_Reference | Sales | YES | Reference | c |
| IX_Sales_SaleDate | Sales | NO | SaleDate | c |
| IX_SupplierPayments_CreatedByUserId | SupplierPayments | NO | CreatedByUserId | c |
| IX_SupplierPayments_PaymentDate | SupplierPayments | NO | PaymentDate | c |
| IX_SupplierPayments_Reference | SupplierPayments | YES | Reference | c |
| IX_SupplierPayments_SupplierId | SupplierPayments | NO | SupplierId | c |
| IX_TreasuryOpeningBalance_CreatedByUserId | TreasuryOpeningBalance | NO | CreatedByUserId | c |
| IX_TreasuryOpeningBalance_OpeningDate | TreasuryOpeningBalance | NO | OpeningDate | c |
| IX_TreasuryTransfers_CreatedByUserId | TreasuryTransfers | NO | CreatedByUserId | c |
| IX_TreasuryTransfers_Reference | TreasuryTransfers | YES | Reference | c |
| IX_TreasuryTransfers_TransferDate | TreasuryTransfers | NO | TransferDate | c |
| IX_CustomerPaymentAllocations_CustomerPaymentId | CustomerPaymentAllocations | NO | CustomerPaymentId | c |
| IX_CustomerPaymentAllocations_InitialCustomerReceivableId | CustomerPaymentAllocations | NO | InitialCustomerReceivableId | c |
| IX_CustomerPaymentAllocations_SaleId | CustomerPaymentAllocations | NO | SaleId | c |
| IX_SupplierPaymentAllocations_InitialSupplierPayableId | SupplierPaymentAllocations | NO | InitialSupplierPayableId | c |
| IX_SupplierPaymentAllocations_PurchaseId | SupplierPaymentAllocations | NO | PurchaseId | c |
| IX_SupplierPaymentAllocations_SupplierPaymentId | SupplierPaymentAllocations | NO | SupplierPaymentId | c |
| UX_TreasuryOpeningBalance_Singleton | TreasuryOpeningBalance | YES | ((1)) | c |

### Migration-003 FK inventory (preserved)

All parent columns are `Id`. Every ON UPDATE remains NO ACTION. The two payment-to-allocation relationships keep CASCADE; all other financial relationships keep RESTRICT.

| Child table | Child column | Parent table | Parent column | ON DELETE | ON UPDATE |
|---|---|---|---|---|---|
| CustomerPayments | ClientId | Clients | Id | RESTRICT | NO ACTION |
| CustomerPayments | CreatedByUserId | Users | Id | RESTRICT | NO ACTION |
| Expenses | ExpenseCategoryId | ExpenseCategories | Id | RESTRICT | NO ACTION |
| Expenses | ConcernedEmployeeUserId | Users | Id | RESTRICT | NO ACTION |
| Expenses | CreatedByUserId | Users | Id | RESTRICT | NO ACTION |
| InitialCustomerReceivables | ClientId | Clients | Id | RESTRICT | NO ACTION |
| InitialCustomerReceivables | CreatedByUserId | Users | Id | RESTRICT | NO ACTION |
| InitialSupplierPayables | SupplierId | Suppliers | Id | RESTRICT | NO ACTION |
| InitialSupplierPayables | CreatedByUserId | Users | Id | RESTRICT | NO ACTION |
| Purchases | SupplierId | Suppliers | Id | RESTRICT | NO ACTION |
| Purchases | CreatedByUserId | Users | Id | RESTRICT | NO ACTION |
| Sales | ClientId | Clients | Id | RESTRICT | NO ACTION |
| Sales | CreatedByUserId | Users | Id | RESTRICT | NO ACTION |
| SupplierPayments | SupplierId | Suppliers | Id | RESTRICT | NO ACTION |
| SupplierPayments | CreatedByUserId | Users | Id | RESTRICT | NO ACTION |
| TreasuryOpeningBalance | CreatedByUserId | Users | Id | RESTRICT | NO ACTION |
| TreasuryTransfers | CreatedByUserId | Users | Id | RESTRICT | NO ACTION |
| CustomerPaymentAllocations | CustomerPaymentId | CustomerPayments | Id | CASCADE | NO ACTION |
| CustomerPaymentAllocations | InitialCustomerReceivableId | InitialCustomerReceivables | Id | RESTRICT | NO ACTION |
| CustomerPaymentAllocations | SaleId | Sales | Id | RESTRICT | NO ACTION |
| SupplierPaymentAllocations | InitialSupplierPayableId | InitialSupplierPayables | Id | RESTRICT | NO ACTION |
| SupplierPaymentAllocations | PurchaseId | Purchases | Id | RESTRICT | NO ACTION |
| SupplierPaymentAllocations | SupplierPaymentId | SupplierPayments | Id | CASCADE | NO ACTION |

### Existing business CHECK inventory (retained separately)

| Table | Existing CHECK | Unchanged expression |
|---|---|---|
| CustomerPayments | CK_CustomerPayments_AmountMinor_Positive | `"AmountMinor" > 0` |
| Expenses | CK_Expenses_AmountMinor_Positive | `"AmountMinor" > 0` |
| InitialCustomerReceivables | CK_InitialCustomerReceivables_InitialAmountMinor_Positive | `"InitialAmountMinor" > 0` |
| InitialSupplierPayables | CK_InitialSupplierPayables_InitialAmountMinor_Positive | `"InitialAmountMinor" > 0` |
| Purchases | CK_Purchases_TotalAmountMinor_Positive | `"TotalAmountMinor" > 0` |
| Sales | CK_Sales_TotalAmountMinor_Positive | `"TotalAmountMinor" > 0` |
| SupplierPayments | CK_SupplierPayments_AmountMinor_Positive | `"AmountMinor" > 0` |
| TreasuryTransfers | CK_TreasuryTransfers_AmountMinor_Positive | `"AmountMinor" > 0` |
| TreasuryTransfers | CK_TreasuryTransfers_DistinctAccounts | `"FromAccount" <> "ToAccount"` |
| CustomerPaymentAllocations | CK_CustomerPaymentAllocations_AmountMinor_Positive | `"AmountMinor" > 0` |
| CustomerPaymentAllocations | CK_CustomerPaymentAllocations_Target_Xor | `("SaleId" IS NOT NULL AND "InitialCustomerReceivableId" IS NULL) OR ("SaleId" IS NULL AND "InitialCustomerReceivableId" IS NOT NULL)` |
| SupplierPaymentAllocations | CK_SupplierPaymentAllocations_AmountMinor_Positive | `"AmountMinor" > 0` |
| SupplierPaymentAllocations | CK_SupplierPaymentAllocations_Target_Xor | `("PurchaseId" IS NOT NULL AND "InitialSupplierPayableId" IS NULL) OR ("PurchaseId" IS NULL AND "InitialSupplierPayableId" IS NOT NULL)` |

### Raw validator, startup and Restore

`FinancialStorageValidator` is reusable, read-only Infrastructure code. It opens a deferred read transaction on its caller-owned open connection, inspects migration history/table presence, and scans the eleven tables with twelve qualified `typeof(f.Column)` predicates. The two opening fields share a table scan. No amount is read as a CLR long. Qualified identifiers prevent SQLite's double-quoted-string fallback from hiding missing columns on empty tables.

A fresh empty database (or empty EF bookkeeping only) is allowed only in pre-startup mode. Once migration history contains rows, every financial table/column is required. A missing table/column, malformed value, or SQLite validation failure returns invalid. Cancellation propagates. The validator makes no data or schema changes.

`FinancialDatabaseStartup.InitializeAsync` opens the connection, validates legacy/fresh storage, runs `Database.MigrateAsync`, validates current storage, then returns to normal initialization. A controlled `FinancialStorageIntegrityException` blocks return to Login/Shell. Desktop's only functional changes are this invocation and a French integrity message in the existing startup catch-and-shutdown path. No raw SQL is added to WPF and raw exception messages are not shown to the user. Backend startup tests are automated; WPF dialog display was verified by source control-flow review, not by launching a user database.

The Restore sequence is now: archive/manifest/hash extraction → SQLite integrity/FK checks → history and exact schema checks → raw financial storage validation → staging migrations when needed → checkpoint/close → SQLite/schema validation and raw financial storage validation again → unchanged ASTRA-004 allocation checks → active RESPONSABLE check → existing authorization/safety-backup gates → atomic live replacement → the same raw/allocation/schema post-swap checks and existing rollback behavior.

The original structural-before-raw order is preserved. On hardened files SQLite's integrity_check may already reject a bypassed CHECK violation; raw validation remains an independent defense and handles valid legacy schema data before migration. REAL/TEXT legacy backups with valid archive/hash envelopes return InvalidBackup before safety backup or live replacement.

Backup creation remains a faithful SQLite snapshot and never repairs source values. The common validator also rejects a malformed snapshot instead of publishing it. The source file/physical storage stays unchanged. Integer-valued ASTRA-004 aggregate corruption remains governed by the existing allocation checks; those tests are unchanged.

### Schema validation contract

Previously `DatabaseSchemaValidator` rejected every raw-SQL migration operation except 003's specific singleton creation. It now also recognizes exactly class 004, exactly one non-suppressed SqlOperation and the fixed reviewed Up SQL fingerprint shown above. Changed/suppressed SQL and unrelated raw migrations remain rejected. This is not a blanket RawSql allowlist.

Final schema validation is still mandatory: compiled target-model tables/CHECKs/PKs/FKs/indexes are compared to sqlite_master and PRAGMA metadata, with 003's singleton as an explicit supplement. The singleton's uniqueness, non-partial form, BINARY ascending expression key and actual SQL expression are verified. Tests reject a dropped singleton or weakened storage check on schema 004, as well as altered SQL fingerprints.

### Permanent proof and test coverage

- Raw INSERT and UPDATE paths cover every column, including integer 100, REAL 100.0, numeric text 100/scientific notation (accepted only with persisted integer), REAL/text 100.5 and abc (rejected), blob/null rejection on updates, Int64 max and opening Int64 min.
- Migration-003 data is populated across all eleven tables, including both customer/supplier allocations to documents and initial obligations. Up and Down compare every persisted field, metadata, all values, AUTOINCREMENT high-water, FK/integrity checks and the singleton's actual second-row rejection.
- Every financial column is tested with legacy REAL and separately TEXT. Preflight failure preserves physical quoted data, old schema and migration history.
- A test-only DbCommandInterceptor injects an error after copies/drops/renames/index recreation, with both Up and Down rolling back all values/schema/indexes/history.
- Additional test-only injections introduce post-copy REAL storage (temporarily bypassing checks) or an orphan allocation. The storage/FK guards deliberately abort, and both directions roll back. Production has no failure hook.
- Fresh/valid legacy startup succeeds. Legacy/current malicious REAL/TEXT is rejected for all twelve columns. Missing legacy table or missing column on an empty table also fails.
- Valid 003 and hardened backups preserve all fields; every legacy REAL/TEXT financial column is rejected before swap with live/source/artifact unchanged. Real backup-service creation/restore round-trip passes; a malformed source is rejected without repair.
- Script tests assert one transaction/history ordering and singleton recreation in both directions.
- Existing ASTRA-004 tests and broader regressions are retained. Only version-count fixture/assertion maintenance was necessary for migration 004.

### Performance (approximate, disposable database)

Actual application DI/SQLite interceptor settings were used: SQLite 3.53.3, foreign_keys ON, WAL, synchronous FULL. The final sample ran alongside regression tests, so these are rough host measurements rather than isolated overhead estimates. Source: `C:/Users/HP/AppData/Local/Temp/astra005-redesign/performance-final.log`.

- 003, prepared raw INSERT batch of 10,000 Expenses, three samples: 419.32 / 368.93 / 234.70 ms.
- 004, same batch, three samples: 336.53 / 222.91 / 427.77 ms.
- Migration with 30,000 populated Expenses: 1,219.15 ms.
- Raw validator over 60,000 Expenses plus the ten other empty financial tables: 28.94 / 51.36 / 59.01 / 23.36 / 70.92 ms per pass.
- Current startup including pre/post scans and migration check: 296.21 ms.
- Dataset: 60,000 expense rows; database around 7.8 MB.

The write ranges overlap; this run cannot isolate a reliable percentage overhead. It shows no obvious unacceptable write/startup degradation at this size. Each row's typeof CHECK is constant work; startup/Restore scans scale with total financial rows, while migration copies/index rebuilds scale with database size. Restore uses the same raw validator: its two staging scans have this per-pass cost, plus separate artifact, structural, backup and post-swap work (not presented as a measured full Restore latency).

### Threat boundary and safety

Normal EF long writers remain integer-valued. Enforced CHECKs reject direct SQLite malformed INSERT/UPDATE attempts across all twelve columns. A hostile external process deliberately setting ignore_check_constraints or modifying file/schema internals exceeds normal write-boundary guarantees; startup/Restore scans detect malformed storage at those boundaries, not continuously after startup. No tamper-proof guarantee is claimed.

There is no financial CAST, rounding, truncation, replacement, clamping, zeroing, or malformed-row deletion in production changes. String Replace in migration construction changes DDL identifiers/constraint text only. sqlite_sequence metadata restoration preserves auto-generated identity high-water values; it is not a monetary update.

### Verification record and requested final checkpoints

- Observed restore/build/test/foundation results: `C:/Users/HP/AppData/Local/Temp/astra005-redesign/verification-results.md`. The PowerShell transcript did not capture native command stdout; this results summary records the command output observed through the execution tool. The final freeze rerun completed in execution session 50152 with exit code 0.
- Build: 0 warnings / 0 errors.
- Complete direct regression: **1071 PASS / 0 FAIL / 0 SKIP**, 4 min 21 s. Foundation repeat: **1071 PASS / 0 FAIL / 0 SKIP**, 4 min 20 s; **verify-foundation PASS**, final command-chain exit code 0.
- Package vulnerability check: none reported by the configured sources. Pending model changes: none.
- Exact migration list (001–003 unchanged; 004 added): `20260903232954_001_InitialSchema`, `20260904004853_002_AllowAnonymousSalePayments`, `20260904111121_003_TreasuryOpeningBalanceSingleton`, `20260908072131_004_EnforceFinancialIntegerStorageClass`. The tooling lists them as Pending against its empty design-time database; no canonical/user database was migrated.
- Targeted earlier runs: 44 storage/migration/startup tests PASS; 181 backup/failure-injection tests PASS (before the final additions and full rerun).
- Static/script audit: PASS; final git diff --check: PASS. Branch and both baseline hashes remain unchanged; staging is empty and both audit reports remain untracked.

| Requested report items | Result / evidence |
|---|---|
| 1–3. Original blockers and cause | Singleton loss and suppressed FK pragmas, explained above using actual generator commands |
| 4–7. Atomic redesign / owner / deferral / proof | Custom ordered rebuild; EF transaction owner; no suppressed SQL; prototype and permanent rollback proof |
| 8–10. Preflight / no affinity / coverage | BLOB guard with raw typeof on all 12 fields; REAL/TEXT rejection before copy |
| 11–12. Rebuild / constraints | Frozen 003 canonical DDL; separate 12 storage constraints matching EF model |
| 13–18. Up/Down singleton, indexes, FKs, CHECKs | Both directions retain singleton, 42 other indexes, 23 FKs, 13 old CHECKs; Down removes only storage checks |
| 19–22. Rollbacks and postchecks | Test-only mid-rebuild/post-storage/post-FK failures roll back; guard enforces both postconditions before history/commit |
| 23–25. Schema validator | Specific fingerprinted 004 exception; complete final-schema validation retained; unexpected SQL rejected |
| 26. EF consistency | Snapshot retains all 12 model CHECKs; final pending-model check reports no changes |
| 27–29. Startup | Raw pre/migrate/raw post; fresh allowed; corrupted database throws controlled error before normal UI flow |
| 30–32. Restore / corrupt backups / backup | Exact sequence above; InvalidBackup before swap; snapshot never repairs and invalid snapshot is rejected |
| 33–38. Raw 100 / 100.0 / numeric text / fractional / abc | Accepted only as integer; fractional/text corruption rejected; INSERT+UPDATE tests cover all columns |
| 39. Int64 | Max stored exactly; opening Min accepted; positive-only Min rejected by old business CHECK |
| 40–43. Legacy valid/REAL/TEXT and fresh | Value preservation / preflight rollback / clean 001–004 creation tests |
| 44–46. Backup variants | Valid old and hardened round-trips; corrupted archive/hash-valid sources rejected; source/live unchanged |
| 47–49. Performance | Actual WAL measurements above; Restore raw-validator per-pass cost measured, full Restore latency not benchmarked |
| 50–56. Regression / build / foundation / vulnerabilities / pending model / migrations | ASTRA-004 regressions retained and green; direct and foundation runs each 1071 PASS / 0 FAIL / 0 SKIP; build 0 warnings / 0 errors; foundation PASS; no known vulnerable packages reported; no pending model changes; exact four migrations listed above |
| 57–60. Final scripts / casts / repair | Atomic Up YES; atomic Down YES; financial casts absent; silent repair absent |
| 61–65. Layer changes | Desktop: minimal startup call/message. Application NO; Domain NO; DbContext NO; packages NO |
| 66–68. Git / reports / ASTRA-003 | diff check clean; both reports local; ASTRA-003 untouched |
| 69–71. Status / blocker / readiness | CLOSED by lead acceptance; implementation complete and validated; remaining technical blocker NONE; ASTRA-005 IMPLEMENTATION READY FOR LEAD REVIEW: YES |

### Accepted freeze and feature push (2026-09-08)

- Lead decision: **ASTRA-005 = ACCEPTED / CLOSED**.
- Exact staged scope: **25 files = 18 modified + 7 added**. `ASTRA-004-REPORT.md` and `ASTRA-005-REPORT.md` remained excluded and untracked.
- Commit: `e033e359446b481b16df16a7a3d5b23f34740396`.
- Commit message: `fix: enforce SQLite financial integer storage integrity`.
- Feature push: `origin/fix/astra-005-sqlite-integer-affinity` succeeded; no integration push or merge was performed.
- Final local feature HEAD equals remote feature HEAD: `e033e359446b481b16df16a7a3d5b23f34740396`.
- `origin/integration` remains at the parent baseline `6dbdfd84c973e067122a76101aea2f7073ae6d53`.
- Final tracked tree is clean; only the two permitted local audit reports remain untracked. `git diff --check` passes with Git's CRLF-to-LF advisory only.
- ASTRA-003 remains untouched and ASTRA-004 remains CLOSED. **ASTRA-005 FEATURE FROZEN: YES.**

### Archived earlier investigation

The sections below preserve the earlier rejected-scaffold and affinity audit history. Their OPEN/approval statements describe those earlier stages, not the current implementation.


## Approved implementation attempt — STOP at generated migration review (2026-09-08)

This update supersedes the status in the historical audit below. Migration approval was received. Implementation started, but the mandatory section 6 stop condition was met. **ASTRA-005 STATUS: OPEN. IMPLEMENTATION READY FOR LEAD REVIEW: NO.** The generated migration is an unexecuted draft and must not be applied as it stands.

### Exact blocker and evidence

The existing migration `20260904111121_003_TreasuryOpeningBalanceSingleton` creates `UX_TreasuryOpeningBalance_Singleton` with raw SQL `CREATE UNIQUE INDEX ... ON TreasuryOpeningBalance ((1))`. This object is absent from the EF model. The generated 004 SQL drops `TreasuryOpeningBalance` (Up script line 230), renames its copied replacement (line 232), then recreates only the CreatedByUserId and OpeningDate indexes (lines 281 and 283). There is no recreation of `UX_TreasuryOpeningBalance_Singleton` anywhere in the script. Executing it would silently remove the single-row guarantee. Down has the same omission (drop at line 218; normal indexes at lines 269 and 271).

The generated Up also commits copies at line 221, commits table replacements at line 270, commits indexes at line 359, and inserts migration history afterward at line 361. This does not satisfy the required atomic A–H sequence: later failures can leave previously committed work. Down likewise has separate commits. No raw preflight or post-copy storage/FK assertion exists in this scaffold yet.

Scripts generated for review only (no database execution):

- Up: `C:/Users/HP/AppData/Local/Temp/astra005-generated-up.sql`
- Down: `C:/Users/HP/AppData/Local/Temp/astra005-generated-down.sql`

Remaining work requires a reviewed migration execution design preserving the expression index in both directions, atomic rebuild/index/history handling, raw preflight without affinity conversion, post-copy storage and FK assertions, then the validator/startup/Restore integrations and required tests. Merely adding CHECK operations to the EF model is insufficient. The existing `DatabaseSchemaValidator` also explicitly rejects raw SQL operations from migrations other than 003; its contract must be reviewed when a customized 004 is implemented, without weakening schema verification.

### Exact changed/new source files

All paths below are relative to `C:/Dev/logiciel-gestion-financiere`:

1. `src/FinancialManagement.Infrastructure/Configurations/CustomerPaymentAllocationConfiguration.cs`
2. `src/FinancialManagement.Infrastructure/Configurations/CustomerPaymentConfiguration.cs`
3. `src/FinancialManagement.Infrastructure/Configurations/ExpenseConfiguration.cs`
4. `src/FinancialManagement.Infrastructure/Configurations/InitialCustomerReceivableConfiguration.cs`
5. `src/FinancialManagement.Infrastructure/Configurations/InitialSupplierPayableConfiguration.cs`
6. `src/FinancialManagement.Infrastructure/Configurations/PurchaseConfiguration.cs`
7. `src/FinancialManagement.Infrastructure/Configurations/SaleConfiguration.cs`
8. `src/FinancialManagement.Infrastructure/Configurations/SupplierPaymentAllocationConfiguration.cs`
9. `src/FinancialManagement.Infrastructure/Configurations/SupplierPaymentConfiguration.cs`
10. `src/FinancialManagement.Infrastructure/Configurations/TreasuryOpeningBalanceConfiguration.cs`
11. `src/FinancialManagement.Infrastructure/Configurations/TreasuryTransferConfiguration.cs`
12. `src/FinancialManagement.Infrastructure/Migrations/FinancialManagementDbContextModelSnapshot.cs`
13. `src/FinancialManagement.Infrastructure/Migrations/20260908072131_004_EnforceFinancialIntegerStorageClass.cs` (new, untracked)
14. `src/FinancialManagement.Infrastructure/Migrations/20260908072131_004_EnforceFinancialIntegerStorageClass.Designer.cs` (new, untracked)

This local untracked report was updated. `ASTRA-004-REPORT.md` was not edited. No staging, commit, push, merge, reset, rebase, or cleanup was performed.

### Required implementation report checkpoints

| Requested items | Status/evidence at mandatory stop |
|---|---|
| 1. Baseline | HEAD and origin/integration both `6dbdfd84c973e067122a76101aea2f7073ae6d53`; initial tracked tree clean |
| 2. Branch | `fix/astra-005-sqlite-integer-affinity` |
| 3. Changed files | Exact 14-file source list above, plus this untracked report |
| 4–6. Coverage, constraints, EF configurations | All 12 inventory columns below have separate named `CK_<Table>_<Column>_IntegerStorage` constraints in 11 configurations, scaffold, designer, and snapshot. Existing business CHECKs left intact. IDs/FKs not hardened |
| 7. Migration name/hash | `20260908072131_004_EnforceFinancialIntegerStorageClass`; main .cs SHA256 `CC81ACC67EB59F3A6F11B1A809C46728A40520BC8FCB0A376537AE6B9B7AF844` |
| 8–9. Up/Down | Generated AddCheck/DropCheck scaffolds inspected; both unsafe as generated for singleton preservation/atomicity; neither executed |
| 10–13. Preflight, raw typeof, BLOB mechanism, postvalidation | Not implemented; stop occurred before customizing scaffold. Existing audit proposal is historical, not an implemented guarantee |
| 14–16. Startup pre/postvalidation, controlled failure | Not implemented |
| 17–19. Restore pre/postvalidation and ordering | Not implemented; canonical pipeline untouched |
| 20. ASTRA-004 | Source/tests untouched; no migration applied |
| 21. Backup behavior | Unchanged; hardened behavior not tested |
| 22–27. Inputs 100, 100.0, numeric text, 100.5, abc, Int64 | New enforcement not executed/tested; earlier audit evidence remains historical baseline evidence only |
| 28–31. Valid/REAL/TEXT legacy migration, failure nonmutation | Not tested; no migration execution attempted |
| 32. Clean DB creation | Not tested under 004 |
| 33–37. PK/FK/delete actions/index/business CHECK preservation | Generated SQL inspected; modelled definitions retained, but unmodelled singleton lost. Runtime verification not run |
| 38. Treasury singleton | BLOCKER: absent from generated recreation in Up and Down |
| 39. Raw write rejection | Permanent tests not implemented/run |
| 40–43. Old valid, corrupted REAL/TEXT, hardened backup restores | Not tested under 004 |
| 44–45. Valid/corrupt startup | Not implemented/tested |
| 46–47. ASTRA-004/full regression | Not rerun due mandatory pre-execution stop. Prior validated baseline is 991 PASS / 0 FAIL / 0 SKIP; this is not a result for the incomplete working tree |
| 48. Build | EF tooling reported Build succeeded during generation/script review; explicit full build warning/error tally not run |
| 49–51. Foundation, vulnerabilities, pending model changes | Not rerun; no current PASS claimed |
| 52. Migration list | Source now contains 001, 002, 003 and new unexecuted 004; no DB history was changed by this implementation attempt |
| 53. Script audit | FAILED acceptance: singleton loss and multiple commit boundaries; exact lines above |
| 54–56. Financial casts, silent repair, money model | Generated copy SELECTs have no financial casts/default substitution; no repair code added; existing `long` money model unchanged |
| 57–61. Desktop/Application/Domain/DbContext/packages | All unchanged |
| 62. Performance | Not measured; implementation stopped before execution |
| 63. git diff --check | PASS (exit 0); Git emitted only a snapshot CRLF-to-LF notice |
| 64. Reports | Both local/untracked; ASTRA-004 report preserved untouched |
| 65. ASTRA-003 | Untouched; not investigated |
| 66–68. Status/blocker/readiness | OPEN; unsafe generated rebuild requires reviewed correction; IMPLEMENTATION READY FOR LEAD REVIEW: NO |

The remainder of this document is the prior audit, retained as historical evidence rather than a claim about the unfinished implementation.

## 1. Baseline and scope

- Official integration baseline: `6dbdfd84c973e067122a76101aea2f7073ae6d53`.
- Audit branch: `fix/astra-005-sqlite-integer-affinity`.
- Baseline tests/build: 991 PASS, 0 FAIL, 0 SKIP; build 0 warning, 0 error; `verify-foundation` PASS.
- `ASTRA-004` is CLOSED and was not changed. `ASTRA-003` is out of scope and was not changed.
- No tracked files were modified. The only pre-existing untracked artifact is `ASTRA-004-REPORT.md`; it remains untouched. This report is also intentionally untracked.
- Current migration list is unchanged: `20260903232954_001_InitialSchema`, `20260904004853_002_AllowAnonymousSalePayments`, `20260904111121_003_TreasuryOpeningBalanceSingleton`. `dotnet ef migrations has-pending-model-changes` reports no model changes.

The audit used disposable file-backed databases created from the current migrations and the installed project stack (`Microsoft.EntityFrameworkCore.Sqlite` 10.0.11, SQLite `3.53.3`). No user or canonical database was used.

## 2. Definitive financial minor-unit inventory

All listed properties are C# `long`, mapped to non-null SQLite `INTEGER`. None is nullable. Existing positivity checks are independent from storage-class integrity.

| Table | Column | Domain property | C# | Declared type | Nullable | Existing CHECK | Meaning | Negative allowed |
|---|---|---|---|---|---|---|---|---|
| Sales | TotalAmountMinor | `Sale.TotalAmountMinor` | `long` | INTEGER | No | `TotalAmountMinor > 0` | Sale total | No |
| Purchases | TotalAmountMinor | `Purchase.TotalAmountMinor` | `long` | INTEGER | No | `TotalAmountMinor > 0` | Purchase total | No |
| Expenses | AmountMinor | `Expense.AmountMinor` | `long` | INTEGER | No | `AmountMinor > 0` | Expense amount | No |
| CustomerPayments | AmountMinor | `CustomerPayment.AmountMinor` | `long` | INTEGER | No | `AmountMinor > 0` | Customer payment | No |
| CustomerPaymentAllocations | AmountMinor | `CustomerPaymentAllocation.AmountMinor` | `long` | INTEGER | No | `AmountMinor > 0` | Allocation applied to a customer payment | No |
| InitialCustomerReceivables | InitialAmountMinor | `InitialCustomerReceivable.InitialAmountMinor` | `long` | INTEGER | No | `InitialAmountMinor > 0` | Opening customer receivable | No |
| SupplierPayments | AmountMinor | `SupplierPayment.AmountMinor` | `long` | INTEGER | No | `AmountMinor > 0` | Supplier payment | No |
| SupplierPaymentAllocations | AmountMinor | `SupplierPaymentAllocation.AmountMinor` | `long` | INTEGER | No | `AmountMinor > 0` | Allocation applied to a supplier payment | No |
| InitialSupplierPayables | InitialAmountMinor | `InitialSupplierPayable.InitialAmountMinor` | `long` | INTEGER | No | `InitialAmountMinor > 0` | Opening supplier payable | No |
| TreasuryOpeningBalance | CashAmountMinor | `TreasuryOpeningBalance.CashAmountMinor` | `long` | INTEGER | No | None | Opening cash balance | Yes (business model currently permits it) |
| TreasuryOpeningBalance | BankAmountMinor | `TreasuryOpeningBalance.BankAmountMinor` | `long` | INTEGER | No | None | Opening bank balance | Yes (business model currently permits it) |
| TreasuryTransfers | AmountMinor | `TreasuryTransfer.AmountMinor` | `long` | INTEGER | No | `AmountMinor > 0` | Cash/bank transfer | No |

No other persisted `long` property is a financial amount. Entity IDs and foreign keys are identifiers, not monetary fields.

## 3. Actual SQLite DDL and current constraints

`PRAGMA table_info` on a migrated disposable database reports `INTEGER`, `notnull=1` for every inventory row above. The generated `sqlite_master.sql` contains the existing business checks shown in the table and ordinary PK/FK/index definitions. There is no `typeof(column) = 'integer'` check anywhere in the current configurations, migrations, or model snapshot. `TreasuryOpeningBalance` has no amount check at all.

Representative generated DDL is:

```sql
"TotalAmountMinor" INTEGER NOT NULL,
CONSTRAINT "CK_Sales_TotalAmountMinor_Positive" CHECK ("TotalAmountMinor" > 0)
```

The same pattern is used by Purchases, Expenses, both payment tables, both allocation tables, both initial-obligation tables, and TreasuryTransfers. TreasuryOpeningBalance declares `CashAmountMinor INTEGER NOT NULL` and `BankAmountMinor INTEGER NOT NULL` without amount checks.

## 4. Affinity versus storage class (actual stack)

`INTEGER` is a declared type and gives the column INTEGER affinity. It is not a storage-class assertion. SQLite stores values with runtime classes `integer`, `real`, `text`, `blob`, or `null`; affinity applies conversion rules before storage and does not require conversion when conversion is lossy or impossible.

The disposable harness queried `typeof()`, `quote()`, and the value after each parameterized insert into the real `Sales.TotalAmountMinor` column:

| Input | Result | `typeof()` | `quote()` |
|---|---|---|---|
| `100L` | accepted | integer | `100` |
| `100.0` (REAL parameter) | accepted | integer | `100` |
| `100.5` (REAL parameter) | accepted | real | `100.5` |
| text `"100"` | accepted | integer | `100` |
| text `"100.5"` | accepted | real | `100.5` |
| text `"1e2"` | accepted | integer | `100` |
| `1e2` (REAL parameter) | accepted | integer | `100` |
| `long.MaxValue` | accepted | integer | `9223372036854775807` |
| `long.MinValue` | rejected by the existing `> 0` check | — | — |
| text `"abc"` | accepted | text | `'abc'` |

Thus losslessly convertible REAL-looking inputs become integer before CHECK evaluation (`100.0` is valid integer storage), while fractional REAL and non-numeric TEXT remain malformed storage. The existing positivity check accepts `100.5` and even `"abc"`; it checks business comparison semantics, not storage class.

A separate disposable `INTEGER` probe confirmed both Int64 boundaries persist as integer storage, while `100.5` and text `"100.5"` persist as REAL.

## 5. EF Core materialization and normal writes

Against the project provider, an injected `Sales.TotalAmountMinor = 100.5` was read through `FinancialManagementDbContext.Sales`: EF returned `long` value `100` without throwing. An injected negative `TreasuryOpeningBalance.CashAmountMinor = -100.5` returned `-100`. Injected text `"abc"` returned `0`. A very large REAL (`9.223372036854776e18`) returned `long.MaxValue`. The observed behavior is provider conversion toward `Int64`, including truncation/clamping; it is not an integrity failure and must not be relied on for monetary correctness.

An ordinary EF write of C# `long 1234567890123` was verified with `typeof()` as `integer`. Normal EF parameter binding therefore does not generate REAL storage for valid `long` values.

## 6. Writer risk by financial area

Sales, Purchases, Expenses, Customer/Supplier Payments, Customer/Supplier Allocations, Initial Customer Receivables, Initial Supplier Payables, Treasury Opening, and Treasury Transfers all assign C# `long` values and are bound as integer parameters on normal EF writes. Their application validation also enforces business positivity where currently defined. No normal application writer was found that binds a `double`, `decimal`, or money string.

The actual writer risk is therefore low for normal application use, but the schema does not prevent corruption from raw SQL, imports, restores, or another process. The distinction is material: application correctness does not establish a database storage invariant.

## 7. ASTRA-004 interaction

An allocation row with physical `CustomerPaymentAllocations.AmountMinor = 100.5` was accepted by the current `> 0`/XOR checks. EF materialized the row as `AmountMinor = 100`, and reflective execution of the existing `AllocationIntegrity.ReadCustomerAsync` returned a non-null snapshot. Therefore ASTRA-004 sees the provider-converted value and cannot report the original REAL storage class. EF materialization can silently alter the value before ASTRA-004 validation; it does not produce `DataIntegrityViolation` for this corruption.

## 8. Read-service behavior

Payment reads, obligation lookups, dashboard calculations, treasury calculations, alerts, sales/purchases reads, expense reads, and initial-obligation reads all use EF projections/aggregates over `long` properties. The provider behavior above means malformed REAL/TEXT values can be silently converted before business checks. Some services may subsequently reject an out-of-range or impossible aggregate as `DataIntegrityViolation`; that is not a storage-class guarantee and is not deterministic for fractional values (for example `100.5` becomes `100`). No existing cross-service raw `typeof()` validation was found.

## 9. Restore, backup, and startup evidence

A disposable live database was migrated, populated, and backed up through the real `DatabaseBackupService`. A structurally valid `.fmbak` was then made by changing only `Sales.TotalAmountMinor` to REAL `100.5` and recomputing its manifest hash. The actual restore pipeline returned success and replaced the disposable live database; querying the live file afterward reported `typeof(TotalAmountMinor) = 'real'`, `quote = 100.5`. This value is outside ASTRA-004 allocation coverage, so current restore validation permits it.

With that corrupted value live, `CreateLocalBackupAsync` also succeeded and the extracted backup preserved `typeof() = 'real'`, `quote = 100.5`. There is no silent repair, but there is also no rejection.

Running `Database.MigrateAsync` on an already-installed disposable database containing the REAL value succeeded because no migration currently checks storage classes. No separate startup integrity validator exists. Consequently current startup neither detects nor repairs this corruption.

## 10. ASTRA-005 invariant and threat boundary

For every persisted financial minor-unit column, the required invariant is `typeof(value) = 'integer'`; nullable columns would explicitly permit NULL, but all current financial columns are non-null. Business rules such as `Amount > 0` remain separate invariants.

- Normal Application/EF writes: observed integer storage; protected by writer types/validation only.
- Restore/import of project backups: current pipeline validates structure, migrations, and allocations but not every financial storage class; REAL in Sales/Treasury/etc. can reach live replacement.
- Pre-existing corrupted DB: current `MigrateAsync` and startup do not scan it.
- Manual SQLite modification while stopped: currently unprotected and survives backup/restore.
- Arbitrary external process while running: currently unprotected; a schema CHECK would reject subsequent writes, while read/startup scans provide detection for legacy files.

## 11. Remediation options

| Option | Strength | Migration/compatibility | Complexity/performance | Limitations |
|---|---|---|---|---|
| A. Application-only validation | Helps normal paths; no DB boundary | No migration | Low write cost; scans must be added to reads | Raw SQL, restore, and external writers remain able to corrupt; EF may truncate before validation |
| B. Restore/startup validation | Detects legacy/imported corruption before use/swap | No schema rebuild | Full scans at startup/restore; no normal write cost | Does not stop an external writer after validation |
| C. Targeted `CHECK(typeof(column)='integer' AND ...)` | Enforces storage class at SQLite write boundary; preserves INTEGER/long model | Requires SQLite table rebuild for all affected tables | One CHECK evaluation per write; one-time rebuild | Existing corrupt rows must fail preflight; migration must preserve all metadata |
| D. STRICT tables | Strong table-wide typing (SQLite 3.53.3 supports STRICT) | Widespread table redesign/rebuild; EF has no simple strict-table model switch | Higher compatibility/testing surface | Broad change affects every column and future schema; unnecessary for the specific money invariant |
| E. Targeted CHECKs + fail-closed restore/startup scans | Covers DB writes, legacy DBs, and backup/import boundary | Same controlled rebuild as C plus validation code | Small per-write CHECK cost; full scans only at startup/restore | Requires approval and careful rebuild implementation |

The minimum robust design is Option E. It keeps money as C# `long`/SQLite INTEGER, avoids persistent REAL/decimal/double, rejects malformed storage at the SQLite boundary, and fails closed for legacy/imported files before live replacement.

## 12. Proposed migration (approval required; not implemented)

- Proposed name: `004_EnforceFinancialIntegerStorageClass`.
- Affected tables: `Sales`, `Purchases`, `Expenses`, `CustomerPayments`, `CustomerPaymentAllocations`, `InitialCustomerReceivables`, `SupplierPayments`, `SupplierPaymentAllocations`, `InitialSupplierPayables`, `TreasuryOpeningBalance`, `TreasuryTransfers`.
- Affected columns: the 12 columns in the inventory above.

### Up design

1. In the migration transaction, preflight every affected column with `typeof(column) <> 'integer'` (and `IS NOT NULL` if nullability ever changes). The preflight must deliberately fail the migration by inserting any offending value into a temporary table with `CHECK(typeof(Value) = 'integer')`; it must never cast, round, truncate, clamp, delete, or rewrite data.
2. Rebuild each affected SQLite table with the same columns and all existing PK/FK/CHECK definitions plus one storage CHECK per financial column, for example:
   `CHECK(typeof("TotalAmountMinor") = 'integer' AND "TotalAmountMinor" > 0)`.
   For TreasuryOpeningBalance use only the storage CHECKs because negative business values remain allowed.
3. Copy rows only after preflight succeeds, drop the old table, and recreate every original unique and normal index. Preserve FK targets, `ON DELETE` actions, nullable flags, defaults, and singleton semantics. Keep `PRAGMA foreign_keys` handling equivalent to EF's normal SQLite rebuild sequence.
4. Run a post-copy `typeof()` assertion for every affected column before the migration completes.

### Down design

Rebuild the same tables back to the current pre-hardening definitions, preserving all data and metadata. Down intentionally removes storage enforcement and therefore must be treated as a rollback to the weaker invariant, not as a repair operation.

### Existing database and backup behavior

- Valid existing V1/current DB (all financial values stored as integer): preflight passes, values remain byte-for-byte equivalent, and migration completes.
- Existing DB containing REAL/TEXT financial storage: preflight fails before any destructive table conversion; no value is changed and the application must remain on the old file.
- Older valid `.fmbak`: staging migration succeeds, then normal validation and swap proceed.
- Older/corrupted `.fmbak` containing REAL/TEXT: staging migration fails closed before live replacement; no silent cast is permitted.
- New hardened backup: restore succeeds after schema and storage validation.

SQLite rebuild verification must cover every affected table's PK, FK, cascade/restrict behavior, existing business CHECKs, unique indexes, normal indexes, nullability, defaults, and the TreasuryOpeningBalance singleton index.

## 13. Required permanent tests after approval

Add tests for integer acceptance, explicit `100.0` lossless conversion to integer, fractional REAL rejection, numeric TEXT behavior, Int64 boundaries, invalid-existing-DB migration failure without mutation, valid-existing-DB value preservation, clean database creation, all index/FK/CHECK preservation, ASTRA-004 allocation regression, old-valid-backup restore, corrupted-REAL-backup rejection before swap, hardened-backup restore, backup of a hardened DB, and startup validation. Include each of the 12 financial columns in the coverage matrix.

## 14. Decision gate

Schema hardening is required for a complete storage-class invariant. Therefore:

**ASTRA-005 STATUS: OPEN — MIGRATION APPROVAL REQUIRED**

**Schema change required: YES**

**Migration required: YES**

No migration, model snapshot, EF configuration, DbContext, Desktop, Domain, or production remediation code was implemented in this audit. Public business contracts remain unchanged.

**ASTRA-005 READY FOR LEAD REVIEW: YES**
