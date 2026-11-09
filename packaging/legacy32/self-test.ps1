$ErrorActionPreference = 'Stop'
if ([IntPtr]::Size -ne 4) { throw '请使用随包的自检 CMD，以启动 32 位 PowerShell。' }
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bundle = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\小阳CAD轮盘插件-AutoCAD2010-Win32.bundle'
$dll = Join-Path $bundle 'Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2010.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw '尚未安装 AutoCAD 2010 Win32 版。' }
[xml]$manifest = [IO.File]::ReadAllText((Join-Path $bundle 'PackageContents.xml'))
$version = [version]$manifest.ApplicationPackage.AppVersion
if ([version](Get-Item -LiteralPath $dll).VersionInfo.FileVersion -ne $version) { throw '内核与清单版本不一致。' }
$assembly = [Reflection.Assembly]::ReflectionOnlyLoadFrom($dll)
$pe = [Reflection.PortableExecutableKinds]0; $machine = [Reflection.ImageFileMachine]::I386
$assembly.ManifestModule.GetPEKind([ref]$pe, [ref]$machine)
if (($pe -band [Reflection.PortableExecutableKinds]::Required32Bit) -eq 0) { throw '内核不是 x86 版本。' }
$keys = @(Get-ChildItem 'HKCU:\Software\Autodesk\AutoCAD\R18.0' -ErrorAction SilentlyContinue | ForEach-Object {
    $path = Join-Path $_.PSPath 'Applications\XiaoYangCADRadialMenuStartup'
    if (Test-Path -LiteralPath $path) { Get-ItemProperty -LiteralPath $path }
} | Where-Object { $_.LOADER -eq $dll -and $_.MANAGED -eq 1 -and ($_.LOADCTRLS -band 2) -ne 0 })
if ($keys.Count -eq 0) { throw '2010 Win32 启动注册缺失。' }
Write-Host "PASS: AutoCAD 2010 Win32 $version; 启动 CAD 后运行 XYLPZJ 完成宿主自检。"
