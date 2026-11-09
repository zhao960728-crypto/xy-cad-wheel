param([Parameter(Mandatory=$true)][string]$Archive, [Parameter(Mandatory=$true)][string]$Version, [Parameter(Mandatory=$true)][string]$Output)
$ErrorActionPreference = 'Stop'
if ([version]$Version -eq $null) { throw 'Invalid version.' }
if (Test-Path -LiteralPath $Output) { throw "EXE already exists; refusing to overwrite: $Output" }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root ('work\installer-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$metadata = Join-Path $work 'AssemblyInfo.cs'
[IO.File]::WriteAllText($metadata, ('[assembly:System.Reflection.AssemblyVersion("{0}")] [assembly:System.Reflection.AssemblyFileVersion("{0}")] [assembly:System.Reflection.AssemblyProduct("小阳 CAD 轮盘一键安装")]' -f ([version]$Version).ToString(4)), [Text.Encoding]::UTF8)
$manifest = Join-Path $work 'app.manifest'
[IO.File]::WriteAllText($manifest, '<?xml version="1.0" encoding="utf-8"?><assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1"><trustInfo xmlns="urn:schemas-microsoft-com:asm.v3"><security><requestedPrivileges><requestedExecutionLevel level="asInvoker" uiAccess="false"/></requestedPrivileges></security></trustInfo></assembly>')
$compiledOutput = Join-Path $work 'installer.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/out:$compiledOutput" "/win32manifest:$manifest" "/resource:$Archive,payload.zip" /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll (Join-Path $root 'src\XiaoYang.Installer\Program.cs') $metadata
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Move-Item -LiteralPath $compiledOutput -Destination $Output
Write-Host "EXE: $Output"
