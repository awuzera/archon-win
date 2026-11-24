using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Archon.Core
{
    public class RegistryBackupEntry
    {
        public string Hive { get; set; } = string.Empty;
        public string SubKey { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public bool Existed { get; set; }
        public string? OriginalValue { get; set; }
        public RegistryValueKind ValueKind { get; set; }
    }

    public static class RegistryEngine
    {
        private static readonly string BackupFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Archon",
            "registry_backup.json");

        private static Dictionary<string, RegistryBackupEntry> _backups = LoadBackups();

        private static Dictionary<string, RegistryBackupEntry> LoadBackups()
        {
            if (!File.Exists(BackupFile))
                return new Dictionary<string, RegistryBackupEntry>();

            var json = File.ReadAllText(BackupFile);
            return JsonSerializer.Deserialize<Dictionary<string, RegistryBackupEntry>>(json)
                   ?? new Dictionary<string, RegistryBackupEntry>();
        }

        private static void SaveBackups()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackupFile)!);
            File.WriteAllText(BackupFile, JsonSerializer.Serialize(_backups, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static RegistryKey GetBaseHive(string hive) =>
            hive.Equals("HKCU", StringComparison.OrdinalIgnoreCase) ? Registry.CurrentUser : Registry.LocalMachine;

        public static void SetValueWithBackup(string hive, string subKeyPath, string valueName, object newValue, RegistryValueKind kind)
        {
            string uniqueId = $"{hive}\\{subKeyPath}\\{valueName}";
            var baseKey = GetBaseHive(hive);

            if (!_backups.ContainsKey(uniqueId))
            {
                using var readKey = baseKey.OpenSubKey(subKeyPath, writable: false);
                var currentVal = readKey?.GetValue(valueName);

                _backups[uniqueId] = new RegistryBackupEntry
                {
                    Hive = hive,
                    SubKey = subKeyPath,
                    ValueName = valueName,
                    Existed = currentVal != null,
                    OriginalValue = currentVal?.ToString(),
                    ValueKind = currentVal != null ? readKey!.GetValueKind(valueName) : kind
                };
                SaveBackups();
            }

            using var writeKey = baseKey.CreateSubKey(subKeyPath);
            writeKey.SetValue(valueName, newValue, kind);
        }

        public static bool CheckDwordEquals(string hive, string subKeyPath, string valueName, int expectedValue)
        {
            var baseKey = GetBaseHive(hive);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            var val = key?.GetValue(valueName);
            return val != null && int.TryParse(val.ToString(), out int current) && current == expectedValue;
        }

        public static void RestoreAllBackups()
        {
            foreach (var entry in _backups.Values)
            {
                var baseKey = GetBaseHive(entry.Hive);
                using var key = baseKey.OpenSubKey(entry.SubKey, writable: true);
                if (key == null) continue;

                if (!entry.Existed)
                {
                    key.DeleteValue(entry.ValueName, throwOnMissingValue: false);
                }
                else if (entry.OriginalValue != null)
                {
                    object parsed = entry.ValueKind == RegistryValueKind.DWord
                        ? int.Parse(entry.OriginalValue)
                        : entry.OriginalValue;
                    key.SetValue(entry.ValueName, parsed, entry.ValueKind);
                }
            }
        }

        public static void RunCommandHidden(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(15000);
        }
    }
}
