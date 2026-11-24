using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Archon.Models
{
    public enum TweakRisk
    {
        Seguro,
        Recomendado,
        Extremo
    }

    public class TweakItem : INotifyPropertyChanged
    {
        private bool _isEnabled;

        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public TweakRisk Risk { get; set; } = TweakRisk.Seguro;
        public string Description { get; set; } = string.Empty;
        public string TechnicalDetail { get; set; } = string.Empty;

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

        public string RiskLabel => $"[ {Risk.ToString().ToUpper()} ]";

        public string RiskColor => Risk switch
        {
            TweakRisk.Seguro => "#2EA043",
            TweakRisk.Recomendado => "#D97706",
            TweakRisk.Extremo => "#D92323",
            _ => "#9A9A9A"
        };

        public Action? ApplyAction { get; set; }
        public Action? RevertAction { get; set; }
        public Func<bool>? DetectCurrentState { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class UserProfileConfig
    {
        public string ProfileName { get; set; } = "Perfil Archon Personalizado";
        public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, bool> TweakStates { get; set; } = new();
    }
}
