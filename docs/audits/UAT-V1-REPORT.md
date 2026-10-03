# REAL UAT V1 — Phase 1 Report

Date: 2026-09-08
Repository: `C:\Dev\logiciel-gestion-financiere`
Release branch: `release/v1.0-rc`
Release hash: `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`

## Result

**PHASE 1 UAT RESULT: PASS**

The earlier ACL/environment episode is retained below as historical evidence. The clean non-elevated first run, Responsable bootstrap, login, opening balance, restart persistence, and final read-only database verification completed successfully.

## Environment and protected data

- OS: Microsoft Windows 11 Famille Unilingue, build 26200, 64-bit.
- Application database path: `C:\ProgramData\FinancialManagement\Data\FinancialManagement.db` (confirmed from `ApplicationPaths`).
- Existing database: present, 303,104 bytes, last write UTC `2026-09-05 00:02:51`.
- Existing sidecars: `FinancialManagement.db-wal` (0 bytes) and `FinancialManagement.db-shm` (32,768 bytes).
- Read-only inspection showed no users and no business rows; the database contains migration history 001–003 and is existing development/UAT data.
- Protected copy created outside the active data directory at `C:\Users\HP\AppData\Local\Temp\FinancialManagement-UAT-protected-20260908\`.
- Protected copy SHA-256 matches the active database: `CFD967320457B1C657166DDF07633D8ACA3D520BB7D02884AA8539D25B1D9B3E`.
- Pre-existing data protected: **YES**.

## UAT workflow

- Clean first-run achieved: **NO**.
- Startup result: **NOT RUN**; no UI action was performed.
- Migrations applied: **NONE** during UAT. Existing database history is 001, 002, 003.
- First screen: **NOT REACHED**.
- Responsable bootstrap: **NOT RUN**.
- Duplicate bootstrap behavior: **NOT TESTED**.
- Password masking: **NOT TESTED**.
- Login: **NOT RUN**.
- Active Responsable count: read-only pre-UAT count was 0; no UAT account was created.
- Initialization access: **NOT TESTED**.
- Cash entered: **NOT ENTERED**.
- Bank entered: **NOT ENTERED**.
- Opening persistence: **NOT TESTED**.
- Singleton behavior: **NOT TESTED**.
- Dashboard metrics: **NOT TESTED**.
- Restart and post-restart login/values: **NOT TESTED**.
- Database minor units/storage classes after UAT: **NOT APPLICABLE**.
- Logs/errors: no UAT application launch logs; no application exception observed.
- Warnings: the Windows ACL prevented renaming the existing database (`Access to the path is denied`).
- UI observations: none; the real Desktop UI was not launched.

## Classification and repository

- Blocker: inability to establish a clean disposable database without altering the existing database, due to current-user filesystem permissions.
- Non-blockers: none.
- Observations: the existing database is preserved and its WAL/SHM files were restored in place after inspection.
- Git status remains limited to local reports plus this untracked report:
  - `ASTRA-003-REPORT.md`
  - `ASTRA-004-REPORT.md`
  - `ASTRA-005-REPORT.md`
  - `UAT-V1-REPORT.md`
- No source modification, commit, push, merge, reset, rebase, or clean was performed.
- REAL UAT Phase 1 stopped before bootstrap as required.

## Baseline reference

- Validated baseline: 1090 PASS / 0 FAIL / 0 SKIP.
- Build: 0 warning / 0 error.
- `verify-foundation`: PASS.
- ASTRA-003, ASTRA-004, ASTRA-005: CLOSED.
- Release freeze: ACTIVE.

## ACL / clean-environment investigation

1. Current user: `med-amine\hp`.
2. Elevated: **NO**. The token is medium integrity; Administrators membership is deny-only under UAC.
3. FinancialManagement Desktop/application process running: **NO**. No matching FinancialManagement, WPF, dotnet, testhost, or vstest process was present.
4. FinancialManagement folder owner: `MED-AMINE\INPT`.
5. Data folder owner: `MED-AMINE\INPT`.
6. Database owner: `MED-AMINE\INPT`. WAL/SHM owners: `MED-AMINE\HP`.
7. Relevant ACLs (read-only inspection):
   - `FinancialManagement` and `Data`: SYSTEM and Administrators FullControl; owner `MED-AMINE\INPT` FullControl; Users inherited ReadAndExecute plus inherited `Write` with `WD,AD,WEA,WA` (write data/add file, append data/add subdirectory, write extended attributes, write attributes).
   - `FinancialManagement.db`: SYSTEM, Administrators and owner FullControl; Users `ReadAndExecute` only.
   - Existing WAL/SHM: SYSTEM, Administrators and `MED-AMINE\HP` FullControl; Users `ReadAndExecute`.
8. Normal-user read allowed: **YES**.
9. Normal-user create/write allowed: **YES** in the Data directory. A harmless probe file was created successfully.
10. Normal-user delete allowed: **YES** for the newly created probe; deletion succeeded.
11. Normal-user rename capability: **YES** for a newly created file owned by the user; rename and cleanup succeeded. Renaming the existing DB is denied because the existing file lacks user Delete/Write and the directory ACL lacks DeleteChild for Users.
12. Harmless probe creation: **SUCCESS** (`uat-permission-probe-20260908-2007.tmp`).
13. Harmless probe deletion: **SUCCESS**; no probe remains.
14. Lock suspected: **NO** based on no application process and ACL-consistent denial. `handle.exe` is not installed; `openfiles` could not query local handles without elevation, so no handle was forcibly inspected or closed.
15. Likely cause of `Access Denied`: NTFS ACL/ownership on the pre-existing admin-owned database, not general Data-directory write failure. The user has directory create/write rights but only read/execution on `FinancialManagement.db`; rename also requires file Delete and directory DeleteChild.
16. Product blocker: **NO — ENVIRONMENT-ONLY** on current evidence. A normal user can create application files in the expected ProgramData Data directory; this pre-existing admin-owned DB is the obstacle to preparing a clean UAT database.
17. Recommended safe next action: **A** — an Administrator safely renames the entire existing `Data` directory, preserving it, then the application is launched normally/non-elevated so it creates a fresh Data directory. Do not delete the preserved directory.
18. Source modifications: **NONE**.
19. Git modifications: **NONE** (the explicitly permitted untracked UAT report update is the only repository change; no tracked or staged files).

Phase 1 UAT status remains **BLOCKED**. No application launch or business scenario was started.

## Safe admin Data-directory preservation

- Admin preservation action performed: **YES**.
- Original `C:\ProgramData\FinancialManagement\Data` renamed as one whole directory: **YES**.
- Preserved path: `C:\ProgramData\FinancialManagement\Data-pre-UAT-20260908`.
- Preserved contents: `FinancialManagement.db`, `FinancialManagement.db-wal`, and `FinancialManagement.db-shm` all present with original sizes/timestamps.
- Preserved database SHA-256: `CFD967320457B1C657166DDF07633D8ACA3D520BB7D02884AA8539D25B1D9B3E` (expected hash; match confirmed).
- Original Data path now absent: **YES**.
- ACL unchanged: **YES**; the renamed directory and database retain the previously observed ACL entries.
- Database contents unchanged: **YES**; only the containing directory name changed.
- Application launched: **NO**.
- Source changes: **NONE**.
- UAT status: **BLOCKED**, pending clean first launch.

The application was not launched and no new `Data` directory was pre-created.

## Clean non-elevated first launch

- Clean non-elevated launch attempted: **YES**.
- Windows user: `med-amine\hp`.
- Elevation: **NO**, medium integrity.
- Executable: `src\FinancialManagement.Desktop\bin\Debug\net10.0-windows\FinancialManagement.Desktop.exe`.
- Build: **SUCCESS**, 0 warnings, 0 errors.
- Data directory automatically created: **YES** — `C:\ProgramData\FinancialManagement\Data`.
- Database automatically created: **YES** — `FinancialManagement.db` (4096 bytes at initial observation).
- Sidecars: `FinancialManagement.db-wal` and `FinancialManagement.db-shm` created automatically.
- Startup succeeded: **YES**.
- Startup duration: approximately 1 second to process/window creation; stable first-screen observation after approximately 12 seconds.
- Migrations after startup: exactly 001, 002, 003, 004; no migration 005.
- First screen reached: **YES** — window title `Première initialisation — Gestion financière`.
- Responsable bootstrap screen reached: **YES**.
- Visible first-run content: `PREMIÈRE INITIALISATION`, `Créer le Responsable`, explanatory text, fields `Nom complet *`, `Nom d'utilisateur *`, `Mot de passe *`, `Confirmer le mot de passe *`, and `Créer le Responsable` button. Password fields are PasswordBox controls.
- Users count: **0**.
- Unexpected financial rows: **NONE** — Sales, Purchases, Expenses, CustomerPayments, SupplierPayments, TreasuryOpeningBalance, InitialCustomerReceivables and InitialSupplierPayables all count 0.
- UI errors: **NONE**; no raw SQLite/EF/storage error was shown.
- Log errors: **0 observed**; no application log file was emitted under the application data directory during this launch.
- Log warnings: **0 observed**.
- Hardened schema verification: all 12 audited financial amount columns are declared `INTEGER`; migration 004 is present in `__EFMigrationsHistory`.
- Previous ENVIRONMENT-ONLY blockage resolved: **YES** for clean-environment creation.
- Release blocker candidate: **NO**.
- Phase 1 status: **IN PROGRESS** — bootstrap, login, initialization and dashboard scenarios remain intentionally untested.

