param([switch]$UseBuiltPackages)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '2.2.0.84'
$dist = Join-Path $root 'dist'
$output = Join-Path $dist ("小阳CAD轮盘插件-AutoCAD+中望-一键安装-$version.exe")
$contactSource = Join-Path $root 'src\XiaoYang.AutoCADPlugin\Resources'
$contactImageNames = @('抖音.png', '加我.png')

function Assert-ContactResources([string]$packagePath, [string]$bundleName) {
    $bundlePath = Join-Path $packagePath $bundleName
    $contactDirectory = Join-Path $bundlePath 'Contents\Resources\Contact'
    if (-not (Test-Path -LiteralPath $contactDirectory -PathType Container)) {
        throw "Shared contact resource directory is missing: $contactDirectory"
    }
    $files = @(Get-ChildItem -LiteralPath $contactDirectory -Recurse -File -Force)
    if ($files.Count -ne $contactImageNames.Count) { throw "Expected exactly two shared contact images: $contactDirectory" }
    foreach ($name in $contactImageNames) {
        $copy = Join-Path $contactDirectory $name
        if (-not (Test-Path -LiteralPath $copy -PathType Leaf) -or
            (Get-FileHash -LiteralPath (Join-Path $contactSource $name) -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash) {
            throw "Shared contact image is missing or differs from the original: $copy"
        }
        $duplicates = @(Get-ChildItem -LiteralPath $bundlePath -Recurse -File -Filter $name)
        if ($duplicates.Count -ne 1) { throw "Shared contact image must occur only once per product bundle: $name" }
    }
}

if (Test-Path -LiteralPath $output) { throw "已存在该版本 EXE，拒绝覆盖：$output" }
if (-not $UseBuiltPackages) {
    & (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'AutoCAD build failed.' }
    & (Join-Path $root 'build-zwcad.ps1')
    if ($LASTEXITCODE -ne 0) { throw 'ZWCAD build failed.' }
}
$work = Join-Path $root ('work\combined-installer-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
New-Item -ItemType Directory -Path $payload | Out-Null
foreach ($product in @('AutoCAD','ZWCAD')) {
    $name = if ($product -eq 'AutoCAD') { '小阳CAD轮盘插件' } else { '小阳ZWCAD轮盘插件' }
    $source = Join-Path $dist ("$name-$version")
    if (-not (Test-Path -LiteralPath $source)) { throw "缺少独立产品包：$source" }
    Assert-ContactResources $source ($name + '.bundle')
    $dlls = @(Get-ChildItem -LiteralPath $source -Filter 'MrZ.XiaoYangRadialMenu.*.dll' -Recurse -File)
    $count = if ($product -eq 'AutoCAD') { 15 } else { 7 }
    if ($dlls.Count -ne $count) { throw "$product 内核数量不正确。" }
    foreach ($dll in $dlls) {
        if ($dll.Name -notlike 'MrZ.XiaoYangRadialMenu.*.dll' -or [version]$dll.VersionInfo.FileVersion -ne [version]$version) {
            throw "不允许的内核或版本：$($dll.FullName)"
        }
    }
    $otherDlls = @(Get-ChildItem -LiteralPath $source -Filter '*.dll' -Recurse -File | Where-Object { $_.Name -notlike 'MrZ.XiaoYangRadialMenu.*.dll' })
    if ($product -eq 'AutoCAD') {
        $legacyRoot = Join-Path $source '小阳CAD轮盘插件.bundle\Contents\NetFramework35'
        if ($otherDlls.Count -ne 1 -or $otherDlls[0].FullName -ne (Join-Path $legacyRoot 'Theraot.Core.dll')) { throw 'Unexpected AutoCAD runtime dependencies.' }
        if (-not (Test-Path -LiteralPath (Join-Path $legacyRoot 'XYCAD.LegacyUpdater.exe')) -or
            [version](Get-Item -LiteralPath (Join-Path $legacyRoot 'XYCAD.LegacyUpdater.exe')).VersionInfo.FileVersion -ne [version]$version) { throw 'Legacy updater is missing or stale.' }
    } elseif ($otherDlls.Count -gt 0) { throw 'Unexpected ZWCAD runtime dependencies.' }
    $forbidden = @(Get-ChildItem -LiteralPath $source -Recurse -Force | Where-Object {
        $_.FullName -match '\\(Data|Logs|work|tests)(\\|$)' -or
        $_.Extension -in @('.pdb', '.dbg', '.mdb', '.log', '.xylpbackup') -or
        $_.Name -match '^(install-report|errors|autocad-plugin)\.json$' -or
        $_.Name -match 'test-fixture|runtime-command-bridge|uipreview|test-data|sample-data' -or
        $_.Name -eq 'install-selection.json' -or $_.Name -like 'install-recovery-*'
    })
    if ($forbidden.Count -gt 0) { throw "产品包包含用户数据或安装现场：$source" }
    Copy-Item -LiteralPath $source -Destination (Join-Path $payload $product) -Recurse
    Assert-ContactResources (Join-Path $payload $product) ($name + '.bundle')
}
foreach ($file in @('installer-ui.ps1','installer-plan.ps1','installer-elevated.ps1','installer-compat.ps1','autocad-selection.ps1')) {
    Copy-Item -LiteralPath (Join-Path $root ('packaging\' + $file)) -Destination $payload
}
Copy-Item -LiteralPath (Join-Path $root 'docs\EXE安装器.md') -Destination (Join-Path $payload '安装说明.md')
& (Join-Path $root 'tests\powershell2-package-parse.ps1') -PackageRoot $payload
$archive = Join-Path $work 'payload.zip'
Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $archive -CompressionLevel Optimal
Add-Type -AssemblyName System.IO.Compression.FileSystem
$payloadArchive = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $forbiddenEntries = @($payloadArchive.Entries | Where-Object {
        $_.FullName -match '(^|/)(Data|Logs|work|tests)(/|$)' -or
        $_.Name -match '\.(pdb|dbg|mdb|log|xylpbackup)$' -or
        $_.Name -match '^(install-report|errors|autocad-plugin)\.json$' -or
        $_.Name -match 'test-fixture|runtime-command-bridge|uipreview|test-data|sample-data' -or
        $_.Name -eq 'install-selection.json' -or $_.Name -like 'install-recovery-*'
    })
    if ($forbiddenEntries.Count -gt 0) { throw 'Combined installer payload contains debug, runtime, test or user data.' }
    # Each standalone product needs its own shared pair for independent installs.
    foreach ($product in @('AutoCAD', 'ZWCAD')) {
        $bundleName = if ($product -eq 'AutoCAD') { '小阳CAD轮盘插件.bundle' } else { '小阳ZWCAD轮盘插件.bundle' }
        foreach ($name in $contactImageNames) {
            $entryName = "$product/$bundleName/Contents/Resources/Contact/$name"
            $images = @($payloadArchive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $entryName })
            if ($images.Count -ne 1) { throw "Combined installer is missing a shared contact image: $entryName" }
            $stream = $images[0].Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $actualHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $sha.Dispose() }
            if ($actualHash -ne (Get-FileHash -LiteralPath (Join-Path $contactSource $name) -Algorithm SHA256).Hash) {
                throw "The combined installer contact image differs from the original: $entryName"
            }
        }
    }
}
finally { $payloadArchive.Dispose() }
& (Join-Path $root 'packaging\build-installer.ps1') -Archive $archive -Version $version -Output $output
Write-Host "Combined EXE: $output"
Write-Host "Payload for verification: $payload"
