<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/banner-dark.png">
    <img src="assets/banner-light.png" alt="Clutch" width="460">
  </picture>
</p>

A light, local-only game clipping app for Windows. Clutch keeps the last moments of your game in memory, and saves them as a clip the instant you press your clip key.

No account, no ads, no uploads. Your clips stay on your PC.

## Features

- **Instant clips:** keeps the last 30 seconds to 3 minutes ready and saves them in about half a second, with no quality loss.
- **Hardware recording:** uses your graphics card's video encoder (NVIDIA, AMD or Intel), so games barely notice it.
- **Clip key:** F8 by default. Works in any game, and the game still receives the key too. Optionally, only hold-to-clip counts, so accidental taps don't make clips.
- **Voice clips:** say "Clutch, clip that" (or your own phrase) to save a clip. Uses Windows speech recognition, or an optional offline voice add-on that's more accurate. Nothing you say leaves your PC.
- **"Clipped!" notice:** a rounded card slides in from the edge of the screen. Drag it to the top, bottom, left or right, and it never appears in your clips.
- **Separate audio tracks:** the game, your other apps (Discord, music) and your microphone are saved separately, so you can lower or mute any of them later. Game-only audio needs Windows 11, or Windows 10 build 20348 or newer.
- **Library:** thumbnails with moving previews on hover, search (which also finds settings), favourites, game filters and grouping by day. Deleting can be undone.
- **Player:** frame-by-frame stepping, 0.25x to 2x speed, looping, screenshots, keyboard shortcuts and full screen.
- **Trim and export:** cut a clip in seconds, adjust game and mic volume, and export for Discord (10 MB, 50 MB or 500 MB) or vertical 9:16 for TikTok, Shorts and Reels.
- **Games:** clips are grouped by game, with official game art. Clutch can record only while your games are running.
- **Share on Discord:** one click shrinks a clip to your upload size, copies it and opens Discord's search. Clips can also be shared as spoilers.
- **Discord status:** optionally shows "Clipping [game] with Clutch" on your Discord profile.
- **Recording health:** the sidebar warns you if your PC can't keep up, with tips to fix it.
- **Updates:** Clutch tells you when a new version is out and updates itself in a few seconds.

## Requirements

- Windows 10 (version 2004 or newer) or Windows 11, 64-bit
- Any graphics card. NVIDIA, AMD and Intel graphics record with their hardware encoder. Other PCs record using the CPU, which works but is heavier.
- NVIDIA cards need driver 551 or newer for hardware recording.
- About 90 MB for FFmpeg, downloaded once the first time you set Clutch up.

## Invite-only

Clutch is invite-only for now. The first time it opens, it shows a PC code to send to the person who invited you, and you paste the invite key they send back.

The PC code is a scrambled code made just for Clutch. It contains nothing about you, your PC or your internet connection, and Clutch never sends it anywhere.

## Privacy

- Clutch has no account and no analytics, and never uploads your clips or anything about you.
- It only goes online to:
  - download FFmpeg the first time you set it up
  - check for updates
  - check whether invite keys have been turned off
  - look up game icons and art
  - download the voice add-on, only if you choose to (from the Vosk project on GitHub and alphacephei.com)
  - show your Discord status, if you turn that on (this only talks to the Discord app on your PC)
- Crash and problem reports are never sent automatically. You can copy one and share it yourself.

## Where your things are kept

- **Clips:** `Videos\Clutch`
- **Settings:** `%APPDATA%\Clutch`
- **FFmpeg, thumbnails and game art:** `%LOCALAPPDATA%\Clutch`
- **The app:** `%LOCALAPPDATA%\Programs\Clutch`

Uninstalling always keeps your clips.

## Known limits

- Exclusive full-screen games can sometimes hide the "Clipped!" notice. Borderless and windowed games always show it.
- Most video players only play the first audio track, which has game and mic mixed together. The separate tracks are there for trimming and editing apps.
- Recording at 1080p or 720p on a bigger screen uses some extra CPU on NVIDIA and AMD cards. Recording at your screen's own resolution is the lightest option.
- Clips from HDR screens may look washed out.
- On laptops with two graphics chips, screen capture may need Clutch set to the same graphics chip as your screen in Windows' graphics settings.
- Windows may warn that it "protected your PC" when you first run the installer, because Clutch isn't code-signed yet.

## Credits and licences

Clutch records and edits video with [FFmpeg](https://ffmpeg.org), which is downloaded separately and isn't part of Clutch. The FFmpeg build it uses (by Gyan Doshi, gyan.dev) is licensed under the GNU GPL version 3. The optional voice add-on uses [Vosk](https://alphacephei.com/vosk/) and its small English model, both licensed under the Apache License 2.0, downloaded separately when you ask for it. Game icons and art belong to their owners. Clutch isn't affiliated with or endorsed by Discord, Valve, Microsoft or any game publisher.