The application remains stopped at the initial Responsable screen. No Responsable was created, no login was performed, and no Phase 2 workflow was started.



## Final read-only database verification — current result

1. Clean first run: **PASS**.
2. Migration history: exactly 001–004; no migration 005.
3. Responsable bootstrap: **PASS**.
4. Login: **PASS**.
5. Opening Cash: `20000.00 EUR` (`CashAmountMinor = 2000000`).
6. Opening Bank: `10000.00 EUR` (`BankAmountMinor = 1000000`).
7. Total Treasury: `30000.00 EUR`.
8. TreasuryOpeningBalance rows: **1**.
9. Persisted Cash minor units: `2000000`.
10. Persisted Bank minor units: `1000000`.
11. SQLite storage classes: both opening values are `integer`; no REAL/TEXT financial amount observed.
12. OpeningDate: `2026-09-08`.
13. Restart persistence: **PASS**; Responsable/login and opening/dashboard values persisted.
14. Dashboard: Sales 0; Purchases 0; Customer collections 0; Supplier payments 0; Expenses 0; Result 0; Customer receivables 0; Supplier payables 0; Cash `20000.00`; Bank `10000.00`; Total Treasury `30000.00`.
15. Financial row counts: Sales 0; Purchases 0; Expenses 0; CustomerPayments 0; SupplierPayments 0; CustomerPaymentAllocations 0; SupplierPaymentAllocations 0; InitialCustomerReceivables 0; InitialSupplierPayables 0; TreasuryTransfers 0.
16. Clients: 0. Suppliers: 0.
17. SQLite integrity: **PASS** (`ok`).
18. Foreign-key integrity: **PASS** (no violations).
19. Logs: **NO LOG FILE OBSERVED**; errors 0 and warnings 0 observed.
20. Blockers: **NONE**.
21. Non-blockers: **NONE**. Observation: application data is persisted with integer storage classes and no unexpected startup rows.
22. Final Git status: only `ASTRA-003-REPORT.md`, `ASTRA-004-REPORT.md`, `ASTRA-005-REPORT.md`, and `UAT-V1-REPORT.md` untracked; no tracked or staged changes.

**PHASE 1 UAT RESULT: PASS**

Phase 2 is not started. No data was modified during this verification; all database checks used read-only access.
