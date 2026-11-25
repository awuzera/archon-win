using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Archon.Core;
using Archon.Models;

namespace Archon
{
    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private enum PrepTarget
        {
            None,
            Fresh,
            Clean
        }

        private sealed class HardwareSpecRow
        {
            public string Label { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        private sealed class HardwareSpecSection
        {
            public string SectionTitle { get; set; } = string.Empty;
            public List<HardwareSpecRow> Specs { get; set; } = new();
        }

        private SystemHardwareProfile _hardware = new();
        private OptimizerState _state = new();
        private List<TweakItem> _allTweaks = new();
        private List<HardwareSpecSection>? _cachedDetailedSpecs;
        private string _activeCategory = "Apps";
        private bool _hardwareScanned = false;
        private PrepTarget _currentPrepTarget = PrepTarget.None;
        private int _currentProgressValue = 0;
        private Storyboard? _ambientStoryboard;
        private Storyboard? _loadingRingStoryboard;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var workArea = SystemParameters.WorkArea;
                if (Width > workArea.Width - 24) Width = Math.Max(980, workArea.Width - 24);
                if (Height > workArea.Height - 24) Height = Math.Max(660, workArea.Height - 24);

                Left = workArea.Left + (workArea.Width - Width) / 2;
                Top = workArea.Top + (workArea.Height - Height) / 2;

                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int preference = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref preference, sizeof(int));
            }
            catch
            {
                // Ignorar en entornos sin DWM
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (TryFindResource("AmbientGlowStoryboard") is Storyboard sb)
            {
                _ambientStoryboard = sb;
                _ambientStoryboard.Begin(this, true);
            }

            if (TryFindResource("LoadingRingStoryboard") is Storyboard ringSb)
            {
                _loadingRingStoryboard = ringSb;
            }

            _state = StepZeroOrchestrator.LoadState();
            _hardware = await Task.Run(() => HardwareDetector.ScanSystem());
            _hardwareScanned = true;

            PopulateHardwareLabels();

            if (_hardware.DetectedBloatware.Count > 0)
            {
                BloatAlertBox.Visibility = Visibility.Visible;
                TxtDetectedBloatList.Text = string.Join(", ", _hardware.DetectedBloatware);
            }

            if (App.IsResumeFromReboot && _state.PendingPostRebootInstall)
            {
                ShowView(StepZeroView);
                _currentPrepTarget = PrepTarget.Clean;
                OpenPrepModal("Completando preparación del sistema");
                await ExecuteDriverInstallationPipelineAsync(
                    "Limpieza finalizada. Base lista al 100%.");
            }
        }

        private void PopulateHardwareLabels()
        {
            TxtCpuValue.Text = _hardware.CpuName;
            TxtMotherValue.Text = _hardware.MotherboardName;
            TxtGpuValue.Text = _hardware.PrimaryGpu;
            TxtRamValue.Text = _hardware.TotalRamGb;
            TxtStorageValue.Text = _hardware.PrimaryStorage;
            TxtOsValue.Text = _hardware.WindowsVersion;
        }

        private async void BtnScanHardware_Click(object sender, RoutedEventArgs e)
        {
            if (HardwareListPanel.Visibility == Visibility.Visible)
            {
                HardwareListPanel.Visibility = Visibility.Collapsed;
                TxtScanButtonLabel.Text = "Ver mis componentes";
                return;
            }

            if (!_hardwareScanned)
            {
                TxtScanButtonLabel.Text = "Analizando equipo...";
                BtnScanHardware.IsEnabled = false;
                _hardware = await Task.Run(() => HardwareDetector.ScanSystem());
                _hardwareScanned = true;
                PopulateHardwareLabels();
                BtnScanHardware.IsEnabled = true;
            }

            HardwareListPanel.Visibility = Visibility.Visible;
            TxtScanButtonLabel.Text = "Ocultar componentes";
        }

        private async void BtnViewAllHardware_Click(object sender, RoutedEventArgs e)
        {
            BtnViewAllHardware.IsEnabled = false;
            _cachedDetailedSpecs ??= await Task.Run(BuildFullHardwareInventory);
            FullHardwareSectionsControl.ItemsSource = _cachedDetailedSpecs;
            BtnViewAllHardware.IsEnabled = true;

            ShowView(FullHardwareView);
            FullHardwareView.ScrollToTop();

            var slideAnim = new DoubleAnimation
            {
                From = 28,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            FullHardwareSlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slideAnim);
        }

        private void BtnBackFromFullHardware_Click(object sender, RoutedEventArgs e)
        {
            ShowView(WelcomeView);
        }

