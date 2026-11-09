$ErrorActionPreference = 'Stop'

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


function Find-SupportedAutoCad {
    $directories = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($environmentName in @(
        'AUTOCAD_2013_DIR', 'AUTOCAD_2014_DIR', 'AUTOCAD_2015_DIR', 'AUTOCAD_2016_DIR', 'AUTOCAD_2017_DIR',
        'AUTOCAD_2018_DIR', 'AUTOCAD_2019_DIR', 'AUTOCAD_2020_DIR',
        'AUTOCAD_2021_DIR', 'AUTOCAD_2022_DIR', 'AUTOCAD_2023_DIR',
        'AUTOCAD_2024_DIR', 'AUTOCAD_2025_DIR', 'AUTOCAD_2026_DIR', 'AUTOCAD_2027_DIR')) {
        $value = [Environment]::GetEnvironmentVariable($environmentName)
        if ($value) { [void]$directories.Add($value) }
    }
    foreach ($root in @('C:\Program Files\Autodesk', 'D:\Program Files\Autodesk')) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue |
            Where-Object { $_.PSIsContainer -and $_.Name -match '^AutoCAD\s+(2013|2014|2015|2016|2017|2018|2019|2020|2021|2022|2023|2024|2025|2026|2027)$' } |
            ForEach-Object { [void]$directories.Add($_.FullName) }
    }
    @($directories) | ForEach-Object {
        $core = Join-Path $_ 'accoreconsole.exe'
        if (-not (Test-Path -LiteralPath $core)) { return }
        $year = if ((Split-Path $_ -Leaf) -match '(2013|2014|2015|2016|2017|2018|2019|2020|2021|2022|2023|2024|2025|2026|2027)') { [int]$Matches[1] } else { 0 }
        if (@(2013, 2014, 2015, 2016, 2017, 2018, 2019, 2020, 2021, 2022, 2023, 2024, 2025, 2026, 2027) -notcontains $year) { return }
        New-Object PSObject -Property @{ Directory = $_; CoreConsole = $core; Year = $year }
    } | Sort-Object Year -Descending
}

function Get-FileDialogRegistrySnapshot {
    $root = 'HKCU:\Software\Autodesk\AutoCAD'
    if (-not (Test-Path -LiteralPath $root)) { return @() }
    @((Get-ChildItem -LiteralPath $root -Recurse -ErrorAction SilentlyContinue) | ForEach-Object {
        try {
            $key = Get-Item -LiteralPath $_.PSPath -ErrorAction Stop
            if ($key.GetValueNames() -notcontains 'FileDialog') { return }
            New-Object PSObject -Property @{
                Path = $_.PSPath
                Value = $key.GetValue('FileDialog')
                Kind = $key.GetValueKind('FileDialog').ToString()
            }
        }
        catch { }
    })
}

function Restore-FileDialogRegistrySnapshot {
    param([object[]]$Snapshot)
    foreach ($entry in @($Snapshot)) {
        if (-not (Test-Path -LiteralPath $entry.Path)) { continue }
        $key = Get-Item -LiteralPath $entry.Path
        if ($key.GetValueNames() -contains 'FileDialog') {
            Set-ItemProperty -LiteralPath $entry.Path -Name FileDialog -Value $entry.Value
        }
        else {
            New-ItemProperty -LiteralPath $entry.Path -Name FileDialog -Value $entry.Value -PropertyType $entry.Kind | Out-Null
        }
    }
}

$productName = -join @(0x5C0F,0x9633,0x43,0x41,0x44,0x8F6E,0x76D8,0x63D2,0x4EF6 | ForEach-Object { [char]$_ })
$bundleName = "$productName.bundle"
$bundle = Join-Path $scriptRoot $bundleName
$registration = Join-Path $env:APPDATA (Join-Path 'Autodesk\ApplicationPlugins' $bundleName)
[xml]$packageManifest = Read-Utf8Text (Join-Path $bundle 'PackageContents.xml')
$expectedPluginVersion = [version]$packageManifest.ApplicationPackage.AppVersion
Assert-SharedContactResources -BundlePath $bundle -Version $expectedPluginVersion
if (-not (Test-Path -LiteralPath $registration)) {
    throw 'The AutoCAD registration was not found. Run the one-click installer first.'
}
Assert-SharedContactResources -BundlePath $registration -Version $expectedPluginVersion

