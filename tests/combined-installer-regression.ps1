param([switch]$Single, [switch]$AutoCadOnly, [switch]$MixedSupport)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$run = Join-Path $repo ('work\combined-ui-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $run 'AutoCAD'),(Join-Path $run 'ZWCAD') | Out-Null
foreach ($name in @('installer-compat.ps1','autocad-selection.ps1','installer-plan.ps1','installer-elevated.ps1')) {
    Copy-Item -LiteralPath (Join-Path $repo ('packaging\' + $name)) -Destination $run
}
$acadStub = @'
param([string]$SelectedSeries)
[IO.File]::AppendAllText((Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'called.txt'), $SelectedSeries + "`r`n")
'@
$zwStub = @'
param([string]$SelectedYears,[switch]$Quiet)
if (-not $Quiet) { throw 'Quiet missing.' }
[IO.File]::AppendAllText((Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'called.txt'), $SelectedYears + "`r`n")
'@
[IO.File]::WriteAllText((Join-Path $run 'AutoCAD\install.ps1'),$acadStub,[Text.Encoding]::UTF8)
[IO.File]::WriteAllText((Join-Path $run 'ZWCAD\install.ps1'),$zwStub,[Text.Encoding]::UTF8)
$detection = @'
function Get-ZwcadInstallations {
    foreach ($year in @(2020,2024)) { New-Object PSObject -Property @{Year=$year;Executable="D:\中望\ZWCAD $year\ZWCAD.exe"} }
}
function Get-ZwcadSavedSelection { @() }
'@
[IO.File]::WriteAllText((Join-Path $run 'ZWCAD\zwcad-selection.ps1'),$detection,[Text.Encoding]::UTF8)
$text = [IO.File]::ReadAllText((Join-Path $repo 'packaging\installer-ui.ps1'))
$fixture = @'
function Get-Process { @() }
$hosts = @('R24.0','R24.3') | ForEach-Object { New-Object PSObject -Property @{Series=$_;Supported=$true;Path=('C:\Autodesk\AutoCAD '+$_+'\acad.exe')} }
'@
$text=$text.Replace('$hosts = @(Find-AutoCadInstallations | Sort-Object Series, Path)',$fixture)
if ($MixedSupport) {
    $mixedFixture = @'
function Get-Process { @() }
$hosts = @('R18.0','R25.0') | ForEach-Object { New-Object PSObject -Property @{Series=$_;Supported=$true;Path=('E:\自定义目录\AutoCAD '+$_+'\acad.exe')} }
'@
    $text = $text.Replace($fixture, $mixedFixture)
    $detection = $detection.Replace('@(2020,2024)', '@()')
    [IO.File]::WriteAllText((Join-Path $run 'ZWCAD\zwcad-selection.ps1'),$detection,[Text.Encoding]::UTF8)
}
if ($Single) {
    if ($AutoCadOnly) {
        $text = $text.Replace("@('R24.0','R24.3')", "@('R24.3')")
        $detection = $detection.Replace('@(2020,2024)', '@()')
    } else {
        $text = $text.Replace("@('R24.0','R24.3')", "@()")
        $detection = $detection.Replace('@(2020,2024)', '@(2020)')
    }
    [IO.File]::WriteAllText((Join-Path $run 'ZWCAD\zwcad-selection.ps1'),$detection,[Text.Encoding]::UTF8)
}
$text=$text.Replace('[void][Windows.Forms.MessageBox]::Show(($details + "`r`n日志：$script:errorLog"), "$product 安装未完成")','$script:failureDialogShown = $true')
$test = @'
$form.Show()
[Windows.Forms.Application]::DoEvents()
if ($rows.Count -ne 4) { throw 'Both products were not displayed.' }
$all.PerformClick()
if ($list.CheckedItems.Count -ne 4) { throw 'Combined select-all failed.' }
$all.PerformClick()
$list.SetItemChecked(1,$true)
$list.SetItemChecked(2,$true)
$bitmap = New-Object Drawing.Bitmap($form.Width,$form.Height)
$form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height)))
$bitmap.Save((Join-Path $root 'combined-installer.png'),[Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
$install.PerformClick()
$deadline = (Get-Date).AddSeconds(20)
while ($script:busy -and (Get-Date) -lt $deadline) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }
if ($script:busy) { throw 'Combined installation queue hung.' }
if ($script:results.Count -ne 2 -or ($script:results -join ';') -ne 'AutoCAD 安装成功;ZWCAD 安装成功') { throw ('Unexpected results: ' + ($script:results -join ';')) }
if ([IO.File]::ReadAllText((Join-Path $autoCadRoot 'called.txt')).Trim() -ne 'R24.3') { throw 'Wrong AutoCAD selection dispatched.' }
if ([IO.File]::ReadAllText((Join-Path $zwcadRoot 'called.txt')).Trim() -ne '2020') { throw 'Wrong ZWCAD selection dispatched.' }
$list.Enabled=$true; $install.Enabled=$true
for ($i=0;$i -lt $rows.Count;$i++) { $list.SetItemChecked($i,$false) }
$list.SetItemChecked(3,$true)
[IO.File]::AppendAllText((Join-Path $zwcadRoot 'install.ps1'),"`r`nWrite-Error 'expected fixture failure'; exit 7")
$install.PerformClick()
$deadline = (Get-Date).AddSeconds(20)
while ($script:busy -and (Get-Date) -lt $deadline) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }
if ($script:busy -or $script:results.Count -ne 1 -or $script:results[0] -ne 'ZWCAD 安装失败（7）') { throw 'Single-product failure reporting failed.' }
if (-not $script:failureDialogShown) { throw 'Failure details were not displayed.' }
if ([IO.File]::ReadAllLines((Join-Path $autoCadRoot 'called.txt')).Count -ne 1) { throw 'Unselected AutoCAD product was touched.' }
$form.Close()
Write-Host ('PASS: combined detection, mixed selection, quiet dispatch, sequential results, failure report, untouched product. Evidence: ' + $root)
'@
$text=$text.Replace('[void]$form.ShowDialog()',$test)
if ($MixedSupport) {
    $mixedTest = @'
$form.Show()
$deadline = (Get-Date).AddMilliseconds(500)
while ((Get-Date) -lt $deadline) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 25 }
if ($rows.Count -ne 2 -or -not $list.Visible -or -not $all.Visible -or -not $hint.Visible) { throw '2010 + 2025 must display version selection.' }
if ($script:busy -or $script:child -or $script:results.Count -ne 0 -or (Test-Path -LiteralPath (Join-Path $autoCadRoot 'called.txt'))) { throw 'Mixed support started installation before confirmation.' }
if ($list.Items[0] -notmatch 'AutoCAD 2010' -or $list.Items[0] -match '不支持' -or $list.Items[1] -notmatch 'AutoCAD 2025') { throw 'Supported 2010/2025 choices missing.' }
$all.PerformClick()
if ($list.CheckedItems.Count -ne 2) { throw 'Select all must include 2010 and 2025.' }
$all.PerformClick()
$install.PerformClick()
if ($status.Text -ne '请至少勾选一个版本。' -or $script:child) { throw 'Empty selection must not install the newest version.' }
$list.SetItemChecked(0,$true)
$status.Text = '请保存图纸并正常关闭所选产品的 CAD 后安装。安装完成后重新启动 CAD。'
$bitmap = New-Object Drawing.Bitmap($form.Width,$form.Height)
$form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height)))
$bitmap.Save((Join-Path $root 'autocad-2010-2025.png'),[Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
$install.PerformClick()
$deadline = (Get-Date).AddSeconds(20)
while ($script:busy -and (Get-Date) -lt $deadline) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }
if ($script:busy -or $script:results.Count -ne 1 -or $script:results[0] -ne 'AutoCAD 安装成功') { throw 'Confirmed supported version did not finish.' }
if ([IO.File]::ReadAllText((Join-Path $autoCadRoot 'called.txt')).Trim() -ne 'R18.0') { throw 'Installation did not use the confirmed older version.' }
if (Test-Path -LiteralPath (Join-Path $zwcadRoot 'called.txt')) { throw 'Unselected product was touched.' }
$form.Close()
Write-Host ('PASS: AutoCAD 2010 + 2025 waits for confirmation, allows both, blocks empty selection and dispatches only the selected 2010. Evidence: ' + $root)
'@
    $text = $text.Replace($test, $mixedTest)
}
if ($Single) {
    $singleTest = @'
$form.Show()
[Windows.Forms.Application]::DoEvents()
$deadline = (Get-Date).AddSeconds(20)
while ($script:busy -and (Get-Date) -lt $deadline) { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }
if ($list.Visible -or $all.Visible -or $hint.Visible) { throw 'Single-version selection UI was shown.' }
if ($script:busy -or $script:results.Count -ne 1 -or $script:results[0] -ne ($rows[0].Product + ' 安装成功')) { throw 'Single version was not installed automatically.' }
$selectedRoot = if ($rows[0].Product -eq 'AutoCAD') { $autoCadRoot } else { $zwcadRoot }
$untouchedRoot = if ($rows[0].Product -eq 'AutoCAD') { $zwcadRoot } else { $autoCadRoot }
if (Test-Path -LiteralPath (Join-Path $untouchedRoot 'called.txt')) { throw 'Unselected product was touched.' }
if ([IO.File]::ReadAllText((Join-Path $selectedRoot 'called.txt')).Trim() -ne $rows[0].Series) { throw 'Wrong automatic selection.' }
$bitmap = New-Object Drawing.Bitmap($form.Width,$form.Height)
$form.DrawToBitmap($bitmap,(New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height)))
$bitmap.Save((Join-Path $root 'single-installer.png'),[Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
$form.Close()
Write-Host ('PASS: single version installs automatically without selection UI. Evidence: ' + $root)
'@
    $text = $text.Replace($test, $singleTest)
}
$file=Join-Path $run 'installer-ui.ps1'
[IO.File]::WriteAllText($file,$text,[Text.Encoding]::UTF8)
& $file
