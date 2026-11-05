# Hatsu Desktop Privacy Policy

**Last updated:** November 5, 2026 · **Publisher:** PipeSick · **Contact:** https://github.com/PipeSick/hatsu-desktop/issues

Hatsu Desktop is free software (Apache-2.0) that adds a menu bar, a Dock and an activity "island" (**Hatsu Notch**) to Windows 11, plus an optional iPhone module (**Connect**). This policy explains what Hatsu Desktop reads from your device, what it stores, and what goes over the Internet. Every statement was checked against the source code published at https://github.com/PipeSick/hatsu-desktop (and the Hatsu Notch code shipped in the package).

## 1. Summary
- **No accounts, no publisher-run servers, no telemetry, no analytics, no automatic crash reports, no advertising.** The publisher receives none of your data. I found no update-check code in the app; updates are handled by Microsoft Store.
- Almost everything is processed and stored **on your device**. What goes over the Internet is listed in section 4, and almost all of it happens only if you turn the feature on or press a button.
- Hatsu Desktop **does not record audio, video, your screen or what you type**.

## 2. Data stored on your device
Inside the Microsoft Store package, the app's per-user data folders are private to the package and are removed when you uninstall it. For compatibility we temporarily keep the internal names `WinMac` and `NotchWin` in these folders.

| What | Where (internal name) | Contents |
|---|---|---|
| Settings | `%APPDATA%\WinMac`, `%LOCALAPPDATA%\NotchWin\settings.json` | Your preferences (theme, enabled modules, chosen city, Dock size, island sections, etc.) |
| Technical logs | `%LOCALAPPDATA%\WinMac\logs`, `%LOCALAPPDATA%\NotchWin\crash.log` | States and error codes and, in `crash.log`, the technical text of unhandled errors; they may include file paths and module or app names. Connect logs contain error codes only, not notification content. They stay on your device and leave it only if you attach them to an issue |
| Lyrics log | `%LOCALAPPDATA%\NotchWin\lyrics.log` | Lyrics lookup status, player name and duration (not title or artist). Deleted when it exceeds 64 KB. Exists only if you enable lyrics |
| AI-agent usage history (see §3) | `%LOCALAPPDATA%\NotchWin\usage-history.json` | Estimated spend per day (numbers) |
| Clipboard history (optional) | `%LOCALAPPDATA%\NotchWin\clipboard.dat` | **Only if you enable "keep history"** (off by default): pinned and recent entries, **encrypted for your Windows user (DPAPI)**. Entries that look like secrets, and entries from excluded apps, are not saved. Turning the option off deletes the file |

## 3. Features that access sensitive data
Each can be turned off in Settings. None sends what it reads to the Internet, except as stated in section 4.
- **Keyboard and mouse (low-level hooks, `WH_KEYBOARD_LL` and `WH_MOUSE_LL`).** They exist only to detect whether you press the Windows key **alone** (to open Hatsu's launcher instead of the Start menu) and whether a mouse click interrupts that press. The hook inspects each key only for that decision; it does not store, log or transmit what you type, lets every key through, can be turned off, and stops when the app closes. Hatsu also registers global hotkeys (`RegisterHotKey`) to open search and other actions.
- **Clipboard (Hatsu Notch).** While the island runs, it watches what you copy (text and images up to 8 MB) to show a history held in memory, with a configurable limit (50 by default). By default **nothing is written to disk**. It skips content that looks like secrets (private keys, known tokens, passwords) and copies from password managers (KeePass, 1Password, Bitwarden, etc.; editable list). A button clears the history. Secret detection is a heuristic and can miss things.
- **Camera (Hatsu Notch).** **Off by default.** If you enable the "Camera" section and open its tab, it shows a live preview in shared read-only mode. Hatsu Desktop **does not record or save images** and releases the camera when you leave the tab. Windows will ask for permission.
- **Synced lyrics (Hatsu Notch).** Off by default. See section 4.
- **AI agents (Hatsu Notch).** The "Agents" module is **off by default** in new installs of the Microsoft Store version; only if you turn it on in Settings (and have Claude Code, Codex, GitHub Copilot CLI or OpenCode installed) does it **read, on your device**, the session files those tools keep in your user folder (`.claude`, `.codex`, `.copilot`, `.local/share/opencode`) to compute usage and estimated cost. If Claude Code's local config file (`.claude.json`) contains them, it shows that account's email and plan; the code does not read credentials or tokens. It does not use the network. You can turn it off again in Settings. Anyone who already had this module on in an earlier install keeps their preference.
- **Other apps' Windows notifications.** Via the public `UserNotificationListener` API, only after you grant permission in Windows; shown in Hatsu's notification center. You can deny or revoke it in Windows Settings.
- **Location.** Only if you ask for location-based weather and Windows allows it; used to pick your city.
- **iPhone (Connect, optional, off by default).** Uses Bluetooth with public Windows APIs to read the iPhone's battery and notifications. Notification content is kept in memory and is not written to disk or logs (logs only hold error codes); you choose to show full content, title only, or app only.
- **Media.** Reads the media session Windows exposes (title, artist and player) to display and control it.
- **Windows, apps and local files.** The Dock and bar list open windows and apps; search (Spotlight) queries apps, recent files and the Windows Search index; the island watches your Downloads and Screenshots folders to show download progress and captures; the bar shows local system readings (CPU, memory, network, disks, temperatures). None of this is transmitted.
- **Shelf (Hatsu Notch).** Lists files you drag in; the list is local.
- **Cleanup (optional).** Scans known categories (user temp files, Windows caches, crash dumps and error reports, old logs) and **only deletes what you confirm**. Deleted files **do not go to the Recycle Bin** (permanent deletion). Emptying the Recycle Bin is a separate action with a second confirmation, never pre-selected.
- **Windows taskbar.** If you enable it, Hatsu hides it while running and restores it on exit.
- **Start at sign-in.** Only if you enable it; you can also change it in Settings › Apps › Startup.

## 4. Network communications
| Destination | When | What is sent |
|---|---|---|
| `api.open-meteo.com`, `geocoding-api.open-meteo.com` (Open-Meteo) | When you use weather or search a city | Coordinates or the city text you search |
| `speed.cloudflare.com` (Cloudflare) | Only when you press the speed test | Measurement traffic; your IP address is visible to the service |
| `music.163.com` (NetEase Cloud Music) | Only if you enable synced lyrics | Title and artist of the playing track (duration is used locally to choose a result) |
| `lrclib.net` (LRCLIB) | Only if lyrics are on and NetEase returns nothing | Title and artist of the playing track; the app identifies itself with its name ("HatsuNotch") and version (User-Agent) |

These services have their own policies. Connect uses only local Bluetooth with your iPhone. I found no other network connection in the code. As with any network client, these services can see your IP address.

## 5. Permissions declared by the package
`runFullTrust` (Windows desktop app), other apps' notifications, location, Bluetooth and camera. Each is used only for the feature described above, and Windows may ask you for permission or let you revoke it.

## 6. Children, retention and your rights
- The publisher does not collect personal data, so it cannot access, correct or delete your data: you control it.
- The app is not specifically directed at children.
- **To delete everything:** uninstall Hatsu Desktop from Settings › Apps (the package data store is removed). If you used a non-Store version before, also delete `%APPDATA%\WinMac`, `%LOCALAPPDATA%\WinMac` and `%LOCALAPPDATA%\NotchWin`.

## 7. Changes
If this policy changes, it will be published here with a new date.
