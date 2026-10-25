# MusicIsland

A sleek, fluid **Dynamic Island** music widget for Windows, designed for Spotify and media playback.

It floats at the top-center of your screen as a pill-shaped capsule displaying animated equalizer waves and album art. Hovering over it fluidly morphs the island into an expanded Now Playing card with playback controls, marquee track scrolling, and dynamic color theming.

---

## Quick Install (PowerShell)

Open Windows PowerShell and run:

```powershell
irm https://raw.githubusercontent.com/Prathambist/MusicIsland/main/install.ps1 | iex
```

> **Note:** The installer downloads the latest self-contained release, sets up shortcuts in your Start Menu and Windows Startup folder (launches on boot), and opens the app immediately. No .NET runtime installation required.

---

## Quick Uninstall

To completely remove MusicIsland and all its shortcuts, run:

```powershell
irm https://raw.githubusercontent.com/Prathambist/MusicIsland/main/uninstall.ps1 | iex
```

---

## Features

- **Dynamic Island Aesthetics:** Detached floating capsule with OLED black background, 1px perimeter outline, and soft ambient drop shadow.
- **Fluid Morphing:** Smoothly animates between compact pill and expanded squircle card on hover.
- **Adaptive Color Theming:** Dynamically samples colors from current album artwork to tint buttons, gradients, outlines, and audio visualizer bars.
- **Media Controls:** Full control over Spotify playback (Previous, Play/Pause, Next).
- **Marquee Title Scroll:** Long song names automatically marquee scroll when expanded.
- **Click-Through Transparency:** Screen space around the island is transparent and click-through, ensuring it never obstructs other windows or browser tabs.

---

## Building from Source

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows 10/11)

```powershell
# Clone the repository
git clone https://github.com/Prathambist/MusicIsland.git
cd MusicIsland

# Build and run in debug mode
dotnet run

# Build a self-contained release package
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```
