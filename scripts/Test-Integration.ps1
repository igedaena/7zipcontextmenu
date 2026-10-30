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
# Exercise the legacy default menu regardless of the user's current options.
$optionsHive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
$optionsKeyPath = 'Software\SevenZipContextMenu'
$previousKey = $optionsHive.OpenSubKey($optionsKeyPath)
$optionsKeyExisted = $null -ne $previousKey
$optionsValueExisted = $previousKey -and $previousKey.GetValueNames() -contains 'OptionsV1'
$optionsSavedValue = $null; $optionsSavedKind = $null
if ($optionsValueExisted) { $optionsSavedValue = $previousKey.GetValue('OptionsV1'); $optionsSavedKind = $previousKey.GetValueKind('OptionsV1') }
$menuOrderExisted = $previousKey -and $previousKey.GetValueNames() -contains 'MenuOrderV1'
$menuOrderSaved = $null; $menuOrderKind = $null
if ($menuOrderExisted) { $menuOrderSaved = $previousKey.GetValue('MenuOrderV1'); $menuOrderKind = $previousKey.GetValueKind('MenuOrderV1') }
if ($previousKey) { $previousKey.Dispose() }
$optionsKey = $optionsHive.CreateSubKey($optionsKeyPath)
try {
$optionsKey.SetValue('OptionsV1',0x2000f,[Microsoft.Win32.RegistryValueKind]::DWord)
$optionsKey.DeleteValue('MenuOrderV1',$false)
$runId = [Guid]::NewGuid().ToString('N').Substring(0,8)
$testRoot = Join-Path $projectRoot ('.review\integration-' + $runId)
$appRoot = Join-Path $projectRoot ('out\test-' + $runId)
$requestRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SevenZipContextMenu\Requests'
$sevenZip = 'C:\Program Files\7-Zip\7z.exe'
$worker = Join-Path $appRoot 'SevenZipContextWorker.exe'
$checks = [System.Collections.Generic.List[string]]::new()
foreach ($directory in @($testRoot,$requestRoot,(Join-Path $appRoot 'x64'),(Join-Path $appRoot 'x86'))) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\x64\Release\SevenZipContextWorker.exe'),(Join-Path $projectRoot 'build\x64\Release\SevenZipContextWorker.exe.config') -Destination $appRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\x64\Release\SevenZipContextMenu.dll') -Destination (Join-Path $appRoot 'x64')
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\Win32\Release\SevenZipContextMenu.dll') -Destination (Join-Path $appRoot 'x86')

function Assert-Check([bool]$Condition,[string]$Name) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $checks.Add($Name)
    Write-Output "PASS: $Name"
}
function New-TestFile([string]$Relative,[string]$Text='fixture') {
    $path = Join-Path $testRoot $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    [IO.File]::WriteAllText($path,$Text,[Text.UTF8Encoding]::new($false))
    return $path
}
function New-Archive([string]$Source,[string]$Relative,[string[]]$Extra=@()) {
    $path = Join-Path $testRoot $Relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    Push-Location -LiteralPath $Source
    try { $log = & $sevenZip a @Extra '--' $path '.' 2>&1; if ($LASTEXITCODE -ne 0) { throw ($log -join "`n") } } finally { Pop-Location }
    return $path
}
function Invoke-Worker([uint32]$Operation,[string[]]$Paths,[string]$Password=$null) {
    $id = [Guid]::NewGuid().ToString('N')
    $request = Join-Path $requestRoot ($id + '.bin')
    $report = Join-Path $requestRoot ($id + '.json')
    $stream = [IO.File]::Open($request,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write)
    $writer = [IO.BinaryWriter]::new($stream,[Text.Encoding]::Unicode)
    try {
        $writer.Write([uint32]0x31455a37); $writer.Write($Operation); $writer.Write([uint32]$Paths.Count)
        foreach ($path in $Paths) { $writer.Write([uint32]$path.Length); $writer.Write([Text.Encoding]::Unicode.GetBytes($path)) }
    } finally { $writer.Dispose() }
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $worker
    $start.Arguments = '--headless --request "' + $request + '" --result "' + $report + '"'
    if ($Password) { $start.Arguments += ' --password-stdin' }
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.RedirectStandardInput = $true
    $process = [Diagnostics.Process]::new(); $process.StartInfo = $start
    try {
        $null = $process.Start()
        if ($Password) {
            $bytes = [Text.Encoding]::UTF8.GetBytes($Password + "`n")
            $process.StandardInput.BaseStream.Write($bytes,0,$bytes.Length); $process.StandardInput.BaseStream.Flush()
        }
        $process.StandardInput.Close()
        if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Worker test timed out' }
        if (-not (Test-Path -LiteralPath $report)) { throw "Worker did not write a result; exit=$($process.ExitCode)" }
        $result = Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
        return [pscustomobject]@{ExitCode=$process.ExitCode; Report=$result}
    } finally { $process.Dispose() }
}

