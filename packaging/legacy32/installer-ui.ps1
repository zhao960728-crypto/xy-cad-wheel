$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Add-Type -AssemblyName System.Windows.Forms
try {
    if ([IntPtr]::Size -ne 4) { throw '请运行 Win32 EXE 或随包的安装 CMD，以使用 32 位 PowerShell。' }
    & (Join-Path $root 'install.ps1') -SelectedSeries 'R18.0'
    if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    exit 0
} catch {
    [IO.File]::WriteAllText((Join-Path $root 'install-error.txt'), $_.Exception.ToString(), [Text.Encoding]::UTF8)
    [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, 'AutoCAD 2010 Win32 安装失败', 'OK', 'Error')
    exit 1
}
