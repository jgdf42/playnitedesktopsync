using Playnite.SDK;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DesktopShortcutLibrary
{
    public sealed class DesktopShortcutLibrarySettingsView : UserControl
    {
        private readonly DesktopShortcutLibrarySettings settings;
        private readonly IPlayniteAPI playniteApi;
        private readonly ListBox folderList;
        private readonly TextBlock countText;
        private readonly Button addButton;
        private readonly Button removeButton;
        private readonly ListBox ignoredList;
        private readonly TextBlock ignoredCountText;
        private readonly Button removeIgnoredButton;

        public DesktopShortcutLibrarySettingsView(DesktopShortcutLibrarySettings settings, IPlayniteAPI playniteApi)
        {
            this.settings = settings;
            this.playniteApi = playniteApi;

            var root = new StackPanel
            {
                Margin = new Thickness(0, 8, 0, 0)
            };

            root.Children.Add(new TextBlock
            {
                Text = "Watch folders",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Shortcuts in these folders are imported into Playnite. Removing a folder removes games imported from its shortcuts during the next sync.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            countText = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 6)
            };
            root.Children.Add(countText);

            folderList = new ListBox
            {
                MinHeight = 140,
                MaxHeight = 260,
                Margin = new Thickness(0, 0, 0, 8)
            };
            folderList.SelectionChanged += delegate { RefreshButtonState(); };
            root.Children.Add(folderList);

            var buttons = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            addButton = new Button
            {
                Content = "Add folder",
                MinWidth = 92,
                Margin = new Thickness(0, 0, 8, 8)
            };
            addButton.Click += AddButton_Click;
            buttons.Children.Add(addButton);

            removeButton = new Button
            {
                Content = "Remove selected",
                MinWidth = 116,
                Margin = new Thickness(0, 0, 8, 8)
            };
            removeButton.Click += RemoveButton_Click;
            buttons.Children.Add(removeButton);

            var restoreButton = new Button
            {
                Content = "Restore Desktop folders",
                MinWidth = 148,
                Margin = new Thickness(0, 0, 8, 8)
            };
            restoreButton.Click += RestoreButton_Click;
            buttons.Children.Add(restoreButton);

            root.Children.Add(buttons);

            root.Children.Add(new TextBlock
            {
                Text = "Ignored shortcut names",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 12, 0, 6)
            });

            root.Children.Add(new TextBlock
            {
                Text = "Entries can be shortcut names with or without .lnk. Wildcards * and ? are supported.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

            ignoredCountText = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 6)
            };
            root.Children.Add(ignoredCountText);

            ignoredList = new ListBox
            {
                MinHeight = 100,
                MaxHeight = 180,
                Margin = new Thickness(0, 0, 0, 8)
            };
            ignoredList.SelectionChanged += delegate { RefreshButtonState(); };
            root.Children.Add(ignoredList);

            var ignoredButtons = new WrapPanel
            {
                Margin = new Thickness(0, 0, 0, 8)
            };

            var addIgnoredButton = new Button
            {
                Content = "Add ignored name",
                MinWidth = 124,
                Margin = new Thickness(0, 0, 8, 8)
            };
            addIgnoredButton.Click += AddIgnoredButton_Click;
            ignoredButtons.Children.Add(addIgnoredButton);

            removeIgnoredButton = new Button
            {
                Content = "Remove selected",
                MinWidth = 116,
                Margin = new Thickness(0, 0, 8, 8)
            };
            removeIgnoredButton.Click += RemoveIgnoredButton_Click;
            ignoredButtons.Children.Add(removeIgnoredButton);

            root.Children.Add(ignoredButtons);

            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = root
            };

            RefreshList();
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (!settings.CanAddWatchFolder())
            {
                playniteApi.Dialogs.ShowMessage(
                    string.Format("You can watch up to {0} folders.", settings.GetWatchFolderLimit()),
                    "Desktop Shortcuts");
                return;
            }

            var folder = playniteApi.Dialogs.SelectFolder();
            if (string.IsNullOrWhiteSpace(folder))
            {
                return;
            }

            var normalizedFolders = settings.GetNormalizedWatchFolders();
            if (normalizedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            settings.WatchFolders = normalizedFolders;
            settings.WatchFolders.Add(folder);
            settings.Normalize();
            RefreshList();
        }

        private void RemoveButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedFolder = folderList.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                return;
            }

            settings.WatchFolders = settings
                .GetNormalizedWatchFolders()
                .Where(a => !a.Equals(selectedFolder, StringComparison.OrdinalIgnoreCase))
                .ToList();
            RefreshList();
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            settings.WatchFolders = settings.GetNormalizedWatchFolders();

            foreach (var folder in DesktopShortcutLibrarySettings.GetDefaultWatchFolders())
            {
                if (settings.WatchFolders.Count >= settings.GetWatchFolderLimit())
                {
                    break;
                }

                if (!settings.WatchFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                {
                    settings.WatchFolders.Add(folder);
                }
            }

            settings.Normalize();
            RefreshList();
        }

        private void AddIgnoredButton_Click(object sender, RoutedEventArgs e)
        {
            var result = playniteApi.Dialogs.SelectString(
                "Add ignored shortcut name",
                "Shortcut name or wildcard pattern",
                string.Empty);

            if (!result.Result || string.IsNullOrWhiteSpace(result.SelectedString))
            {
                return;
            }

            settings.IgnoredShortcutNames = settings.GetNormalizedIgnoredShortcutNames();
            var value = result.SelectedString.Trim();
            if (!settings.IgnoredShortcutNames.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                settings.IgnoredShortcutNames.Add(value);
            }

            settings.Normalize();
            RefreshList();
        }

        private void RemoveIgnoredButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedName = ignoredList.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(selectedName))
            {
                return;
            }

            settings.IgnoredShortcutNames = settings
                .GetNormalizedIgnoredShortcutNames()
                .Where(a => !a.Equals(selectedName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            RefreshList();
        }

        private void RefreshList()
        {
            settings.Normalize();
            folderList.ItemsSource = null;
            folderList.ItemsSource = settings.WatchFolders;
            countText.Text = string.Format("{0} of {1} watch folders", settings.WatchFolders.Count, settings.GetWatchFolderLimit());
            ignoredList.ItemsSource = null;
            ignoredList.ItemsSource = settings.IgnoredShortcutNames;
            ignoredCountText.Text = string.Format("{0} ignored shortcut names", settings.IgnoredShortcutNames.Count);
            RefreshButtonState();
        }

        private void RefreshButtonState()
        {
            addButton.IsEnabled = settings.CanAddWatchFolder();
            removeButton.IsEnabled = folderList.SelectedItem != null;
            removeIgnoredButton.IsEnabled = ignoredList.SelectedItem != null;
        }
    }
}
