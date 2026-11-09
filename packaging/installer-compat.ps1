$ErrorActionPreference = 'Stop'

function Read-Utf8Text {
    param([string]$Path)
    [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8)
}

function Read-JsonFile {
    param([string]$Path)
    $text = Read-Utf8Text $Path
    $result = @{}
    $pattern = '"(?<key>(?:\\.|[^"\\])*)"\s*:\s*(?<value>"(?:\\.|[^"\\])*"|true|false|null|-?\d+(?:\.\d+)?)'
    foreach ($match in [Text.RegularExpressions.Regex]::Matches($text, $pattern)) {
        $key = $match.Groups['key'].Value
        $raw = $match.Groups['value'].Value
        if ($raw.StartsWith('"')) {
            $value = $raw.Substring(1, $raw.Length - 2)
            $value = [Text.RegularExpressions.Regex]::Replace($value, '\\u(?<hex>[0-9A-Fa-f]{4})', {
                param($unicodeMatch)
                [char][Convert]::ToInt32($unicodeMatch.Groups['hex'].Value, 16)
            })
            $value = $value.Replace('\r', "`r").Replace('\n', "`n").Replace('\t', "`t").Replace('\"', '"').Replace('\\', '\')
        }
        elseif ($raw -eq 'true') { $value = $true }
        elseif ($raw -eq 'false') { $value = $false }
        elseif ($raw -eq 'null') { $value = $null }
        else { $value = $raw -as [double] }
        $result[$key] = $value
    }
    $result
}

function Write-JsonFile {
    param([string]$Path, [object]$Value)
    $utf8Bom = New-Object Text.UTF8Encoding($true)
    $safeValue = ConvertTo-JsonSafeValue $Value
    $json = ConvertTo-JsonText $safeValue
    [IO.File]::WriteAllText($Path, $json, $utf8Bom)
}

function ConvertTo-JsonText {
    param([object]$Value)
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [string] -or $Value -is [char]) {
        $escaped = "$Value".Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t')
        return '"' + $escaped + '"'
    }
    if ($Value -is [bool]) { if ($Value) { return 'true' } else { return 'false' } }
    if ($Value -is [byte] -or $Value -is [sbyte] -or $Value -is [int16] -or
        $Value -is [uint16] -or $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64] -or $Value -is [single] -or
        $Value -is [double] -or $Value -is [decimal]) {
        return [Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture)
    }
    if ($Value -is [Collections.IDictionary]) {
        $parts = @()
        foreach ($key in $Value.Keys) { $parts += (ConvertTo-JsonText "$key") + ':' + (ConvertTo-JsonText $Value[$key]) }
        return '{' + ($parts -join ',') + '}'
    }
    if ($Value -is [Collections.IEnumerable]) {
        $parts = @()
        foreach ($item in $Value) { $parts += ConvertTo-JsonText $item }
        return '[' + ($parts -join ',') + ']'
    }
    return ConvertTo-JsonText (ConvertTo-JsonSafeValue $Value)
}

function ConvertTo-JsonSafeValue {
    param([object]$Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [string] -or $Value -is [char] -or $Value -is [bool] -or
        $Value -is [byte] -or $Value -is [sbyte] -or $Value -is [int16] -or
        $Value -is [uint16] -or $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64] -or $Value -is [single] -or
        $Value -is [double] -or $Value -is [decimal]) { return $Value }
    if ($Value -is [DateTime]) { return $Value.ToString('o') }
    if ($Value -is [Version] -or $Value -is [Guid]) { return $Value.ToString() }
    if ($Value -is [Collections.IDictionary]) {
        $dictionary = @{}
        foreach ($key in $Value.Keys) { $dictionary["$key"] = ConvertTo-JsonSafeValue $Value[$key] }
        return $dictionary
    }
    if ($Value -is [Collections.IEnumerable]) {
        $items = @()
        foreach ($item in $Value) { $items += ,(ConvertTo-JsonSafeValue $item) }
        return ,$items
    }
    $properties = @($Value.PSObject.Properties | Where-Object {
        $_.MemberType -eq 'NoteProperty' -or $_.MemberType -eq 'Property'
    })
    if ($properties.Count -gt 0) {
        $object = @{}
        foreach ($property in $properties) { $object[$property.Name] = ConvertTo-JsonSafeValue $property.Value }
        return $object
    }
    return "$Value"
}

function Get-Sha256Hash {
    param([string]$Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $sha256.ComputeHash($stream)
        -join @($bytes | ForEach-Object { $_.ToString('x2') })
    }
    finally {
        $stream.Dispose()
        $sha256.Clear()
    }
}

