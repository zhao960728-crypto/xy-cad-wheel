param([string]$Installer, [string]$SelectedYears, [string]$LogRoot, [string]$ExpectedUserSid)
$ErrorActionPreference = 'Stop'
$errorLog = Join-Path $LogRoot 'ZWCAD-installer-error.txt'
$child = $null
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    if ($identity.User.Value -ne $ExpectedUserSid) { throw '请使用当前 Windows 账号授权中望安装，不能切换账号，以免修改其他用户的配置。' }
    if ($SelectedYears -notmatch '^202[0-6](,202[0-6])*$') { throw 'Invalid ZWCAD selection.' }
    $arguments = '-NoLogo -NoProfile -NonInteractive -STA -ExecutionPolicy Bypass -File "' + $Installer + '" -Quiet -SelectedYears ' + $SelectedYears
    $child = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $LogRoot 'ZWCAD-installer-output.txt') -RedirectStandardError $errorLog
    $null = $child.Handle
    $child.WaitForExit()
    exit $child.ExitCode
} catch {
    [IO.File]::WriteAllText($errorLog, $_.Exception.Message, [Text.Encoding]::UTF8)
    exit 1
} finally { if ($child) { $child.Dispose() } }
