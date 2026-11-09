# Requires installer-compat.ps1. Keep Windows PowerShell 2 syntax.
function Assert-AutoCadTransactionIdle {
    if (@(Get-Process acad, accoreconsole -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'AutoCAD is running. Close it normally before changing or restoring registration.'
    }
}

function Get-AutoCadTransactionKeySnapshot {
    param([string]$Path)
    $records = @()
    if (Test-Path -LiteralPath $Path) {
        $items = @((Get-Item -LiteralPath $Path)) + @(Get-ChildItem -LiteralPath $Path -Recurse)
        foreach ($item in $items) {
            $values = @()
            foreach ($name in @($item.GetValueNames())) {
                $values += New-Object PSObject -Property @{
                    Name = $name
                    Kind = $item.GetValueKind($name).ToString()
                    Value = $item.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                }
            }
            $baseName = (Get-Item -LiteralPath $Path).Name
            $records += New-Object PSObject -Property @{ Relative = $item.Name.Substring($baseName.Length).TrimStart('\'); Values = $values }
        }
    }
    New-Object PSObject -Property @{ Path = $Path; Exists = (Test-Path -LiteralPath $Path); Records = $records }
}

function Get-AutoCadTransactionFinalPath {
    param([string]$Path)
    if (-not ('XyCadInstallTransaction.NativePath' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
namespace XyCadInstallTransaction {
 public static class NativePath {
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern SafeFileHandle CreateFile(string p, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
  static extern uint GetFinalPathNameByHandle(SafeFileHandle h, StringBuilder b, uint n, uint f);
  public static string Resolve(string p) {
   using (SafeFileHandle h = CreateFile(p, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero)) {
    if(h.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    StringBuilder b = new StringBuilder(32768);
    uint n = GetFinalPathNameByHandle(h, b, (uint)b.Capacity, 0);
    if(n == 0 || n >= b.Capacity) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    return b.ToString().TrimEnd('\\');
   }
  }
 }
}
'@
    }
    [XyCadInstallTransaction.NativePath]::Resolve($Path)
}

function Assert-AutoCadTransactionRegistrationPath {
    param([string]$Path)
    $root = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins')).TrimEnd('\')
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $product = -join @(0x5C0F,0x9633,0x43,0x41,0x44,0x8F6E,0x76D8,0x63D2,0x4EF6 | ForEach-Object { [char]$_ })
    if (-not (Split-Path -Parent $full).Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
        @("$product.bundle", 'XY-CAD-H.bundle') -notcontains (Split-Path -Leaf $full)) {
        throw "Unexpected product registration path: $full"
    }
    $full
}

function Start-AutoCadInstallTransaction {
    param([string]$SourceBundle, [string[]]$RegistrationPaths, [string[]]$ProfilePaths)
    Assert-AutoCadTransactionIdle
    $source = [IO.Path]::GetFullPath($SourceBundle).TrimEnd('\')
    Assert-SafeRuntimeDataDestination $source
    $sourceManifest = Get-RuntimeDataManifest $source
    $merged = @{}
    foreach ($relative in @($sourceManifest.Files.Keys)) { $merged[$relative] = $sourceManifest.Files[$relative] }
    $registrations = @()
    foreach ($path in @($RegistrationPaths | Select-Object -Unique)) {
        $full = Assert-AutoCadTransactionRegistrationPath $path
        if ($full.Equals($source, [StringComparison]::OrdinalIgnoreCase)) { throw 'Source cannot be a registration directory.' }
        $item = Get-Item -LiteralPath $full -Force -ErrorAction SilentlyContinue
        $manifest = $null
        if ($item) {
            $manifest = Get-RuntimeDataManifest $full
            foreach ($relative in @($manifest.Files.Keys)) {
                if ($merged.ContainsKey($relative) -and
                    ($merged[$relative].Sha256 -ne $manifest.Files[$relative].Sha256 -or
                     [long]$merged[$relative].Length -ne [long]$manifest.Files[$relative].Length)) {
                    throw "Runtime data conflict; no registration was changed: $relative"
                }
                $merged[$relative] = $manifest.Files[$relative]
            }
        }
        $registrations += New-Object PSObject -Property @{
            Path = $full; Exists = [bool]$item; Manifest = $manifest
            Backup = ($full + '.' + [Guid]::NewGuid().ToString('N') + '.backup')
            Suspended = $false
        }
    }
    $keys = @()
    foreach ($profile in @($ProfilePaths | Select-Object -Unique)) {
        if ($profile -notmatch '^HKCU:\\Software\\Autodesk\\AutoCAD\\R[0-9]+\.[0-9]+\\ACAD-[^\\]+:[0-9A-Fa-f]+$') {
            throw "Unexpected AutoCAD profile path: $profile"
        }
        foreach ($name in @('XiaoYangCADRadialMenu', 'XiaoYangCADRadialMenuStartup')) {
            $keys += Get-AutoCadTransactionKeySnapshot (Join-Path $profile ("Applications\" + $name))
        }
    }
    $evidence = Join-Path (Split-Path -Parent $source) ('install-recovery-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $evidence | Out-Null
    $sourceFiles = @()
    $fileIndex = 0
    foreach ($relative in @('PackageContents.xml', 'PackageContents.xml.all-versions', 'install-selection.json', 'Logs\install-report.json')) {
        $file = Join-Path $source $relative
        $exists = Test-Path -LiteralPath $file
        $backup = Join-Path $evidence ('source-file-' + $fileIndex)
        if ($exists) { Copy-Item -LiteralPath $file -Destination $backup -ErrorAction Stop }
        $sourceFiles += New-Object PSObject -Property @{ Path = $file; Exists = $exists; Backup = $backup }
        $fileIndex++
    }
    $transaction = New-Object PSObject -Property @{
        SourceBundle = $source; Registrations = $registrations; Registry = $keys
        Evidence = $evidence; State = 'Prepared'; SourceFiles = $sourceFiles
        SourceIdentity = (Get-AutoCadTransactionFinalPath $source)
    }
    $transaction | Export-Clixml -Path (Join-Path $evidence 'transaction.xml') -Depth 40
    $index = 0
    foreach ($entry in $registrations) {
        if (-not $entry.Exists) { continue }
        $snapshot = Join-Path $evidence ('runtime-' + $index)
        Copy-RuntimeData -FromBundle $entry.Path -ToBundle $snapshot
        Assert-RuntimeDataManifest -Bundle $snapshot -Expected $entry.Manifest -Exact
        Copy-RuntimeData -FromBundle $snapshot -ToBundle $source
        Assert-RuntimeDataManifest -Bundle $source -Expected $entry.Manifest
        $index++
    }
    Assert-RuntimeDataManifest -Bundle $source -Expected $sourceManifest
    $transaction
}

function Suspend-AutoCadRegistrations {
    param([object]$Transaction)
    Assert-AutoCadTransactionIdle
    foreach ($entry in @($Transaction.Registrations)) {
        [void](Assert-AutoCadTransactionRegistrationPath $entry.Path)
        $expectedParent = Split-Path -Parent $entry.Path
        if (-not (Split-Path -Parent ([IO.Path]::GetFullPath($entry.Backup))).Equals($expectedParent, [StringComparison]::OrdinalIgnoreCase) -or
            $entry.Backup -notmatch ('^' + [regex]::Escape($entry.Path) + '\.[0-9a-f]{32}\.backup$')) {
            throw 'Invalid registration backup path.'
        }
        if ($entry.Exists) {
            if (Test-Path -LiteralPath $entry.Backup) { throw "Backup already exists: $($entry.Backup)" }
            Assert-RuntimeDataManifest -Bundle $entry.Path -Expected $entry.Manifest -Exact
            Move-Item -LiteralPath $entry.Path -Destination $entry.Backup -ErrorAction Stop
            $entry.Suspended = $true
            $Transaction | Export-Clixml -Path (Join-Path $Transaction.Evidence 'transaction.xml') -Depth 40
        }
    }
    $Transaction.State = 'Suspended'
}

function Complete-AutoCadInstallTransaction {
    param([object]$Transaction)
    $Transaction.State = 'Complete'
    $Transaction | Export-Clixml -Path (Join-Path $Transaction.Evidence 'transaction.xml') -Depth 40
}

function Undo-AutoCadInstallTransaction {
    param([object]$Transaction)
    Assert-AutoCadTransactionIdle
    $errors = @()
    # Preflight all registration paths before restoring metadata. A concurrent
    # replacement must not cause rollback to change files now used by another run.
    if (-not (Get-AutoCadTransactionFinalPath $Transaction.SourceBundle).Equals($Transaction.SourceIdentity, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installation source was redirected concurrently; preserving it. Evidence: $($Transaction.Evidence)"
    }
    foreach ($entry in @($Transaction.Registrations)) {
        [void](Assert-AutoCadTransactionRegistrationPath $entry.Path)
        if ($entry.Exists -and -not $entry.Suspended) { continue }
        $current = Get-Item -LiteralPath $entry.Path -Force -ErrorAction SilentlyContinue
        if ($current -and (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -or
            -not (Get-AutoCadTransactionFinalPath $entry.Path).Equals($Transaction.SourceIdentity, [StringComparison]::OrdinalIgnoreCase))) {
            throw "Registration was changed concurrently; preserving all recovery files: $($entry.Path)"
        }
    }
    Assert-SafeRuntimeDataDestination $Transaction.SourceBundle
    foreach ($file in @($Transaction.SourceFiles)) {
        $current = Get-Item -LiteralPath $file.Path -Force -ErrorAction SilentlyContinue
        if ($current -and ($current.PSIsContainer -or ($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
            throw "Recovery metadata was replaced by a directory or link; preserving it: $($file.Path)"
        }
    }
    # Preserve the failed attempt's metadata too. Restore install-report before
    # validating a same-source junction's original Data/Logs manifest.
    foreach ($file in @($Transaction.SourceFiles)) {
        try {
            if (Test-Path -LiteralPath $file.Path) {
                Copy-Item -LiteralPath $file.Path -Destination ($file.Backup + '.before-undo-' + [Guid]::NewGuid().ToString('N')) -ErrorAction Stop
            }
            if ($file.Exists) { Copy-Item -LiteralPath $file.Backup -Destination $file.Path -Force -ErrorAction Stop }
            elseif (Test-Path -LiteralPath $file.Path) { Remove-Item -LiteralPath $file.Path -Force -ErrorAction Stop }
        }
        catch { $errors += $_.Exception.Message }
    }
    if ($errors.Count -gt 0) {
        $Transaction.State = 'RecoveryRequired'
        $Transaction | Export-Clixml -Path (Join-Path $Transaction.Evidence 'transaction.xml') -Depth 40
        throw ("Metadata recovery incomplete. Evidence: $($Transaction.Evidence). " + ($errors -join '; '))
    }
    foreach ($entry in @($Transaction.Registrations)) {
        try {
            [void](Assert-AutoCadTransactionRegistrationPath $entry.Path)
            if ($entry.Exists -and -not $entry.Suspended) { continue }
            $current = Get-Item -LiteralPath $entry.Path -Force -ErrorAction SilentlyContinue
            if ($current) {
                if (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
                    throw "Refusing to overwrite an unexpected directory: $($entry.Path)"
                }
                if (-not (Get-AutoCadTransactionFinalPath $entry.Path).Equals((Get-AutoCadTransactionFinalPath $Transaction.SourceBundle), [StringComparison]::OrdinalIgnoreCase)) {
                    throw "Registration was changed concurrently; preserving it: $($entry.Path)"
                }
                # This removes only the product junction, never its target/data.
                [IO.Directory]::Delete($entry.Path)
            }
            if ($entry.Suspended) {
                Move-Item -LiteralPath $entry.Backup -Destination $entry.Path -ErrorAction Stop
                $entry.Suspended = $false
                Assert-RuntimeDataManifest -Bundle $entry.Path -Expected $entry.Manifest -Exact
            }
        }
        catch { $errors += $_.Exception.Message }
    }
    foreach ($snapshot in @($Transaction.Registry)) {
        try {
            if (Test-Path -LiteralPath $snapshot.Path) {
                $currentKey = Get-Item -LiteralPath $snapshot.Path
                $loader = [string]$currentKey.GetValue('LOADER', '')
                if ($loader) {
                    $allowed = $false
                    foreach ($bundle in @($Transaction.SourceBundle) + @($Transaction.Registrations | ForEach-Object { $_.Path })) {
                        if ($loader.StartsWith(($bundle.TrimEnd('\') + '\'), [StringComparison]::OrdinalIgnoreCase)) { $allowed = $true }
                    }
                    # Existing unusual registrations may be restored only if their loader is unchanged.
                    foreach ($record in @($snapshot.Records)) {
                        if ($record.Relative) { continue }
                        foreach ($value in @($record.Values)) {
                            if ($value.Name -eq 'LOADER' -and $loader -eq [string]$value.Value) { $allowed = $true }
                        }
                    }
                    if (-not $allowed) { throw "Registry loader was changed concurrently; preserving it: $($snapshot.Path)" }
                }
            }
            if (Test-Path -LiteralPath $snapshot.Path) { Remove-Item -LiteralPath $snapshot.Path -Recurse -Force -ErrorAction Stop }
            if (-not $snapshot.Exists) { continue }
            foreach ($record in @($snapshot.Records)) {
                $path = $snapshot.Path
                if ($record.Relative) { $path = Join-Path $path $record.Relative }
                New-Item -Path $path -Force | Out-Null
                foreach ($value in @($record.Values)) {
                    # Windows PowerShell's registry provider can return a
                    # read-only RegistryKey from Get-Item. Write through the
                    # provider so rollback retains every original value kind.
                    New-ItemProperty -LiteralPath $path -Name $value.Name -Value $value.Value -PropertyType $value.Kind -Force -ErrorAction Stop | Out-Null
                }
            }
        }
        catch { $errors += $_.Exception.Message }
    }
    $Transaction.State = 'Restored'
    if ($errors.Count -gt 0) { $Transaction.State = 'RecoveryRequired' }
    $Transaction | Export-Clixml -Path (Join-Path $Transaction.Evidence 'transaction.xml') -Depth 40
    if ($errors.Count -gt 0) { throw ("Recovery incomplete. Evidence: $($Transaction.Evidence). " + ($errors -join '; ')) }
}
