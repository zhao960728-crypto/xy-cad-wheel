param(
    [Parameter(Mandatory=$true)][string]$Archive,
    [Parameter(Mandatory=$true)][ValidateSet('xiaoyang-autocad-wheel','xiaoyang-zwcad-wheel')][string]$ProductId,
    [Parameter(Mandatory=$true)][string]$Version
)
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'Expected four-part version' }
$file=Get-Item -LiteralPath $Archive
$product=if($ProductId -eq 'xiaoyang-autocad-wheel'){'小阳CAD轮盘插件'}else{'小阳ZWCAD轮盘插件'}
if($file.Name -ne ($product+'-'+$Version+'.zip')) {throw 'Manifest product/version must match archive filename'}
$manifest=[ordered]@{
    SchemaVersion=1; ProductId=$ProductId; Version=$Version
    PackageUrl='https://mc931217.top/downloads/'+[Uri]::EscapeDataString($file.Name)
    Size=$file.Length; Sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    Notes='完善先选对象再执行轮盘命令：保留预选，沿用原生命令、别名、LISP和第三方插件解析；需参数的命令继续正常交互。'
}
$json=$manifest|ConvertTo-Json
$sidecar=Join-Path $file.DirectoryName ($ProductId+'-'+$Version+'.update.txt')
if(Test-Path -LiteralPath $sidecar) {throw "Versioned backup manifest already exists: $sidecar"}
[IO.File]::WriteAllText($sidecar,$json,[Text.UTF8Encoding]::new($false))
$feed=Join-Path $PSScriptRoot 'update-feed'
New-Item -ItemType Directory -Path $feed -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $feed ($ProductId+'.json')),$json,[Text.UTF8Encoding]::new($false))
Write-Host "Update manifest: $sidecar"
