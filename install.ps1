param (
    [string]$Repo = "Prathambist/MusicIsland",
    [switch]$NoStartup
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 -bor [Net.SecurityProtocolType]::Tls13

Write-Host ""
Write-Host "  =====================================" -ForegroundColor Cyan
Write-Host "         MusicIsland Installer         " -ForegroundColor White
Write-Host "  =====================================" -ForegroundColor Cyan
Write-Host ""

$InstallDir = "$env:LOCALAPPDATA\MusicIsland"
$TempZip = "$env:TEMP\MusicIsland-win-x64.zip"
$DownloadUrl = "https://github.com/$Repo/releases/latest/download/MusicIsland-win-x64.zip"

# 1. Stop existing process if running
$running = Get-Process -Name "MusicIsland" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping running instance of MusicIsland..." -ForegroundColor Yellow
    Stop-Process -Name "MusicIsland" -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

# 2. Download latest release
Write-Host "Downloading latest release from $Repo..." -ForegroundColor Cyan
try {
    Invoke-WebRequest -Uri $DownloadUrl -OutFile $TempZip -UseBasicParsing -MaximumRedirection 10
} catch {
    Write-Host "Direct download failed, querying GitHub API..." -ForegroundColor Yellow
    try {
        $apiJson = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -UseBasicParsing
        $asset = $apiJson.assets | Where-Object { $_.name -like "*.zip" } | Select-Object -First 1
        if (-not $asset) { throw "No zip asset found in release." }
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $TempZip -UseBasicParsing -MaximumRedirection 10
    } catch {
        Write-Error "Failed to download MusicIsland: $_"
        exit 1
    }
}

# 3. Extract to LocalAppData
Write-Host "Extracting to $InstallDir..." -ForegroundColor Cyan
if (Test-Path $InstallDir) {
    Remove-Item -Path $InstallDir -Recurse -Force
}
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Expand-Archive -Path $TempZip -DestinationPath $InstallDir -Force
Remove-Item -Path $TempZip -Force

# 4. Create Shortcuts
$ExePath = Join-Path $InstallDir "MusicIsland.exe"
if (-not (Test-Path $ExePath)) {
    Write-Error "MusicIsland.exe was not found in the extracted files."
    exit 1
}

$WshShell = New-Object -ComObject WScript.Shell

# Start Menu Shortcut
$StartMenuPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\MusicIsland.lnk"
$Shortcut = $WshShell.CreateShortcut($StartMenuPath)
$Shortcut.TargetPath = $ExePath
$Shortcut.WorkingDirectory = $InstallDir
$Shortcut.Description = "iPhone-style Dynamic Island Spotify widget for Windows"
$Shortcut.Save()

# Windows Startup Shortcut (launches automatically on boot)
if (-not $NoStartup) {
    $StartupPath = "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Startup\MusicIsland.lnk"
    $StartupShortcut = $WshShell.CreateShortcut($StartupPath)
    $StartupShortcut.TargetPath = $ExePath
    $StartupShortcut.WorkingDirectory = $InstallDir
    $StartupShortcut.Description = "MusicIsland Startup"
    $StartupShortcut.Save()
    Write-Host "Added to Windows Startup (starts automatically on boot)." -ForegroundColor DarkGray
}

# 5. Launch Application
Write-Host "Launching MusicIsland..." -ForegroundColor Green
Start-Process -FilePath $ExePath

Write-Host ""
Write-Host "  Installation completed successfully!" -ForegroundColor Green
Write-Host "  Location: $InstallDir" -ForegroundColor DarkGray
Write-Host "  Start Menu shortcut created." -ForegroundColor DarkGray
Write-Host ""
