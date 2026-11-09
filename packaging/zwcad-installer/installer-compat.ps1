$ErrorActionPreference = 'Stop'

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
