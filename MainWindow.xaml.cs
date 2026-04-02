using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MSFS_AddonManager
{
    public partial class MainWindow : Window
    {
        private string _communityFolder = string.Empty;
        private ObservableCollection<AddonItem> _allAddons = new();
        private ObservableCollection<AddonItem> _filteredAddons = new();

        public MainWindow()
        {
            InitializeComponent();
            AddonListView.ItemsSource = _filteredAddons;

            
            string configPath = GetSafeConfigPath();

            if (File.Exists(configPath))
            {
                string savedPath = File.ReadAllText(configPath);
                if (Directory.Exists(savedPath))
                {
                    SetCommunityFolder(savedPath);
                    return; 
                }
            }

            // If no text file exists, try to auto-detect
            TryAutoDetectFolder();
        }

        // ─────────────────────────────── AUTO-DETECT ───────────────────────────────

        private void TryAutoDetectFolder()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Packages", "Microsoft.FlightSimulator_8wekyb3d8bbwe", "LocalCache", "Packages", "Community"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft Flight Simulator", "Packages", "Community"),
            };

            foreach (var path in candidates)
            {
                if (Directory.Exists(path))
                {
                    SetCommunityFolder(path);
                    return;
                }
            }
        }

        // ─────────────────────────────── FOLDER BROWSE ─────────────────────────────

        private void BrowseFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select your MSFS Community Folder"
            };

            if (_communityFolder != string.Empty)
                dialog.InitialDirectory = _communityFolder;

            if (dialog.ShowDialog() == true)
            {
                SetCommunityFolder(dialog.FolderName);
            }
        }

        private void SetCommunityFolder(string path)
        {
            _communityFolder = path;
            CommunityPathBox.Text = path;
            InstallButton.IsEnabled = true;

            
            string configPath = GetSafeConfigPath();
            File.WriteAllText(configPath, path);

            LoadAddons();
        }

        // ─────────────────────────────── LOAD ADDONS ───────────────────────────────

        private void RefreshAddons_Click(object sender, RoutedEventArgs e) => LoadAddons();

        private void LoadAddons()
        {
            if (string.IsNullOrEmpty(_communityFolder) || !Directory.Exists(_communityFolder))
            {
                SetStatus("Community folder not found.");
                return;
            }

            _allAddons.Clear();

            try
            {
                var dirs = Directory.GetDirectories(_communityFolder);
                foreach (var dir in dirs)
                {
                    var info = new DirectoryInfo(dir);
                    long size = GetDirectorySize(info);

                    _allAddons.Add(new AddonItem
                    {
                        Name = info.Name,
                        FolderPath = dir,
                        SizeBytes = size,
                        InstalledDate = info.CreationTime // Formats the date nicely
                    });
                }

                ApplySearch();
                UpdateStats();
                SetStatus($"Loaded {_allAddons.Count} addon(s) from Community folder.");
            }
            catch (Exception ex)
            {
                SetStatus($"Error loading addons: {ex.Message}");
            }
        }

        private static long GetDirectorySize(DirectoryInfo dir)
        {
            long size = 0;
            try
            {
                foreach (var file in dir.GetFiles("*", SearchOption.AllDirectories))
                    size += file.Length;
            }
            catch { /* skip inaccessible files */ }
            return size;
        }

        // ─────────────────────────────── SEARCH ────────────────────────────────────

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

        private void ApplySearch()
        {
            var query = SearchBox.Text.Trim().ToLowerInvariant();
            _filteredAddons.Clear();

            var results = string.IsNullOrEmpty(query)
                ? _allAddons
                : _allAddons.Where(a => a.Name.ToLowerInvariant().Contains(query));

            foreach (var item in results)
                _filteredAddons.Add(item);

            EmptyState.Visibility = _filteredAddons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            AddonListView.Visibility = _filteredAddons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            AddonCountLabel.Text = _filteredAddons.Count.ToString();
        }

        // ─────────────────────────────── INSTALL ───────────────────────────────────

        private async void InstallAddon_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Addon Archive",
                Filter = "Archive Files (*.zip;*.rar;*.7z)|*.zip;*.rar;*.7z|All Files (*.*)|*.*",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;

            var archivePath = dialog.FileName;
            ShowProgress(true, $"Installing {Path.GetFileName(archivePath)}...");

            try
            {
                await Task.Run(() => ExtractArchive(archivePath, _communityFolder));
                SetStatus($"✔ Successfully installed: {Path.GetFileNameWithoutExtension(archivePath)}");
                LoadAddons();
            }
            catch (Exception ex)
            {
                SetStatus($"✘ Install failed.");
                MessageBox.Show($"Failed to install addon:\n\n{ex.Message}", "Install Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ShowProgress(false);
            }
        }

        private static void ExtractArchive(string archivePath, string destinationFolder)
        {
            string tempFolder = Path.Combine(Path.GetTempPath(), "MSFS_Mod_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempFolder);

            try
            {
                using (Stream stream = File.OpenRead(archivePath))
                using (var reader = ReaderFactory.Open(stream))
                {
                    while (reader.MoveToNextEntry())
                    {
                        if (!reader.Entry.IsDirectory)
                        {
                            reader.WriteEntryToDirectory(tempFolder, new ExtractionOptions
                            {
                                ExtractFullPath = true,
                                Overwrite = true
                            });
                        }
                    }
                }

                
                var layoutFile = Directory.GetFiles(tempFolder, "layout.json", SearchOption.AllDirectories).FirstOrDefault();
                if (layoutFile == null) throw new Exception("No layout.json found in this archive. It might not be a valid MSFS Mod.");

                string modSourceFolder = Path.GetDirectoryName(layoutFile);
                string modName = new DirectoryInfo(modSourceFolder).Name;
                string finalDestination = Path.Combine(destinationFolder, modName);

                if (Directory.Exists(finalDestination)) DeleteDirectorySecure(finalDestination);

                
                CopyDirectory(modSourceFolder, finalDestination);
            }
            finally
            {
                if (Directory.Exists(tempFolder)) DeleteDirectorySecure(tempFolder);
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);
            foreach (var file in Directory.GetFiles(sourceDir))
                File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(sourceDir))
                CopyDirectory(dir, Path.Combine(destinationDir, Path.GetFileName(dir)));
        }

        // ─────────────────────────────── UNINSTALL ─────────────────────────────────

        private void UninstallAddon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AddonItem addon)
            {
                ConfirmAndUninstall(new List<AddonItem> { addon });
            }
        }

        private void BatchUninstall_Click(object sender, RoutedEventArgs e)
        {
            var selected = AddonListView.SelectedItems.Cast<AddonItem>().ToList();
            if (selected.Count > 0)
                ConfirmAndUninstall(selected);
        }

        private void AddonListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            BatchUninstallBtn.IsEnabled = AddonListView.SelectedItems.Count > 0;
        }

        private void ConfirmAndUninstall(List<AddonItem> addons)
        {
            var names = string.Join("\n• ", addons.Select(a => a.Name));
            var msg = addons.Count == 1
                ? $"Are you sure you want to uninstall:\n\n• {names}\n\nThis will permanently delete the folder."
                : $"Uninstall {addons.Count} addons?\n\n• {names}\n\nThis will permanently delete these folders.";

            var result = MessageBox.Show(msg, "Confirm Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            int removed = 0, failed = 0;
            foreach (var addon in addons)
            {
                try
                {
                    if (Directory.Exists(addon.FolderPath))
                    {
                        DeleteDirectorySecure(addon.FolderPath);
                        removed++;
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    SetStatus($"Failed to delete {addon.Name}: {ex.Message}");
                }
            }

            SetStatus(failed == 0
                ? $"✔ Uninstalled {removed} addon(s)."
                : $"✔ Uninstalled {removed}, ✘ {failed} failed.");

            LoadAddons();
        }

        private static void DeleteDirectorySecure(string targetDir)
        {
            string[] files = Directory.GetFiles(targetDir);
            string[] dirs = Directory.GetDirectories(targetDir);

            foreach (string file in files)
            {
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
            }

            foreach (string dir in dirs)
            {
                DeleteDirectorySecure(dir);
            }

            File.SetAttributes(targetDir, FileAttributes.Normal);
            Directory.Delete(targetDir, false);
        }

        // ─────────────────────────────── HELPERS ───────────────────────────────────

        private void UpdateStats()
        {
            StatCount.Text = _allAddons.Count.ToString();
            long totalBytes = _allAddons.Sum(a => a.SizeBytes);
            StatSize.Text = totalBytes >= 1_073_741_824
                ? $"{totalBytes / 1_073_741_824.0:F1} GB"
                : $"{totalBytes / 1_048_576.0:F0} MB";
        }

        private void ShowProgress(bool visible, string? message = null)
        {
            ProgressOverlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (message != null) ProgressLabel.Text = message;
        }

        private void SetStatus(string message)
        {
            Dispatcher.Invoke(() => StatusLabel.Text = message);
        }

        
        private string GetSafeConfigPath()
        {
            
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            
            string myAppFolder = Path.Combine(appData, "MSFS_AddonManager");

            
            if (!Directory.Exists(myAppFolder))
            {
                Directory.CreateDirectory(myAppFolder);
            }

            // 4. Return the full, safe path to the text file
            return Path.Combine(myAppFolder, "saved_path.txt");
        }
    }

}