function Get-RuntimeDataManifest {
    param([string]$Bundle)
    $bundleFull = [IO.Path]::GetFullPath($Bundle).TrimEnd('\')
    $files = @{}
    $directories = New-Object 'System.Collections.Generic.List[string]'
    foreach ($rootName in @('Data', 'Logs')) {
        $root = Join-Path $bundleFull $rootName
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $rootItem = Get-Item -LiteralPath $root -Force
        if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Runtime data contains a reparse point: $root"
        }
        [void]$directories.Add($rootName)
        foreach ($item in @(Get-ChildItem -LiteralPath $root -Recurse -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Runtime data contains a reparse point: $($item.FullName)"
            }
            $relative = $item.FullName.Substring($bundleFull.Length).TrimStart('\')
            if ($item.PSIsContainer) {
                [void]$directories.Add($relative)
            }
            else {
                $files[$relative] = New-Object PSObject -Property @{
                    Length = [long]$item.Length
                    Sha256 = Get-Sha256Hash $item.FullName
                }
            }
        }
    }
    New-Object PSObject -Property @{
        Directories = @($directories | Sort-Object -Unique)
        Files = $files
    }
}

function Assert-RuntimeDataManifest {
    param(
        [string]$Bundle,
        [object]$Expected,
        [switch]$Exact
    )
    $actual = Get-RuntimeDataManifest $Bundle
    foreach ($directory in @($Expected.Directories)) {
        if (@($actual.Directories) -notcontains $directory) {
            throw "Runtime data directory was not preserved: $directory"
        }
    }
    foreach ($relative in @($Expected.Files.Keys)) {
        if (-not $actual.Files.ContainsKey($relative)) {
            throw "Runtime data file was not preserved: $relative"
        }
        $expectedFile = $Expected.Files[$relative]
        $actualFile = $actual.Files[$relative]
        if ([long]$expectedFile.Length -ne [long]$actualFile.Length -or
            $expectedFile.Sha256 -ne $actualFile.Sha256) {
            throw "Runtime data file changed during migration: $relative"
        }
    }
    if ($Exact) {
        if (@($Expected.Directories).Count -ne @($actual.Directories).Count -or
            @($Expected.Files.Keys).Count -ne @($actual.Files.Keys).Count) {
            throw 'Runtime data snapshot contains unexpected or missing entries.'
        }
    }
}

function Assert-SafeRuntimeDataDestination {
    param([string]$Bundle)
    $bundleFull = [IO.Path]::GetFullPath($Bundle).TrimEnd('\')
    foreach ($rootName in @('Data', 'Logs')) {
        $root = Join-Path $bundleFull $rootName
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($item in @((Get-Item -LiteralPath $root -Force)) + @(Get-ChildItem -LiteralPath $root -Recurse -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Runtime data destination contains a reparse point: $($item.FullName)"
            }
        }
    }
}

function Copy-RuntimeData {
    param([string]$FromBundle, [string]$ToBundle)
    if (-not $FromBundle -or -not (Test-Path -LiteralPath $FromBundle)) { return }
    if ([IO.Path]::GetFullPath($FromBundle).TrimEnd('\') -eq [IO.Path]::GetFullPath($ToBundle).TrimEnd('\')) { return }
    Assert-SafeRuntimeDataDestination $ToBundle
    $manifest = Get-RuntimeDataManifest $FromBundle
    foreach ($directory in @($manifest.Directories)) {
        New-Item -ItemType Directory -Force -Path (Join-Path $ToBundle $directory) | Out-Null
    }
    foreach ($relative in @($manifest.Files.Keys)) {
        $destination = Join-Path $ToBundle $relative
        $parent = Split-Path -Parent $destination
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
        Copy-Item -LiteralPath (Join-Path $FromBundle $relative) -Destination $destination -Force
    }
}

function Remove-RegistrationPreservingRuntimeData {
    param([string]$RegistrationBundle, [string]$ToBundle)
    $item = Get-Item -LiteralPath $RegistrationBundle -Force -ErrorAction SilentlyContinue
    if (-not $item) { throw "Registration does not exist: $RegistrationBundle" }
    $registrationFull = [IO.Path]::GetFullPath($RegistrationBundle).TrimEnd('\')
    $destinationFull = [IO.Path]::GetFullPath($ToBundle).TrimEnd('\')
    if ($registrationFull.Equals($destinationFull, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Registration and installation source are the same directory: $registrationFull"
    }
    $isLink = ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
    # Validate before snapshotting/removing the old registration so an unsafe
    # destination can never cause a write-through or data loss.
    Assert-SafeRuntimeDataDestination $ToBundle
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $snapshot = [IO.Path]::GetFullPath((Join-Path $tempRoot ('XYCAD-Runtime-' + [Guid]::NewGuid().ToString('N'))))
    if (-not $snapshot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime data snapshot escaped the temporary directory: $snapshot"
    }
    New-Item -ItemType Directory -Force -Path $snapshot | Out-Null
    $completed = $false
    try {
        $manifest = Get-RuntimeDataManifest $RegistrationBundle
        Copy-RuntimeData -FromBundle $RegistrationBundle -ToBundle $snapshot
        Assert-RuntimeDataManifest -Bundle $snapshot -Expected $manifest -Exact

        if ($isLink) {
            # Delete only the junction. Its target and all user files remain intact.
            [IO.Directory]::Delete($RegistrationBundle)
        }
        else {
            Remove-Item -LiteralPath $RegistrationBundle -Recurse -Force
        }
        if (Test-Path -LiteralPath $RegistrationBundle) {
            throw "Failed to remove registration junction: $RegistrationBundle"
        }

        Copy-RuntimeData -FromBundle $snapshot -ToBundle $ToBundle
        Assert-RuntimeDataManifest -Bundle $ToBundle -Expected $manifest
        $completed = $true
    }
    catch {
        throw "$($_.Exception.Message) Runtime-data recovery snapshot kept at: $snapshot"
    }
    finally {
        if ($completed -and (Test-Path -LiteralPath $snapshot)) {
            try { Remove-Item -LiteralPath $snapshot -Recurse -Force }
            catch { Write-Warning "Runtime data was restored, but the temporary snapshot could not be removed: $snapshot" }
        }
    }
}

function New-DirectoryJunctionCompatible {
    param([string]$Path, [string]$Target)
    $arguments = '/d /c mklink /J "{0}" "{1}"' -f $Path.Replace('"', '""'), $Target.Replace('"', '""')
    $process = Start-Process -FilePath $env:ComSpec -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $Path)) {
        throw "Failed to create directory junction: $Path -> $Target (exit code $($process.ExitCode))"
    }
}
