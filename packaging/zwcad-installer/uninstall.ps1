param([switch]$ForceClose)

$ErrorActionPreference = 'Stop'
$appName = 'XiaoYangZWCADRadialMenu'
$running = @(Get-Process -Name 'ZWCAD' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    $close = $ForceClose
    if (-not $close) {
        $answer = Read-Host '检测到中望CAD正在运行。是否强制关闭后卸载？输入 Y 确认，其他键取消'
        $close = $answer -match '^(Y|YES|是)$'
    }
    if (-not $close) { throw '中望CAD仍在运行，卸载已取消。' }
    $running | Stop-Process -Force
}

$removed = 0
foreach ($root in @('HKLM:\SOFTWARE\ZWSOFT\ZWCAD','HKLM:\SOFTWARE\ZWSOFT\ZWCADM','HKLM:\SOFTWARE\WOW6432Node\ZWSOFT\ZWCAD','HKLM:\SOFTWARE\WOW6432Node\ZWSOFT\ZWCADM','HKCU:\SOFTWARE\ZWSOFT\ZWCAD','HKCU:\SOFTWARE\ZWSOFT\ZWCADM')) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    foreach ($versionKey in Get-ChildItem -LiteralPath $root -ErrorAction SilentlyContinue) {
        foreach ($localeKey in Get-ChildItem -LiteralPath $versionKey.PSPath -ErrorAction SilentlyContinue) {
            $appKey = Join-Path $localeKey.PSPath ('Applications\' + $appName)
            if (Test-Path -LiteralPath $appKey) {
                Remove-Item -LiteralPath $appKey -Recurse -Force
                $removed++
            }
        }
    }
}
Write-Host "已删除 $removed 个中望CAD自动加载注册项。"
Write-Host '插件文件、配置和日志仍保留在当前解压目录；需要时可重新安装。'



