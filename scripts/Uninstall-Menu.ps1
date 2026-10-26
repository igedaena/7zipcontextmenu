[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$classId = '{A5CF4977-65A2-4C35-B147-EB2239F4B720}'
$className = 'SevenZipContextMenu'
foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64,[Microsoft.Win32.RegistryView]::Registry32)) {
    $hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,$view)
    try {
        foreach ($type in @('*','Directory')) {
            $path = "Software\Classes\$type\shellex\ContextMenuHandlers\$className"
            $handler = $hive.OpenSubKey($path)
            $owned = $false
            if ($handler) { try { $owned = $handler.GetValue('') -eq $classId } finally { $handler.Dispose() } }
            if ($owned) { $hive.DeleteSubKeyTree($path,$false) }
        }
        $classPath = "Software\Classes\CLSID\$classId"
        $class = $hive.OpenSubKey($classPath)
        $owned = $false
        if ($class) { try { $owned = $class.GetValue('') -eq $className } finally { $class.Dispose() } }
        if ($owned) { $hive.DeleteSubKeyTree($classPath,$false) }
    } finally { $hive.Dispose() }
}
Add-Type -Namespace SevenZipMenuUninstall -Name Shell -MemberDefinition '[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, System.IntPtr item1, System.IntPtr item2);'
[SevenZipMenuUninstall.Shell]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
Write-Output "Removed this project's classic-menu registrations. Existing 7-Zip settings and output files were preserved."
Write-Output 'If the menu does not refresh, close and reopen the file manager you are using. Application files remain in the project folder.'
