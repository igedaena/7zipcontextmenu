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
param([switch]$Force)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$version='0.4.1'
$stage=Join-Path $projectRoot ('out\portable-'+[Guid]::NewGuid().ToString('N').Substring(0,8)+'\SevenZipExtension-Portable-'+$version)
foreach($directory in @($stage,(Join-Path $stage 'x64'),(Join-Path $stage 'x86'),(Join-Path $stage 'scripts'))) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\x64\Release\SevenZipContextMenu.dll') -Destination (Join-Path $stage 'x64')
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\Win32\Release\SevenZipContextMenu.dll') -Destination (Join-Path $stage 'x86')
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\x64\Release\SevenZipContextWorker.exe'),(Join-Path $projectRoot 'build\x64\Release\SevenZipContextWorker.exe.config'),(Join-Path $projectRoot 'assets\sevenzip-extension.ico') -Destination $stage
foreach($file in @('메뉴 등록.cmd','메뉴 해제.cmd','설정 열기.cmd','사용법.txt')) { Copy-Item -LiteralPath (Join-Path $projectRoot ('portable\'+$file)) -Destination $stage }
Copy-Item -LiteralPath (Join-Path $projectRoot 'portable\scripts\Portable-Menu.ps1') -Destination (Join-Path $stage 'scripts')
foreach($file in @('LICENSE','NOTICE.txt','SOURCE.txt','THIRD_PARTY_NOTICES.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $stage }
$manifest=@(Get-ChildItem -LiteralPath $stage -File -Recurse | ForEach-Object { [pscustomobject]@{Path=$_.FullName.Substring($stage.Length+1);SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'files.sha256.json') -Encoding UTF8
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=Join-Path $projectRoot ('dist\SevenZipExtension-Portable-'+$version+'.zip')
if((Test-Path -LiteralPath $archive) -and -not $Force) { throw "Archive already exists; use -Force to replace this generated ZIP: $archive" }
$temporary=Join-Path (Split-Path -Parent $stage) 'package.zip'
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$temporary,[IO.Compression.CompressionLevel]::Optimal,$true)
Move-Item -LiteralPath $temporary -Destination $archive -Force
[pscustomobject]@{Version=$version;Path=$archive;Stage=$stage;Bytes=(Get-Item -LiteralPath $archive).Length;SHA256=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash;PayloadFiles=$manifest.Count;RegistrationExecuted=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\portable-build.json') -Encoding UTF8
Write-Output "Portable ZIP created: $archive"
