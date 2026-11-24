using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Archon.Core
{
    public class OptimizerState
    {
        public bool StepZeroCompleted { get; set; }
        public bool RestorePointCreated { get; set; }
        public bool PendingPostRebootInstall { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public static class StepZeroOrchestrator
    {
        private static readonly string StateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Archon");

        private static readonly string StateFile = Path.Combine(StateDir, "state.json");

        public static OptimizerState LoadState()
        {
            if (!File.Exists(StateFile))
                return new OptimizerState();

            var json = File.ReadAllText(StateFile);
            return JsonSerializer.Deserialize<OptimizerState>(json) ?? new OptimizerState();
        }

        public static void SaveState(OptimizerState state)
        {
            Directory.CreateDirectory(StateDir);
            state.LastUpdated = DateTime.UtcNow;
            File.WriteAllText(StateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static void SetWindowsUpdateDriverBlock(bool blockDrivers)
        {
            const string policyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
            using var key = Registry.LocalMachine.CreateSubKey(policyPath);
            if (blockDrivers)
                key.SetValue("ExcludeWUDriversInQualityUpdate", 1, RegistryValueKind.DWord);
            else
                key.DeleteValue("ExcludeWUDriversInQualityUpdate", throwOnMissingValue: false);
        }

        public static void RegisterPostRebootResume()
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            using var runOnce = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", writable: true);
            runOnce?.SetValue("ArchonResume", $"\"{exePath}\" --resume-step0", RegistryValueKind.String);
        }

        public static async Task<int> RunSilentProcessAsync(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar {fileName}");
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
    }
}
