using System.Collections.Generic;
using Archon.Models;
using Microsoft.Win32;

namespace Archon.Core
{
    public static class TweakCatalog
    {
        public static List<TweakItem> GetAllTweaks()
        {
            var list = new List<TweakItem>
            {
                // ==================== INSTALADOR DÍA 0 (WINGET) ====================
                new TweakItem
                {
                    Id = "install_vcredist",
                    Category = "Instalador Día 0",
                    Title = "Instalar Visual C++ Redistributables (2015-2022 x86/x64)",
                    Risk = TweakRisk.Seguro,
                    Description = "Librerías obligatorias para que los juegos y launchers no tiren errores de archivos .dll faltantes.",
                    TechnicalDetail = "winget install --id Microsoft.VCRedist.2015+.x64 -e --silent + x86",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("winget.exe", "install --id Microsoft.VCRedist.2015+.x64 -e --silent --accept-package-agreements --accept-source-agreements");
                        RegistryEngine.RunCommandHidden("winget.exe", "install --id Microsoft.VCRedist.2015+.x86 -e --silent --accept-package-agreements --accept-source-agreements");
                    }
                },
                new TweakItem
                {
                    Id = "install_directx",
                    Category = "Instalador Día 0",
                    Title = "Instalar DirectX End-User Runtime",
                    Risk = TweakRisk.Seguro,
                    Description = "Componentes clásicos de DirectX 9/10/11 necesarios para juegos competitivos y títulos anteriores.",
                    TechnicalDetail = "winget install --id Microsoft.DirectX -e --silent",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("winget.exe", "install --id Microsoft.DirectX -e --silent --accept-package-agreements --accept-source-agreements")
                },
                new TweakItem
                {
                    Id = "install_7zip",
                    Category = "Instalador Día 0",
                    Title = "Instalar 7-Zip (Descompresor liviano sin publicidad)",
                    Risk = TweakRisk.Seguro,
                    Description = "Herramienta gratuita y ultraliviana para abrir archivos .zip, .rar y .7z.",
                    TechnicalDetail = "winget install --id 7zip.7zip -e --silent",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("winget.exe", "install --id 7zip.7zip -e --silent --accept-package-agreements --accept-source-agreements")
                },
                new TweakItem
                {
                    Id = "install_steam",
                    Category = "Instalador Día 0",
                    Title = "Instalar Steam",
                    Risk = TweakRisk.Seguro,
                    Description = "Descarga e instala el cliente oficial de Steam en segundo plano.",
                    TechnicalDetail = "winget install --id Valve.Steam -e --silent",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("winget.exe", "install --id Valve.Steam -e --silent --accept-package-agreements --accept-source-agreements")
                },
                new TweakItem
                {
                    Id = "install_discord",
                    Category = "Instalador Día 0",
                    Title = "Instalar Discord",
                    Risk = TweakRisk.Seguro,
                    Description = "Descarga e instala la última versión oficial de Discord.",
                    TechnicalDetail = "winget install --id Discord.Discord -e --silent",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("winget.exe", "install --id Discord.Discord -e --silent --accept-package-agreements --accept-source-agreements")
                },

                // ==================== GAMING Y ENERGÍA ====================
                new TweakItem
                {
                    Id = "power_ultimate",
                    Category = "Gaming y Energía",
                    Title = "Plan de Energía: Máximo Rendimiento",
                    Risk = TweakRisk.Seguro,
                    Description = "Desbloquea y activa el plan oculto de Windows para evitar que el procesador baje frecuencias entre cuadros.",
                    TechnicalDetail = "Comando: powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 + powercfg /setactive",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("powercfg.exe", "-duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61");
                        RegistryEngine.RunCommandHidden("powercfg.exe", "/setactive SCHEME_MIN");
                    },
                    RevertAction = () => RegistryEngine.RunCommandHidden("powercfg.exe", "/setactive SCHEME_BALANCED")
                },
                new TweakItem
                {
                    Id = "disable_gamedvr",
                    Category = "Gaming y Energía",
                    Title = "Desactivar Game DVR (Grabación en segundo plano)",
                    Risk = TweakRisk.Recomendado,
                    Description = "Apaga la captura continua de video de Xbox Game Bar que consume GPU y genera micro-tirones (stuttering).",
                    TechnicalDetail = @"HKCU\System\GameConfigStore -> GameDVR_Enabled = 0 | HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR -> AllowGameDVR = 0",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 0),
                    ApplyAction = () =>
                    {
                        RegistryEngine.SetValueWithBackup("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 0, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0, RegistryValueKind.DWord);
                    },
                    RevertAction = () =>
                    {
                        RegistryEngine.SetValueWithBackup("HKCU", @"System\GameConfigStore", "GameDVR_Enabled", 1, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 1, RegistryValueKind.DWord);
                    }
                },
                new TweakItem
                {
                    Id = "disable_hibernation",
                    Category = "Gaming y Energía",
                    Title = "Desactivar Hibernación (Liberar espacio SSD)",
                    Risk = TweakRisk.Seguro,
                    Description = "Elimina el archivo oculto hiberfil.sys y recupera entre 6 GB y 16 GB de almacenamiento en el disco principal.",
                    TechnicalDetail = "Comando: powercfg.exe -h off",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("powercfg.exe", "-h off"),
                    RevertAction = () => RegistryEngine.RunCommandHidden("powercfg.exe", "-h on")
                },
                new TweakItem
                {
                    Id = "gpu_hags",
                    Category = "Gaming y Energía",
                    Title = "Activar Programación de GPU Acelerada por Hardware (HAGS)",
                    Risk = TweakRisk.Recomendado,
                    Description = "Permite que la placa de video gestione su propia memoria VRAM reduciendo la latencia de envío de cuadros desde la CPU.",
                    TechnicalDetail = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers -> HwSchMode = 2",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1, RegistryValueKind.DWord)
                },

