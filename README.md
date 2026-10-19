# Clutch

A light, local-only game clipping app for Windows 11. Clutch keeps the last 30 s to 3 min of your screen and game audio in memory. Press **F8** and that moment is saved as an MP4 straight away, with no re-encoding.

It has no account, no ads and no uploads. Your clips stay in `Videos\Clutch`.

## Building

Double-click `build.bat`. It uses the C# compiler that comes with Windows (.NET Framework 4), so nothing needs installing. The output is `Clutch.exe` next to it, plus `ClutchSetup.exe`, an identical copy that opens the installer when run under that name.

- All code is in `src\` (C# 5, ASCII-only source; symbols are written as `\uXXXX` escapes).
- **Logo:** `logo.png` (the square, transparent logo) and `icon.ico` (the app icon: the same transparent logo) are embedded by `build.bat`. To change the logo, run `Clutch-dev.exe --make-logo <your-logo.png> <folder>`, then copy the new `logo.png` and `icon.ico` next to `build.bat` and rebuild.

## Installing, updating and uninstalling

There's no separate installer program. `ClutchSetup.exe` is Clutch itself: under that name (or with `--install`) it shows a small setup window instead of the app.

- **Install** copies Clutch to `%LOCALAPPDATA%\Programs\Clutch`, adds a Start menu shortcut (and, optionally, a desktop shortcut and start with Windows) and an entry in Windows Settings > Apps > Installed apps. It's per user, so there's no admin prompt. If Clutch is running, it's asked to quit first.
- **Uninstall** from Installed apps or Settings > General. Clips are always kept. Settings and FFmpeg are removed only if you tick that option.
- **Updates:** Clutch checks the latest GitHub release a few seconds after it starts, then twice a day (Settings > General can turn this off). When there's a newer version, a notice offers **Update**. The main window closes and a small update window downloads the release's `ClutchSetup.exe`, checks it against the SHA-256 checksum GitHub publishes for it, swaps the exe and reopens Clutch. A download that doesn't match is thrown away.

### Publishing a release

1. In `src\Update.cs`, set `Repo` to your GitHub repository, e.g. `"yourname/clutch"`. Until it's set, update checks are off.
2. Raise `Version` in `src\App.cs` (e.g. `1.1.0`) and run `build.bat`.
3. On GitHub, create a release tagged `v1.1.0`, write the notes (the first lines show in the update window) and attach `ClutchSetup.exe`.

Clutch only reads public release information from GitHub and sends nothing about you.

## Invite-only access

Clutch needs an invite key before it records. On first launch it shows a PC code (a one-way scramble of Windows' random install ID, mixed with "Clutch": it contains nothing about the person, their PC or their connection). The person sends that code to whoever invited them, gets an invite key back, and pastes it in. Clutch then works offline.

- Keys are ECDSA (P-256) signatures. Clutch only contains the public key in `src\Licence.cs`, which can check keys but never make them. Nothing secret is in this repository, and there's no list of keys anywhere in it.
- A key only works on the PC whose code it was made for, and can have an expiry date.
- Turned-off keys: Clutch reads a signed `revoked.txt` from the root of this repository's `main` branch (same timing as update checks). It lists only random key numbers, no names or PC codes, and Clutch ignores it unless it's signed with the matching private key.

## Problems and crash reports

If Clutch crashes, it closes and opens a report window (as a fresh copy, so it still works) with **Restart Clutch**. Settings > General > Report a problem opens the same window at any time. The report holds the Clutch and Windows versions, graphics card, encoder, recording settings and the last log lines, with your Windows user name taken out of paths. Common problems are recognised and explained, such as an old NVIDIA driver, interrupted screen capture, a full drive or a blocked microphone. Nothing is sent: you copy or save the report and send it yourself.

## Credits and licences

FFmpeg isn't part of Clutch. It's downloaded separately (see below) and run as its own program. The build Clutch uses is licensed under the GPLv3. Settings > General > Credits and licences shows the full notice, plus FFmpeg's own licence file once it's downloaded.

## First run

Clutch records with **FFmpeg** (free and open source). On first run it shows a one-time setup card. When you click **Download FFmpeg**, it:

- downloads FFmpeg 7.0.2 "essentials" (about 90 MB) from gyan.dev's official GitHub releases,
- checks it against a SHA-256 fingerprint built into Clutch (`src\FFmpeg.cs`),
- and keeps only `ffmpeg.exe` and `ffprobe.exe` in `%LOCALAPPDATA%\Clutch\ffmpeg`.

**Why 7.0.2 and not the newest?** Newer FFmpeg builds need newer NVIDIA drivers for NVENC: driver 570 or later for 7.1 and 8.x, and 610 or later for 9.x. GTX 10-series cards like the GTX 1080 can't run those drivers. 7.0.2 works with NVIDIA drivers from 551.76 up, and with AMD and Intel too.

## How it works

| Part | How |
|---|---|
| Screen capture | FFmpeg's `ddagrab` (Windows Desktop Duplication). Frames stay on the graphics card. |
| Encoding | Picked automatically from your graphics card: NVIDIA **NVENC**, AMD **AMF**, Intel **Quick Sync**, or CPU (x264) as a fallback. Each option gets a 10-frame test (saved nowhere), and the result is remembered. |
| Replay buffer | FFmpeg streams MPEG-TS into Clutch's memory, cut into roughly 1-second pieces at keyframes. Old pieces are dropped, so nothing is written to disk until you save. |
| Audio | Game/desktop audio (WASAPI loopback) and the microphone are captured by Clutch itself, into their own memory buffers, timestamped against the same clock as the video. |
| Saving a clip | The video pieces are copied out and remuxed to MP4 with `-c copy` (no video re-encode). The audio is encoded to AAC. With the mic on, the clip gets 3 audio tracks: **Game + Mic** (what players hear), **Game** and **Mic**. Saving takes about half a second. |
| "Clip saved" notice | A small click-through window in the corner of the game. It never takes focus and is excluded from screen capture, so it doesn't appear in later clips. |
| Library | Every `.mp4` in the clips folder, with thumbnails, search, favourites, rename and delete. Each clip shows its game's icon, taken from the game's `.exe` the first time Clutch sees it running (stored once per game as a ~1 KB PNG in `%LOCALAPPDATA%\Clutch\icons`). Delete and rename use **Undo** instead of "are you sure?". Deleted clips go to the Recycle Bin. |
| Player | The built-in Windows Media Player control. |
| Trim & export | Drag the start and end handles on the filmstrip and set game/mic volume (or mute either with its speaker button). **Advanced > Vertical 9:16** crops a phone-shaped slice for TikTok, Shorts and Reels (1080 × 1920); Position picks which part, and the cut-off sides are shaded on the preview. **Original quality** is instant, with no re-encode; the start snaps to the nearest keyframe, within 1 s. **Discord 10 MB / 50 MB / 500 MB** re-encodes on the GPU to fit, lowering resolution and frame rate if needed. "Copy file" puts the export on the clipboard, ready to paste into Discord. |
| Games page | One card per game with its official banner (Steam or Microsoft Store art, else Discord's cover art; fetched once and kept as a ~50 KB JPEG in `%LOCALAPPDATA%\Clutch\banners`), its icon, number of clips, total footage and last clip. Click a game to see just its clips. |
| Discord status | While Clutch records one of your games, your Discord status shows "Clipping Roblox with Clutch" with the game's official icon (looked up once in Discord's public game list and kept as a one-line link in `%LOCALAPPDATA%\Clutch\discord-art.txt`) and the Clutch logo as the small badge. It talks to the Discord app on your PC directly. Turn it off in Settings > General. |
| Auto-start with games | Add games from your running apps or by picking their `.exe`. With "Only record while a game is running" on, the buffer runs only while one of them is open. The top-bar switch turns everything off. |

## Using it

- **Status:** the meter at the bottom of the sidebar shows how much is ready to clip. Click it to turn the buffer on or off, or to record only while a game runs.
- **Clips:** hover a clip for Copy (paste into Discord), Trim and more. Clips are grouped by day when sorted by date.
- **Select several:** Ctrl+click, Shift+click a range, or Ctrl+A. Then Favourite, Copy or Delete them together. Esc clears the selection.
- **Viewer:** Space play/pause, Left/Right jump 5 s, Ctrl+Left/Right previous/next clip, F full screen, Delete delete and show the next clip, Esc back.
- **Trim:** I and O set the start and end, Left/Right move 1 s. The Export button shows the expected file size.
- **Settings** are split into Recording, Audio, Games, Storage and General.
- **First run:** a short welcome walks through downloading FFmpeg, the clip key and choosing games.

### Where things are kept

- Settings: `%APPDATA%\Clutch\settings.json`, saved instantly
- Clip details such as favourites and game: `%APPDATA%\Clutch\library.json`
- Error log: `%APPDATA%\Clutch\errors.log` (also under Settings > General > Report a problem > Error log)
- Installed app: `%LOCALAPPDATA%\Programs\Clutch\Clutch.exe`
- FFmpeg, thumbnails and temp files: `%LOCALAPPDATA%\Clutch`

Only one copy of Clutch runs at a time; opening it again just brings the window forward. Closing the window keeps Clutch in the tray so the clip key keeps working. Right-click the tray icon to quit.

## Measured on the dev PC (GTX 1080, 3440 x 1440 at 60 fps)

- CPU while recording: FFmpeg about 0.8%, Clutch about 0.5% of the whole PC (NVENC, frames kept on the GPU)
- Memory: about 110 MB in total while recording (60 s buffer, Standard quality). The UI's caches are released when Clutch goes to the tray
- Saving a 12 s clip: 0.5 s
- Audio/video sync: the picture lands in the clip 1 to 2 frames after it's drawn. The sound lands at the moment it reaches the speakers.

## Developer switches

These are only in the developer build: run `build.bat dev` to make `Clutch-dev.exe`. The normal `Clutch.exe` leaves them out to stay small. None of them send mouse or keyboard input.

- `Clutch-dev.exe --demo --shot <page> <file.png>`: screenshot of a page with made-up clips. Pages: `clips`, `viewer`, `trim`, `settings`, `settings-audio`, `settings-games`, `settings-general`, `toast`, `overlay`, `rename`. **Never records.**
- `Clutch-dev.exe --selftest <folder>`: records about 14 s (with a flashing square and beeps), saves a clip, measures sync and CPU, and tests both kinds of export.
- `Clutch-dev.exe --e2e <folder>`: runs the real app, saves a clip through the clip-key path, opens the player and trim pages, and tests game auto-start with a hidden `ping.exe`.
- `Clutch-dev.exe --download-ffmpeg <report.txt>`: runs the FFmpeg download on its own.
- `Clutch-dev.exe --playtest <clip.mp4> <out.png>` and `--viewtest <clip.mp4> <out.png>`: play a clip and screenshot the screen area, to check the player shows video.
- `Clutch-dev.exe --make-logo <logo.png> <folder>`: makes `logo.png` and `icon.ico` from a new logo.
- `Clutch-dev.exe --winshot <window> <file.png>`: screenshot of a standalone window: `setup`, `setup-done`, `uninstall`, `update`, `update-failed`, `report`, `crash`, `licences`. Does nothing else.
- `Clutch-dev.exe --setuptest install|uninstall <log.txt>`: runs the install or uninstall steps without the window.
- `Clutch-dev.exe --updatetest <log.txt>` with `CLUTCH_UPDATE_FEED` set to a release JSON file: fetch, download, verify and swap, then start the new exe with `--whoami`.
- `Clutch-dev.exe --verttest <clip.mp4> <folder>`: vertical exports through the trim page's code.
- `Clutch-dev.exe --crashtest`: a pretend crash, to check the crash report window opens.

## Known limits (v1)

- Exclusive full-screen games can sometimes hide the "Clip saved" notice. Borderless and windowed games always show it.
- Most players only play the first audio track (**Game + Mic**). The separate tracks are there for the trim editor and editing apps.
- The clip key is watched, not taken, so games still receive it. If a game runs as administrator, Windows hides its key presses from normal apps, so while that game is in front Clutch briefly reserves the key the old way.
- 1080p / 720p recording shrinks each frame on the CPU (1-2 cores at 60 fps) on NVIDIA and AMD cards, because FFmpeg 7.0.2 can't shrink Desktop Duplication frames on those GPUs. Intel Quick Sync does it on the GPU. Native resolution is the lightest option.