$startupRegistrations = @()
$autoCadRegistryRoot = 'HKCU:\Software\Autodesk\AutoCAD'
if (Test-Path -LiteralPath $autoCadRegistryRoot) {
    Get-ChildItem -LiteralPath $autoCadRegistryRoot -ErrorAction SilentlyContinue | ForEach-Object {
        if (@('R18.0', 'R18.1', 'R18.2', 'R19.0', 'R19.1', 'R20.0', 'R20.1', 'R21.0', 'R22.0', 'R23.0', 'R23.1', 'R24.0', 'R24.1', 'R24.2', 'R24.3', 'R25.0', 'R25.1', 'R26.0') -notcontains $_.PSChildName) { return }
        Get-ChildItem -LiteralPath $_.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
            $applicationKey = Join-Path $_.PSPath 'Applications\XiaoYangCADRadialMenuStartup'
            if (-not (Test-Path -LiteralPath $applicationKey)) { return }
            $values = Get-ItemProperty -LiteralPath $applicationKey
            if (($values.LOADCTRLS -band 2) -ne 0 -and (Test-Path -LiteralPath $values.LOADER)) {
                $startupRegistrations += "$applicationKey | LOADCTRLS=$($values.LOADCTRLS)"
            }
        }
    }
}
if ($startupRegistrations.Count -eq 0) {
    throw 'AutoCAD startup registration is missing or does not include LOADCTRLS 0x02.'
}

$trustedPathRegistrations = @()
$startupSuiteRegistrations = @()
$startupBootstrap = Join-Path $bundle 'Contents\Startup\XiaoYangCADStartup.lsp'
if (-not (Test-Path -LiteralPath $startupBootstrap)) {
    throw "The no-op startup placeholder is missing: $startupBootstrap"
}
$startupBootstrapText = [IO.File]::ReadAllText($startupBootstrap)
if ($startupBootstrapText -match '(?i)\b(?:vl-cmdf|command|command-s|XYLPSTART|XYLP|XYLPMENU)\b') {
    throw 'Startup placeholder is not gentle; it must not run plugin commands or change LSP loading order.'
}

