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

[CmdletBinding(SupportsShouldProcess=$true)]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Register','Unregister','Status')][string]$Action,
    [Parameter(DontShow=$true)][string]$TestRegistryRoot,
    [Parameter(DontShow=$true)][switch]$TestFailAfterFirstView
)
$ErrorActionPreference = 'Stop'
$appRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$classId = '{A5CF4977-65A2-4C35-B147-EB2239F4B720}'
$className = 'SevenZipContextMenu'
$owner = 'SevenZipContextMenu.Portable.v1'
$appId = '{E59B3E67-D2E0-4B08-B1DF-A3D277C53D18}_is1'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
if ([Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '일반 권한으로 실행해 주세요. 포터블 메뉴는 관리자 권한이 필요하지 않습니다.'
}
if ($TestRegistryRoot -and $TestRegistryRoot -notmatch '^Software\\SevenZipContextMenu\\PortableTests\\[a-f0-9]{32}$') { throw 'Invalid isolated test registry root.' }
if ($TestFailAfterFirstView -and -not $TestRegistryRoot) { throw 'Failure injection is allowed only in an isolated test registry.' }
$views = @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)
$handles = @()
$snapshots = @()
$mutationStarted = $false
$shortcut = $null
$shortcutBytes = $null
$shortcutExisted = $false
$mutex = [Threading.Mutex]::new($false,('Local\SevenZipContextMenu.Portable.'+$identity.User.Value))
$mutexOwned = $false

