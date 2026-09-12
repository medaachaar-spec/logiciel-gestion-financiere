[CmdletBinding()]
param(
    [string] $InnoCompiler
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath([IO.Path]::Combine($PSScriptRoot, '..'))
$project = [IO.Path]::Combine(
    $repository, 'src', 'FinancialManagement.Desktop', 'FinancialManagement.Desktop.csproj')
$publish = [IO.Path]::Combine($repository, 'artifacts', 'publish', 'win-x64',
    [Guid]::NewGuid().ToString('N'))
$output = [IO.Path]::Combine($repository, 'artifacts', 'installer')
$script = [IO.Path]::Combine($repository, 'installer', 'Tresoria.iss')

[IO.Directory]::CreateDirectory($publish) | Out-Null
[IO.Directory]::CreateDirectory($output) | Out-Null

& dotnet publish $project --configuration Release --runtime win-x64 `
    --self-contained true --output $publish `
    -p:PublishSingleFile=false -p:UseAppHost=true
if ($LASTEXITCODE -ne 0) {
    throw "Self-contained win-x64 publish failed with exit code $LASTEXITCODE."
}

$executable = [IO.Path]::Combine($publish, 'FinancialManagement.Desktop.exe')
if (-not [IO.File]::Exists($executable)) {
    throw "The publish output does not contain $executable."
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) {
        $InnoCompiler = $command.Source
    }
    else {
        foreach ($candidate in @(
            'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
            'C:\Program Files\Inno Setup 6\ISCC.exe',
            'C:\Program Files (x86)\Inno Setup 7\ISCC.exe',
            'C:\Program Files\Inno Setup 7\ISCC.exe')) {
            if ([IO.File]::Exists($candidate)) {
                $InnoCompiler = $candidate
                break
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or
    -not [IO.File]::Exists($InnoCompiler)) {
    Write-Warning 'ISCC.exe is unavailable. The self-contained publish succeeded; installer compilation was skipped.'
    exit 2
}

& $InnoCompiler "/DPublishDir=$publish" "/DInstallerOutputDir=$output" $script
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}
Write-Host "Tresoria installer created in $output"
