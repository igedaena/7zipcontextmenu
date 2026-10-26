[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$classId = '{A5CF4977-65A2-4C35-B147-EB2239F4B720}'
$className = 'SevenZipContextMenu'
$machineHive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
try {
    $publicEntry = $machineHive.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Uninstall\{E59B3E67-D2E0-4B08-B1DF-A3D277C53D18}_is1')
    if ($publicEntry) { $publicEntry.Dispose(); throw 'An all-user version is installed. Use the public setup EXE instead of shadowing it with a per-user development registration.' }
} finally { $machineHive.Dispose() }
$build64 = Join-Path $projectRoot 'build\x64\Release'
$build32 = Join-Path $projectRoot 'build\Win32\Release'
foreach ($file in @((Join-Path $build64 'SevenZipContextMenu.dll'), (Join-Path $build32 'SevenZipContextMenu.dll'),
    (Join-Path $build64 'SevenZipContextWorker.exe'), (Join-Path $build64 'SevenZipContextWorker.exe.config'))) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Build output missing: $file" }
}

$installRoot = Join-Path $projectRoot ('out\app-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
foreach ($architecture in @('x64','x86')) { New-Item -ItemType Directory -Path (Join-Path $installRoot $architecture) -Force | Out-Null }
Copy-Item -LiteralPath (Join-Path $build64 'SevenZipContextMenu.dll') -Destination (Join-Path $installRoot 'x64\SevenZipContextMenu.dll')
Copy-Item -LiteralPath (Join-Path $build32 'SevenZipContextMenu.dll') -Destination (Join-Path $installRoot 'x86\SevenZipContextMenu.dll')
Copy-Item -LiteralPath (Join-Path $build64 'SevenZipContextWorker.exe'),(Join-Path $build64 'SevenZipContextWorker.exe.config') -Destination $installRoot

# Preflight both views before writing any registry keys.
foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)) {
    $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
    try {
        $class = $hive.OpenSubKey("Software\Classes\CLSID\$classId")
        if ($class) { try { if ($class.GetValue('') -ne $className) { throw 'The CLSID is already owned by another registration.' } } finally { $class.Dispose() } }
        foreach ($type in @('*','Directory')) {
            $handler = $hive.OpenSubKey("Software\Classes\$type\shellex\ContextMenuHandlers\$className")
            if ($handler) { try { if ($handler.GetValue('') -ne $classId) { throw 'The menu handler name is already in use.' } } finally { $handler.Dispose() } }
        }
    } finally { $hive.Dispose() }
}

foreach ($pair in @(@([Microsoft.Win32.RegistryView]::Registry64,'x64'),@([Microsoft.Win32.RegistryView]::Registry32,'x86'))) {
    $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$pair[0])
    try {
        $class = $hive.CreateSubKey("Software\Classes\CLSID\$classId")
        try { $class.SetValue('', $className) } finally { $class.Dispose() }
        $server = $hive.CreateSubKey("Software\Classes\CLSID\$classId\InprocServer32")
        try { $server.SetValue('',(Join-Path $installRoot ($pair[1] + '\SevenZipContextMenu.dll'))); $server.SetValue('ThreadingModel','Apartment') } finally { $server.Dispose() }
        foreach ($type in @('*','Directory')) {
            $handler = $hive.CreateSubKey("Software\Classes\$type\shellex\ContextMenuHandlers\$className")
            try { $handler.SetValue('',$classId) } finally { $handler.Dispose() }
        }
    } finally { $hive.Dispose() }
}
New-Item -ItemType Directory -Path (Join-Path $projectRoot '.tools') -Force | Out-Null
[pscustomobject]@{InstallRoot=$installRoot; ClassId=$classId; Scope='CurrentUser'; InstalledAt=(Get-Date).ToString('o')} |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\menu-installation.json') -Encoding UTF8
Add-Type -Namespace SevenZipMenuInstall -Name Shell -MemberDefinition '[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, System.IntPtr item1, System.IntPtr item2);'
[SevenZipMenuInstall.Shell]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
Write-Output "Registered classic menus for the current user: $installRoot"
Write-Output 'If the menu does not refresh, close and reopen the file manager you are using.'
