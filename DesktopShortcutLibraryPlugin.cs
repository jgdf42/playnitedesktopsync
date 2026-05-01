using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace DesktopShortcutLibrary
{
    public class DesktopShortcutLibraryPlugin : LibraryPlugin
    {
        private static readonly ILogger Logger = LogManager.GetLogger();
        private static readonly Guid PluginGuid = Guid.Parse("5e43a80a-37d8-4d7e-a6be-b137744aa82b");
        private readonly IPlayniteAPI playniteApi;
        private DesktopShortcutLibrarySettings settings;

        public override Guid Id
        {
            get { return PluginGuid; }
        }

        public override string Name
        {
            get { return "Desktop Shortcuts"; }
        }

        public DesktopShortcutLibraryPlugin(IPlayniteAPI playniteAPI) : base(playniteAPI)
        {
            playniteApi = playniteAPI;
            Properties = new LibraryPluginProperties
            {
                HasCustomizedGameImport = true
            };

            LoadSettings();
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            SyncShortcutFolders(false);
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var games = GetWatchFolderGames().ToList();
            Logger.Info(string.Format("Shortcut folder library scan found {0} supported shortcuts.", games.Count));
            return games;
        }

        public override IEnumerable<Game> ImportGames(LibraryImportGamesArgs args)
        {
            return SyncShortcutFolders(false).AddedGames;
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunSettings)
        {
            return new DesktopShortcutLibrarySettingsView(Settings, playniteApi);
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            return new[]
            {
                new MainMenuItem
                {
                    MenuSection = "@Desktop Shortcuts",
                    Description = "Sync desktop shortcuts",
                    Action = delegate(MainMenuItemActionArgs menuArgs)
                    {
                        var result = SyncShortcutFolders(true);
                        playniteApi.Dialogs.ShowMessage(
                            string.Format(
                                "Shortcut sync complete.\n\nWatch folders: {0}\nFound: {1}\nAdded: {2}\nUpdated: {3}\nRemoved: {4}\nSkipped: {5}",
                                result.WatchFolders,
                                result.Found,
                                result.Added,
                                result.Updated,
                                result.Removed,
                                result.Skipped),
                            "Desktop Shortcuts");
                    }
                }
            };
        }

        internal SyncResult SyncShortcutFolders(bool showErrors)
        {
            var result = new SyncResult();

            try
            {
                var watchFolders = Settings.GetNormalizedWatchFolders();
                var ignoredNames = Settings.GetNormalizedIgnoredShortcutNames();
                var shortcuts = GetAllowedShortcutPaths(watchFolders, ignoredNames, result);
                var shortcutGames = CreateGames(shortcuts).ToList();
                result.WatchFolders = watchFolders.Count;
                result.IgnoredNames = ignoredNames.Count;
                result.Found = shortcutGames.Count;
                Logger.Info(string.Format(
                    "Syncing {0} shortcuts from {1} watch folders into Playnite. Ignored names: {2}, blacklisted shortcuts: {3}.",
                    shortcutGames.Count,
                    watchFolders.Count,
                    ignoredNames.Count,
                    result.Blacklisted));

                var currentIds = new HashSet<string>(
                    shortcutGames.Select(a => a.GameId).Where(a => !string.IsNullOrWhiteSpace(a)),
                    StringComparer.OrdinalIgnoreCase);

                var existingGames = playniteApi.Database.Games
                    .Where(a => a.PluginId == Id)
                    .ToList();

                var existingByGameId = existingGames
                    .Where(a => !string.IsNullOrWhiteSpace(a.GameId))
                    .GroupBy(a => a.GameId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(a => a.Key, a => a.First(), StringComparer.OrdinalIgnoreCase);

                using (playniteApi.Database.BufferedUpdate())
                {
                    foreach (var gameMetadata in shortcutGames)
                    {
                        if (string.IsNullOrWhiteSpace(gameMetadata.GameId))
                        {
                            result.Skipped++;
                            continue;
                        }

                        Game existingGame;
                        if (existingByGameId.TryGetValue(gameMetadata.GameId, out existingGame))
                        {
                            UpdateExistingGame(existingGame, gameMetadata);
                            playniteApi.Database.Games.Update(existingGame);
                            result.Updated++;
                        }
                        else
                        {
                            var importedGame = playniteApi.Database.ImportGame(gameMetadata, this);
                            if (importedGame != null)
                            {
                                result.Added++;
                                result.AddedGames.Add(importedGame);
                            }
                            else
                            {
                                result.Skipped++;
                            }
                        }
                    }

                    foreach (var existingGame in existingGames)
                    {
                        if (!currentIds.Contains(existingGame.GameId))
                        {
                            if (playniteApi.Database.Games.Remove(existingGame.Id))
                            {
                                result.Removed++;
                            }
                        }
                    }
                }

                Logger.Info(string.Format(
                    "Shortcut folder sync complete. Watch folders: {0}, Ignored names: {1}, Blacklisted: {2}, Found: {3}, Added: {4}, Updated: {5}, Removed: {6}, Skipped: {7}.",
                    result.WatchFolders,
                    result.IgnoredNames,
                    result.Blacklisted,
                    result.Found,
                    result.Added,
                    result.Updated,
                    result.Removed,
                    result.Skipped));
            }
            catch (Exception e)
            {
                Logger.Error(e, "Shortcut folder sync failed.");
                if (showErrors)
                {
                    playniteApi.Dialogs.ShowErrorMessage(e.Message, "Desktop Shortcuts");
                }
            }

            return result;
        }

        private DesktopShortcutLibrarySettings Settings
        {
            get
            {
                return settings ?? LoadSettings();
            }
        }

        private DesktopShortcutLibrarySettings LoadSettings()
        {
            settings = LoadPluginSettings<DesktopShortcutLibrarySettings>();
            if (settings == null)
            {
                settings = new DesktopShortcutLibrarySettings();
            }

            settings.AttachPlugin(this);
            if (settings.EnsureInitialized())
            {
                SavePluginSettings(settings);
            }

            return settings;
        }

        private void UpdateExistingGame(Game game, GameMetadata metadata)
        {
            game.Name = metadata.Name;
            game.GameId = metadata.GameId;
            game.PluginId = Id;
            game.IsInstalled = true;
            game.OverrideInstallState = true;
            game.InstallDirectory = metadata.InstallDirectory;
            game.GameActions = new ObservableCollection<GameAction>(metadata.GameActions ?? new List<GameAction>());
            game.Modified = DateTime.Now;
        }

        private IEnumerable<GameMetadata> GetWatchFolderGames()
        {
            return CreateGames(GetAllowedShortcutPaths(
                Settings.GetNormalizedWatchFolders(),
                Settings.GetNormalizedIgnoredShortcutNames(),
                null));
        }

        private static IEnumerable<GameMetadata> CreateGames(IEnumerable<string> shortcuts)
        {
            foreach (var shortcut in shortcuts)
            {
                GameMetadata game = null;

                try
                {
                    game = CreateGame(shortcut);
                }
                catch (Exception e)
                {
                    Logger.Warn(e, string.Format("Failed to import shortcut {0}", shortcut));
                }

                if (game != null)
                {
                    yield return game;
                }
            }
        }

        private static List<string> GetAllowedShortcutPaths(
            IEnumerable<string> watchFolders,
            IEnumerable<string> ignoredNames,
            SyncResult result)
        {
            var ignored = ignoredNames
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var shortcuts = new List<string>();
            foreach (var shortcut in GetWatchFolderShortcuts(watchFolders))
            {
                if (IsShortcutIgnored(shortcut, ignored))
                {
                    if (result != null)
                    {
                        result.Blacklisted++;
                    }

                    continue;
                }

                shortcuts.Add(shortcut);
            }

            return shortcuts;
        }

        private static IEnumerable<string> GetWatchFolderShortcuts(IEnumerable<string> watchFolders)
        {
            var shortcuts = new List<string>();

            foreach (var folder in watchFolders.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                try
                {
                    shortcuts.AddRange(Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly).Where(IsSupportedShortcut));
                }
                catch (Exception e)
                {
                    Logger.Warn(e, string.Format("Failed to scan watch folder {0}", folder));
                }
            }

            return shortcuts
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.CurrentCultureIgnoreCase);
        }

        private static bool IsSupportedShortcut(string path)
        {
            var fileName = Path.GetFileName(path);
            if (fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var extension = Path.GetExtension(path);
            return extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsShortcutIgnored(string path, IEnumerable<string> ignoredNames)
        {
            var shortcutName = Path.GetFileName(path);
            var shortcutNameWithoutExtension = Path.GetFileNameWithoutExtension(path);
            var cleanShortcutName = CleanGameName(shortcutNameWithoutExtension);

            var candidates = new[]
            {
                shortcutName,
                shortcutNameWithoutExtension,
                cleanShortcutName
            };

            foreach (var ignoredName in ignoredNames.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                foreach (var candidate in candidates)
                {
                    if (IsNameMatch(candidate, ignoredName))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsNameMatch(string value, string pattern)
        {
            if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(pattern))
            {
                return false;
            }

            if (pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0)
            {
                var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
                return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return value.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static GameMetadata CreateGame(string shortcutPath)
        {
            var shortcut = ShortcutInfo.FromFile(shortcutPath);
            var launchPath = string.IsNullOrWhiteSpace(shortcut.TargetPath) ? shortcutPath : shortcut.TargetPath;
            var isUrl = IsUrl(launchPath);

            var game = new GameMetadata
            {
                Name = CleanGameName(Path.GetFileNameWithoutExtension(shortcutPath)),
                GameId = StableId(shortcutPath),
                IsInstalled = true,
                Source = new MetadataNameProperty("Shortcut Folder"),
                Platforms = new HashSet<MetadataProperty>
                {
                    new MetadataSpecProperty("pc_windows")
                },
                GameActions = new List<GameAction>
                {
                    new GameAction
                    {
                        Name = "Play",
                        Type = isUrl ? GameActionType.URL : GameActionType.File,
                        Path = launchPath,
                        Arguments = isUrl ? null : shortcut.Arguments,
                        WorkingDir = isUrl ? null : shortcut.WorkingDirectory,
                        IsPlayAction = true,
                        TrackingMode = TrackingMode.Default
                    }
                }
            };

            if (!isUrl)
            {
                game.InstallDirectory = GetInstallDirectory(shortcut, launchPath);
            }

            var iconPath = GetIconPath(shortcut, launchPath, shortcutPath);
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                game.Icon = new MetadataFile(iconPath);
            }

            return game;
        }

        private static string CleanGameName(string name)
        {
            var suffixes = new[] { " - Shortcut", " Shortcut" };
            foreach (var suffix in suffixes)
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return name.Substring(0, name.Length - suffix.Length).Trim();
                }
            }

            return name.Trim();
        }

        private static string StableId(string shortcutPath)
        {
            using (var sha1 = SHA1.Create())
            {
                var fullPath = Path.GetFullPath(shortcutPath).ToUpperInvariant();
                var bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(fullPath));
                return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string GetInstallDirectory(ShortcutInfo shortcut, string launchPath)
        {
            if (!string.IsNullOrWhiteSpace(shortcut.WorkingDirectory) && Directory.Exists(shortcut.WorkingDirectory))
            {
                return shortcut.WorkingDirectory;
            }

            if (!string.IsNullOrWhiteSpace(launchPath) && File.Exists(launchPath))
            {
                return Path.GetDirectoryName(launchPath);
            }

            return null;
        }

        private static string GetIconPath(ShortcutInfo shortcut, string launchPath, string shortcutPath)
        {
            var iconPath = StripIconIndex(shortcut.IconLocation);
            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                return iconPath;
            }

            if (!IsUrl(launchPath) && File.Exists(launchPath))
            {
                return launchPath;
            }

            return File.Exists(shortcutPath) ? shortcutPath : null;
        }

        private static string StripIconIndex(string iconLocation)
        {
            if (string.IsNullOrWhiteSpace(iconLocation))
            {
                return null;
            }

            var commaIndex = iconLocation.IndexOf(',');
            return commaIndex >= 0 ? iconLocation.Substring(0, commaIndex) : iconLocation;
        }

        private static bool IsUrl(string value)
        {
            Uri uri;
            return Uri.TryCreate(value, UriKind.Absolute, out uri) &&
                   !string.IsNullOrWhiteSpace(uri.Scheme) &&
                   !uri.IsFile;
        }

        internal sealed class SyncResult
        {
            public int WatchFolders { get; set; }

            public int IgnoredNames { get; set; }

            public int Blacklisted { get; set; }

            public int Found { get; set; }

            public int Added { get; set; }

            public int Updated { get; set; }

            public int Removed { get; set; }

            public int Skipped { get; set; }

            public List<Game> AddedGames { get; private set; }

            public SyncResult()
            {
                AddedGames = new List<Game>();
            }
        }

        private sealed class ShortcutInfo
        {
            public string TargetPath { get; private set; }

            public string Arguments { get; private set; }

            public string WorkingDirectory { get; private set; }

            public string IconLocation { get; private set; }

            public static ShortcutInfo FromFile(string path)
            {
                if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    return FromUrlFile(path);
                }

                return FromShellLink(path);
            }

            private static ShortcutInfo FromUrlFile(string path)
            {
                var values = File.ReadLines(path)
                    .Select(line => line.Split(new[] { '=' }, 2))
                    .Where(parts => parts.Length == 2)
                    .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

                string url;
                string iconFile;
                values.TryGetValue("URL", out url);
                values.TryGetValue("IconFile", out iconFile);

                return new ShortcutInfo
                {
                    TargetPath = url,
                    IconLocation = iconFile
                };
            }

            private static ShortcutInfo FromShellLink(string path)
            {
                object shell = null;
                object shortcut = null;

                try
                {
                    var shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType == null)
                    {
                        return new ShortcutInfo();
                    }

                    shell = Activator.CreateInstance(shellType);
                    shortcut = shellType.InvokeMember(
                        "CreateShortcut",
                        BindingFlags.InvokeMethod,
                        null,
                        shell,
                        new object[] { path });

                    return new ShortcutInfo
                    {
                        TargetPath = GetProperty(shortcut, "TargetPath"),
                        Arguments = GetProperty(shortcut, "Arguments"),
                        WorkingDirectory = GetProperty(shortcut, "WorkingDirectory"),
                        IconLocation = GetProperty(shortcut, "IconLocation")
                    };
                }
                catch (Exception e)
                {
                    Logger.Warn(e, string.Format("Failed to resolve shortcut {0}", path));
                    return new ShortcutInfo();
                }
                finally
                {
                    ReleaseComObject(shortcut);
                    ReleaseComObject(shell);
                }
            }

            private static string GetProperty(object instance, string name)
            {
                return instance == null
                    ? null
                    : instance.GetType().InvokeMember(name, BindingFlags.GetProperty, null, instance, null) as string;
            }

            private static void ReleaseComObject(object instance)
            {
                if (instance != null && Marshal.IsComObject(instance))
                {
                    Marshal.FinalReleaseComObject(instance);
                }
            }
        }
    }
}
