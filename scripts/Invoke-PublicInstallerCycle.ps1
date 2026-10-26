# Copyright (C) 2026 igedaena
# SPDX-License-Identifier: GPL-3.0-or-later
#
# This file is part of 7-Zip Extension Menu.
# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# This program is distributed in the hope that it will be useful,
# but WITHOUT ANY WARRANTY; without even the implied warranty of
# MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See LICENSE
# or https://www.gnu.org/licenses/ for the complete license.

[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator privileges are required for the public installer test.' }
$setup = Join-Path $projectRoot 'dist\SevenZipExtension-Setup-0.4.2.exe'
$target = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'SevenZipContextMenu'
$appKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{E59B3E67-D2E0-4B08-B1DF-A3D277C53D18}_is1'
$classKey = 'Software\Classes\CLSID\{A5CF4977-65A2-4C35-B147-EB2239F4B720}'
$reportPath = Join-Path $projectRoot '.tools\public-installer-admin-results.json'
$report = [ordered]@{Success=$false; InstallRoot=$target; DefaultProgramFiles=$false; InstallExitCode=$null; UninstallExitCode=$null; ReinstallExitCode=$null; Error=$null}
function Invoke-Installer([string]$File,[string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer failed: exit $($process.ExitCode)" }
    return $process.ExitCode
}
try {
    if (Test-Path -LiteralPath $target) { throw 'The public install directory already exists; the destructive install/remove cycle will not replace it.' }
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)) {
        $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view)
        try { foreach ($path in @($appKey,$classKey)) { $entry=$hive.OpenSubKey($path); if ($entry) { $entry.Dispose(); throw 'A public registration already exists; this cycle will not replace it.' } } } finally { $hive.Dispose() }
    }
    $report.InstallExitCode = Invoke-Installer $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/NOICONS',('/LOG="' + (Join-Path $projectRoot '.tools\public-install.log') + '"'))
    $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
    try {
        $entry=$hive.OpenSubKey($appKey)
        if (-not $entry) { throw 'The machine uninstall entry is missing.' }
        try { if (-not [string]::Equals(([string]$entry.GetValue('InstallLocation')).TrimEnd('\'),$target,[StringComparison]::OrdinalIgnoreCase)) { throw 'The default location was not Program Files.' } } finally { $entry.Dispose() }
    } finally { $hive.Dispose() }
    $report.DefaultProgramFiles=$true
    $uninstaller=Join-Path $target 'unins000.exe'
    if (-not (Test-Path -LiteralPath $uninstaller)) { throw 'The public uninstaller is missing.' }
    $report.UninstallExitCode = Invoke-Installer $uninstaller @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $projectRoot '.tools\public-uninstall.log') + '"'))
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)) {
        $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view)
        try { foreach ($path in @($appKey,$classKey,'Software\Classes\*\shellex\ContextMenuHandlers\SevenZipContextMenu','Software\Classes\Directory\shellex\ContextMenuHandlers\SevenZipContextMenu')) { $entry=$hive.OpenSubKey($path); if ($entry) { $entry.Dispose(); throw "Machine registration survived removal: $path" } } } finally { $hive.Dispose() }
    }
    foreach ($file in @('SevenZipContextWorker.exe','x64\SevenZipContextMenu.dll','x86\SevenZipContextMenu.dll')) {
        if (Test-Path -LiteralPath (Join-Path $target $file)) { throw "A program file survived public uninstall: $file" }
    }
    $report.ReinstallExitCode = Invoke-Installer $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + (Join-Path $projectRoot '.tools\public-reinstall.log') + '"'))
    [pscustomobject]@{InstallRoot=$target; ClassId='{A5CF4977-65A2-4C35-B147-EB2239F4B720}'; Scope='AllUsers'; InstalledAt=(Get-Date).ToString('o')} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\menu-installation.json') -Encoding UTF8
    $report.Success=$true
} catch { $report.Error=$_.Exception.Message; throw } finally { $report | ConvertTo-Json | Set-Content -LiteralPath $reportPath -Encoding UTF8 }
