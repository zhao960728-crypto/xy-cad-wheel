$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$run = Join-Path $repo ('work\installer-ui-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run | Out-Null
foreach ($name in @('installer-compat.ps1','autocad-selection.ps1','installer-plan.ps1')) {
    Copy-Item -LiteralPath (Join-Path $repo ('packaging\' + $name)) -Destination $run
}
$text = [IO.File]::ReadAllText((Join-Path $repo 'packaging\installer-ui.ps1'))
$fixture = @'
$hosts = @('R24.0','R24.3','R25.0','R26.0','R27.0') | ForEach-Object {
    New-Object PSObject -Property @{Series=$_;Supported=($_ -ne 'R27.0');Path=('C:\Program Files\Autodesk\AutoCAD '+$_+'\acad.exe')}
}
'@
$text = $text.Replace('$hosts = @(Find-AutoCadInstallations | Sort-Object Series, Path)', $fixture)
$test = @'
$form.Show()
[Windows.Forms.Application]::DoEvents()
if ($list.Items.Count -ne 5) { throw 'Missing detected versions.' }
$all.PerformClick()
if ($list.CheckedItems.Count -ne 4) { throw 'Select all included an unsupported version.' }
$list.SetItemChecked(4,$true)
if ($list.GetItemChecked(4)) { throw 'Unsupported version could be selected.' }
$all.PerformClick()
if ($list.CheckedItems.Count -ne 0) { throw 'Clear all failed.' }
$install.PerformClick()
if ($status.Text -ne '请至少勾选一个版本。') { throw 'Empty selection was accepted.' }
$list.SetItemChecked(0,$true)
$list.SetItemChecked(1,$true)
$status.Text = '请保存图纸并正常关闭 AutoCAD 后安装。安装完成后重新启动 CAD。'
$bitmap = New-Object Drawing.Bitmap($form.Width,$form.Height)
$form.DrawToBitmap($bitmap, (New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height)))
$bitmap.Save((Join-Path $root 'installer.png'),[Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
$form.Close()
Write-Host ('PASS: UI selection, unsupported, empty guard. Screenshot: ' + (Join-Path $root 'installer.png'))
'@
$text = $text.Replace('[void]$form.ShowDialog()', $test)
$file = Join-Path $run 'installer-ui.ps1'
[IO.File]::WriteAllText($file,$text,[Text.Encoding]::UTF8)
& $file
