# RELEASE RECOVERY ADVERSARIAL AUDIT

Audited working tree: `feature/release-recovery-v1`, HEAD/base `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`. Scope: B02/B03/M09 only.

| Check | Result | Evidence |
| --- | --- | --- |
| Old-schema PreMigration restore | **PASS** for accepting and recovering the actual artifact | Production `IDatabaseBackupService.RestoreAsync` accepted the actual schema-003 PreMigration `.fmbak`, preserved seeded user/financial records, retained the source artifact unchanged, and created a pre-restore safety artifact. It restored live schema **004**, not 003. |
| Restore → next startup → migration | **FAIL** for the specified journey | Restore migrates staging to 004 before replacement. Immediately after restore there are four applied migrations; the next startup finds no pending migration and creates no PreMigration artifact. Required steps 6, 8–10 cannot occur through this production path. |
| Pre-migration ordering | **FAIL** as an unconditional API guarantee; **PASS** in the wired Desktop path | Desktop supplies the service and follows validation → detection → validated PreMigration artifact → migration → post-validation → Daily. However, the public optional/null service path migrates an existing 003 database without any backup. |
| WAL/snapshot safety | **PASS** | Both PreMigration and normal backups use SQLite `BackupDatabase`, not a raw live-file copy. The external WAL oracle had 70,072 WAL bytes, one live sale, and zero sales in the main-file-only control; the actual PreMigration artifact preserved the committed sale and other seeded records. |
| Migration failure recovery artifact | **PASS** for retention and reporting | Existing failure test preserves the schema-003 artifact and old working schema when the pre-migration hook throws. Production wraps migration failures with the recovery path, and App logs/displays it. This does not make the raw-003 restore journey supported. |
| Once-per-day cross-process behavior | **PASS** for the specified publication ordering | The daily-state lookup is inside the exclusive filesystem guard. Two separate harness processes started before publication: A created the artifact; B invoked after A exited, acquired the guard, returned `Created=false` with the same path, and left exactly one artifact. |
| Partial artifact retry | **FAIL** for the complete requested invalid/partial-artifact requirement | An abandoned `.partial` file does not block publication (**PASS**). A zero-byte, malformed, or subsequently corrupted matching Daily `.fmbak` does block retry and returns `Success=true, Created=false` (**FAIL**). |
| Restore authorization/safety | **PASS** within inspected/tested paths | Employee denial, pre-swap validation, pre-restore safety backup, rollback on failed validation, successful session clearing, and bounded archive parsing are covered by passing tests and source review. WPF forwards invalidation to shell logout/closure. |
| Startup interface exposure | **PASS** within normal Desktop workflows | Only App startup resolves this interface in Desktop. Its two methods accept only a cancellation token; they expose neither an arbitrary destination nor restore. Employee UI routes cannot invoke it. |

## Concrete blockers

### 1. RELEASE BLOCKER — the explicitly required recovery journey is unsupported

`src/FinancialManagement.Infrastructure/Backup/DatabaseBackupService.cs:172` accepts a known older schema, but line 186 invokes `MigrateStagingAsync`; line 203 requires the current schema before replacement and line 278 requires it again after replacement. The existing `Restore_MigratesValidVersion002StagingWithoutChangingSource` test explicitly expects this upgrade behavior.

The external harness created a real migration-003 database, seeded an active Responsable, client, sale (125,075 minor units), and expense (5,000 minor units), and confirmed migration 004 was pending. Normal startup produced the actual PreMigration artifact and migrated the working database. Inspection confirmed that artifact contained three applied migrations and the original seeded data. A post-backup client marker was then added to live data before authenticating and restoring the actual artifact through the production interface.

Restore succeeded, removed the later marker, preserved all columns of the seeded records, cleared authentication, and left the artifact SHA-256 unchanged. However, live migration count was already **4**. A fresh service provider/context simulating the next normal startup reported **zero pending migrations**, no new PreMigration artifact, successful validation, and preserved records.

Thus the artifact is usable for recovery into the current schema. There is no evidence that legitimate schema-003 artifacts are rejected, and no seeded data loss was observed. The blocker is the exact required behavior: live schema 003 after restore followed by migration on the next startup is impossible through this path. Per the audit instruction, this is classified RELEASE BLOCKER.

### 2. RELEASE BLOCKER — invalid Daily artifacts suppress recovery protection

`src/FinancialManagement.Infrastructure/Backup/DatabaseBackupService.cs:52` selects the first matching filename and line 60 returns `AlreadyExists` without checking length, archive integrity, manifest/hash, or schema.

Three external cases independently reproduced the problem:

- A zero-byte `FinancialManagement_Daily_20260909_audit.fmbak`.
- The same matching name containing plain text rather than an archive.
- An actual successfully published Daily artifact overwritten with corrupt content.

In every case the next request returned `Success=true, Created=false`; another request on the same date behaved identically. No usable replacement was created for that date. Startup therefore receives no failure status for this condition. This violates the explicit requirement that an invalid Daily artifact must not count as successful protection.

## Concrete non-blockers and evidence limits

