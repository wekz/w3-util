# wekzy-d1-toolkit

Custom RGC (Ranked Gaming Client, Dota 1) setup: a redesigned skin, remade UI sounds, and a small tray app for quality-of-life features. Private backup / source of truth for everything built here.

## What's inside

- **`skin/`** — "Wekzy Dark" RGC skin (light, minimal, monospace accents, green highlights). Drop into `Ranked Gaming Client\skins\`.
- **`sound/`** — Custom mechanical "thock" UI sounds. Drop into `Ranked Gaming Client\sound\` (replaces the stock `.wav` files).
- **`backgrounds/`** — Warcraft III main menu background models (`.mdx`), extracted from the game's own files (ROC/TFT campaigns). Used by the app's "Menu background" feature.
- **`src/RGCWatcher.cs`** — Source for **Wekz App**, the tray helper:
  - Borderless fullscreen + auto-focus when Warcraft III launches (needs `-window` in RGC's launch options)
  - `Alt+\`` — one-click SIGN (calibrated position, click-based so it behaves like a real user action)
  - `Alt+F2` — calibrate the SIGN button position
  - Swap the Warcraft III main menu background from a tray submenu
  - Settings window (toggle borderless / sound / notifications / hotkey)
- **`src/WekzySetup.cs`** — Source for **WekzySetup.exe**, an all-in-one USB installer that embeds the skin, sounds, app, backgrounds, and JetBrains Mono font, and installs everything with one click (admin prompt).
- **`build.ps1`** — Compiles `Wekzy.exe` from `src/RGCWatcher.cs`.
- **`build-setup.ps1`** — Packages everything into `WekzySetup.exe`.
- **`install.ps1`** — Registers/updates the Scheduled Task that runs Wekz App at logon (as admin, required since RGC itself runs elevated).

## Rebuilding after a code change

```powershell
.\build.ps1          # compiles Wekzy.exe
.\install.ps1         # run as Administrator — updates the running tray app
.\build-setup.ps1     # optional — rebuilds the USB installer with the latest payload
```

## Notes

- No installers are required to build — everything uses the C# compiler bundled with .NET Framework (already on Windows).
- `settings.ini`, `sign-offset.txt`, and `*.log` are machine-specific and intentionally left out of version control.
- The Warcraft III backgrounds are game assets extracted from a legally owned copy of Warcraft III, kept here for personal use only.
