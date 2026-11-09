param([string]$Repository = (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)))
$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $Repository 'src\XiaoYang.AutoCADPlugin\PluginUpdateHandoff.cs'))
$match = [regex]::Match($source, '(?s)internal const string Script = @"(.*)";')
if (-not $match.Success) { throw 'Embedded handoff script not found.' }
$text = $match.Groups[1].Value.Replace('""','"')
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseInput($text, [ref]$tokens, [ref]$errors)
if ($errors.Count -ne 0) { throw ($errors | Out-String) }
. (Join-Path $Repository 'packaging\installer-compat.ps1')
# Load only function definitions; never run UI, host process operations, installer or registry setup.
foreach ($function in $ast.FindAll({ param($node) $node -is [Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($function.Extent.Text))
}
$testRoot = Join-Path $Repository ('work\one-click-update-71\test-handoff-' + [guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testRoot | Out-Null
$script:checks = 0
function Check([bool]$condition, [string]$message) { $script:checks++; if (-not $condition) { throw $message }; Write-Output ('PASS ' + $message) }
function Must-Fail([scriptblock]$action, [string]$message) { $failed = $false; try { & $action } catch { $failed = $true }; Check $failed $message }
$old = Join-Path $testRoot 'old.bundle'
$newBundle = Join-Path $testRoot 'new.bundle'
$manual = Join-Path $testRoot 'manual.bundle'
foreach ($directory in @($old,$newBundle,$manual)) { New-Item -ItemType Directory -Path $directory | Out-Null }
foreach ($relative in @('Data\config.json','Data\aliases.pgp','Logs\before.log')) {
    $path = Join-Path $old $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path, 'preserve ' + $relative)
}
$before = Get-DataManifest $old
Check ($before.Count -eq 3) 'all Data and Logs files hashed'
Assert-Data $old $before
Check $true 'unchanged manifest accepted'
$changed = Join-Path $old 'Data\config.json'
$original = [IO.File]::ReadAllText($changed)
[IO.File]::WriteAllText($changed, 'changed')
Must-Fail { Assert-Data $old $before } 'modified configuration detected'
[IO.File]::WriteAllText($changed, $original)
$added = Join-Path $old 'Data\unexpected.json'
[IO.File]::WriteAllText($added, 'unexpected')
Must-Fail { Assert-Data $old $before } 'unexpected extra configuration detected'
Remove-Item -LiteralPath $added
$temporary = Join-Path $old 'Data\aliases.pgp'
Move-Item -LiteralPath $temporary -Destination ($temporary + '.moved')
Must-Fail { Assert-Data $old $before } 'missing configuration detected'
Move-Item -LiteralPath ($temporary + '.moved') -Destination $temporary
$link = Join-Path $testRoot 'active.bundle'
New-DirectoryJunctionCompatible -Path $link -Target $old
Check ((Resolve-LinkTarget $link) -eq $old) 'junction resolves to original installation'
[IO.Directory]::Delete($link)
New-DirectoryJunctionCompatible -Path $link -Target $newBundle
Restore-Link @{Path=$link; Target=$old}
Check ((Resolve-LinkTarget $link) -eq $old) 'failed update junction restored to original installation'
Assert-Data $old $before
Check $true 'original configuration survives rollback'
[IO.Directory]::Delete($link)
New-DirectoryJunctionCompatible -Path $link -Target $manual
Must-Fail { Restore-Link @{Path=$link; Target=$old} } 'manual junction change is not overwritten'
Check ((Resolve-LinkTarget $link) -eq $manual) 'manual junction target remains intact'
[IO.Directory]::Delete($link)
New-DirectoryJunctionCompatible -Path $link -Target $newBundle
Restore-Link @{Path=$link; Target=$null}
Check (-not (Test-Path -LiteralPath $link)) 'new junction removed on rollback without removing target'
Check (Test-Path -LiteralPath $newBundle) 'new target directory remains available for diagnosis'
$dataLink = Join-Path $old 'Data\linked'
New-DirectoryJunctionCompatible -Path $dataLink -Target $manual
Must-Fail { Get-DataManifest $old } 'reparse data directory blocked'
[IO.Directory]::Delete($dataLink)
Assert-Data $old $before
Check $true 'all original Data and Logs hashes preserved'
$package = Join-Path $testRoot 'payload'
$evidence = Join-Path $testRoot 'evidence'
New-Item -ItemType Directory -Path $package,$evidence | Out-Null
$payloadFile = Join-Path $package 'install.ps1'
[IO.File]::WriteAllText($payloadFile, '# inert test payload')
$line = (Get-Sha256Hash $payloadFile) + ' ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('install.ps1'))
[IO.File]::WriteAllLines((Join-Path $evidence 'payload-sha256.txt'), @($line))
Assert-Payload
Check $true 'unchanged waiting payload accepted'
[IO.File]::WriteAllText($payloadFile, '# modified test payload')
Must-Fail { Assert-Payload } 'waiting payload modification blocks installation'
[IO.File]::WriteAllText($payloadFile, '# inert test payload')
[IO.File]::WriteAllText((Join-Path $package 'unexpected.ps1'), '# extra')
Must-Fail { Assert-Payload } 'waiting payload extra file blocks installation'
Remove-Item -LiteralPath (Join-Path $package 'unexpected.ps1')
Remove-Item -LiteralPath $payloadFile
Must-Fail { Assert-Payload } 'waiting payload missing file blocks installation'
Check ($text -notmatch '\b(?:Stop-Process|taskkill|Kill)\b') 'handoff has no force-close host command'
Check ($text.IndexOf("if (`$cancelled)") -lt $text.IndexOf('Prepare-Transaction', $text.IndexOf('$form ='))) 'cancel is checked before preparing transaction'
Check ($text -notmatch '(?i)\b(?:ConvertTo-Json|ConvertFrom-Json|Get-FileHash|Get-CimInstance)\b|-File\s+-Recurse|::new\(') 'handoff avoids known PowerShell 3+ only APIs'
# Registry rollback functions are exercised against an in-memory provider facade only.
& {
    $script:mockRegistry = @{}
    function Make-Key([string]$name) {
        $node = [pscustomobject]@{ Name=$name; Values=@{} }
        $node | Add-Member ScriptMethod GetValueNames { @($this.Values.Keys) }
        $node | Add-Member ScriptMethod GetValueKind { param($name) $this.Values[$name].Kind }
        $node | Add-Member ScriptMethod GetValue { param($name,$default,$options) $this.Values[$name].Value }
        $node | Add-Member ScriptMethod SetValue { param($name,$value,$kind) $this.Values[$name]=@{ Value=$value; Kind=[int]$kind } }
        $node | Add-Member ScriptMethod Close { }
        $script:mockRegistry[$name]=$node
        return $node
    }
    function Test-Path { param($LiteralPath) $script:mockRegistry.ContainsKey($LiteralPath) }
    function Get-Item { param($LiteralPath) $script:mockRegistry[$LiteralPath] }
    function Get-ChildItem { param($LiteralPath,[switch]$Recurse) @($script:mockRegistry.Values | Where-Object { $_.Name.StartsWith($LiteralPath+'\') }) }
    function Get-ItemProperty {
        param($LiteralPath)
        $properties=@{}; foreach ($key in $script:mockRegistry[$LiteralPath].Values.Keys) { $properties[$key]=$script:mockRegistry[$LiteralPath].Values[$key].Value }
        [pscustomobject]$properties
    }
    function Remove-Item { param($LiteralPath,[switch]$Recurse,[switch]$Force) foreach ($key in @($script:mockRegistry.Keys)) { if ($key -eq $LiteralPath -or $key.StartsWith($LiteralPath+'\')) { $script:mockRegistry.Remove($key) } } }
    function New-Item { param($Path,[switch]$Force) if (-not $script:mockRegistry.ContainsKey($Path)) { Make-Key $Path } }
    $isAcad=$false
    $keyPath='MEMREG:\Product'
    $node=Make-Key $keyPath
    $node.SetValue('LOADER','old.dll',1)
    $node.SetValue('LOADCTRLS',2,4)
    $node.SetValue('Custom',@('one','two'),7)
    $commands=Make-Key ($keyPath+'\Commands')
    $commands.SetValue('CUSTOM','CUSTOM',1)
    $records=@(Read-Key $keyPath)
    Check ($records.Count -eq 2 -and $records[0] -is [hashtable]) 'registry snapshot records are flat and include child keys'
    $snapshot=@{Path=$keyPath; Loader='old.dll'; Records=$records}
    $snapshot | Export-Clixml -LiteralPath (Join-Path $testRoot 'mock-registry.xml') -Depth 30
    $snapshot=Import-Clixml -LiteralPath (Join-Path $testRoot 'mock-registry.xml')
    Check ($snapshot.Records.Count -eq 2) 'registry snapshot survives serialization'
    $node.SetValue('LOADER',(Join-Path $newBundle 'kernel.dll'),1)
    $node.SetValue('LOADCTRLS',14,4)
    $node.SetValue('MANAGED',1,4)
    $node.Values.Remove('Custom')
    $commands.SetValue('XYLP','XYLP',1)
    Restore-Key $snapshot
    Check ($script:mockRegistry[$keyPath].Values['LOADER'].Value -eq 'old.dll') 'known installer loader restored'
    Check ($script:mockRegistry[$keyPath].Values['LOADCTRLS'].Value -eq 2) 'original load flags restored'
    Check (($script:mockRegistry[$keyPath].Values['Custom'].Value -join ',') -eq 'one,two') 'deleted multi-string custom value restored'
    Check (-not $script:mockRegistry[$keyPath].Values.ContainsKey('MANAGED')) 'new installer value removed'
    Check (-not $script:mockRegistry[$keyPath+'\Commands'].Values.ContainsKey('XYLP')) 'new installer command removed'
    $script:mockRegistry[$keyPath].SetValue('UserChange','keep',1)
    Must-Fail { Restore-Key $snapshot } 'concurrent unknown registry value prevents overwrite'
    Check ($script:mockRegistry[$keyPath].Values['UserChange'].Value -eq 'keep') 'manual registry value remains untouched'
    $newPath='MEMREG:\NewProduct'
    Check ($null -eq (Read-Key $newPath)) 'missing registry key snapshots as null'
    $newNode=Make-Key $newPath
    $newNode.SetValue('LOADER',(Join-Path $newBundle 'kernel.dll'),1)
    $newNode.SetValue('MANAGED',1,4)
    Restore-Key @{Path=$newPath; Loader=''; Records=$null}
    Check (-not $script:mockRegistry.ContainsKey($newPath)) 'newly created product registration removed on rollback'
}
# Execute the complete AutoCAD preparation branch, with the per-process APPDATA redirected
# to this fixture and product registry discovery replaced by an empty in-memory result.
& {
    function Get-ProductKeys { @() }
    function Invoke-PrepareFixture([string]$name, [string]$oldVersion, [bool]$secondSource, [bool]$conflict, [string]$expectedFailure) {
        $fixture = Join-Path $testRoot $name
        $package = Join-Path $fixture 'package'
        $evidence = Join-Path $fixture 'evidence'
        $bundleName = '小阳CAD轮盘插件.bundle'
        $newBundle = Join-Path $package $bundleName
        $oldBundle = Join-Path $fixture 'old.bundle'
        $otherBundle = Join-Path $fixture 'other.bundle'
        $mockAppData = Join-Path $fixture 'appdata'
        $pluginDirectory = Join-Path $mockAppData 'Autodesk\ApplicationPlugins'
        $isAcad = $true
        $version = [version]'2.2.0.71'
        $script:registrySnapshots=@(); $script:links=@(); $script:dataSnapshots=@(); $script:preservedManifest=@{}
        foreach ($directory in @($newBundle,$oldBundle,$evidence,$pluginDirectory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
        [IO.File]::WriteAllText((Join-Path $package 'install.ps1'), '# inert; never executed')
        $xmlSource = Join-Path $Repository 'dist\小阳CAD轮盘插件-2.2.0.70\小阳CAD轮盘插件.bundle\PackageContents.xml'
        [xml]$packageXml=[IO.File]::ReadAllText($xmlSource)
        $packageXml.ApplicationPackage.AppVersion='2.2.0.71'
        $packageXml.Save((Join-Path $newBundle 'PackageContents.xml'))
        $packageXml.ApplicationPackage.AppVersion=$oldVersion
        $packageXml.Save((Join-Path $oldBundle 'PackageContents.xml'))
        foreach ($relative in @('Data\config.json','Data\aliases.pgp','Logs\history.log')) {
            $path=Join-Path $oldBundle $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
            [IO.File]::WriteAllText($path,'original ' + $relative)
        }
        $before=Get-DataManifest $oldBundle
        $primaryLink=Join-Path $pluginDirectory $bundleName
        New-DirectoryJunctionCompatible -Path $primaryLink -Target $oldBundle
        if ($secondSource) {
            Copy-Item -LiteralPath $oldBundle -Destination $otherBundle -Recurse
            if ($conflict) { [IO.File]::WriteAllText((Join-Path $otherBundle 'Data\config.json'), 'other configuration') }
            else { [IO.File]::WriteAllText((Join-Path $otherBundle 'Data\extra.json'), 'unique second-source setting') }
            New-DirectoryJunctionCompatible -Path (Join-Path $pluginDirectory 'XY-CAD-H.bundle') -Target $otherBundle
        }
        $payloadLines=@()
        foreach ($file in @(Get-ChildItem -LiteralPath $package -Recurse | Where-Object { -not $_.PSIsContainer })) {
            $relative=$file.FullName.Substring($package.Length+1)
            $payloadLines += (Get-Sha256Hash $file.FullName) + ' ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($relative))
        }
        [IO.File]::WriteAllLines((Join-Path $evidence 'payload-sha256.txt'),$payloadLines)
        $originalAppData=$env:APPDATA
        try {
            $env:APPDATA=$mockAppData
            $failure=$null
            try { Prepare-Transaction } catch { $failure=$_ }
            if ($expectedFailure) {
                Check ($null -ne $failure -and $failure.Exception.Message.Contains($expectedFailure)) ($name + ': expected rejection reason')
                Check (-not (Test-Path -LiteralPath (Join-Path $evidence 'rollback-snapshot.xml'))) ($name + ': rejected preparation cannot commit snapshot')
            } else {
                if ($failure) { throw $failure }
                Check (Test-Path -LiteralPath (Join-Path $evidence 'rollback-snapshot.xml')) ($name + ': full transaction snapshot written')
                $saved=Import-Clixml -LiteralPath (Join-Path $evidence 'rollback-snapshot.xml')
                $expectedSources=if ($secondSource) {2} else {1}
                Check ($saved.Data.Count -eq $expectedSources) ($name + ': every source has serialized backup')
                foreach ($data in $saved.Data) { Assert-Data $data.Bundle $data.Manifest; Assert-Data $data.Backup $data.Manifest }
                Check $true ($name + ': serialized backup hashes equal original data and logs')
                Check ($script:preservedManifest.Count -eq $(if ($secondSource) {4} else {3})) ($name + ': preserved manifest merges source files')
                Check ($script:registrySnapshots.Count -eq 0) ($name + ': registry remains untouched')
            }
            Assert-Data $oldBundle $before
            Check ((Resolve-LinkTarget $primaryLink) -eq $oldBundle) ($name + ': preparation preserves active installation and original data')
        } finally { $env:APPDATA=$originalAppData }
    }
    Invoke-PrepareFixture 'prepare-upgrade' '2.2.0.70' $false $false $null
    Invoke-PrepareFixture 'prepare-same-version' '2.2.0.71' $false $false $null
    Invoke-PrepareFixture 'prepare-downgrade' '2.2.0.72' $false $false '更高版本'
    Invoke-PrepareFixture 'prepare-two-sources' '2.2.0.70' $true $false $null
    Invoke-PrepareFixture 'prepare-conflicting-data' '2.2.0.70' $true $true '数据冲突'
}
Write-Output "RESULT handoff assertions=$script:checks failures=0; artifacts=$testRoot; registryWrites=0; installerRuns=0"
