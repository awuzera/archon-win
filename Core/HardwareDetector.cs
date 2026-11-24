using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using Microsoft.Win32;

namespace Archon.Core
{
    public class SystemHardwareProfile
    {
        public string CpuName { get; set; } = "Detectando...";
        public bool IsAmdCpu => CpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase);
        public bool IsIntelCpu => CpuName.Contains("Intel", StringComparison.OrdinalIgnoreCase);

        public List<string> GpuNames { get; set; } = new();
        public string PrimaryGpu => GpuNames.FirstOrDefault() ?? "Adaptador Básico";
        public bool HasNvidiaGpu => GpuNames.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        public bool HasAmdGpu => GpuNames.Any(g => g.Contains("AMD", StringComparison.OrdinalIgnoreCase) || g.Contains("Radeon", StringComparison.OrdinalIgnoreCase));

        public string Motherboard { get; set; } = "Desconocida";
        public string TotalRamGb { get; set; } = "0 GB";
        public string WindowsVersion { get; set; } = "Windows";
        public List<string> DetectedBloatware { get; set; } = new();
    }

    public static class HardwareDetector
    {
        private static readonly string[] TargetBloatKeywords =
        {
            "Armoury Crate",
            "NVIDIA GeForce Experience",
            "NVIDIA App",
            "AMD Software",
            "MSI Center",
            "Gigabyte Control Center",
            "Aura Service"
        };

        public static SystemHardwareProfile ScanSystem()
        {
            var profile = new SystemHardwareProfile();

            using (var cpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
            {
                foreach (var obj in cpuSearcher.Get())
                {
                    profile.CpuName = obj["Name"]?.ToString()?.Trim() ?? "Desconocido";
                    break;
                }
            }

            using (var gpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController"))
            {
                foreach (var obj in gpuSearcher.Get())
                {
                    var name = obj["Name"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(name) && !name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                    {
                        profile.GpuNames.Add(name);
                    }
                }
            }

            using (var moboSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
            {
                foreach (var obj in moboSearcher.Get())
                {
                    profile.Motherboard = $"{obj["Manufacturer"]} {obj["Product"]}".Trim();
                    break;
                }
            }

            using (var ramSearcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
            {
                foreach (var obj in ramSearcher.Get())
                {
                    if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out ulong bytes))
                    {
                        double gb = Math.Round(bytes / (1024.0 * 1024.0 * 1024.0));
                        profile.TotalRamGb = $"{gb} GB";
                    }
                    break;
                }
            }

            int build = Environment.OSVersion.Version.Build;
            profile.WindowsVersion = build >= 22000 ? $"Windows 11 (Build {build})" : $"Windows 10 (Build {build})";
            profile.DetectedBloatware = ScanInstalledBloat();

            return profile;
        }

        private static List<string> ScanInstalledBloat()
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string[] registryPaths =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var path in registryPaths)
            {
                using var baseKey = Registry.LocalMachine.OpenSubKey(path);
                if (baseKey == null) continue;

                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    using var subKey = baseKey.OpenSubKey(subKeyName);
                    var displayName = subKey?.GetValue("DisplayName")?.ToString();
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    if (TargetBloatKeywords.Any(k => displayName.Contains(k, StringComparison.OrdinalIgnoreCase)))
                    {
                        found.Add(displayName);
                    }
                }
            }

            return found.ToList();
        }
    }
}
