# Dashboard V2 — Post-login runtime crash report

Date: 2026-09-10

Branch: `feature/dashboard-v2`

HEAD: `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`

## 1. Exact root cause

The populated VISUAL-UAT database caused `DueAlertsView` to materialize its due-alert item template. The two inline `Run.Text` bindings used their WPF default mode. For `Run.Text`, that mode attempted source updates, but `DueAlertItemViewModel.Reference` and `DueDate` are intentionally read-only properties. WPF rejected the first binding (`Reference`) during template/layout materialization.

The almost-empty database did not create alert rows, so this template path had previously remained dormant. The data is valid; it exposed an undocumented UI binding assumption.

## 2. Exception type and message

Outer exception:

`System.Windows.Markup.XamlParseException: Une liaison TwoWay ou OneWayToSource ne peut pas fonctionner sur la propriété en lecture seule 'Reference' de type 'FinancialManagement.Desktop.ViewModels.DueAlertItemViewModel'.`

Inner exception:

`System.InvalidOperationException` with the same message.

The relevant stack begins at `MS.Internal.Data.PropertyPathWorker.CheckReadOnly`, continues through `ClrBindingWorker.AttachDataItem`, `StyleHelper.GetInstanceValue`, `FrameworkTemplate.LoadTemplateXaml`, and the WPF layout/render pass.

## 3. Exact source line

`src/FinancialManagement.Desktop/Views/DueAlertsView.xaml:54` — the inline bindings for `Reference` and `DueDate` in `DueAlertItemTemplate`.

Optimized BAML reported line/position `0/0`; the source line was established by the isolated WPF regression and a negative-control run that restored the original binding and reproduced the identical exception.

## 4. First failing startup stage

Stage **I: first WPF binding/render pass**, when non-empty due-alert rows were generated.

Stages A–H reached successfully: authentication, `MainWindow`, `ShellViewModel`, default Dashboard route, `DashboardViewModel`, production Dashboard reads, `DashboardView`, and chart-series initialization. The failing binding was not in LiveCharts.

## 5. Authentication

Authentication succeeded. Before the fix, Windows recorded a managed CLR failure after login (`0xe0434352`, followed by unhandled callback termination `0xc000041d`). The application did not intentionally call `Shutdown` for this path.

After the fix, a manual Mohamed login transitioned from the login window to the responsive main window titled `Gestion financière`; the process remained alive and Windows recorded zero matching Application errors after launch.

## 6. Dashboard backend reads

PASS against the existing real database for Aujourd'hui, Ce mois, and Cette année. The post-fix read-only audit again returned the seeded totals and expected point counts.

## 7. LiveCharts involvement

**NO.** The post-fix real-data shell produced 3 activity series and 2 cash-flow series. The original exception is a WPF binding-mode failure in the due-alert template.

## 8. WPF binding involvement

**YES.** This is the direct cause.

## 9. Dataset validity

PASS. Post-login read-only audit:

- `PRAGMA quick_check = ok`
- `PRAGMA integrity_check = ok`
- 5 clients, 5 suppliers, 36 sales, 27 purchases, 18 expenses, 39 customer payments, 28 supplier payments
- 3 customer alerts and 3 supplier alerts
- Dashboard Today/Month/Year and Reports comparison/evolution production reads PASS

## 10. Files changed by this targeted fix

- `src/FinancialManagement.Desktop/Views/DueAlertsView.xaml`
- `tests/FinancialManagement.Desktop.Tests/FinancialManagement.Desktop.Tests.csproj`
- `tests/FinancialManagement.Desktop.Tests/DueAlertsViewBindingTests.cs`
- `FinancialManagement.sln`
- `DASHBOARD-V2-RUNTIME-CRASH-REPORT.md`

All other listed modifications/untracked Dashboard and Reports files predated this targeted fix and were preserved.

## 11. Exact fix

Both read-only inline alert fields now declare `Mode=OneWay`:

- `Reference`
- `DueDate`

No exception is swallowed, no alert or chart is hidden, and the ViewModel properties remain read-only.

## 12. Regression test

Added a Windows/WPF STA regression that initializes the real `App.xaml` resources, renders `DueAlertsView` with non-empty UAT-like customer and supplier alert rows, performs layout, and captures `DispatcherUnhandledException`.

Negative control: with the original bindings restored temporarily, the test failed with the exact `XamlParseException` for read-only `Reference`. After restoring `Mode=OneWay`, it passed 1/1.

## 13. Targeted results

- New WPF binding regression: 1/1 PASS
- Dashboard-related tests: 58/58 PASS
- Reports tests: 6/6 PASS
- Authorization tests: 15/15 PASS
- Isolated concurrency-test recheck: 1/1 PASS
- Real-data post-login shell/navigation harness: PASS, zero dispatcher exceptions

## 14. Full suite

**1108/1108 PASS** total:

- Existing permanent suite: 1107/1107 PASS (3 min 38 s independent run)
- New Desktop/WPF regression: 1/1 PASS

The first `verify-foundation.cmd` invocation subsequently hit one transient failure in the pre-existing concurrent-employee-creation test (1106/1107). That test passed immediately in isolation, and the complete foundation script was rerun successfully with 1107/1107 existing tests plus the new 1/1 WPF test.

## 15. Build

`dotnet build FinancialManagement.sln --no-restore`: PASS — 0 warnings, 0 errors.

## 16. Foundation

`scripts\verify-foundation.cmd`: PASS on the complete rerun.

## 17. Migrations

Unchanged; exactly:

1. `001_InitialSchema`
2. `002_AllowAnonymousSalePayments`
3. `003_TreasuryOpeningBalanceSingleton`
4. `004_EnforceFinancialIntegerStorageClass`

No migration 005.

## 18. Schema drift

NONE. `dotnet ef migrations has-pending-model-changes` reported: `No changes have been made to the model since the last migration.`

## 19. ProgramData financial rows changed

**NO.** The diagnostic/render harness used production read paths only. A post-login read-only audit reconfirmed the exact VISUAL-UAT row counts and both SQLite integrity checks.

Protected directories and ACLs were not touched.

## 20. Manual runtime login proof

PASS. The real Desktop executable was launched, Mohamed logged in manually, and the process transitioned to the responsive `Gestion financière` main window. It remained alive with no matching Windows Application error after launch.

## 21. Dashboard real-data render proof

PASS. The same compiled `App.xaml`, `MainWindow`, `ShellViewModel`, Dashboard view, services, and existing VISUAL-UAT database were exercised by the external post-login harness. `ContentRendered` completed; Ce mois had data; activity series count was 3 and cash-flow series count was 2; dispatcher exception count was 0.

## 22. Reports navigation proof

PASS. The real-data shell completed Dashboard → Reports → Dashboard. Reports returned `HasSynthesisData=True`, both navigation calls returned `True`, and the final Dashboard remained populated with all chart series.

## 23. Git status and safety

- Branch remains `feature/dashboard-v2`
- HEAD remains `fea6f90da6b9c7e6ada3ef71d68dcc57a2edb4ba`
- `git diff --check`: PASS
- No `git add`, commit, push, merge, reset, rebase, or clean
- Existing uncommitted Dashboard/Reports work preserved
- No known vulnerable packages in any solution project

## 24. Final result

**PASS**
