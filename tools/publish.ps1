# Builds both single-file versions of Zarpazo into publish\ (the folder is git-ignored):
#   Zarpazo.exe         everything included, ~180 MB (not compressed: a compressed one is unpacked into RAM, ~70 MB more in use), runs on any 64-bit Windows
#   Zarpazo-ligero.exe  without .NET, ~25 MB (the Windows OCR bindings), needs the .NET 8 Desktop Runtime installed
# Run from anywhere: pwsh tools\publish.ps1
$ErrorActionPreference = 'Stop'

# The 64-bit SDK; a 32-bit dotnet found first on PATH has no SDK installed.
$dotnet = Join-Path $env:ProgramW6432 'dotnet\dotnet.exe'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'Zarpazo\Zarpazo.csproj'
$publish = Join-Path $root 'publish'

function Publish-Version([string]$FileName, [bool]$SelfContained) {
    $work = Join-Path ([IO.Path]::GetTempPath()) "zarpazo-publish-$FileName"
    if (Test-Path $work) { Remove-Item $work -Recurse -Force }

    $selfContainedArgs = if ($SelfContained) {
        # WPF's native libraries must be bundled too, or they are left next to the .exe.
        '--self-contained', 'true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:EnableCompressionInSingleFile=false'
    } else {
        '--self-contained', 'false'
    }
    & $dotnet publish $project -c Release -r win-x64 @selfContainedArgs -p:PublishSingleFile=true -p:DebugType=none -o $work
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($FileName) failed with exit code $LASTEXITCODE" }

    New-Item -ItemType Directory -Force $publish | Out-Null
    try {
        Copy-Item (Join-Path $work 'Zarpazo.exe') (Join-Path $publish $FileName) -Force -ErrorAction Stop
    } catch [System.IO.IOException] {
        # A running program can't be replaced; the old file stays and the other version is still built.
        Write-Warning "$FileName esta en uso (cierra Zarpazo desde la bandeja) y no se actualizo."
    }
    Remove-Item $work -Recurse -Force
}

Publish-Version 'Zarpazo.exe' $true
Publish-Version 'Zarpazo-ligero.exe' $false

Get-ChildItem $publish | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 2) } }
