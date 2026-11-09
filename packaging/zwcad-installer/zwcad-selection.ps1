function Get-BundleRootFromLoader([string]$loader) {
    if (-not $loader -or $loader.Trim().Length -eq 0) { return $null }
    $directory = Split-Path -Parent $loader
    while ($directory -and $directory.Trim().Length -gt 0) {
        if ((Split-Path -Leaf $directory) -like '*.bundle') { return $directory }
        $parent = Split-Path -Parent $directory
        if ($parent -eq $directory) { break }
        $directory = $parent
    }
    return $null
}

function Get-ZwcadRegistryRoots {
    @(
        'HKLM:\SOFTWARE\ZWSOFT\ZWCAD',
        'HKLM:\SOFTWARE\ZWSOFT\ZWCADM',
        'HKLM:\SOFTWARE\WOW6432Node\ZWSOFT\ZWCAD',
        'HKLM:\SOFTWARE\WOW6432Node\ZWSOFT\ZWCADM',
        'HKCU:\SOFTWARE\ZWSOFT\ZWCAD',
        'HKCU:\SOFTWARE\ZWSOFT\ZWCADM'
    ) | Where-Object { Test-Path -LiteralPath $_ }
}

function Test-Pe64([string]$path) {
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    try {
        $reader = New-Object IO.BinaryReader($stream)
        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        $stream.Position = $peOffset + 4
        return $reader.ReadUInt16() -eq 0x8664
    } finally {
        $stream.Dispose()
    }
}

function Get-ZwcadInstallations {
    $found = @()
    foreach ($root in Get-ZwcadRegistryRoots) {
        foreach ($versionKey in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            $year = 0
            if (-not [int]::TryParse($versionKey.PSChildName, [ref]$year) -or @(2020,2021,2022,2023,2024,2025,2026) -notcontains $year) { continue }
            foreach ($localeKey in Get-ChildItem -LiteralPath $versionKey.PSPath -ErrorAction SilentlyContinue) {
                if ($localeKey.PSChildName -eq 'Applications') { continue }
                $properties = Get-ItemProperty -LiteralPath $localeKey.PSPath -ErrorAction SilentlyContinue
                $locationProperty = if ($properties) { $properties.PSObject.Properties['Location'] } else { $null }
                $location = if ($locationProperty) { [string]$locationProperty.Value } else { '' }
                if (-not $location -or $location.Trim().Length -eq 0) { continue }
                $exe = Join-Path $location 'ZWCAD.exe'
                if (-not (Test-Path -LiteralPath $exe)) { continue }
                if (-not (Test-Pe64 $exe)) { continue }
                $found += New-Object PSObject -Property @{
                    Year = $year
                    Locale = $localeKey.PSChildName
                    RegistryPath = $localeKey.PSPath
                    InstallLocation = (Resolve-Path -LiteralPath $location).Path
                    Executable = (Resolve-Path -LiteralPath $exe).Path
                    FileVersion = (Get-Item -LiteralPath $exe).VersionInfo.FileVersion
                }
            }
        }
    }
    $found | Sort-Object Year, Locale, Executable -Unique
}

function Get-ExistingPluginLoaders {
    $loaders = @()
    foreach ($root in Get-ZwcadRegistryRoots) {
        foreach ($versionKey in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
            foreach ($localeKey in Get-ChildItem -LiteralPath $versionKey.PSPath -ErrorAction SilentlyContinue) {
                $appKey = Join-Path $localeKey.PSPath ('Applications\XiaoYangZWCADRadialMenu')
                if (-not (Test-Path -LiteralPath $appKey)) { continue }
                $properties = Get-ItemProperty -LiteralPath $appKey -ErrorAction SilentlyContinue
                $loaderProperty = if ($properties) { $properties.PSObject.Properties['LOADER'] } else { $null }
                $loader = if ($loaderProperty) { [string]$loaderProperty.Value } else { '' }
                if ($loader -and $loader.Trim().Length -gt 0) { $loaders += $loader }
            }
        }
    }
    $loaders | Sort-Object -Unique
}


function Get-ZwcadProductRegistrationPaths {
    foreach ($root in Get-ZwcadRegistryRoots) {
        foreach ($versionKey in Get-ChildItem -LiteralPath $root) {
            foreach ($localeKey in Get-ChildItem -LiteralPath $versionKey.PSPath) {
                $path = Join-Path $localeKey.PSPath 'Applications\XiaoYangZWCADRadialMenu'
                if (Test-Path -LiteralPath $path) { $path }
            }
        }
    }
}

function Read-ZwcadSelection([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Add-Type -AssemblyName System.Web.Extensions
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $value = $serializer.DeserializeObject([IO.File]::ReadAllText($Path))
    if (-not $value.ContainsKey('SelectedYears')) { throw "Invalid ZWCAD selection file: $Path" }
    foreach ($year in $value['SelectedYears']) {
        $number = 0
        if (-not [int]::TryParse([string]$year,[ref]$number) -or $number -lt 2020 -or $number -gt 2026) { throw "Invalid ZWCAD selected year in $Path" }
        $number
    }
}

function Get-ZwcadSavedSelection([string]$BundlePath) {
    if ($BundlePath) {
        $local = Join-Path $BundlePath 'install-selection.json'
        if (Test-Path -LiteralPath $local) { Read-ZwcadSelection $local; return }
    }
    $paths = @()
    foreach ($loader in Get-ExistingPluginLoaders) {
        $root = Get-BundleRootFromLoader $loader
        if ($root) {
            $path = Join-Path $root 'install-selection.json'
            if (Test-Path -LiteralPath $path) { $paths += Get-Item -LiteralPath $path }
        }
    }
    $latest = $paths | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($latest) { Read-ZwcadSelection $latest.FullName }
}

function Resolve-ZwcadSelection($Installations, $Years) {
    if (@($Years).Count -eq 0) { throw 'Choose at least one ZWCAD year.' }
    $available = @($Installations | ForEach-Object { [int]$_.Year })
    $selected = @()
    foreach ($year in $Years) {
        $number = 0
        if (-not [int]::TryParse([string]$year,[ref]$number) -or $available -notcontains $number) { throw "Selected ZWCAD year is not installed: $year" }
        $selected += $number
    }
    $Installations | Where-Object { $selected -contains [int]$_.Year }
}
