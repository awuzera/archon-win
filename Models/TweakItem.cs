using System;

namespace Archon.Models
{
    public enum TweakRisk
    {
        Seguro,
        Recomendado
    }

    public class TweakItem
    {
        public string Id { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string GroupName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TechnicalDetail { get; set; } = string.Empty;
        public TweakRisk Risk { get; set; } = TweakRisk.Seguro;
        public string IconGeometry { get; set; } = string.Empty;
        public string IconColor { get; set; } = "#3B82F6";
        public bool HasIcon => !string.IsNullOrEmpty(IconGeometry);
        public bool IsEnabled { get; set; }
        public Action? ApplyAction { get; set; }
        public Action? RevertAction { get; set; }
    }
}
