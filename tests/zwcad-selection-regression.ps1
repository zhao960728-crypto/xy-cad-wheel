param([string]$PackageRoot)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $repo 'packaging\zwcad-installer\installer-compat.ps1')
. (Join-Path $repo 'packaging\zwcad-installer\zwcad-selection.ps1')
. (Join-Path $repo 'packaging\zwcad-installer\zwcad-transaction.ps1')
$script:checks = 0
function Assert($value,[string]$message) { if (-not $value) { throw $message }; $script:checks++ }
$run = Join-Path $repo ('work\zwcad-selection-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$fixture = 'HKCU:\Software\XiaoYangInstallerTests\' + [Guid]::NewGuid().ToString('N')
New-Item -Path $fixture -Force | Out-Null
try {
    $bundle = Join-Path $run 'fixture.bundle'
    New-Item -ItemType Directory -Path (Join-Path $bundle 'Data') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $bundle 'Logs') -Force | Out-Null
    $config = Join-Path $bundle 'Data\config.json'
    [IO.File]::WriteAllText($config,'{"custom":"preserve"}')
    $hash = [Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($config)))
    function Get-ZwcadRegistryRoots { $fixture }
    foreach ($year in @(2020,2024,2026)) {
        $folder = Join-Path $run ([string]$year)
        New-Item -ItemType Directory -Path $folder | Out-Null
        $bytes = New-Object byte[] 134
        $bytes[0] = 0x4d; $bytes[1] = 0x5a; $bytes[60] = 128; $bytes[128] = 0x50; $bytes[129] = 0x45
        [BitConverter]::GetBytes([uint16]0x8664).CopyTo($bytes,132)
        [IO.File]::WriteAllBytes((Join-Path $folder 'ZWCAD.exe'),$bytes)
        foreach ($locale in @('zh-CN','en-US')) {
            $key = Join-Path $fixture "$year\$locale"
            New-Item -Path $key -Force | Out-Null
            New-ItemProperty -Path $key -Name Location -Value $folder -PropertyType String | Out-Null
        }
    }
    $hosts = @(Get-ZwcadInstallations)
    Assert ($hosts.Count -eq 6) 'Detection omitted host or locale.'
    $selected = @(Resolve-ZwcadSelection $hosts @('2020','2024'))
    Assert ($selected.Count -eq 4) 'Multi-year selection failed.'
    Assert (@($selected | Where-Object { $_.Year -eq 2026 }).Count -eq 0) 'Unselected host included.'
    foreach ($years in @(@(),@('2020','2030'))) {
        $blocked = $false
        try { Resolve-ZwcadSelection $hosts $years | Out-Null } catch { $blocked = $true }
        Assert $blocked 'Invalid selection accepted.'
    }
    $app = Join-Path $fixture '2020\zh-CN\Applications\XiaoYangZWCADRadialMenu'
    New-Item -Path $app -Force | Out-Null
    New-ItemProperty -Path $app -Name LOADER -Value (Join-Path $bundle 'Contents\kernel.dll') -PropertyType String | Out-Null
    New-ItemProperty -Path $app -Name LOADCTRLS -Value 14 -PropertyType DWord | Out-Null
    New-ItemProperty -Path $app -Name EXPAND -Value '%TEMP%\literal' -PropertyType ExpandString | Out-Null
    New-ItemProperty -Path $app -Name BINARY -Value ([byte[]]@(1,2,255)) -PropertyType Binary | Out-Null
    Write-JsonFile (Join-Path $bundle 'install-selection.json') @{ SelectedYears = @(2020,2024) }
    Assert ((@(Get-ZwcadSavedSelection) -join ',') -eq '2020,2024') 'Saved update selection not inherited.'
    $newApp = Join-Path $fixture '2024\zh-CN\Applications\XiaoYangZWCADRadialMenu'
    $state = New-ZwcadInstallTransaction $bundle @($app,$newApp)
    Remove-ZwcadProductRegistrations @($app)
    New-Item -Path $newApp -Force | Out-Null
    [IO.File]::WriteAllText($config,'changed')
    [IO.File]::WriteAllText((Join-Path $bundle 'Data\new.json'),'new')
    $state.ExpectedFiles = @(Get-ZwcadProtectedFileState $bundle)
    function Get-Process { @() }
    Restore-ZwcadInstallTransaction $state
    Assert (-not (Test-Path -LiteralPath $newApp)) 'New registration survived rollback.'
    $key = Get-Item -LiteralPath $app
    Assert ($key.GetValueKind('LOADCTRLS').ToString() -eq 'DWord') 'DWORD kind was lost.'
    Assert ($key.GetValueKind('EXPAND').ToString() -eq 'ExpandString') 'Expandable kind was lost.'
    Assert ($key.GetValue('EXPAND',$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) -eq '%TEMP%\literal') 'Expandable string was expanded.'
    Assert (($key.GetValue('BINARY') -join ',') -eq '1,2,255') 'Binary value was lost.'
    $afterHash = [Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($config)))
    Assert ($afterHash -eq $hash) 'Config hash changed after rollback.'
    Assert (-not (Test-Path -LiteralPath (Join-Path $bundle 'Data\new.json'))) 'Rollback left migrated files.'
    # Exercise the production non-overwriting migration function without executing install.ps1.
    $source = [IO.File]::ReadAllText((Join-Path $repo 'packaging\zwcad-installer\install.ps1'))
    $start = $source.IndexOf('function Copy-DirectoryContent')
    $end = $source.IndexOf(". (Join-Path `$scriptRoot 'zwcad-selection.ps1')")
    . ([scriptblock]::Create($source.Substring($start,$end-$start)))
    $oldData = Join-Path $run 'old-data'
    New-Item -ItemType Directory -Path $oldData | Out-Null
    [IO.File]::WriteAllText((Join-Path $oldData 'config.json'),'do-not-overwrite')
    [IO.File]::WriteAllText((Join-Path $oldData 'missing.json'),'migrate')
    $transaction = $null
    Copy-DirectoryContent $oldData (Join-Path $bundle 'Data')
    Assert (([IO.File]::ReadAllText($config)) -eq '{"custom":"preserve"}') 'Migration overwrote configuration.'
    Assert (Test-Path -LiteralPath (Join-Path $bundle 'Data\missing.json')) 'Migration did not fill missing file.'
    if ($PackageRoot) {
        $package = Join-Path $run 'package'
        Copy-Item -LiteralPath $PackageRoot -Destination $package -Recurse
        foreach ($name in @('install.ps1','installer-compat.ps1','zwcad-selection.ps1','zwcad-transaction.ps1')) {
            Copy-Item -LiteralPath (Join-Path $repo ('packaging\zwcad-installer\' + $name)) -Destination (Join-Path $package $name) -Force
        }
        $bundleFolder = @(Get-ChildItem -LiteralPath $package | Where-Object { $_.PSIsContainer -and $_.Name -like '*.bundle' })[0].FullName
        $runtime = @(Get-ChildItem -LiteralPath $bundleFolder -Filter '*.dll' -Recurse)[0]
        $version = $runtime.VersionInfo.FileVersion
        $installerPath = Join-Path $package 'install.ps1'
        $installerSource = [IO.File]::ReadAllText($installerPath)
        $installerSource = [regex]::Replace($installerSource,'(?m)^\$packageVersion = .*',('$packageVersion = [version]' + [char]39 + $version + [char]39))
        [IO.File]::WriteAllText($installerPath,$installerSource,(New-Object Text.UTF8Encoding($true)))
        $helper = Join-Path $package 'zwcad-selection.ps1'
        [IO.File]::AppendAllText($helper,"`r`nfunction Get-ZwcadRegistryRoots { '" + $fixture.Replace("'","''") + "' }`r`n",(New-Object Text.UTF8Encoding($true)))
        & $installerPath -Quiet -SelectedYears '2020,2024'
        Assert (@(Get-ZwcadProductRegistrationPaths).Count -eq 4) 'Installer did not register exactly selected years/locales.'
        Assert ((@(Get-ZwcadSavedSelection) -join ',') -eq '2020,2024') 'Installer did not persist selection.'
        & $installerPath -Quiet
        Assert (@(Get-ZwcadProductRegistrationPaths).Count -eq 4) 'No-argument update expanded selection.'
        $savedConfig = Join-Path $bundleFolder 'Data\config.json'
        $beforeFailureHash = [Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($savedConfig)))
        $failSource = $installerSource.Replace('$mode = if',"throw 'Fixture registration failure'`r`n" + '$mode = if')
        [IO.File]::WriteAllText($installerPath,$failSource,(New-Object Text.UTF8Encoding($true)))
        $failed = $false
        try { & $installerPath -Quiet -SelectedYears '2026' } catch { $failed = $_.ToString() -like '*Fixture registration failure*' }
        Assert $failed 'Fixture failure was not surfaced.'
        Assert (@(Get-ZwcadProductRegistrationPaths).Count -eq 4) 'Failed installation did not restore previous registrations.'
        Assert ((@(Get-ZwcadSavedSelection) -join ',') -eq '2020,2024') 'Rollback did not restore selected years.'
        $afterFailureHash = [Convert]::ToBase64String([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($savedConfig)))
        Assert ($beforeFailureHash -eq $afterFailureHash) 'Failed installation changed config hash.'
    }
    Write-Host "ZWCAD selection regression passed: $script:checks checks. Fixture: $run"
} finally {
    if ($fixture -notlike 'HKCU:\Software\XiaoYangInstallerTests\*') { throw 'Invalid fixture cleanup path.' }
    Remove-Item -LiteralPath $fixture -Recurse -Force
}
