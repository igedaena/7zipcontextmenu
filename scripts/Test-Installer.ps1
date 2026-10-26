# Compatibility entry point for the all-user installer. The cycle refuses to replace an existing installation.
& (Join-Path $PSScriptRoot 'Test-PublicInstaller.ps1')