# Builds dist\SparkleCursor.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# Usage:  .\build.ps1            build only
#         .\build.ps1 -Install   build, install to %LOCALAPPDATA%\Programs\SparkleCursor and (re)launch
param([switch]$Install)
$ErrorActionPreference = 'Stop'

$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$obj = Join-Path $PSScriptRoot 'obj'
$dist = Join-Path $PSScriptRoot 'dist'
$src = Join-Path $PSScriptRoot 'SparkleCursor.cs'
$refs = '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll'
New-Item -ItemType Directory -Force $obj, $dist | Out-Null

# Pass 1 builds a throwaway exe that renders the logo to a multi-size .ico; pass 2 embeds it.
& $csc /nologo /target:winexe /optimize "/out:$obj\stage1.exe" @refs $src
if ($LASTEXITCODE -ne 0) { throw 'stage 1 build failed' }
Start-Process "$obj\stage1.exe" -ArgumentList '--export-icon', "`"$obj\SparkleCursor.ico`"" -Wait
& $csc /nologo /target:winexe /optimize "/win32icon:$obj\SparkleCursor.ico" "/out:$dist\SparkleCursor.exe" @refs $src
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
"Built $dist\SparkleCursor.exe"

if ($Install) {
    $dest = "$env:LOCALAPPDATA\Programs\SparkleCursor"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Get-Process SparkleCursor -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    Copy-Item "$dist\SparkleCursor.exe" $dest -Force

    $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut("$env:APPDATA\Microsoft\Windows\Start Menu\Programs\Sparkle Cursor.lnk")
    $lnk.TargetPath = "$dest\SparkleCursor.exe"
    $lnk.WorkingDirectory = $dest
    $lnk.Save()

    Start-Process "$dest\SparkleCursor.exe"
    "Installed to $dest"
}
