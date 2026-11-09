function Get-ZwcadProtectedFileState([string]$BundlePath) {
    $items = @()
    foreach ($name in @('Data','Logs','install-selection.json')) {
        $path = Join-Path $BundlePath $name
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $entries = @((Get-Item -LiteralPath $path))
        if ($entries[0].PSIsContainer) { $entries += @(Get-ChildItem -LiteralPath $path -Recurse -Force) }
        foreach ($entry in $entries) {
            if ($entry.PSIsContainer) { continue }
            $algorithm = [Security.Cryptography.SHA256]::Create()
            try { $hash = [Convert]::ToBase64String($algorithm.ComputeHash([IO.File]::ReadAllBytes($entry.FullName))) } finally { $algorithm.Dispose() }
            $items += $entry.FullName.Substring($BundlePath.Length) + '|' + $hash
        }
    }
    $items | Sort-Object
}

# Registry snapshots preserve value kinds, including expandable strings and binary data.
function Get-ZwcadRegistrySnapshot([string]$Path) {
    $exists = Test-Path -LiteralPath $Path
    $nodes = @()
    if ($exists) {
        $keys = @((Get-Item -LiteralPath $Path)) + @(Get-ChildItem -LiteralPath $Path -Recurse)
        foreach ($key in $keys) {
            $values = @()
            foreach ($name in $key.GetValueNames()) {
                $values += New-Object PSObject -Property @{
                    Name = $name; Kind = $key.GetValueKind($name).ToString()
                    Value = $key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
                }
            }
            $nodes += New-Object PSObject -Property @{ Relative = $key.Name.Substring($keys[0].Name.Length).TrimStart('\'); Values = $values }
        }
    }
    New-Object PSObject -Property @{ Path = $Path; Exists = $exists; Nodes = $nodes }
}

function New-ZwcadInstallTransaction([string]$BundlePath, $Paths) {
    # Validate write access before migrating data or removing any existing registration.
    foreach ($path in $Paths) {
        $ancestor = $path
        while (-not (Test-Path -LiteralPath $ancestor)) {
            $parent = Split-Path -Parent $ancestor
            if (-not $parent -or $parent -eq $ancestor) { throw "Cannot find registry parent for $path" }
            $ancestor = $parent
        }
        $key = Get-Item -LiteralPath $ancestor
        $writable = $key.OpenSubKey('', $true)
        if ($null -eq $writable) { throw "Cannot write product registry path: $path" }
        $writable.Close()
    }
    $backup = Join-Path ([IO.Path]::GetTempPath()) ('XiaoYang-ZWCAD-rollback-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    $snapshots = @($Paths | ForEach-Object { Get-ZwcadRegistrySnapshot $_ })
    $files = @()
    foreach ($name in @('Data','Logs','install-selection.json')) {
        $path = Join-Path $BundlePath $name
        $exists = Test-Path -LiteralPath $path
        if ($exists) { Copy-Item -LiteralPath $path -Destination (Join-Path $backup $name) -Recurse }
        $files += New-Object PSObject -Property @{ Name = $name; Exists = $exists }
    }
    $state = New-Object PSObject -Property @{ Backup = $backup; BundlePath = $BundlePath; Registry = $snapshots; Files = $files; ExpectedFiles = @(Get-ZwcadProtectedFileState $BundlePath); ExpectedLoaders = @{} }
    $state | Export-Clixml -Path (Join-Path $backup 'transaction.xml') -Depth 40
    $state
}

function Remove-ZwcadProductRegistrations($Paths) {
    foreach ($path in $Paths) {
        if ((Split-Path -Leaf $path) -ne 'XiaoYangZWCADRadialMenu') { throw 'Refusing to change a non-product registry key.' }
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop }
    }
}

function Restore-ZwcadInstallTransaction($State) {
    if (@(Get-Process -Name ZWCAD -ErrorAction SilentlyContinue).Count -gt 0) { throw 'ZWCAD restarted; rollback refused to protect active user data.' }
    $currentFiles = @(Get-ZwcadProtectedFileState $State.BundlePath)
    if (($currentFiles -join "`n") -ne (@($State.ExpectedFiles) -join "`n")) { throw 'Protected files changed concurrently; rollback refused. Original backup is retained.' }
    foreach ($snapshot in $State.Registry) {
        if (-not (Test-Path -LiteralPath $snapshot.Path)) { continue }
        $key = Get-Item -LiteralPath $snapshot.Path
        $currentLoader = [string]$key.GetValue('LOADER','')
        $oldLoader = ''
        foreach ($node in $snapshot.Nodes) {
            if (-not $node.Relative) {
                foreach ($value in $node.Values) { if ($value.Name -eq 'LOADER') { $oldLoader = [string]$value.Value } }
            }
        }
        $expectedLoader = [string]$State.ExpectedLoaders[$snapshot.Path]
        if ($currentLoader -and $currentLoader -ne $oldLoader -and $currentLoader -ne $expectedLoader) { throw 'Product LOADER changed concurrently; rollback refused.' }
    }
    foreach ($snapshot in $State.Registry) {
        $path = $snapshot.Path
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force -ErrorAction Stop }
        if ($snapshot.Exists) {
            foreach ($node in $snapshot.Nodes) {
                $target = $path
                if ($node.Relative) { $target = Join-Path $path $node.Relative }
                $key = New-Item -Path $target -Force
                foreach ($value in $node.Values) {
                    $kind = [Microsoft.Win32.RegistryValueKind][Enum]::Parse([Microsoft.Win32.RegistryValueKind],$value.Kind)
                    $key.SetValue($value.Name,$value.Value,$kind)
                }
            }
        }
    }
    $root = [IO.Path]::GetFullPath($State.BundlePath).TrimEnd('\') + '\'
    foreach ($file in $State.Files) {
        $path = [IO.Path]::GetFullPath((Join-Path $State.BundlePath $file.Name))
        if (-not $path.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid rollback file path.' }
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        if ($file.Exists) { Copy-Item -LiteralPath (Join-Path $State.Backup $file.Name) -Destination $path -Recurse }
    }
}
