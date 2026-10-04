# Removes Sparkle Cursor, its Start Menu shortcut, its startup entry and its saved settings.
#   irm https://raw.githubusercontent.com/GangSwan/SparkleCursor/main/uninstall.ps1 | iex
Get-Process SparkleCursor -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name SparkleCursor -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\SparkleCursor' -Recurse -ErrorAction SilentlyContinue
Remove-Item "$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Sparkle Cursor.lnk" -ErrorAction SilentlyContinue
Remove-Item "$env:LOCALAPPDATA\Programs\SparkleCursor" -Recurse -ErrorAction SilentlyContinue
Write-Host 'Sparkle Cursor removed.'
