[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Validate', 'Provision', 'Verify', 'SelfTest')]
    [string] $Mode,

    [Parameter(Mandatory = $true)]
    [string] $OperatorAccount
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# SID_NAME_USE = 1 is a user. Do not accept a group as the operator account.
$accountLookup = @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace TresoriaAcl
{
    public static class NativeAccounts
    {
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool LookupAccountName(
            string systemName, string accountName, byte[] sid, ref int sidSize,
            StringBuilder domainName, ref int domainSize, out int use);

        public static SecurityIdentifier ResolveUserSid(string account)
        {
            int sidSize = 0;
            int domainSize = 0;
            int use;
            LookupAccountName(null, account, null, ref sidSize, null, ref domainSize, out use);
            if (Marshal.GetLastWin32Error() != 122 || sidSize <= 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "The Windows account could not be resolved.");

            byte[] sid = new byte[sidSize];
            StringBuilder domain = new StringBuilder(domainSize);
            if (!LookupAccountName(null, account, sid, ref sidSize, domain,
                ref domainSize, out use))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "The Windows account could not be resolved.");
            if (use != 1)
                throw new ArgumentException(
                    "The authorized operator must be an individual Windows user account.");

            return new SecurityIdentifier(sid, 0);
        }
    }
}
'@

function Get-DataRoot {
    $common = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::CommonApplicationData)
    if ([string]::IsNullOrWhiteSpace($common)) {
        throw 'The Windows ProgramData location is unavailable.'
    }
    return [IO.Path]::GetFullPath([IO.Path]::Combine($common, 'FinancialManagement'))
}

function Get-Descendants {
    param([Parameter(Mandatory = $true)][string] $Directory)

    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to traverse a reparse point in production data: $($entry.FullName)"
        }
        $entry
        if ($entry.PSIsContainer) {
            Get-Descendants -Directory $entry.FullName
        }
    }
}

function Remove-ExplicitRules {
    param([Parameter(Mandatory = $true)] $Acl)

    foreach ($rule in @($Acl.Access | Where-Object { -not $_.IsInherited })) {
        $Acl.RemoveAccessRuleSpecific($rule)
    }
}

function Set-SecureRoot {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $OperatorSid,
        [Parameter(Mandatory = $true)] $AdminSid,
        [Parameter(Mandatory = $true)] $SystemSid,
        [Parameter(Mandatory = $true)] $OwnerSid
    )

    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    Remove-ExplicitRules -Acl $acl
    $acl.SetOwner($OwnerSid)

    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor
        [Security.AccessControl.InheritanceFlags]::ObjectInherit
    $propagation = [Security.AccessControl.PropagationFlags]::None
    $allow = [Security.AccessControl.AccessControlType]::Allow
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $SystemSid, [Security.AccessControl.FileSystemRights]::FullControl,
        $inheritance, $propagation, $allow))
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $AdminSid, [Security.AccessControl.FileSystemRights]::FullControl,
        $inheritance, $propagation, $allow))
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
        $OperatorSid, [Security.AccessControl.FileSystemRights]::Modify,
        $inheritance, $propagation, $allow))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Set-InheritedChild {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $OwnerSid
    )

    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($false, $false)
    Remove-ExplicitRules -Acl $acl
    # Existing objects may have been owned by an unrelated account, which
    # could otherwise rewrite the DACL despite the new access rules.
    $acl.SetOwner($OwnerSid)
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Assert-SecureAcl {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)] $OperatorSid,
        [Parameter(Mandatory = $true)] $AdminSid,
        [Parameter(Mandatory = $true)] $SystemSid,
        [Parameter(Mandatory = $true)][bool] $IsRoot
    )

    $acl = Get-Acl -LiteralPath $Path
    if ($acl.AreAccessRulesProtected -ne $IsRoot) {
        throw "Unexpected ACL inheritance state: $Path"
    }

    $ownerSid = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($ownerSid -notin @($OperatorSid.Value, $AdminSid.Value, $SystemSid.Value)) {
        throw "Unauthorized owner on production data: $Path ($ownerSid)"
    }

    $rules = @($acl.Access)
    if ($rules.Count -ne 3) {
        throw "Expected exactly three access rules on $Path; found $($rules.Count)."
    }

    $expected = @{
        $SystemSid.Value = [Security.AccessControl.FileSystemRights]::FullControl
        $AdminSid.Value = [Security.AccessControl.FileSystemRights]::FullControl
        $OperatorSid.Value = ([Security.AccessControl.FileSystemRights]::Modify -bor
            [Security.AccessControl.FileSystemRights]::Synchronize)
    }
    $seen = @{}
    foreach ($rule in $rules) {
        $sid = $rule.IdentityReference.Translate(
            [Security.Principal.SecurityIdentifier]).Value
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            -not $expected.ContainsKey($sid) -or $seen.ContainsKey($sid) -or
            $rule.IsInherited -eq $IsRoot) {
            throw "Unexpected access rule on production data: $Path ($sid)"
        }
        $actualRights = [int]$rule.FileSystemRights
        $expectedRights = [int]$expected[$sid]
        if (($actualRights -band $expectedRights) -ne $expectedRights -or
            ($actualRights -band (-bnot $expectedRights)) -ne 0) {
            throw "Unexpected access rights on production data: $Path ($sid)"
        }
        $seen[$sid] = $true
    }
}

