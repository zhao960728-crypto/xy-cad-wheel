$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4) { throw '请使用随包的卸载 CMD，以启动 32 位 PowerShell。' }
if (@(Get-Process acad, accoreconsole -ErrorAction SilentlyContinue).Count -gt 0) { throw '请先保存图纸并正常退出 CAD。' }
$bundle = [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\小阳CAD轮盘插件-AutoCAD2010-Win32.bundle'))
foreach ($product in @(Get-ChildItem 'HKCU:\Software\Autodesk\AutoCAD\R18.0' -ErrorAction SilentlyContinue)) {
    foreach ($name in @('XiaoYangCADRadialMenu', 'XiaoYangCADRadialMenuStartup')) {
        $key = Join-Path $product.PSPath ('Applications\' + $name)
        if (-not (Test-Path -LiteralPath $key)) { continue }
        $loader = [string](Get-ItemProperty -LiteralPath $key).LOADER
        if ($loader.StartsWith(($bundle + '\'), [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $key -Recurse -Force }
    }
}
$link = Get-Item -LiteralPath $bundle -Force -ErrorAction SilentlyContinue
if ($link) {
    if (($link.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) { throw '安装入口不是目录联接，保留文件并停止。' }
    [IO.Directory]::Delete($bundle)
}
Write-Host 'AutoCAD 2010 Win32 启动入口已移除；原安装目录、配置和日志已保留。'
