# Desktop Shortcut Library

Desktop Shortcut Library is a Playnite library extension that imports Windows shortcut files from configurable watch folders.

<img width="1784" height="1138" alt="image" src="https://github.com/user-attachments/assets/9c103ed4-171f-4b12-99f7-51c33ffa6094" />

## Features

- Imports Windows `.lnk` shortcuts as Playnite games.
- Watches the current user's Desktop and Public Desktop by default.
- Supports up to 10 additional watch folders.
- Lets users remove the default Desktop folders if they are not needed.
- Supports ignored shortcut names with exact matches or simple wildcards like `*Launcher*`.
- Syncs on startup/library update and includes a manual `Desktop Shortcuts -> Sync desktop shortcuts` action.
- Removes plugin-imported games when their source shortcut is removed or ignored.

## Supported Files

Only `.lnk` files are imported. `.url` files and raw `.exe` files are ignored.

## Installation

Precompiled `.pext` packages are available on the [GitHub Releases page](https://github.com/jgdf42/playnitedesktopsync/releases).

1. Download the latest `DesktopShortcutLibrary_*.pext` file.
2. Open the file with Playnite.
3. Restart Playnite if prompted.

---

## OPTIONAL: Building from Source

This project targets .NET Framework 4.6.2 and references the Playnite SDK package.

## Packaging

Build the extension DLL, then package `DesktopShortcutLibrary.dll` and `extension.yaml` into a `.pext` file for installation in Playnite.
