using System;
using System.Diagnostics;
using System.Management;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace OptimizadorFrankenn.Core
{
    public static class RestorePointManager
    {
        private const string SystemRestoreRegPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

        public static async Task<bool> CreateMandatoryRestorePointAsync(string description = "OptimizadorFrankenn - Punto Pre-Optimizacion")
        {
            return await Task.Run(() =>
            {
                try
                {
                    SetFrequencyLimit(0);
                    EnableSystemProtection("C:\\");
                    CapShadowStorage("C:", "5%");

                    var scope = new ManagementScope(@"\\localhost\root\default");
                    var path = new ManagementPath("SystemRestore");
                    using var restoreClass = new ManagementClass(scope, path, new ObjectGetOptions());
                    using var inParams = restoreClass.GetMethodParameters("CreateRestorePoint");

                    inParams["Description"] = description;
                    inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
                    inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE

                    using var outParams = restoreClass.InvokeMethod("CreateRestorePoint", inParams, null);
                    uint returnValue = Convert.ToUInt32(outParams["ReturnValue"]);

                    return returnValue == 0;
                }
                catch
                {
                    return false;
                }
            });
        }

        private static void SetFrequencyLimit(int minutes)
        {
            using var key = Registry.LocalMachine.CreateSubKey(SystemRestoreRegPath);
            key.SetValue("SystemRestorePointCreationFrequency", minutes, RegistryValueKind.DWord);
        }

        private static void EnableSystemProtection(string drive)
        {
            var scope = new ManagementScope(@"\\localhost\root\default");
            var path = new ManagementPath("SystemRestore");
            using var restoreClass = new ManagementClass(scope, path, new ObjectGetOptions());
            using var inParams = restoreClass.GetMethodParameters("Enable");
            inParams["Drive"] = drive;
            restoreClass.InvokeMethod("Enable", inParams, null);
        }

        private static void CapShadowStorage(string drive, string maxSize)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "vssadmin.exe",
                Arguments = $"resize shadowstorage /for={drive} /on={drive} /maxsize={maxSize}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);
        }
    }
}