                // ==================== RED Y DNS ====================
                new TweakItem
                {
                    Id = "net_throttling_off",
                    Category = "Red y DNS",
                    Title = "Desactivar Limitación de Tráfico de Red (Network Throttling)",
                    Risk = TweakRisk.Recomendado,
                    Description = "Elimina el límite artificial de 10 paquetes por milisegundo que Windows impone cuando detecta reproducción multimedia.",
                    TechnicalDetail = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile -> NetworkThrottlingIndex = 0xFFFFFFFF",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", -1),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", 10, RegistryValueKind.DWord)
                },
                new TweakItem
                {
                    Id = "dns_flush_cache",
                    Category = "Red y DNS",
                    Title = "Purgar Caché de Resolución DNS",
                    Risk = TweakRisk.Seguro,
                    Description = "Limpia entradas DNS antiguas o corruptas almacenadas en Windows.",
                    TechnicalDetail = "Comando: ipconfig.exe /flushdns",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("ipconfig.exe", "/flushdns")
                },

                // ==================== DEBLOAT ====================
                new TweakItem
                {
                    Id = "debloat_telemetry",
                    Category = "Debloat",
                    Title = "Reducir Telemetría y Diagnósticos de Microsoft",
                    Risk = TweakRisk.Seguro,
                    Description = "Minimiza el envío de datos de uso y diagnósticos en segundo plano hacia los servidores de Microsoft.",
                    TechnicalDetail = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection -> AllowTelemetry = 0",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0, RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 1, RegistryValueKind.DWord)
                },
                new TweakItem
                {
                    Id = "debloat_bing_search",
                    Category = "Debloat",
                    Title = "Desactivar Búsqueda Web de Bing en el Menú Inicio",
                    Risk = TweakRisk.Seguro,
                    Description = "Hace que el buscador de Windows busque únicamente archivos y programas locales al instante, sin consultar internet.",
                    TechnicalDetail = @"HKCU\SOFTWARE\Policies\Microsoft\Windows\Explorer -> DisableSearchBoxSuggestions = 1",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKCU", @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKCU", @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKCU", @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 0, RegistryValueKind.DWord)
                },

                // ==================== SERVICIOS ====================
                new TweakItem
                {
                    Id = "svc_sysmain",
                    Category = "Servicios",
                    Title = "Desactivar SysMain (Superfetch) para SSD / NVMe",
                    Risk = TweakRisk.Recomendado,
                    Description = "Evita lecturas constantes innecesarias en discos sólidos modernos donde la precarga en RAM ya no aporta velocidad.",
                    TechnicalDetail = "sc stop SysMain && sc config SysMain start= disabled",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "stop SysMain");
                        RegistryEngine.RunCommandHidden("sc.exe", "config SysMain start= disabled");
                    },
                    RevertAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "config SysMain start= auto");
                        RegistryEngine.RunCommandHidden("sc.exe", "start SysMain");
                    }
                },

                // ==================== LIMPIEZA ====================
                new TweakItem
                {
                    Id = "clean_temp_files",
                    Category = "Limpieza",
                    Title = "Limpiar Archivos Temporales de Usuario y Sistema",
                    Risk = TweakRisk.Seguro,
                    Description = "Borra archivos residuales acumulados en %TEMP% y C:\\Windows\\Temp.",
                    TechnicalDetail = "del /q /f /s %TEMP%\\* & del /q /f /s C:\\Windows\\Temp\\*",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("cmd.exe", "/c del /q /f /s %TEMP%\\* & del /q /f /s C:\\Windows\\Temp\\*");
                    }
                }
            };

            foreach (var item in list)
            {
                if (item.DetectCurrentState != null)
                {
                    item.IsEnabled = item.DetectCurrentState();
                }
            }

            return list;
        }
    }
}