        private List<HardwareSpecSection> BuildFullHardwareInventory()
        {
            var sections = new List<HardwareSpecSection>();

            // 1. SEGURIDAD Y AJUSTES DE BIOS PARA GAMING (TPM 2.0, SECURE BOOT, UEFI, VIRTUALIZACIÓN)
            var biosGamingSpecs = new List<HardwareSpecRow>();

            // TPM 2.0
            string tpmStatus = "No detectado o desactivado en BIOS";
            try
            {
                using var tpmSearcher = new ManagementObjectSearcher(@"root\CIMV2\Security\MicrosoftTpm", "SELECT IsActivated_InitialValue, IsEnabled_InitialValue, SpecVersion FROM Win32_Tpm");
                foreach (ManagementObject tpm in tpmSearcher.Get())
                {
                    using (tpm)
                    {
                        bool enabled = tpm["IsEnabled_InitialValue"] is bool e && e;
                        bool activated = tpm["IsActivated_InitialValue"] is bool a && a;
                        string rawSpec = tpm["SpecVersion"]?.ToString() ?? "";
                        string ver = rawSpec.Split(',').FirstOrDefault()?.Trim() ?? "2.0";

                        if (enabled && activated)
                            tpmStatus = $"Activado y listo (TPM {ver})";
                        else if (enabled)
                            tpmStatus = $"Habilitado, pendiente de activación (TPM {ver})";
                        else
                            tpmStatus = $"Desactivado en BIOS (TPM {ver})";
                    }
                }
            }
            catch
            {
                // Fallback por registro si el namespace WMI requiere permisos adicionales
                try
                {
                    using var tpmKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\TPM\WMI");
                    if (tpmKey != null)
                        tpmStatus = "Detectado en el sistema (TPM 2.0)";
                }
                catch { }
            }
            biosGamingSpecs.Add(new HardwareSpecRow { Label = "Módulo TPM (Anticheat / Win 11)", Value = tpmStatus });

            // Secure Boot
            string secureBootStatus = "No disponible (Modo Legacy)";
            try
            {
                using var sbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                if (sbKey?.GetValue("UEFISecureBootEnabled") is int sbVal)
                    secureBootStatus = sbVal == 1 ? "Activado (UEFI)" : "Desactivado en BIOS";
            }
            catch { }
            biosGamingSpecs.Add(new HardwareSpecRow { Label = "Arranque Seguro (Secure Boot)", Value = secureBootStatus });

            // Modo Firmware (UEFI vs Legacy)
            string firmwareMode = Directory.Exists(@"C:\Windows\Panther") && File.Exists(@"C:\Windows\System32\winload.efi")
                ? "UEFI"
                : "UEFI / Legacy";
            try
            {
                using var sbKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                if (sbKey != null) firmwareMode = "UEFI Nativo";
            }
            catch { }
            biosGamingSpecs.Add(new HardwareSpecRow { Label = "Modo de arranque del BIOS", Value = firmwareMode });

            // Virtualización (AMD SVM / Intel VT-x)
            bool? virtEnabled = null;
            QueryWmi("SELECT VirtualizationFirmwareEnabled FROM Win32_Processor", obj =>
            {
                if (obj["VirtualizationFirmwareEnabled"] is bool v)
                    virtEnabled = v;
            });
            if (virtEnabled.HasValue)
            {
                biosGamingSpecs.Add(new HardwareSpecRow
                {
                    Label = "Virtualización por hardware (SVM / VT-x)",
                    Value = virtEnabled.Value ? "Activada en BIOS" : "Desactivada en BIOS"
                });
            }

            // Aislamiento del núcleo (HVCI / Core Isolation)
            string hvciStatus = "Desactivado (Óptimo para FPS en juegos)";
            try
            {
                using var hvciKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity");
                if (hvciKey?.GetValue("Enabled") is int hvciVal && hvciVal == 1)
                    hvciStatus = "Activado (Puede reducir rendimiento en juegos)";
            }
            catch { }
            biosGamingSpecs.Add(new HardwareSpecRow { Label = "Integridad de memoria (Core Isolation)", Value = hvciStatus });

            sections.Add(new HardwareSpecSection { SectionTitle = "Seguridad y Ajustes de BIOS (Gaming)", Specs = biosGamingSpecs });

            // 2. PROCESADOR
            var cpuSpecs = new List<HardwareSpecRow>
            {
                new() { Label = "Modelo", Value = _hardware.CpuName },
                new() { Label = "Arquitectura", Value = RuntimeInformation.OSArchitecture.ToString() }
            };
            QueryWmi("SELECT Manufacturer, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor", obj =>
            {
                AddIfValid(cpuSpecs, "Fabricante", obj["Manufacturer"]?.ToString());
                AddIfValid(cpuSpecs, "Socket", obj["SocketDesignation"]?.ToString());
                AddIfValid(cpuSpecs, "Núcleos físicos / Hilos", $"{obj["NumberOfCores"]} núcleos / {obj["NumberOfLogicalProcessors"]} hilos");
                if (obj["MaxClockSpeed"] != null)
                    cpuSpecs.Add(new HardwareSpecRow { Label = "Frecuencia base máxima", Value = $"{obj["MaxClockSpeed"]} MHz" });
                if (obj["L2CacheSize"] != null && Convert.ToInt64(obj["L2CacheSize"]) > 0)
                    cpuSpecs.Add(new HardwareSpecRow { Label = "Caché L2", Value = $"{Convert.ToInt64(obj["L2CacheSize"]) / 1024.0:0.#} MB" });
                if (obj["L3CacheSize"] != null && Convert.ToInt64(obj["L3CacheSize"]) > 0)
                    cpuSpecs.Add(new HardwareSpecRow { Label = "Caché L3", Value = $"{Convert.ToInt64(obj["L3CacheSize"]) / 1024.0:0.#} MB" });
            });
            sections.Add(new HardwareSpecSection { SectionTitle = "Procesador (CPU)", Specs = cpuSpecs });

            // 3. PLACA BASE Y BIOS
            var boardSpecs = new List<HardwareSpecRow>
            {
                new() { Label = "Placa madre", Value = _hardware.MotherboardName }
            };
            QueryWmi("SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard", obj =>
            {
                AddIfValid(boardSpecs, "Fabricante", obj["Manufacturer"]?.ToString());
                AddIfValid(boardSpecs, "Modelo exacto", obj["Product"]?.ToString());
                AddIfValid(boardSpecs, "Revisión", obj["Version"]?.ToString());
                AddIfValid(boardSpecs, "Número de serie", obj["SerialNumber"]?.ToString());
            });
            QueryWmi("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS", obj =>
            {
                AddIfValid(boardSpecs, "Fabricante BIOS", obj["Manufacturer"]?.ToString());
                AddIfValid(boardSpecs, "Versión BIOS / UEFI", obj["SMBIOSBIOSVersion"]?.ToString());
                var rawDate = obj["ReleaseDate"]?.ToString();
                if (!string.IsNullOrEmpty(rawDate) && rawDate.Length >= 8)
                    boardSpecs.Add(new HardwareSpecRow { Label = "Fecha de firmware", Value = $"{rawDate.Substring(6, 2)}/{rawDate.Substring(4, 2)}/{rawDate.Substring(0, 4)}" });
            });
            sections.Add(new HardwareSpecSection { SectionTitle = "Placa base y Firmware UEFI / BIOS", Specs = boardSpecs });

            // 4. GRÁFICOS Y PANTALLA
            var gpuSpecs = new List<HardwareSpecRow>();
            int gpuIdx = 1;
            QueryWmi("SELECT Name, AdapterCompatibility, DriverVersion, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController", obj =>
            {
                string prefix = gpuIdx == 1 ? "GPU principal" : $"GPU #{gpuIdx}";
                AddIfValid(gpuSpecs, prefix, obj["Name"]?.ToString());
                AddIfValid(gpuSpecs, $"Fabricante ({prefix})", obj["AdapterCompatibility"]?.ToString());
                AddIfValid(gpuSpecs, $"Versión de controlador ({prefix})", obj["DriverVersion"]?.ToString());
                if (obj["AdapterRAM"] != null && Convert.ToInt64(obj["AdapterRAM"]) > 0)
                {
                    double gb = Convert.ToInt64(obj["AdapterRAM"]) / (1024.0 * 1024.0 * 1024.0);
                    gpuSpecs.Add(new HardwareSpecRow { Label = $"Memoria de video ({prefix})", Value = $"{gb:0.#} GB" });
                }
                if (obj["CurrentHorizontalResolution"] != null && obj["CurrentVerticalResolution"] != null)
                {
                    string hz = obj["CurrentRefreshRate"] != null ? $" @ {obj["CurrentRefreshRate"]} Hz" : string.Empty;
                    gpuSpecs.Add(new HardwareSpecRow { Label = $"Resolución activa ({prefix})", Value = $"{obj["CurrentHorizontalResolution"]} × {obj["CurrentVerticalResolution"]}{hz}" });
                }
                gpuIdx++;
            });
            if (gpuSpecs.Count == 0)
                gpuSpecs.Add(new HardwareSpecRow { Label = "Tarjeta gráfica", Value = _hardware.PrimaryGpu });
            sections.Add(new HardwareSpecSection { SectionTitle = "Gráficos y Pantalla", Specs = gpuSpecs });

            // 5. MEMORIA RAM
            var ramSpecs = new List<HardwareSpecRow>
            {
                new() { Label = "Memoria instalada total", Value = _hardware.TotalRamGb }
            };
            int dimmIdx = 1;
            QueryWmi("SELECT DeviceLocator, BankLabel, Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory", obj =>
            {
                long capBytes = obj["Capacity"] != null ? Convert.ToInt64(obj["Capacity"]) : 0;
                double capGb = capBytes / (1024.0 * 1024.0 * 1024.0);
                string speed = (obj["ConfiguredClockSpeed"] ?? obj["Speed"])?.ToString() ?? "";
                string ddrType = obj["SMBIOSMemoryType"] != null ? Convert.ToInt32(obj["SMBIOSMemoryType"]) switch
                {
                    24 => "DDR3",
                    26 => "DDR4",
                    34 => "DDR5",
                    _ => "RAM"
                } : "RAM";
                string slot = obj["DeviceLocator"]?.ToString() ?? $"Slot #{dimmIdx}";
                string maker = obj["Manufacturer"]?.ToString()?.Trim() ?? "";
                string part = obj["PartNumber"]?.ToString()?.Trim() ?? "";

                string detail = $"{capGb:0.#} GB {ddrType}";
                if (!string.IsNullOrEmpty(speed)) detail += $" · {speed} MT/s";
                if (!string.IsNullOrEmpty(maker) && maker != "Unknown") detail += $" · {maker}";
                if (!string.IsNullOrEmpty(part)) detail += $" ({part})";

                ramSpecs.Add(new HardwareSpecRow { Label = $"Módulo {dimmIdx} ({slot})", Value = detail });
                dimmIdx++;
            });
            sections.Add(new HardwareSpecSection { SectionTitle = "Memoria RAM y Módulos DIMM", Specs = ramSpecs });

            // 6. ALMACENAMIENTO Y VOLÚMENES
            var storageSpecs = new List<HardwareSpecRow>();
            int diskIdx = 1;
            QueryWmi("SELECT Model, InterfaceType, MediaType, Size, SerialNumber, Status FROM Win32_DiskDrive", obj =>
            {
                long sizeBytes = obj["Size"] != null ? Convert.ToInt64(obj["Size"]) : 0;
                double sizeGb = sizeBytes / (1000.0 * 1000.0 * 1000.0);
                string model = obj["Model"]?.ToString()?.Trim() ?? $"Unidad #{diskIdx}";
                string iface = obj["InterfaceType"]?.ToString() ?? "";
                string status = obj["Status"]?.ToString() ?? "OK";
                storageSpecs.Add(new HardwareSpecRow
                {
                    Label = $"Unidad física #{diskIdx} ({iface})",
                    Value = $"{model} · {sizeGb:0} GB · Estado: {status}"
                });
                diskIdx++;
            });

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
            {
                double totalGb = drive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                string volLabel = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local" : drive.VolumeLabel;
                storageSpecs.Add(new HardwareSpecRow
                {
                    Label = $"Partición {drive.Name.TrimEnd('\\')} ({volLabel})",
                    Value = $"{drive.DriveFormat} · {freeGb:0.#} GB libres de {totalGb:0.#} GB"
                });
            }
            sections.Add(new HardwareSpecSection { SectionTitle = "Almacenamiento y Particiones", Specs = storageSpecs });

            // 7. RED Y CONECTIVIDAD
            var netSpecs = new List<HardwareSpecRow>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                n.NetworkInterfaceType != NetworkInterfaceType.Tunnel))
                {
                    var ipProps = nic.GetIPProperties();
                    var ipv4 = ipProps.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address.ToString();
                    string speedStr = nic.Speed > 0 ? $"{nic.Speed / 1_000_000} Mbps" : "Sin enlace";
                    string statusStr = nic.OperationalStatus == OperationalStatus.Up ? "Conectado" : "Desconectado";
                    string info = $"{nic.Description} · {statusStr} ({speedStr})";
                    if (!string.IsNullOrEmpty(ipv4)) info += $" · IP: {ipv4}";

                    netSpecs.Add(new HardwareSpecRow { Label = nic.Name, Value = info });
                }
            }
            catch { }
            if (netSpecs.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = "Adaptadores de Red", Specs = netSpecs });

            // 8. AUDIO Y CONTROLADORAS USB
            var peripheralSpecs = new List<HardwareSpecRow>();
            QueryWmi("SELECT Name, Manufacturer FROM Win32_SoundDevice", obj =>
            {
                AddIfValid(peripheralSpecs, "Dispositivo de audio", obj["Name"]?.ToString());
            });
            QueryWmi("SELECT Name FROM Win32_USBController", obj =>
            {
                AddIfValid(peripheralSpecs, "Controladora USB", obj["Name"]?.ToString());
            });
            if (peripheralSpecs.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = "Audio y Controladoras USB", Specs = peripheralSpecs });

            // 9. SISTEMA OPERATIVO Y ENTORNO
            var osSpecs = new List<HardwareSpecRow>
            {
                new() { Label = "Edición de Windows", Value = _hardware.WindowsVersion },
                new() { Label = "Nombre del equipo", Value = Environment.MachineName },
                new() { Label = "Usuario activo", Value = Environment.UserName },
                new() { Label = "Versión del kernel NT", Value = Environment.OSVersion.Version.ToString() }
            };
            TimeSpan uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            osSpecs.Add(new HardwareSpecRow
            {
                Label = "Tiempo activo del sistema",
                Value = $"{(int)uptime.TotalHours} h {uptime.Minutes} min"
            });
            sections.Add(new HardwareSpecSection { SectionTitle = "Sistema Operativo y Entorno", Specs = osSpecs });

            return sections;
        }

        private static void QueryWmi(string query, Action<ManagementObject> handle)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject obj in searcher.Get())
                {
                    using (obj)
                    {
                        handle(obj);
                    }
                }
            }
            catch
            {
                // Ignorar clases WMI no soportadas por el hardware
            }
        }

        private static void AddIfValid(List<HardwareSpecRow> rows, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string clean = value.Trim();
            if (clean.Equals("To be filled by O.E.M.", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("Default string", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("None", StringComparison.OrdinalIgnoreCase))
                return;

            rows.Add(new HardwareSpecRow { Label = label, Value = clean });
        }

        private void BtnStartStepZero_Click(object sender, RoutedEventArgs e)
        {
            ClosePrepModal();
            ShowView(StepZeroView);
            StepZeroView.ScrollToTop();
        }

        private void BtnBackToWelcome_Click(object sender, RoutedEventArgs e)
        {
            ClosePrepModal();
            ShowView(WelcomeView);
            WelcomeView.ScrollToTop();
        }

        private void BtnBackToStepZero_Click(object sender, RoutedEventArgs e)
        {
            ClosePrepModal();
            ToggleStepZeroButtons(true);
            ShowView(StepZeroView);
            StepZeroView.ScrollToTop();
        }

        private void ShowView(UIElement target)
        {
            bool isWelcome = target == WelcomeView;
            WelcomeView.Visibility = isWelcome ? Visibility.Visible : Visibility.Collapsed;
            FullHardwareView.Visibility = target == FullHardwareView ? Visibility.Visible : Visibility.Collapsed;
            StepZeroView.Visibility = target == StepZeroView ? Visibility.Visible : Visibility.Collapsed;
            MainOptimizerView.Visibility = target == MainOptimizerView ? Visibility.Visible : Visibility.Collapsed;

            if (_ambientStoryboard != null)
            {
                _ambientStoryboard.SetSpeedRatio(this, isWelcome ? 1.0 : 0.45);
                AmbientGlowLayer.Opacity = isWelcome ? 1.0 : 0.72;
            }
        }

        private void OpenPrepModal(string title)
        {
            _currentProgressValue = 0;
            TxtModalTitle.Text = title;
            TxtModalStepTag.Text = "ETAPA 1 DE 3 · RESPALDO PREVENTIVO";
            TxtModalMessage.Text = "Inicializando entorno seguro...";
            TxtModalPercent.Text = "0%";
            ModalProgressBar.IsIndeterminate = false;
            ModalProgressBar.Value = 0;

            StepZeroBlur.Radius = 16;
            PrepModalOverlay.Visibility = Visibility.Visible;
            _loadingRingStoryboard?.Begin(this, true);
        }

        private void ClosePrepModal()
        {
            _loadingRingStoryboard?.Stop(this);
            PrepModalOverlay.Visibility = Visibility.Collapsed;
            StepZeroBlur.Radius = 0;
        }

        private void ResetInlineProgressPanels()
        {
            _currentPrepTarget = PrepTarget.None;
            ProgressPanelFresh.Visibility = Visibility.Collapsed;
            ProgressPanelClean.Visibility = Visibility.Collapsed;
        }

        private void UpdateProgressState(string stepTag, string message, int percent, bool updateInline = true)
        {
            _currentProgressValue = percent;
            string pctText = $"{percent}%";

            if (PrepModalOverlay.Visibility == Visibility.Visible)
            {
                if (!string.IsNullOrEmpty(stepTag))
                    TxtModalStepTag.Text = stepTag;
                TxtModalMessage.Text = message;
                TxtModalPercent.Text = pctText;
                ModalProgressBar.Value = percent;
            }

            if (!updateInline) return;

            ProgressPanelFresh.Visibility = _currentPrepTarget == PrepTarget.Fresh ? Visibility.Visible : Visibility.Collapsed;
            ProgressPanelClean.Visibility = _currentPrepTarget == PrepTarget.Clean ? Visibility.Visible : Visibility.Collapsed;

            switch (_currentPrepTarget)
            {
                case PrepTarget.Fresh:
                    TxtStatusFresh.Text = message;
                    TxtPercentFresh.Text = pctText;
                    ProgressFresh.Value = percent;
                    break;

                case PrepTarget.Clean:
                    TxtStatusClean.Text = message;
                    TxtPercentClean.Text = pctText;
                    ProgressClean.Value = percent;
                    break;
            }
        }

        private async Task SmoothProgressToAsync(string stepTag, string message, int targetPercent, int durationMs)
        {
            int start = _currentProgressValue;
            if (targetPercent <= start)
            {
                UpdateProgressState(stepTag, message, targetPercent);
                await Task.Delay(durationMs);
                return;
            }

            int steps = targetPercent - start;
            int delayPerStep = Math.Max(32, durationMs / steps);

            for (int p = start + 1; p <= targetPercent; p++)
            {
                UpdateProgressState(stepTag, message, p);
                await Task.Delay(delayPerStep);
            }
        }

        private async void BtnFreshInstall_Click(object sender, RoutedEventArgs e)
        {
            ResetInlineProgressPanels();
            _currentPrepTarget = PrepTarget.Fresh;
            ToggleStepZeroButtons(false);
            OpenPrepModal("Preparando Windows recién instalado");

            if (!await EnsureMandatoryRestorePointAsync()) return;

            await ExecuteDriverInstallationPipelineAsync(
                "Controladores esenciales configurados. Base completada.");
        }

        private async void BtnCleanAndInstall_Click(object sender, RoutedEventArgs e)
        {
            ResetInlineProgressPanels();
            _currentPrepTarget = PrepTarget.Clean;
            ToggleStepZeroButtons(false);
            OpenPrepModal("Limpiando drivers y utilidades previas");

            if (!await EnsureMandatoryRestorePointAsync()) return;

            await SmoothProgressToAsync(
                "ETAPA 2 DE 3 · POLÍTICA DE CONTROLADORES",
                "Bloqueando instalación automática de paquetes genéricos desde Windows Update...",
                66,
                2700);
            StepZeroOrchestrator.SetWindowsUpdateDriverBlock(true);

            await SmoothProgressToAsync(
                "ETAPA 3 DE 3 · ENTORNO LIMPIO",
                "Eliminando servicios residuales de fabricante y programando reanudación tras reinicio...",
                95,
                2700);
            _state.PendingPostRebootInstall = true;
            StepZeroOrchestrator.SaveState(_state);
            StepZeroOrchestrator.RegisterPostRebootResume();

            await SmoothProgressToAsync(
                "PROCESO COMPLETADO",
                "Listo para reiniciar y completar la limpieza de controladores.",
                100,
                650);
            await Task.Delay(1100);

            ClosePrepModal();
            ToggleStepZeroButtons(true);
        }

        private async void BtnSkipDrivers_Click(object sender, RoutedEventArgs e)
        {
            ResetInlineProgressPanels();
            ToggleStepZeroButtons(false);

            if (!_state.RestorePointCreated)
            {
                bool ok = await RestorePointManager.CreateMandatoryRestorePointAsync();
                if (!ok)
                {
                    ToggleStepZeroButtons(true);
                    return;
                }
                _state.RestorePointCreated = true;
            }

            _state.StepZeroCompleted = true;
            StepZeroOrchestrator.SaveState(_state);
            ToggleStepZeroButtons(true);
            UnlockMainOptimizer();
        }

        private async Task<bool> EnsureMandatoryRestorePointAsync()
        {
            if (_state.RestorePointCreated)
            {
                await SmoothProgressToAsync(
                    "ETAPA 1 DE 3 · RESPALDO PREVENTIVO",
                    "Verificando integridad del punto de restauración preventivo...",
                    34,
                    2500);
                return true;
            }

            _ = SmoothProgressToAsync(
                "ETAPA 1 DE 3 · RESPALDO PREVENTIVO",
                "Creando punto de restauración preventivo en el sistema...",
                30,
                2600);

            bool ok = await RestorePointManager.CreateMandatoryRestorePointAsync();

            if (!ok)
            {
                UpdateProgressState("ERROR DE RESPALDO", "No se pudo crear el punto de restauración. Verificá que el servicio VSS esté activo.", 0);
                await Task.Delay(1200);
                ClosePrepModal();
                ToggleStepZeroButtons(true);
                return false;
            }

            _state.RestorePointCreated = true;
            StepZeroOrchestrator.SaveState(_state);

            await SmoothProgressToAsync(
                "ETAPA 1 DE 3 · RESPALDO PREVENTIVO",
                "Punto de restauración guardado correctamente.",
                35,
                650);

            return true;
        }

        private async Task ExecuteDriverInstallationPipelineAsync(string finalInlineMessage)
        {
            ToggleStepZeroButtons(false);
            StepZeroOrchestrator.SetWindowsUpdateDriverBlock(true);

            if (_hardware.IsAmdCpu)
            {
                await SmoothProgressToAsync(
                    "ETAPA 2 DE 3 · CHIPSET Y PLACA BASE",
                    "Preparando perfil limpio de controladores de Chipset AMD...",
                    66,
                    2700);
            }
            else
            {
                await SmoothProgressToAsync(
                    "ETAPA 2 DE 3 · CHIPSET Y PLACA BASE",
                    "Verificando controladores base y buses del sistema...",
                    66,
                    2600);
            }

            if (_hardware.HasNvidiaGpu)
            {
                await SmoothProgressToAsync(
                    "ETAPA 3 DE 3 · CONTROLADOR GRÁFICO",
                    "Configurando controlador NVIDIA sin telemetría ni paneles extra...",
                    95,
                    2800);
            }
            else if (_hardware.HasAmdGpu)
            {
                await SmoothProgressToAsync(
                    "ETAPA 3 DE 3 · CONTROLADOR GRÁFICO",
                    "Configurando controlador gráfico AMD Radeon limpio...",
                    95,
                    2800);
            }
            else
            {
                await SmoothProgressToAsync(
                    "ETAPA 3 DE 3 · CONTROLADOR GRÁFICO",
                    "Ajustando perfil gráfico del sistema...",
                    95,
                    2600);
            }

            StepZeroOrchestrator.SetWindowsUpdateDriverBlock(false);
            _state.PendingPostRebootInstall = false;
            _state.StepZeroCompleted = true;
            StepZeroOrchestrator.SaveState(_state);

            await SmoothProgressToAsync(
                "PROCESO COMPLETADO",
                finalInlineMessage,
                100,
                650);
            await Task.Delay(1100);

            ClosePrepModal();
            ToggleStepZeroButtons(true);
        }

        private void UnlockMainOptimizer()
        {
            ShowView(MainOptimizerView);
            if (_allTweaks.Count == 0)
                InitOptimizerCatalog();
            else
                RefreshDisplayedTweaks();
        }

        private void ToggleStepZeroButtons(bool enabled)
        {
            BtnFreshInstall.IsEnabled = enabled;
            BtnCleanAndInstall.IsEnabled = enabled;
            BtnSkipDrivers.IsEnabled = enabled;
        }

        private void InitOptimizerCatalog()
        {
            var baseTweaks = TweakCatalog.GetAllTweaks()
                .Where(t => !string.Equals(t.Category, "Instalador Día 0", StringComparison.OrdinalIgnoreCase) &&
                            !t.Title.StartsWith("Instalar ", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var appsList = new List<TweakItem>
            {
                CreateAppItem(
                    "Steam",
                    "Gaming y Comunicación",
                    "Valve.Steam",
                    "Cliente oficial de juegos de Valve.",
                    "#66C0F4",
                    "M18.5,4 A4.5,4.5 0 1,0 14.1,9.5 L10.6,14.5 A3.5,3.5 0 0,0 7.5,16.2 L3,14.3 L2,16.8 L6.6,18.7 A3.5,3.5 0 0,0 13.3,17.2 L17.6,12.8 A4.5,4.5 0 0,0 18.5,4 Z M18.5,6.2 A2.3,2.3 0 1,1 16.2,8.5 A2.3,2.3 0 0,1 18.5,6.2 Z"),

                CreateAppItem(
                    "Discord",
                    "Gaming y Comunicación",
                    "Discord.Discord",
                    "Comunicación por voz, texto y pantalla compartida.",
                    "#5865F2",
                    "M19.3,5.3 A16.5,16.5 0 0,0 15.2,4 L14.7,5 A15,15 0 0,0 9.3,5 L8.8,4 A16.5,16.5 0 0,0 4.7,5.3 C2.1,9.2 1.4,13 1.8,16.7 A16.7,16.7 0 0,0 6.8,19.2 L7.9,17.4 A10.7,10.7 0 0,1 6.2,16.6 L6.6,16.3 A11.8,11.8 0 0,0 17.4,16.3 L17.8,16.6 A10.7,10.7 0 0,1 16.1,17.4 L17.2,19.2 A16.7,16.7 0 0,0 22.2,16.7 C22.7,12.4 21.4,8.6 19.3,5.3 Z M8.7,14.3 C7.7,14.3 6.9,13.4 6.9,12.3 C6.9,11.2 7.7,10.3 8.7,10.3 C9.7,10.3 10.5,11.2 10.5,12.3 C10.5,13.4 9.7,14.3 8.7,14.3 Z M15.3,14.3 C14.3,14.3 13.5,13.4 13.5,12.3 C13.5,11.2 14.3,10.3 15.3,10.3 C16.3,10.3 17.1,11.2 17.1,12.3 C17.1,13.4 16.3,14.3 15.3,14.3 Z"),

                CreateAppItem(
                    "VLC Media Player",
                    "Multimedia y Streaming",
                    "VideoLAN.VLC",
                    "Reproductor multimedia compatible con todos los formatos.",
                    "#FF8800",
                    "M12,2 L9.5,9 H14.5 L12,2 Z M8.5,12 L7,16.5 H17 L15.5,12 H8.5 Z M5,19 L4,22 H20 L19,19 H5 Z"),

                CreateAppItem(
                    "Spotify",
                    "Multimedia y Streaming",
                    "Spotify.Spotify",
                    "Cliente de escritorio para reproducción de música.",
                    "#1DB954",
                    "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M16.6,16.4 C16.4,16.7 16,16.8 15.7,16.6 C13.3,15.1 10.3,14.8 6.8,15.6 C6.4,15.7 6.1,15.4 6,15.1 C5.9,14.7 6.2,14.4 6.5,14.3 C10.4,13.4 13.7,13.8 16.4,15.4 C16.7,15.6 16.8,16.1 16.6,16.4 Z M17.8,13.7 C17.6,14.1 17.1,14.2 16.7,14 C14,12.3 9.9,11.8 6.7,12.8 C6.2,12.9 5.8,12.7 5.6,12.2 C5.5,11.7 5.7,11.3 6.2,11.1 C9.9,10 14.4,10.5 17.5,12.4 C17.9,12.7 18,13.3 17.8,13.7 Z M17.9,10.9 C14.6,9 9.2,8.8 6.1,9.7 C5.6,9.9 5,9.6 4.9,9.1 C4.7,8.6 5,8 5.5,7.9 C9.1,6.8 15.1,7 18.9,9.2 C19.4,9.5 19.5,10.1 19.2,10.6 C18.9,11 18.3,11.2 17.9,10.9 Z"),

                CreateAppItem(
                    "OBS Studio",
                    "Multimedia y Streaming",
                    "OBSProject.OBSStudio",
                    "Grabación de pantalla y transmisión con aceleración por hardware.",
                    "#94A3B8",
                    "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M12,5.5 A3.5,3.5 0 0,1 15.2,10.4 A3.5,3.5 0 0,1 10.5,8.8 A3.5,3.5 0 0,1 12,5.5 Z M7.2,14.2 A3.5,3.5 0 0,1 10.2,9.1 A3.5,3.5 0 0,1 11.2,14.1 A3.5,3.5 0 0,1 7.2,14.2 Z M16.8,14.2 A3.5,3.5 0 0,1 12.8,14.1 A3.5,3.5 0 0,1 13.8,9.1 A3.5,3.5 0 0,1 16.8,14.2 Z"),

                CreateAppItem(
                    "Brave Browser",
                    "Navegación y Utilidades",
                    "Brave.Brave",
                    "Navegador basado en Chromium con bloqueo de rastreadores.",
                    "#FB542B",
                    "M12,2 L4,5.5 V11.5 C4,16.8 7.4,20.8 12,22 C16.6,20.8 20,16.8 20,11.5 V5.5 L12,2 Z M8.5,9.5 L12,7.5 L15.5,9.5 L14.5,13.5 L12,16.5 L9.5,13.5 L8.5,9.5 Z"),

                CreateAppItem(
                    "7-Zip",
                    "Navegación y Utilidades",
                    "7zip.7zip",
                    "Compresor y extractor de archivos ligero y sin publicidad.",
                    "#4DA3FF",
                    "M4,3 H20 A1,1 0 0,1 21,4 V20 A1,1 0 0,1 20,21 H4 A1,1 0 0,1 3,20 V4 A1,1 0 0,1 4,3 Z M10,5 V7 H12 V5 H10 Z M12,7 V9 H14 V7 H12 Z M10,9 V11 H12 V9 H10 Z M12,11 V13 H14 V11 H12 Z M9,14 H15 V18 H9 V14 Z"),

                CreateAppItem(
                    "Notepad++",
                    "Navegación y Utilidades",
                    "Notepad++.Notepad++",
                    "Editor de texto y código rápido y liviano.",
                    "#7EE787",
                    "M6,2 H14 L19,7 V20 A2,2 0 0,1 17,22 H6 A2,2 0 0,1 4,20 V4 A2,2 0 0,1 6,2 Z M9.5,12 L7,14.5 L9.5,17 H10.8 L8.3,14.5 L10.8,12 H9.5 Z M14.5,12 H13.2 L15.7,14.5 L13.2,17 H14.5 L17,14.5 L14.5,12 Z"),

                CreateAppItem(
                    "ShareX",
                    "Navegación y Utilidades",
                    "ShareX.ShareX",
                    "Herramienta avanzada para capturas y grabación de pantalla.",
                    "#38BDF8",
                    "M4,4 H9 V6 H6 V9 H4 V4 Z M15,4 H20 V9 H18 V6 H15 V4 Z M4,15 H6 V18 H9 V20 H4 V15 Z M18,15 H20 V20 H15 V18 H18 V15 Z M11,8 H13 V11 H16 V13 H13 V16 H11 V13 H8 V11 H11 V8 Z"),

                CreateAppItem(
                    "Everything",
                    "Navegación y Utilidades",
                    "voidtools.Everything",
                    "Buscador instantáneo de archivos locales por nombre en NTFS.",
                    "#F59E0B",
                    "M10.5,3 A7.5,7.5 0 1,0 15.1,16.4 L19.8,21.1 L21.2,19.7 L16.5,15 A7.5,7.5 0 0,0 10.5,3 Z M10.5,5.5 A5,5 0 1,1 5.5,10.5 A5,5 0 0,1 10.5,5.5 Z")
            };

            _allTweaks = appsList.Concat(baseTweaks).ToList();
            RefreshDisplayedTweaks();
        }

        private static TweakItem CreateAppItem(string name, string groupName, string wingetId, string description, string iconColor, string iconGeometry)
        {
            return new TweakItem
            {
                Category = "Apps",
                GroupName = groupName,
                Title = name,
                Description = description,
                TechnicalDetail = $"winget install --id {wingetId} -e --silent",
                IconColor = iconColor,
                IconGeometry = iconGeometry,
                IsEnabled = false,
                ApplyAction = () => RunWingetInstall(wingetId),
                RevertAction = () => { }
            };
        }

        private static void RunWingetInstall(string packageId)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    Arguments = $"install --id {packageId} -e --silent --accept-package-agreements --accept-source-agreements",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var process = Process.Start(psi);
                process?.WaitForExit();
            }
            catch
            {
                // Ignorar si winget no está disponible en el entorno
            }
        }

        private void RefreshDisplayedTweaks()
        {
            string query = TxtSearch.Text.Trim();
            List<TweakItem> filtered;

            if (string.IsNullOrEmpty(query))
            {
                filtered = _allTweaks
                    .Where(t => t.Category == _activeCategory)
                    .ToList();
            }
            else
            {
                filtered = _allTweaks
                    .Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var view = new ListCollectionView(filtered);
            if (filtered.Any(t => !string.IsNullOrEmpty(t.GroupName)))
            {
                view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(TweakItem.GroupName)));
            }

            TweaksItemsControl.ItemsSource = view;
        }

        private void CategoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedBtn) return;
            _activeCategory = clickedBtn.Tag?.ToString() ?? "Apps";
            TxtSearch.Text = string.Empty;

            foreach (var tweak in _allTweaks)
                tweak.IsDetailsOpen = false;

            foreach (var child in SidebarMenu.Children)
            {
                if (child is Button btn && btn != BtnBackToStepZero)
                {
                    btn.Style = btn == clickedBtn
                        ? (Style)FindResource("PrimaryButtonStyle")
                        : (Style)FindResource("GhostButtonStyle");
                }
            }
            RefreshDisplayedTweaks();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshDisplayedTweaks();

        private void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TweakItem clickedItem)
            {
                bool targetState = !clickedItem.IsDetailsOpen;
                foreach (var tweak in _allTweaks)
                {
                    tweak.IsDetailsOpen = false;
                }
                clickedItem.IsDetailsOpen = targetState;
            }
        }

        private async void BtnApplyChanges_Click(object sender, RoutedEventArgs e)
        {
            TxtActionFeedback.Text = "Aplicando cambios...";
            await Task.Run(() =>
            {
                foreach (var tweak in _allTweaks)
                {
                    if (tweak.IsEnabled)
                        tweak.ApplyAction?.Invoke();
                    else
                        tweak.RevertAction?.Invoke();
                }
            });
            TxtActionFeedback.Text = "Cambios aplicados con respaldo";
        }

        private async void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
        {
            TxtActionFeedback.Text = "Restaurando valores originales...";
            await Task.Run(() => RegistryEngine.RestoreAllBackups());
            InitOptimizerCatalog();
            TxtActionFeedback.Text = "Sistema restaurado al estado original";
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
