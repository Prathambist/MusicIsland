# MusicIsland

A sleek, fluid **Dynamic Island** music widget for Windows. It works with the Spotify and Apple Music apps and with web players such as Spotify Web, YouTube, YouTube Music and Apple Music on the web.

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

- **Fluid, spring-driven morphing:** The island is a single pure-black shape whose size, corner radius and clip are animated by physical springs (the same response/damping model as iOS). Animations stay smooth and continuous even if you move in and out mid-animation.
- **Works with your players:** The island follows the Spotify app, the Apple Music app, and any web player in Chrome, Edge, Firefox, Opera or Brave (Spotify Web, YouTube, YouTube Music, Apple Music on the web, ...). When several are playing, it stays on the one it's showing. When that one pauses, it switches to whichever is still playing. You can turn each source on or off in Settings.
- **Real-time audio visualizer:** The bars follow the actual bass, mids and treble of whatever is playing, using WASAPI loopback capture and an FFT. You can switch to a lighter synthetic animation in Settings.
- **Scroll to change volume:** Scroll over the island to change the playing app's own volume, or the system volume. For web players, the app's volume is the whole browser's. A small volume pill shows the level while you scroll.
- **Like button:** A heart in the expanded card adds or removes the current song from your Liked Songs through the Spotify Web API. It works with the Spotify app and with Spotify Web. For other players, the heart is replaced by the source's name. Optional; see below.
- **Full-screen auto-hide, per app:** The island hides when a game, video or slide show is full-screen. Apps are listed in Settings the first time they go full-screen, and each one can be set to *Default*, *Always hide* or *Always show*. You can also change the rule for the current full-screen app from the tray icon.
- **Idle / ambient state:** When Spotify is closed or has been paused for a while, the island can shrink to a small camera notch, hide completely, or show a clock (with optional weather from Open-Meteo). Hovering it then shows the time, date, weather and an *Open Spotify* button.
- **Adaptive color theming:** The visualizer bars and the expanded card's glow take their color from the album artwork.
- **Media controls and seeking:** Previous / Play-Pause / Next, and a live progress bar you can click or drag to jump within the song. Long song names scroll.
- **Synced lyrics:** When you open the island, the line being sung appears under the song. Lyrics come from [LRCLIB](https://lrclib.net), and titles from YouTube-style uploads are cleaned up before the lookup.
- **Real covers for music videos:** YouTube and other video sites only share a video frame, so MusicIsland finds the song's actual album or single cover in Apple's iTunes catalogue (free, no key). YouTube Music and Spotify Web already send the cover, so they're left as they are.
- **Song-change peek:** When the next song starts, the island briefly widens to show the new title and cover, then settles back, like iOS.
- **Message notifications:** New Discord, WhatsApp and Messenger messages drop out of the island for a few seconds. Click one to open the chat. This works with the desktop apps and with WhatsApp Web or Messenger installed as an app from your browser (*Install app*). Windows doesn't tell apps which website a plain browser tab's notification came from, so those are an optional "browser notifications" switch. MusicIsland asks Windows for permission to read notifications the first time.
- **Themes and sizes:** Midnight (pure black), Graphite, Album tint (coloured by the cover art) or Translucent, in Small, Default or Large.
- **Auto-update:** MusicIsland checks GitHub Releases on launch and every 12 hours. When a new version is out, it shows in the island and the tray menu, and one click installs it and restarts the app.
- **Stays out of the way:** The island doesn't take focus or show up in Alt+Tab, and clicks pass through everywhere outside the island.

## Settings

Open Settings from the gear in the expanded island, by right-clicking the island, from the tray icon, or by launching MusicIsland again (only one copy ever runs). Settings are saved to `%AppData%\MusicIsland\settings.json`.

You can also turn **Launch at login** on or off there. If something misbehaves, **Open log folder** shows `log.txt`, which records errors (never message contents).

### Connecting Spotify (for the Like button)

1. Create an app at [developer.spotify.com/dashboard](https://developer.spotify.com/dashboard) and choose **Web API**.
2. Add the Redirect URI `http://127.0.0.1:5543/callback`.
3. Paste the app's **Client ID** in MusicIsland's Settings and press **Connect**.

> Spotify requires Development Mode apps to be owned by a Premium account. The refresh token is encrypted with Windows DPAPI for your user account.

---

## Releasing

Push a tag such as `v1.2.0`. The release workflow stamps that version into the app, so installed copies see it as an update.

---

## Building from Source

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows 10/11)

```powershell
# Clone the repository
git clone https://github.com/Prathambist/MusicIsland.git
cd MusicIsland

# Build and run in debug mode
dotnet run

# Run the tests (also run by the release workflow before publishing)
dotnet test MusicIsland.Tests

# Build a self-contained release package
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```
