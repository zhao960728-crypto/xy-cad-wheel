param([string]$PackageRoot)

$ErrorActionPreference = 'Stop'
if (-not $PackageRoot -or -not (Test-Path -LiteralPath $PackageRoot)) { throw 'PackageRoot is invalid.' }

$scripts = @(Get-ChildItem -LiteralPath $PackageRoot -Recurse | Where-Object {
    -not $_.PSIsContainer -and $_.Extension -eq '.ps1'
})
foreach ($script in $scripts) {
    [void][scriptblock]::Create([IO.File]::ReadAllText($script.FullName))
    $bytes = [IO.File]::ReadAllBytes($script.FullName)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191) {
        throw "PowerShell 2 UTF-8 BOM is missing: $($script.FullName)"
    }
}

$cmdFiles = @(Get-ChildItem -LiteralPath $PackageRoot -Recurse | Where-Object {
    -not $_.PSIsContainer -and $_.Extension -eq '.cmd'
})
foreach ($cmdFile in $cmdFiles) {
    $bytes = [IO.File]::ReadAllBytes($cmdFile.FullName)
    for ($index = 0; $index -lt $bytes.Length; $index++) {
        if ($bytes[$index] -eq 10 -and ($index -eq 0 -or $bytes[$index - 1] -ne 13)) {
            throw "Windows cmd CRLF is missing: $($cmdFile.FullName)"
        }
    }
}

Write-Host ("PowerShell 2 package parse passed: {0} scripts, {1} cmd files." -f $scripts.Count, $cmdFiles.Count)
