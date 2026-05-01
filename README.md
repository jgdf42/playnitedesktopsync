# playnitedesktopsync

Desktop Shortcut Library is a small Playnite library extension that imports Windows desktop shortcuts into your Playnite library.

It scans configurable watch folders for .lnk files, imports them as installed games, and uses the shortcut’s target, arguments, working directory, and icon when available. By default it watches the current user Desktop and Public Desktop, but you can customize those folders in the extension settings and add up to 10 additional watch folders.

The extension syncs on startup/library update and also includes a manual Desktop Shortcuts -> Sync desktop shortcuts menu action. If a shortcut is removed from a watched folder, the matching Playnite entry imported by this extension is removed on the next sync. If a shortcut is renamed, syncing updates the library entry behavior through the shortcut scan. Supports blacklisting certain shortcuts from syncing with wildcard support.

Current support: Windows .lnk shortcuts only. It does not import .url files or raw .exe files.
