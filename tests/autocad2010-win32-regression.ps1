param([Parameter(Mandatory=$true)][string]$PackageRoot)
$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4) { throw 'Run in x86 Windows PowerShell.' }
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$run = Join-Path $repo ('work\win32-install-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
. (Join-Path $PackageRoot 'installer-compat.ps1')
. (Join-Path $PackageRoot 'autocad-selection.ps1')
$script:checks = 0
function Assert($condition, $message) { if (-not $condition) { throw $message }; $script:checks++ }
$fixtureKey = 'Software\XiaoYangWin32InstallerTests\' + [Guid]::NewGuid().ToString('N')
$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($fixtureKey); $key.Close()
$originalAppData = $env:APPDATA
try {
    Remove-PSDrive HKCU,HKLM
    New-PSDrive HKCU Registry ('HKEY_CURRENT_USER\' + $fixtureKey) | Out-Null
    New-PSDrive HKLM Registry ('HKEY_CURRENT_USER\' + $fixtureKey) | Out-Null
    $env:APPDATA = Join-Path $run 'appdata'
    $env:XYCAD_INSTALLER_AUTOCONFIRM = '1'
    foreach ($hostCase in @(@('AutoCAD 2010 x86','R18.0','ACAD-8001:409',0x014c), @('AutoCAD 2010 x64','R18.0','ACAD-8005:409',0x8664), @('AutoCAD 2011 x86','R18.1','ACAD-9001:409',0x014c))) {
        $folder = Join-Path $run $hostCase[0]; New-Item -ItemType Directory -Path $folder | Out-Null
        $bytes = New-Object byte[] 256
        $bytes[0]=0x4d; $bytes[1]=0x5a; $bytes[60]=128; $bytes[128]=0x50; $bytes[129]=0x45
        [BitConverter]::GetBytes([uint16]$hostCase[3]).CopyTo($bytes,132)
        [IO.File]::WriteAllBytes((Join-Path $folder 'acad.exe'),$bytes)
        $profile = 'HKCU:\Software\Autodesk\AutoCAD\' + $hostCase[1] + '\' + $hostCase[2]
        New-Item -Path $profile -Force | Out-Null
        New-ItemProperty -LiteralPath $profile -Name AcadLocation -Value $folder -PropertyType String | Out-Null
    }
    $detected = @(Find-AutoCadInstallations | Where-Object { $_.Path.StartsWith($run, [StringComparison]::OrdinalIgnoreCase) })
    Assert ($detected.Count -eq 3) 'Discovery omitted a registered executable.'
    Assert (@($detected | Where-Object { $_.Supported }).Count -eq 1) 'Only 2010 x86 may be supported.'
    Assert (@($detected | Where-Object { $_.Supported })[0].Path.Contains('2010 x86')) 'Wrong architecture was selected.'
    $compatible = @($detected | Where-Object { $_.Supported })
    . (Join-Path $repo 'packaging\legacy32\profiles.ps1')
    $profiles = @(Get-SupportedAutoCadProfileKeys)
    Assert ($profiles.Count -eq 1 -and $profiles[0].Path.EndsWith('ACAD-8001:409')) 'A different product profile was included.'
    foreach ($profile in @('HKCU:\Software\Autodesk\AutoCAD\R18.0\ACAD-8005:409','HKCU:\Software\Autodesk\AutoCAD\R18.1\ACAD-9001:409')) {
        $other = Join-Path $profile 'Applications\XiaoYangCADRadialMenuStartup'
        New-Item -Path $other -Force | Out-Null
        New-ItemProperty -LiteralPath $other -Name LOADER -Value 'keep-other-kernel.dll' -PropertyType String | Out-Null
    }
    $otherBundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\小阳CAD轮盘插件.bundle'
    New-Item -ItemType Directory -Path (Join-Path $otherBundle 'Data') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $otherBundle 'Data\keep.json'),'{"keep":"x64"}')
    $otherHash = Get-Sha256Hash (Join-Path $otherBundle 'Data\keep.json')
    function Prepare-Package($name, $fail) {
        $path = Join-Path $run $name
        Copy-Item -LiteralPath $PackageRoot -Destination $path -Recurse
        $script = Join-Path $path 'install.ps1'
        $text = [IO.File]::ReadAllText($script)
        # Test-only: the real CAD process stays open throughout this isolated run.
        $text = $text.Replace('$productName = -join', 'function Get-Process { @() }' + "`r`n" + '$productName = -join')
        if ($fail) { $text = $text.Replace('$startupRegistrations = @(Register-AutoCadStartup -RegistrationBundle $targetFull)', "throw 'Fixture registration failure'") }
        [IO.File]::WriteAllText($script,$text,[Text.Encoding]::UTF8)
        $path
    }
    $first = Prepare-Package 'first' $false
    & (Join-Path $first 'install.ps1') -SelectedSeries 'R18.0'
    $registered = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\小阳CAD轮盘插件-AutoCAD2010-Win32.bundle'
    $startup = 'HKCU:\Software\Autodesk\AutoCAD\R18.0\ACAD-8001:409\Applications\XiaoYangCADRadialMenuStartup'
    $dll = Join-Path $registered 'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2010.dll'
    Assert (Test-Path -LiteralPath $dll) 'Fresh registration missing.'
    Assert ((Get-ItemProperty -LiteralPath $startup).LOADER -eq $dll) 'Wrong startup kernel path.'
    $report = Read-JsonFile (Join-Path $registered 'Logs\install-report.json')
    Assert ($report.InstallMode -eq 'FreshInstall') 'Fresh install classification failed.'
    [IO.File]::WriteAllText((Join-Path $registered 'Data\fixture.json'),'{"custom":"用户配置"}',[Text.Encoding]::UTF8)
    [IO.File]::WriteAllText((Join-Path $registered 'Logs\fixture.log'),'keep-log')
    $dataHash = Get-Sha256Hash (Join-Path $registered 'Data\fixture.json')
    $logHash = Get-Sha256Hash (Join-Path $registered 'Logs\fixture.log')
    $second = Prepare-Package 'repair' $false
    & (Join-Path $second 'install.ps1')
    $report = Read-JsonFile (Join-Path $registered 'Logs\install-report.json')
    Assert ($report.InstallMode -eq 'Repair') 'Same-version repair classification failed.'
    Assert ((Get-Sha256Hash (Join-Path $registered 'Data\fixture.json')) -eq $dataHash) 'Repair changed user data.'
    Assert ((Get-Sha256Hash (Join-Path $registered 'Logs\fixture.log')) -eq $logHash) 'Repair changed logs.'
    Assert ((@(Read-AutoCadSelection (Join-Path $registered 'install-selection.json')) -join ',') -eq 'R18.0') 'Repair lost the year selection.'
    $manifestPath = Join-Path $registered 'PackageContents.xml'
    [xml]$priorManifest = [IO.File]::ReadAllText($manifestPath)
    $packageVersion = [version]$priorManifest.ApplicationPackage.AppVersion
    $priorVersion = New-Object Version($packageVersion.Major, $packageVersion.Minor, $packageVersion.Build, ($packageVersion.Revision - 1))
    $priorManifest.ApplicationPackage.SetAttribute('AppVersion',$priorVersion.ToString()); $priorManifest.Save($manifestPath)
    $upgraded = Prepare-Package 'upgrade' $false
    & (Join-Path $upgraded 'install.ps1')
    $report = Read-JsonFile (Join-Path $registered 'Logs\install-report.json')
    Assert ($report.InstallMode -eq 'Upgrade' -and $report.PreviousVersion -eq $priorVersion.ToString()) 'Upgrade classification failed.'
    Assert ((Get-Sha256Hash (Join-Path $registered 'Data\fixture.json')) -eq $dataHash) 'Upgrade changed user data.'
    Assert ((Get-Sha256Hash (Join-Path $registered 'Logs\fixture.log')) -eq $logHash) 'Upgrade changed logs.'
    [xml]$futureManifest = [IO.File]::ReadAllText($manifestPath)
    $currentVersion = [version]$futureManifest.ApplicationPackage.AppVersion
    $futureVersion = New-Object Version($currentVersion.Major, $currentVersion.Minor, $currentVersion.Build, ($currentVersion.Revision + 1))
    $futureManifest.ApplicationPackage.SetAttribute('AppVersion',$futureVersion.ToString()); $futureManifest.Save($manifestPath)
    $downgrade = Prepare-Package 'downgrade' $false
    $blocked = $false
    try { & (Join-Path $downgrade 'install.ps1') } catch { $blocked = $_.Exception.Message.Contains('will not downgrade') }
    Assert $blocked 'Downgrade was allowed.'
    $futureManifest.ApplicationPackage.SetAttribute('AppVersion',$currentVersion.ToString()); $futureManifest.Save($manifestPath)
    $third = Prepare-Package 'failed' $true
    $failed = $false
    try { & (Join-Path $third 'install.ps1') -SelectedSeries 'R18.0' } catch { $failed = $true; Write-Host ('Expected failure and recovery result: ' + $_.Exception.Message) }
    Assert $failed 'Injected failure did not fire.'
    Assert ((Get-Sha256Hash (Join-Path $registered 'Data\fixture.json')) -eq $dataHash) 'Rollback changed user data.'
    Assert ((Get-Sha256Hash (Join-Path $registered 'Logs\fixture.log')) -eq $logHash) 'Rollback changed logs.'
    Assert ((Get-ItemProperty -LiteralPath $startup).LOADER -eq $dll) ('Rollback lost the prior startup registration. Actual=' + (Get-ItemProperty -LiteralPath $startup).LOADER + '; Expected=' + $dll)
    & (Join-Path $upgraded 'self-test.ps1')
    Assert ((Get-Sha256Hash (Join-Path $otherBundle 'Data\keep.json')) -eq $otherHash) 'Independent x64 data changed.'
    foreach ($key in @('HKCU:\Software\Autodesk\AutoCAD\R18.0\ACAD-8005:409\Applications\XiaoYangCADRadialMenuStartup','HKCU:\Software\Autodesk\AutoCAD\R18.1\ACAD-9001:409\Applications\XiaoYangCADRadialMenuStartup')) {
        Assert ((Get-ItemProperty -LiteralPath $key).LOADER -eq 'keep-other-kernel.dll') 'Other architecture/year registration changed.'
    }
    $uninstall = Join-Path $upgraded 'uninstall.ps1'
    $text = [IO.File]::ReadAllText($uninstall).Replace('$ErrorActionPreference = ''Stop''', '$ErrorActionPreference = ''Stop''' + "`r`nfunction Get-Process { @() }")
    [IO.File]::WriteAllText($uninstall,$text,[Text.Encoding]::UTF8)
    & $uninstall
    Assert (-not (Test-Path -LiteralPath $registered)) 'Uninstall left its junction.'
    Assert (-not (Test-Path -LiteralPath $startup)) 'Uninstall left its startup key.'
    Assert ((Get-Sha256Hash (Join-Path $otherBundle 'Data\keep.json')) -eq $otherHash) 'Uninstall changed the x64 product.'
    Assert (Test-Path -LiteralPath (Join-Path $upgraded '小阳CAD轮盘插件-AutoCAD2010-Win32.bundle\Data\fixture.json')) 'Uninstall removed user data.'
    Write-Host "PASS: $script:checks Win32 installation assertions. Evidence: $run"
} finally {
    $env:APPDATA = $originalAppData
    Remove-PSDrive HKCU,HKLM -ErrorAction SilentlyContinue
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($fixtureKey)
    New-PSDrive HKCU Registry HKEY_CURRENT_USER | Out-Null
    New-PSDrive HKLM Registry HKEY_LOCAL_MACHINE | Out-Null
}
