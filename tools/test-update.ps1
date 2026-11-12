# Tries the whole automatic update for real, without touching your Zarpazo, your settings or GitHub:
#   look for a version -> download it -> check its hash -> restart with no window -> the new version opens by itself.
#
# It builds two throw-away copies (9.0.0 and 9.0.1) with their own installers, id, folder and mutex, installs 9.0.0 in a temp
# folder, serves a fake "latest release" from 127.0.0.1, and waits for 9.0.1 to appear. Two cases:
#   Online   the app finds 9.0.1 through the fake feed, downloads it and restarts as if "Reiniciar ahora" had been pressed.
#   OnStart  9.0.1 was already downloaded and "Al cerrar" chosen: the next start installs it, with no network at all.
# Everything is removed at the end, and it checks that your real uninstall entry and start-up entry were not changed.
#
# Needs Inno Setup 6 and the .NET 8 SDK and Desktop Runtime. The test copies read ZARPAZO_TEST_* variables because they are built
# with -p:UpdateTest=true; a normal or published build never does. Takes a few minutes (it builds twice).
# Run from anywhere: pwsh tools\test-update.ps1   (-Scenario Online|OnStart, -KeepFiles to look at the result afterwards)
#   pwsh tools\test-update.ps1 -Scenario Manual   to see and try it yourself: it leaves the 9.0.0 copy open with 9.0.1 waiting
param(
    [ValidateSet('All', 'Online', 'OnStart', 'Manual')][string]$Scenario = 'All',
    [int]$Port = 47831,
    [int]$TimeoutSeconds = 120,
    [switch]$KeepFiles
)
$ErrorActionPreference = 'Stop'

$dotnet = Join-Path $env:ProgramW6432 'dotnet\dotnet.exe'
$root = Split-Path $PSScriptRoot -Parent
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 is not installed (winget install JRSoftware.InnoSetup).' }

$id = [guid]::NewGuid().ToString().ToUpper()
$work = Join-Path ([IO.Path]::GetTempPath()) "zarpazo-update-test-$($id.Substring(0, 8).ToLower())"
$src = Join-Path $work 'src'
$installDir = Join-Path $work 'installed'
$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{$id}_is1"
$realUninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F0B6A52-3C1D-4E8A-9B47-5D2A0C7E91F4}_is1'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$testVariables = @('ZARPAZO_TEST_FEED', 'ZARPAZO_TEST_DOWNLOADS', 'ZARPAZO_TEST_APPID', 'ZARPAZO_TEST_DATA', 'ZARPAZO_TEST_MUTEX',
    'ZARPAZO_TEST_CHECKDELAY', 'ZARPAZO_TEST_AUTORESTART')
$failures = [System.Collections.Generic.List[string]]::new()
$server = $null

function Step([string]$text) { Write-Host "`n== $text" -ForegroundColor Cyan }
function Pass([string]$text) { Write-Host "   OK   $text" -ForegroundColor Green }
function Fail([string]$text) { Write-Host "   FALLA $text" -ForegroundColor Red; $script:failures.Add($text) }
function Check([bool]$condition, [string]$text) { if ($condition) { Pass $text } else { Fail $text } }

# What must be the same before and after: the real installation and the real "start with Windows" entry.
function Get-RealState {
    $key = Get-ItemProperty $realUninstallKey -ErrorAction SilentlyContinue
    $run = (Get-ItemProperty $runKey -ErrorAction SilentlyContinue).Zarpazo
    "$($key.DisplayVersion)|$($key.InstallLocation)|$run"
}

