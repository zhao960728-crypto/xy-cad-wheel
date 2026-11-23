$ErrorActionPreference = 'Stop'
$net2027Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.AutoCAD2027\XiaoYang.AutoCADPlugin.AutoCAD2027.csproj'
$net2026Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin\XiaoYang.AutoCADPlugin.csproj'
$net2025Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.AutoCAD2025\XiaoYang.AutoCADPlugin.AutoCAD2025.csproj'
$net2020Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.AutoCAD2020\XiaoYang.AutoCADPlugin.AutoCAD2020.csproj'
$net2019Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.AutoCAD2019\XiaoYang.AutoCADPlugin.AutoCAD2019.csproj'
$net2018Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.AutoCAD2018\XiaoYang.AutoCADPlugin.AutoCAD2018.csproj'
$net2017Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.AutoCAD2017\XiaoYang.AutoCADPlugin.AutoCAD2017.csproj'
$net2016Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.AutoCAD2016\XiaoYang.AutoCADPlugin.AutoCAD2016.csproj'
$net2015Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.AutoCAD2015\XiaoYang.AutoCADPlugin.AutoCAD2015.csproj'
$net2014Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.AutoCAD2014\XiaoYang.AutoCADPlugin.AutoCAD2014.csproj'
$net48Project = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.Net48\XiaoYang.AutoCADPlugin.Net48.csproj'
$version = '2.2.0.84'
$assemblyVersion = [version]'2.2.0.84'
$productName = -join @(0x5C0F,0x9633,0x43,0x41,0x44,0x8F6E,0x76D8,0x63D2,0x4EF6 | ForEach-Object { [char]$_ })
$releaseBundleName = "$productName.bundle"
$contactSource = Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin\Resources'
$contactImageNames = @('抖音.png', '加我.png')

function Copy-ReleaseContents([string]$source, [string]$destination) {
    # Keep symbols available in bin/ and the developer bundle; omit them only
    # from the customer payload, including symbols left by earlier builds.
    $sourceRoot = [IO.Path]::GetFullPath($source).TrimEnd('\') + '\'
    foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
        if ($file.Extension -in @('.pdb', '.dbg', '.mdb')) { continue }
        $target = Join-Path $destination $file.FullName.Substring($sourceRoot.Length)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        [IO.File]::Copy($file.FullName, $target, $true)
    }
}

function Assert-ContactResources([string]$bundlePath) {
    $contactDirectory = Join-Path $bundlePath 'Contents\Resources\Contact'
    if (-not (Test-Path -LiteralPath $contactDirectory -PathType Container)) {
        throw "Shared contact resource directory is missing: $contactDirectory"
    }
    $files = @(Get-ChildItem -LiteralPath $contactDirectory -Recurse -File -Force)
    if ($files.Count -ne $contactImageNames.Count) { throw "Expected exactly two shared contact images: $contactDirectory" }
    foreach ($name in $contactImageNames) {
        $original = Join-Path $contactSource $name
        $copy = Join-Path $contactDirectory $name
        if (-not (Test-Path -LiteralPath $copy -PathType Leaf) -or
            (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $copy -Algorithm SHA256).Hash) {
            throw "Shared contact image is missing or differs from the original: $copy"
        }
        $duplicates = @(Get-ChildItem -LiteralPath $bundlePath -Recurse -File -Filter $name)
        if ($duplicates.Count -ne 1) { throw "Shared contact image must occur only once per product bundle: $name" }
    }
}

