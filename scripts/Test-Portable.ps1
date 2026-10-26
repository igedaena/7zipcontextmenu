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
$projectRoot=Split-Path -Parent $PSScriptRoot
$build=Get-Content -LiteralPath (Join-Path $projectRoot '.tools\portable-build.json') -Raw | ConvertFrom-Json
$caseRoot=Join-Path $projectRoot ('.review\portable-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
[void][IO.Directory]::CreateDirectory($caseRoot)
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($build.Path,$caseRoot)
$package=(Get-ChildItem -LiteralPath $caseRoot -Directory | Select-Object -First 1).FullName
$moved=Join-Path $caseRoot '옮긴 폴더'
Copy-Item -LiteralPath $package -Destination $moved -Recurse
$testId=[Guid]::NewGuid().ToString('N')
$testBase='Software\SevenZipContextMenu\PortableTests\'+$testId
$hive=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
$classId='{A5CF4977-65A2-4C35-B147-EB2239F4B720}'
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Name) { if(-not $Condition){throw "FAIL: $Name"};$checks.Add($Name);Write-Output "PASS: $Name" }
function Invoke-Menu([string]$Root,[string]$Action,[switch]$Fail,[switch]$ExpectFailure) {
    $params=@('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $Root 'scripts\Portable-Menu.ps1'),'-Action',$Action,'-TestRegistryRoot',$testBase)
    if($Fail){$params+='-TestFailAfterFirstView'}
    $savedPreference=$ErrorActionPreference
    try { $ErrorActionPreference='Continue'; $output=& powershell.exe @params 2>&1; $code=$LASTEXITCODE }
    finally { $ErrorActionPreference=$savedPreference }
    if($ExpectFailure){Check ($code -ne 0) ('Refused/rolled back: '+$Action);return}
    if($code -ne 0){throw ($output -join "`n")}
}
function Read-Value([string]$Path,[string]$Name='') {
    $key=$hive.OpenSubKey($testBase+'\'+$Path)
    if(-not $key){return $null};try{return $key.GetValue($Name)}finally{$key.Dispose()}
}
function Set-TestValue([string]$Path,[string]$Name,[string]$Value) {
    $key=$hive.CreateSubKey($testBase+'\'+$Path);try{$key.SetValue($Name,$Value)}finally{$key.Dispose()}
}
try {
    $manifest=Get-Content -LiteralPath (Join-Path $package 'files.sha256.json') -Raw | ConvertFrom-Json
    foreach($file in $manifest){Check ((Get-FileHash -LiteralPath (Join-Path $package $file.Path) -Algorithm SHA256).Hash -eq $file.SHA256) ('ZIP hash: '+$file.Path)}
    Invoke-Menu $package Register
    foreach($view in @('Registry64','Registry32')) {
        $architecture=if($view -eq 'Registry64'){'x64'}else{'x86'}
        Check ((Read-Value "$view\Classes\CLSID\$classId\InprocServer32") -eq (Join-Path $package ($architecture+'\SevenZipContextMenu.dll'))) ('Correct absolute path: '+$view)
        Check ((Read-Value "$view\Classes\*\shellex\ContextMenuHandlers\SevenZipContextMenu") -eq $classId) ('Handler: '+$view)
    }
    $shortcut=Join-Path ([IO.Path]::GetTempPath()) ('SevenZipPortableTest-'+$testId+'\7-Zip 확장 설정.lnk')
    $shell=New-Object -ComObject WScript.Shell
    Check ((Test-Path -LiteralPath $shortcut) -and $shell.CreateShortcut($shortcut).Arguments -eq '--settings') 'Isolated settings shortcut'
    Invoke-Menu $moved Register -Fail -ExpectFailure
    Check ((Read-Value "Registry64\Classes\CLSID\$classId" 'PortableRoot') -eq $package -and (Read-Value "Registry32\Classes\CLSID\$classId" 'PortableRoot') -eq $package) 'Failed relocation restores both views'
    Invoke-Menu $moved Register
    Check ((Read-Value "Registry64\Classes\CLSID\$classId" 'PortableRoot') -eq $moved -and (Read-Value "Registry32\Classes\CLSID\$classId" 'PortableRoot') -eq $moved) 'Relocation registration'
    Check ($shell.CreateShortcut($shortcut).TargetPath -eq (Join-Path $moved 'SevenZipContextWorker.exe')) 'Shortcut follows relocation'
    Invoke-Menu $moved Unregister -Fail -ExpectFailure
    Check ((Read-Value "Registry64\Classes\CLSID\$classId" 'PortableRoot') -eq $moved -and (Read-Value "Registry32\Classes\CLSID\$classId" 'PortableRoot') -eq $moved -and (Test-Path -LiteralPath $shortcut)) 'Failed unregister restores both views and shortcut'
    Invoke-Menu $moved Unregister
    Check ($null -eq (Read-Value "Registry64\Classes\CLSID\$classId") -and $null -eq (Read-Value "Registry32\Classes\CLSID\$classId")) 'Unregister removes owned classes'
    Check (-not (Test-Path -LiteralPath $shortcut)) 'Unregister removes settings shortcut'
    Check (Test-Path -LiteralPath (Join-Path $moved 'SevenZipContextWorker.exe')) 'Unregister preserves program files'
    Invoke-Menu $moved Unregister
    Check $true 'Repeated unregister'
    Set-TestValue "Machine\Registry64\Classes\CLSID\$classId" '' 'Installed public version'
    Invoke-Menu $package Register -ExpectFailure
    Check ($null -eq (Read-Value "Registry64\Classes\CLSID\$classId")) 'Public conflict changes no user class'
    $hive.DeleteSubKeyTree($testBase+'\Machine',$false)
    Set-TestValue "Registry32\Classes\CLSID\$classId" '' 'Foreign owner'
    Invoke-Menu $package Register -ExpectFailure
    Invoke-Menu $package Unregister -ExpectFailure
    Check ((Read-Value "Registry32\Classes\CLSID\$classId") -eq 'Foreign owner' -and $null -eq (Read-Value "Registry64\Classes\CLSID\$classId")) 'Foreign owner preserved before either view is changed'
    $requestRoot=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'SevenZipContextMenu\Requests'
    [void][IO.Directory]::CreateDirectory($requestRoot)
    $request=Join-Path $requestRoot ([Guid]::NewGuid().ToString('N')+'.bin')
    $source=Join-Path $caseRoot '포터블 실행.txt'; [IO.File]::WriteAllText($source,'portable fixture')
    $report=Join-Path $caseRoot 'worker.json'
    $writer=[IO.BinaryWriter]::new([IO.File]::Open($request,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write),[Text.Encoding]::Unicode)
    try {$writer.Write([uint32]0x31455a37);$writer.Write([uint32]3);$writer.Write([uint32]1);$writer.Write([uint32]$source.Length);$writer.Write([Text.Encoding]::Unicode.GetBytes($source))} finally {$writer.Dispose()}
    try {
        $process=Start-Process -FilePath (Join-Path $moved 'SevenZipContextWorker.exe') -ArgumentList @('--headless','--request',('"'+$request+'"'),'--result',('"'+$report+'"')) -WindowStyle Hidden -Wait -PassThru
        $job=Get-Content -LiteralPath $report -Raw -Encoding UTF8 | ConvertFrom-Json
        Check ($process.ExitCode -eq 0 -and $job.Items[0].Success -and (Test-Path -LiteralPath $job.Items[0].Output)) 'Extracted portable worker creates ZIP after relocation'
    } finally {if(Test-Path -LiteralPath $request){[IO.File]::Delete($request)}}
    [pscustomobject]@{Success=$true;Passed=$checks.Count;Checks=$checks;TestRoot=$caseRoot;Isolation='HKCU PortableTests only; no real Classes or Start menu changes';PublicInstallationChanged=$false} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\portable-test-results.json') -Encoding UTF8
} finally {
    $hive.DeleteSubKeyTree($testBase,$false);$hive.Dispose()
    $shortcut=Join-Path ([IO.Path]::GetTempPath()) ('SevenZipPortableTest-'+$testId+'\7-Zip 확장 설정.lnk')
    if(Test-Path -LiteralPath $shortcut){[IO.File]::Delete($shortcut)}
}
