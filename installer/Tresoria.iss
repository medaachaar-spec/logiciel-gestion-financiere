; Tresoria x64 deployment. Compile through scripts/build-installer.ps1.
#ifndef PublishDir
  #error PublishDir must point to a self-contained win-x64 dotnet publish output.
#endif
#ifndef InstallerOutputDir
  #error InstallerOutputDir must point to the installer output directory.
#endif

[Setup]
AppId={{D6F12654-629B-4FCA-A08A-1D707D8D2C3B}
AppName=Tresoria
AppVersion=1.0.0
AppPublisher=Tresoria
DefaultDirName={commonpf64}\Tresoria
DisableDirPage=yes
DefaultGroupName=Tresoria
UninstallDisplayIcon={app}\FinancialManagement.Desktop.exe
SetupIconFile=..\src\FinancialManagement.Desktop\Resources\Branding\TresoriaAppIcon.ico
OutputDir={#InstallerOutputDir}
OutputBaseFilename=Tresoria-Setup-1.0.0-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UsePreviousAppDir=no
; ProgramData is deliberately not an Inno-managed [Files] or [Dirs] target.
; Updates and uninstall therefore leave the database and backups untouched.

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; Flags: unchecked

[Files]
Source: "..\scripts\TresoriaAcl.ps1"; Flags: dontcopy noencryption
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.db,*.db-wal,*.db-shm,*.db-journal"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\TresoriaAcl.ps1"; DestDir: "{app}\Security"; Flags: ignoreversion

[Icons]
Name: "{group}\Tresoria"; Filename: "{app}\FinancialManagement.Desktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\FinancialManagement.Desktop.exe"
Name: "{commondesktop}\Tresoria"; Filename: "{app}\FinancialManagement.Desktop.exe"; WorkingDir: "{app}"; IconFilename: "{app}\FinancialManagement.Desktop.exe"; Tasks: desktopicon

[Code]
var
  OperatorPage: TInputQueryWizardPage;
  AuthorizedAccount: String;

function IsSafeAccountText(const Value: String): Boolean;
begin
  Result := (Trim(Value) <> '') and (Pos('"', Value) = 0) and
    (Pos(#13, Value) = 0) and (Pos(#10, Value) = 0);
end;

function RunAclTool(const Mode, Account: String; var Detail: String): Boolean;
var
  ScriptPath, Parameters: String;
  ExitCode: Integer;
begin
  ScriptPath := ExpandConstant('{tmp}\TresoriaAcl.ps1');
  Parameters := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ' +
    AddQuotes(ScriptPath) + ' -Mode ' + Mode + ' -OperatorAccount ' +
    AddQuotes(Account);
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      Parameters, '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
  begin
    Detail := 'PowerShell ne peut pas démarrer : ' + SysErrorMessage(ExitCode);
    Result := False;
  end
  else
  begin
    Detail := 'Code de sortie : ' + IntToStr(ExitCode);
    Result := ExitCode = 0;
  end;
end;

procedure InitializeWizard;
begin
  ExtractTemporaryFile('TresoriaAcl.ps1');
  AuthorizedAccount := Trim(ExpandConstant('{param:OPERATORACCOUNT|}'));
  OperatorPage := CreateInputQueryPage(wpSelectProgramGroup,
    'Compte Windows autorisé',
    'Sélectionnez le compte Windows individuel qui utilisera Tresoria sans élévation.',
    'Indiquez COMPUTER\utilisateur ou DOMAINE\utilisateur. Le compte doit déjà exister.');
  OperatorPage.Add('Compte Windows :', False);
  OperatorPage.Values[0] := AuthorizedAccount;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Detail: String;
begin
  Result := True;
  if CurPageID <> OperatorPage.ID then
    Exit;
  Detail := '';
  AuthorizedAccount := Trim(OperatorPage.Values[0]);
  if not IsSafeAccountText(AuthorizedAccount) or
      not RunAclTool('Validate', AuthorizedAccount, Detail) then
  begin
    MsgBox('Le compte Windows autorisé est invalide. Indiquez un compte ' +
      'utilisateur individuel existant, qualifié par le nom de l''ordinateur ' +
      'ou du domaine. ' + Detail, mbError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Detail: String;
begin
  Detail := '';
  if CurStep = ssInstall then
  begin
    if (AuthorizedAccount = '') or
        not RunAclTool('Validate', AuthorizedAccount, Detail) or
        not RunAclTool('Provision', AuthorizedAccount, Detail) then
      RaiseException('Installation arrêtée : les permissions de ' +
        'ProgramData\FinancialManagement ne peuvent pas être sécurisées. ' + Detail);
  end;
  if CurStep = ssPostInstall then
  begin
    if not RunAclTool('Verify', AuthorizedAccount, Detail) then
      RaiseException('Installation arrêtée : la vérification des permissions ' +
        'de ProgramData\FinancialManagement a échoué. ' + Detail);
  end;
end;
