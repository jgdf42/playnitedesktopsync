using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

namespace DesktopShortcutLibrary
{
    public class DesktopShortcutLibrarySettings : ISettings
    {
        public const int AdditionalWatchFolderLimit = 10;

        private DesktopShortcutLibraryPlugin plugin;
        private DesktopShortcutLibrarySettings editingClone;

        public bool WatchFoldersInitialized { get; set; }

        public List<string> WatchFolders { get; set; }

        public List<string> IgnoredShortcutNames { get; set; }

        public DesktopShortcutLibrarySettings()
        {
            WatchFolders = new List<string>();
            IgnoredShortcutNames = new List<string>();
        }

        internal void AttachPlugin(DesktopShortcutLibraryPlugin sourcePlugin)
        {
            plugin = sourcePlugin;
        }

        internal bool EnsureInitialized()
        {
            var changed = false;

            if (WatchFolders == null)
            {
                WatchFolders = new List<string>();
                changed = true;
            }

            if (IgnoredShortcutNames == null)
            {
                IgnoredShortcutNames = new List<string>();
                changed = true;
            }

            if (!WatchFoldersInitialized)
            {
                WatchFolders = GetDefaultWatchFolders();
                WatchFoldersInitialized = true;
                changed = true;
            }

            changed = Normalize() || changed;
            return changed;
        }

        internal bool Normalize()
        {
            var normalizedWatchFolders = GetNormalizedWatchFolders();
            var normalizedIgnoredNames = GetNormalizedIgnoredShortcutNames();
            var changed =
                WatchFolders == null ||
                WatchFolders.Count != normalizedWatchFolders.Count ||
                !WatchFolders.SequenceEqual(normalizedWatchFolders, StringComparer.OrdinalIgnoreCase) ||
                IgnoredShortcutNames == null ||
                IgnoredShortcutNames.Count != normalizedIgnoredNames.Count ||
                !IgnoredShortcutNames.SequenceEqual(normalizedIgnoredNames, StringComparer.OrdinalIgnoreCase);

            WatchFolders = normalizedWatchFolders;
            IgnoredShortcutNames = normalizedIgnoredNames;
            return changed;
        }

        internal List<string> GetNormalizedWatchFolders()
        {
            if (WatchFolders == null)
            {
                return new List<string>();
            }

            return WatchFolders
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => Environment.ExpandEnvironmentVariables(a.Trim()))
                .Select(a => TrimTrailingDirectorySeparator(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal List<string> GetNormalizedIgnoredShortcutNames()
        {
            if (IgnoredShortcutNames == null)
            {
                return new List<string>();
            }

            return IgnoredShortcutNames
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Select(a => a.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        internal int GetWatchFolderLimit()
        {
            return GetDefaultWatchFolders().Count + AdditionalWatchFolderLimit;
        }

        internal bool CanAddWatchFolder()
        {
            return GetNormalizedWatchFolders().Count < GetWatchFolderLimit();
        }

        public void BeginEdit()
        {
            editingClone = Clone();
        }

        public void CancelEdit()
        {
            if (editingClone != null)
            {
                WatchFoldersInitialized = editingClone.WatchFoldersInitialized;
                WatchFolders = new List<string>(editingClone.WatchFolders ?? new List<string>());
                IgnoredShortcutNames = new List<string>(editingClone.IgnoredShortcutNames ?? new List<string>());
            }
        }

        public void EndEdit()
        {
            WatchFoldersInitialized = true;
            Normalize();

            if (plugin != null)
            {
                plugin.SavePluginSettings(this);
                plugin.SyncShortcutFolders(false);
            }
        }

        public bool VerifySettings(out List<string> errors)
        {
            errors = new List<string>();
            var folders = GetNormalizedWatchFolders();
            var limit = GetWatchFolderLimit();
            var ignoredNames = GetNormalizedIgnoredShortcutNames();

            if (folders.Count > limit)
            {
                errors.Add(string.Format("You can watch up to {0} folders.", limit));
            }

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                {
                    errors.Add(string.Format("Watch folder does not exist: {0}", folder));
                }
            }

            foreach (var ignoredName in ignoredNames)
            {
                if (ignoredName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 &&
                    ignoredName.IndexOf('*') < 0 &&
                    ignoredName.IndexOf('?') < 0)
                {
                    errors.Add(string.Format("Ignored shortcut name contains invalid filename characters: {0}", ignoredName));
                }
            }

            return errors.Count == 0;
        }

        internal static List<string> GetDefaultWatchFolders()
        {
            var folders = new List<string>
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            };

            return folders
                .Where(a => !string.IsNullOrWhiteSpace(a) && Directory.Exists(a))
                .Select(a => TrimTrailingDirectorySeparator(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private DesktopShortcutLibrarySettings Clone()
        {
            return new DesktopShortcutLibrarySettings
            {
                WatchFoldersInitialized = WatchFoldersInitialized,
                WatchFolders = new List<string>(WatchFolders ?? new List<string>()),
                IgnoredShortcutNames = new List<string>(IgnoredShortcutNames ?? new List<string>())
            };
        }

        private static string TrimTrailingDirectorySeparator(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var root = Path.GetPathRoot(path);
            while (path.Length > root.Length &&
                (path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                 path.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)))
            {
                path = path.Substring(0, path.Length - 1);
            }

            return path;
        }
    }
}
