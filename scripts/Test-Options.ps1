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
$testRoot = Join-Path $projectRoot ('.review\options-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
$keyPath = 'Software\SevenZipContextMenu'
$existing = $hive.OpenSubKey($keyPath)
$keyExisted = $null -ne $existing
$valueExisted = $existing -and $existing.GetValueNames() -contains 'OptionsV1'
$savedValue = $null; $savedKind = $null
if ($valueExisted) { $savedValue = $existing.GetValue('OptionsV1'); $savedKind = $existing.GetValueKind('OptionsV1') }
$orderExisted = $existing -and $existing.GetValueNames() -contains 'MenuOrderV1'
$savedOrder = $null; $savedOrderKind = $null
if ($orderExisted) { $savedOrder = $existing.GetValue('MenuOrderV1'); $savedOrderKind = $existing.GetValueKind('MenuOrderV1') }
if ($existing) { $existing.Dispose() }
$key = $hive.CreateSubKey($keyPath)
try {
    $key.DeleteValue('MenuOrderV1',$false)
    $compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    foreach ($fixture in @(
        @{Name='old'; Tool='26.4.0.0'; Engine='26.5.0.0'},
        @{Name='minimum'; Tool='26.5.0.0'; Engine='26.5.0.0'},
        @{Name='future'; Tool='27.0.0.0'; Engine='27.0.0.0'},
        @{Name='old-engine'; Tool='26.5.0.0'; Engine='26.4.0.0'},
        @{Name='missing-engine'; Tool='26.5.0.0'; Engine=$null},
        @{Name='unversioned'; Tool=$null; Engine='26.5.0.0'})) {
        $fixtureRoot = Join-Path $testRoot ('versions\' + $fixture.Name)
        New-Item -ItemType Directory -Path $fixtureRoot -Force | Out-Null
        foreach ($component in @(@{Name='7z.exe';Version=$fixture.Tool},@{Name='7z.dll';Version=$fixture.Engine})) {
            if ($component.Name -eq '7z.dll' -and -not $component.Version) { continue }
            $source = 'public sealed class VersionFixture {}'
            if ($component.Version) { $source = '[assembly: System.Reflection.AssemblyFileVersion("' + $component.Version + '")]' + $source }
            $sourcePath = Join-Path $fixtureRoot ($component.Name + '.cs')
            [IO.File]::WriteAllText($sourcePath,$source)
            & $compiler /nologo /target:library /optimize+ ('/out:' + (Join-Path $fixtureRoot $component.Name)) $sourcePath
            if ($LASTEXITCODE -ne 0) { throw 'Version fixture compilation failed' }
        }
    }
    $exe = Join-Path $testRoot 'options_tests.exe'
    & $compiler /nologo /target:exe /main:OptionsTests /warnaserror+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll /r:System.Core.dll /r:System.Xml.dll "/out:$exe" (Join-Path $projectRoot 'tools\options_tests.cs') (Join-Path $projectRoot 'src\worker\Worker.cs') (Join-Path $projectRoot 'src\worker\Settings.cs') (Join-Path $projectRoot 'src\worker\NativeCommands.cs') (Join-Path $projectRoot 'src\worker\RuntimeSafety.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Options test build failed' }
    & $exe $testRoot | Tee-Object -FilePath (Join-Path $testRoot 'worker-tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Options worker/UI tests failed' }
    $plain = Join-Path $testRoot 'sample.txt'; [IO.File]::WriteAllText($plain,'sample')
    $archive = Join-Path $testRoot 'sample.zip'; [IO.File]::WriteAllText($archive,'menu fixture')
    $many = @(1..100 | ForEach-Object { $p = Join-Path $testRoot ("file$_.txt"); [IO.File]::WriteAllText($p,'sample'); $p })
    $count = 0
    foreach ($architecture in @('x64','Win32')) {
        $probe = Join-Path $projectRoot ("build\$architecture\Release\shell_probe.exe")
        $dll = Join-Path $projectRoot ("build\$architecture\Release\SevenZipContextMenu.dll")
        foreach ($scenario in @(
            @{Value=0x31fff; Mode='options-direct-icons'}, @{Value=0x21fff; Mode='options-cascade-icons'},
            @{Value=0x11fff; Mode='options-direct'}, @{Value=0x1fff; Mode='options-cascade'},
            @{Value=0x10000; Mode='options-direct-off'}, @{Value=0; Mode='options-cascade-off'},
            @{Value=0x10800; Mode='options-direct-filter'},
            @{Value=0x71fff; Mode='options-direct-icons-hidden'}, @{Value=0x61fff; Mode='options-cascade-icons-hidden'},
            @{Value=0x50000; Mode='options-direct-hidden-off'}, @{Value=0x40000; Mode='options-cascade-hidden-off'})) {
            $key.SetValue('OptionsV1',[int]$scenario.Value,[Microsoft.Win32.RegistryValueKind]::DWord)
            foreach ($selection in @(@($plain),@($archive),@($testRoot),@($plain,$archive),$many)) {
                & $probe $dll $scenario.Mode 0 @selection
                if ($LASTEXITCODE -ne 0) { throw "Shell options failed: $architecture $($scenario.Mode)" }
                $count++
            }
        }
        # Bad settings fall back to defaults in the DLL, not an empty menu.
        $preview = Join-Path $testRoot 'preview.txt'; [IO.File]::WriteAllText($preview,'preview')
        [IO.File]::WriteAllText((Join-Path $testRoot 'preview.zip'),'existing ZIP')
        [void][IO.Directory]::CreateDirectory((Join-Path $testRoot 'preview (2).zip'))
        [IO.File]::WriteAllText((Join-Path $testRoot 'preview.7z'),'existing 7z')
        $key.SetValue('OptionsV1',0x31fff,[Microsoft.Win32.RegistryValueKind]::DWord)
        & $probe $dll options-direct-icons-preview 0 $preview
        if ($LASTEXITCODE -ne 0) { throw 'Output filename preview/collision check failed' }
        $count++
        $folderPreview = Join-Path $testRoot 'preview-folder'
        [void][IO.Directory]::CreateDirectory($folderPreview)
        & $probe $dll options-direct-icons-folder-preview 0 ($folderPreview+'\')
        if ($LASTEXITCODE -ne 0) { throw 'Folder output preview with trailing separator failed' }
        $count++
        $longStem = ('앞' * 33) + [char]::ConvertFromUtf32(0x1f680) + [char]::ConvertFromUtf32(0x1f600) + ('긴&이름' * 12) + ' 꼬리'
        $longPath = Join-Path $testRoot ($longStem + '.txt')
        [IO.File]::WriteAllText($longPath,'long filename')
        foreach ($suffix in @('.zip',' (2).zip','.7z')) { [IO.File]::WriteAllText((Join-Path $testRoot ($longStem+$suffix)),'existing output') }
        foreach ($layout in @('direct','cascade')) {
            $key.SetValue('OptionsV1',$(if ($layout -eq 'direct') {0x31fff} else {0x21fff}),[Microsoft.Win32.RegistryValueKind]::DWord)
            & $probe $dll ('options-'+$layout+'-icons-long-name') 0 $longPath
            if ($LASTEXITCODE -ne 0) { throw 'Long filename abbreviation failed' }
            $count++
        }
        foreach ($orderCase in @('reverse','partial','invalid','oversized')) {
            if ($orderCase -eq 'reverse') { $key.SetValue('MenuOrderV1',[string[]]@('4096','8','4','2048','1024','512','256','2','1','128','64','32','16'),[Microsoft.Win32.RegistryValueKind]::MultiString) }
            elseif ($orderCase -eq 'partial') { $key.SetValue('MenuOrderV1',[string[]]@('4096','4096','unknown','2','00016','-1'),[Microsoft.Win32.RegistryValueKind]::MultiString) }
            elseif ($orderCase -eq 'invalid') { $key.SetValue('MenuOrderV1','4096',[Microsoft.Win32.RegistryValueKind]::String) }
            else { $key.SetValue('MenuOrderV1',[string[]]@(('x'*1100)),[Microsoft.Win32.RegistryValueKind]::MultiString) }
            foreach ($layout in @('direct','cascade')) {
                $key.SetValue('OptionsV1',$(if ($layout -eq 'direct') {0x31fff} else {0x21fff}),[Microsoft.Win32.RegistryValueKind]::DWord)
                foreach ($selection in @(@($plain),@($archive),@($testRoot),@($plain,$archive),$many)) {
                    & $probe $dll ('options-'+$layout+'-icons-order-'+$orderCase) 0 @selection
                    if ($LASTEXITCODE -ne 0) { throw "Saved menu order failed: $orderCase $layout $architecture" }
                    $count++
                }
            }
        }
        $key.DeleteValue('MenuOrderV1',$false)
        foreach ($orderCase in @('default','reverse')) {
            if ($orderCase -eq 'reverse') { $key.SetValue('MenuOrderV1',[string[]]@('4096','8','4','2048','1024','512','256','2','1','128','64','32','16'),[Microsoft.Win32.RegistryValueKind]::MultiString) }
            foreach ($layout in @('direct','cascade')) {
                foreach ($compressionCase in @(
                    @{Value=0xc0c;Name='both'}, @{Value=0xc;Name='each'}, @{Value=0xc00;Name='quick'},
                    @{Value=0x408;Name='split-zip'}, @{Value=0x804;Name='split-7z'}, @{Value=0;Name='neither'})) {
                    $flags = 0x20000 -bor $compressionCase.Value
                    if ($layout -eq 'direct') { $flags = $flags -bor 0x10000 }
                    $key.SetValue('OptionsV1',$flags,[Microsoft.Win32.RegistryValueKind]::DWord)
                    foreach ($selection in @(@($plain),@($archive),@($testRoot+'\'),@($plain,$archive),@($plain,$testRoot))) {
                        & $probe $dll ('options-'+$layout+'-icons-order-'+$orderCase+'-dedupe-'+$compressionCase.Name) 0 @selection
                        if ($LASTEXITCODE -ne 0) { throw "Compression deduplication failed: $architecture $layout $orderCase $($compressionCase.Name)" }
                        $count++
                    }
                }
            }
        }
        $key.DeleteValue('MenuOrderV1',$false)
        $key.SetValue('OptionsV1','invalid',[Microsoft.Win32.RegistryValueKind]::String)
        & $probe $dll compress 0 $plain
        if ($LASTEXITCODE -ne 0) { throw 'Invalid-setting fallback failed' }
    }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    [pscustomobject]@{Success=$true; PackageVersion='0.4.3'; MenuScenarios=$count; BothArchitectures=$true; SettingsUi=$true; SavedMenuOrder=$true; ShortMenuLabels=$true; NativeCommandArguments=$true; RuntimeIsAdministrator=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); TestRoot=$testRoot; PublicInstallationChanged=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\options-test-results.json') -Encoding UTF8
} finally {
    if ($valueExisted) { $key.SetValue('OptionsV1',$savedValue,$savedKind) } else { $key.DeleteValue('OptionsV1',$false) }
    if ($orderExisted) { $key.SetValue('MenuOrderV1',$savedOrder,$savedOrderKind) } else { $key.DeleteValue('MenuOrderV1',$false) }
    $empty = $key.GetValueNames().Count -eq 0 -and $key.GetSubKeyNames().Count -eq 0
    $key.Dispose()
    if (-not $keyExisted -and $empty) { $hive.DeleteSubKey($keyPath,$false) }
    $hive.Dispose()
}
