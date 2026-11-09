param([switch]$ListOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $root 'installer-compat.ps1')
. (Join-Path $root 'autocad-selection.ps1')
. (Join-Path $root 'installer-plan.ps1')
$autoCadRoot = $root
if (Test-Path -LiteralPath (Join-Path $root 'AutoCAD\install.ps1')) { $autoCadRoot = Join-Path $root 'AutoCAD' }
$zwcadRoot = ''
$zwHosts = @()
if (Test-Path -LiteralPath (Join-Path $root 'ZWCAD\zwcad-selection.ps1')) {
    $zwcadRoot = Join-Path $root 'ZWCAD'
    . (Join-Path $zwcadRoot 'zwcad-selection.ps1')
    $zwHosts = @(Get-ZwcadInstallations)
}
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[Windows.Forms.Application]::EnableVisualStyles()
$hosts = @(Find-AutoCadInstallations | Sort-Object Series, Path)
if ($ListOnly) { $hosts; $zwHosts; return }
$form = New-Object Windows.Forms.Form
$form.Text = '小阳 CAD 轮盘 · 一键安装'
$form.ClientSize = New-Object Drawing.Size(760, 480)
$form.MinimumSize = New-Object Drawing.Size(700, 470)
$form.StartPosition = 'CenterScreen'
$form.AutoScaleMode = 'Dpi'
$form.Font = New-Object Drawing.Font('Microsoft YaHei UI', 10)
$title = New-Object Windows.Forms.Label
$title.Text = if ($zwcadRoot) { '选择 AutoCAD / 中望 CAD 版本' } else { '选择要启用轮盘的 AutoCAD 版本' }
$title.Font = New-Object Drawing.Font('Microsoft YaHei UI', 15, ([Drawing.FontStyle]::Bold))
$title.SetBounds(22, 20, 710, 38)
$title.Anchor = 'Top,Left,Right'
$form.Controls.Add($title)
$hint = New-Object Windows.Forms.Label
$hint.Text = '可跨产品多选；标注“不支持”的版本不可安装。按勾选年份启用轮盘，未勾选的产品不作处理。原配置保留。'
$hint.SetBounds(24, 66, 710, 52)
$hint.Anchor = 'Top,Left,Right'
$form.Controls.Add($hint)
$list = New-Object Windows.Forms.CheckedListBox
$list.SetBounds(24, 120, 712, 225)
$list.Anchor = 'Top,Bottom,Left,Right'
$list.CheckOnClick = $true
$list.HorizontalScrollbar = $true
$list.IntegralHeight = $false
$form.Controls.Add($list)
$savedFile = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\小阳CAD轮盘插件.bundle\install-selection.json'
$savedSeries = @()
if (Test-Path -LiteralPath $savedFile) { $savedSeries = @(Read-AutoCadSelection $savedFile) }
$rows = @()
foreach ($group in @($hosts | Group-Object Series)) {
    $supported = @($group.Group | Where-Object { $_.Supported })
    $year = Get-AutoCadYear $group.Name
    $label = if ($year) { "AutoCAD $year" } else { "未知版本 $($group.Name)" }
    if ($supported.Count -eq 0) { $label += '（不支持：需 2010–2027 x64）' }
    $label += '  |  ' + (($group.Group | ForEach-Object { Split-Path -Parent $_.Path }) -join '; ')
    $rows += New-Object PSObject -Property @{ Product = 'AutoCAD'; Series = $group.Name; Supported = ($supported.Count -gt 0) }
    [void]$list.Items.Add($label, ($supported.Count -gt 0 -and $savedSeries -contains $group.Name))
}
$savedYears = @()
if ($zwcadRoot) { $savedYears = @(Get-ZwcadSavedSelection) }
foreach ($group in @($zwHosts | Group-Object Year | Sort-Object Name)) {
    $label = '中望 ZWCAD ' + $group.Name + '  |  ' + (($group.Group | ForEach-Object { Split-Path -Parent $_.Executable } | Sort-Object -Unique) -join '; ')
    $rows += New-Object PSObject -Property @{ Product = 'ZWCAD'; Series = $group.Name; Supported = $true }
    [void]$list.Items.Add($label, ($savedYears -contains [int]$group.Name))
}
$list.Add_ItemCheck({ if (-not $rows[$_.Index].Supported) { $_.NewValue = [Windows.Forms.CheckState]::Unchecked } })
$status = New-Object Windows.Forms.Label
$status.SetBounds(24, 357, 712, 58)
$status.Anchor = 'Bottom,Left,Right'
$status.Text = if ($rows.Count -eq 0) { '未检测到受支持的 CAD。请先安装并启动 CAD，再重新打开安装器。' } else { '请保存图纸并正常关闭所选产品的 CAD 后安装。安装完成后重新启动 CAD。' }
$form.Controls.Add($status)
$all = New-Object Windows.Forms.Button
$all.Text = '全选 / 清空'
$all.SetBounds(24, 428, 125, 34)
$all.Anchor = 'Bottom,Left'
$all.Add_Click({
    $check = $list.CheckedIndices.Count -lt @($rows | Where-Object { $_.Supported }).Count
    for ($i = 0; $i -lt $rows.Count; $i++) { $list.SetItemChecked($i, ($check -and $rows[$i].Supported)) }
})
$form.Controls.Add($all)
$install = New-Object Windows.Forms.Button
$install.Text = '安装所选版本'
$install.SetBounds(468, 428, 150, 34)
$install.Anchor = 'Bottom,Right'
$install.Enabled = @($rows | Where-Object { $_.Supported }).Count -gt 0
$form.Controls.Add($install)
$close = New-Object Windows.Forms.Button
$close.Text = '关闭'
$close.SetBounds(632, 428, 104, 34)
$close.Anchor = 'Bottom,Right'
$close.Add_Click({ $form.Close() })
$form.Controls.Add($close)
$script:child = $null
$script:busy = $false
$script:jobs = @()
$script:jobIndex = 0
$script:results = @()
$script:anyFailed = $false
$timer = New-Object Windows.Forms.Timer
$timer.Interval = 200
$form.Add_FormClosing({ if ($script:busy) { $_.Cancel = $true } })
function Start-NextCadInstall {
    $job = $script:jobs[$script:jobIndex]
    $script:output = Join-Path $root ($job.Product + '-installer-output.txt')
    $script:errorLog = Join-Path $root ($job.Product + '-installer-error.txt')
    $previous = $env:XYCAD_INSTALLER_AUTOCONFIRM
    try {
        $env:XYCAD_INSTALLER_AUTOCONFIRM = '1'
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = New-Object Security.Principal.WindowsPrincipal($identity)
        if ($job.Product -eq 'ZWCAD' -and -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
            $arguments = '-NoProfile -STA -ExecutionPolicy Bypass -File "' + (Join-Path $root 'installer-elevated.ps1') + '" -Installer "' + $job.Installer + '" -SelectedYears ' + $job.Selection + ' -LogRoot "' + $root + '" -ExpectedUserSid ' + $identity.User.Value
            $script:child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -PassThru
        } else {
            $script:child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $job.Arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput $script:output -RedirectStandardError $script:errorLog
        }
        $null = $script:child.Handle
    } finally { $env:XYCAD_INSTALLER_AUTOCONFIRM = $previous }
    $status.Text = (($script:results -join '；') + "`r`n正在安装 $($job.Product)，请稍候……").Trim()
    $timer.Start()
}
$install.Add_Click({
    try {
        $selected = @($list.CheckedIndices | ForEach-Object { $rows[$_] })
        if ($selected.Count -eq 0) { $status.Text = '请至少勾选一个版本。'; return }
        $script:jobs = @(New-CadInstallPlan -Rows $selected -AutoCadRoot $autoCadRoot -ZwcadRoot $zwcadRoot)
        foreach ($job in $script:jobs) {
            if (@(Get-Process -Name $job.Processes -ErrorAction SilentlyContinue).Count -gt 0) {
                $status.Text = "$($job.Product) 仍在运行。请保存图纸并正常退出，再点击安装。"; return
            }
        }
        $script:jobIndex = 0
        $script:results = @()
        $script:anyFailed = $false
        $script:busy = $true
        $list.Enabled = $false; $all.Enabled = $false; $install.Enabled = $false; $close.Enabled = $false
        Start-NextCadInstall
    } catch {
        $script:busy = $false
        $close.Enabled = $true
        $status.Text = $_.Exception.Message
    }
})
$timer.Add_Tick({
    if (-not $script:child.HasExited) { return }
    $timer.Stop()
    $script:child.WaitForExit()
    $code = $script:child.ExitCode
    $script:child.Dispose(); $script:child = $null
    $product = $script:jobs[$script:jobIndex].Product
    if ($code -eq 0) {
        $script:results += "$product 安装成功"
    } else {
        $script:anyFailed = $true
        $script:results += "$product 安装失败（$code）"
        $details = if (Test-Path -LiteralPath $script:errorLog) { [IO.File]::ReadAllText($script:errorLog) } else { '安装程序未能写入日志，请检查 Windows 权限提示。' }
        [void][Windows.Forms.MessageBox]::Show(($details + "`r`n日志：$script:errorLog"), "$product 安装未完成")
    }
    $script:jobIndex++
    if ($script:jobIndex -lt $script:jobs.Count) {
        try { Start-NextCadInstall; return }
        catch { $script:anyFailed = $true; $script:results += ($script:jobs[$script:jobIndex].Product + ' 未启动：' + $_.Exception.Message) }
    }
    $script:busy = $false
    $close.Enabled = $true
    $install.Text = '安装已结束'
    $nextStep = if ($script:anyFailed) { '成功的产品重启 CAD 后生效。失败产品请查看日志，关闭窗口后重试。' } else { '请重新启动 CAD，输入 XYLP 打开轮盘。' }
    $status.Text = ($script:results -join '；') + "`r`n" + $nextStep
})
$supportedIndices = @()
for ($index = 0; $index -lt $rows.Count; $index++) {
    if ($rows[$index].Supported) { $supportedIndices += $index }
}
if ($rows.Count -eq 1 -and $supportedIndices.Count -eq 1) {
    # Skip selection only when there is exactly one detected product/year in total.
    $list.SetItemChecked($supportedIndices[0], $true)
    $single = $rows[$supportedIndices[0]]
    $year = if ($single.Product -eq 'AutoCAD') { Get-AutoCadYear $single.Series } else { $single.Series }
    $title.Text = "安装到 $($single.Product) $year"
    $hint.Visible = $false
    $list.Visible = $false
    $all.Visible = $false
    $form.MinimumSize = New-Object Drawing.Size(700, 240)
    $form.ClientSize = New-Object Drawing.Size(760, 220)
    $status.SetBounds(24, 76, 712, 78)
    $install.SetBounds(468, 168, 150, 34)
    $close.SetBounds(632, 168, 104, 34)
    $install.Text = '继续安装'
    $form.Add_Shown({ $install.PerformClick() })
}
try { [void]$form.ShowDialog() }
finally { $timer.Dispose(); $form.Dispose() }
