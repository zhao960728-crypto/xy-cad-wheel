function Find-AutoCadInstallations {
    $paths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($registryRoot in @('HKCU:\Software\Autodesk\AutoCAD', 'HKLM:\SOFTWARE\Autodesk\AutoCAD', 'HKLM:\SOFTWARE\WOW6432Node\Autodesk\AutoCAD')) {
        if (-not (Test-Path -LiteralPath $registryRoot)) { continue }
        $productKeys = @(Get-ChildItem -LiteralPath $registryRoot -ErrorAction SilentlyContinue | ForEach-Object {
            Get-ChildItem -LiteralPath $_.PSPath -ErrorAction SilentlyContinue
        })
        $productKeys | ForEach-Object {
            $properties = Get-ItemProperty -LiteralPath $_.PSPath -ErrorAction SilentlyContinue
            foreach ($name in @('Location', 'AcadLocation')) {
                $property = if ($properties) { $properties.PSObject.Properties[$name] } else { $null }
                if (-not $property -or -not $property.Value) { continue }
                $acad = Join-Path ([string]$property.Value) 'acad.exe'
                if (Test-Path -LiteralPath $acad) { [void]$paths.Add([IO.Path]::GetFullPath($acad)) }
            }
        }
    }
    foreach ($root in @('C:\Program Files\Autodesk', 'D:\Program Files\Autodesk')) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue |
            Where-Object { $_.PSIsContainer -and $_.Name -like 'AutoCAD *' } |
            ForEach-Object {
                $acad = Join-Path $_.FullName 'acad.exe'
                if (Test-Path -LiteralPath $acad) { [void]$paths.Add([IO.Path]::GetFullPath($acad)) }
            }
    }
    @($paths) | ForEach-Object {
        $item = Get-Item -LiteralPath $_
        $versionText = "$($item.VersionInfo.ProductVersion) $($item.VersionInfo.FileVersion) $($item.Directory.Name)"
        $series = $null
        if ($versionText -match '(?i)(?:^|[^0-9])R?(18\.[0-2]|19\.[01]|20\.[01]|21\.0|22\.0|23\.[01]|24\.[0-3]|25\.[01]|26\.0)(?:[^0-9]|$)') {
            $series = $Matches[1]
        }
        elseif ($item.Directory.Name -match 'AutoCAD\s+(2010|2011|2012|2013|2014|2015|2016|2017|2018|2019|2020|2021|2022|2023|2024|2025|2026|2027)') {
            $series = @{
                '2010' = '18.0'; '2011' = '18.1'; '2012' = '18.2'; '2013' = '19.0'
                '2014' = '19.1'; '2015' = '20.0'; '2016' = '20.1'; '2017' = '21.0'
                '2018' = '22.0'; '2019' = '23.0'; '2020' = '23.1'
                '2021' = '24.0'; '2022' = '24.1'; '2023' = '24.2'
                '2024' = '24.3'; '2025' = '25.0'; '2026' = '25.1'; '2027' = '26.0'
            }[$Matches[1]]
        }
        $runtime = if (@('18.0', '18.1', '18.2') -contains $series) {
            '.NET Framework 3.5'
        } elseif (@('19.0', '19.1', '20.0', '20.1', '21.0', '22.0', '23.0', '23.1', '24.0', '24.1', '24.2', '24.3') -contains $series) {
            '.NET Framework 4.8'
        } elseif (@('25.0', '25.1') -contains $series) {
            '.NET 8'
        } elseif ($series -eq '26.0') {
            '.NET 10'
        } else {
            'Unsupported'
        }
        $runtimeFolder = if ($runtime -eq '.NET Framework 3.5') { 'NetFramework35' } elseif ($runtime -eq '.NET Framework 4.8') { 'NetFramework48' } elseif ($runtime -eq '.NET 8') { 'Net8' } elseif ($runtime -eq '.NET 10') { 'Net10' } else { '' }
        $runtimeDll = switch ($series) {
            '18.0' { 'MrZ.XiaoYangRadialMenu.AutoCAD2010.dll' }; '18.1' { 'MrZ.XiaoYangRadialMenu.AutoCAD2011.dll' }
            '18.2' { 'MrZ.XiaoYangRadialMenu.AutoCAD2012.dll' }; '19.0' { 'MrZ.XiaoYangRadialMenu.AutoCAD2013.dll' }
            '19.1' { 'MrZ.XiaoYangRadialMenu.AutoCAD2014.dll' }; '20.0' { 'MrZ.XiaoYangRadialMenu.AutoCAD2015.dll' }
            '20.1' { 'MrZ.XiaoYangRadialMenu.AutoCAD2016.dll' }; '21.0' { 'MrZ.XiaoYangRadialMenu.AutoCAD2017.dll' }
            '22.0' { 'MrZ.XiaoYangRadialMenu.AutoCAD2018.dll' }; '23.0' { 'MrZ.XiaoYangRadialMenu.AutoCAD2019.dll' }
            '23.1' { 'MrZ.XiaoYangRadialMenu.AutoCAD2020.dll' }
            { @('24.0', '24.1', '24.2', '24.3') -contains $_ } { 'MrZ.XiaoYangRadialMenu.AutoCAD2021-2024.dll' }
            '25.0' { 'MrZ.XiaoYangRadialMenu.AutoCAD2025.dll' }; '25.1' { 'MrZ.XiaoYangRadialMenu.AutoCAD2026.dll' }
            '26.0' { 'MrZ.XiaoYangRadialMenu.AutoCAD2027.dll' }
            default { '' }
        }
        New-Object PSObject -Property @{
            Path = $item.FullName
            Series = if ($series) { "R$series" } else { $item.VersionInfo.FileVersion }
            Runtime = $runtime
            RuntimeFolder = $runtimeFolder
            RuntimeDll = $runtimeDll
            Supported = ($runtime -ne 'Unsupported' -and (Test-AutoCad64Bit $item.FullName))
        }
    }
}

