# Builds the self-contained single-file Zarpazo.exe into publish\ (the folder is git-ignored).
# Run from anywhere: pwsh tools\publish.ps1
$ErrorActionPreference = 'Stop'

# The 64-bit SDK; a 32-bit dotnet found first on PATH has no SDK installed.
$dotnet = Join-Path $env:ProgramW6432 'dotnet\dotnet.exe'
$root = Split-Path $PSScriptRoot -Parent

& $dotnet publish (Join-Path $root 'Zarpazo\Zarpazo.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o (Join-Path $root 'publish')
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Get-ChildItem (Join-Path $root 'publish') | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
