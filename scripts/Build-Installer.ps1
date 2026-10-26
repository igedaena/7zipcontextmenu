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
$compiler = Join-Path $projectRoot '.tools\InnoSetup\ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Run scripts\Install-InstallerCompiler.ps1 first.' }
$script = Join-Path $projectRoot 'installer\SevenZipExtension.iss'
& $compiler ('/DProjectRoot=' + $projectRoot) $script
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $projectRoot 'dist\SevenZipExtension-Setup-0.4.1.exe'
[pscustomobject]@{Path=$installer; Bytes=(Get-Item -LiteralPath $installer).Length; SHA256=(Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\installer-build.json') -Encoding UTF8
Write-Output "Installer created: $installer"