$singleFile = New-TestFile 'sources\single\문서.txt' '한글 단일 파일'
$folderFile = New-TestFile 'sources\wrapped\photos\a.txt' 'wrapped-a'
$null = New-TestFile 'sources\wrapped\photos\b.txt' 'wrapped-b'
$looseFile = New-TestFile 'sources\loose\a.txt' 'loose-a'
$null = New-TestFile 'sources\loose\b.txt' 'loose-b'
$single = New-Archive (Split-Path -Parent $singleFile) 'cases\single\single.zip'
$wrapped = New-Archive (Split-Path -Parent (Split-Path -Parent $folderFile)) 'cases\wrapped\different.zip'
$loose = New-Archive (Split-Path -Parent $looseFile) 'cases\loose\loose.zip'

foreach ($case in @(@($single,'문서.txt'),@($wrapped,'photos'),@($loose,'loose'))) {
    $job = Invoke-Worker 1 @($case[0])
    Assert-Check ($job.ExitCode -eq 0 -and (Split-Path -Leaf $job.Report.Items[0].Output) -eq $case[1]) ('Smart extraction: ' + $case[1])
}
Assert-Check ((Get-FileHash -LiteralPath $singleFile).Hash -eq (Get-FileHash -LiteralPath (Join-Path (Split-Path -Parent $single) '문서.txt')).Hash) 'Smart single-file content matches'
$job = Invoke-Worker 1 @($single)
Assert-Check ($job.ExitCode -eq 0 -and (Split-Path -Leaf $job.Report.Items[0].Output) -eq '문서 (2).txt') 'Existing smart-extract output is preserved'
$fixed = New-Archive (Split-Path -Parent $singleFile) 'cases\fixed\fixed.zip'
$job = Invoke-Worker 2 @($fixed)
Assert-Check ($job.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $job.Report.Items[0].Output '문서.txt'))) 'Fixed archive-name folder'
$multiSingle = New-Archive (Split-Path -Parent $singleFile) 'cases\multi\one.zip'
$multiLoose = New-Archive (Split-Path -Parent $looseFile) 'cases\multi\two.zip'
$job = Invoke-Worker 1 @($multiSingle,$multiLoose)
Assert-Check ($job.ExitCode -eq 0 -and $job.Report.Items.Count -eq 2 -and (Split-Path -Leaf $job.Report.Items[0].Output) -eq '문서.txt' -and (Split-Path -Leaf $job.Report.Items[1].Output) -eq 'two') 'Each selected archive uses its own smart destination'
$job = Invoke-Worker 2 @($multiSingle,$multiLoose)
Assert-Check ($job.ExitCode -eq 0 -and $job.Report.Items.Count -eq 2 -and (Split-Path -Leaf $job.Report.Items[0].Output) -eq 'one' -and (Split-Path -Leaf $job.Report.Items[1].Output) -eq 'two (2)') 'Each selected archive uses its own named folder'

