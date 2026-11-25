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

                // ==================== GAMING Y ENERGÍA ====================
                new TweakItem
                {
                    Id = "power_ultimate",
                    Category = "Gaming y Energía",
                    Title = "Plan de Energía: Máximo Rendimiento",
                    Risk = TweakRisk.Seguro,
                    Description = "Desbloquea y activa el plan oculto de Windows para evitar que el procesador baje frecuencias entre cuadros.",
                    TechnicalDetail = "powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 + powercfg /setactive",
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
                    TechnicalDetail = @"HKCU\System\GameConfigStore -> GameDVR_Enabled = 0",
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
                    Id = "enable_gamemode",
                    Category = "Gaming y Energía",
                    Title = "Activar Modo de Juego de Windows (Game Mode)",
                    Risk = TweakRisk.Seguro,
                    Description = "Prioriza el hilo principal del juego y pausa tareas de Windows Update mientras estás en pantalla completa.",
                    TechnicalDetail = @"HKCU\Software\Microsoft\GameBar -> AutoGameModeEnabled = 1",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKCU", @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 0, RegistryValueKind.DWord)
                },
                new TweakItem
                {
                    Id = "gpu_hags",
                    Category = "Gaming y Energía",
                    Title = "Programación de GPU Acelerada por Hardware (HAGS)",
                    Risk = TweakRisk.Recomendado,
                    Description = "Permite que la tarjeta gráfica gestione su propia memoria VRAM reduciendo la latencia de envío de cuadros.",
                    TechnicalDetail = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers -> HwSchMode = 2",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 1, RegistryValueKind.DWord)
                },
                new TweakItem
                {
                    Id = "mmcss_gaming_priority",
                    Category = "Gaming y Energía",
                    Title = "Prioridad Multimedia de CPU y GPU para Juegos",
                    Risk = TweakRisk.Recomendado,
                    Description = "Reserva el 100% de recursos multimedia para juegos en primer plano y eleva la prioridad de planificación de GPU.",
                    TechnicalDetail = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", 8),
                    ApplyAction = () =>
                    {
                        string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
                        RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 0, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKLM", path, "GPU Priority", 8, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKLM", path, "Priority", 6, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKLM", path, "Scheduling Category", "High", RegistryValueKind.String);
                    },
                    RevertAction = () =>
                    {
                        string path = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games";
                        RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 20, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKLM", path, "GPU Priority", 2, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKLM", path, "Priority", 2, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKLM", path, "Scheduling Category", "Medium", RegistryValueKind.String);
                    }
                },
                new TweakItem
                {
                    Id = "mouse_raw_1to1",
                    Category = "Gaming y Energía",
                    Title = "Desactivar Aceleración del Mouse (Curva 1:1)",
                    Risk = TweakRisk.Seguro,
                    Description = "Elimina la precisión mejorada del puntero de Windows para mantener memoria muscular exacta en shooters.",
                    TechnicalDetail = @"HKCU\Control Panel\Mouse -> MouseSpeed = 0",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKCU", @"Control Panel\Mouse", "MouseThreshold1", 0),
                    ApplyAction = () =>
                    {
                        RegistryEngine.SetValueWithBackup("HKCU", @"Control Panel\Mouse", "MouseSpeed", "0", RegistryValueKind.String);
                        RegistryEngine.SetValueWithBackup("HKCU", @"Control Panel\Mouse", "MouseThreshold1", 0, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", @"Control Panel\Mouse", "MouseThreshold2", 0, RegistryValueKind.DWord);
                    },
                    RevertAction = () =>
                    {
                        RegistryEngine.SetValueWithBackup("HKCU", @"Control Panel\Mouse", "MouseSpeed", "1", RegistryValueKind.String);
                        RegistryEngine.SetValueWithBackup("HKCU", @"Control Panel\Mouse", "MouseThreshold1", 6, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", @"Control Panel\Mouse", "MouseThreshold2", 10, RegistryValueKind.DWord);
                    }
                },
                new TweakItem
                {
                    Id = "pcie_link_state_off",
                    Category = "Gaming y Energía",
                    Title = "Desactivar Ahorro de Energía PCI Express (ASPM)",
                    Risk = TweakRisk.Seguro,
                    Description = "Evita que la tarjeta gráfica o el SSD NVMe entren en estados de bajo consumo durante partidas.",
                    TechnicalDetail = "powercfg /setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("powercfg.exe", "/setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 0");
                        RegistryEngine.RunCommandHidden("powercfg.exe", "/setactive SCHEME_CURRENT");
                    },
                    RevertAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("powercfg.exe", "/setacvalueindex SCHEME_CURRENT SUB_PCIEXPRESS ASPM 1");
                        RegistryEngine.RunCommandHidden("powercfg.exe", "/setactive SCHEME_CURRENT");
                    }
                },
                new TweakItem
                {
                    Id = "disable_core_parking",
                    Category = "Gaming y Energía",
                    Title = "Desactivar Estacionamiento de Núcleos (Core Parking)",
                    Risk = TweakRisk.Recomendado,
                    Description = "Mantiene el 100% de los núcleos del procesador despiertos para evitar caídas bruscas de FPS.",
                    TechnicalDetail = "powercfg -setacvalueindex scheme_current sub_processor CPMINCORES 100",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("powercfg.exe", "-setacvalueindex scheme_current sub_processor CPMINCORES 100");
                        RegistryEngine.RunCommandHidden("powercfg.exe", "/setactive SCHEME_CURRENT");
                    },
                    RevertAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("powercfg.exe", "-setacvalueindex scheme_current sub_processor CPMINCORES 5");
                        RegistryEngine.RunCommandHidden("powercfg.exe", "/setactive SCHEME_CURRENT");
                    }
                },
                new TweakItem
                {
                    Id = "disable_hibernation",
                    Category = "Gaming y Energía",
                    Title = "Desactivar Hibernación (Liberar espacio SSD)",
                    Risk = TweakRisk.Seguro,
                    Description = "Elimina el archivo oculto hiberfil.sys y recupera entre 6 GB y 16 GB en el disco principal.",
                    TechnicalDetail = "powercfg.exe -h off",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("powercfg.exe", "-h off"),
                    RevertAction = () => RegistryEngine.RunCommandHidden("powercfg.exe", "-h on")
                },

                // ==================== RED Y DNS ====================
                new TweakItem
                {
                    Id = "net_throttling_off",
                    Category = "Red y DNS",
                    Title = "Desactivar Limitación de Tráfico de Red (Network Throttling)",
                    Risk = TweakRisk.Recomendado,
                    Description = "Elimina el límite artificial de 10 paquetes por milisegundo que Windows impone al reproducir audio o video.",
                    TechnicalDetail = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile -> NetworkThrottlingIndex = 0xFFFFFFFF",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", -1),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "NetworkThrottlingIndex", 10, RegistryValueKind.DWord)
                },
                new TweakItem
                {
                    Id = "net_delivery_opt_off",
                    Category = "Red y DNS",
                    Title = "Bloquear P2P de Windows Update (Delivery Optimization)",
                    Risk = TweakRisk.Seguro,
                    Description = "Impide que Windows utilice tu ancho de banda de subida para enviar actualizaciones a otras PC de internet.",
                    TechnicalDetail = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization -> DODownloadMode = 0",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0, RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 1, RegistryValueKind.DWord)
                },
                new TweakItem
                {
                    Id = "net_tcp_low_latency",
                    Category = "Red y DNS",
                    Title = "Optimizar Pila TCP/IP (RSS y Auto-Tuning)",
                    Risk = TweakRisk.Seguro,
                    Description = "Habilita el procesamiento multinúcleo de paquetes de red (RSS) y ajusta la ventana TCP para menor latencia.",
                    TechnicalDetail = "netsh int tcp set global rss=enabled autotuninglevel=normal",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("netsh.exe", "int tcp set global rss=enabled");
                        RegistryEngine.RunCommandHidden("netsh.exe", "int tcp set global autotuninglevel=normal");
                    },
                    RevertAction = () => RegistryEngine.RunCommandHidden("netsh.exe", "int tcp set global autotuninglevel=normal")
                },
                new TweakItem
                {
                    Id = "net_nagle_disable",
                    Category = "Red y DNS",
                    Title = "Desactivar Algoritmo de Nagle en Interfaces Activas",
                    Risk = TweakRisk.Recomendado,
                    Description = "Envía paquetes TCP pequeños de inmediato sin esperar a agruparlos, reduciendo el ping en juegos online.",
                    TechnicalDetail = "TcpAckFrequency=1 & TCPNoDelay=1 en Interfaces TCPIP",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("powershell.exe", "-NoProfile -Command \"Get-ChildItem 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces' | ForEach-Object { New-ItemProperty -Path $_.PSPath -Name 'TcpAckFrequency' -Value 1 -PropertyType DWord -Force | Out-Null; New-ItemProperty -Path $_.PSPath -Name 'TCPNoDelay' -Value 1 -PropertyType DWord -Force | Out-Null }\""),
                    RevertAction = () => RegistryEngine.RunCommandHidden("powershell.exe", "-NoProfile -Command \"Get-ChildItem 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces' | ForEach-Object { Remove-ItemProperty -Path $_.PSPath -Name 'TcpAckFrequency' -ErrorAction SilentlyContinue; Remove-ItemProperty -Path $_.PSPath -Name 'TCPNoDelay' -ErrorAction SilentlyContinue }\"")
                },
                new TweakItem
                {
                    Id = "dns_cloudflare",
                    Category = "Red y DNS",
                    Title = "Aplicar DNS Rápido Cloudflare (1.1.1.1 / 1.0.0.1)",
                    Risk = TweakRisk.Seguro,
                    Description = "Configura los servidores DNS de menor latencia en los adaptadores de red conectados.",
                    TechnicalDetail = "Set-DnsClientServerAddress 1.1.1.1, 1.0.0.1",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("powershell.exe", "-NoProfile -Command \"Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ServerAddresses ('1.1.1.1','1.0.0.1')\""),
                    RevertAction = () => RegistryEngine.RunCommandHidden("powershell.exe", "-NoProfile -Command \"Get-NetAdapter | Where-Object Status -eq 'Up' | Set-DnsClientServerAddress -ResetServerAddresses\"")
                },
                new TweakItem
                {
                    Id = "dns_flush_cache",
                    Category = "Red y DNS",
                    Title = "Purgar Caché de Resolución DNS",
                    Risk = TweakRisk.Seguro,
                    Description = "Limpia entradas DNS antiguas o corruptas almacenadas en Windows.",
                    TechnicalDetail = "ipconfig.exe /flushdns",
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
                    Description = "Hace que el buscador de Windows busque únicamente archivos y programas locales al instante.",
                    TechnicalDetail = @"HKCU\SOFTWARE\Policies\Microsoft\Windows\Explorer -> DisableSearchBoxSuggestions = 1",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKCU", @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKCU", @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1, RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKCU", @"SOFTWARE\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 0, RegistryValueKind.DWord)
                },
                new TweakItem
                {
                    Id = "debloat_ads_suggestions",
                    Category = "Debloat",
                    Title = "Desactivar Anuncios y Apps Sugeridas en Windows",
                    Risk = TweakRisk.Seguro,
                    Description = "Evita que Windows instale apps promocionadas silenciosamente o muestre sugerencias en Configuración e Inicio.",
                    TechnicalDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKCU", @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0),
                    ApplyAction = () =>
                    {
                        string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                        RegistryEngine.SetValueWithBackup("HKCU", cdm, "SilentInstalledAppsEnabled", 0, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", cdm, "SubscribedContent-338388Enabled", 0, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", cdm, "SubscribedContent-338389Enabled", 0, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", cdm, "SystemPaneSuggestionsEnabled", 0, RegistryValueKind.DWord);
                    },
                    RevertAction = () =>
                    {
                        string cdm = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
                        RegistryEngine.SetValueWithBackup("HKCU", cdm, "SilentInstalledAppsEnabled", 1, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", cdm, "SubscribedContent-338388Enabled", 1, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", cdm, "SubscribedContent-338389Enabled", 1, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", cdm, "SystemPaneSuggestionsEnabled", 1, RegistryValueKind.DWord);
                    }
                },
                new TweakItem
                {
                    Id = "debloat_widgets_copilot",
                    Category = "Debloat",
                    Title = "Ocultar Widgets y Desactivar Copilot en Barra de Tareas",
                    Risk = TweakRisk.Seguro,
                    Description = "Libera memoria RAM usada por procesos WebView2 de noticias, clima y Copilot.",
                    TechnicalDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced -> TaskbarDa = 0",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0),
                    ApplyAction = () =>
                    {
                        RegistryEngine.SetValueWithBackup("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 0, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1, RegistryValueKind.DWord);
                    },
                    RevertAction = () =>
                    {
                        RegistryEngine.SetValueWithBackup("HKCU", @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarDa", 1, RegistryValueKind.DWord);
                        RegistryEngine.SetValueWithBackup("HKCU", @"Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 0, RegistryValueKind.DWord);
                    }
                },
                new TweakItem
                {
                    Id = "debloat_background_apps",
                    Category = "Debloat",
                    Title = "Bloquear Apps UWP en Segundo Plano",
                    Risk = TweakRisk.Recomendado,
                    Description = "Impide que aplicaciones preinstaladas de la Microsoft Store sigan consumiendo CPU y red cuando están cerradas.",
                    TechnicalDetail = @"HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications -> GlobalUserDisabled = 1",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKCU", @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKCU", @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1, RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKCU", @"Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 0, RegistryValueKind.DWord)
                },
                new TweakItem
                {
                    Id = "debloat_fast_startup",
                    Category = "Debloat",
                    Title = "Desactivar Inicio Rápido (Fast Startup)",
                    Risk = TweakRisk.Seguro,
                    Description = "Asegura un reinicio limpio del kernel y los drivers al apagar el equipo, evitando errores acumulados.",
                    TechnicalDetail = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power -> HiberbootEnabled = 0",
                    DetectCurrentState = () => RegistryEngine.CheckDwordEquals("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0),
                    ApplyAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0, RegistryValueKind.DWord),
                    RevertAction = () => RegistryEngine.SetValueWithBackup("HKLM", @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1, RegistryValueKind.DWord)
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
                new TweakItem
                {
                    Id = "svc_diagtrack",
                    Category = "Servicios",
                    Title = "Desactivar Servicio de Telemetría (DiagTrack)",
                    Risk = TweakRisk.Recomendado,
                    Description = "Detiene el servicio de experiencias del usuario conectado y telemetría en segundo plano.",
                    TechnicalDetail = "sc stop DiagTrack && sc config DiagTrack start= disabled",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "stop DiagTrack");
                        RegistryEngine.RunCommandHidden("sc.exe", "config DiagTrack start= disabled");
                    },
                    RevertAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "config DiagTrack start= auto");
                        RegistryEngine.RunCommandHidden("sc.exe", "start DiagTrack");
                    }
                },
                new TweakItem
                {
                    Id = "svc_wsearch_manual",
                    Category = "Servicios",
                    Title = "Pasar Windows Search (Indexación) a Manual",
                    Risk = TweakRisk.Seguro,
                    Description = "Evita picos de uso de disco y CPU causados por el indexador automático de archivos.",
                    TechnicalDetail = "sc stop WSearch && sc config WSearch start= demand",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "stop WSearch");
                        RegistryEngine.RunCommandHidden("sc.exe", "config WSearch start= demand");
                    },
                    RevertAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "config WSearch start= delayed-auto");
                        RegistryEngine.RunCommandHidden("sc.exe", "start WSearch");
                    }
                },
                new TweakItem
                {
                    Id = "svc_wer",
                    Category = "Servicios",
                    Title = "Desactivar Informe de Errores de Windows (WerSvc)",
                    Risk = TweakRisk.Seguro,
                    Description = "Evita que Windows recolecte y envíe volcados de memoria cada vez que un programa se cierra.",
                    TechnicalDetail = "sc stop WerSvc && sc config WerSvc start= disabled",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "stop WerSvc");
                        RegistryEngine.RunCommandHidden("sc.exe", "config WerSvc start= disabled");
                    },
                    RevertAction = () => RegistryEngine.RunCommandHidden("sc.exe", "config WerSvc start= demand")
                },
                new TweakItem
                {
                    Id = "svc_mapsbroker",
                    Category = "Servicios",
                    Title = "Desactivar Administrador de Mapas Descargados",
                    Risk = TweakRisk.Seguro,
                    Description = "Detiene el servicio de sincronización de mapas sin conexión innecesario en PC de escritorio.",
                    TechnicalDetail = "sc stop MapsBroker && sc config MapsBroker start= disabled",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "stop MapsBroker");
                        RegistryEngine.RunCommandHidden("sc.exe", "config MapsBroker start= disabled");
                    },
                    RevertAction = () => RegistryEngine.RunCommandHidden("sc.exe", "config MapsBroker start= delayed-auto")
                },
                new TweakItem
                {
                    Id = "svc_spooler",
                    Category = "Servicios",
                    Title = "Desactivar Cola de Impresión (Print Spooler)",
                    Risk = TweakRisk.Avanzado,
                    Description = "Recomendado solo si no usás impresoras físicas ni impresora PDF de Microsoft.",
                    TechnicalDetail = "sc stop Spooler && sc config Spooler start= disabled",
                    ApplyAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "stop Spooler");
                        RegistryEngine.RunCommandHidden("sc.exe", "config Spooler start= disabled");
                    },
                    RevertAction = () =>
                    {
                        RegistryEngine.RunCommandHidden("sc.exe", "config Spooler start= auto");
                        RegistryEngine.RunCommandHidden("sc.exe", "start Spooler");
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
                    ApplyAction = () => RegistryEngine.RunCommandHidden("cmd.exe", "/c del /q /f /s %TEMP%\\* & del /q /f /s C:\\Windows\\Temp\\*")
                },
                new TweakItem
                {
                    Id = "clean_wu_cache",
                    Category = "Limpieza",
                    Title = "Limpiar Caché de Descargas de Windows Update",
                    Risk = TweakRisk.Seguro,
                    Description = "Elimina instaladores viejos acumulados en SoftwareDistribution\\Download.",
                    TechnicalDetail = "net stop wuauserv & del /q /f /s C:\\Windows\\SoftwareDistribution\\Download\\* & net start wuauserv",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("cmd.exe", "/c net stop wuauserv & del /q /f /s C:\\Windows\\SoftwareDistribution\\Download\\* & net start wuauserv")
                },
                new TweakItem
                {
                    Id = "clean_shader_thumbs",
                    Category = "Limpieza",
                    Title = "Limpiar Caché de Miniaturas y DirectX Shader Cache",
                    Risk = TweakRisk.Seguro,
                    Description = "Elimina miniaturas corruptas del Explorador y shaders antiguos que pueden causar tirones tras actualizar drivers.",
                    TechnicalDetail = "Limpieza de Explorer\\thumbcache_*.db y D3DSCache",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("cmd.exe", "/c del /f /s /q \"%LocalAppData%\\Microsoft\\Windows\\Explorer\\thumbcache_*.db\" & rmdir /s /q \"%LocalAppData%\\D3DSCache\"")
                },
                new TweakItem
                {
                    Id = "clean_recycle_bin",
                    Category = "Limpieza",
                    Title = "Vaciar Papelera de Reciclaje de Todas las Unidades",
                    Risk = TweakRisk.Seguro,
                    Description = "Libera definitivamente el espacio ocupado por archivos eliminados en todos los discos.",
                    TechnicalDetail = "Clear-RecycleBin -Force",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("powershell.exe", "-NoProfile -Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"")
                },
                new TweakItem
                {
                    Id = "clean_winsxs",
                    Category = "Limpieza",
                    Title = "Limpiar Componentes Antiguos de Windows (WinSxS)",
                    Risk = TweakRisk.Recomendado,
                    Description = "Compacta y elimina versiones reemplazadas de componentes del sistema operativo mediante DISM.",
                    TechnicalDetail = "Dism.exe /Online /Cleanup-Image /StartComponentCleanup",
                    ApplyAction = () => RegistryEngine.RunCommandHidden("Dism.exe", "/Online /Cleanup-Image /StartComponentCleanup")
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
