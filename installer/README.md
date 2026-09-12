# Tresoria Windows installer (SEC-002)

The installer is an elevated, machine-wide Inno Setup package for Windows x64.
It installs the self-contained .NET 10 desktop publish output in
`%ProgramFiles%\Tresoria`. Normal use starts from the Start Menu or optional
Desktop shortcut and does not request elevation. The installer does not launch
the application from its elevated process.

The existing production paths are unchanged:

- `%ProgramData%\FinancialManagement\Data\FinancialManagement.db` (including
  SQLite `-wal` and `-shm` sidecars and restore staging files)
- `%ProgramData%\FinancialManagement\Backups` (local daily, migration and
  pre-restore backups, plus temporary backup validation files)

The installer secures `%ProgramData%\FinancialManagement` before copying app
binaries. It asks for a qualified, existing individual Windows account such as
`COMPUTER\operator` or `DOMAIN\operator`. This is deliberate: UAC may run the
installer under a different administrator account than the person who will use
Tresoria, so Setup must not infer the operator from its own process identity.
The account is resolved with Windows `LookupAccountName`, must have SID type
`SidTypeUser`, and is stored as a SID in the ACL. A group is rejected.

The protected root has exactly three inheritable allow entries: SYSTEM and
Administrators have Full Control, and the selected operator has Modify. The
root blocks inherited access rules from ProgramData. Existing descendants
have explicit access rules removed, inheritance re-enabled and ownership
assigned to Administrators; new files and directories inherit the secured
rules. The provisioning step rejects reparse points and fails installation if
any object cannot be secured or verified. There is no fallback grant for
Everyone, Authenticated Users or BUILTIN\Users.

The installer owns only files under Program Files. It neither installs files
into ProgramData through Inno Setup nor declares an uninstall deletion there.
Upgrades reapply the ACL and leave the database and backups in place. Uninstall
removes application binaries and shortcuts but preserves ProgramData. When
upgrading, select the same operator account unless access should be moved to
a different account; provisioning replaces the prior operator grant.

## Build

Install Inno Setup 6.3 or later separately, then run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\build-installer.ps1
```

The script publishes `FinancialManagement.Desktop` for `win-x64` with
`--self-contained true`, then compiles `installer\Tresoria.iss` with `ISCC.exe`.
Build outputs are ignored under `artifacts\`. If Inno Setup is absent, the
publish still runs and the script exits with code 2 without installing tools.
The installer should be smoke-tested on a disposable Windows x64 machine with
a non-admin operator account before distribution.

Interactive Setup requests the operator account in a wizard page. Silent Setup
requires a qualified account parameter, for example:

```powershell
Tresoria-Setup-1.0.0-win-x64.exe /VERYSILENT /OPERATORACCOUNT="COMPUTER\operator"
```

The account must already exist. Provisioning runs with the elevated installer;
the application itself has no administrator requirement.

## Verify after installation

From an elevated PowerShell prompt, run the installed verification tool:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "${env:ProgramFiles}\Tresoria\Security\TresoriaAcl.ps1" -Mode Verify -OperatorAccount "COMPUTER\operator"
```

It checks the root and every existing descendant, including any DB, WAL, SHM,
backup or staging file present. It requires exactly the three expected SIDs
and rights, rejects broad or additional ACEs, checks ownership, then creates
and removes only uniquely named test children under the root, Data and Backups
to prove inheritance. It never opens, changes or deletes the production DB.

Developers can exercise the ACL logic on a disposable tree under `%TEMP%`
without touching ProgramData:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts\TresoriaAcl.ps1 -Mode SelfTest -OperatorAccount "COMPUTER\operator"
```

External backups copied to a directory selected by a user are outside this
protected tree and follow the ACL of that destination. Operators should choose
a protected external destination.
