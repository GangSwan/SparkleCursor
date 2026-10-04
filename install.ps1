# Installs (or updates) Sparkle Cursor from the latest GitHub release.
#   irm https://raw.githubusercontent.com/GangSwan/SparkleCursor/main/install.ps1 | iex
$ErrorActionPreference = 'Stop'
$repo = 'GangSwan/SparkleCursor'
$dest = "$env:LOCALAPPDATA\Programs\SparkleCursor"
$exe = "$dest\SparkleCursor.exe"

New-Item -ItemType Directory -Force $dest | Out-Null
Get-Process SparkleCursor -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
Write-Host "Downloading Sparkle Cursor from github.com/$repo ..."
Invoke-WebRequest "https://github.com/$repo/releases/latest/download/SparkleCursor.exe" -OutFile $exe -UseBasicParsing
Unblock-File $exe

$lnk = (New-Object -ComObject WScript.Shell).CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Sparkle Cursor.lnk")
$lnk.TargetPath = $exe
$lnk.WorkingDirectory = $dest
$lnk.Save()

Start-Process $exe
Write-Host "Installed to $dest - look for the sparkle in your system tray. Click it to customize."
