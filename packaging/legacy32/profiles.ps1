function Get-SupportedAutoCadProfileKeys {
    # Only register product IDs whose executable was verified as 2010 x86.
    # HKCU can also contain profiles from a different architecture/year.
    $supportedPaths = @($compatible | ForEach-Object { $_.Path })
    $profiles = @{}
    foreach ($root in @('HKCU:\Software\Autodesk\AutoCAD\R18.0', 'HKLM:\SOFTWARE\Autodesk\AutoCAD\R18.0')) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        foreach ($key in @(Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue)) {
            if ($key.PSChildName -notmatch '^ACAD-[^\\]+:[0-9A-Fa-f]+$') { continue }
            $properties = Get-ItemProperty -LiteralPath $key.PSPath
            foreach ($name in @('Location', 'AcadLocation')) {
                $value = $properties.PSObject.Properties[$name]
                if (-not $value -or -not $value.Value) { continue }
                $exe = [IO.Path]::GetFullPath((Join-Path ([string]$value.Value) 'acad.exe'))
                if ($supportedPaths -notcontains $exe) { continue }
                $profiles[$key.PSChildName] = New-Object PSObject -Property @{
                    Release = 'R18.0'
                    Path = ('HKCU:\Software\Autodesk\AutoCAD\R18.0\' + $key.PSChildName)
                }
            }
        }
    }
    @($profiles.Values)
}