- **Optional startup service bypass:** `FinancialDatabaseStartup.cs:12` defaults the service to null; line 42 skips the backup requirement for null, but line 66 still migrates. The `null-service` harness migrated 003→004 with zero artifacts. This is a real public API guarantee gap, explaining the ordering FAIL above, but no normal shipped Desktop bypass was found: `App.xaml.cs:215` resolves the service as required and passes it at line 216.
- **Correct normal ordering:** with the production service supplied, old storage validation precedes detection and snapshot creation. Artifact creation validates the snapshot and publishes the closed archive before returning. A failed required backup throws before migration; the existing failure test confirms 004 remains unapplied and Daily is not called. The fresh-database harness completed all four migrations with no PreMigration artifact and one Daily artifact.
- **Correct snapshot mechanism and older-schema validation:** `DatabaseBackupService.cs:475` uses the shared snapshot pipeline; line 591 calls SQLite's backup API. Old-schema validation checks an exact known migration prefix, matching schema metadata, SQLite integrity/foreign keys, and financial storage. Restore independently validates the actual old artifact; creation-time validation was not used as a substitute for restore proof.
- **Correct guard placement:** the guard is acquired at `DatabaseBackupService.cs:44`, before enumeration at line 52. There is no production pre-guard lookup to become stale. The existing concurrency test uses one service/semaphore; the existing guard test holds another file handle in the same test process. Neither permanently tests two actual application processes with A publishing before B acquires the released guard. The external A/B harness supplies that additional evidence for the audited ordering. It is not a randomized simultaneous-process stress test.
- **Partial publication and date separation:** creation writes `.partial`, closes/flushes it, then uses a same-directory `File.Move` to publish `.fmbak` (`DatabaseBackupService.cs:497`). An abandoned `.partial` allowed a valid Daily artifact. PreMigration names do not match Daily names. Advancing the harness date to September 10 created a new Daily artifact in all daily scenarios.
- **Restore protections:** authorization is checked before work and after waits; staged validation/migration and pre-restore backup precede the live swap. Existing tests cover rejected malformed/foreign/future/unsafe data, failures before replacement, post-swap validation rollback, WAL handling, and late authorization failures. Successful restore clears the session at line 310. `BackupViewModel` raises authentication invalidation; Administration forwards it; `ShellViewModel.RequestLogout` stops operations and logs out; `MainWindow` closes; App disposes the shell scope and returns to login. This WPF chain was inspected, not manually exercised against ProgramData.
- **Archive/path handling:** exactly two fixed archive entries are accepted, with bounded metadata/payloads and hash checks. Extraction writes to a caller-generated staging path and never combines archive names into filesystem paths. User backup destinations are canonicalized; generated filenames do not accept traversal components. Startup destinations come from configured options, not an Employee command.
- **Failure-test limit:** the existing migration-failure test injects its exception immediately before `MigrateAsync`; it proves artifact ordering/retention and exception reporting, not a real mid-migration engine failure. Production catches migration exceptions after the artifact exists. Cancellation is explicitly excluded from the recovery-path wrapper, although the already-published artifact remains on disk.
- **Missing permanent coverage:** actual PreMigration → production restore → fresh startup, corrupt/zero-byte Daily retry, true separate-process publication ordering, and omitted startup-service behavior were not covered by the existing passing recovery tests. No permanent test was added during this audit.

## Exact tests/harness executed

External workspace, containing harness source, logs, disposable databases, and TRX:

`C:\Users\HP\AppData\Local\Temp\FinancialManagement-RecoveryAudit-57565768587e4574816dc1097f30b003`

Existing tests were rebuilt and executed from the repository with:

```powershell
dotnet test tests/FinancialManagement.Tests/FinancialManagement.Tests.csproj --no-restore --filter "FullyQualifiedName~DatabaseStartupBackupTests|FullyQualifiedName~DatabaseBackupServiceTests" --logger "trx;LogFileName=recovery-existing.trx" --results-directory C:\Users\HP\AppData\Local\Temp\FinancialManagement-RecoveryAudit-57565768587e4574816dc1097f30b003/test-results --nologo
```

Result: **183 PASS / 0 FAIL / 0 SKIP**. The `DatabaseBackupServiceTests` partial class includes authorization, schema, financial-storage, and allocation-integrity cases. The startup selection includes all eight startup tests. The previously reported 1099-test full suite was not rerun or treated as adversarial proof.

External harness build: `dotnet build Audit.csproj --nologo -v minimal` — success, zero warnings/errors. Modes were executed as `dotnet .\bin\Debug\net10.0\Audit.dll <mode> <absolute-temp-data-directory>`:

| Mode / log | Scenario |
| --- | --- |
| `journey` / `journey.log` | Initial actual-artifact recovery run. Its positional `SELECT *` comparison was affected by migration column ordering; this diagnostic comparison was corrected only in the external harness. |
| `journey` / `journey-canonical.log` | Fresh repeat using sorted column-name/value comparisons; artifact and post-restore/post-startup seeded data all matched. Built with `dotnet build Audit.csproj --no-restore --nologo -v quiet`. This is the definitive data-preservation result. |
| `race` / `race.log` | Two distinct worker processes, both ready before publication; A publishes/exits, then B invokes the production Daily method. One artifact, both process exit codes zero. |
| `partial` / `partial.log` | Abandoned `.partial`, same-date repeat, next-date backup. |
| `zero` / `zero.log` | Zero-byte matching Daily artifact and retry/date behavior. |
| `invalid` / `invalid.log` | Malformed matching Daily artifact and retry/date behavior. |
| `corrupt-published` / `corrupt-published.log` | Corrupt a genuinely published Daily artifact, then retry. |
| `null-service` / `null-service.log` | Public startup invocation omitting backup service on schema 003. |
| `fresh` / `fresh.log` | Normal startup on a new empty disposable database. |

All harness runs completed with exit code zero; scenario findings above reflect observed application results, not a claim that every acceptance condition passed.

## Source modified: NONE

SHA-256 comparison of **411 source/test files** before and after execution found **0 changes**. Git status was unchanged before report creation. The only repository file created by this audit is this untracked report. No source or permanent tests were modified, staged, committed, or pushed. `git diff --check` passed.

## Real DB touched: NO

All test/harness databases were disposable files under TEMP. The real ProgramData database was not opened, copied, migrated, or restored, and the desktop application was not launched.

## FINAL: FIX REQUIRED