function Test-AutoCad64Bit {
    param([string]$Path)
    $reader = $null
    try {
        $reader = New-Object IO.BinaryReader ([IO.File]::OpenRead($Path))
        if ($reader.ReadUInt16() -ne 0x5A4D) { return $false }
        $reader.BaseStream.Position = 0x3C
        $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $reader.BaseStream.Length - 6) { return $false }
        $reader.BaseStream.Position = $offset
        return ($reader.ReadUInt32() -eq 0x4550 -and $reader.ReadUInt16() -eq 0x8664)
    } finally { if ($reader) { $reader.Close() } }
}

function Get-AutoCadYear {
    param([string]$Series)
    return @{'R18.0'=2010; 'R18.1'=2011; 'R18.2'=2012; 'R19.0'=2013;
        'R19.1'=2014; 'R20.0'=2015; 'R20.1'=2016; 'R21.0'=2017; 'R22.0'=2018;
        'R23.0'=2019; 'R23.1'=2020; 'R24.0'=2021; 'R24.1'=2022; 'R24.2'=2023;
        'R24.3'=2024; 'R25.0'=2025; 'R25.1'=2026; 'R26.0'=2027}[$Series]
}

function Read-AutoCadSelection {
    param([string]$Path)
    $saved = Read-JsonFile $Path
    if (-not $saved.Series) { throw "CAD 版本选择记录无效，请重新运行一键安装 EXE：$Path" }
    foreach ($release in @(([string]$saved.Series) -split ',')) {
        if (-not (Get-AutoCadYear $release)) { throw "CAD 版本选择记录包含未知版本：$release" }
        $release
    }
}

function Resolve-AutoCadSelection {
    param([object[]]$Installations, [string[]]$Series)
    if (-not $Series -or $Series.Count -eq 0) { throw '请至少选择一个 AutoCAD 版本。' }
    foreach ($release in $Series) {
        if (@($Installations | Where-Object { $_.Supported -and $_.Series -eq $release }).Count -eq 0) {
            throw "未检测到受支持的所选 AutoCAD 版本：$release"
        }
    }
    @($Installations | Where-Object { $_.Supported -and $Series -contains $_.Series })
}

function Set-AutoCadManifestSelection {
    param([string]$ManifestPath, [string[]]$Series)
    [xml]$xml = Read-Utf8Text $ManifestPath
    # Always derive from the complete manifest, including on same-folder repair.
    $template = $ManifestPath + '.all-versions'
    if (Test-Path -LiteralPath $template) { [xml]$xml = Read-Utf8Text $template }
    else { Copy-Item -LiteralPath $ManifestPath -Destination $template }
    $nodes = @($xml.ApplicationPackage.Components)
    $matched = @()
    foreach ($node in $nodes) {
        $min = [version]$node.RuntimeRequirements.SeriesMin.TrimStart('R')
        $max = [version]$node.RuntimeRequirements.SeriesMax.TrimStart('R')
        foreach ($release in @($Series | Sort-Object -Unique)) {
            $version = [version]$release.TrimStart('R')
            if ($version -lt $min -or $version -gt $max) { continue }
            $copy = $node.CloneNode($true)
            $copy.RuntimeRequirements.SetAttribute('SeriesMin', $release)
            $copy.RuntimeRequirements.SetAttribute('SeriesMax', $release)
            [void]$xml.ApplicationPackage.AppendChild($copy)
            $matched += $release
        }
        [void]$xml.ApplicationPackage.RemoveChild($node)
    }
    foreach ($release in $Series) {
        if ($matched -notcontains $release) { throw "安装清单缺少所选版本：$release" }
    }
    $xml.Save($ManifestPath)
}
