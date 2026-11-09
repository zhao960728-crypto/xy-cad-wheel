$ErrorActionPreference = 'Stop'
$targetRoot = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins'
$targetRootFull = [IO.Path]::GetFullPath($targetRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$removed = 0
$productName = -join @(0x5C0F,0x9633,0x43,0x41,0x44,0x8F6E,0x76D8,0x63D2,0x4EF6 | ForEach-Object { [char]$_ })
foreach ($bundleName in @("$productName.bundle", 'XY-CAD-H.bundle')) {
    $targetFull = [IO.Path]::GetFullPath((Join-Path $targetRoot $bundleName))
    if (-not $targetFull.StartsWith($targetRootFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Uninstall target is outside ApplicationPlugins: $targetFull"
    }
    $item = Get-Item -LiteralPath $targetFull -Force -ErrorAction SilentlyContinue
    if (-not $item) { continue }
    $isLink = ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    if ($isLink) { [IO.Directory]::Delete($targetFull) }
    else { Remove-Item -LiteralPath $targetFull -Recurse -Force }
    $removed++
}
$autoCadRoot = 'HKCU:\Software\Autodesk\AutoCAD'
if (Test-Path -LiteralPath $autoCadRoot) {
    Get-ChildItem -LiteralPath $autoCadRoot -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem -LiteralPath $_.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
            foreach ($applicationName in @('XiaoYangCADRadialMenu', 'XiaoYangCADRadialMenuStartup')) {
                $applicationKey = Join-Path $_.PSPath "Applications\$applicationName"
                if (Test-Path -LiteralPath $applicationKey) {
                    Remove-Item -LiteralPath $applicationKey -Recurse -Force
                }
            }
        }
    }
}
if ($removed -eq 0) { Write-Host "$productName is not registered." }
else { Write-Host "$productName AutoCAD registration was removed. Extracted files, settings and logs were kept." }
