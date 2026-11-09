param([string[]]$SelectedSeries)
$ErrorActionPreference = 'Stop'
if ($SelectedSeries) { $SelectedSeries = @($SelectedSeries | ForEach-Object { $_ -split ',' }) }
Add-Type -AssemblyName System.Windows.Forms

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

. (Join-Path $scriptRoot 'autocad-selection.ps1')
. (Join-Path $scriptRoot 'autocad-install-transaction.ps1')
$stringsPath = Join-Path $scriptRoot 'installer-zh-CN.json'
if (-not (Test-Path -LiteralPath $stringsPath)) {
    throw "Installer language file is missing: $stringsPath"
}
$strings = Read-JsonFile $stringsPath
$autoConfirm = $env:XYCAD_INSTALLER_AUTOCONFIRM -eq '1'

function Show-InstallerMessage {
    param(
        [string]$Text,
        [System.Windows.Forms.MessageBoxButtons]$Buttons = [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]$Icon = [System.Windows.Forms.MessageBoxIcon]::Information
    )
    if ($autoConfirm) {
        if ($Buttons -eq [System.Windows.Forms.MessageBoxButtons]::YesNo) {
            return [System.Windows.Forms.DialogResult]::Yes
        }
        return [System.Windows.Forms.DialogResult]::OK
    }
    $defaultButton = if ($Buttons -eq [System.Windows.Forms.MessageBoxButtons]::YesNo) {
        [System.Windows.Forms.MessageBoxDefaultButton]::Button2
    } else {
        [System.Windows.Forms.MessageBoxDefaultButton]::Button1
    }
    [System.Windows.Forms.MessageBox]::Show($Text, $strings.Title, $Buttons, $Icon, $defaultButton)
}

function Get-PackageVersion {
    param([string]$ManifestPath)
    if (-not (Test-Path -LiteralPath $ManifestPath)) { return $null }
    try {
        [xml]$manifest = Read-Utf8Text $ManifestPath
        return [version]$manifest.ApplicationPackage.AppVersion
    }
    catch { return $null }
}

function Get-ComparablePackageVersion {
    param([version]$Version)
    if (-not $Version) { return $null }
    # Historical compatibility: 2.1.9.0 was the old spelling of 2.1.0.9.
    # New releases always compare all four fields as Major.Minor.Build.Revision.
    if ($Version.Major -eq 2 -and $Version.Minor -eq 1 -and $Version.Build -gt 0 -and $Version.Revision -eq 0) {
        $normalized = "{0}.{1}.0.{2}" -f $Version.Major, $Version.Minor, $Version.Build
        return ($normalized -as [version])
    }
    $build = [Math]::Max(0, $Version.Build)
    $revision = [Math]::Max(0, $Version.Revision)
    return New-Object System.Version -ArgumentList @($Version.Major, $Version.Minor, $build, $revision)
}

function Get-SupportedAutoCadProfileKeys {
    $profiles = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($registryRoot in @('HKCU:\Software\Autodesk\AutoCAD', 'HKLM:\SOFTWARE\Autodesk\AutoCAD')) {
        if (-not (Test-Path -LiteralPath $registryRoot)) { continue }
        foreach ($releaseKey in @(Get-ChildItem -LiteralPath $registryRoot -ErrorAction SilentlyContinue)) {
            $release = $releaseKey.PSChildName
            if (@('R18.0', 'R18.1', 'R18.2', 'R19.0', 'R19.1', 'R20.0', 'R20.1', 'R21.0', 'R22.0', 'R23.0', 'R23.1', 'R24.0', 'R24.1', 'R24.2', 'R24.3', 'R25.0', 'R25.1', 'R26.0') -notcontains $release) { continue }
            foreach ($productKey in @(Get-ChildItem -LiteralPath $releaseKey.PSPath -ErrorAction SilentlyContinue)) {
                if ($productKey.PSChildName -notmatch '^ACAD-[^\\]+:[0-9A-Fa-f]+$') { continue }
                $profilePath = "HKCU:\Software\Autodesk\AutoCAD\$release\$($productKey.PSChildName)"
                $identity = "$release|$($productKey.PSChildName)"
                if (-not $profiles.ContainsKey($identity)) {
                    $profiles[$identity] = New-Object PSObject -Property @{ Release = $release; Path = $profilePath }
                }
            }
        }
    }
    @($profiles.Values)
}

