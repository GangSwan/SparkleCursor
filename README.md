# ✦ Sparkle Cursor

A tiny Windows tray app that makes sparkles flutter from your mouse cursor as it moves.

- Click-through, always-on-top overlay that never steals focus, works across multiple monitors and high-DPI displays, and sits idle when your mouse isn't moving
- Settings window with a live preview. Click the tray icon to open it:
  - 8 palettes, including Custom with three colours you pick
  - 9 sprites: Sparkle, Star, Heart, Diamond, Orb, Snowflake, Ring, Glint, Confetti
  - Sliders for density, size, lifetime, gravity (negative floats upward), flutter, spread, glow and twinkle
  - Optional Start with Windows
- A single ~90 KB exe with no dependencies. It runs on the .NET Framework 4.x that comes with Windows 10 and 11

## Install

Run this in PowerShell:

```powershell
irm https://raw.githubusercontent.com/GangSwan/SparkleCursor/main/install.ps1 | iex
```

Or download `SparkleCursor.exe` from [Releases](https://github.com/GangSwan/SparkleCursor/releases/latest) and run it from anywhere. It isn't code-signed, so if Windows SmartScreen warns you, choose **More info → Run anyway**.

Running the installer again updates to the latest release.

## Use

- **Click** the tray icon to open settings
- **Right-click** the tray icon for quick pause, palette, Start with Windows and Exit
- Launching it again while it's running opens its settings

## Uninstall

```powershell
irm https://raw.githubusercontent.com/GangSwan/SparkleCursor/main/uninstall.ps1 | iex
```

## Build from source

```powershell
.\build.ps1            # -> dist\SparkleCursor.exe
.\build.ps1 -Install   # build, install locally and relaunch
```

No SDK needed. It uses the `csc.exe` that comes with Windows.
