@echo off
setlocal

cd /d "%~dp0.."
if errorlevel 1 exit /b %errorlevel%

echo [1/7] dotnet tool restore
dotnet tool restore
if errorlevel 1 exit /b %errorlevel%

echo [2/7] dotnet restore
dotnet restore
if errorlevel 1 exit /b %errorlevel%

echo [3/7] dotnet build FinancialManagement.sln
dotnet build FinancialManagement.sln
if errorlevel 1 exit /b %errorlevel%

echo [4/7] dotnet test FinancialManagement.sln
dotnet test FinancialManagement.sln
if errorlevel 1 exit /b %errorlevel%

echo [5/7] dotnet ef migrations list --project src/FinancialManagement.Infrastructure
dotnet ef migrations list --project src/FinancialManagement.Infrastructure
if errorlevel 1 exit /b %errorlevel%

echo [6/7] dotnet ef migrations has-pending-model-changes --project src/FinancialManagement.Infrastructure
dotnet ef migrations has-pending-model-changes --project src/FinancialManagement.Infrastructure
if errorlevel 1 exit /b %errorlevel%

echo [7/7] dotnet list src/FinancialManagement.Infrastructure/FinancialManagement.Infrastructure.csproj package --vulnerable --include-transitive
dotnet list src/FinancialManagement.Infrastructure/FinancialManagement.Infrastructure.csproj package --vulnerable --include-transitive
if errorlevel 1 exit /b %errorlevel%

echo Verification succeeded.
exit /b 0
