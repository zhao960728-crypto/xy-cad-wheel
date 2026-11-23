param(
    [string]$Configuration = 'Release',
    [switch]$SkipZip
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$version = '2.2.0.84'
$versionMatrix = @(
    [pscustomobject]@{ Year = 2020; Api = '20.20.0' },
    [pscustomobject]@{ Year = 2021; Api = '20.21.0' },
    [pscustomobject]@{ Year = 2022; Api = '20.22.0' },
    [pscustomobject]@{ Year = 2023; Api = '20.23.0' },
    [pscustomobject]@{ Year = 2024; Api = '20.24.0' },
    [pscustomobject]@{ Year = 2025; Api = '20.25.0' },
    [pscustomobject]@{ Year = 2026; Api = '20.26.0' }
)
$root = $PSScriptRoot
$project = Join-Path $root 'src\XiaoYang.ZWCADPlugin\XiaoYang.ZWCADPlugin.csproj'
$zwProductName = -join @(0x5C0F,0x9633,0x5A,0x57,0x43,0x41,0x44,0x8F6E,0x76D8,0x63D2,0x4EF6 | ForEach-Object { [char]$_ })
$bundle = Join-Path $root (Join-Path 'packaging' ($zwProductName + '.bundle'))
$installerTemplates = Join-Path $root 'packaging\zwcad-installer'
$menuSource = Join-Path $root 'packaging\XY-CAD-H.bundle\Contents\Menu\XYCADMENU.mnu'
$templateName = -join @(0x6279,0x91CF,0x5BFC,0x5165,0x43,0x41,0x44,0x5FEB,0x6377,0x952E,0x6A21,0x677F,0x2E,0x78,0x6C,0x73,0x78 | ForEach-Object { [char]$_ })
$templateWorkbook = Join-Path $root (Join-Path 'packaging' $templateName)
$distFolder = Join-Path $root (Join-Path 'dist' ($zwProductName + '-' + $version))
$zipPath = $distFolder + '.zip'
$contactSource = Join-Path $root 'src\XiaoYang.AutoCADPlugin\Resources'
$contactImageNames = @('抖音.png', '加我.png')
if (-not $SkipZip -and (Test-Path -LiteralPath $zipPath)) {
    throw "Release $version already exists. Increment the four-part version before rebuilding: $zipPath"
}

function Assert-WorkspacePath([string]$path) {
    $fullRoot = [IO.Path]::GetFullPath($root).TrimEnd('\') + '\'
    $fullPath = [IO.Path]::GetFullPath($path)
    if (-not $fullPath.StartsWith($fullRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to process a path outside the workspace: $fullPath"
    }
}

function Assert-NoRuntimeData([string]$bundlePath) {
    foreach ($runtimeFolder in @('Data', 'Logs')) {
        $runtimePath = Join-Path $bundlePath $runtimeFolder
        if (Test-Path -LiteralPath $runtimePath) {
            throw "Refusing to rebuild an installed/staged release that contains $runtimeFolder. Increment the four-part version before rebuilding so user configuration and logs cannot be deleted: $runtimePath"
        }
    }
}

function Copy-ReleaseContents([string]$source, [string]$destination) {
    # Preserve developer symbols in bin/ and packaging; filter only the release.
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

foreach ($required in @($project, $installerTemplates, $menuSource, $templateWorkbook)) {
    if (-not (Test-Path -LiteralPath $required)) { throw "Missing build input: $required" }
}

# Reject a stale installer before producing a package that cannot install.
$installerVersionMatch = [regex]::Match([IO.File]::ReadAllText((Join-Path $installerTemplates 'install.ps1')),
    '\$packageVersion\s*=\s*\[version\]\s*''(?<version>\d+\.\d+\.\d+\.\d+)''')
if (-not $installerVersionMatch.Success -or [version]$installerVersionMatch.Groups['version'].Value -ne [version]$version) {
    throw "ZWCAD installer version must match build version $version before packaging."
}

# Windows cmd.exe requires CRLF here. A previous UTF-8/LF-only entry script
# was split into malformed commands when users double-clicked the installer.
foreach ($cmdFile in Get-ChildItem -LiteralPath $installerTemplates -Filter '*.cmd' -File) {
    $bytes = [IO.File]::ReadAllBytes($cmdFile.FullName)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -eq 10 -and ($index -eq 0 -or $bytes[$index - 1] -ne 13)) {
            throw "One-click script must use CRLF line endings: $($cmdFile.FullName)"
        }
    }
}

$powerShell2Forbidden = '\$PSScriptRoot|\[pscustomobject\]|\[ordered\]|\bConvert(To|From)-Json\b|\bGet-FileHash\b|Get-Content[^\r\n]*-Raw|Get-ChildItem[^\r\n]*-(File|Directory)|Where-Object\s+(?!\{)[A-Za-z_]|ForEach-Object\s+(?!\{)[A-Za-z_]|IsNullOrWhiteSpace|\s-(in|notin)\s|::new\('
foreach ($scriptFile in Get-ChildItem -LiteralPath $installerTemplates -Filter '*.ps1' -File) {
    if ([IO.File]::ReadAllText($scriptFile.FullName) -match $powerShell2Forbidden) {
        throw "Customer script contains PowerShell 3+ syntax: $($scriptFile.FullName)"
    }
}

Assert-NoRuntimeData $bundle
Assert-NoRuntimeData (Join-Path $distFolder (Split-Path -Leaf $bundle))
foreach ($path in @($bundle, $distFolder)) {
    Assert-WorkspacePath $path
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}

New-Item -ItemType Directory -Path (Join-Path $bundle 'Contents\Menu') -Force | Out-Null
Copy-Item -LiteralPath $menuSource -Destination (Join-Path $bundle 'Contents\Menu\XYCADMENU.mnu') -Force
Copy-Item -LiteralPath (Join-Path $root 'packaging\update-backup.json') -Destination $bundle -Force

foreach ($target in $versionMatrix) {
    Write-Host ("Building ZWCAD {0} runtime (ZRX.NET API {1})..." -f $target.Year, $target.Api)
    & dotnet build $project -c $Configuration --nologo `
        -p:ZwcadYear=$($target.Year) -p:ZwcadApiVersion=$($target.Api)
    if ($LASTEXITCODE -ne 0) {
        throw "ZWCAD $($target.Year) runtime build failed with exit code: $LASTEXITCODE"
    }

    $assembly = Join-Path $bundle ("Contents\NetFramework48\ZWCAD{0}\MrZ.XiaoYangRadialMenu.ZWCAD{0}.dll" -f $target.Year)
    if (-not (Test-Path -LiteralPath $assembly)) { throw "Runtime DLL was not found after build: $assembly" }
    $assemblyVersion = [version](Get-Item -LiteralPath $assembly).VersionInfo.FileVersion
    if ($assemblyVersion -ne [version]$version) {
        throw "ZWCAD $($target.Year) runtime version mismatch. Expected $version, actual $assemblyVersion"
    }
}

$contactDirectory = Join-Path $bundle 'Contents\Resources\Contact'
New-Item -ItemType Directory -Force -Path $contactDirectory | Out-Null
foreach ($name in $contactImageNames) {
    $original = Join-Path $contactSource $name
    if (-not (Test-Path -LiteralPath $original -PathType Leaf)) { throw "Original contact image is missing: $original" }
    [IO.File]::Copy($original, (Join-Path $contactDirectory $name), $true)
}
Assert-ContactResources $bundle
New-Item -ItemType Directory -Path $distFolder -Force | Out-Null
$stagingBundle = Join-Path $distFolder (Split-Path -Leaf $bundle)
Copy-ReleaseContents $bundle $stagingBundle
Assert-ContactResources $stagingBundle
Get-ChildItem -LiteralPath $installerTemplates -File | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $distFolder $_.Name) -Force
}
Copy-Item -LiteralPath $templateWorkbook -Destination (Join-Path $distFolder (Split-Path -Leaf $templateWorkbook)) -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE'), (Join-Path $root 'LICENSE-SOURCE.txt') -Destination $distFolder -Force

$forbidden = @(Get-ChildItem -LiteralPath $distFolder -Recurse -File | Where-Object {
    $_.FullName -match '\\(Data|Logs|work|tests)(\\|$)' -or
    $_.Extension -in @('.pdb', '.dbg', '.mdb', '.log', '.xylpbackup') -or
    $_.Name -match '^(install-report|errors|autocad-plugin)\.json$' -or
    $_.Name -match 'self-test|test-fixture|test-data|sample-data' -or
    $_.Name -match 'runtime-command-bridge|uipreview'
})
if ($forbidden.Count -gt 0) {
    throw ('Release package contains forbidden runtime/test/user data: ' + (($forbidden | ForEach-Object FullName) -join '; '))
}

if (-not $SkipZip) {
    Compress-Archive -Path (Join-Path $distFolder '*') -DestinationPath $zipPath -CompressionLevel Optimal
    if (-not (Test-Path -LiteralPath $zipPath)) {
        throw "Release archive was not created: $zipPath"
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $forbiddenEntries = @($archive.Entries | Where-Object {
            $_.FullName -match '(^|/)(Data|Logs|work|tests)(/|$)' -or
            $_.Name -match 'self-test|test-fixture|test-data|sample-data|runtime-command-bridge|uipreview' -or
            $_.Name -match '\.(pdb|dbg|mdb|log|xylpbackup)$' -or
            $_.Name -match '^(install-report|errors|autocad-plugin)\.json$'
        })
        if ($forbiddenEntries.Count -gt 0) {
            throw ('Release archive contains forbidden runtime/test/user data: ' + (($forbiddenEntries | ForEach-Object FullName) -join '; '))
        }
        foreach ($name in $contactImageNames) {
            $entryName = (Split-Path -Leaf $bundle) + "/Contents/Resources/Contact/$name"
            $images = @($archive.Entries | Where-Object { $_.FullName.Replace('\', '/') -eq $entryName })
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
    finally {
        $archive.Dispose()
    }
    & (Join-Path $root 'packaging\write-update-manifest.ps1') -Archive $zipPath -ProductId 'xiaoyang-zwcad-wheel' -Version $version
}

Write-Host "ZWCAD multi-version package created: $distFolder"
if (-not $SkipZip) { Write-Host "Archive: $zipPath" }





