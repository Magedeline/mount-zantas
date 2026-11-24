# Platforms

Mount Zantas is split in two so it can reach Linux and macOS:

- **`core/`** (`MountZantas.Core`, plain .NET 8, no UI): settings, downloads and checksum checks, Everest install/remove, mod scanning, the mod database, GameBanana, the map reader, game detection, Lönn. Runs on Windows, Linux and macOS.
- **`MountZantas.csproj`**: the Windows app (WinForms) on top of the core. Still the Windows release.
- **`desktop/`** (`MountZantas.Desktop`, Avalonia): the same pages for **Linux and macOS** on the same core. It also runs on Windows, which is how its pages are checked (`MountZantas --shot <page> out.png`).
- **`tests/MountZantas.CoreChecks`**: a console program that checks the core. CI runs it on **Ubuntu, macOS and Windows** for every push.

## Status

| | Windows | Linux | macOS |
|---|---|---|---|
| Full app (window, buttons, pages) | yes (WinForms) | yes (Avalonia, 0.8.0b), **not yet run on real hardware** | yes (Avalonia, 0.8.0b), **not yet run on real hardware** |
| Self-update | replaces itself | tells you a new version is out | tells you a new version is out |
| Core logic | tested in CI | tested in CI | tested in CI |
| Game detection | Steam, Epic, GOG, itch, folders | Steam (incl. Flatpak, extra libraries), itch, folders | Steam, itch, /Applications, folders |
| Everest install/remove | tested | patcher chosen and made executable, tested in CI; full patching **not yet run on real hardware** | same as Linux |
| Start Celeste | tested | implemented, **untested on real hardware** | implemented, **untested on real hardware** |
| Lönn | tested | AppImage chosen, **untested on real hardware** | app bundle chosen, **untested on real hardware** |
| Topo | yes | no (Windows-only program) | no (Windows-only program) |

## What is verified and what is not

CI checks, on all three systems: the OS-specific paths, which Everest patcher is used (and that Everest's release ships all three), that the patcher can be made executable, detection of Steam and itch installs on fake home folders (including the macOS `Celeste.app/Contents/Resources` layout), the uninstall restore, map reading, and the live services (mod database, GameBanana, news, release checksums).

Not checked yet, because it needs a real Linux or macOS machine with Celeste installed: running the Everest patcher end to end, starting the game, and starting Lönn.

## What is left for a Linux and macOS release

1. A test pass on real Linux and macOS machines for the "not yet run on real hardware" rows above.
2. macOS builds are ad-hoc signed in CI (enough for Apple Silicon to run them) but not notarized, so Gatekeeper asks on first launch (right-click > Open).

## Building

```
dotnet publish desktop/MountZantas.Desktop.csproj -c Release -r linux-x64 -p:DebugType=none -o out   # or osx-x64 / osx-arm64
```

Build `osx-arm64` on a Mac (or in CI): a build made on Windows or Linux can't be signed, and Apple Silicon refuses to run unsigned arm64 code.
