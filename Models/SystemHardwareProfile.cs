using System.Collections.Generic;

namespace Archon.Models
{
    public class SystemHardwareProfile
    {
        public string CpuName { get; set; } = string.Empty;
        public string MotherboardName { get; set; } = string.Empty;
        public string PrimaryGpu { get; set; } = string.Empty;
        public string TotalRamGb { get; set; } = string.Empty;
        public string PrimaryStorage { get; set; } = string.Empty;
        public string WindowsVersion { get; set; } = string.Empty;
        public bool IsAmdCpu { get; set; }
        public bool IsIntelCpu { get; set; }
        public bool HasNvidiaGpu { get; set; }
        public bool HasAmdGpu { get; set; }
        public bool HasIntelGpu { get; set; }
        public List<string> DetectedBloatware { get; set; } = new();
    }
}
