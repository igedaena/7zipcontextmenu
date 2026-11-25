# Copyright (C) 2026 igedaena
# SPDX-License-Identifier: GPL-3.0-or-later
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $projectRoot ('.review\bandizip-' + [Guid]::NewGuid().ToString('N').Substring(0,8))
[void][IO.Directory]::CreateDirectory($testRoot)
$hive = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser,[Microsoft.Win32.RegistryView]::Registry64)
$keyPath = 'Software\SevenZipContextMenu'
$key = $hive.CreateSubKey($keyPath)
$saved = @{}
foreach ($name in @('BandizipPathV1','BandizipFormatsV1','BandizipEnabledV1')) {
    if ($key.GetValueNames() -contains $name) { $saved[$name] = @{Value=$key.GetValue($name);Kind=$key.GetValueKind($name)} }
}
try {
    $compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $exe = Join-Path $testRoot 'bandizip_tests.exe'
    & $compiler /nologo /target:exe /main:BandizipTests /warnaserror+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Runtime.Serialization.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Management.dll ("/out:"+$exe) (Join-Path $projectRoot 'tools\bandizip_tests.cs') (Join-Path $projectRoot 'tools\native_progress_tests.cs') (Join-Path $projectRoot 'src\worker\Worker.cs') (Join-Path $projectRoot 'src\worker\Settings.cs') (Join-Path $projectRoot 'src\worker\NativeCommands.cs') (Join-Path $projectRoot 'src\worker\RuntimeSafety.cs') (Join-Path $projectRoot 'src\worker\Appearance.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Bandizip test compilation failed' }
    & $exe $testRoot | Tee-Object -FilePath (Join-Path $testRoot 'bandizip-tests.log')
    if ($LASTEXITCODE -ne 0) { throw 'Bandizip extraction tests failed' }
    [pscustomobject]@{Success=$true;TestRoot=$testRoot;PublicInstallationChanged=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\bandizip-test-results.json') -Encoding UTF8
} finally {
    foreach ($name in @('BandizipPathV1','BandizipFormatsV1','BandizipEnabledV1')) {
        if ($saved.ContainsKey($name)) { $key.SetValue($name,$saved[$name].Value,$saved[$name].Kind) } else { $key.DeleteValue($name,$false) }
    }
    $key.Dispose(); $hive.Dispose()
}