$compress1 = New-TestFile 'cases\compress\same.txt' 'one'
$compress2 = New-TestFile 'cases\compress\same.csv' 'two'
$folderInput = Split-Path -Parent (New-TestFile 'cases\compress\사진\안녕.txt' 'folder')
$special = New-TestFile 'cases\compress\literal[1] & @name.txt' 'literal'
$job = Invoke-Worker 3 @($compress1,$compress2,$folderInput,$special)
Assert-Check ($job.ExitCode -eq 0 -and $job.Report.Items.Count -eq 4) 'Files and folders compress individually to ZIP'
Assert-Check ((Split-Path -Leaf $job.Report.Items[0].Output) -eq 'same.zip' -and (Split-Path -Leaf $job.Report.Items[1].Output) -eq 'same (2).zip') 'Equal base names produce separate archives'
foreach ($item in $job.Report.Items) { $log = & $sevenZip t '--' $item.Output 2>&1; if ($LASTEXITCODE -ne 0) { throw ($log -join "`n") } }
Assert-Check $true 'All generated ZIP archives pass 7-Zip integrity testing'
$job = Invoke-Worker 4 @($compress1,$folderInput)
Assert-Check ($job.ExitCode -eq 0 -and $job.Report.Items.Count -eq 2 -and $job.Report.Items[0].Output.EndsWith('.7z')) 'Individual 7z compression'
$tar = New-Archive (Split-Path -Parent $looseFile) 'tar-source\bundle.tar' @('-ttar')
$gzip = Join-Path $testRoot 'cases\tgz\bundle.tar.gz'
New-Item -ItemType Directory -Path (Split-Path -Parent $gzip) -Force | Out-Null
$log = & $sevenZip a -tgzip '--' $gzip $tar 2>&1
if ($LASTEXITCODE -ne 0) { throw ($log -join "`n") }
$job = Invoke-Worker 1 @($gzip)
Assert-Check ($job.ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $job.Report.Items[0].Output 'a.txt')) -and -not (Test-Path -LiteralPath (Join-Path $job.Report.Items[0].Output 'bundle.tar'))) 'TAR.GZ unwraps to final contents'

$corrupt = New-TestFile 'cases\corrupt\bad.zip' 'not an archive'
$job = Invoke-Worker 1 @($corrupt)
Assert-Check ($job.ExitCode -ne 0 -and -not $job.Report.Items[0].Success) 'Corrupt archive reports failure'
Assert-Check (@(Get-ChildItem -LiteralPath (Split-Path -Parent $corrupt) -Force -Directory).Count -eq 0) 'Failed extraction leaves no staging folder'
$encrypted = New-Archive (Split-Path -Parent $singleFile) 'cases\encrypted\secret.7z' @('-t7z','-p시험암호','-mhe=on')
$job = Invoke-Worker 1 @($encrypted) '시험암호'
Assert-Check ($job.ExitCode -eq 0 -and $job.Report.Items[0].Success) 'Encrypted headers and Unicode password through stdin'
$job = Invoke-Worker 1 @($encrypted)
Assert-Check ($job.ExitCode -eq 2 -and $job.Report.Cancelled) 'Password cancellation is not reported as success'