$hostInfo = @(Find-SupportedAutoCad) | Select-Object -First 1
if (-not $hostInfo) {
    throw '未找到 AutoCAD 2013–2027 核心控制台。AutoCAD 2010–2012 请在 CAD 内执行 XYLPZJ 自检；未执行宿主检查，不能判定已通过。'
}
$relativeDll = switch ($hostInfo.Year) {
    2013 { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2013.dll' }
    2014 { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2014.dll' }
    2015 { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2015.dll' }
    2016 { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2016.dll' }
    2017 { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2017.dll' }
    2018 { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2018.dll' }
    2019 { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2019.dll' }
    2020 { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2020.dll' }
    2025 { 'Contents\Net8\MrZ.XiaoYangRadialMenu.AutoCAD2025.dll' }
    2026 { 'Contents\Net8\MrZ.XiaoYangRadialMenu.AutoCAD2026.dll' }
    2027 { 'Contents\Net10\MrZ.XiaoYangRadialMenu.AutoCAD2027.dll' }
    default { 'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2021-2024.dll' }
}
$installedDll = Join-Path $bundle $relativeDll
$registeredDll = Join-Path $registration $relativeDll
foreach ($dll in @($installedDll, $registeredDll)) {
    if (-not (Test-Path -LiteralPath $dll)) {
        throw "The required plugin DLL was not found: $dll"
    }
}
$runtimeDlls = @(
    'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2010.dll',
    'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2011.dll',
    'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2012.dll',
    'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2013.dll',
    'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2014.dll',
    'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2015.dll',
    'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2016.dll',
    'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2017.dll',
    'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2018.dll',
    'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2019.dll',
    'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2020.dll',
    'Contents\NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2021-2024.dll',
    'Contents\Net8\MrZ.XiaoYangRadialMenu.AutoCAD2025.dll',
    'Contents\Net8\MrZ.XiaoYangRadialMenu.AutoCAD2026.dll',
    'Contents\Net10\MrZ.XiaoYangRadialMenu.AutoCAD2027.dll'
)
foreach ($runtimeDll in $runtimeDlls) {
    $path = Join-Path $bundle $runtimeDll
    # Older installs may omit runtimes; new installers retain all isolated kernels.
    if ((Test-Path -LiteralPath $path) -and
        ([version](Get-Item -LiteralPath $path).VersionInfo.FileVersion).CompareTo($expectedPluginVersion) -ne 0) {
        throw "Compatibility runtime is stale: $path"
    }
}
$menuSource = Join-Path $bundle 'Contents\Menu\XYCADMENU.mnu'
if (-not (Test-Path -LiteralPath $menuSource)) { throw "Independent menu source is missing: $menuSource" }
$menuText = [IO.File]::ReadAllText($menuSource, [Text.Encoding]::GetEncoding(936))
if ($menuText -notmatch '\*\*\*MENUGROUP=XYCADMENU' -or $menuText -notmatch '(?m)^\[[^\r\n\]]+\]\s*XYLPMENU\s*$') {
    throw 'Independent menu source validation failed.'
}

function Get-RunningAutoCadApplication {
    foreach ($progId in @("AutoCAD.Application.$($hostInfo.Year - 2000).$($hostInfo.Year - 2021)", 'AutoCAD.Application')) {
        try {
            $application = [Runtime.InteropServices.Marshal]::GetActiveObject($progId)
            if ($application) { return $application }
        }
        catch { }
    }
    return $null
}

function Invoke-FullAutoCadSelfTest {
    $acad = Get-RunningAutoCadApplication
    if (-not $acad) {
        $acadExe = Join-Path $hostInfo.Directory 'acad.exe'
        if (-not (Test-Path -LiteralPath $acadExe)) {
            throw "AutoCAD executable was not found: $acadExe"
        }
        Start-Process -FilePath $acadExe -ArgumentList @('/product', 'ACAD', '/language', 'zh-CN') | Out-Null
        $deadline = (Get-Date).AddSeconds(90)
        while (-not $acad -and (Get-Date) -lt $deadline) {
            Start-Sleep -Seconds 1
            $acad = Get-RunningAutoCadApplication
        }
    }
    if (-not $acad) { throw 'AutoCAD COM application was not available for full-host self-test.' }
    try { $acad.Visible = $true } catch { }

    $doc = $null
    for ($attempt = 0; $attempt -lt 60 -and -not $doc; $attempt++) {
        try {
            if ($acad.Documents.Count -eq 0) { $doc = $acad.Documents.Add() }
            else { $doc = $acad.ActiveDocument }
        }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $doc) { throw 'AutoCAD document was not available for full-host self-test.' }

    try {
        $shell = New-Object -ComObject WScript.Shell
        [void]$shell.AppActivate($acad.Caption)
    }
    catch { }

    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        try {
            $state = $acad.GetAcadState()
            if ($state -and $state.IsQuiescent) { break }
        }
        catch {
            try {
                $names = [string]$doc.GetVariable('CMDNAMES')
                if ($names.Trim().Length -eq 0) { break }
            }
            catch { }
        }
        Start-Sleep -Seconds 1
    }

    $sent = $false
    for ($attempt = 0; $attempt -lt 60 -and -not $sent; $attempt++) {
        try {
            $doc.SendCommand("XYLPZJ ")
            $sent = $true
        }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $sent) { throw 'AutoCAD did not accept the XYLPZJ self-test command.' }
}

$work = Join-Path $env:TEMP 'XiaoYangCADRadialMenu-selftest'
New-Item -ItemType Directory -Force -Path $work | Out-Null

$startedAt = Get-Date
$fileDialogSnapshot = @(Get-FileDialogRegistrySnapshot)
try {
    Invoke-FullAutoCadSelfTest
}
finally {
    Restore-FileDialogRegistrySnapshot -Snapshot $fileDialogSnapshot
}

$reportFile = Get-Item -LiteralPath (Join-Path $bundle 'Logs\self-test.json') -ErrorAction SilentlyContinue
if (-not $reportFile) { throw 'The self-test report was not created.' }
if ($reportFile.LastWriteTime -lt $startedAt.AddSeconds(-2)) { throw 'The self-test report was not updated.' }

$report = Read-JsonFile $reportFile.FullName
if (-not $report.Passed) { throw "The plugin self-test did not pass: $($reportFile.FullName)" }
if ($report.DialogSystemVariablesPreserved -ne $true) {
    throw 'FILEDIA/CMDDIA preservation check did not pass.'
}
if ($report.UserSystemVariablesPreserved -ne $true) {
    throw 'AutoCAD command-state variables changed during plugin self-test.'
}

Write-Host "Self-test passed with AutoCAD $($hostInfo.Year): $($reportFile.FullName)"
Write-Host "Plugin version: $($report.PluginVersion)"
Write-Host "Plugin path: $($report.AssemblyPath)"
Write-Host "FILEDIA preserved: $($report.FileDiaBefore) -> $($report.FileDiaAfter)"
Write-Host "Command-state variables preserved: $($report.UserSystemVariablesPreserved)"
$startupRegistrations | ForEach-Object { Write-Host "Startup registration passed: $_" }
Write-Host "Gentle startup policy: Applications loader only; TRUSTEDPATHS and Startup Suite untouched."