function Read-Tree($Hive,[string]$Path) {
    $key = $Hive.OpenSubKey($Path)
    if (-not $key) { return $null }
    try {
        $values = @(); $children = @()
        foreach ($name in $key.GetValueNames()) {
            $values += [pscustomobject]@{Name=$name;Value=$key.GetValue($name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);Kind=$key.GetValueKind($name)}
        }
        foreach ($child in $key.GetSubKeyNames()) { $children += [pscustomobject]@{Name=$child;Tree=(Read-Tree $Hive ($Path+'\'+$child))} }
        return [pscustomobject]@{Values=$values;Children=$children}
    } finally { $key.Dispose() }
}
function Write-Tree($Hive,[string]$Path,$Tree) {
    $Hive.DeleteSubKeyTree($Path,$false)
    if ($null -eq $Tree) { return }
    $key = $Hive.CreateSubKey($Path)
    try { foreach ($value in $Tree.Values) { $key.SetValue($value.Name,$value.Value,$value.Kind) } } finally { $key.Dispose() }
    foreach ($child in $Tree.Children) { Write-Tree $Hive ($Path+'\'+$child.Name) $child.Tree }
}
function Value-At($Hive,[string]$Path,[string]$Name='') {
    $key = $Hive.OpenSubKey($Path)
    if (-not $key) { return $null }
    try { return $key.GetValue($Name) } finally { $key.Dispose() }
}
function Set-Values($Hive,[string]$Path,[hashtable]$Values) {
    $key = $Hive.CreateSubKey($Path)
    try { foreach ($name in $Values.Keys) { $key.SetValue($name,$Values[$name],[Microsoft.Win32.RegistryValueKind]::String) } } finally { $key.Dispose() }
}
function Check-Machine($State) {
    foreach ($path in @($State.MachineClassPath,$State.UninstallPath)) {
        $key = $State.Machine.OpenSubKey($path)
        if ($key) { $key.Dispose(); throw '공용 설치가 등록되어 있습니다. Windows 앱 목록에서 기존 버전을 제거한 뒤 포터블 메뉴를 등록해 주세요.' }
    }
}
function Validate-Payload {
    if (-not [Environment]::Is64BitOperatingSystem) { throw 'Windows x64 환경용입니다.' }
    foreach ($file in @('SevenZipContextWorker.exe','SevenZipContextWorker.exe.config','x64\SevenZipContextMenu.dll','x86\SevenZipContextMenu.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $appRoot $file) -PathType Leaf)) { throw "포터블 파일이 없습니다: $file" }
    }
    $release = Value-At $handles[0].Machine 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' 'Release'
    if (-not $release -or [int]$release -lt 528040) { throw '.NET Framework 4.8 이상이 필요합니다.' }
    $candidates = @()
    foreach ($state in $handles) {
        foreach ($name in @('Path64','Path')) {
            $directory = Value-At $state.Machine 'SOFTWARE\7-Zip' $name
            if ($directory) { $candidates += $directory }
        }
    }
    foreach ($variable in @('ProgramW6432','ProgramFiles','ProgramFiles(x86)')) {
        $directory = [Environment]::GetEnvironmentVariable($variable)
        if ($directory) { $candidates += Join-Path $directory '7-Zip' }
    }
    $supported = $false
    foreach ($directory in ($candidates | Select-Object -Unique)) {
        $valid = $true
        foreach ($file in @('7z.exe','7z.dll')) {
            $path = Join-Path $directory $file
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $valid=$false; break }
            $info = [Diagnostics.FileVersionInfo]::GetVersionInfo($path)
            if ([Version]::new($info.FileMajorPart,$info.FileMinorPart,$info.FileBuildPart,$info.FilePrivatePart) -lt [Version]'26.5.0.0') { $valid=$false; break }
        }
        if ($valid) { $supported=$true; break }
    }
    if (-not $supported) { throw '7-Zip 26.05 이상을 먼저 설치하거나 업데이트해 주세요. 포터블 ZIP에는 7-Zip이 포함되어 있지 않습니다.' }
}

try {
    try { $mutexOwned=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $mutexOwned=$true }
    if (-not $mutexOwned) { throw '다른 포터블 메뉴 등록 작업이 진행 중입니다.' }
    foreach ($view in $views) {
        $user = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
        $machine = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,$view)
        $userBase='Software\Classes'; $machineBase='Software\Classes'
        $uninstall='Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$appId
        if ($TestRegistryRoot) {
            $userBase=$TestRegistryRoot+'\'+$view+'\Classes'
            # Test fixtures live in HKCU; real HKLM is never mutated by this script.
            $machineBase=$TestRegistryRoot+'\Machine\'+$view+'\Classes'
            $uninstall=$TestRegistryRoot+'\Machine\'+$view+'\Uninstall'
        }
        $handles += [pscustomobject]@{View=$view;User=$user;Machine=$machine;ClassPath=$userBase+'\CLSID\'+$classId;MachineClassPath=$machineBase+'\CLSID\'+$classId;UninstallPath=$uninstall;Handlers=@(($userBase+'\*\shellex\ContextMenuHandlers\'+$className),($userBase+'\Directory\shellex\ContextMenuHandlers\'+$className));Architecture=$(if ($view -eq [Microsoft.Win32.RegistryView]::Registry64) {'x64'} else {'x86'})}
    }
    if ($Action -eq 'Status') {
        foreach ($state in $handles) {
            [pscustomobject]@{View=[string]$state.View;Owner=(Value-At $state.User $state.ClassPath 'PortableOwner');Root=(Value-At $state.User $state.ClassPath 'PortableRoot');Dll=(Value-At $state.User ($state.ClassPath+'\InprocServer32'))}
        }
        return
    }
    if ($Action -eq 'Register') { Validate-Payload }
    # Preflight both views before changing either one.
    foreach ($state in $handles) {
        if ($Action -eq 'Register' -and -not $TestRegistryRoot) {
            # User and machine bases match in production.
            Check-Machine $state
            $legacy = $state.User.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\'+$appId)
            if ($legacy) { $legacy.Dispose(); throw '기존 사용자별 설치를 Windows 앱 목록에서 먼저 제거해 주세요.' }
        }
        if ($Action -eq 'Register' -and $TestRegistryRoot) {
            foreach ($path in @($state.MachineClassPath,$state.UninstallPath)) {
                $key=$state.User.OpenSubKey($path)
                if ($key) { $key.Dispose(); throw '공용 설치가 등록되어 있습니다.' }
            }
        }
        $tree = Read-Tree $state.User $state.ClassPath
        if ($null -ne $tree) {
            $oldRoot = Value-At $state.User $state.ClassPath 'PortableRoot'
            $oldDll = Value-At $state.User ($state.ClassPath+'\InprocServer32')
            if ((Value-At $state.User $state.ClassPath) -ne $className -or (Value-At $state.User $state.ClassPath 'PortableOwner') -ne $owner -or -not $oldRoot) {
                throw '이 클래스 ID는 포터블 등록 소유가 아닙니다. 기존 메뉴 등록을 자동으로 덮어쓰거나 제거하지 않습니다.'
            }
            $expected = Join-Path $oldRoot ($state.Architecture+'\SevenZipContextMenu.dll')
            if (-not [string]::Equals($oldDll,$expected,[StringComparison]::OrdinalIgnoreCase)) { throw '포터블 등록 경로가 소유 기록과 다릅니다. 자동 변경을 중단했습니다.' }
        }
        foreach ($path in $state.Handlers) {
            $handler=Read-Tree $state.User $path
            if ($null -ne $handler -and ($null -eq $tree -or (Value-At $state.User $path) -ne $classId)) {
                throw '이름이 같은 다른 메뉴 등록이 있어 변경하지 않습니다.'
            }
        }
        foreach ($path in @($state.ClassPath)+$state.Handlers) { $snapshots += [pscustomobject]@{Hive=$state.User;Path=$path;Tree=(Read-Tree $state.User $path)} }
    }
    $shortcutDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) '7-Zip 확장 (포터블)'
    if ($TestRegistryRoot) { $shortcutDirectory=Join-Path ([IO.Path]::GetTempPath()) ('SevenZipPortableTest-'+($TestRegistryRoot.Split('\')[-1])) }
    $shortcut=Join-Path $shortcutDirectory '7-Zip 확장 설정.lnk'
    $shortcutExisted=Test-Path -LiteralPath $shortcut
    $shell=New-Object -ComObject WScript.Shell
    if ($shortcutExisted) {
        $existingShortcut=$shell.CreateShortcut($shortcut)
        if ($existingShortcut.Description -ne $owner) { throw '같은 이름의 다른 설정 바로 가기가 있어 변경하지 않습니다.' }
        $shortcutBytes=[IO.File]::ReadAllBytes($shortcut)
    }
    if (-not $PSCmdlet.ShouldProcess($appRoot,"$Action current-user portable context menu")) { return }
    $mutationStarted=$true
    foreach ($state in $handles) {
        if ($Action -eq 'Register') {
            Set-Values $state.User $state.ClassPath @{''=$className;PortableOwner=$owner;PortableRoot=$appRoot}
            Set-Values $state.User ($state.ClassPath+'\InprocServer32') @{''=(Join-Path $appRoot ($state.Architecture+'\SevenZipContextMenu.dll'));ThreadingModel='Apartment'}
            foreach ($path in $state.Handlers) { Set-Values $state.User $path @{''=$classId} }
        } else {
            foreach ($path in $state.Handlers) { $state.User.DeleteSubKeyTree($path,$false) }
            $state.User.DeleteSubKeyTree($state.ClassPath,$false)
        }
        if ($TestFailAfterFirstView) { throw 'Simulated write failure in isolated registry.' }
    }
    if ($Action -eq 'Register') {
        [void][IO.Directory]::CreateDirectory($shortcutDirectory)
        $link=$shell.CreateShortcut($shortcut)
        $link.TargetPath=Join-Path $appRoot 'SevenZipContextWorker.exe'
        $link.Arguments='--settings'; $link.WorkingDirectory=$appRoot; $link.Description=$owner
        $link.IconLocation=(Join-Path $appRoot 'SevenZipContextWorker.exe')+',0'
        $link.Save()
    } elseif ($shortcutExisted) { [IO.File]::Delete($shortcut) }
    $mutationStarted=$false
    if (-not $TestRegistryRoot) {
        Add-Type -Namespace SevenZipPortable -Name Shell -MemberDefinition '[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, System.IntPtr item1, System.IntPtr item2);'
        [SevenZipPortable.Shell]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
    }
    Write-Output $(if ($Action -eq 'Register') {'현재 사용자용 메뉴와 설정 바로 가기를 등록했습니다.'} else {'포터블 메뉴와 설정 바로 가기를 해제했습니다. 개인 옵션과 프로그램 파일은 보존했습니다.'})
    Write-Output '메뉴가 갱신되지 않으면 사용 중인 파일관리자를 종료한 뒤 다시 실행해 주세요.'
} catch {
    if ($mutationStarted) {
        foreach ($snapshot in $snapshots) { Write-Tree $snapshot.Hive $snapshot.Path $snapshot.Tree }
        if ($shortcut) {
            if ($shortcutExisted) { [IO.File]::WriteAllBytes($shortcut,$shortcutBytes) }
            elseif (Test-Path -LiteralPath $shortcut) { [IO.File]::Delete($shortcut) }
        }
    }
    throw
} finally {
    foreach ($state in $handles) { $state.User.Dispose(); $state.Machine.Dispose() }
    if ($mutexOwned) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