$emptyDirectory = Join-Path $testRoot 'sources\empty\empty-folder'
New-Item -ItemType Directory -Path $emptyDirectory -Force | Out-Null
$emptyArchive = New-Archive (Split-Path -Parent $emptyDirectory) 'cases\empty\empty.zip'
$job = Invoke-Worker 1 @($emptyArchive)
Assert-Check ($job.ExitCode -eq 0 -and (Test-Path -LiteralPath $job.Report.Items[0].Output -PathType Container) -and (Split-Path -Leaf $job.Report.Items[0].Output) -eq 'empty-folder') 'Empty top-level folder is preserved'
$zoneArchive = New-Archive (Split-Path -Parent $singleFile) 'cases\zone\zone.zip'
Set-Content -LiteralPath $zoneArchive -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3" -Encoding ASCII
$job = Invoke-Worker 1 @($zoneArchive)
$zone = Get-Content -LiteralPath $job.Report.Items[0].Output -Stream Zone.Identifier -Raw -ErrorAction Stop
Assert-Check ($job.ExitCode -eq 0 -and $zone.Contains('ZoneId=3')) 'Downloaded archive Zone.Identifier reaches extracted file'
$partialGood = New-Archive (Split-Path -Parent $singleFile) 'cases\partial\good.zip'
$partialBad = New-TestFile 'cases\partial\bad.zip' 'not an archive'
$partialLast = New-Archive (Split-Path -Parent $looseFile) 'cases\partial\last.zip'
$job = Invoke-Worker 1 @($partialGood,$partialBad,$partialLast)
Assert-Check ($job.ExitCode -eq 1 -and $job.Report.Items.Count -eq 3 -and $job.Report.Items[0].Success -and -not $job.Report.Items[1].Success -and $job.Report.Items[2].Success) 'Partial failure is reported and other items continue'
$splitSource = New-TestFile 'sources\split\large.txt' ('0123456789abcdef' * 1000)
$splitFirst = Join-Path $testRoot 'cases\split\split.7z'
New-Item -ItemType Directory -Path (Split-Path -Parent $splitFirst) -Force | Out-Null
$log = & $sevenZip a -t7z -mx0 -v1024b '--' $splitFirst $splitSource 2>&1
if ($LASTEXITCODE -ne 0) { throw ($log -join "`n") }
$volumes = @(Get-ChildItem -LiteralPath (Split-Path -Parent $splitFirst) -File | Select-Object -ExpandProperty FullName)
$job = Invoke-Worker 1 $volumes
Assert-Check ($job.ExitCode -eq 0 -and $job.Report.Items.Count -eq 1 -and (Get-FileHash -LiteralPath $job.Report.Items[0].Output).Hash -eq (Get-FileHash -LiteralPath $splitSource).Hash) 'All selected split volumes are extracted once'
$files = @(); $archives = @()
for ($i=1; $i -le 100; $i++) {
    $files += New-TestFile ('selection\file{0:D3}.txt' -f $i) ('item' + $i)
    $archivePath = Join-Path $testRoot ('selection\archive{0:D3}.zip' -f $i)
    Copy-Item -LiteralPath $single -Destination $archivePath
    $archives += $archivePath
}
foreach ($architecture in @('x64','Win32')) {
    $dllArchitecture = if ($architecture -eq 'Win32') { 'x86' } else { 'x64' }
    $probe = Join-Path $projectRoot ("build\$architecture\Release\shell_probe.exe")
    $dll = Join-Path $appRoot ("$dllArchitecture\SevenZipContextMenu.dll")
    foreach ($count in @(1,2,16,17,100)) {
        & $probe $dll compress 0 @($files[0..($count-1)])
        if ($LASTEXITCODE -ne 0) { throw "Shell probe failed: $architecture compression $count" }
        & $probe $dll extract 0 @($archives[0..($count-1)])
        if ($LASTEXITCODE -ne 0) { throw "Shell probe failed: $architecture extraction $count" }
    }
    & $probe $dll compress 0 $single $folderInput
    if ($LASTEXITCODE -ne 0) { throw 'Mixed selection probe failed' }
    $before = @(Get-ChildItem -LiteralPath $requestRoot -Filter '*.json' | Select-Object -ExpandProperty FullName)
    & $probe $dll compress 1 @($files)
    if ($LASTEXITCODE -ne 0) { throw 'Diagnostic dispatch failed' }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $newReport = $null
    do {
        $newReport = Get-ChildItem -LiteralPath $requestRoot -Filter '*.json' | Where-Object { $_.FullName -notin $before } | Select-Object -First 1
        if (-not $newReport) { Start-Sleep -Milliseconds 100 }
    } while (-not $newReport -and [DateTime]::UtcNow -lt $deadline)
    if (-not $newReport) { throw 'Diagnostic worker did not respond' }
    $diagnostic = Get-Content -LiteralPath $newReport.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Check ($diagnostic.SelectedCount -eq 100 -and ($diagnostic.SelectedPaths -join '|') -eq ($files -join '|')) ("$architecture handler sends all 100 paths to worker")
    Assert-Check $true ("$architecture menu labels, command IDs, canonical verbs, default-only behavior, mixed selection, and COM lifetime")
}
Assert-Check (Test-Path -LiteralPath $singleFile) 'Original files remain present'
[pscustomobject]@{TestRoot=$testRoot; AppRoot=$appRoot; Passed=$checks.Count; Checks=$checks; CompletedAt=(Get-Date).ToString('o')} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\integration-results.json') -Encoding UTF8
Write-Output ("Integration checks passed: " + $checks.Count)
} finally {
    if ($optionsValueExisted) { $optionsKey.SetValue('OptionsV1',$optionsSavedValue,$optionsSavedKind) } else { $optionsKey.DeleteValue('OptionsV1',$false) }
    if ($menuOrderExisted) { $optionsKey.SetValue('MenuOrderV1',$menuOrderSaved,$menuOrderKind) } else { $optionsKey.DeleteValue('MenuOrderV1',$false) }
    $optionsKeyEmpty = $optionsKey.GetValueNames().Count -eq 0 -and $optionsKey.GetSubKeyNames().Count -eq 0
    $optionsKey.Dispose()
    if (-not $optionsKeyExisted -and $optionsKeyEmpty) { $optionsHive.DeleteSubKey($optionsKeyPath,$false) }
    $optionsHive.Dispose()
}