foreach ($legacyYear in @(2010, 2011, 2012, 2013)) {
    $legacyProject = Join-Path $PSScriptRoot ("src\XiaoYang.AutoCADPlugin.AutoCAD$legacyYear\XiaoYang.AutoCADPlugin.AutoCAD$legacyYear.csproj")
    dotnet build $legacyProject -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
dotnet build (Join-Path $PSScriptRoot 'src\XiaoYang.AutoCADPlugin.LegacyUpdater\XiaoYang.AutoCADPlugin.LegacyUpdater.csproj') -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2027Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2026Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2025Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2020Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2019Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2018Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2017Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2016Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2015Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net2014Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $net48Project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$bundleContents = Join-Path $PSScriptRoot 'packaging\XY-CAD-H.bundle\Contents'
$contactDirectory = Join-Path $bundleContents 'Resources\Contact'
New-Item -ItemType Directory -Force -Path $contactDirectory | Out-Null
foreach ($name in $contactImageNames) {
    $original = Join-Path $contactSource $name
    if (-not (Test-Path -LiteralPath $original -PathType Leaf)) { throw "Original contact image is missing: $original" }
    [IO.File]::Copy($original, (Join-Path $contactDirectory $name), $true)
}
Assert-ContactResources (Split-Path -Parent $bundleContents)
$bundleContentsRoot = [IO.Path]::GetFullPath($bundleContents).TrimEnd('\') + '\'
foreach ($legacyRuntimeFolder in @('2014', '2015', '2016', '2017', '2018', '2019', '2020', '2021-2024', '2025', '2026', '2027')) {
    $legacyRuntimePath = [IO.Path]::GetFullPath((Join-Path $bundleContents $legacyRuntimeFolder))
    if (-not $legacyRuntimePath.StartsWith($bundleContentsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Legacy runtime cleanup escaped bundle Contents: $legacyRuntimePath"
    }
    if (Test-Path -LiteralPath $legacyRuntimePath) { Remove-Item -LiteralPath $legacyRuntimePath -Recurse -Force }
}
$menuTemplate = Join-Path $PSScriptRoot 'packaging\menu-source\XYCADMENU.utf8.mnu'
$menuDirectory = Join-Path $bundleContents 'Menu'
$menuSource = Join-Path $menuDirectory 'XYCADMENU.mnu'
if (-not (Test-Path -LiteralPath $menuTemplate)) { throw "Menu template is missing: $menuTemplate" }
New-Item -ItemType Directory -Force -Path $menuDirectory | Out-Null
$menuText = [IO.File]::ReadAllText($menuTemplate, [Text.Encoding]::UTF8)
[IO.File]::WriteAllText($menuSource, $menuText, [Text.Encoding]::GetEncoding(936))

$requiredDlls = @(
    (Join-Path $bundleContents 'NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2010.dll'),
    (Join-Path $bundleContents 'NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2011.dll'),
    (Join-Path $bundleContents 'NetFramework35\MrZ.XiaoYangRadialMenu.AutoCAD2012.dll'),
    (Join-Path $bundleContents 'NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2013.dll'),
    (Join-Path $bundleContents 'NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2014.dll'),
    (Join-Path $bundleContents 'NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2015.dll'),
    (Join-Path $bundleContents 'NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2016.dll'),
    (Join-Path $bundleContents 'NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2017.dll'),
    (Join-Path $bundleContents 'NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2018.dll'),
    (Join-Path $bundleContents 'NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2019.dll'),
    (Join-Path $bundleContents 'NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2020.dll'),
    (Join-Path $bundleContents 'NetFramework48\MrZ.XiaoYangRadialMenu.AutoCAD2021-2024.dll'),
    (Join-Path $bundleContents 'Net8\MrZ.XiaoYangRadialMenu.AutoCAD2025.dll'),
    (Join-Path $bundleContents 'Net8\MrZ.XiaoYangRadialMenu.AutoCAD2026.dll'),
    (Join-Path $bundleContents 'Net10\MrZ.XiaoYangRadialMenu.AutoCAD2027.dll')
)
$expectedDllVersion = $assemblyVersion
foreach ($dll in $requiredDlls) {
    if (-not (Test-Path -LiteralPath $dll)) { throw "Required runtime DLL was not built: $dll" }
    $dllVersion = [version](Get-Item -LiteralPath $dll).VersionInfo.FileVersion
    if ($dllVersion -ne $expectedDllVersion) {
        throw "Stale runtime DLL version ${dllVersion}: $dll"
    }
}
$legacyCadUnfoldDirectory = Join-Path $bundleContents 'Features\CadUnfold'
if (Test-Path -LiteralPath $legacyCadUnfoldDirectory) {
    throw "Legacy CadUnfold payload must not be present in the radial-wheel package: $legacyCadUnfoldDirectory"
}
$packageManifest = Join-Path $PSScriptRoot 'packaging\XY-CAD-H.bundle\PackageContents.xml'
if ([IO.File]::ReadAllText($packageManifest) -match 'XYUNFOLD|CadUnfold') {
    throw 'Legacy unfold command registration remains in PackageContents.xml.'
}
if (-not (Test-Path -LiteralPath $menuSource) -or (Get-Item -LiteralPath $menuSource).Length -lt 100) {
    throw 'The version-native AutoCAD partial menu source was not generated.'
}

$usageGuideItem = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'packaging') -Filter '*.txt' -File |
    Select-Object -First 1
if (-not $usageGuideItem) { throw 'The Chinese usage guide source file is missing.' }
$usageGuide = $usageGuideItem.FullName
$guideName = $usageGuideItem.Name
$bundleGuide = Join-Path $PSScriptRoot (Join-Path 'packaging\XY-CAD-H.bundle' $guideName)
[IO.File]::Copy($usageGuide, $bundleGuide, $true)

$dist = Join-Path $PSScriptRoot 'dist'
$staging = Join-Path $dist "$productName-$version"
$archive = Join-Path $dist "$productName-$version.zip"
if (Test-Path -LiteralPath $archive) {
    throw "Release $version already exists. Increment the four-part version before rebuilding: $archive"
}
$distFull = [IO.Path]::GetFullPath($dist).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$stagingFull = [IO.Path]::GetFullPath($staging)
if (-not $stagingFull.StartsWith($distFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a staging path outside dist: $stagingFull"
}
$existingStagingBundle = Join-Path $staging $releaseBundleName
foreach ($runtimeFolder in @('Data', 'Logs')) {
    $runtimePath = Join-Path $existingStagingBundle $runtimeFolder
    if (Test-Path -LiteralPath $runtimePath) {
        throw "Refusing to rebuild an installed/staged release that contains $runtimeFolder. Increment the four-part version before rebuilding so user configuration and logs cannot be deleted: $runtimePath"
    }
}
if (Test-Path -LiteralPath $staging) {
    # Keep the staging root itself. It can legitimately be the target of the
    # ApplicationPlugins junction or the current folder of an open Explorer /
    # installer window; deleting only its children still produces a clean build.
    Get-ChildItem -LiteralPath $staging -Force | Remove-Item -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $staging | Out-Null
$sourceBundle = Join-Path $PSScriptRoot 'packaging\XY-CAD-H.bundle'
$stagingBundle = Join-Path $staging $releaseBundleName
New-Item -ItemType Directory -Force -Path $stagingBundle | Out-Null
Copy-ReleaseContents (Join-Path $sourceBundle 'Contents') (Join-Path $stagingBundle 'Contents')
Assert-ContactResources $stagingBundle
Copy-Item -LiteralPath (Join-Path $sourceBundle 'PackageContents.xml') -Destination $stagingBundle -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\update-backup.json') -Destination $stagingBundle -Force
[IO.File]::Copy($usageGuide, (Join-Path $stagingBundle $guideName), $true)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\install.ps1') -Destination $staging -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\installer-compat.ps1') -Destination $staging -Force
foreach ($helper in @('autocad-selection.ps1', 'autocad-install-transaction.ps1', 'installer-ui.ps1', 'installer-plan.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot (Join-Path 'packaging' $helper)) -Destination $staging -Force
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\installer-zh-CN.json') -Destination $staging -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\uninstall.ps1') -Destination $staging -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging\self-test.ps1') -Destination $staging -Force
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'packaging') -Filter '*.cmd' -File |
    Copy-Item -Destination $staging -Force

# These scripts run on customer machines, including Windows 7 / PowerShell 2.0.
# Reject syntax and cmd line endings that the old engine cannot consume.
$customerPowerShellScripts = @(
    (Join-Path $staging 'installer-compat.ps1'),
    (Join-Path $staging 'autocad-selection.ps1'),
    (Join-Path $staging 'autocad-install-transaction.ps1'),
    (Join-Path $staging 'installer-ui.ps1'),
    (Join-Path $staging 'installer-plan.ps1'),
    (Join-Path $staging 'install.ps1'),
    (Join-Path $staging 'self-test.ps1'),
    (Join-Path $staging 'uninstall.ps1')
)
$powerShell2Forbidden = '\$PSScriptRoot|\[pscustomobject\]|\[ordered\]|\bConvert(To|From)-Json\b|\bGet-FileHash\b|Get-Content[^\r\n]*-Raw|Get-ChildItem[^\r\n]*-(File|Directory)|Where-Object\s+(?!\{)[A-Za-z_]|ForEach-Object\s+(?!\{)[A-Za-z_]|IsNullOrWhiteSpace|\s-(in|notin)\s|::new\('
foreach ($script in $customerPowerShellScripts) {
    $scriptText = [IO.File]::ReadAllText($script)
    if ($scriptText -match $powerShell2Forbidden) {
        throw "Customer script contains PowerShell 3+ syntax: $script"
    }
}
foreach ($cmdFile in Get-ChildItem -LiteralPath $staging -Filter '*.cmd') {
    $bytes = [IO.File]::ReadAllBytes($cmdFile.FullName)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -eq 10 -and ($index -eq 0 -or $bytes[$index - 1] -ne 13)) {
            throw "One-click script must use CRLF line endings: $($cmdFile.FullName)"
        }
    }
}
$templateName = -join @(0x6279,0x91CF,0x5BFC,0x5165,0x43,0x41,0x44,0x5FEB,0x6377,0x952E,0x6A21,0x677F,0x2E,0x78,0x6C,0x73,0x78 | ForEach-Object { [char]$_ })
$template = Join-Path $PSScriptRoot (Join-Path 'packaging' $templateName)
& (Join-Path $PSScriptRoot 'packaging\create-command-template.ps1') -OutputPath $template
Copy-Item -LiteralPath $template -Destination $staging -Force
$stagingGuide = Join-Path $staging $guideName
[IO.File]::Copy($usageGuide, $stagingGuide, $true)
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $staging -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE'), (Join-Path $PSScriptRoot 'LICENSE-SOURCE.txt') -Destination $staging -Force
if (-not (Test-Path -LiteralPath $bundleGuide) -or -not (Test-Path -LiteralPath $stagingGuide)) {
    throw 'The usage guide was not included in the release package.'
}
foreach ($forbiddenFolder in @('Data', 'Logs')) {
    if (Test-Path -LiteralPath (Join-Path $stagingBundle $forbiddenFolder)) {
        throw "Runtime/test data must not be packaged: $forbiddenFolder"
    }
}
$forbiddenFiles = @(Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object {
    $_.FullName -match '\\(Data|Logs|work|tests)(\\|$)' -or
    $_.Extension -in @('.pdb', '.dbg', '.mdb', '.log', '.xylpbackup') -or
    $_.Name -match '^(install-report|errors|autocad-plugin)\.json$' -or
    $_.Name -match 'test-fixture|runtime-command-bridge|uipreview|test-data|sample-data'
})
if ($forbiddenFiles.Count -gt 0) {
    throw ('Release package contains forbidden runtime/test/user data: ' + (($forbiddenFiles | ForEach-Object FullName) -join '; '))
}
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    $entries = @($zip.Entries | ForEach-Object FullName)
    if ($entries | Where-Object {
        $_ -match '(^|/)(Data|Logs|work|tests)(/|$)' -or
        $_ -match '\.(pdb|dbg|mdb|log|xylpbackup)$' -or
        $_ -match '(install-report|errors|autocad-plugin)\.json$' -or
        $_ -match 'test-fixture|runtime-command-bridge|uipreview|test-data|sample-data'
    }) {
        throw 'The release archive contains runtime or test data.'
    }
    if (@($entries | Where-Object { [IO.Path]::GetExtension($_) -eq '.txt' }).Count -lt 2) {
        throw 'The release archive does not contain the usage guide.'
    }
    foreach ($name in $contactImageNames) {
        $entryName = "$releaseBundleName/Contents/Resources/Contact/$name"
        $images = @($zip.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $entryName })
        if ($images.Count -ne 1) { throw "The release archive is missing a shared contact image: $entryName" }
        $stream = $images[0].Open()
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actualHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($actualHash -ne (Get-FileHash -LiteralPath (Join-Path $contactSource $name) -Algorithm SHA256).Hash) {
            throw "The archived contact image differs from the original: $entryName"
        }
    }
}
finally { $zip.Dispose() }

& (Join-Path $PSScriptRoot 'packaging\write-update-manifest.ps1') -Archive $archive -ProductId 'xiaoyang-autocad-wheel' -Version $version
& (Join-Path $PSScriptRoot 'packaging\build-installer.ps1') -Archive $archive -Version $version -Output (Join-Path $dist "$productName-一键安装-$version.exe")

Write-Host 'Build complete. AutoCAD APIs: R19.1 / R20.0 / R20.1 / R21.0 / R22.0 / R23.0 / R23.1 / R24.0 / R25.0 / R25.1 / R26.0; XYCADMENU uses a version-native partial CUIX.'
Write-Host "Bundle: packaging\XY-CAD-H.bundle"
Write-Host "Release: $archive"
