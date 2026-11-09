function New-CadInstallPlan {
    param([object[]]$Rows, [string]$AutoCadRoot, [string]$ZwcadRoot)
    foreach ($product in @('AutoCAD', 'ZWCAD')) {
        $selected = @($Rows | Where-Object { $_.Product -eq $product -and $_.Supported } | ForEach-Object { $_.Series } | Sort-Object -Unique)
        if ($selected.Count -eq 0) { continue }
        $directory = if ($product -eq 'AutoCAD') { $AutoCadRoot } else { $ZwcadRoot }
        if (-not $directory) { throw "缺少 $product 安装包。" }
        $script = Join-Path $directory 'install.ps1'
        if (-not (Test-Path -LiteralPath $script)) { throw "缺少安装脚本：$script" }
        foreach ($value in $selected) {
            if ($product -eq 'AutoCAD' -and $value -notmatch '^R(18\.[0-2]|19\.[01]|20\.[01]|21\.0|22\.0|23\.[01]|24\.[0-3]|25\.[01]|26\.0)$') { throw 'AutoCAD 版本选择无效。' }
            if ($product -eq 'ZWCAD' -and $value -notmatch '^202[0-6]$') { throw '中望版本选择无效。' }
        }
        $arguments = '-NoLogo -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File "' + $script + '"'
        $arguments += if ($product -eq 'AutoCAD') { ' -SelectedSeries ' } else { ' -Quiet -SelectedYears ' }
        $arguments += ($selected -join ',')
        New-Object PSObject -Property @{
            Product = $product
            Arguments = $arguments
            Installer = $script
            Selection = ($selected -join ',')
            Processes = $(if ($product -eq 'AutoCAD') { @('acad','accoreconsole') } else { @('ZWCAD') })
        }
    }
}
