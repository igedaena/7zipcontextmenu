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
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$testRoot=Join-Path $root ('.review\native-progress-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$compiler=Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$exe=Join-Path $testRoot 'native_progress_tests.exe'
& $compiler /nologo /target:exe /main:NativeProgressTests /warnaserror+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Management.dll ("/out:"+$exe) (Join-Path $root 'tools\native_progress_tests.cs') (Join-Path $root 'src\worker\Worker.cs') (Join-Path $root 'src\worker\Settings.cs') (Join-Path $root 'src\worker\NativeCommands.cs') (Join-Path $root 'src\worker\RuntimeSafety.cs')
if($LASTEXITCODE -ne 0){throw 'Native progress test compilation failed'}
& $exe $testRoot | Tee-Object -FilePath (Join-Path $testRoot 'native-progress.log')
if($LASTEXITCODE -ne 0){throw ('Native progress tests failed: '+$testRoot)}
[pscustomobject]@{Success=$true;Version='0.4.2';TestRoot=$testRoot;Real7ZipGui=$true;NativePassword=$true;NativeCancel=$true;CollisionPreserved=$true;RegistrationChanged=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root '.tools\native-progress-test-results.json') -Encoding UTF8