function Build-Version([string]$Version) {
    $exeDir = Join-Path $work "exe-$Version"
    # Framework-dependent single file (~25 MB): the update path is the same as the real one and it builds and downloads fast.
    & $dotnet publish (Join-Path $src 'Zarpazo.csproj') -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true `
        -p:DebugType=none -p:UpdateTest=true "-p:Version=$Version" -o $exeDir --nologo -v:q | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $Version failed ($LASTEXITCODE)" }

    $setupDir = Join-Path $work "setup-$Version"
    & $iscc "/DAppVersion=$Version" '/DTestBuild=1' ("/DAppIdValue={{$id}") '/DAppDisplayName=Zarpazo (prueba de actualizacion)' `
        "/DAppMutexName=Local\Zarpazo.UpdateTest.$($id.Substring(0, 8))" "/DInstallDirDefault=$installDir" `
        "/DSourceExe=$(Join-Path $exeDir 'Zarpazo.exe')" "/DOutDir=$setupDir" /Q (Join-Path $root 'installer\Zarpazo.iss') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup $Version failed ($LASTEXITCODE)" }
    Join-Path $setupDir "Zarpazo-Setup-$Version.exe"
}

function Start-FeedServer([string]$SetupPath, [string]$Version) {
    $name = "Zarpazo-Setup-$Version.exe"
    $json = @{
        tag_name = "v$Version"; draft = $false; prerelease = $false
        html_url = "https://github.com/emmanuelLugo/Zarpazo/releases/tag/v$Version"; body = 'Prueba'
        assets = @(@{
            name = $name; size = (Get-Item $SetupPath).Length
            digest = 'sha256:' + (Get-FileHash $SetupPath -Algorithm SHA256).Hash.ToLower()
            browser_download_url = "http://127.0.0.1:$Port/download/v$Version/$name"
        })
    } | ConvertTo-Json -Depth 5

    $listener = [System.Net.HttpListener]::new()
    $listener.Prefixes.Add("http://127.0.0.1:$Port/")
    $listener.Start()
    $requests = [System.Collections.ArrayList]::Synchronized([System.Collections.ArrayList]::new())
    $shell = [powershell]::Create()
    [void]$shell.AddScript({
        param($listener, $json, $setupPath, $requests)
        while ($listener.IsListening) {
            try { $context = $listener.GetContext() } catch { break }
            try {
                $path = $context.Request.Url.AbsolutePath
                [void]$requests.Add($path)
                if ($path -eq '/latest') {
                    $bytes = [Text.Encoding]::UTF8.GetBytes($json)
                    $context.Response.ContentType = 'application/json'
                } elseif ($path -like '/download/*') {
                    $bytes = [IO.File]::ReadAllBytes($setupPath)
                    $context.Response.ContentType = 'application/octet-stream'
                } else {
                    $context.Response.StatusCode = 404
                    $bytes = [byte[]]::new(0)
                }
                $context.Response.ContentLength64 = $bytes.Length
                $context.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            } catch { } finally { $context.Response.Close() }
        }
    }).AddArgument($listener).AddArgument($json).AddArgument($SetupPath).AddArgument($requests)
    $handle = $shell.BeginInvoke()
    [pscustomobject]@{ Listener = $listener; Shell = $shell; Handle = $handle; Requests = $requests }
}

function Stop-FeedServer($Server) {
    if (-not $Server) { return }
    try { $Server.Listener.Stop(); $Server.Listener.Close() } catch { }
    try { $Server.Shell.Stop(); $Server.Shell.Dispose() } catch { }
}

function Get-TestProcesses {
    Get-Process -Name Zarpazo -ErrorAction SilentlyContinue | Where-Object {
        try { $_.Path -and $_.Path.StartsWith($installDir, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
    }
}

function Stop-TestProcesses {
    Get-TestProcesses | ForEach-Object { try { $_.Kill(); $_.WaitForExit(5000) | Out-Null } catch { } }
}

function Install-Copy([string]$Setup) {
    $p = Start-Process -FilePath $Setup -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=$installDir" -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "Installing $Setup failed ($($p.ExitCode))" }
}

function Uninstall-Copy {
    Stop-TestProcesses
    $uninstaller = Join-Path $installDir 'unins000.exe'
    if (Test-Path $uninstaller) {
        Start-Process -FilePath $uninstaller -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART' -Wait | Out-Null
        # the uninstaller finishes through a temporary copy of itself: give it a moment to delete the folder
        for ($i = 0; $i -lt 40 -and (Test-Path $uninstaller); $i++) { Start-Sleep -Milliseconds 250 }
    }
}

function Wait-Until([scriptblock]$Condition, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) { return $true }
        Start-Sleep -Milliseconds 500
    }
    $false
}

function Read-Settings([string]$Data) {
    $file = Join-Path $Data 'settings.json'
    if (Test-Path $file) { try { Get-Content $file -Raw | ConvertFrom-Json } catch { $null } }
}

function Get-InstalledVersion { (Get-Item (Join-Path $installDir 'Zarpazo.exe') -ErrorAction SilentlyContinue).VersionInfo.ProductVersion }

# Starts the 9.0.0 copy and waits for the update to turn it into 9.0.1.
function Run-Update([string]$Data, [string]$Name) {
    $env:ZARPAZO_TEST_DATA = $Data
    Start-Process -FilePath (Join-Path $installDir 'Zarpazo.exe') | Out-Null

    # (In the OnStart case the 9.0.0 copy closes itself at once to install, so there is no point looking for it first.)
    $updated = Wait-Until {
        (Get-InstalledVersion) -like '9.0.1*' -and (Read-Settings $Data).LastRunVersion -eq '9.0.1' -and (Get-TestProcesses)
    } $TimeoutSeconds
    Check $updated "$Name`: se actualizó sola a 9.0.1 y volvió a abrirse (versión en disco: $(Get-InstalledVersion))"

    if ($updated) {
        $settings = Read-Settings $Data
        Check ($settings.PendingUpdateTag -eq '' -and -not $settings.ApplyUpdateOnClose) "$Name`: no queda nada pendiente, no se reintentará"
        Check ((Get-TestProcesses | Measure-Object).Count -eq 1) "$Name`: hay una sola copia abierta"
    }
}

