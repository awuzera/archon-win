cat << 'EOF' > Core/HardwareDetector.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;
using Archon.Models;

namespace Archon.Core
{
    public static class HardwareDetector
    {
        public static SystemHardwareProfile ScanSystem()
        {
            var profile = new SystemHardwareProfile();

            try
            {
                using var cpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
                foreach (ManagementObject obj in cpuSearcher.Get())
                {
                    string rawCpu = obj["Name"]?.ToString() ?? "CPU Desconocido";
                    profile.CpuName = Regex.Replace(rawCpu, @"\s+", " ").Trim();
                    profile.IsAmdCpu = profile.CpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                       profile.CpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase);
                    break;
                }
            }
            catch { }

            try
            {
                using var moboSearcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
                foreach (ManagementObject obj in moboSearcher.Get())
                {
                    string mfg = CleanManufacturer(obj["Manufacturer"]?.ToString() ?? "");
                    string prod = (obj["Product"]?.ToString() ?? "").Trim();
                    string combined = $"{mfg} {prod}".Trim();
                    if (!string.IsNullOrWhiteSpace(combined))
                        profile.MotherboardName = combined;
                    break;
                }
            }
            catch { }

            try
            {
                var gpus = new List<string>();
                using var gpuSearcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (ManagementObject obj in gpuSearcher.Get())
                {
                    string name = (obj["Name"]?.ToString() ?? "").Trim();
                    if (!string.IsNullOrEmpty(name) &&
                        !name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) &&
                        !name.Contains("Parsec", StringComparison.OrdinalIgnoreCase))
                    {
                        gpus.Add(name);
                    }
                }

                string? dedicatedGpu = gpus.FirstOrDefault(g =>
                    g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                    g.Contains("Arc", StringComparison.OrdinalIgnoreCase));

                profile.PrimaryGpu = dedicatedGpu ?? gpus.FirstOrDefault() ?? "GPU Desconocida";
                profile.HasNvidiaGpu = gpus.Any(g => g.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
                profile.HasAmdGpu = !profile.HasNvidiaGpu &&
                                    gpus.Any(g => g.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                                  g.Contains("Radeon", StringComparison.OrdinalIgnoreCase));
            }
            catch { }

            try
            {
                double totalGb = 0;
                using var csSearcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (ManagementObject obj in csSearcher.Get())
                {
                    if (ulong.TryParse(obj["TotalPhysicalMemory"]?.ToString(), out ulong bytes))
                        totalGb = Math.Round(bytes / (1024.0 * 1024.0 * 1024.0));
                    break;
                }

                uint speedMhz = 0;
                using var memSearcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in memSearcher.Get())
                {
                    if (uint.TryParse(obj["ConfiguredClockSpeed"]?.ToString(), out uint cfgSpeed) && cfgSpeed > 0)
                        speedMhz = cfgSpeed;
                    else if (uint.TryParse(obj["Speed"]?.ToString(), out uint spd) && spd > 0)
                        speedMhz = spd;
                    if (speedMhz > 0) break;
                }

                profile.TotalRamGb = speedMhz > 0 ? $"{totalGb} GB @ {speedMhz} MHz" : $"{totalGb} GB";
            }
            catch { }

            try
            {
                using var diskSearcher = new ManagementObjectSearcher("SELECT Model, Size, MediaType FROM Win32_DiskDrive");
                foreach (ManagementObject obj in diskSearcher.Get())
                {
                    string media = obj["MediaType"]?.ToString() ?? "";
                    if (media.Contains("Removable", StringComparison.OrdinalIgnoreCase) ||
                        media.Contains("External", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string model = (obj["Model"]?.ToString() ?? "Disco Principal").Trim();
                    if (ulong.TryParse(obj["Size"]?.ToString(), out ulong diskBytes) && diskBytes > 0)
                    {
                        double gb = Math.Round(diskBytes / (1000.0 * 1000.0 * 1000.0));
                        string cap = gb >= 1000 ? $"{Math.Round(gb / 1000.0, 1)} TB" : $"{gb} GB";
                        profile.PrimaryStorage = $"{model} ({cap})";
                    }
                    else
                    {
                        profile.PrimaryStorage = model;
                    }
                    break;
                }
            }
            catch { }

            try
            {
                using var osSearcher = new ManagementObjectSearcher("SELECT Caption, BuildNumber FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in osSearcher.Get())
                {
                    string caption = (obj["Caption"]?.ToString() ?? "Windows").Replace("Microsoft ", "").Trim();
                    string build = obj["BuildNumber"]?.ToString() ?? "";
                    profile.WindowsVersion = string.IsNullOrEmpty(build) ? caption : $"{caption} (Build {build})";
                    break;
                }
            }
            catch { }

            profile.DetectedBloatware = DetectManufacturerBloat();
            return profile;
        }

        private static string CleanManufacturer(string raw)
        {
            if (raw.Contains("ASUSTeK", StringComparison.OrdinalIgnoreCase)) return "ASUS";
            if (raw.Contains("Micro-Star", StringComparison.OrdinalIgnoreCase)) return "MSI";
            if (raw.Contains("Gigabyte", StringComparison.OrdinalIgnoreCase)) return "GIGABYTE";
            if (raw.Contains("ASRock", StringComparison.OrdinalIgnoreCase)) return "ASRock";
            return raw.Trim();
        }

        private static List<string> DetectManufacturerBloat()
        {
            var found = new List<string>();
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            if (Directory.Exists(Path.Combine(pf, "ASUS", "ARMOURY CRATE Service")) ||
                Directory.Exists(Path.Combine(pf86, "ASUS", "ArmouryDevice")))
                found.Add("ASUS Armoury Crate");

            if (Directory.Exists(Path.Combine(pf, "NVIDIA Corporation", "NVIDIA GeForce Experience")) ||
                Directory.Exists(Path.Combine(pf, "NVIDIA Corporation", "NVIDIA App")))
                found.Add("NVIDIA GeForce Experience / App");

            if (Directory.Exists(Path.Combine(pf86, "MSI", "MSI Center")))
                found.Add("MSI Center");

            if (Directory.Exists(Path.Combine(pf86, "Razer", "Synapse3")))
                found.Add("Razer Synapse");

            return found;
        }
    }
}
EOF
