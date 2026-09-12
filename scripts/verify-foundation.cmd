@echo off
setlocal

cd /d "%~dp0.."
if errorlevel 1 exit /b %errorlevel%

set "foundation_snapshot=%TEMP%\FinancialManagement.Foundation.%RANDOM%.%RANDOM%.json"

echo [0/8] ProgramData DB/WAL/SHM safety capture
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "scripts\verify-programdata-unchanged.ps1" -Mode Capture -SnapshotPath "%foundation_snapshot%"
if errorlevel 1 exit /b %errorlevel%

echo [1/8] dotnet tool restore
dotnet tool restore
if errorlevel 1 goto :foundation_failed

echo [2/8] dotnet restore
dotnet restore
if errorlevel 1 goto :foundation_failed

echo [3/8] dotnet build FinancialManagement.sln
dotnet build FinancialManagement.sln
if errorlevel 1 goto :foundation_failed

echo [4/8] dotnet test FinancialManagement.sln
dotnet test FinancialManagement.sln
if errorlevel 1 goto :foundation_failed

echo [5/8] dotnet ef migrations list --project src/FinancialManagement.Infrastructure
dotnet ef migrations list --project src/FinancialManagement.Infrastructure
if errorlevel 1 goto :foundation_failed

echo [6/8] dotnet ef migrations has-pending-model-changes --project src/FinancialManagement.Infrastructure
dotnet ef migrations has-pending-model-changes --project src/FinancialManagement.Infrastructure
if errorlevel 1 goto :foundation_failed

echo [7/8] dotnet list src/FinancialManagement.Infrastructure/FinancialManagement.Infrastructure.csproj package --vulnerable --include-transitive
dotnet list src/FinancialManagement.Infrastructure/FinancialManagement.Infrastructure.csproj package --vulnerable --include-transitive
if errorlevel 1 goto :foundation_failed

set "foundation_exit=0"
goto :verify_programdata

:foundation_failed
set "foundation_exit=1"

:verify_programdata
echo [8/8] ProgramData DB/WAL/SHM safety verification
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "scripts\verify-programdata-unchanged.ps1" -Mode Verify -SnapshotPath "%foundation_snapshot%"
if errorlevel 1 (
    del /q "%foundation_snapshot%" >nul 2>&1
    exit /b 1
)

del /q "%foundation_snapshot%" >nul 2>&1
if not "%foundation_exit%"=="0" exit /b %foundation_exit%

echo Verification succeeded.
exit /b 0
