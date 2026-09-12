[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Capture', 'Verify')]
    [string] $Mode,

    [Parameter(Mandatory = $true)]
    [string] $SnapshotPath,

    [string] $DatabasePath
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($DatabasePath)) {
    $commonApplicationData = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::CommonApplicationData)
    $DatabasePath = [IO.Path]::Combine(
        $commonApplicationData,
        'FinancialManagement',
        'Data',
        'FinancialManagement.db')
}

$DatabasePath = [IO.Path]::GetFullPath($DatabasePath)
$SnapshotPath = [IO.Path]::GetFullPath($SnapshotPath)
$defaultProductionPath = [IO.Path]::GetFullPath([IO.Path]::Combine(
    [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData),
    'FinancialManagement',
    'Data',
    'FinancialManagement.db'))

function Get-FileSha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [IO.File]::Open(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try {
            return (($algorithm.ComputeHash($stream) | ForEach-Object {
                $_.ToString('X2')
            }) -join '')
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
}

function Get-FileState {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Path
    )

    if (-not [IO.File]::Exists($Path)) {
        return [ordered]@{
            Name = $Name
            Path = $Path
            Exists = $false
            Length = $null
            LastWriteTimeUtcTicks = $null
            Sha256 = $null
        }
    }

    $before = [IO.FileInfo]::new($Path)
    $before.Refresh()
    $hash = Get-FileSha256 -Path $Path
    $after = [IO.FileInfo]::new($Path)
    $after.Refresh()

    if (-not $after.Exists -or
        $before.Length -ne $after.Length -or
        $before.LastWriteTimeUtc.Ticks -ne $after.LastWriteTimeUtc.Ticks) {
        throw "Database file changed while it was being fingerprinted: $Path"
    }

    return [ordered]@{
        Name = $Name
        Path = $Path
        Exists = $true
        Length = $after.Length
        LastWriteTimeUtcTicks = $after.LastWriteTimeUtc.Ticks
        Sha256 = $hash
    }
}

function Assert-ProductionApplicationIsStopped {
    if (-not [string]::Equals(
        $DatabasePath,
        $defaultProductionPath,
        [StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    $processes = @(Get-CimInstance Win32_Process | Where-Object {
        $_.Name -match '^(FinancialManagement\.Desktop|Tresoria)(\.exe)?$' -or
        $_.CommandLine -match 'FinancialManagement\.Desktop\.(dll|exe)(?:"|\s|$)'
    })

    if ($processes.Count -gt 0) {
        $details = $processes | ForEach-Object {
            '{0} (PID {1})' -f $_.Name, $_.ProcessId
        }
        throw ('Refusing to fingerprint ProgramData while the production application is running: ' +
            ($details -join ', '))
    }
}

function Get-DatabaseState {
    Assert-ProductionApplicationIsStopped
    return @(
        Get-FileState -Name 'Database' -Path $DatabasePath
        Get-FileState -Name 'Wal' -Path ($DatabasePath + '-wal')
        Get-FileState -Name 'Shm' -Path ($DatabasePath + '-shm')
    )
}

try {
    if ($Mode -eq 'Capture') {
        $snapshot = [ordered]@{
            Version = 1
            DatabasePath = $DatabasePath
            CapturedAtUtc = [DateTime]::UtcNow.ToString('O')
            Files = @(Get-DatabaseState)
        }

        $snapshotDirectory = [IO.Path]::GetDirectoryName($SnapshotPath)
        if (-not [string]::IsNullOrWhiteSpace($snapshotDirectory)) {
            [IO.Directory]::CreateDirectory($snapshotDirectory) | Out-Null
        }
        [IO.File]::WriteAllText(
            $SnapshotPath,
            ($snapshot | ConvertTo-Json -Depth 5),
            [Text.UTF8Encoding]::new($false))
        Write-Host "[FOUNDATION SAFETY] Captured DB/WAL/SHM filesystem fingerprints."
        exit 0
    }

    if (-not [IO.File]::Exists($SnapshotPath)) {
        throw "Foundation safety snapshot does not exist: $SnapshotPath"
    }

    $before = Get-Content -LiteralPath $SnapshotPath -Raw | ConvertFrom-Json
    if ($before.Version -ne 1) {
        throw "Unsupported foundation safety snapshot version: $($before.Version)"
    }
    if (-not [string]::Equals(
        [IO.Path]::GetFullPath([string]$before.DatabasePath),
        $DatabasePath,
        [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Foundation safety snapshot targets a different database path.'
    }

    $after = @(Get-DatabaseState)
    $changes = [Collections.Generic.List[string]]::new()
    foreach ($current in $after) {
        $original = @($before.Files | Where-Object { $_.Name -eq $current.Name })
        if ($original.Count -ne 1) {
            $changes.Add("$($current.Name): missing or duplicate baseline state")
            continue
        }

        foreach ($property in @('Exists', 'Length', 'LastWriteTimeUtcTicks', 'Sha256')) {
            $oldValue = $original[0].$property
            $newValue = $current.$property
            if ([string]$oldValue -cne [string]$newValue) {
                $changes.Add("$($current.Name).${property}: '$oldValue' -> '$newValue'")
            }
        }
    }

    if ($changes.Count -gt 0) {
        Write-Error ("[FOUNDATION SAFETY] Production DB filesystem state changed:`n - " +
            ($changes -join "`n - "))
        exit 1
    }

    Write-Host '[FOUNDATION SAFETY] DB/WAL/SHM remained unchanged.'
    exit 0
}
catch {
    Write-Error ("[FOUNDATION SAFETY] " + $_.Exception.Message)
    exit 2
}
