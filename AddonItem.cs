using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MSFS_AddonManager
{
    public class AddonItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public string Name { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public DateTime InstalledDate { get; set; }

        public string SizeDisplay => SizeBytes >= 1_073_741_824
            ? $"{SizeBytes / 1_073_741_824.0:F2} GB"
            : SizeBytes >= 1_048_576
                ? $"{SizeBytes / 1_048_576.0:F1} MB"
                : $"{SizeBytes / 1024.0:F0} KB";

        public string DateInstalled => InstalledDate.ToString("MMM dd, yyyy");

        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = "")
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
