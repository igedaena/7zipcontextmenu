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
param([switch]$ElevatedPart,[string]$CaseDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($ElevatedPart) {
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '.review')) + [IO.Path]::DirectorySeparatorChar
    $casePath = [IO.Path]::GetFullPath($CaseDirectory)
    if (-not $casePath.StartsWith($allowedRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Test directory must be inside the workspace review folder.' }
    $report = [ordered]@{Success=$false; ElevatedToken=$isAdmin; SameUser=$false; ExitCode=$null; RequestPreserved=$false; ResultAbsent=$false; BothElevatedHostsRejected=$false; Error=$null}
    try {
        $config = Get-Content -LiteralPath (Join-Path $casePath 'case.json') -Raw | ConvertFrom-Json
        $report.SameUser = $identity.User.Value -eq $config.UserSid
        if (-not $isAdmin -or -not $report.SameUser) { throw 'An elevated token for the same user is required.' }
        $worker = Join-Path $projectRoot 'build\x64\Release\SevenZipContextWorker.exe'
        $process = Start-Process -FilePath $worker -ArgumentList @('--headless','--request',('"' + $config.Request + '"'),'--result',('"' + $config.Result + '"')) -WindowStyle Hidden -Wait -PassThru
        $report.ExitCode = $process.ExitCode
        $report.RequestPreserved = Test-Path -LiteralPath $config.Request
        $report.ResultAbsent = -not (Test-Path -LiteralPath $config.Result)
        $beforeRequests = @(Get-ChildItem -LiteralPath (Split-Path -Parent $config.Request) -File | Select-Object -ExpandProperty FullName)
        foreach ($architecture in @('x64','Win32')) {
            & (Join-Path $projectRoot ("build\$architecture\Release\shell_probe.exe")) (Join-Path $projectRoot ("build\$architecture\Release\SevenZipContextMenu.dll")) compress reject-admin (Join-Path $projectRoot 'README.md')
            if ($LASTEXITCODE -ne 0) { throw "Elevated host dispatch guard failed: $architecture" }
        }
        $newRequests = @(Get-ChildItem -LiteralPath (Split-Path -Parent $config.Request) -File | Where-Object { $_.FullName -notin $beforeRequests })
        if ($newRequests.Count -ne 0) { throw 'Elevated host created request data.' }
        $report.BothElevatedHostsRejected = $true
        $report.Success = $report.ExitCode -eq 1 -and $report.RequestPreserved -and $report.ResultAbsent
        if (-not $report.Success) { throw 'An elevated worker touched the request or wrote a result.' }
    } catch { $report.Error = $_.Exception.Message }
    $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $casePath 'elevated.json') -Encoding UTF8
    if ($report.Success) { exit 0 } else { exit 1 }
}
if ($isAdmin) { throw 'Start this test under a standard user token.' }
$casePath = Join-Path $projectRoot ('.review\admin-runtime-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $casePath -Force | Out-Null
$requests = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SevenZipContextMenu\Requests'
New-Item -ItemType Directory -Path $requests -Force | Out-Null
$request = Join-Path $requests ([Guid]::NewGuid().ToString('N') + '.bin')
$result = Join-Path $casePath 'runtime-result.json'
$sample = Join-Path $projectRoot 'README.md'
$writer = [IO.BinaryWriter]::new([IO.File]::Open($request,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write),[Text.Encoding]::Unicode)
try { $writer.Write([uint32]0x31455a37); $writer.Write([uint32]0); $writer.Write([uint32]1); $writer.Write([uint32]$sample.Length); $writer.Write([Text.Encoding]::Unicode.GetBytes($sample)) } finally { $writer.Dispose() }
try {
    @{UserSid=$identity.User.Value; Request=$request; Result=$result} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $casePath 'case.json') -Encoding UTF8
    $powershell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $process = Start-Process -FilePath $powershell -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"' + $PSCommandPath + '"'),'-ElevatedPart','-CaseDirectory',('"' + $casePath + '"')) -Verb RunAs -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw 'Elevated runtime check failed; inspect the case report.' }
    $elevated = Get-Content -LiteralPath (Join-Path $casePath 'elevated.json') -Raw | ConvertFrom-Json
    if (-not $elevated.Success) { throw $elevated.Error }
    # Prove the exact same request is valid and is consumed by a standard worker.
    $worker = Join-Path $projectRoot 'build\x64\Release\SevenZipContextWorker.exe'
    $normal = Start-Process -FilePath $worker -ArgumentList @('--headless','--request',('"' + $request + '"'),'--result',('"' + $result + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($normal.ExitCode -ne 0 -or (Test-Path -LiteralPath $request) -or -not (Test-Path -LiteralPath $result)) { throw 'Standard-user control case failed.' }
    $diagnostic = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    if ($diagnostic.SelectedCount -ne 1 -or $diagnostic.SelectedPaths[0] -ne $sample) { throw 'Control request did not round-trip.' }
    [pscustomobject]@{Success=$true; ElevatedTokenRejected=$true; BothElevatedHostsRejected=$elevated.BothElevatedHostsRejected; BeforeRequestCreation=$true; BeforeRequestReadOrDelete=$true; BeforeResultWrite=$true; StandardUserControl=$true; TestRoot=$casePath} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\runtime-safety-test-results.json') -Encoding UTF8
    Write-Output 'PASS: real elevated token rejected before request consumption/result write; same request succeeds as a standard user.'
} finally { if (Test-Path -LiteralPath $request) { Remove-Item -LiteralPath $request } }
