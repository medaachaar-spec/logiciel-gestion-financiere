# LOGIN PASSWORD VISIBILITY V1 — REPORT

## 1. Starting SHA

`b062f4dc7d8d8dd5639b7a35b1480ccc133da283`

The branch was created from the exact fetched `origin/integration` baseline.

## 2. Branch

`feature/login-password-visibility-v1`

No commit or push was performed.

## 3. Implementation approach

- Added a compact, icon-only WPF `ToggleButton` at the right edge of the existing password field.
- Kept the accepted Login card dimensions and general layout unchanged.
- Added an eye/eye-with-slash vector icon using native WPF shapes; no third-party UI dependency was introduced.
- Kept the original `PasswordBox` as the hidden representation and added a local `TextBox` shown only while password visibility is enabled.
- Reserved right-side input padding so password text never overlaps the eye control.

## 4. Plain-text exposure strategy

- No password property was added to `LoginViewModel` or any persistent application state.
- The visible value exists only in the Login window's visible `TextBox` while reveal mode is active.
- Returning to hidden mode copies the current value back to the `PasswordBox` and immediately clears the visible `TextBox`.
- Authentication selects the currently active UI representation into a method-local value and passes it directly to `LoginViewModel.AuthenticateAsync`.
- Both UI representations are cleared in `finally` after every authentication attempt.
- No password persistence, logging, database write, configuration entry, telemetry, or exception message was introduced.
- WPF/.NET string memory is not claimed to be perfectly erasable; this implementation minimizes practical lifetime and scope.

## 5. Files changed

- `src/FinancialManagement.Desktop/LoginWindow.xaml`
- `src/FinancialManagement.Desktop/LoginWindow.xaml.cs`
- `tests/FinancialManagement.Desktop.Tests/LoginPasswordVisibilityTests.cs` (new)
- `LOGIN-PASSWORD-VISIBILITY-V1-REPORT.md` (new, untracked report)

## 6. Keyboard and accessibility behavior

- Existing order remains username (`TabIndex=0`) → password (`TabIndex=1`) → login (`TabIndex=2`).
- The eye remains keyboard accessible after the login button (`TabIndex=3`).
- The login button remains the sole `IsDefault` action; toggling never submits the form.
- Accessible name and tooltip are `Afficher le mot de passe` while hidden and `Masquer le mot de passe` while visible.
- Focus returns to the active password control after toggling, with the visible caret placed at the end.

## 7. Clearing behavior

- Failed authentication: hidden and visible controls cleared; UI returns to hidden mode.
- Successful authentication: hidden and visible controls cleared before window closure.
- Blank password: no authentication service call; both controls remain cleared and hidden.
- Normal window close: both controls cleared.
- A close request remains cancelled while authentication is busy, preserving the existing lifecycle behavior.

## 8. Tests added

Permanent real-WPF coverage proves:

1. hidden mode is the default and the eye exists;
2. reveal preserves the exact current value;
3. hiding preserves edits and clears the visible copy;
4. repeated toggles do not lose or duplicate characters;
5. authentication uses the active hidden or visible value;
6. failed authentication clears both representations;
7. successful authentication clears both representations;
8. blank passwords are not submitted;
9. accessible show/hide state changes correctly;
10. username/password/login tab order and default action remain intact;
11. window close clears both representations.

## 9. Targeted results

- Login tests: **15/15 PASS** (new visibility coverage plus existing Login lifecycle/branding coverage).
- Desktop suite: **45/45 PASS**.

## 10. Full-suite result

- Desktop: **45/45 PASS**.
- Core: **1179/1179 PASS**.
- Total: **1224/1224 PASS**.

An initial full-suite run encountered one known intermittent timeout in an unrelated allocation concurrency test. Its 40 parameterized cases passed in isolation, and the complete suite then passed.

## 11. Build

`dotnet build FinancialManagement.sln`: **PASS — 0 warnings / 0 errors**.

## 12. Foundation

`scripts\verify-foundation.cmd`: **PASS** with process-local `DOTNET_PROCESSOR_COUNT=2` to reduce xUnit collection contention.

Two earlier Foundation attempts encountered unrelated historical concurrency timeouts. No source or test configuration was modified; the final unchanged script execution completed successfully.

## 13. Migrations

Only migrations `001` through `004` are present. No migration `005` exists.

## 14. Schema drift

None. EF reports: `No changes have been made to the model since the last migration.`

## 15. Vulnerabilities

None reported for `FinancialManagement.Infrastructure`, including transitive packages.

## 16. ProgramData touched

**YES — validation requirement not met.**

Before the final Foundation run, the production database snapshot was:

- last write UTC: `2026-09-11T23:17:33.3515795Z`
- SHA-256: `7893C2ABCCFAF5FA699CFCD93C74C1C69657D78AD095C122656EE471AC5D70AD`

After Foundation it was:

- last write UTC: `2026-09-11T23:32:30.3115584Z`
- SHA-256: `562DAB1BC5FCD3A454E2C3490A1932DE370C1AE8051E463F08E5BFB59ABADACB`

No direct ProgramData write or application launch was performed for this implementation. No Tresoria process remained active when investigated. The source of the concurrent write was not established, so the database was deliberately left untouched rather than restored or altered.

## 17. Git diff check

`git diff --check`: **PASS**.

## 18. Git status

Tracked modifications are limited to the two Login window files. The new WPF regression test and this report are untracked. All pre-existing untracked reports remain preserved. Nothing is staged.

## 19. Manual visual acceptance required

**YES.** Native Windows application automation was unavailable on this host. Real WPF construction and control behavior are covered automatically, but final human UAT should confirm visual polish and real credential flow.

## 20. FINAL RESULT

**PARTIAL**

The Login password visibility feature, security constraints, automated behavior, build, full suite, Foundation, migrations, schema drift, vulnerability scan, and Git checks pass. Final PASS cannot be declared because ProgramData changed during validation and manual visual acceptance remains required.
