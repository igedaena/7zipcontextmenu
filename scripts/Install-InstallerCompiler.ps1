[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $projectRoot '.tools\InnoSetup'
if (Test-Path -LiteralPath (Join-Path $toolRoot 'ISCC.exe')) { Write-Output "Installer compiler already available: $toolRoot"; return }
$downloadRoot = Join-Path $projectRoot '.tools\downloads'
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
$installer = Join-Path $downloadRoot 'innosetup-7.1.0-x64.exe'
if (-not (Test-Path -LiteralPath $installer)) {
    $page = Invoke-WebRequest -Uri 'https://jrsoftware.org/isdl.php' -UseBasicParsing
    $download = [regex]::Match($page.Content,'href="(https://github\.com/jrsoftware/issrc/releases/download/[^"<>]+/innosetup-7\.1\.0-x64\.exe)"')
    if (-not $download.Success) { throw 'The official compiler download link could not be verified.' }
    Invoke-WebRequest -Uri $download.Groups[1].Value -OutFile $installer -UseBasicParsing
}
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
    throw "Unexpected installer compiler signature: $($signature.Status)"
}
$arguments = @('/CURRENTUSER','/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/DIR="' + $toolRoot + '"'),('/LOG="' + (Join-Path $projectRoot '.tools\inno-tool-install.log') + '"'))
$process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw "Installer compiler setup failed: $($process.ExitCode)" }
if (-not (Test-Path -LiteralPath (Join-Path $toolRoot 'ISCC.exe'))) { throw 'Installer compiler not found after setup.' }
Write-Output "Installer compiler ready: $toolRoot"
