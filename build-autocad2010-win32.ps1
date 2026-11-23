param([switch]$UseBuiltKernel)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '2.2.0.86'
$name = "小阳CAD轮盘插件-AutoCAD2010-Win32-$version"
$bundleName = '小阳CAD轮盘插件-AutoCAD2010-Win32.bundle'
$dist = Join-Path $root 'dist'
$staging = Join-Path $dist $name
$archive = Join-Path $dist ($name + '.zip')
$exe = Join-Path $dist ($name + '.exe')
foreach ($path in @($staging, $archive, $exe)) {
    if (Test-Path -LiteralPath $path) { throw "Release already exists; do not overwrite: $path" }
}
$project = Join-Path $root 'src\XiaoYang.AutoCADPlugin.AutoCAD2010.Win32\XiaoYang.AutoCADPlugin.AutoCAD2010.Win32.csproj'
if (-not $UseBuiltKernel) {
    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Win32 kernel build failed.' }
}
$output = Join-Path (Split-Path -Parent $project) 'bin\Release\net35'
$dll = Join-Path $output 'MrZ.XiaoYangRadialMenu.AutoCAD2010.dll'
if ([version](Get-Item -LiteralPath $dll).VersionInfo.FileVersion -ne [version]$version) { throw 'Stale Win32 kernel.' }
function Write-Utf8($path, $text) { [IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($true))) }
function Replace-Required($text, $old, $new) {
    if (-not $text.Contains($old)) { throw "Packaging template changed: $old" }
    $text.Replace($old, $new)
}
$bundle = Join-Path $staging $bundleName
$runtime = Join-Path $bundle 'Contents\NetFramework35'
$contact = Join-Path $bundle 'Contents\Resources\Contact'
$menu = Join-Path $bundle 'Contents\Menu'
New-Item -ItemType Directory -Path $runtime,$contact,$menu | Out-Null
Copy-Item -LiteralPath $dll,(Join-Path $output 'Theraot.Core.dll'),(Join-Path $root 'src\XiaoYang.AutoCADPlugin.LegacySupport\Theraot.Core.LICENSE.txt') -Destination $runtime
foreach ($image in @('抖音.png','加我.png')) { Copy-Item -LiteralPath (Join-Path $root ('src\XiaoYang.AutoCADPlugin\Resources\' + $image)) -Destination $contact }
[IO.File]::WriteAllText((Join-Path $menu 'XYCADMENU.mnu'), [IO.File]::ReadAllText((Join-Path $root 'packaging\menu-source\XYCADMENU.utf8.mnu')), [Text.Encoding]::GetEncoding(936))
[xml]$manifest = [IO.File]::ReadAllText((Join-Path $root 'packaging\XY-CAD-H.bundle\PackageContents.xml'))
$manifest.ApplicationPackage.SetAttribute('AppVersion', $version)
$manifest.ApplicationPackage.SetAttribute('ProductCode', '{04E37B05-825A-4D1F-927A-04B9EE28A685}')
$manifest.ApplicationPackage.SetAttribute('UpgradeCode', '{F2AA94B5-4124-4AEC-AE19-E77D8D68178B}')
$manifest.ApplicationPackage.SetAttribute('Name', '小阳CAD轮盘插件 AutoCAD 2010 Win32')
$manifest.ApplicationPackage.SetAttribute('Helpfile', '../使用说明.txt')
foreach ($node in @($manifest.ApplicationPackage.Components)) {
    if ($node.RuntimeRequirements.SeriesMin -ne 'R18.0') { [void]$manifest.ApplicationPackage.RemoveChild($node); continue }
    $node.SetAttribute('Description','AutoCAD 2010 Win32 (.NET Framework 3.5)')
    $node.RuntimeRequirements.SetAttribute('OS', 'Win32')
}
$manifest.Save((Join-Path $bundle 'PackageContents.xml'))

# Derive the single-architecture payload from the established transaction code.
# Required replacements fail closed if the shared templates change.
$install = [IO.File]::ReadAllText((Join-Path $root 'packaging\install.ps1'))
$install = Replace-Required $install '$bundleName = "$productName.bundle"' '$bundleName = "$productName-AutoCAD2010-Win32.bundle"'
$install = Replace-Required $install "`$legacyBundleName = 'XY-CAD-H.bundle'" "`$legacyBundleName = '$bundleName'"
$install = Replace-Required $install "'Theraot.Core.LICENSE.txt', 'XYCAD.LegacyUpdater.exe', 'XYCAD.LegacyUpdater.exe.config'" "'Theraot.Core.LICENSE.txt'"
$helperCheck = "    if ([version](Get-Item -LiteralPath (Join-Path `$legacyRuntime 'XYCAD.LegacyUpdater.exe')).VersionInfo.FileVersion -ne `$packageVersion) { throw '旧版 CAD 更新组件版本不一致。' }"
$install = Replace-Required $install $helperCheck ''
$install = Replace-Required $install 'AutoCAD 2010-2027 x64 (R18.0-R26.0).' 'AutoCAD 2010 Win32 (R18.0).'
$install = [regex]::Replace($install, '(?s)function Get-SupportedAutoCadProfileKeys \{.*?(?=function Register-AutoCadStartup)', [Text.RegularExpressions.MatchEvaluator]{ param($m) [IO.File]::ReadAllText((Join-Path $root 'packaging\legacy32\profiles.ps1')) + "`r`n" })
$install = [regex]::Replace($install, '(?s)\$allDllPaths = @\{.*?\r?\n\}', '$allDllPaths = @{ ''AutoCAD 2010 / .NET Framework 3.5'' = Join-Path $source ''Contents\NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2010.dll'' }')
$install = Replace-Required $install '$ErrorActionPreference = ''Stop''' ('$ErrorActionPreference = ''Stop''' + "`r`nif ([IntPtr]::Size -ne 4) { throw 'Use the Win32 EXE or CMD launcher.' }")
Write-Utf8 (Join-Path $staging 'install.ps1') $install
$transaction = [IO.File]::ReadAllText((Join-Path $root 'packaging\autocad-install-transaction.ps1'))
$transaction = Replace-Required $transaction '@("$product.bundle", ''XY-CAD-H.bundle'')' '@("$product-AutoCAD2010-Win32.bundle")'
Write-Utf8 (Join-Path $staging 'autocad-install-transaction.ps1') $transaction
$selection = [IO.File]::ReadAllText((Join-Path $root 'packaging\autocad-selection.ps1'))
$selection = Replace-Required $selection "(`$runtime -ne 'Unsupported' -and (Test-AutoCad64Bit `$item.FullName))" "(`$series -eq '18.0' -and (Test-AutoCad32Bit `$item.FullName))"
$selection = Replace-Required $selection 'Test-AutoCad64Bit' 'Test-AutoCad32Bit'
$selection = Replace-Required $selection '0x8664' '0x014c'
$selection = Replace-Required $selection "@('C:\Program Files\Autodesk', 'D:\Program Files\Autodesk')" "@('C:\Program Files\Autodesk', 'D:\Program Files\Autodesk', 'C:\Program Files (x86)\Autodesk', 'D:\Program Files (x86)\Autodesk')"
Write-Utf8 (Join-Path $staging 'autocad-selection.ps1') $selection
Copy-Item -LiteralPath (Join-Path $root 'packaging\installer-compat.ps1') -Destination $staging
foreach ($file in @('installer-ui.ps1','self-test.ps1','uninstall.ps1')) { Write-Utf8 (Join-Path $staging $file) ([IO.File]::ReadAllText((Join-Path $root ('packaging\legacy32\' + $file)))) }
$strings = @{ Title='AutoCAD 2010 Win32 独立安装'; NoCompatible='未检测到 AutoCAD 2010 的 32 位版本。请先正常启动该 CAD 一次，再安装。'; Success='AutoCAD 2010 Win32 {0} 安装成功。配置与日志位于：{1}。启动 CAD 后运行 XYLP、XYLPSZ、XYLPZJ。' }
Write-Utf8 (Join-Path $staging 'installer-zh-CN.json') ($strings | ConvertTo-Json)
foreach ($entry in @(@('安装插件.cmd','installer-ui.ps1'), @('自检插件.cmd','self-test.ps1'), @('卸载插件.cmd','uninstall.ps1'))) {
    $cmd = @('@echo off','setlocal','set "XYPS=%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe"','if exist "%SystemRoot%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" set "XYPS=%SystemRoot%\SysWOW64\WindowsPowerShell\v1.0\powershell.exe"',('"%XYPS%" -NoLogo -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0' + $entry[1] + '"'),'pause') -join "`r`n"
    [IO.File]::WriteAllText((Join-Path $staging $entry[0]), ($cmd + "`r`n"), [Text.Encoding]::ASCII)
}
$guide = @"
小阳 CAD 轮盘 · AutoCAD 2010 Win32 · $version

适用：AutoCAD 2010 32 位（R18.0），Windows 7 或更高版本，.NET Framework 3.5 SP1，Windows PowerShell 2.0 或更高版本。
安装器和插件均为 x86 / CLR 2；不需要 .NET 4.8，不改 acad.exe.config。

使用：先启动一次 AutoCAD 2010，再保存图纸并正常关闭 CAD，运行独立 EXE，或解压 ZIP 到长期保存的本地目录后运行“安装插件.cmd”。
安装后不能移动或删除安装目录。启动 CAD，用 XYLP 打开轮盘、XYLPSZ 设置、XYLPZJ 宿主自检。
自动发现依靠 AutoCAD 安装注册表中的 Location / AcadLocation，请勿将 AutoCAD LT 当作完整 AutoCAD。

与 64 位版使用不同 bundle 目录、独立产品标识，只注册已确认的 2010 x86 产品；升级/修复保留该独立版 Data/Logs，并带失败回滚。
更新请使用 AutoCAD2010-Win32 独立包；本版更新入口不访问 64 位版更新源。
本次修复：启动服务等待 CAD 主窗口和图纸空闲；输入法切换延后到空闲时执行，保存对话框打开时暂停；旧版宿主退出时不再解绑已销毁的原生事件反应器。
请复测：打开轮盘、修改并保存设置，创建并保存测试图纸，再正常退出 CAD。若仍出现 Autodesk 错误报告，请保留并提供独立 bundle 的 Logs 目录（含 autocad-plugin.log 和 errors.log），用阶段记录定位首次故障。
卸载运行随包“卸载插件.cmd”，保留原文件、配置和日志。

验证范围：32 位 CLR 2 运行、配置/Excel/设置页、安装事务和包抽取另有构建记录；本机没有 AutoCAD 2010 32 位宿主，实际加载、鼠标触发和 CAD 命令仍需在目标机验收。
"@
Write-Utf8 (Join-Path $staging '使用说明.txt') $guide
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $staging
& (Join-Path $root 'tests\powershell2-package-parse.ps1') -PackageRoot $staging
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive -CompressionLevel Optimal
dotnet build (Join-Path $root 'src\XiaoYang.Installer.Legacy32\XiaoYang.Installer.Legacy32.csproj') -c Release "-p:PayloadArchive=$archive"
if ($LASTEXITCODE -ne 0) { throw 'Win32 installer build failed.' }
Copy-Item -LiteralPath (Join-Path $root 'src\XiaoYang.Installer.Legacy32\bin\Release\net35\XYCAD.AutoCAD2010.Win32.Setup.exe') -Destination $exe
# CLR 2 is the default without a sidecar config; the launcher requires it too.
Write-Host "Win32 EXE: $exe"
Write-Host "Win32 ZIP: $archive"