try {
    $realBefore = Get-RealState
    Step "Preparando una copia aislada en $work"
    New-Item -ItemType Directory -Force $work | Out-Null
    & robocopy (Join-Path $root 'Zarpazo') $src /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }

    Step 'Compilando 9.0.0 y 9.0.1 (con ganchos de prueba) y sus instaladores'
    $setup900 = Build-Version '9.0.0'
    $setup901 = Build-Version '9.0.1'

    # Every test copy reads these; the app that is already running on this PC (the real one) is not affected.
    $env:ZARPAZO_TEST_APPID = "{$id}"
    $env:ZARPAZO_TEST_MUTEX = "Local\Zarpazo.UpdateTest.$($id.Substring(0, 8))"
    $env:ZARPAZO_TEST_FEED = "http://127.0.0.1:$Port/latest"
    $env:ZARPAZO_TEST_DOWNLOADS = "http://127.0.0.1:$Port/download/"

    if ($Scenario -in 'All', 'Online') {
        Step 'Caso Online: busca, descarga, verifica, reinicia sin ventanas'
        $data = Join-Path $work 'data-online'
        $env:ZARPAZO_TEST_CHECKDELAY = '2'
        $env:ZARPAZO_TEST_AUTORESTART = '1'
        Install-Copy $setup900
        Check ((Get-InstalledVersion) -like '9.0.0*') 'se instaló la 9.0.0 de prueba'
        Check (Test-Path $uninstallKey) 'tiene su propia entrada de desinstalación'
        $server = Start-FeedServer $setup901 '9.0.1'
        Run-Update $data 'Online'
        Check ($server.Requests -contains '/latest') 'preguntó por la última versión'
        Check (($server.Requests | Where-Object { $_ -like '/download/*' }).Count -ge 1) 'descargó el instalador'
        Stop-FeedServer $server
        $server = $null
        Uninstall-Copy
    }

    if ($Scenario -in 'All', 'OnStart') {
        Step 'Caso OnStart: ya descargada y "Al cerrar" elegido, el siguiente arranque la instala sin red'
        $data = Join-Path $work 'data-onstart'
        Remove-Item Env:ZARPAZO_TEST_AUTORESTART -ErrorAction SilentlyContinue
        $env:ZARPAZO_TEST_CHECKDELAY = '600' # no look at the feed: it must not be needed
        New-Item -ItemType Directory -Force (Join-Path $data 'update') | Out-Null
        Copy-Item $setup901 (Join-Path $data 'update\Zarpazo-Setup-9.0.1.exe')
        @{
            CheckForUpdates = $false; ApplyUpdateOnClose = $true; PendingUpdateTag = 'v9.0.1'
            PendingUpdateSha256 = (Get-FileHash $setup901 -Algorithm SHA256).Hash.ToLower(); PendingUpdateSize = (Get-Item $setup901).Length
        } | ConvertTo-Json | Set-Content (Join-Path $data 'settings.json') -Encoding UTF8
        Install-Copy $setup900
        Check ((Get-InstalledVersion) -like '9.0.0*') 'se instaló la 9.0.0 de prueba'
        Run-Update $data 'OnStart'
        Uninstall-Copy
    }

    if ($Scenario -eq 'Manual') {
        Step 'Caso Manual: lo pruebas tú con la copia 9.0.0 (se limpia todo al terminar)'
        $env:ZARPAZO_TEST_DATA = Join-Path $work 'data-manual'
        $env:ZARPAZO_TEST_CHECKDELAY = '3'
        Install-Copy $setup900
        $server = Start-FeedServer $setup901 '9.0.1'
        Start-Process -FilePath (Join-Path $installDir 'Zarpazo.exe') | Out-Null
        Write-Host ''
        Write-Host '   Se abrió una copia de PRUEBA de Zarpazo 9.0.0 (otro icono en la bandeja; los atajos globales siguen siendo los de la tuya).'
        Write-Host '   En unos 5 segundos descargará la 9.0.1 y saldrá la tarjeta "Zarpazo 9.0.1 está lista". Cosas que probar:'
        Write-Host '    - Menú de la bandeja (clic derecho en el icono): "Reiniciar para actualizar a la versión 9.0.1".'
        Write-Host '    - Configuración > General > Actualizaciones: "Reiniciar y actualizar" y "Al cerrar".'
        Write-Host '    - "Reiniciar ahora": se cierra, no se ve ninguna ventana, vuelve a abrirse sola y a los ~4 s sale "Zarpazo se actualizó a la versión 9.0.1".'
        Write-Host '    - "Al cerrar" y luego Salir desde la bandeja: se instala al salir y NO se vuelve a abrir.'
        Write-Host '    - Para ver la espera: empieza una captura (el área sin soltar) y pulsa "Reiniciar para actualizar" en la bandeja: dice que se reiniciará al terminar.'
        Write-Host ''
        Read-Host 'Cuando termines, pulsa Enter para cerrar la copia de prueba y limpiarlo todo' | Out-Null
        Stop-FeedServer $server
        $server = $null
    }

    Step 'Comprobando que no se tocó tu instalación'
    Check ((Get-RealState) -eq $realBefore) 'la entrada de desinstalación y el inicio con Windows reales siguen igual'
}
finally {
    Stop-FeedServer $server
    Stop-TestProcesses
    Uninstall-Copy
    $testVariables | ForEach-Object { Remove-Item "Env:$_" -ErrorAction SilentlyContinue }
    if (-not $KeepFiles -and (Test-Path $work)) { Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue }
    elseif ($KeepFiles) { Write-Host "`nSe dejó todo en $work" }
}

Write-Host ''
if ($failures.Count -gt 0) {
    Write-Host "FALLARON $($failures.Count) comprobaciones:" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}
Write-Host 'Todo correcto: la actualización automática funciona de punta a punta.' -ForegroundColor Green
