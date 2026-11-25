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
        private bool _isDetailsOpen;
        public bool IsDetailsOpen
        {
            get => _isDetailsOpen;
            set
            {
                if (_isDetailsOpen != value)
                {
                    _isDetailsOpen = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDetailsOpen)));
                }
            }
        }
        public string GroupName { get; set; } = string.Empty;
        public string IconGeometry { get; set; } = string.Empty;
        public string IconColor { get; set; } = "#3B82F6";
        public bool HasIcon => !string.IsNullOrEmpty(IconGeometry);
        private bool _isEnabled;

        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public TweakRisk Risk { get; set; } = TweakRisk.Safe;
        public string TechnicalDetail { get; set; } = string.Empty;
        public Func<bool>? DetectCurrentState { get; set; }
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

    

    public class UserProfileConfig
    {
        public Dictionary<string, bool> TweakStates { get; set; } = new();
    }
}
