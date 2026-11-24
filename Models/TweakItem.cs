using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Archon.Models
{
    public enum TweakRisk
    {
        Safe = 0,
        Seguro = 0,
        Recommended = 1,
        Recomendado = 1,
        Advanced = 2,
        Avanzado = 2
    }

    public class TweakItem : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public TweakRisk Risk { get; set; } = TweakRisk.Safe;
        public string TechnicalDetail { get; set; } = string.Empty;
        public Action? ApplyAction { get; set; }
        public Action? RevertAction { get; set; }

        public string RiskLevel => (int)Risk switch
        {
            1 => "RECOMENDADO",
            2 => "AVANZADO",
            _ => "SEGURO"
        };

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

    public class UserProfileConfig
    {
        public Dictionary<string, bool> TweakStates { get; set; } = new();
    }
}
