param(
    [switch]$ForceClose,
    [switch]$Quiet,
    [string]$SelectedYears
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$packageVersion = [version]'2.2.0.84'
$supportedYears = @(2020, 2021, 2022, 2023, 2024, 2025, 2026)
$appName = 'XiaoYangZWCADRadialMenu'
$bundleName = ((-join @(0x5C0F,0x9633,0x5A,0x57,0x43,0x41,0x44,0x8F6E,0x76D8,0x63D2,0x4EF6 | ForEach-Object { [char]$_ })) + '.bundle')
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot 'installer-compat.ps1')
function Assert-SharedContactResources {
    param([string]$BundlePath, [version]$Version)
    if ($Version -lt [version]'2.2.0.83') { return }
    foreach ($name in @('抖音.png', '加我.png')) {
        $path = Join-Path $BundlePath ('Contents\Resources\Contact\' + $name)
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Shared contact image is missing: $path" }
        $stream = [IO.File]::OpenRead($path)
        try {
            $header = New-Object byte[] 24
            if ($stream.Length -lt 33 -or $stream.Read($header, 0, 24) -ne 24) { throw "Shared contact PNG header is incomplete: $path" }
            $signature = @(137,80,78,71,13,10,26,10,0,0,0,13,73,72,68,82)
            for ($index = 0; $index -lt $signature.Count; $index++) {
                if ($header[$index] -ne $signature[$index]) { throw "Shared contact PNG header is invalid: $path" }
            }
            if ($header[16] -gt 127 -or $header[20] -gt 127 -or
                (($header[16] -bor $header[17] -bor $header[18] -bor $header[19]) -eq 0) -or
                (($header[20] -bor $header[21] -bor $header[22] -bor $header[23]) -eq 0)) {
                throw "Shared contact PNG dimensions are invalid: $path"
            }
        }
        finally { $stream.Dispose() }
    }
}

$bundlePath = Join-Path $scriptRoot $bundleName
$logDirectory = Join-Path $bundlePath 'Logs'
$dataDirectory = Join-Path $bundlePath 'Data'

function Get-KernelPath([int]$year) {
    Join-Path $bundlePath ("Contents\NetFramework48\ZWCAD{0}\MrZ.XiaoYangRadialMenu.ZWCAD{0}.dll" -f $year)
}

function Write-Status([string]$message) {
    Write-Host ('[XiaoYang ZWCAD RadialMenu] ' + $message)
}

function Copy-DirectoryContent([string]$source, [string]$target) {
    if (-not (Test-Path -LiteralPath $source)) { return }
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Get-ChildItem -LiteralPath $source -Recurse -Force | ForEach-Object {
        $relative = $_.FullName.Substring($source.Length).TrimStart('\')
        $destination = Join-Path $target $relative
        if ($_.PSIsContainer) {
            New-Item -ItemType Directory -Path $destination -Force | Out-Null
        } else {
            $parent = Split-Path -Parent $destination
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
            if (-not (Test-Path -LiteralPath $destination)) { Copy-Item -LiteralPath $_.FullName -Destination $destination; if ($transaction) { $transaction.ExpectedFiles = @(Get-ZwcadProtectedFileState $bundlePath) } }
        }
    }
}

. (Join-Path $scriptRoot 'zwcad-selection.ps1')
. (Join-Path $scriptRoot 'zwcad-transaction.ps1')

Assert-SharedContactResources -BundlePath $bundlePath -Version $packageVersion
$kernelAssemblies = @()
foreach ($year in $supportedYears) {
    $kernelPath = Get-KernelPath $year
    if (-not (Test-Path -LiteralPath $kernelPath)) {
        throw "Package is missing the ZWCAD $year runtime: $kernelPath"
    }
    $kernelVersion = [version](Get-Item -LiteralPath $kernelPath).VersionInfo.FileVersion
    if ($kernelVersion -ne $packageVersion) {
        throw "Package version mismatch: script=$packageVersion, ZWCAD $year runtime=$kernelVersion"
    }
    $kernelAssemblies += New-Object PSObject -Property @{
        Year = $year
        Version = $kernelVersion.ToString()
        Path = $kernelPath
    }
}

$installations = @(Get-ZwcadInstallations)
if ($installations.Count -eq 0) {
    throw 'No supported ZWCAD 2020-2026 x64 installation was detected.'
}

$allInstallations = $installations
if ($PSBoundParameters.ContainsKey('SelectedYears')) {
    $years = @($SelectedYears -split ',' | ForEach-Object { $_.Trim() })
} else {
    $years = @(Get-ZwcadSavedSelection -BundlePath $bundlePath)
    if ($years.Count -eq 0) { $years = @($installations | ForEach-Object { $_.Year } | Sort-Object -Unique) }
}
$installations = @(Resolve-ZwcadSelection $allInstallations $years)
$years = @($installations | ForEach-Object { [int]$_.Year } | Sort-Object -Unique)

$running = @(Get-Process -Name 'ZWCAD' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $close = $ForceClose
    if (-not $close -and -not $Quiet) {
        $answer = Read-Host 'ZWCAD is running. Type Y to force close and continue; other keys cancel'
        $close = $answer -match '^(Y|YES)$'
    }
    if (-not $close) { throw 'ZWCAD is still running; installation cancelled.' }
    Write-Status 'Force closing ZWCAD...'
    $running | Stop-Process -Force
    $running | ForEach-Object { try { $_.WaitForExit(10000) } catch { } }
}

$existingLoaders = @(Get-ExistingPluginLoaders)
$previousVersions = @()
foreach ($loader in $existingLoaders) {
    if (Test-Path -LiteralPath $loader) {
        try { $previousVersions += [version](Get-Item -LiteralPath $loader).VersionInfo.FileVersion } catch { }
    }
}
$previousVersion = $previousVersions | Sort-Object -Descending | Select-Object -First 1
if ($previousVersion -and $previousVersion -gt $packageVersion) {
    throw "Detected a newer installed version $previousVersion; downgrade to $packageVersion was blocked."
}

$registrationPaths = @(Get-ZwcadProductRegistrationPaths)
foreach ($installation in $allInstallations) {
    $registrationPaths += Join-Path $installation.RegistryPath ('Applications\' + $appName)
}
$registrationPaths = @($registrationPaths | Sort-Object -Unique)
$transaction = New-ZwcadInstallTransaction $bundlePath $registrationPaths
foreach ($installation in $installations) {
    $path = Join-Path $installation.RegistryPath ('Applications\' + $appName)
    $transaction.ExpectedLoaders[$path] = Get-KernelPath $installation.Year
}
try {
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
foreach ($loader in $existingLoaders) {
    $oldBundle = Get-BundleRootFromLoader $loader
    if (-not $oldBundle -or $oldBundle.Trim().Length -eq 0 -or -not (Test-Path -LiteralPath $oldBundle)) { continue }
    if ([IO.Path]::GetFullPath($oldBundle).TrimEnd('\') -eq [IO.Path]::GetFullPath($bundlePath).TrimEnd('\')) { continue }
    Copy-DirectoryContent (Join-Path $oldBundle 'Data') $dataDirectory
    Copy-DirectoryContent (Join-Path $oldBundle 'Logs') $logDirectory
}

Remove-ZwcadProductRegistrations $registrationPaths
$registered = @()
$commands = @('XYLP','XYMENU','XYLPSZ','XYSETTINGS','XYLPIME','XYLPIMETOGGLE','XYMERGEDWG','XYREFRESHCMDS','XYRESTORELOCALCMDS','XYLPMENU','XYLPSTART','XYLPZJ','XYSELFTEST')
foreach ($installation in $installations) {
    $assemblyPath = Get-KernelPath $installation.Year
    $appKey = Join-Path $installation.RegistryPath ('Applications\' + $appName)
    New-Item -Path $appKey -Force | Out-Null
    New-ItemProperty -Path $appKey -Name 'DESCRIPTION' -Value 'XiaoYang CAD radial menu for ZWCAD' -PropertyType String -Force | Out-Null
    # 2 = load at startup, 4 = load when a registered command is invoked,
    # 8 = load on explicit application request. 14 keeps all three paths.
    New-ItemProperty -Path $appKey -Name 'LOADCTRLS' -Value 14 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $appKey -Name 'LOADER' -Value $assemblyPath -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $appKey -Name 'MANAGED' -Value 1 -PropertyType DWord -Force | Out-Null
    $commandsKey = Join-Path $appKey 'Commands'
    New-Item -Path $commandsKey -Force | Out-Null
    foreach ($command in $commands) {
        New-ItemProperty -Path $commandsKey -Name $command -Value $command -PropertyType String -Force | Out-Null
    }
    $groupsKey = Join-Path $appKey 'Groups'
    New-Item -Path $groupsKey -Force | Out-Null
    New-ItemProperty -Path $groupsKey -Name 'XYCADMENU' -Value 'XYCADMENU' -PropertyType String -Force | Out-Null
    $registered += New-Object PSObject -Property @{
        Year = $installation.Year
        Locale = $installation.Locale
        FileVersion = $installation.FileVersion
        Architecture = 'x64'
        Kernel = ("ZWCAD{0}-net48" -f $installation.Year)
        AssemblyPath = $assemblyPath
        Executable = $installation.Executable
        RegistryPath = $appKey
    }
}

$mode = if (-not $previousVersion) { 'FreshInstall' } elseif ($previousVersion -eq $packageVersion) { 'Repair' } else { 'Upgrade' }
$report = @{
    Product = 'XiaoYang CAD radial menu for ZWCAD'
    PackageVersion = $packageVersion.ToString()
    AssemblyVersion = $packageVersion.ToString()
    KernelCompatibility = 'ZWCAD 2020-2026 x64; exact-year ZRX.NET kernels'
    PreviousVersion = if ($previousVersion) { $previousVersion.ToString() } else { $null }
    Mode = $mode
    BundlePath = $bundlePath
    KernelAssemblies = $kernelAssemblies
    RegisteredVersions = $registered
    ConfigurationPreserved = $true
    InstalledAt = (Get-Date).ToString('o')
}
Write-JsonFile -Path (Join-Path $logDirectory 'install-report.json') -Value $report
$transaction.ExpectedFiles = @(Get-ZwcadProtectedFileState $bundlePath)

Write-JsonFile -Path (Join-Path $bundlePath 'install-selection.json') -Value @{ Product = 'ZWCAD'; SelectedYears = $years; PackageVersion = $packageVersion.ToString() }
$transaction.ExpectedFiles = @(Get-ZwcadProtectedFileState $bundlePath)
} catch {
    $failure = $_
    try { Restore-ZwcadInstallTransaction $transaction } catch { throw ('Installation failed: ' + $failure + '; rollback failed; backup: ' + $transaction.Backup + '; ' + $_) }
    throw $failure
}

Write-Status ("Install complete: {0}; registered ZWCAD versions: {1}" -f $mode, $registered.Count)
$registered | ForEach-Object { Write-Host ("  ZWCAD {0} {1}: {2}" -f $_.Year, $_.Locale, $_.Executable) }
Write-Host 'After starting ZWCAD, use XYLP for wheel, XYLPSZ for settings, XYLPZJ for self-test.'





