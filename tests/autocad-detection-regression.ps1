$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
. (Join-Path $repo 'packaging\installer-compat.ps1')
. (Join-Path $repo 'packaging\autocad-selection.ps1')
$run = Join-Path $repo ('work\autocad-detection-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
$fixtureKey = 'Software\XiaoYangInstallerTests\' + [Guid]::NewGuid().ToString('N')
$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($fixtureKey); $key.Close()
$script:checks = 0
function Assert($condition, $message) { if (-not $condition) { throw $message }; $script:checks++ }
try {
    # Redirect registry drives only in this test process; never change CAD registration.
    Remove-PSDrive HKCU,HKLM
    New-PSDrive HKCU Registry ("HKEY_CURRENT_USER\" + $fixtureKey) | Out-Null
    New-PSDrive HKLM Registry ("HKEY_CURRENT_USER\" + $fixtureKey) | Out-Null
    foreach ($year in @(2010,2011,2012,2013,2025)) {
        $directory = Join-Path $run ("自定义 CAD\AutoCAD $year")
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $bytes = New-Object byte[] 134
        $bytes[0]=0x4d; $bytes[1]=0x5a; $bytes[60]=128; $bytes[128]=0x50; $bytes[129]=0x45
        $machine = if ($year -eq 2011) { 0x014c } else { 0x8664 }
        [BitConverter]::GetBytes([uint16]$machine).CopyTo($bytes,132)
        [IO.File]::WriteAllBytes((Join-Path $directory 'acad.exe'),$bytes)
        $series = @{2010='R18.0';2011='R18.1';2012='R18.2';2013='R19.0';2025='R25.0'}[$year]
        $path = "HKCU:\Software\Autodesk\AutoCAD\$series\ACAD-F00:409"
        New-Item -Path $path -Force | Out-Null
        $property = if ($year -eq 2010) { 'AcadLocation' } else { 'Location' }
        New-ItemProperty -LiteralPath $path -Name $property -Value $directory -PropertyType String | Out-Null
        if ($year -eq 2025) {
            # Same executable from both values must be listed only once.
            New-ItemProperty -LiteralPath $path -Name AcadLocation -Value $directory -PropertyType String | Out-Null
        }
    }
    $detected = @(Find-AutoCadInstallations | Where-Object { $_.Path.StartsWith($run, [StringComparison]::OrdinalIgnoreCase) })
    Assert ($detected.Count -eq 5) 'Registry paths or old versions were lost or duplicated.'
    $old = @($detected | Where-Object { $_.Series -eq 'R18.0' })
    Assert ($old.Count -eq 1 -and $old[0].Supported -and $old[0].RuntimeFolder -eq 'NetFramework35') 'AutoCAD 2010 must use the CLR 2 kernel.'
    Assert ((Get-AutoCadYear $old[0].Series) -eq 2010) 'AutoCAD 2010 must show its real year.'
    Assert (@($detected | Where-Object { $_.Supported }).Count -eq 4) 'Year support or x86 exclusion changed.'
    $new = @($detected | Where-Object { $_.Series -eq 'R25.0' })
    Assert ($new.Count -eq 1 -and $new[0].Supported -and $new[0].RuntimeDll -eq 'MrZ.XiaoYangRadialMenu.AutoCAD2025.dll') 'AutoCAD 2025 kernel mapping changed.'
    $saved = Join-Path $run 'install-selection.json'
    [IO.File]::WriteAllText($saved,'{"Series":"R18.0"}',[Text.Encoding]::UTF8)
    Assert ((@(Read-AutoCadSelection $saved) -join ',') -eq 'R18.0') 'AutoCAD 2010 selection cannot be inherited.'
    Assert (@(Resolve-AutoCadSelection $detected @('R18.0')).Count -eq 1) 'AutoCAD 2010 cannot be selected.'
    Write-Host "PASS: $script:checks detection assertions. Evidence: $run"
} finally {
    Remove-PSDrive HKCU,HKLM -ErrorAction SilentlyContinue
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($fixtureKey)
    New-PSDrive HKCU Registry HKEY_CURRENT_USER | Out-Null
    New-PSDrive HKLM Registry HKEY_LOCAL_MACHINE | Out-Null
}
