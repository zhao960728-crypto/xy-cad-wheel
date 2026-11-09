param([string]$PackageRoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $repo 'packaging\installer-compat.ps1')
. (Join-Path $repo 'packaging\autocad-selection.ps1')
$run = Join-Path $repo ('work\installer-selection-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$script:checks = 0
function Assert($condition, $message) { if (-not $condition) { throw $message }; $script:checks++ }
$hosts = @('R18.0','R18.1','R18.2','R19.0','R24.0','R24.3','R25.0','R26.0') | ForEach-Object { New-Object PSObject -Property @{Series=$_;Supported=$true} }
Assert (@(Resolve-AutoCadSelection $hosts @('R18.0','R18.1','R18.2','R19.0')).Count -eq 4) 'New legacy years cannot be selected.'
Assert (@(Resolve-AutoCadSelection $hosts @('R24.0','R25.0','R26.0')).Count -eq 3) 'Non-adjacent selections failed.'
Assert ((Get-AutoCadYear 'R26.0') -eq 2027) 'AutoCAD 2027 release mapping is missing.'
foreach ($selection in @(@(), @('R24.0','R99.0'))) {
    $blocked = $false
    try { Resolve-AutoCadSelection $hosts $selection | Out-Null } catch { $blocked = $true }
    Assert $blocked 'Empty or unknown selection was accepted.'
}
$manifest = Join-Path $run 'PackageContents.xml'
Copy-Item -LiteralPath (Join-Path $repo 'packaging\XY-CAD-H.bundle\PackageContents.xml') -Destination $manifest
Set-AutoCadManifestSelection $manifest @('R24.0','R24.3','R25.0')
[xml]$xml = Read-Utf8Text $manifest
$components = @($xml.ApplicationPackage.Components)
Assert ($components.Count -eq 3) 'Wrong component count.'
Assert (@($components | Where-Object { $_.RuntimeRequirements.SeriesMin -ne $_.RuntimeRequirements.SeriesMax }).Count -eq 0) 'A shared DLL enabled unselected years.'
Assert (@($components | Where-Object { $_.RuntimeRequirements.SeriesMin -eq 'R24.1' }).Count -eq 0) 'AutoCAD 2022 was incorrectly enabled.'
Set-AutoCadManifestSelection $manifest @('R19.1','R26.0')
[xml]$xml = Read-Utf8Text $manifest
Assert (@($xml.ApplicationPackage.Components).Count -eq 2) 'Reselection lost the full template.'
foreach ($machine in @(0x8664,0x014c)) {
    $file = Join-Path $run ("pe-$machine.exe")
    $bytes = New-Object byte[] 134
    $bytes[0]=0x4d; $bytes[1]=0x5a; $bytes[60]=128; $bytes[128]=0x50; $bytes[129]=0x45
    [BitConverter]::GetBytes([uint16]$machine).CopyTo($bytes,132)
    [IO.File]::WriteAllBytes($file,$bytes)
    Assert ((Test-AutoCad64Bit $file) -eq ($machine -eq 0x8664)) 'PE architecture detection failed.'
}
if ($PackageRoot) {
    # Replace registry drives only inside this isolated test process. Real Autodesk keys are never used.
    $fixtureKey = 'Software\XiaoYangInstallerTests\' + [Guid]::NewGuid().ToString('N')
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($fixtureKey); $key.Close()
    $originalAppData = $env:APPDATA
    try {
        Remove-PSDrive HKCU,HKLM
        New-PSDrive HKCU Registry ("HKEY_CURRENT_USER\" + $fixtureKey) | Out-Null
        New-PSDrive HKLM Registry ("HKEY_CURRENT_USER\" + $fixtureKey) | Out-Null
        $env:APPDATA = Join-Path $run 'appdata'
        $env:XYCAD_INSTALLER_AUTOCONFIRM = '1'
        foreach ($series in @('R18.0','R18.1','R18.2','R19.0','R24.0','R24.3','R25.0','R26.0')) {
            New-Item -Path "HKCU:\Software\Autodesk\AutoCAD\$series\ACAD-F00:409" -Force | Out-Null
        }
        $injection = @'
function Get-Process { @() }
function Find-AutoCadInstallations {
    foreach ($release in @('R18.0','R18.1','R18.2','R19.0','R24.0','R24.3','R25.0','R26.0')) {
        New-Object PSObject -Property @{
            Series=$release; Supported=$true; Path='fixture-acad.exe'; Runtime='fixture'
            RuntimeFolder=$(if ($release -like 'R18.*') { 'NetFramework35' } elseif ($release -eq 'R25.0') { 'Net8' } elseif ($release -eq 'R26.0') { 'Net10' } else { 'NetFramework48' })
            RuntimeDll=$(if ($release -like 'R18.*' -or $release -eq 'R19.0') { 'MrZ.XiaoYangRadialMenu.AutoCAD' + (Get-AutoCadYear $release) + '.dll' } elseif ($release -eq 'R25.0') { 'MrZ.XiaoYangRadialMenu.AutoCAD2025.dll' } elseif ($release -eq 'R26.0') { 'MrZ.XiaoYangRadialMenu.AutoCAD2027.dll' } else { 'MrZ.XiaoYangRadialMenu.AutoCAD2021-2024.dll' })
        }
    }
}
'@
        function Prepare-Package($name, $fail) {
            $path = Join-Path $run $name
            Copy-Item -LiteralPath $PackageRoot -Destination $path -Recurse
            $file = Join-Path $path 'install.ps1'
            $text = [IO.File]::ReadAllText($file)
            $position = $text.IndexOf('$productName = -join')
            if ($position -lt 0) { throw 'Installer injection point missing.' }
            $text = $text.Insert($position, $injection + "`r`n")
            if ($fail) { $text = $text.Replace('$startupRegistrations = @(Register-AutoCadStartup -RegistrationBundle $targetFull)', "throw 'Fixture registration failure'") }
            [IO.File]::WriteAllText($file,$text,[Text.Encoding]::UTF8)
            return $path
        }
        $first = Prepare-Package 'first' $false
        & (Join-Path $first 'install.ps1') -SelectedSeries 'R18.0,R18.1,R18.2,R19.0,R24.0,R25.0,R26.0'
        $registered = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\小阳CAD轮盘插件.bundle'
        Assert (Test-Path -LiteralPath "$registered\PackageContents.xml") 'Fresh registration missing.'
        Assert (-not (Test-Path 'HKCU:\Software\Autodesk\AutoCAD\R24.3\ACAD-F00:409\Applications\XiaoYangCADRadialMenuStartup')) 'Unselected CAD got a loader.'
        $legacyLoader = (Get-ItemProperty -LiteralPath 'HKCU:\Software\Autodesk\AutoCAD\R18.0\ACAD-F00:409\Applications\XiaoYangCADRadialMenuStartup').LOADER
        Assert ($legacyLoader -like '*\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2010.dll') 'AutoCAD 2010 loader points at the wrong runtime.'
        [IO.File]::WriteAllText((Join-Path $registered 'Data\fixture.json'),'{"custom":"用户配置"}',[Text.Encoding]::UTF8)
        $before = Get-Sha256Hash (Join-Path $registered 'Data\fixture.json')
        $second = Prepare-Package 'second' $false
        & (Join-Path $second 'install.ps1')
        $saved = @(Read-AutoCadSelection (Join-Path $registered 'install-selection.json'))
        Assert ((@($saved) -join ',') -eq 'R18.0,R18.1,R18.2,R19.0,R24.0,R25.0,R26.0') 'Silent update did not preserve selection.'
        Assert ((Get-Sha256Hash (Join-Path $registered 'Data\fixture.json')) -eq $before) 'Update changed user config.'
        $third = Prepare-Package 'failed' $true
        $failed = $false
        try { & (Join-Path $third 'install.ps1') -SelectedSeries 'R24.3' } catch { $failed = $true }
        Assert $failed 'Fixture failure did not fire.'
        Assert ((Get-Sha256Hash (Join-Path $registered 'Data\fixture.json')) -eq $before) 'Rollback changed user config.'
        $saved = @(Read-AutoCadSelection (Join-Path $registered 'install-selection.json'))
        Assert ((@($saved) -join ',') -eq 'R18.0,R18.1,R18.2,R19.0,R24.0,R25.0,R26.0') 'Rollback changed prior selection.'
        Assert (Test-Path 'HKCU:\Software\Autodesk\AutoCAD\R18.0\ACAD-F00:409\Applications\XiaoYangCADRadialMenuStartup') 'Rollback lost the prior AutoCAD 2010 loader.'
        Assert (Test-Path 'HKCU:\Software\Autodesk\AutoCAD\R25.0\ACAD-F00:409\Applications\XiaoYangCADRadialMenuStartup') 'Rollback lost prior loader.'
        $fourth = Prepare-Package 'reselect' $false
        & (Join-Path $fourth 'install.ps1') -SelectedSeries 'R24.3'
        Assert (-not (Test-Path 'HKCU:\Software\Autodesk\AutoCAD\R25.0\ACAD-F00:409\Applications\XiaoYangCADRadialMenuStartup')) 'Reselection left stale loader.'
        Assert (-not (Test-Path 'HKCU:\Software\Autodesk\AutoCAD\R26.0\ACAD-F00:409\Applications\XiaoYangCADRadialMenuStartup')) 'Reselection left stale AutoCAD 2027 loader.'
        foreach ($oldSeries in @('R18.0','R18.1','R18.2','R19.0')) {
            Assert (-not (Test-Path "HKCU:\Software\Autodesk\AutoCAD\$oldSeries\ACAD-F00:409\Applications\XiaoYangCADRadialMenuStartup")) 'Reselection left a stale legacy loader.'
        }
        Assert (Test-Path 'HKCU:\Software\Autodesk\AutoCAD\R24.3\ACAD-F00:409\Applications\XiaoYangCADRadialMenuStartup') 'Reselection omitted selected loader.'
        Assert ((Get-Sha256Hash (Join-Path $registered 'Data\fixture.json')) -eq $before) 'Reselection changed user config.'
    } finally {
        $env:APPDATA = $originalAppData
        Remove-PSDrive HKCU,HKLM -ErrorAction SilentlyContinue
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($fixtureKey)
        New-PSDrive HKCU Registry HKEY_CURRENT_USER | Out-Null
        New-PSDrive HKLM Registry HKEY_LOCAL_MACHINE | Out-Null
    }
}
Write-Host "PASS: $script:checks assertions. Evidence: $run"
