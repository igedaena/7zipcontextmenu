[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$vswherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath)) {
    throw 'Visual Studio Build Tools were not found. Run scripts\Install-BuildTools.ps1 first.'
}
$installationPath = & $vswherePath -latest -products Microsoft.VisualStudio.Product.BuildTools `
    -version '[17.0,18.0)' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 `
    Microsoft.VisualStudio.Component.Windows11SDK.26100 Microsoft.VisualStudio.Component.VC.CMake.Project `
    -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $installationPath) {
    throw 'A complete VS 2022 Build Tools installation with the configured components was not found.'
}
$cmakePath = Join-Path $installationPath 'Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe'
if (-not (Test-Path -LiteralPath $cmakePath)) { throw "CMake not found: $cmakePath" }

$results = @()
foreach ($architecture in @('x64', 'Win32')) {
    $buildDirectory = Join-Path $projectRoot ('build\' + $architecture)
    & $cmakePath -S $projectRoot -B $buildDirectory -G 'Visual Studio 17 2022' -A $architecture
    if ($LASTEXITCODE -ne 0) { throw "CMake configuration failed: $architecture" }
    & $cmakePath --build $buildDirectory --config Release --parallel 1 -- /nodeReuse:false
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $architecture" }
    $probePath = Join-Path $buildDirectory 'Release\sdk_probe.exe'
    $probeOutput = & $probePath
    if ($LASTEXITCODE -ne 0) { throw "SDK runtime verification failed: $architecture" }
    Write-Output $probeOutput
    $results += [pscustomobject]@{
        Architecture = $architecture
        Executable = $probePath
        Output = $probeOutput
        ExitCode = $LASTEXITCODE
    }
}
New-Item -ItemType Directory -Path (Join-Path $projectRoot '.tools') -Force | Out-Null
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $projectRoot '.tools\build-probe-results.json') -Encoding UTF8
