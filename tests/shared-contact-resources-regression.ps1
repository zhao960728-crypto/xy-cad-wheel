$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$run = Join-Path $repo ('work\slim-20261017\contact-validation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run -Force | Out-Null
$checks = 0
foreach ($script in @('packaging\install.ps1', 'packaging\zwcad-installer\install.ps1', 'packaging\self-test.ps1')) {
    $source = [IO.File]::ReadAllText((Join-Path $repo $script))
    $function = [regex]::Match($source, '(?ms)^function Assert-SharedContactResources \{.*?^\}')
    if (-not $function.Success) { throw "Shared contact validator was not found: $script" }
    . ([scriptblock]::Create($function.Value))
    $bundle = Join-Path $run ([IO.Path]::GetFileNameWithoutExtension($script) + '-' + $checks + '.bundle')
    $contact = Join-Path $bundle 'Contents\Resources\Contact'
    Assert-SharedContactResources $bundle ([version]'2.2.0.70'); $checks++
    Assert-SharedContactResources $bundle ([version]'2.2.0.82'); $checks++
    $blocked = $false
    try { Assert-SharedContactResources $bundle ([version]'2.2.0.83') } catch { $blocked = $_.ToString() -like '*Shared contact*' }
    if (-not $blocked) { throw "Missing .83 contact resources were accepted: $script" }; $checks++
    New-Item -ItemType Directory -Path $contact -Force | Out-Null
    foreach ($name in @('抖音.png', '加我.png')) {
        Copy-Item -LiteralPath (Join-Path $repo ('src\XiaoYang.AutoCADPlugin\Resources\' + $name)) -Destination (Join-Path $contact $name)
    }
    Assert-SharedContactResources $bundle ([version]'2.2.0.83'); $checks++
    foreach ($name in @('抖音.png', '加我.png')) {
        $path = Join-Path $contact $name
        $original = [IO.File]::ReadAllBytes($path)
        foreach ($defect in @('missing', 'signature', 'truncated', 'zero width')) {
            try {
                $changed = [byte[]]$original.Clone()
                switch ($defect) {
                    'missing' { Remove-Item -LiteralPath $path }
                    'signature' { $changed[0] = 0; [IO.File]::WriteAllBytes($path, $changed) }
                    'truncated' { [IO.File]::WriteAllBytes($path, [byte[]]$changed[0..23]) }
                    'zero width' { [Array]::Clear($changed,16,4); [IO.File]::WriteAllBytes($path, $changed) }
                }
                $blocked = $false
                try { Assert-SharedContactResources $bundle ([version]'2.2.0.83') } catch { $blocked = $_.ToString() -like '*Shared contact*' }
                if (-not $blocked) { throw "Invalid .83 contact resource accepted: $script / $name / $defect" }; $checks++
            }
            finally { [IO.File]::WriteAllBytes($path, $original) }
        }
    }
}
Write-Host "Shared contact resource regression passed: $checks checks. Evidence: $run"
