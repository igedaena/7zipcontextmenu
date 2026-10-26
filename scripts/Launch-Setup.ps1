[CmdletBinding()]
param([ValidateSet('Install','Uninstall')][string]$Action='Install')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
if ($Action -eq 'Install') {
    $executable=Join-Path $projectRoot 'dist\SevenZipExtension-Setup-0.4.1.exe'
} else {
    $hive=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
    try {
        $key=$hive.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\{E59B3E67-D2E0-4B08-B1DF-A3D277C53D18}_is1')
        if (-not $key) { throw 'No all-user installation was found. Use Windows Installed apps to manage other versions.' }
        try { $directory=[string]$key.GetValue('InstallLocation') } finally { $key.Dispose() }
    } finally { $hive.Dispose() }
    if ([string]::IsNullOrWhiteSpace($directory)) { throw 'The public install location is missing.' }
    $executable=Join-Path $directory 'unins000.exe'
}
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Setup executable not found: $executable" }
# This is an interactive installer/uninstaller explicitly launched by the user.
$process=Start-Process -FilePath $executable -Verb RunAs -Wait -PassThru
exit $process.ExitCode
