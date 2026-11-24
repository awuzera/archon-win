cat << 'EOF' > Models/TweakItem.cs
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Archon.Models
{
    public class TweakItem : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = "SEGURO";
        public string TechnicalDetail { get; set; } = string.Empty;
        public Action? ApplyAction { get; set; }
        public Action? RevertAction { get; set; }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class SystemHardwareProfile
    {
        public string CpuName { get; set; } = "Desconocido";
        public string MotherboardName { get; set; } = "Desconocida";
        public string PrimaryGpu { get; set; } = "Desconocida";
        public string TotalRamGb { get; set; } = "Desconocida";
        public string PrimaryStorage { get; set; } = "Desconocido";
        public string WindowsVersion { get; set; } = "Windows";
        public bool IsAmdCpu { get; set; }
        public bool HasNvidiaGpu { get; set; }
        public bool HasAmdGpu { get; set; }
        public List<string> DetectedBloatware { get; set; } = new();
    }

    public class OptimizerState
    {
        public bool RestorePointCreated { get; set; }
        public bool StepZeroCompleted { get; set; }
        public bool PendingPostRebootInstall { get; set; }
    }

    public class UserProfileConfig
    {
        public Dictionary<string, bool> TweakStates { get; set; } = new();
    }
}
EOF
