[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $projectRoot '.tools\InnoSetup\ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Run scripts\Install-InstallerCompiler.ps1 first.' }
$script = Join-Path $projectRoot 'installer\SevenZipExtension.iss'
& $compiler ('/DProjectRoot=' + $projectRoot) $script
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $projectRoot 'dist\SevenZipExtension-Setup-0.4.1.exe'
[pscustomobject]@{Path=$installer; Bytes=(Get-Item -LiteralPath $installer).Length; SHA256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\installer-build.json') -Encoding UTF8
Write-Output "Installer created: $installer"