function Assert-ChildInheritance {
    param(
        [Parameter(Mandatory = $true)][string] $Parent,
        [Parameter(Mandatory = $true)] $OperatorSid,
        [Parameter(Mandatory = $true)] $AdminSid,
        [Parameter(Mandatory = $true)] $SystemSid
    )

    $directory = [IO.Path]::Combine($Parent, '.sec002-acl-check-' + [Guid]::NewGuid().ToString('N'))
    $file = [IO.Path]::Combine($directory, 'inheritance-check.txt')
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    try {
        Assert-SecureAcl -Path $directory -OperatorSid $OperatorSid -AdminSid $AdminSid -SystemSid $SystemSid -IsRoot $false
        [IO.File]::WriteAllText($file, 'SEC-002 inheritance check')
        Assert-SecureAcl -Path $file -OperatorSid $OperatorSid -AdminSid $AdminSid -SystemSid $SystemSid -IsRoot $false
    }
    finally {
        if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
        if ([IO.Directory]::Exists($directory)) { [IO.Directory]::Delete($directory, $false) }
    }
}

try {
    if ($OperatorAccount -notmatch '^[^\\/:*?"<>|\r\n]+\\[^\\/:*?"<>|\r\n]+$') {
        throw 'Enter a qualified individual account such as COMPUTER\user or DOMAIN\user.'
    }
    Add-Type -TypeDefinition $accountLookup -Language CSharp
    $operatorSid = [TresoriaAcl.NativeAccounts]::ResolveUserSid($OperatorAccount)
    $adminSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    $systemSid = [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
    Write-Host "Authorized operator: $OperatorAccount ($($operatorSid.Value))"
    if ($Mode -eq 'Validate') { exit 0 }

    $root = if ($Mode -eq 'SelfTest') {
        [IO.Path]::Combine([IO.Path]::GetTempPath(),
            'TresoriaAclSelfTest-' + [Guid]::NewGuid().ToString('N'))
    }
    else {
        Get-DataRoot
    }
    $data = [IO.Path]::Combine($root, 'Data')
    $backups = [IO.Path]::Combine($root, 'Backups')
    if ($Mode -in @('Provision', 'SelfTest')) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        if ($Mode -eq 'Provision' -and
            -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            throw 'ACL provisioning requires an elevated administrator installer.'
        }
        $ownerSid = if ($Mode -eq 'SelfTest') { $operatorSid } else { $adminSid }
        if ([IO.Directory]::Exists($root) -and
            (([IO.File]::GetAttributes($root) -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "Refusing to provision a reparse-point data root: $root"
        }
        if (-not [IO.Directory]::Exists($root)) {
            [IO.Directory]::CreateDirectory($root) | Out-Null
        }
        if ($Mode -eq 'SelfTest') {
            [IO.Directory]::CreateDirectory($data) | Out-Null
            [IO.Directory]::CreateDirectory($backups) | Out-Null
            $mockDatabase = [IO.Path]::Combine($data, 'FinancialManagement.db')
            [IO.File]::WriteAllText($mockDatabase, 'SEC-002 synthetic ACL test only')
            foreach ($case in @(
                @{ Path = $data; Sid = 'S-1-5-11' },
                @{ Path = $backups; Sid = 'S-1-5-32-545' },
                @{ Path = $mockDatabase; Sid = 'S-1-1-0' })) {
                $testAcl = Get-Acl -LiteralPath $case.Path
                $testAcl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
                    [Security.Principal.SecurityIdentifier]::new($case.Sid),
                    [Security.AccessControl.FileSystemRights]::Read,
                    [Security.AccessControl.AccessControlType]::Allow))
                Set-Acl -LiteralPath $case.Path -AclObject $testAcl
            }
        }
        # Inspect before changing any ACL so junctions cannot redirect the walk.
        $existing = @(Get-Descendants -Directory $root)
        Set-SecureRoot -Path $root -OperatorSid $operatorSid -AdminSid $adminSid -SystemSid $systemSid -OwnerSid $ownerSid
        foreach ($entry in $existing) {
            Set-InheritedChild -Path $entry.FullName -OwnerSid $ownerSid
        }
        [IO.Directory]::CreateDirectory($data) | Out-Null
        [IO.Directory]::CreateDirectory($backups) | Out-Null
    }
    elseif (-not [IO.Directory]::Exists($root)) {
        throw "Tresoria production data directory does not exist: $root"
    }

    Assert-SecureAcl -Path $root -OperatorSid $operatorSid -AdminSid $adminSid -SystemSid $systemSid -IsRoot $true
    foreach ($entry in @(Get-Descendants -Directory $root)) {
        Assert-SecureAcl -Path $entry.FullName -OperatorSid $operatorSid -AdminSid $adminSid -SystemSid $systemSid -IsRoot $false
    }
    Assert-ChildInheritance -Parent $root -OperatorSid $operatorSid -AdminSid $adminSid -SystemSid $systemSid
    Assert-ChildInheritance -Parent $data -OperatorSid $operatorSid -AdminSid $adminSid -SystemSid $systemSid
    Assert-ChildInheritance -Parent $backups -OperatorSid $operatorSid -AdminSid $adminSid -SystemSid $systemSid
    Write-Host "Tresoria ACL verification succeeded: $root"
    if ($Mode -eq 'SelfTest') {
        [IO.File]::Delete($mockDatabase)
        [IO.Directory]::Delete($data, $false)
        [IO.Directory]::Delete($backups, $false)
        [IO.Directory]::Delete($root, $false)
    }
    exit 0
}
catch {
    Write-Error ("Tresoria ACL $Mode failed: " + $_.Exception.Message)
    exit 1
}
