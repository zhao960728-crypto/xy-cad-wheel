param([string]$WorkspaceRoot)

$ErrorActionPreference = 'Stop'
if (-not $WorkspaceRoot) { throw 'WorkspaceRoot is required.' }

$helper = Join-Path $WorkspaceRoot 'packaging\installer-compat.ps1'
$stringsFile = Join-Path $WorkspaceRoot 'packaging\installer-zh-CN.json'
. $helper

$strings = Read-JsonFile $stringsFile
if (-not $strings.Title -or $strings.Title -notmatch 'CAD') { throw 'PowerShell 2 JSON string parsing failed.' }

$testRoot = Join-Path $env:TEMP ('xylp-ps2-' + [Guid]::NewGuid().ToString('N'))
$target = Join-Path $testRoot 'target'
$junction = Join-Path $testRoot 'junction'
$replacement = Join-Path $testRoot 'replacement'
$repairTarget = Join-Path $testRoot 'repair-target'
$repairJunction = Join-Path $testRoot 'repair-junction'
$folderRegistration = Join-Path $testRoot 'folder-registration'
$folderReplacement = Join-Path $testRoot 'folder-replacement'
$unsafeTarget = Join-Path $testRoot 'unsafe-target'
$unsafeRegistration = Join-Path $testRoot 'unsafe-registration'
$unsafeDestination = Join-Path $testRoot 'unsafe-destination'
$unsafeExternal = Join-Path $testRoot 'unsafe-external'
$reportPath = Join-Path $testRoot 'report.json'
try {
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $hashInput = Join-Path $target 'hash.txt'
    [IO.File]::WriteAllText($hashInput, 'abc', [Text.Encoding]::ASCII)
    if ((Get-Sha256Hash $hashInput) -ne 'ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad') {
        throw 'PowerShell 2 SHA256 verification failed.'
    }
    New-DirectoryJunctionCompatible -Path $junction -Target $target
    if (-not (Test-Path -LiteralPath $junction)) { throw 'PowerShell 2 junction creation failed.' }
    New-Item -ItemType Directory -Path (Join-Path $target 'Data\Empty') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $target 'Logs') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $target 'Data\config.json'), '{"value":"鐢ㄦ埛閰嶇疆"}', [Text.Encoding]::UTF8)
    [IO.File]::WriteAllBytes((Join-Path $target 'Logs\binary.bin'), [byte[]](0, 1, 127, 255))
    $migrationManifest = Get-RuntimeDataManifest $junction
    New-Item -ItemType Directory -Path $replacement -Force | Out-Null
    Remove-RegistrationPreservingRuntimeData -RegistrationBundle $junction -ToBundle $replacement
    if (Test-Path -LiteralPath $junction) { throw 'PowerShell 2 migration did not remove the junction.' }
    if (-not (Test-Path -LiteralPath $target)) { throw 'PowerShell 2 migration removed the junction target.' }
    Assert-RuntimeDataManifest -Bundle $replacement -Expected $migrationManifest -Exact

    New-Item -ItemType Directory -Path (Join-Path $repairTarget 'Data\Empty') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $repairTarget 'Logs') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $repairTarget 'Data\config.json'), '{"repair":true}', [Text.Encoding]::UTF8)
    $repairManifest = Get-RuntimeDataManifest $repairTarget
    New-DirectoryJunctionCompatible -Path $repairJunction -Target $repairTarget
    Remove-RegistrationPreservingRuntimeData -RegistrationBundle $repairJunction -ToBundle $repairTarget
    if (Test-Path -LiteralPath $repairJunction) { throw 'PowerShell 2 repair did not remove the junction.' }
    Assert-RuntimeDataManifest -Bundle $repairTarget -Expected $repairManifest -Exact

    New-Item -ItemType Directory -Path (Join-Path $folderRegistration 'Data\Empty') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $folderRegistration 'Logs') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $folderRegistration 'Data\config.json'), '{"folder":true}', [Text.Encoding]::UTF8)
    $folderManifest = Get-RuntimeDataManifest $folderRegistration
    New-Item -ItemType Directory -Path $folderReplacement -Force | Out-Null
    Remove-RegistrationPreservingRuntimeData -RegistrationBundle $folderRegistration -ToBundle $folderReplacement
    if (Test-Path -LiteralPath $folderRegistration) { throw 'PowerShell 2 folder migration did not remove the old folder.' }
    Assert-RuntimeDataManifest -Bundle $folderReplacement -Expected $folderManifest -Exact

    New-Item -ItemType Directory -Path (Join-Path $unsafeTarget 'Data') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $unsafeTarget 'Data\config.json'), '{"unsafe":false}', [Text.Encoding]::UTF8)
    New-DirectoryJunctionCompatible -Path $unsafeRegistration -Target $unsafeTarget
    New-Item -ItemType Directory -Path $unsafeDestination -Force | Out-Null
    New-Item -ItemType Directory -Path $unsafeExternal -Force | Out-Null
    New-DirectoryJunctionCompatible -Path (Join-Path $unsafeDestination 'Data') -Target $unsafeExternal
    $unsafeBlocked = $false
    try {
        Remove-RegistrationPreservingRuntimeData -RegistrationBundle $unsafeRegistration -ToBundle $unsafeDestination
    }
    catch { $unsafeBlocked = $true }
    if (-not $unsafeBlocked -or -not (Test-Path -LiteralPath $unsafeRegistration)) {
        throw 'PowerShell 2 unsafe destination was not blocked before registration removal.'
    }
    if (Test-Path -LiteralPath (Join-Path $unsafeExternal 'config.json')) {
        throw 'PowerShell 2 unsafe destination wrote through a reparse point.'
    }
    $record = New-Object PSObject -Property @{ Year = 2020; Runtime = '.NET Framework 4.8' }
    $report = @{ Version = '2.2.0.32'; Passed = $true; Records = @($record) }
    Write-JsonFile -Path $reportPath -Value $report
    if (-not (Test-Path -LiteralPath $reportPath)) { throw 'PowerShell 2 JSON report writing failed.' }
    $writtenReport = Read-JsonFile $reportPath
    if ($writtenReport.Version -ne '2.2.0.32' -or $writtenReport.Passed -ne $true) {
        throw 'PowerShell 2 JSON report round-trip failed.'
    }
    Write-Host 'PowerShell 2 installer compatibility smoke test passed.'
}
finally {
    if (Test-Path -LiteralPath $junction) { [IO.Directory]::Delete($junction) }
    if (Test-Path -LiteralPath $repairJunction) { [IO.Directory]::Delete($repairJunction) }
    if (Test-Path -LiteralPath $unsafeRegistration) { [IO.Directory]::Delete($unsafeRegistration) }
    if (Test-Path -LiteralPath (Join-Path $unsafeDestination 'Data')) { [IO.Directory]::Delete((Join-Path $unsafeDestination 'Data')) }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
