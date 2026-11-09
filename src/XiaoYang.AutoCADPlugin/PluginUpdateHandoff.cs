using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace XiaoYang.AutoCADPlugin
{
    /// <summary>Runs after the host exits normally. The package is a permanent installation location.</summary>
    internal static class PluginUpdateHandoff
    {
        internal static void Launch(string packageDirectory, string productId, string version)
        {
            if (productId != "xiaoyang-autocad-wheel" && productId != "xiaoyang-zwcad-wheel")
                throw new ArgumentException("未知更新产品。", nameof(productId));
            Version parsed;
            if (!Version.TryParse(version, out parsed) || parsed.Revision < 0)
                throw new ArgumentException("更新版本无效。", nameof(version));
            string directory = Path.GetFullPath(packageDirectory);
            if (!File.Exists(Path.Combine(directory, "install.ps1")))
                throw new FileNotFoundException("更新包安装程序不存在。");
            string evidence = Path.Combine(Path.GetDirectoryName(directory), "update-" + productId + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(evidence);
            var payload = new List<string>();
            SnapshotFiles(directory, directory, payload);
            File.WriteAllLines(Path.Combine(evidence, "payload-sha256.txt"), payload.ToArray(), Encoding.ASCII);
            string script = Path.Combine(evidence, "handoff.ps1");
            File.WriteAllText(script, Script, new UTF8Encoding(true));
            // Parameters travel in the child environment, never through executable command text.
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                Arguments = "-NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File \"" + script + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Normal,
                WorkingDirectory = evidence
            };
            start.EnvironmentVariables["XY_UPDATE_PACKAGE"] = directory;
            start.EnvironmentVariables["XY_UPDATE_PRODUCT"] = productId;
            start.EnvironmentVariables["XY_UPDATE_VERSION"] = version;
            start.EnvironmentVariables["XY_UPDATE_EVIDENCE"] = evidence;
            using (var process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("无法启动更新安装窗口。");
            }
        }

        private static void SnapshotFiles(string root, string directory, List<string> manifest)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("更新目录不能包含目录链接。");
            foreach (string entry in Directory.GetFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("更新包不能包含链接。");
                if ((attributes & FileAttributes.Directory) != 0) SnapshotFiles(root, entry, manifest);
                else
                {
                    using (var sha = SHA256.Create())
                    using (var stream = File.OpenRead(entry))
                        manifest.Add(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") + " " + Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.Substring(root.TrimEnd(Path.DirectorySeparatorChar).Length + 1))));
                }
            }
        }

        // Internal for offline parser/transaction tests; no external resource needs to be shipped.
        internal const string Script = @"
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$package = [IO.Path]::GetFullPath($env:XY_UPDATE_PACKAGE)
$product = $env:XY_UPDATE_PRODUCT
$version = [version]$env:XY_UPDATE_VERSION
$evidence = [IO.Path]::GetFullPath($env:XY_UPDATE_EVIDENCE)
$isAcad = $product -eq 'xiaoyang-autocad-wheel'
if (-not $isAcad -and $product -ne 'xiaoyang-zwcad-wheel') { throw 'Unknown update product.' }
$bundleName = if ($isAcad) { '小阳CAD轮盘插件.bundle' } else { '小阳ZWCAD轮盘插件.bundle' }
$newBundle = Join-Path $package $bundleName
$stateFile = Join-Path $evidence 'status.json'
$committing = $false
$cancelled = $false
$mutationStarted = $false
$registrySnapshots = @()
$links = @()
$dataSnapshots = @()
$preservedManifest = @{}
$mutex = $null
$ownsMutex = $false
function Write-State([string]$state, [string]$message) {
    $values=@{ ProductId=$product; Version=$version.ToString(); State=$state; Message=$message; PackageDirectory=$package; EvidenceDirectory=$evidence; UpdatedAt=[DateTime]::UtcNow.ToString('o') }
    $parts=@()
    foreach ($key in $values.Keys) { $parts += (Quote-Json $key) + ':' + (Quote-Json ([string]$values[$key])) }
    [IO.File]::WriteAllText($stateFile,('{' + ($parts -join ',') + '}'),[Text.Encoding]::UTF8)
}
function Quote-Json([string]$value) {
    $escaped=New-Object Text.StringBuilder
    [void]$escaped.Append([char]34)
    foreach ($character in $value.ToCharArray()) { [void]$escaped.Append(('\u' + ([int]$character).ToString('x4'))) }
    [void]$escaped.Append([char]34)
    $escaped.ToString()
}
function Assert-Payload {
    Assert-NoReparse $package
    $entries=@{}
    foreach ($line in [IO.File]::ReadAllLines((Join-Path $evidence 'payload-sha256.txt'))) {
        if ($line -notmatch '^([A-F0-9]{64}) ([A-Za-z0-9+/=]+)$') { throw '更新文件校验清单无效。' }
        $expected=$Matches[1]
        $relative=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($Matches[2]))
        $path=[IO.Path]::GetFullPath((Join-Path $package $relative))
        if (-not $path.StartsWith(($package.TrimEnd('\')+'\'),[StringComparison]::OrdinalIgnoreCase) -or $entries.ContainsKey($relative)) { throw '更新文件路径无效。' }
        $hash=[Security.Cryptography.SHA256]::Create()
        $stream=$null
        try {
            $stream=[IO.File]::OpenRead($path)
            $actual=[BitConverter]::ToString($hash.ComputeHash($stream)).Replace('-','')
        } finally { if ($stream) { $stream.Dispose() }; $hash.Clear() }
        if ($actual -ne $expected) { throw ('等待期间更新文件已改变，请重新下载：' + $relative) }
        $entries[$relative]=$expected
    }
    $actualFiles=@(Get-ChildItem -LiteralPath $package -Recurse -Force | Where-Object { -not $_.PSIsContainer })
    if ($actualFiles.Count -ne $entries.Count) { throw '等待期间更新文件清单已改变，请重新下载。' }
}
function Get-HostProcesses {
    if ($isAcad) { @(Get-Process -Name acad,accoreconsole -ErrorAction SilentlyContinue) }
    else { @(Get-Process -Name zwcad -ErrorAction SilentlyContinue) }
}
function Assert-NoReparse([string]$directory) {
    if (-not (Test-Path -LiteralPath $directory)) { return }
    foreach ($item in @((Get-Item -LiteralPath $directory -Force)) + @(Get-ChildItem -LiteralPath $directory -Force -Recurse)) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('不支持包含目录链接的数据或更新目录：' + $item.FullName) }
    }
}
function Get-DataManifest([string]$bundle) {
    $result = @{}
    foreach ($name in @('Data','Logs')) {
        $root = Join-Path $bundle $name
        Assert-NoReparse $root
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($file in @(Get-ChildItem -LiteralPath $root -Recurse -Force | Where-Object { -not $_.PSIsContainer })) {
            $relative = $file.FullName.Substring($bundle.TrimEnd('\').Length).TrimStart('\')
            $result[$relative] = Get-Sha256Hash $file.FullName
        }
    }
    return ,$result
}
function Assert-Data([string]$bundle, $manifest) {
    $current = Get-DataManifest $bundle
    if ($current.Count -ne $manifest.Count) { throw ('配置/日志文件清单出现增减：' + $bundle) }
    foreach ($relative in $manifest.Keys) {
        if (-not $current.ContainsKey($relative) -or $current[$relative] -ne $manifest[$relative]) { throw ('配置/日志校验失败：' + (Join-Path $bundle $relative)) }
    }
}
function Get-ProductKeys {
    $result = @()
    if ($isAcad) {
        foreach ($root in @('HKCU:\Software\Autodesk\AutoCAD','HKLM:\SOFTWARE\Autodesk\AutoCAD')) {
            if (-not (Test-Path -LiteralPath $root)) { continue }
            foreach ($release in @(Get-ChildItem -LiteralPath $root)) {
                if (@('R18.0','R18.1','R18.2','R19.0','R19.1','R20.0','R20.1','R21.0','R22.0','R23.0','R23.1','R24.0','R24.1','R24.2','R24.3','R25.0','R25.1','R26.0') -notcontains $release.PSChildName) { continue }
                foreach ($profile in @(Get-ChildItem -LiteralPath $release.PSPath)) {
                    if ($profile.PSChildName -match '^ACAD-[^\\]+:[0-9A-Fa-f]+$') {
                        $result += 'HKCU:\Software\Autodesk\AutoCAD\' + $release.PSChildName + '\' + $profile.PSChildName + '\Applications\XiaoYangCADRadialMenuStartup'
                    }
                }
            }
        }
    } else {
        foreach ($root in @('HKLM:\SOFTWARE\ZWSOFT\ZWCAD','HKLM:\SOFTWARE\ZWSOFT\ZWCADM','HKLM:\SOFTWARE\WOW6432Node\ZWSOFT\ZWCAD','HKLM:\SOFTWARE\WOW6432Node\ZWSOFT\ZWCADM','HKCU:\SOFTWARE\ZWSOFT\ZWCAD','HKCU:\SOFTWARE\ZWSOFT\ZWCADM')) {
            if (-not (Test-Path -LiteralPath $root)) { continue }
            foreach ($release in @(Get-ChildItem -LiteralPath $root)) {
                foreach ($locale in @(Get-ChildItem -LiteralPath $release.PSPath)) {
                    $key = $root + '\' + $release.PSChildName + '\' + $locale.PSChildName + '\Applications\XiaoYangZWCADRadialMenu'
                    $supported = @('2020','2021','2022','2023','2024','2025','2026') -contains $release.PSChildName
                    $props = Get-ItemProperty -LiteralPath $locale.PSPath
                    $location = $props.PSObject.Properties['Location']
                    if ((Test-Path -LiteralPath $key) -or ($supported -and $location -and (Test-Path -LiteralPath (Join-Path $location.Value 'ZWCAD.exe')))) { $result += $key }
                }
            }
        }
    }
    @($result | Sort-Object -Unique)
}
function Read-Key([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $items = @((Get-Item -LiteralPath $path)) + @(Get-ChildItem -LiteralPath $path -Recurse)
    $records = @()
    $baseName = (Get-Item -LiteralPath $path).Name
    foreach ($key in $items) {
        $values = @()
        foreach ($name in $key.GetValueNames()) { $values += @{ Name=$name; Kind=[int]$key.GetValueKind($name); Value=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } }
        $records += @{ Relative=$key.Name.Substring($baseName.Length).TrimStart('\'); Values=$values }
    }
    return $records
}
function Restore-Key($snapshot) {
    $path = $snapshot.Path
    if (Test-Path -LiteralPath $path) {
        $properties = Get-ItemProperty -LiteralPath $path
        $loaderProperty = $properties.PSObject.Properties['LOADER']
        $loader = if ($loaderProperty) { [string]$loaderProperty.Value } else { '' }
        $oldLoader = $snapshot.Loader
        $expected = if ($isAcad) { (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\小阳CAD轮盘插件.bundle') + '\' } else { $newBundle.TrimEnd('\') + '\' }
        if ($loader -and $loader -ne $oldLoader -and -not $loader.StartsWith($expected,[StringComparison]::OrdinalIgnoreCase)) { throw ('检测到安装期间手动更改加载入口，未覆盖：' + $path) }
        # Only undo values that belonged to the snapshot or are written by this installer.
        foreach ($record in @(Read-Key $path)) {
            $knownRecord= -not $record.Relative -or (-not $isAcad -and @('Commands','Groups') -contains $record.Relative)
            foreach ($oldRecord in @($snapshot.Records)) { if ($oldRecord -and $oldRecord.Relative -eq $record.Relative) { $knownRecord=$true } }
            if (-not $knownRecord) { throw ('注册出现额外子项，未覆盖：' + $path + '\' + $record.Relative) }
            foreach ($value in $record.Values) {
                $unchanged=$false
                foreach ($oldRecord in @($snapshot.Records)) {
                    if ($oldRecord -and $oldRecord.Relative -eq $record.Relative) {
                        foreach ($oldValue in $oldRecord.Values) {
                            if ($oldValue.Name -eq $value.Name -and $oldValue.Kind -eq $value.Kind -and (@($oldValue.Value) -join [char]0) -ceq (@($value.Value) -join [char]0)) { $unchanged=$true }
                        }
                    }
                }
                if ($unchanged) { continue }
                $allowed=$false
                if (-not $record.Relative) {
                    if ($value.Name -eq 'LOADER' -and [string]$value.Value -eq $loader) { $allowed=$true }
                    if ($value.Name -eq 'LOADCTRLS' -and [int]$value.Value -eq $(if ($isAcad) {2} else {14})) { $allowed=$true }
                    if ($value.Name -eq 'MANAGED' -and [int]$value.Value -eq 1) { $allowed=$true }
                    if ($value.Name -eq 'DESCRIPTION' -and [string]$value.Value -eq $(if ($isAcad) {'小阳CAD轮盘插件'} else {'XiaoYang CAD radial menu for ZWCAD'})) { $allowed=$true }
                } elseif (-not $isAcad) {
                    if ($record.Relative -eq 'Commands' -and @('XYLP','XYMENU','XYLPSZ','XYSETTINGS','XYLPIME','XYLPIMETOGGLE','XYMERGEDWG','XYREFRESHCMDS','XYRESTORELOCALCMDS','XYLPMENU','XYLPSTART','XYLPZJ','XYSELFTEST') -contains $value.Name -and [string]$value.Value -eq $value.Name) { $allowed=$true }
                    if ($record.Relative -eq 'Groups' -and $value.Name -eq 'XYCADMENU' -and [string]$value.Value -eq 'XYCADMENU') { $allowed=$true }
                }
                if (-not $allowed) { throw ('注册内容出现其他修改，未覆盖：' + $path + '\' + $record.Relative + ' / ' + $value.Name) }
            }
        }
        Remove-Item -LiteralPath $path -Recurse -Force
    }
    if ($null -ne $snapshot.Records) {
        foreach ($record in $snapshot.Records) {
            $destination = if ($record.Relative) { $path + '\' + $record.Relative } else { $path }
            New-Item -Path $destination -Force | Out-Null
            $key = Get-Item -LiteralPath $destination
            foreach ($value in $record.Values) { $key.SetValue($value.Name,$value.Value,[Microsoft.Win32.RegistryValueKind]$value.Kind) }
            $key.Close()
        }
    }
}
function Resolve-LinkTarget([string]$path) {
    $item = Get-Item -LiteralPath $path -Force
    if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) { throw ('旧安装不是可恢复的目录联接，请手动更新：' + $path) }
    if (-not ('XiaoYangUpdateLink' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class XiaoYangUpdateLink {
    [DllImport(""kernel32.dll"", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFile(string n,uint a,uint s,IntPtr p,uint c,uint f,IntPtr t);
    [DllImport(""kernel32.dll"", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle h,StringBuilder b,uint n,uint f);
    public static string Resolve(string p) {
        using(var h=CreateFile(p,0,7,IntPtr.Zero,3,0x02000000,IntPtr.Zero)) {
            if(h.IsInvalid) throw new System.ComponentModel.Win32Exception();
            var b=new StringBuilder(32768);
            uint length=GetFinalPathNameByHandle(h,b,(uint)b.Capacity,0);
            if(length==0 || length>=b.Capacity) throw new System.ComponentModel.Win32Exception();
            string r=b.ToString();
            if(r.StartsWith(@""\\?\UNC\"")) return @""\\""+r.Substring(8);
            return r.StartsWith(@""\\?\"") ? r.Substring(4) : r;
        }
    }
}
'@
    }
    [XiaoYangUpdateLink]::Resolve($path).TrimEnd('\')
}
function Restore-Link($link) {
    if (Test-Path -LiteralPath $link.Path) {
        $current = Resolve-LinkTarget $link.Path
        if ($current -ne $link.Target -and $current -ne $newBundle.TrimEnd('\')) { throw ('安装期间目录链接被手动修改，未覆盖：' + $link.Path) }
        [IO.Directory]::Delete($link.Path)
    }
    if ($link.Target) { New-DirectoryJunctionCompatible -Path $link.Path -Target $link.Target }
}
function Prepare-Transaction {
    Assert-Payload
    if (-not (Test-Path -LiteralPath (Join-Path $package 'install.ps1')) -or -not (Test-Path -LiteralPath $newBundle)) { throw '更新包结构不完整。' }
    # A fresh validated package must not introduce default runtime files over user data.
    foreach ($name in @('Data','Logs')) {
        $root = Join-Path $newBundle $name
        if ((Test-Path -LiteralPath $root) -and @(Get-ChildItem -LiteralPath $root -Recurse -Force).Count -gt 0) { throw '更新包包含已有 Data/Logs 数据，请重新下载。' }
    }
    $oldBundles = @()
    foreach ($path in @(Get-ProductKeys)) {
        $records = Read-Key $path
        $loader = ''
        if ($null -ne $records) {
            $props = Get-ItemProperty -LiteralPath $path
            $property = $props.PSObject.Properties['LOADER']
            if ($property) { $loader = [string]$property.Value }
        }
        $script:registrySnapshots += @{ Path=$path; Records=$records; Loader=$loader }
        if (-not $isAcad -and $loader) {
            if (-not (Test-Path -LiteralPath $loader)) { throw ('旧加载入口已失效，无法安全更新：' + $loader) }
            if ([version](Get-Item -LiteralPath $loader).VersionInfo.FileVersion -gt $version) { throw '已安装更高版本，已取消更新。' }
            $parent = Split-Path -Parent $loader
            while ($parent -and (Split-Path -Leaf $parent) -notlike '*.bundle') { $parent = Split-Path -Parent $parent }
            if (-not $parent -or (Split-Path -Leaf $parent) -ne $bundleName) { throw '旧加载入口不属于本产品，拒绝改写。' }
            $oldBundles += $parent
        }
    }
    if ($isAcad) {
        foreach ($name in @($bundleName,'XY-CAD-H.bundle')) {
            $path = Join-Path $env:APPDATA ('Autodesk\ApplicationPlugins\' + $name)
            $target = $null
            if (Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue) {
                $target = Resolve-LinkTarget $path
                if (-not (Test-Path -LiteralPath $target)) { throw '旧安装联接目标失效。' }
                $oldPackageXml = [xml][IO.File]::ReadAllText((Join-Path $target 'PackageContents.xml'))
                if ($oldPackageXml.ApplicationPackage.AutodeskProduct -ne 'AutoCAD') { throw '旧安装不属于 AutoCAD 产品。' }
                if ([version]$oldPackageXml.ApplicationPackage.AppVersion -gt $version) { throw '已安装更高版本，已取消更新。' }
                $oldBundles += $target
            }
            $script:links += @{ Path=$path; Target=$target }
        }
    }
    if ($oldBundles.Count -eq 0) { throw '找不到可安全恢复的已安装产品，请使用普通安装程序。' }
    $merged = @{}
    foreach ($bundle in @($oldBundles | Sort-Object -Unique)) {
        if ([IO.Path]::GetFullPath($bundle).TrimEnd('\') -eq $newBundle.TrimEnd('\')) { throw '更新目标与旧安装目录相同。' }
        $manifest = Get-DataManifest $bundle
        foreach ($relative in $manifest.Keys) {
            if ($merged.ContainsKey($relative) -and $merged[$relative] -ne $manifest[$relative]) { throw ('多个旧安装的数据冲突，需手动处理：' + $relative) }
            $merged[$relative] = $manifest[$relative]
        }
        $backup = Join-Path $evidence ('data-backup-' + $script:dataSnapshots.Count)
        New-Item -ItemType Directory -Path $backup | Out-Null
        foreach ($name in @('Data','Logs')) {
            $source = Join-Path $bundle $name
            if (Test-Path -LiteralPath $source) { Copy-Item -LiteralPath $source -Destination $backup -Recurse -Force }
        }
        Assert-Data $backup $manifest
        $script:dataSnapshots += @{ Bundle=$bundle; Backup=$backup; Manifest=$manifest }
    }
    $script:preservedManifest=$merged
    @{ Registry=$script:registrySnapshots; Links=$script:links; Data=$script:dataSnapshots } | Export-Clixml -LiteralPath (Join-Path $evidence 'rollback-snapshot.xml') -Depth 30
}
$form = New-Object Windows.Forms.Form
$form.Text = '小阳轮盘一键更新'
$form.Size = New-Object Drawing.Size(580,220)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$label = New-Object Windows.Forms.Label
$label.Location = New-Object Drawing.Point(20,20)
$label.Size = New-Object Drawing.Size(530,100)
$label.Text = '更新已下载。请保存图纸并正常关闭本产品的所有 CAD 窗口，关闭后自动安装。可以取消；程序不会强制结束 CAD。'
$form.Controls.Add($label)
$button = New-Object Windows.Forms.Button
$button.Text = '取消更新'
$button.Location = New-Object Drawing.Point(420,130)
$button.Size = New-Object Drawing.Size(110,32)
$button.Add_Click({ $script:cancelled=$true; $form.Close() })
$form.Controls.Add($button)
$form.Add_FormClosing({ param($sender,$eventArgs) if ($script:committing) { $eventArgs.Cancel=$true } else { $script:cancelled=$true } })
try {
    $mutex = New-Object Threading.Mutex($false,('Global\XiaoYangWheelUpdate-' + $product))
    try { $ownsMutex=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $ownsMutex=$true }
    if (-not $ownsMutex) { throw '同一产品已有更新任务，请先完成或取消原任务。' }
    Assert-Payload
    . (Join-Path $package 'installer-compat.ps1')
    Write-State 'WaitingForHostExit' '等待用户正常关闭 CAD。'
    $form.Show()
    do {
        [Windows.Forms.Application]::DoEvents()
        if ($cancelled) { Write-State 'Cancelled' '用户取消，未修改安装。'; return }
        $running = @(Get-HostProcesses)
        if ($running.Count -gt 0) { Start-Sleep -Milliseconds 250 }
    } while ($running.Count -gt 0)
    $committing=$true
    $button.Enabled=$false
    $label.Text='正在备份并安装更新，请稍候。为保护配置和安装入口，完成前不能关闭此窗口。'
    [Windows.Forms.Application]::DoEvents()
    Prepare-Transaction
    if (@(Get-HostProcesses).Count -gt 0) { throw 'CAD 已重新启动，未执行安装；请关闭后重试。' }
    Write-State 'Installing' '配置和加载入口备份完成，正在安装。'
    $mutationStarted=$true
    $start=New-Object Diagnostics.ProcessStartInfo
    $start.FileName=Join-Path $PSHOME 'powershell.exe'
    $start.Arguments='-NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File ' + [char]34 + (Join-Path $package 'install.ps1') + [char]34
    if (-not $isAcad) { $start.Arguments += ' -Quiet' }
    $oldAutoConfirm=$env:XYCAD_INSTALLER_AUTOCONFIRM
    try {
        $env:XYCAD_INSTALLER_AUTOCONFIRM='1'
        $process=Start-Process -FilePath $start.FileName -ArgumentList $start.Arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $evidence 'installer-output.txt') -RedirectStandardError (Join-Path $evidence 'installer-error.txt')
        $null=$process.Handle
    } finally { $env:XYCAD_INSTALLER_AUTOCONFIRM=$oldAutoConfirm }
    while (-not $process.HasExited) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 100 }
    $process.WaitForExit()
    $exitCode=$process.ExitCode
    $process.Dispose()
    if ($exitCode -ne 0) { throw ('安装程序失败，退出码：' + $exitCode) }
    $newReport=Join-Path $newBundle 'Logs\install-report.json'
    if (-not (Test-Path -LiteralPath $newReport)) { throw '安装程序没有写入成功报告。' }
    $report=Read-JsonFile $newReport
    if ([version]$report.PackageVersion -ne $version) { throw '安装报告版本与更新版本不一致。' }
    if ($isAcad -and (Resolve-LinkTarget (Join-Path $env:APPDATA ('Autodesk\ApplicationPlugins\' + $bundleName))) -ne $newBundle.TrimEnd('\')) { throw 'AutoCAD 安装联接未指向本次更新目录。' }
    $verifiedLoaders=0
    foreach ($key in @(Get-ProductKeys)) {
        if (-not (Test-Path -LiteralPath $key)) { continue }
        $loader=(Get-ItemProperty -LiteralPath $key).LOADER
        $expectedLoaderRoot=if ($isAcad) { (Join-Path $env:APPDATA ('Autodesk\ApplicationPlugins\' + $bundleName)) + '\' } else { $newBundle.TrimEnd('\') + '\' }
        if (-not ([string]$loader).StartsWith($expectedLoaderRoot,[StringComparison]::OrdinalIgnoreCase)) { throw ('实际加载入口没有指向本次更新：' + $key) }
        if (-not (Test-Path -LiteralPath $loader) -or [version](Get-Item -LiteralPath $loader).VersionInfo.FileVersion -ne $version) { throw ('实际注册内核版本验证失败：' + $key) }
        $verifiedLoaders++
    }
    if ($verifiedLoaders -eq 0) { throw '未找到更新后的有效加载入口。' }
    Copy-Item -LiteralPath $newReport -Destination (Join-Path $evidence 'new-install-report.json')
    foreach ($snapshot in $dataSnapshots) {
        Assert-Data $snapshot.Bundle $snapshot.Manifest
        if ($snapshot.Manifest.ContainsKey('Logs\install-report.json')) {
            Copy-Item -LiteralPath (Join-Path $snapshot.Backup 'Logs\install-report.json') -Destination $newReport -Force
        }
    }
    if (-not $preservedManifest.ContainsKey('Logs\install-report.json')) { Remove-Item -LiteralPath $newReport -Force }
    Assert-Data $newBundle $preservedManifest
    Write-State 'Succeeded' '安装完成；下次正常启动 CAD 后加载新版本。'
    [void][Windows.Forms.MessageBox]::Show(('更新安装完成。请重新启动 CAD。更新目录是长期安装位置，请勿删除：' + [Environment]::NewLine + $package),'小阳轮盘更新')
} catch {
    $failure=$_.Exception.Message
    $rollbackErrors=@()
    if ($mutationStarted -and @(Get-HostProcesses).Count -gt 0) {
        $rollbackErrors += 'CAD 在安装期间重新启动。已保留现场与恢复快照，请正常关闭 CAD 后依据 rollback-snapshot.xml 恢复本产品入口；更新器没有修改正在运行的宿主。'
    } elseif ($mutationStarted) {
        # Old bundle data is never edited by this updater; do not overwrite concurrent user edits.
        foreach ($snapshot in $dataSnapshots) { try { Assert-Data $snapshot.Bundle $snapshot.Manifest } catch { $rollbackErrors += $_.Exception.Message } }
        foreach ($link in $links) { try { Restore-Link $link } catch { $rollbackErrors += $_.Exception.Message } }
        foreach ($snapshot in $registrySnapshots) { try { Restore-Key $snapshot } catch { $rollbackErrors += $_.Exception.Message } }
    }
    $message=$failure
    if ($rollbackErrors.Count -gt 0) { $message += [Environment]::NewLine + '自动恢复未完全成功；为避免覆盖手动更改已保留现场：' + ($rollbackErrors -join '; ') }
    elseif ($mutationStarted) { $message += [Environment]::NewLine + '已恢复原加载入口，原安装与配置保留。' }
    Write-State $(if ($rollbackErrors.Count -gt 0) { 'RecoveryRequired' } else { 'Failed' }) $message
    [void][Windows.Forms.MessageBox]::Show(($message + [Environment]::NewLine + '恢复证据：' + $evidence),'小阳轮盘更新失败')
} finally {
    $committing=$false
    $form.Dispose()
    if ($ownsMutex) { $mutex.ReleaseMutex() }
    if ($mutex) { $mutex.Dispose() }
}
";
    }
}
