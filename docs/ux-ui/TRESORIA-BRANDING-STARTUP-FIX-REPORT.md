# TRESORIA BRANDING STARTUP FIX REPORT

## 1. Exact original exception that was masked

`LoginWindow.InitializeComponent()` threw:

- `System.Windows.Markup.XamlParseException` at the Window icon attribute;
- inner `System.IO.FileFormatException`: the image decoder could not decode the image;
- inner COM error `0x88982F60`: “L’image n’est pas reconnue.”

An actual WPF construction test reproduced the exception before the production resource fix.

## 2. Why LoginWindow.OnClosing threw NullReferenceException

The constructor called `InitializeComponent()` before assigning `_viewModel`. When XAML icon decoding failed, construction stopped with `_viewModel` still null. Application shutdown then called `OnClosing()`, which dereferenced `_viewModel.IsBusy` and masked the first XAML/resource exception.

## 3. Root cause

The derived multi-frame `TresoriaAppIcon.ico` used PNG-compressed icon frames. Pillow could read it and Windows could extract its icon, but the WPF/WIC decoder rejected it during Window XAML loading. The approved source PNG and sidebar PNG were valid and were not the cause.

## 4. Production fix

- `LoginWindow` now validates and assigns its required `LoginViewModel` before `InitializeComponent()`.
- `DataContext` uses the assigned field.
- `OnClosing()` remains safe for partial construction while preserving the normal `IsBusy` close cancellation.
- The ICO was regenerated from the unchanged approved circular PNG with WPF-compatible BMP/DIB frames.

## 5. ICO regeneration

YES. `TresoriaAppIcon.ico` was regenerated from `TresoriaAppIcon.png` only. It retains 16, 24, 32, 48, 64, 128, and 256-pixel frames. Final ICO SHA-256: `39CEEEDBB533AE96E44967AFD3C64270DA9DDCAA60798670DF0C84F38A5EF7E3`.

## 6. Branding assets preserved

YES.

- Circular PNG SHA-256 remains `5DCDB586AE9563B1D29775E1AC9F74490152A93E4F54E4637E06A05A23CF9B5E`.
- Sidebar PNG SHA-256 remains `21D8321FCBF4BBE42CA3BD6A5B27973FB6710D04C9AF069CEE7E5F079FBAD0EC`.
- No substitute or redesigned artwork was introduced.

## 7. Files changed

Branding implementation retained:

- `src/FinancialManagement.Desktop/FinancialManagement.Desktop.csproj`
- `src/FinancialManagement.Desktop/FirstRunWindow.xaml`
- `src/FinancialManagement.Desktop/InitializationWindow.xaml`
- `src/FinancialManagement.Desktop/LoginWindow.xaml`
- `src/FinancialManagement.Desktop/MainWindow.xaml`
- `src/FinancialManagement.Desktop/Services/BackupDialogService.cs`
- `src/FinancialManagement.Desktop/Resources/Branding/TresoriaAppIcon.png`
- `src/FinancialManagement.Desktop/Resources/Branding/TresoriaSidebarLogo.png`
- `src/FinancialManagement.Desktop/Resources/Branding/TresoriaAppIcon.ico`
- `tests/FinancialManagement.Tests/BrandingResourceTests.cs`

Startup-fix additions:

- `src/FinancialManagement.Desktop/LoginWindow.xaml.cs`
- `tests/FinancialManagement.Desktop.Tests/LoginWindowBrandingLifecycleTests.cs`

## 8. Tests added/updated

Three real WPF regression tests now prove:

- `LoginWindow` constructs and decodes both branding resources;
- null dependency rejection occurs before XAML initialization;
- normal close remains cancelled while the login ViewModel is busy.

The existing six structural branding cases continue to verify exact asset hashes, project packaging, common ICO sizes, titles, sidebar wiring, accessibility name, and portable paths.

## 9. Targeted results

- Structural branding: 6/6 PASS.
- WPF startup/lifecycle/resource decoding: 3/3 PASS.
- Combined targeted branding/startup: 9/9 PASS.

## 10. Full-suite result

1197/1197 PASS:

- Core: 1172/1172.
- Desktop: 25/25.

## 11. Build

PASS — 0 warnings / 0 errors.

## 12. Foundation

PARTIAL due to an unrelated intermittent concurrency timeout. The verifier restored and rebuilt successfully and passed 25/25 Desktop tests, but its core run stopped at 1171/1172 when `AllocationIntegrityTests.Concurrent_writers_serialize_validation_and_commit_valid_aggregates(customer: True, initial: True, mode: "source", compatible: False)` timed out. The immediately preceding full suite passed 1197/1197, and the allocation concurrency group had passed 40/40 in isolation. No concurrency code or test was weakened.

Foundation steps 5–7 were run independently afterward and passed.

## 13. Runtime smoke result

PASS.

- `dotnet run --no-build --project src/FinancialManagement.Desktop/FinancialManagement.Desktop.csproj` stayed alive.
- Process was responsive.
- Initial window title was `Connexion — Tresoria`.
- No “Démarrage impossible” dialog or unhandled exception occurred.
- Authentication completed normally; the window title changed to `Tresoria`, proving the shell opened and its branding resources loaded.
- The application exited normally through the window close path.

## 14. New Windows Event Viewer errors after final smoke

NO. No new `.NET Runtime` event 1026 or `Application Error` event 1000 referencing `FinancialManagement.Desktop` was recorded from the smoke start onward.

## 15. Migrations

PASS — exactly 001–004. No migration 005.

## 16. Schema drift

NONE. `dotnet ef migrations has-pending-model-changes` reports no model changes.

## 17. Vulnerabilities

NONE. Infrastructure and transitive dependencies report no known vulnerable packages.

## 18. ProgramData financial rows changed

NO. No financial action was invoked. A post-smoke read-only audit returned `quick_check=ok` and `integrity_check=ok`. The newest financial timestamp was `2026-09-11T15:05:58.3747526Z`, before the smoke start at `2026-09-11T16:18:11.0072788Z`; no sale, purchase, expense, payment, allocation, initial obligation, opening balance, or transfer was created or updated during the smoke.

## 19. Git diff --check

PASS.

## 20. Git status

- Branch: `feature/tresoria-branding-v1`.
- HEAD unchanged: `6a9d9ff8379f618c4fd349f0dc1c98aaf84fe80a`.
- Branding and startup-fix files remain unstaged and uncommitted.
- Existing untracked reports remain preserved.
- No add, commit, push, merge, rebase, reset, or clean was performed.

## 21. FINAL RESULT

**PASS.** The masked exception and underlying WPF ICO incompatibility are fixed, the approved branding is preserved, real startup/login/shell smoke passes, and all relevant automated validation is green. The only incomplete aggregate gate is the Foundation script’s unrelated intermittent concurrency test, reported above with a clean full-suite result.
