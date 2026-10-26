[CmdletBinding()]
param(
    [switch]$DownloadOnly
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$configPath = Join-Path $projectRoot '.vsconfig'
$downloadDirectory = Join-Path $projectRoot '.tools\downloads'
$installerPath = Join-Path $downloadDirectory 'vs_buildtools_2022.exe'
$installerUri = 'https://aka.ms/vs/17/release/vs_buildtools.exe'
$resultPath = Join-Path $projectRoot '.tools\build-tools-install.json'

if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
    throw "Missing component configuration: $configPath"
}
$null = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    Write-Output 'Downloading the official Visual Studio 2022 Build Tools bootstrapper.'
    Invoke-WebRequest -Uri $installerUri -OutFile $installerPath -UseBasicParsing
}

$signature = Get-AuthenticodeSignature -LiteralPath $installerPath
if ($signature.Status -ne 'Valid' -or
    $signature.SignerCertificate.Subject -notmatch 'O=Microsoft Corporation(?:,|$)') {
    throw "The bootstrapper does not have a valid Microsoft signature: $($signature.Status)"
}

$result = [ordered]@{
    InstallerUri = $installerUri
    InstallerPath = $installerPath
    InstallerVersion = (Get-Item -LiteralPath $installerPath).VersionInfo.ProductVersion
    InstallerSHA256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash
    Signer = $signature.SignerCertificate.Subject
    SignatureStatus = [string]$signature.Status
    Configuration = $configPath
    Status = 'DownloadVerified'
    ExitCode = $null
}
$result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8

if ($DownloadOnly) {
    Write-Output "Verified installer: $installerPath"
    return
}

$arguments = @('--quiet', '--wait', '--norestart', '--nocache', '--config', ('"' + $configPath + '"'))
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$startArguments = @{
    FilePath = $installerPath
    ArgumentList = $arguments
    WorkingDirectory = $projectRoot
    WindowStyle = 'Hidden'
    Wait = $true
    PassThru = $true
}
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $startArguments.Verb = 'RunAs'
}

Write-Output 'Installing MSVC x86/x64 tools, Windows SDK 26100, and CMake tools. Windows may request administrator approval.'
$result.Status = 'Installing'
$result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
try {
    $process = Start-Process @startArguments
    $result.ExitCode = $process.ExitCode
    if ($process.ExitCode -eq 0) {
        $result.Status = 'InstallerSucceeded'
    } elseif ($process.ExitCode -eq 3010) {
        $result.Status = 'InstallerSucceededRebootRequired'
    } else {
        $result.Status = 'InstallerFailed'
        throw "Build Tools installer returned exit code $($process.ExitCode)."
    }
} catch {
    if ($result.Status -eq 'Installing') { $result.Status = 'LaunchFailedOrCancelled' }
    throw
} finally {
    $result | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
Write-Output "Installer result: $($result.Status). Build verification is still required."