function Register-AutoCadStartup {
    param([string]$RegistrationBundle)
    $results = @()
    foreach ($profile in @(Get-SupportedAutoCadProfileKeys)) {
        if ($effectiveSeries -notcontains $profile.Release) { continue }
        $relativeDll = switch ($profile.Release) {
            'R18.0' { 'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2010.dll' }
            'R18.1' { 'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2011.dll' }
            'R18.2' { 'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2012.dll' }
            'R19.0' { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2013.dll' }
            'R19.1' { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2014.dll' }
            'R20.0' { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2015.dll' }
            'R20.1' { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2016.dll' }
            'R21.0' { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2017.dll' }
            'R22.0' { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2018.dll' }
            'R23.0' { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2019.dll' }
            'R23.1' { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2020.dll' }
            'R25.0' { 'Contents\Net8\MrZ.XiaoYangRadialMenu.AutoCAD2025.dll' }
            'R25.1' { 'Contents\Net8\MrZ.XiaoYangRadialMenu.AutoCAD2026.dll' }
            'R26.0' { 'Contents\Net10\MrZ.XiaoYangRadialMenu.AutoCAD2027.dll' }
            default { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2021-2024.dll' }
        }
        $loader = Join-Path $RegistrationBundle $relativeDll
        if (-not (Test-Path -LiteralPath $loader)) { continue }
        # Keep the startup loader under its own name. AutoCAD's bundle scanner
        # rebuilds the command application's LOADCTRLS value on every launch;
        # a separate startup entry is the same stable bootstrap pattern used by
        # mature CAD tool installers and is not overwritten by that scan.
        $applicationKey = Join-Path $profile.Path 'Applications\XiaoYangCADRadialMenuStartup'
        if (-not (Test-Path -LiteralPath $profile.Path)) { New-Item -Path $profile.Path -Force | Out-Null }
        if (-not (Test-Path -LiteralPath $applicationKey)) { New-Item -Path $applicationKey -Force | Out-Null }
        New-ItemProperty -Path $applicationKey -Name DESCRIPTION -Value $productName -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $applicationKey -Name LOADCTRLS -Value 2 -PropertyType DWord -Force | Out-Null
        New-ItemProperty -Path $applicationKey -Name LOADER -Value $loader -PropertyType String -Force | Out-Null
        New-ItemProperty -Path $applicationKey -Name MANAGED -Value 1 -PropertyType DWord -Force | Out-Null

        $verified = Get-ItemProperty -LiteralPath $applicationKey
        if (($verified.LOADCTRLS -band 2) -eq 0 -or -not (Test-Path -LiteralPath $verified.LOADER)) {
            throw "AutoCAD startup registration verification failed: $applicationKey"
        }
        $results += "$($profile.Release) | LOADCTRLS=$($verified.LOADCTRLS) | $($verified.LOADER)"
    }
    if ($results.Count -eq 0) {
        throw 'No supported AutoCAD user profile could be registered for startup loading.'
    }
    $results
}

$installMutex = New-Object Threading.Mutex($false, 'Local\XiaoYang.AutoCAD.InstallTransaction')
$ownsInstallMutex = $false
try {
    try { $ownsInstallMutex = $installMutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $ownsInstallMutex = $true }
    if (-not $ownsInstallMutex) { throw '另一个小阳 CAD 安装正在执行，请等待它完成。' }
$productName = -join @(0x5C0F,0x9633,0x43,0x41,0x44,0x8F6E,0x76D8,0x63D2,0x4EF6 | ForEach-Object { [char]$_ })
$displayVersion = ''
$bundleName = "$productName.bundle"
$legacyBundleName = 'XY-CAD-H.bundle'
$source = [IO.Path]::GetFullPath((Join-Path $scriptRoot $bundleName))
$manifestPath = Join-Path $source 'PackageContents.xml'
$allDllPaths = @{
    'AutoCAD 2010 / .NET Framework 3.5' = Join-Path $source 'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2010.dll'
    'AutoCAD 2011 / .NET Framework 3.5' = Join-Path $source 'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2011.dll'
    'AutoCAD 2012 / .NET Framework 3.5' = Join-Path $source 'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2012.dll'
    'AutoCAD 2013 / .NET Framework 4.8' = Join-Path $source 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2013.dll'
    'AutoCAD 2014 / .NET Framework 4.8' = Join-Path $source 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2014.dll'
    'AutoCAD 2015 / .NET Framework 4.8' = Join-Path $source 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2015.dll'
    'AutoCAD 2016 / .NET Framework 4.8' = Join-Path $source 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2016.dll'
    'AutoCAD 2017 / .NET Framework 4.8' = Join-Path $source 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2017.dll'
    'AutoCAD 2018 / .NET Framework 4.8' = Join-Path $source 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2018.dll'
    'AutoCAD 2019 / .NET Framework 4.8' = Join-Path $source 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2019.dll'
    'AutoCAD 2020 / .NET Framework 4.8' = Join-Path $source 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2020.dll'
    'AutoCAD 2021-2024 / .NET Framework 4.8' = Join-Path $source 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2021-2024.dll'
    'AutoCAD 2025 / .NET 8' = Join-Path $source 'Contents\Net8\MrZ.XiaoYangRadialMenu.AutoCAD2025.dll'
    'AutoCAD 2026 / .NET 8' = Join-Path $source 'Contents\Net8\MrZ.XiaoYangRadialMenu.AutoCAD2026.dll'
    'AutoCAD 2027 / .NET 10' = Join-Path $source 'Contents\Net10\MrZ.XiaoYangRadialMenu.AutoCAD2027.dll'
}
$menuSource = Join-Path $source 'Contents\Menu\XYCADMENU.mnu'
if (-not (Test-Path -LiteralPath $menuSource)) {
    throw 'The independent AutoCAD menu source is missing: Contents\Menu\XYCADMENU.mnu'
}
$menuText = [IO.File]::ReadAllText($menuSource, [Text.Encoding]::GetEncoding(936))
if ($menuText -notmatch '\*\*\*MENUGROUP=XYCADMENU' -or $menuText -notmatch '(?m)^\[[^\r\n\]]+\]\s*XYLP\s*$') {
    throw 'The independent AutoCAD menu source is invalid or stale.'
}

$packageVersion = Get-PackageVersion $manifestPath
if (-not $packageVersion) { throw 'PackageContents.xml is missing or invalid.' }
$packageVersion = Get-ComparablePackageVersion $packageVersion
Assert-SharedContactResources -BundlePath $source -Version $packageVersion
$displayVersion = $packageVersion.ToString(4)
$installations = @(Find-AutoCadInstallations)
$compatible = @($installations | Where-Object { $_.Supported })
if ($compatible.Count -eq 0) {
    [void](Show-InstallerMessage -Text $strings.NoCompatible -Icon Error)
    throw 'No supported AutoCAD installation was found. Supported: AutoCAD 2010-2027 x64 (R18.0-R26.0).'
}
# Silent updates inherit the explicit choice from the installed product.
$previousSelection = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\$bundleName\install-selection.json"
if (-not $PSBoundParameters.ContainsKey('SelectedSeries') -and (Test-Path -LiteralPath $previousSelection)) {
    $saved = @(Read-AutoCadSelection $previousSelection)
    $SelectedSeries = @($saved | Where-Object { $release = $_; @($compatible | Where-Object { $_.Series -eq $release }).Count -gt 0 })
    if ($SelectedSeries.Count -eq 0) { throw '原来选择的 CAD 版本均已不可用，请使用一键安装 EXE 重新选择。' }
}
if ($PSBoundParameters.ContainsKey('SelectedSeries') -or $SelectedSeries) {
    $compatible = @(Resolve-AutoCadSelection -Installations $installations -Series $SelectedSeries)
}
$effectiveSeries = @($compatible | Select-Object -ExpandProperty Series -Unique)
$availableProfiles = @(Get-SupportedAutoCadProfileKeys)
foreach ($release in $effectiveSeries) {
    if (@($availableProfiles | Where-Object { $_.Release -eq $release }).Count -eq 0) {
        throw "所选版本缺少 AutoCAD 产品注册信息：$release。请先正常启动该 CAD 一次再安装。"
    }
}
$retainedRuntimeFolders = @($compatible | Select-Object -ExpandProperty RuntimeFolder -Unique)
$retainedRuntimeDlls = @($compatible | Select-Object -ExpandProperty RuntimeDll -Unique)
$dllPaths = @{}
foreach ($entry in $allDllPaths.GetEnumerator()) {
    if ($retainedRuntimeDlls -notcontains (Split-Path $entry.Value -Leaf)) { continue }
    if (-not (Test-Path -LiteralPath $entry.Value)) {
        throw "Plugin DLL is missing for $($entry.Key). Re-extract the complete release package and install again."
    }
    $dllPaths[$entry.Key] = $entry.Value
}
$dllVersions = @{}
if ($retainedRuntimeFolders -contains 'NetFramework35') {
    $legacyRuntime = Join-Path $source 'Contents\NetFramework35'
    foreach ($name in @('Theraot.Core.dll', 'Theraot.Core.LICENSE.txt', 'XYCAD.LegacyUpdater.exe', 'XYCAD.LegacyUpdater.exe.config')) {
        if (-not (Test-Path -LiteralPath (Join-Path $legacyRuntime $name))) { throw "旧版 CAD 运行组件缺失，请重新解压完整安装包：$name" }
    }
    if ([version](Get-Item -LiteralPath (Join-Path $legacyRuntime 'XYCAD.LegacyUpdater.exe')).VersionInfo.FileVersion -ne $packageVersion) { throw '旧版 CAD 更新组件版本不一致。' }
}
foreach ($entry in $dllPaths.GetEnumerator()) {
    $dllVersion = [version](Get-Item -LiteralPath $entry.Value).VersionInfo.FileVersion
    $dllVersions[$entry.Key] = $dllVersion.ToString()
    if ($packageVersion.CompareTo((Get-ComparablePackageVersion $dllVersion)) -ne 0) {
        throw "Package version $packageVersion does not match $($entry.Key) DLL version $dllVersion. The release package is incomplete or stale."
    }
}

$running = @(Get-Process acad, accoreconsole -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $processText = ($running | ForEach-Object { "$($_.ProcessName) (PID $($_.Id))" }) -join "`r`n"
    # Do not force-terminate AutoCAD. A forced exit can discard a user's
    # just-changed profile variables (for example FILEDIA=1) before the host
    # writes them to its profile. Close CAD normally, then run the upgrade.
    [void](Show-InstallerMessage -Icon Warning -Text ("检测到正在运行的 AutoCAD/核心控制台：`r`n{0}`r`n`r`n请先正常关闭 CAD 后重新运行安装。为保护 FILEDIA 等用户系统变量，安装程序不会强制结束 CAD。" -f $processText))
    Write-Host 'Installation cancelled because AutoCAD is still running. Close it normally and run the installer again.'
    exit 2
}

$targetRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$target = Join-Path $targetRoot $bundleName
$targetRootFull = [IO.Path]::GetFullPath($targetRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$targetFull = [IO.Path]::GetFullPath($target)
if (-not $targetFull.StartsWith($targetRootFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Install registration is outside ApplicationPlugins: $targetFull"
}
if ($source.TrimEnd('\').Equals($targetFull.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Do not extract the installation package directly into Autodesk\ApplicationPlugins. Move it to a long-term local folder and run the installer again.'
}

$existingVersion = Get-PackageVersion (Join-Path $targetFull 'PackageContents.xml')
$installMode = 'FreshInstall'
if ($existingVersion) {
    $existingComparable = Get-ComparablePackageVersion $existingVersion
    $comparison = $existingComparable.CompareTo($packageVersion)
    if ($comparison -gt 0) {
        throw "A newer $productName version $existingVersion is already registered. This package is $packageVersion and will not downgrade it."
    }
    $installMode = if ($comparison -eq 0) { 'Repair' } else { 'Upgrade' }
}

New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $source 'Data') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $source 'Logs') | Out-Null

# Back up this product's registrations and data before changing load scope.
$legacyTarget = Join-Path $targetRoot $legacyBundleName
$transaction = Start-AutoCadInstallTransaction -SourceBundle $source -RegistrationPaths @($legacyTarget, $targetFull) -ProfilePaths @($availableProfiles | ForEach-Object { $_.Path })
try {
Suspend-AutoCadRegistrations -Transaction $transaction
Set-AutoCadManifestSelection -ManifestPath $manifestPath -Series $effectiveSeries
Write-JsonFile -Path (Join-Path $source 'install-selection.json') -Value @{ Series = ($effectiveSeries -join ',') }

# Keep all isolated kernels so a later selection or rollback never loses a runtime.
$removedRuntimeFiles = New-Object 'System.Collections.Generic.List[string]'
$writeProbe = Join-Path $source 'Data\.installer-write-test'
try { [IO.File]::WriteAllText($writeProbe, 'ok') }
finally { if (Test-Path -LiteralPath $writeProbe) { Remove-Item -LiteralPath $writeProbe -Force } }

New-DirectoryJunctionCompatible -Path $targetFull -Target $source
$registered = Get-Item -LiteralPath $targetFull -Force
if (($registered.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) {
    throw 'ApplicationPlugins registration was not created as a directory junction.'
}

# Gentle upgrade policy: only register this plugin's managed Applications
# loader. Do not rewrite TRUSTEDPATHS, Startup Suite, menu registrations, or
# any other user/plugin setting.
$menuRegistrationsReset = @()
$startupRegistrations = @(Register-AutoCadStartup -RegistrationBundle $targetFull)
foreach ($profile in $availableProfiles) {
    if ($effectiveSeries -contains $profile.Release) { continue }
    foreach ($name in @('XiaoYangCADRadialMenu', 'XiaoYangCADRadialMenuStartup')) {
        $key = Join-Path $profile.Path "Applications\$name"
        if (Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Recurse -Force }
    }
}
$trustedPathRegistrations = @()
$startupSuiteRegistrations = @()

$dllHashes = @{}
foreach ($entry in $dllPaths.GetEnumerator()) {
    $relativePath = $entry.Value.Substring($source.Length).TrimStart('\')
    $sourceHash = Get-Sha256Hash $entry.Value
    $targetHash = Get-Sha256Hash (Join-Path $targetFull $relativePath)
    if ($sourceHash -ne $targetHash) { throw "The registered DLL failed SHA256 verification: $($entry.Key)" }
    $dllHashes[$entry.Key] = $targetHash
}
$report = @{
    PackageVersion = $displayVersion
    AssemblyVersion = $packageVersion.ToString()
    PreviousVersion = if ($existingVersion) { $existingVersion.ToString() } else { '' }
    InstallMode = $installMode
    DllVersions = $dllVersions
    InstalledFolder = $source
    AutoCadRegistration = $targetFull
    RegistrationType = 'DirectoryJunction'
    DllSha256 = $dllHashes
    CompatibleAutoCad = @($compatible | ForEach-Object { "$($_.Series) | $($_.Runtime) | $($_.Path)" })
    SelectedSeries = $effectiveSeries
    RetainedRuntimeFolders = $retainedRuntimeFolders
    RetainedRuntimeDlls = $retainedRuntimeDlls
    RemovedRuntimeFiles = @($removedRuntimeFiles)
    StartupRegistrations = $startupRegistrations
    TrustedPathRegistrations = $trustedPathRegistrations
    StartupSuiteRegistrations = $startupSuiteRegistrations
    PartialMenuRegistrationsReset = $menuRegistrationsReset
    InstalledAt = (Get-Date).ToString('s')
}
Write-JsonFile -Path (Join-Path $source 'Logs\install-report.json') -Value $report
Complete-AutoCadInstallTransaction -Transaction $transaction
} catch {
    $failure = $_
    try { Undo-AutoCadInstallTransaction -Transaction $transaction }
    catch { throw ("安装失败：{0}；恢复未完成：{1}。恢复文件位于：{2}" -f $failure.Exception.Message, $_.Exception.Message, $source) }
    throw $failure
}

Write-Host "Installed folder: $source"
Write-Host "AutoCAD registration: $targetFull -> $source"
Write-Host "Version: $displayVersion (assembly $packageVersion)"
Write-Host "Install mode: $installMode$(if ($existingVersion) { " | previous $existingVersion" })"
$dllHashes.GetEnumerator() | ForEach-Object { Write-Host "DLL SHA256 [$($_.Key)]: $($_.Value)" }
$startupRegistrations | ForEach-Object { Write-Host "Startup registration: $_" }
$trustedPathRegistrations | ForEach-Object { Write-Host "Trusted path: $_" }
$startupSuiteRegistrations | ForEach-Object { Write-Host "Startup Suite: $_" }
$menuRegistrationsReset | ForEach-Object { Write-Host "Reset stale partial menu registration: $_" }
[void](Show-InstallerMessage -Text ($strings.Success -f $displayVersion, $source))

} finally {
    if ($ownsInstallMutex) { $installMutex.ReleaseMutex() }
    $installMutex.Close()
}
