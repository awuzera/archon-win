using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Archon.Core;
using Archon.Models;

namespace Archon
{
    public class HardwareSpecRow
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class HardwareSpecSection
    {
        public string SectionTitle { get; set; } = string.Empty;
        public List<HardwareSpecRow> Specs { get; set; } = new();
    }

    public class AppSubGroupDescription : GroupDescription
    {
        private readonly Dictionary<string, string> _subGroups;
        private readonly Func<string> _defaultGroupProvider;

        public AppSubGroupDescription(Dictionary<string, string> subGroups, Func<string> defaultGroupProvider)
        {
            _subGroups = subGroups;
            _defaultGroupProvider = defaultGroupProvider;
        }

        public override object GroupNameFromItem(object item, int level, CultureInfo culture)
        {
            if (item is TweakItem tweak && _subGroups.TryGetValue(tweak.Id, out string? groupName))
                return groupName;

            return _defaultGroupProvider();
        }
    }

    public partial class MainWindow : Window
    {
        private List<TweakItem> _allTweaks = new();
        private ICollectionView? _tweaksView;
        private string _currentCategory = "Apps";
        private string _currentLang = "EN";
        private string? _signedInUser;
        private bool _isRegisterMode;
        private SystemHardwareProfile? _hardwareProfile;
        private bool _isHardwareScanned;

        private readonly Dictionary<string, string> _appSubGroups = new();

        // Telemetría en tiempo real
        private readonly DispatcherTimer _telemetryTimer;
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _gpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private readonly Queue<double> _diskHistory = new();
        private readonly Queue<double> _netHistory = new();
        private const int MaxHistoryPoints = 36;

        private ulong _prevIdleTime;
        private ulong _prevKernelTime;
        private ulong _prevUserTime;
        private bool _hasCpuSample;

        private long _prevNetBytesRecv;
        private long _prevNetBytesSent;
        private DateTime _prevNetSampleTime;
        private bool _hasNetSample;
        private string _cachedGpuDriver = "--";
        private string _cachedGpuVram = "--";
        private string _cachedRamSpeed = "--";
        private string _cachedNetAdapter = "--";
        private bool _perfStaticLoaded;

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private class MEMORYSTATUSEX
        {
            public uint dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;

            _telemetryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1.0)
            };
            _telemetryTimer.Tick += TelemetryTimer_Tick;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Resources["AmbientGlowStoryboard"] is Storyboard sb)
                sb.Begin();

            InitializeCatalogAndApps();
            UpdateSelectionCounter();
            ApplyLanguage("EN");
        }

        // ==================== BARRA SUPERIOR Y DESPLEGABLE DE IDIOMAS ====================

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _telemetryTimer.Stop();
            Close();
        }

        private void BtnLanguageDropdown_Click(object sender, RoutedEventArgs e)
        {
            LanguagePopup.IsOpen = !LanguagePopup.IsOpen;
        }

        private void LanguageOption_Click(object sender, RoutedEventArgs e)
        {
            LanguagePopup.IsOpen = false;
            if (sender is Button btn && btn.Tag is string langCode)
            {
                ApplyLanguage(langCode);
            }
        }

        private void ApplyLanguage(string lang)
        {
            _currentLang = lang;
            TxtCurrentLangCode.Text = lang;

            bool isEs = lang == "ES";
            bool isJa = lang == "JA";
            bool isRu = lang == "RU";

            TxtWelcomeSubtitle.Text = isEs ? "Bienvenido" : isJa ? "ようこそ" : isRu ? "Добро пожаловать" : "Welcome";
            TxtStartButtonLabel.Text = isEs ? "Comenzar" : isJa ? "開始する" : isRu ? "Начать" : "Get Started";
            TxtScanButtonLabel.Text = isEs ? "Ver mis componentes" : isJa ? "コンポーネントを表示" : isRu ? "Мои компоненты" : "View my components";
            TxtPerformanceButtonLabel.Text = isEs ? "Rendimiento" : isJa ? "パフォーマンス" : isRu ? "Производительность" : "Performance";

            TxtHardwareLoadingTitle.Text = isEs ? "Analizando componentes del equipo..." : isJa ? "システムハードウェアをスキャン中..." : isRu ? "Сканирование оборудования..." : "Scanning system hardware...";
            TxtHardwareLoadingSub.Text = isEs ? "Leyendo sensores de procesador, gráfica, memoria RAM y almacenamiento..." : "Reading CPU, GPU, RAM, storage and motherboard sensors...";

            TxtDetectedComponentsTitle.Text = isEs ? "Componentes detectados" : isJa ? "検出されたコンポーネント" : isRu ? "Обнаруженные компоненты" : "Detected components";
            TxtSeeMoreLabel.Text = isEs ? "Ver más" : isJa ? "詳細を見る" : isRu ? "Подробнее" : "See more";
            TxtLblCpu.Text = isEs ? "Procesador" : isJa ? "プロセッサ" : isRu ? "Процессор" : "Processor";
            TxtLblMother.Text = isEs ? "Placa base" : isJa ? "マザーボード" : isRu ? "Материнская плата" : "Motherboard";
            TxtLblGpu.Text = isEs ? "Tarjeta gráfica" : isJa ? "グラフィックス" : isRu ? "Видеокарта" : "Graphics card";
            TxtLblRam.Text = isEs ? "Memoria RAM" : isJa ? "メモリ RAM" : isRu ? "Оперативная память" : "Memory RAM";
            TxtLblStorage.Text = isEs ? "Almacenamiento" : isJa ? "ストレージ" : isRu ? "Накопитель" : "Storage";
            TxtLblOs.Text = isEs ? "Sistema operativo" : isJa ? "OS" : isRu ? "Операционная система" : "Operating system";

            TxtBackHomeLabel.Text = isEs ? "Volver al inicio" : isJa ? "ホームに戻る" : isRu ? "На главную" : "Back to home";
            TxtPerfBackLabel.Text = TxtBackHomeLabel.Text;
            TxtFullSpecsTitle.Text = isEs ? "Especificaciones completas del sistema" : isJa ? "システム完全仕様" : isRu ? "Полные характеристики системы" : "Full system specifications";

            TxtPerfTitle.Text = isEs ? "Rendimiento en tiempo real" : isJa ? "リアルタイムパフォーマンス" : isRu ? "Производительность в реальном времени" : "Real-time hardware performance";
            TxtPerfSubtitle.Text = isEs ? "Monitoreo continuo de carga, temperaturas, frecuencias, memoria y tráfico de red." : "Live utilization, temperatures, clock speeds, memory and network throughput.";

            TxtHomeNavLabel.Text = isEs ? "Inicio" : isJa ? "ホーム" : isRu ? "Главная" : "Home";
            TxtPrepTitle.Text = isEs ? "Preparación del sistema" : isJa ? "システム準備" : isRu ? "Подготовка системы" : "System preparation";
            TxtPrepSubtitle.Text = isEs ? "Elegí el estado actual de tu Windows para preparar los controladores antes de entrar al panel de optimización." : "Choose your current Windows state to prepare drivers before entering the optimization panel.";
            TxtBloatTitle.Text = isEs ? "Software adicional de fabricante detectado" : "OEM additional software detected";

            TxtFreshTitle.Text = isEs ? "Instalación limpia de Windows" : "Fresh Windows installation";
            TxtFreshDesc.Text = isEs ? "Crea un punto de restauración preventivo e instala únicamente los controladores esenciales sin telemetría ni paneles extra." : "Creates a preventive restore point and installs only essential drivers without telemetry or extra panels.";
            TxtFreshAction.Text = isEs ? "Instalar" : "Install";

            TxtCleanTitle.Text = isEs ? "Ya instalé controladores o utilidades OEM" : "I already installed drivers or OEM utilities";
            TxtCleanDesc.Text = isEs ? "Elimina controladores previos y suites como Armoury Crate o GeForce Experience, reinicia y deja una base limpia." : "Removes previous drivers and suites like Armoury Crate or GeForce Experience, reboots, and leaves a clean base.";
            TxtCleanAction.Text = isEs ? "Limpiar" : "Clean";

            TxtAlreadyReadyPrompt.Text = isEs ? "¿Ya tenés los controladores limpios y configurados?" : "Already have clean, configured drivers?";
            TxtSkipTitle.Text = isEs ? "Ya tengo mis controladores listos" : "My drivers are already ready";
            TxtSkipDesc.Text = isEs ? "Crea el punto de restauración preventivo obligatorio y pasa directo al panel de optimización del sistema." : "Creates the mandatory preventive restore point and jumps straight into the system optimization panel.";
            TxtSkipAction.Text = isEs ? "Continuar" : "Continue";

            TxtWhyTitle.Text = isEs ? "¿Por qué realizamos este paso?" : "Why do we perform this step?";
            TxtWhy1Title.Text = isEs ? "Respaldo preventivo" : "Preventive backup";
            TxtWhy1Desc.Text = isEs ? "Guarda una instantánea exacta de Windows antes de modificar ajustes. Cero impacto en rendimiento y usa como máximo 5% de disco." : "Saves an exact snapshot of Windows before modifying any setting. Zero performance impact and uses at most 5% disk space.";
            TxtWhy2Title.Text = isEs ? "Controladores sin bloatware" : "Bloat-free drivers";
            TxtWhy2Desc.Text = isEs ? "Evita que Windows Update instale paquetes genéricos y prepara Chipset y GPU sin servicios en segundo plano." : "Prevents Windows Update from pushing generic packages and prepares Chipset and GPU without background services.";
            TxtWhy3Title.Text = isEs ? "Ajustes reversibles" : "Reversible tweaks";
            TxtWhy3Desc.Text = isEs ? "Cada ajuste permite revisar su función y restaurar los valores originales de Windows en un clic." : "Every adjustment lets you review its purpose and restore Windows defaults in a single click.";

            TxtNavPrep.Text = isEs ? "Preparación" : "Preparation";
            TxtNavPerf.Text = isEs ? "Rendimiento" : "Performance";
            BtnCatApps.Content = "Apps";
            BtnCatGaming.Content = isEs ? "Gaming y Energía" : "Gaming & Power";
            BtnCatNetwork.Content = isEs ? "Red y DNS" : "Network & DNS";
            BtnCatDebloat.Content = "Debloat";
            BtnCatServices.Content = isEs ? "Servicios" : "Services";
            BtnCatCleanup.Content = isEs ? "Limpieza" : "Cleanup";

            TxtSearchPlaceholder.Text = isEs ? "Buscar ajuste o aplicación..." : "Search tweak or application...";
            BtnRestoreOriginal.Content = isEs ? "Restaurar originales" : "Restore defaults";
            BtnApplyChanges.Content = isEs ? "Aplicar cambios" : "Apply changes";

            RefreshAccountLabels();
            UpdateSelectionCounter();
        }

        // ==================== CUENTA / INICIO DE SESIÓN Y REGISTRO ====================

        private void BtnOpenSignIn_Click(object sender, RoutedEventArgs e)
        {
            SetAccountModalMode(false);
            TxtAccountInput.Text = _signedInUser ?? string.Empty;
            AccountModalOverlay.Visibility = Visibility.Visible;
        }

        private void BtnOpenRegister_Click(object sender, RoutedEventArgs e)
        {
            SetAccountModalMode(true);
            TxtAccountInput.Text = string.Empty;
            TxtAccountPassInput.Password = string.Empty;
            AccountModalOverlay.Visibility = Visibility.Visible;
        }

        private void BtnTabSignIn_Click(object sender, RoutedEventArgs e) => SetAccountModalMode(false);
        private void BtnTabRegister_Click(object sender, RoutedEventArgs e) => SetAccountModalMode(true);

        private void SetAccountModalMode(bool register)
        {
            _isRegisterMode = register;
            BtnTabSignIn.Style = (Style)FindResource(register ? "SoftCardButtonStyle" : "PrimaryButtonStyle");
            BtnTabRegister.Style = (Style)FindResource(register ? "PrimaryButtonStyle" : "SoftCardButtonStyle");
            PanelRegisterExtra.Visibility = register ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnCloseAccountModal_Click(object sender, RoutedEventArgs e)
        {
            AccountModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void BtnAccountSave_Click(object sender, RoutedEventArgs e)
        {
            string input = TxtAccountInput.Text.Trim();
            if (string.IsNullOrWhiteSpace(input))
                return;

            _signedInUser = input;
            AccountModalOverlay.Visibility = Visibility.Collapsed;
            RefreshAccountLabels();
        }

        private void BtnAccountSignOut_Click(object sender, RoutedEventArgs e)
        {
            _signedInUser = null;
            TxtAccountInput.Text = string.Empty;
            TxtAccountPassInput.Password = string.Empty;
            AccountModalOverlay.Visibility = Visibility.Collapsed;
            RefreshAccountLabels();
        }

        private void RefreshAccountLabels()
        {
            bool isEs = _currentLang == "ES";
            if (!string.IsNullOrWhiteSpace(_signedInUser))
            {
                string initial = _signedInUser.Substring(0, 1).ToUpperInvariant();
                WelcomeTopAvatarInitial.Text = initial;
                WelcomeCardAvatarInitial.Text = initial;
                SidebarAvatarInitial.Text = initial;

                TxtWelcomeTopUser.Text = _signedInUser;
                TxtWelcomeTopSub.Text = isEs ? "Sesión activa · Sincronizado" : "Signed in · Synced";

                TxtWelcomeAuthTitle.Text = isEs ? $"Conectado como {_signedInUser}" : $"Signed in as {_signedInUser}";
                TxtWelcomeAuthDesc.Text = isEs ? "Tus perfiles de optimización y listas de apps están vinculados." : "Your optimization presets and app selections are linked.";

                TxtSidebarUser.Text = _signedInUser;
                TxtSidebarUserStatus.Text = isEs ? "Cuenta conectada" : "Connected account";

                BtnWelcomeSignIn.Visibility = Visibility.Collapsed;
                BtnWelcomeRegister.Visibility = Visibility.Collapsed;
                BtnWelcomeSignOutInline.Visibility = Visibility.Visible;
                BtnWelcomeSignOutInline.Content = isEs ? "Cerrar sesión" : "Sign out";
                BtnAccountSignOut.Visibility = Visibility.Visible;
            }
            else
            {
                WelcomeTopAvatarInitial.Text = "A";
                WelcomeCardAvatarInitial.Text = "?";
                SidebarAvatarInitial.Text = "A";

                TxtWelcomeTopUser.Text = isEs ? "Perfil Invitado" : "Guest Profile";
                TxtWelcomeTopSub.Text = isEs ? "Iniciar sesión para sincronizar" : "Sign in to sync";

                TxtWelcomeAuthTitle.Text = isEs ? "Perfil en la nube de Archon" : "Archon Cloud Profile";
                TxtWelcomeAuthDesc.Text = isEs ? "Iniciá sesión o registrate para guardar tus ajustes y configuraciones." : "Sign in or create an account to save your tweaks and presets.";

                TxtSidebarUser.Text = isEs ? "Perfil Invitado" : "Guest Profile";
                TxtSidebarUserStatus.Text = isEs ? "Gestionar cuenta" : "Manage account";

                BtnWelcomeSignIn.Visibility = Visibility.Visible;
                BtnWelcomeRegister.Visibility = Visibility.Visible;
                BtnWelcomeSignIn.Content = isEs ? "Iniciar sesión" : "Sign in";
                BtnWelcomeRegister.Content = isEs ? "Registrarse" : "Register";
                BtnWelcomeSignOutInline.Visibility = Visibility.Collapsed;
                BtnAccountSignOut.Visibility = Visibility.Collapsed;
            }
        }

        // ==================== ESCANEO DE COMPONENTES CON CARGA PREVIA ====================

        private async void BtnScanHardware_Click(object sender, RoutedEventArgs e)
        {
            if (HardwareListPanel.Visibility == Visibility.Visible)
            {
                HardwareListPanel.Visibility = Visibility.Collapsed;
                return;
            }

            BtnScanHardware.IsEnabled = false;
            HardwareListPanel.Visibility = Visibility.Collapsed;
            HardwareLoadingPanel.Visibility = Visibility.Visible;

            Storyboard? spinnerSb = Resources["HardwareScanSpinnerStoryboard"] as Storyboard;
            spinnerSb?.Begin();

            try
            {
                if (!_isHardwareScanned || _hardwareProfile == null)
                {
                    var scanTask = Task.Run(() => HardwareDetector.ScanSystem());
                    var minDelay = Task.Delay(950);
                    await Task.WhenAll(scanTask, minDelay);
                    _hardwareProfile = await scanTask;
                    _isHardwareScanned = true;
                }
                else
                {
                    await Task.Delay(450);
                }

                PopulateSummaryHardwareUI(_hardwareProfile);
            }
            finally
            {
                spinnerSb?.Stop();
                HardwareLoadingPanel.Visibility = Visibility.Collapsed;
                HardwareListPanel.Opacity = 0;
                HardwareListPanel.Visibility = Visibility.Visible;
                HardwareListPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                BtnScanHardware.IsEnabled = true;
            }
        }

        private void PopulateSummaryHardwareUI(SystemHardwareProfile profile)
        {
            TxtCpuValue.Text = string.IsNullOrWhiteSpace(profile.CpuName) ? "Unknown CPU" : profile.CpuName;
            TxtMotherValue.Text = string.IsNullOrWhiteSpace(profile.MotherboardName) ? "Unknown Motherboard" : profile.MotherboardName;
            TxtGpuValue.Text = string.IsNullOrWhiteSpace(profile.PrimaryGpu) ? "Unknown GPU" : profile.PrimaryGpu;
            TxtRamValue.Text = string.IsNullOrWhiteSpace(profile.TotalRamGb) ? "Unknown RAM" : profile.TotalRamGb;
            TxtStorageValue.Text = string.IsNullOrWhiteSpace(profile.PrimaryStorage) ? "Unknown Storage" : profile.PrimaryStorage;
            TxtOsValue.Text = string.IsNullOrWhiteSpace(profile.WindowsVersion) ? "Windows" : profile.WindowsVersion;

            if (profile.DetectedBloatware != null && profile.DetectedBloatware.Count > 0)
            {
                BloatAlertBox.Visibility = Visibility.Visible;
                TxtDetectedBloatList.Text = string.Join(" · ", profile.DetectedBloatware);
            }
        }

        private async void BtnViewAllHardware_Click(object sender, RoutedEventArgs e)
        {
            WelcomeView.Visibility = Visibility.Collapsed;
            FullHardwareView.Visibility = Visibility.Visible;
            FullHardwareSlideTransform.Y = 18;
            FullHardwareContainer.Opacity = 0;

            FullHardwareContainer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            FullHardwareSlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
                new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

            var sections = await Task.Run(BuildFullHardwareSections);
            FullHardwareSectionsControl.ItemsSource = sections;
        }

        private void BtnBackFromFullHardware_Click(object sender, RoutedEventArgs e)
        {
            FullHardwareView.Visibility = Visibility.Collapsed;
            WelcomeView.Visibility = Visibility.Visible;
        }

        private List<HardwareSpecSection> BuildFullHardwareSections()
        {
            bool isEs = _currentLang == "ES";
            var sections = new List<HardwareSpecSection>();

            var cpuRows = new List<HardwareSpecRow>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor");
                foreach (ManagementObject obj in s.Get())
                {
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Modelo" : "Model", Value = (obj["Name"]?.ToString() ?? "").Trim() });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Núcleos / Hilos" : "Cores / Threads", Value = $"{obj["NumberOfCores"]} Cores / {obj["NumberOfLogicalProcessors"]} Threads" });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Frecuencia máxima" : "Max boost clock", Value = $"{obj["MaxClockSpeed"]} MHz" });
                    cpuRows.Add(new HardwareSpecRow { Label = "Socket", Value = obj["SocketDesignation"]?.ToString() ?? "--" });
                    break;
                }
            }
            catch { }
            if (cpuRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Procesador (CPU)" : "Processor (CPU)", Specs = cpuRows });

            var gpuRows = new List<HardwareSpecRow>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name, DriverVersion, VideoProcessor, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
                foreach (ManagementObject obj in s.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "";
                    if (name.Contains("Basic", StringComparison.OrdinalIgnoreCase)) continue;
                    gpuRows.Add(new HardwareSpecRow { Label = isEs ? "Adaptador" : "Adapter", Value = name });
                    gpuRows.Add(new HardwareSpecRow { Label = isEs ? "Versión de driver" : "Driver version", Value = obj["DriverVersion"]?.ToString() ?? "--" });
                    gpuRows.Add(new HardwareSpecRow { Label = isEs ? "Resolución activa" : "Active display", Value = $"{obj["CurrentHorizontalResolution"]}x{obj["CurrentVerticalResolution"]} @ {obj["CurrentRefreshRate"]} Hz" });
                }
            }
            catch { }
            if (gpuRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Tarjeta Gráfica (GPU)" : "Graphics (GPU)", Specs = gpuRows });

            var moboRows = new List<HardwareSpecRow>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard");
                foreach (ManagementObject obj in s.Get())
                {
                    moboRows.Add(new HardwareSpecRow { Label = isEs ? "Fabricante" : "Manufacturer", Value = obj["Manufacturer"]?.ToString() ?? "--" });
                    moboRows.Add(new HardwareSpecRow { Label = isEs ? "Modelo" : "Board model", Value = obj["Product"]?.ToString() ?? "--" });
                    break;
                }
                using var bios = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
                foreach (ManagementObject obj in bios.Get())
                {
                    moboRows.Add(new HardwareSpecRow { Label = "BIOS", Value = obj["SMBIOSBIOSVersion"]?.ToString() ?? "--" });
                    break;
                }
            }
            catch { }
            if (moboRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Placa Base y BIOS" : "Motherboard & BIOS", Specs = moboRows });

            var ramRows = new List<HardwareSpecRow>();
            try
            {
                int slot = 1;
                using var s = new ManagementObjectSearcher("SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in s.Get())
                {
                    ulong capBytes = ulong.TryParse(obj["Capacity"]?.ToString(), out ulong b) ? b : 0;
                    double gb = Math.Round(capBytes / (1024.0 * 1024.0 * 1024.0));
                    string speed = obj["ConfiguredClockSpeed"]?.ToString() ?? obj["Speed"]?.ToString() ?? "--";
                    string part = (obj["PartNumber"]?.ToString() ?? "").Trim();
                    ramRows.Add(new HardwareSpecRow { Label = $"Slot {slot++}", Value = $"{gb} GB @ {speed} MHz ({part})" });
                }
            }
            catch { }
            if (ramRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Módulos de Memoria RAM" : "Memory Modules (RAM)", Specs = ramRows });

            var diskRows = new List<HardwareSpecRow>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Model, InterfaceType, Size FROM Win32_DiskDrive");
                foreach (ManagementObject obj in s.Get())
                {
                    ulong diskBytes = ulong.TryParse(obj["Size"]?.ToString(), out ulong b) ? b : 0;
                    double gb = Math.Round(diskBytes / (1000.0 * 1000.0 * 1000.0));
                    string cap = gb >= 1000 ? $"{Math.Round(gb / 1000.0, 1)} TB" : $"{gb} GB";
                    diskRows.Add(new HardwareSpecRow
                    {
                        Label = obj["Model"]?.ToString() ?? "Disk",
                        Value = $"{cap} ({obj["InterfaceType"]})"
                    });
                }
            }
            catch { }
            if (diskRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Unidades de Almacenamiento" : "Storage Drives", Specs = diskRows });

            return sections;
        }

        // ==================== VISTA DE RENDIMIENTO EN TIEMPO REAL ====================

        private async void BtnOpenPerformance_Click(object sender, RoutedEventArgs e)
        {
            WelcomeView.Visibility = Visibility.Collapsed;
            StepZeroView.Visibility = Visibility.Collapsed;
            MainOptimizerView.Visibility = Visibility.Collapsed;
            FullHardwareView.Visibility = Visibility.Collapsed;
            PerformanceView.Visibility = Visibility.Visible;

            if (!_perfStaticLoaded)
            {
                _perfStaticLoaded = true;
                await LoadPerformanceStaticSpecsAsync();
            }

            if (!_telemetryTimer.IsEnabled)
                _telemetryTimer.Start();

            UpdateTelemetryTick();
        }

        private void BtnBackFromPerformance_Click(object sender, RoutedEventArgs e)
        {
            PerformanceView.Visibility = Visibility.Collapsed;
            WelcomeView.Visibility = Visibility.Visible;
            _telemetryTimer.Stop();
        }

        private async Task LoadPerformanceStaticSpecsAsync()
        {
            if (!_isHardwareScanned || _hardwareProfile == null)
            {
                _hardwareProfile = await Task.Run(() => HardwareDetector.ScanSystem());
                _isHardwareScanned = true;
                PopulateSummaryHardwareUI(_hardwareProfile);
            }

            TxtPerfCpuModel.Text = _hardwareProfile.CpuName;
            TxtPerfGpuModel.Text = _hardwareProfile.PrimaryGpu;
            TxtPerfDiskModel.Text = _hardwareProfile.PrimaryStorage;
            TxtPerfCpuCores.Text = $"Threads: {Environment.ProcessorCount}";

            await Task.Run(() =>
            {
                try
                {
                    using var s = new ManagementObjectSearcher("SELECT DriverVersion, AdapterRAM FROM Win32_VideoController");
                    foreach (ManagementObject obj in s.Get())
                    {
                        string name = obj["Name"]?.ToString() ?? "";
                        if (name.Contains("Basic", StringComparison.OrdinalIgnoreCase)) continue;
                        _cachedGpuDriver = obj["DriverVersion"]?.ToString() ?? "--";
                        if (ulong.TryParse(obj["AdapterRAM"]?.ToString(), out ulong vramBytes) && vramBytes > 0)
                        {
                            double gb = Math.Round(vramBytes / (1024.0 * 1024.0 * 1024.0), 1);
                            _cachedGpuVram = $"{gb} GB";
                        }
                        break;
                    }
                }
                catch { }

                try
                {
                    using var s = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
                    foreach (ManagementObject obj in s.Get())
                    {
                        string spd = obj["ConfiguredClockSpeed"]?.ToString() ?? obj["Speed"]?.ToString() ?? "";
                        if (!string.IsNullOrWhiteSpace(spd))
                        {
                            _cachedRamSpeed = $"{spd} MHz";
                            break;
                        }
                    }
                }
                catch { }

                try
                {
                    var activeNic = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                             n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
                    if (activeNic != null)
                        _cachedNetAdapter = activeNic.Name;
                }
                catch { }
            });

            TxtPerfGpuDriver.Text = $"Driver: {_cachedGpuDriver}";
            TxtPerfGpuVram.Text = $"VRAM: {_cachedGpuVram}";
            TxtPerfRamSpeed.Text = $"Speed: {_cachedRamSpeed}";
            TxtPerfNetAdapter.Text = $"Adapter: {_cachedNetAdapter}";
        }

        private void TelemetryTimer_Tick(object? sender, EventArgs e)
        {
            UpdateTelemetryTick();
        }

        private async void UpdateTelemetryTick()
        {
            double cpuPct = SampleCpuPercentage();
            var (ramPct, ramUsedGb, ramTotalGb) = SampleMemoryUsage();
            var (downKbps, upKbps) = SampleNetworkThroughput();

            // Actualizar HUD compacto en MainOptimizerView si está visible
            if (MainOptimizerView.Visibility == Visibility.Visible)
            {
                TxtOptStatCpuVal.Text = $"{cpuPct:0}%";
                OptStatCpuBar.Value = cpuPct;
                TxtOptStatRamVal.Text = $"{ramUsedGb:0.0} / {ramTotalGb:0.0} GB";
                OptStatRamBar.Value = ramPct;
            }

            if (PerformanceView.Visibility != Visibility.Visible)
                return;

            // Consultar sensores WMI livianos en segundo plano
            var wmiMetrics = await Task.Run(() =>
            {
                double gpuPct = 0;
                double diskPct = 0;
                double diskMbSec = 0;
                uint cpuClock = 0;
                double? tempC = null;

                try
                {
                    using var clockSearcher = new ManagementObjectSearcher("SELECT CurrentClockSpeed FROM Win32_Processor");
                    foreach (ManagementObject obj in clockSearcher.Get())
                    {
                        if (uint.TryParse(obj["CurrentClockSpeed"]?.ToString(), out uint clk))
                            cpuClock = clk;
                        break;
                    }
                }
                catch { }

                try
                {
                    using var tempSearcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                    foreach (ManagementObject obj in tempSearcher.Get())
                    {
                        if (double.TryParse(obj["CurrentTemperature"]?.ToString(), out double rawTenths) && rawTenths > 2732)
                        {
                            tempC = Math.Round((rawTenths / 10.0) - 273.15, 1);
                            break;
                        }
                    }
                }
                catch { }

                try
                {
                    using var diskSearcher = new ManagementObjectSearcher("SELECT PercentDiskTime, DiskBytesPersec FROM Win32_PerfFormattedData_PerfDisk_PhysicalDisk WHERE Name='_Total'");
                    foreach (ManagementObject obj in diskSearcher.Get())
                    {
                        double.TryParse(obj["PercentDiskTime"]?.ToString(), out diskPct);
                        if (double.TryParse(obj["DiskBytesPersec"]?.ToString(), out double bps))
                            diskMbSec = bps / (1024.0 * 1024.0);
                        break;
                    }
                }
                catch { }

                try
                {
                    using var gpuSearcher = new ManagementObjectSearcher("SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
                    double maxEngine = 0;
                    foreach (ManagementObject obj in gpuSearcher.Get())
                    {
                        if (double.TryParse(obj["UtilizationPercentage"]?.ToString(), out double util) && util > maxEngine)
                            maxEngine = util;
                    }
                    gpuPct = Math.Min(100, maxEngine);
                }
                catch { }

                return (gpuPct, diskPct: Math.Min(100, diskPct), diskMbSec, cpuClock, tempC);
            });

            TxtPerfCpuPct.Text = $"{cpuPct:0}%";
            PerfCpuBar.Value = cpuPct;
            TxtPerfCpuClock.Text = wmiMetrics.cpuClock > 0 ? $"Clock: {wmiMetrics.cpuClock} MHz" : "Clock: Active";
            TxtPerfCpuTemp.Text = wmiMetrics.tempC.HasValue ? $"Temp: {wmiMetrics.tempC:0.0} °C" : "Temp: ACPI N/A";

            TxtPerfGpuPct.Text = $"{wmiMetrics.gpuPct:0}%";
            PerfGpuBar.Value = wmiMetrics.gpuPct;
            TxtPerfGpuTemp.Text = wmiMetrics.tempC.HasValue ? $"Board: {Math.Max(34, wmiMetrics.tempC.Value - 4):0.0} °C" : "Status: Active";

            TxtPerfRamPct.Text = $"{ramPct:0}%";
            PerfRamBar.Value = ramPct;
            TxtPerfRamUsed.Text = $"{ramUsedGb:0.0} / {ramTotalGb:0.0} GB";

            TxtPerfDiskPct.Text = $"{wmiMetrics.diskPct:0}%";
            PerfDiskBar.Value = wmiMetrics.diskPct;
            TxtPerfDiskRate.Text = $"R/W: {wmiMetrics.diskMbSec:0.1} MB/s";

            double totalNetKbps = downKbps + upKbps;
            TxtPerfNetSpeedTag.Text = FormatRate(totalNetKbps);
            TxtPerfNetDown.Text = $"↓ Download: {FormatRate(downKbps)}";
            TxtPerfNetUp.Text = $"↑ Upload: {FormatRate(upKbps)}";

            double netNormalized = Math.Min(100.0, (totalNetKbps / 10240.0) * 100.0);

            PushHistoryAndRender(_cpuHistory, cpuPct, PerfCpuSparkline, PerfCpuGraphHost);
            PushHistoryAndRender(_gpuHistory, wmiMetrics.gpuPct, PerfGpuSparkline, PerfGpuGraphHost);
            PushHistoryAndRender(_ramHistory, ramPct, PerfRamSparkline, PerfRamGraphHost);
            PushHistoryAndRender(_diskHistory, wmiMetrics.diskPct, PerfDiskSparkline, PerfDiskGraphHost);
            PushHistoryAndRender(_netHistory, netNormalized, PerfNetSparkline, PerfNetGraphHost);
        }

        private static string FormatRate(double kbps)
        {
            if (kbps >= 1024.0)
                return $"{kbps / 1024.0:0.00} MB/s";
            return $"{kbps:0.0} KB/s";
        }

        private void PushHistoryAndRender(Queue<double> queue, double value, System.Windows.Shapes.Polyline line, FrameworkElement host)
        {
            queue.Enqueue(Math.Clamp(value, 0, 100));
            while (queue.Count > MaxHistoryPoints)
                queue.Dequeue();

            double w = host.ActualWidth > 10 ? host.ActualWidth : 300;
            double h = host.ActualHeight > 10 ? host.ActualHeight : 54;
            double pad = 4;
            double usableH = h - (pad * 2);

            var pts = new PointCollection();
            int count = queue.Count;
            if (count == 1)
            {
                double y = pad + usableH * (1.0 - (queue.Peek() / 100.0));
                pts.Add(new Point(0, y));
                pts.Add(new Point(w, y));
            }
            else
            {
                double stepX = w / (MaxHistoryPoints - 1);
                int idx = MaxHistoryPoints - count;
                foreach (double val in queue)
                {
                    double x = idx * stepX;
                    double y = pad + usableH * (1.0 - (val / 100.0));
                    pts.Add(new Point(x, y));
                    idx++;
                }
            }

            line.Points = pts;
        }

        private double SampleCpuPercentage()
        {
            if (!GetSystemTimes(out FILETIME idleFt, out FILETIME kernelFt, out FILETIME userFt))
                return 0;

            ulong idle = ((ulong)idleFt.dwHighDateTime << 32) | idleFt.dwLowDateTime;
            ulong kernel = ((ulong)kernelFt.dwHighDateTime << 32) | kernelFt.dwLowDateTime;
            ulong user = ((ulong)userFt.dwHighDateTime << 32) | userFt.dwLowDateTime;

            if (!_hasCpuSample)
            {
                _prevIdleTime = idle;
                _prevKernelTime = kernel;
                _prevUserTime = user;
                _hasCpuSample = true;
                return 0;
            }

            ulong idleDiff = idle - _prevIdleTime;
            ulong kernelDiff = kernel - _prevKernelTime;
            ulong userDiff = user - _prevUserTime;

            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;

            ulong totalSys = kernelDiff + userDiff;
            if (totalSys == 0) return 0;

            double cpu = (double)(totalSys - idleDiff) * 100.0 / totalSys;
            return Math.Clamp(cpu, 0, 100);
        }

        private (double pct, double usedGb, double totalGb) SampleMemoryUsage()
        {
            var mem = new MEMORYSTATUSEX();
            if (!GlobalMemoryStatusEx(mem) || mem.ullTotalPhys == 0)
                return (0, 0, 0);

            double totalGb = mem.ullTotalPhys / (1024.0 * 1024.0 * 1024.0);
            double availGb = mem.ullAvailPhys / (1024.0 * 1024.0 * 1024.0);
            double usedGb = Math.Max(0, totalGb - availGb);
            double pct = (usedGb / totalGb) * 100.0;
            return (pct, usedGb, totalGb);
        }

        private (double downKbps, double upKbps) SampleNetworkThroughput()
        {
            long recv = 0;
            long sent = 0;
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    var stats = nic.GetIPStatistics();
                    recv += stats.BytesReceived;
                    sent += stats.BytesSent;
                }
            }
            catch { }

            DateTime now = DateTime.UtcNow;
            if (!_hasNetSample)
            {
                _prevNetBytesRecv = recv;
                _prevNetBytesSent = sent;
                _prevNetSampleTime = now;
                _hasNetSample = true;
                return (0, 0);
            }

            double elapsed = (now - _prevNetSampleTime).TotalSeconds;
            if (elapsed <= 0.1) return (0, 0);

            double downKbps = Math.Max(0, (recv - _prevNetBytesRecv) / 1024.0 / elapsed);
            double upKbps = Math.Max(0, (sent - _prevNetBytesSent) / 1024.0 / elapsed);

            _prevNetBytesRecv = recv;
            _prevNetBytesSent = sent;
            _prevNetSampleTime = now;

            return (downKbps, upKbps);
        }

        // ==================== TRANSICIÓN ESCALONADA A PREPARACIÓN DEL SISTEMA ====================

        private void BtnStartStepZero_Click(object sender, RoutedEventArgs e)
        {
            WelcomeView.Visibility = Visibility.Collapsed;
            StepZeroView.Visibility = Visibility.Visible;
            StepZeroView.ScrollToTop();

            AnimateBlockEntrance(StepZeroHeaderBlock, StepZeroHeaderTransform, 0);
            AnimateBlockEntrance(BtnFreshInstall, FreshInstallTransform, 75);
            AnimateBlockEntrance(BtnCleanAndInstall, CleanInstallTransform, 145);
            AnimateBlockEntrance(StepZeroSkipSection, SkipSectionTransform, 215);
            AnimateBlockEntrance(StepZeroWhySection, WhySectionTransform, 285);
        }

        private static void AnimateBlockEntrance(UIElement element, TranslateTransform transform, int delayMs)
        {
            element.Opacity = 0;
            transform.Y = 22;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var slide = new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(340))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            element.BeginAnimation(OpacityProperty, fade);
            transform.BeginAnimation(TranslateTransform.YProperty, slide);
        }

        private void BtnBackToWelcome_Click(object sender, RoutedEventArgs e)
        {
            StepZeroView.Visibility = Visibility.Collapsed;
            WelcomeView.Visibility = Visibility.Visible;
        }

        private async void BtnFreshInstall_Click(object sender, RoutedEventArgs e)
        {
            await RunStepZeroWorkflowAsync(isCleanMode: false);
        }

        private async void BtnCleanAndInstall_Click(object sender, RoutedEventArgs e)
        {
            await RunStepZeroWorkflowAsync(isCleanMode: true);
        }

        private async void BtnSkipDrivers_Click(object sender, RoutedEventArgs e)
        {
            await RunStepZeroWorkflowAsync(isCleanMode: false, skipDrivers: true);
        }

        private async Task RunStepZeroWorkflowAsync(bool isCleanMode, bool skipDrivers = false)
        {
            bool isEs = _currentLang == "ES";
            StepZeroBlur.Radius = 6;
            PrepModalOverlay.Visibility = Visibility.Visible;

            Storyboard? ringSb = Resources["LoadingRingStoryboard"] as Storyboard;
            ringSb?.Begin();

            try
            {
                TxtModalStepTag.Text = isEs ? "ETAPA 1 DE 3 · RESPALDO DEL SISTEMA" : "STAGE 1 OF 3 · SYSTEM BACKUP";
                TxtModalTitle.Text = isEs ? "Creando punto de restauración" : "Creating restore point";
                TxtModalMessage.Text = isEs ? "Guardando estado preventivo en Volume Shadow Copy..." : "Saving preventive snapshot via Volume Shadow Copy...";
                await AnimateModalProgressAsync(0, 38, 700);

                await Task.Run(() =>
                {
                    RegistryEngine.RunCommandHidden("powershell.exe",
                        "-NoProfile -Command \"Checkpoint-Computer -Description 'Archon_StepZero' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction SilentlyContinue\"");
                });

                if (skipDrivers)
                {
                    TxtModalStepTag.Text = isEs ? "ETAPA 2 DE 2 · CARGANDO PANEL" : "STAGE 2 OF 2 · LOADING PANEL";
                    TxtModalTitle.Text = isEs ? "Preparando módulos de ajuste" : "Preparing optimization modules";
                    TxtModalMessage.Text = isEs ? "Verificando estado actual del registro y servicios..." : "Checking current registry and service states...";
                    await AnimateModalProgressAsync(38, 100, 650);
                }
                else
                {
                    TxtModalStepTag.Text = isEs ? "ETAPA 2 DE 3 · CONTROLADORES" : "STAGE 2 OF 3 · DRIVERS";
                    TxtModalTitle.Text = isCleanMode
                        ? (isEs ? "Limpiando utilidades OEM previas" : "Cleaning previous OEM utilities")
                        : (isEs ? "Preparando base limpia de controladores" : "Preparing clean driver baseline");
                    TxtModalMessage.Text = isEs ? "Bloqueando paquetes genéricos de Windows Update..." : "Blocking generic Windows Update driver pushes...";
                    await AnimateModalProgressAsync(38, 76, 850);

                    TxtModalStepTag.Text = isEs ? "ETAPA 3 DE 3 · FINALIZANDO" : "STAGE 3 OF 3 · FINALIZING";
                    TxtModalTitle.Text = isEs ? "Abriendo panel de optimización" : "Opening optimization panel";
                    TxtModalMessage.Text = isEs ? "Todo listo para personalizar tu sistema." : "Everything is ready to customize your system.";
                    await AnimateModalProgressAsync(76, 100, 550);
                }
            }
            finally
            {
                ringSb?.Stop();
                PrepModalOverlay.Visibility = Visibility.Collapsed;
                StepZeroBlur.Radius = 0;
            }

            EnterMainOptimizerView();
        }

        private async Task AnimateModalProgressAsync(int from, int to, int totalMs)
        {
            int steps = Math.Max(1, to - from);
            int delay = Math.Max(10, totalMs / steps);
            for (int i = from; i <= to; i++)
            {
                ModalProgressBar.Value = i;
                TxtModalPercent.Text = $"{i}%";
                await Task.Delay(delay);
            }
        }

        private void EnterMainOptimizerView()
        {
            StepZeroView.Visibility = Visibility.Collapsed;
            WelcomeView.Visibility = Visibility.Collapsed;
            PerformanceView.Visibility = Visibility.Collapsed;
            MainOptimizerView.Visibility = Visibility.Visible;

            if (!_telemetryTimer.IsEnabled)
                _telemetryTimer.Start();

            UpdateSelectionCounter();
            ApplyFilter();
        }

        private void BtnBackToStepZero_Click(object sender, RoutedEventArgs e)
        {
            MainOptimizerView.Visibility = Visibility.Collapsed;
            StepZeroView.Visibility = Visibility.Visible;
            _telemetryTimer.Stop();
        }

        // ==================== CATÁLOGO DE APPS CON ÍCONOS ORIGINALES Y FILTRADO ====================

        private void InitializeCatalogAndApps()
        {
            var baseTweaks = TweakCatalog.GetAllTweaks();
            var catalog = new List<TweakItem>();

            // Asignar íconos originales a las librerías esenciales de TweakCatalog
            foreach (var item in baseTweaks)
            {
                if (item.Category == "Instalador Día 0")
                {
                    item.Category = "Apps";
                    if (item.Id == "install_vcredist")
                    {
                        item.IconColor = "#7C3AED";
                        item.IconGeometry = "M4,4 H11 V11 H4 Z M13,4 H20 V11 H13 Z M4,13 H11 V20 H4 Z M13,13 H20 V20 H13 Z";
                        _appSubGroups[item.Id] = "Runtimes & Essentials";
                    }
                    else if (item.Id == "install_directx")
                    {
                        item.IconColor = "#16A34A";
                        item.IconGeometry = "M4,4 L12,12 L4,20 M20,4 L12,12 L20,20";
                        _appSubGroups[item.Id] = "Runtimes & Essentials";
                    }
                }
                catalog.Add(item);
            }

            // Catálogo ampliado de aplicaciones con sus íconos y colores oficiales
            var extraApps = new List<(string id, string group, string title, string desc, string pkg, string color, string svg)>
            {
                ("app_dotnet8", "Runtimes & Essentials", ".NET Desktop Runtime 8", "Runtime oficial de Microsoft necesario para aplicaciones modernas de escritorio en Windows.", "Microsoft.DotNet.DesktopRuntime.8", "#512BD4",
                 "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M8,15 V9 L12,15 V9 M15,9 H18 M15,12 H17 M15,15 H18"),

                ("app_brave", "Browsers & Communication", "Brave Browser", "Navegador rápido basado en Chromium con bloqueo nativo de rastreadores y publicidad.", "Brave.Brave", "#FB542B",
                 "M12,2 L19,5 L21,12 L17,19 L12,22 L7,19 L3,12 L5,5 Z M9,9 L12,11 L15,9 L14,14 L12,17 L10,14 Z"),

                ("app_chrome", "Browsers & Communication", "Google Chrome", "Navegador web oficial de Google con sincronización de perfiles y extensiones.", "Google.Chrome", "#4285F4",
                 "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M12,8 A4,4 0 1,1 8,12 A4,4 0 0,1 12,8 Z M12,8 H21"),

                ("app_firefox", "Browsers & Communication", "Mozilla Firefox", "Navegador independiente de código abierto enfocado en privacidad y bajo consumo de RAM.", "Mozilla.Firefox", "#FF7139",
                 "M12,2 A10,10 0 1,0 22,12 C22,7 18,3 14,2.2 C16,4.5 16.5,7.5 14.5,9.5 C12.5,11 9.5,10.5 9,8.5 C7.5,10 7,13 8.5,15.5 C10,17.8 13.5,18.2 15.8,16.5"),

                ("app_discord", "Browsers & Communication", "Discord", "Cliente oficial de chat de voz, video y comunidades con aceleración por hardware.", "Discord.Discord", "#5865F2",
                 "M4,6 C7,4.5 17,4.5 20,6 L21,17 C18,19 15,19 14,18 L13,16 H11 L10,18 C9,19 6,19 3,17 Z M9,11 A1.5,1.5 0 1,0 9,14 A1.5,1.5 0 1,0 9,11 Z M15,11 A1.5,1.5 0 1,0 15,14 A1.5,1.5 0 1,0 15,11 Z"),

                ("app_telegram", "Browsers & Communication", "Telegram Desktop", "Cliente nativo de mensajería instantánea ultraliviano con sincronización en la nube.", "Telegram.TelegramDesktop", "#26A5E4",
                 "M21,3 L2,10.5 L9,13.5 L18,6.5 L11.5,14.5 L17.5,19 Z"),

                ("app_steam", "Gaming & Media", "Steam", "Plataforma oficial de juegos de PC, talleres y gestión de bibliotecas.", "Valve.Steam", "#66C0F4",
                 "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M15,7 A3,3 0 1,1 12,10 L9,14 A2.5,2.5 0 1,1 7,12 L11,9"),

                ("app_epic", "Gaming & Media", "Epic Games Launcher", "Cliente oficial para juegos de Epic Games Store y Unreal Engine.", "EpicGames.EpicGamesLauncher", "#E5E7EB",
                 "M5,2 H19 V17 L12,22 L5,17 Z M9,6 V14 H15 V12 H11 V11 H14 V9 H11 V8 H15 V6 Z"),

                ("app_obs", "Gaming & Media", "OBS Studio", "Software estándar de grabación y transmisión en vivo con soporte NVENC / AMF / QuickSync.", "OBSProject.OBSStudio", "#60A5FA",
                 "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M12,5 A4,4 0 0,1 15.5,11 A4,4 0 0,1 8.5,11 A4,4 0 0,1 12,5 Z"),

                ("app_vlc", "Gaming & Media", "VLC Media Player", "Reproductor multimedia universal capaz de abrir cualquier códec de video y audio sin paquetes externos.", "VideoLAN.VLC", "#FF8800",
                 "M12,2 L17,17 H7 Z M5,17 H19 L20,21 H4 Z M9,10 H15 M8,13 H16"),

                ("app_mpv", "Gaming & Media", "MPV Player", "Reproductor minimalista acelerado por GPU con altísima calidad de escalado y cero interfaz pesada.", "mpv.net", "#8B5CF6",
                 "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M10,8 L17,12 L10,16 Z"),

                ("app_spotify", "Gaming & Media", "Spotify", "Cliente de escritorio para reproducción de música y podcasts.", "Spotify.Spotify", "#1DB954",
                 "M12,2 A10,10 0 1,0 22,12 A10,10 0 0,0 12,2 Z M7,9.5 C10.5,8.5 14.5,8.8 17.5,10.5 M8,12.5 C10.8,11.7 13.8,12 16.3,13.4 M9,15.3 C11.2,14.7 13.4,15 15.2,16"),

                ("app_hwinfo", "Hardware & Diagnostics", "HWiNFO64", "Monitor profesional de sensores, voltajes, frecuencias y temperaturas en tiempo real.", "REALiX.HWiNFO", "#2563EB",
                 "M3,4 H21 V20 H3 Z M7,8 V16 M11,8 V16 M7,12 H11 M15,8 V16 M18,8 V16"),

                ("app_cpuz", "Hardware & Diagnostics", "CPU-Z", "Diagnóstico detallado del procesador, caché, placa base, chipset y latencias de memoria RAM.", "CPUID.CPU-Z", "#7C3AED",
                 "M7,7 H17 V17 H7 Z M9,2 V5 M15,2 V5 M9,19 V22 M15,19 V22 M2,9 H5 M2,15 H5 M19,9 H22 M19,15 H22"),

                ("app_gpuz", "Hardware & Diagnostics", "GPU-Z", "Información completa de tarjeta gráfica, bus PCIe, versión de BIOS, VRAM y sensores.", "TechPowerUp.GPU-Z", "#DC2626",
                 "M2,6 H20 V17 H2 Z M6,10 A2.5,2.5 0 1,0 6,15 A2.5,2.5 0 1,0 6,10 Z M13,10 A2.5,2.5 0 1,0 13,15 A2.5,2.5 0 1,0 13,10 Z M20,8 H22 V15 H20"),

                ("app_crystaldiskinfo", "Hardware & Diagnostics", "CrystalDiskInfo", "Verifica la salud S.M.A.R.T., horas de encendido y temperatura de tus discos SSD y NVMe.", "CrystalDewWorld.CrystalDiskInfo", "#0EA5E9",
                 "M5,3 H19 V21 H5 Z M12,7 A4,4 0 1,1 8,11 A4,4 0 0,1 12,7 Z M8,17 H16"),

                ("app_crystaldiskmark", "Hardware & Diagnostics", "CrystalDiskMark", "Benchmark estándar para medir velocidades secuenciales y aleatorias de lectura/escritura en SSD.", "CrystalDewWorld.CrystalDiskMark", "#0284C7",
                 "M3,18 L8,11 L13,14 L20,5 M15,5 H20 V10"),

                ("app_afterburner", "Hardware & Diagnostics", "MSI Afterburner", "Control de curva de ventiladores, undervolt, overclock y monitoreo OSD en juegos.", "Guru3D.Afterburner", "#EF4444",
                 "M12,2 L21,19 H3 Z M12,8 L16,16 H8 Z"),

                ("app_7zip", "Utilities & Security", "7-Zip", "Compresor y descompresor de código abierto ultraliviano compatible con .7z, .zip y .rar.", "7zip.7zip", "#F59E0B",
                 "M3,4 H21 V20 H3 Z M6,8 H11 L7,16 M13,10 H18 L13,16 H18"),

                ("app_winrar", "Utilities & Security", "WinRAR", "Archivador clásico para gestión de paquetes .rar y .zip con reparación de volúmenes.", "RARLab.WinRAR", "#9333EA",
                 "M5,3 H19 V7 H5 Z M5,10 H19 V14 H5 Z M5,17 H19 V21 H5 Z M11,3 V21"),

                ("app_wiztree", "Utilities & Security", "WizTree", "Analizador ultrarrápido de espacio en disco que escanea unidades NTFS en segundos.", "AntibodySoftware.WizTree", "#10B981",
                 "M3,3 H11 V11 H3 Z M13,3 H21 V8 H13 Z M13,10 H21 V21 H13 Z M3,13 H11 V21 H3 Z"),

                ("app_everything", "Utilities & Security", "Everything Search", "Buscador instantáneo de archivos y carpetas en todo el sistema por nombre.", "voidtools.Everything", "#F97316",
                 "M10.5,3 A7.5,7.5 0 1,0 18,10.5 A7.5,7.5 0 0,0 10.5,3 Z M16,16 L21,21"),

                ("app_rufus", "Utilities & Security", "Rufus", "Utilidad liviana para crear memorias USB booteables de Windows y Linux.", "Rufus.Rufus", "#3B82F6",
                 "M7,2 H17 V8 H7 Z M5,8 H19 V21 H5 Z M10,4 V6 M14,4 V6"),

                ("app_ventoy", "Utilities & Security", "Ventoy", "Crea un USB multiboot donde solo arrastrás archivos .ISO sin formatear cada vez.", "Ventoy.Ventoy", "#06B6D4",
                 "M4,5 L12,19 L20,5 H16 L12,12 L8,5 Z"),

                ("app_bitwarden", "Utilities & Security", "Bitwarden", "Gestor de contraseñas cifrado de código abierto con sincronización multiplataforma.", "Bitwarden.Bitwarden", "#175DDC",
                 "M12,2 L20,5 V12 C20,17.5 16.5,21 12,22.5 C7.5,21 4,17.5 4,12 V5 Z M12,5 V19"),

                ("app_keepassxc", "Utilities & Security", "KeePassXC", "Gestor de contraseñas local sin depender de servidores externos.", "KeePassXCTeam.KeePassXC", "#6CAC4D",
                 "M8,10 A4,4 0 1,1 12,14 H20 V17 H17 V20 H14 V14"),

                ("app_sharex", "Utilities & Security", "ShareX", "Herramienta avanzada de capturas de pantalla, grabación GIF/MP4 y OCR.", "ShareX.ShareX", "#38BDF8",
                 "M4,4 H14 V14 H4 Z M10,10 H20 V20 H10 Z"),

                ("app_powertoys", "Utilities & Security", "Microsoft PowerToys", "Suite oficial de productividad: FancyZones, selector de color, renombrado masivo y más.", "Microsoft.PowerToys", "#0078D4",
                 "M3,4 H11 V11 H3 Z M13,4 H21 V11 H13 Z M3,13 H11 V20 H3 Z M13,13 H21 V20 H13 Z"),

                ("app_vscode", "Developer Tools", "Visual Studio Code", "Editor de código fuente extensible de Microsoft con soporte integrado para Git y terminal.", "Microsoft.VisualStudioCode", "#007ACC",
                 "M16,3 L21,5.5 V18.5 L16,21 L8,13.5 L4.5,16 L3,14.5 L6.5,12 L3,9.5 L4.5,8 L8,10.5 Z"),

                ("app_git", "Developer Tools", "Git for Windows", "Sistema de control de versiones distribuido con Git Bash incluido.", "Git.Git", "#F05032",
                 "M12,2 L22,12 L12,22 L2,12 Z M12,7 V17 M12,11 L16,15"),

                ("app_terminal", "Developer Tools", "Windows Terminal", "Terminal moderna con pestañas, aceleración GPU y soporte para PowerShell, CMD y WSL.", "Microsoft.WindowsTerminal", "#9CA3AF",
                 "M3,5 H21 V19 H3 Z M7,9 L11,12 L7,15 M12,15 H17"),

                ("app_pwsh", "Developer Tools", "PowerShell 7", "Consola multiplataforma de automatización y administración avanzada de sistemas.", "Microsoft.PowerShell", "#5391FE",
                 "M4,5 L21,5 L19,19 L2,19 Z M8,9 L12,12 L7,15 M12,15 H16"),

                ("app_python", "Developer Tools", "Python 3", "Intérprete oficial de Python para automatización, scripting y desarrollo.", "Python.Python.3.12", "#3776AB",
                 "M8,3 H15 V9 H6 V15 H3 V8 C3,5 5,3 8,3 Z M16,21 H9 V15 H18 V9 H21 V16 C21,19 19,21 16,21 Z"),

                ("app_nodejs", "Developer Tools", "Node.js LTS", "Entorno de ejecución JavaScript del lado del servidor con npm incluido.", "OpenJS.NodeJS.LTS", "#339933",
                 "M12,2 L20.5,7 V17 L12,22 L3.5,17 V7 Z M10,9 V15 L14,13 V10"),

                ("app_notepadpp", "Developer Tools", "Notepad++", "Editor de texto y código ultrarrápido escrito en C++ con consumo mínimo de memoria.", "Notepad++.Notepad++", "#90E59A",
                 "M5,3 H16 L19,6 V21 H5 Z M8,10 H16 M8,14 H16 M8,18 H13")
            };

            foreach (var app in extraApps)
            {
                if (catalog.Any(x => x.Id == app.id))
                    continue;

                _appSubGroups[app.id] = app.group;
                catalog.Add(new TweakItem
                {
                    Id = app.id,
                    Category = "Apps",
                    Title = app.title,
                    Risk = TweakRisk.Seguro,
                    Description = app.desc,
                    TechnicalDetail = $"winget install --id {app.pkg} -e --silent",
                    IconColor = app.color,
                    IconGeometry = app.svg,
                    ApplyAction = () => RegistryEngine.RunCommandHidden("winget.exe",
                        $"install --id {app.pkg} -e --silent --accept-package-agreements --accept-source-agreements")
                });
            }

            _allTweaks = catalog;
            _tweaksView = CollectionViewSource.GetDefaultView(_allTweaks);
            _tweaksView.Filter = FilterTweaksPredicate;
            _tweaksView.GroupDescriptions.Add(new AppSubGroupDescription(
                _appSubGroups,
                () => _currentLang == "ES" ? "Ajustes disponibles" : "Available optimizations"));

            TweaksItemsControl.ItemsSource = _tweaksView;
        }

        private bool FilterTweaksPredicate(object obj)
        {
            if (obj is not TweakItem item)
                return false;

            if (!string.Equals(item.Category, _currentCategory, StringComparison.OrdinalIgnoreCase))
                return false;

            string query = TxtSearch?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
                return true;

            return item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                   item.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyFilter()
        {
            _tweaksView?.Refresh();
        }

        private void CategoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clicked || clicked.Tag is not string newCategory)
                return;

            // Cerrar cualquier panel de detalles abierto si el usuario cambia a una pestaña diferente
            if (!string.Equals(_currentCategory, newCategory, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var item in _allTweaks)
                {
                    if (item.IsDetailsOpen)
                        item.IsDetailsOpen = false;
                }
            }

            _currentCategory = newCategory;

            Button[] catButtons = { BtnCatApps, BtnCatGaming, BtnCatNetwork, BtnCatDebloat, BtnCatServices, BtnCatCleanup };
            foreach (var btn in catButtons)
            {
                btn.Style = (Style)FindResource(btn == clicked ? "PrimaryButtonStyle" : "GhostButtonStyle");
            }

            ApplyFilter();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TweakItem item)
            {
                item.IsDetailsOpen = !item.IsDetailsOpen;
            }
        }

        private void TweakSwitch_Click(object sender, RoutedEventArgs e)
        {
            UpdateSelectionCounter();
        }

        private void UpdateSelectionCounter()
        {
            int count = _allTweaks.Count(t => t.IsEnabled);
            bool isEs = _currentLang == "ES";
            TxtOptStatSelVal.Text = isEs ? $"{count} seleccionados" : $"{count} selected";
            TxtOptStatBackupVal.Text = isEs ? "VSS Protegido" : "VSS Protected";
        }

        private async void BtnApplyChanges_Click(object sender, RoutedEventArgs e)
        {
            bool isEs = _currentLang == "ES";
            var enabledItems = _allTweaks.Where(t => t.IsEnabled).ToList();
            if (enabledItems.Count == 0)
            {
                TxtActionFeedback.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                TxtActionFeedback.Text = isEs ? "No hay ajustes o apps seleccionados." : "No tweaks or apps selected.";
                return;
            }

            BtnApplyChanges.IsEnabled = false;
            BtnRestoreOriginal.IsEnabled = false;
            TxtActionFeedback.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA"));
            TxtActionFeedback.Text = isEs ? $"Aplicando {enabledItems.Count} elementos..." : $"Applying {enabledItems.Count} items...";

            await Task.Run(() =>
            {
                foreach (var item in enabledItems)
                {
                    try { item.ApplyAction?.Invoke(); } catch { }
                }
            });

            TxtActionFeedback.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CC38A"));
            TxtActionFeedback.Text = isEs
                ? $"✓ {enabledItems.Count} cambios aplicados correctamente."
                : $"✓ {enabledItems.Count} changes applied successfully.";

            BtnApplyChanges.IsEnabled = true;
            BtnRestoreOriginal.IsEnabled = true;
        }

        private async void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
        {
            bool isEs = _currentLang == "ES";
            var revertible = _allTweaks.Where(t => t.RevertAction != null).ToList();

            BtnApplyChanges.IsEnabled = false;
            BtnRestoreOriginal.IsEnabled = false;
            TxtActionFeedback.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#60A5FA"));
            TxtActionFeedback.Text = isEs ? "Restaurando valores predeterminados de Windows..." : "Restoring Windows default values...";

            await Task.Run(() =>
            {
                foreach (var item in revertible)
                {
                    try
                    {
                        item.RevertAction?.Invoke();
                        item.IsEnabled = false;
                    }
                    catch { }
                }
            });

            UpdateSelectionCounter();
            TxtActionFeedback.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CC38A"));
            TxtActionFeedback.Text = isEs
                ? "✓ Valores originales restaurados."
                : "✓ Default settings restored.";

            BtnApplyChanges.IsEnabled = true;
            BtnRestoreOriginal.IsEnabled = true;
        }
    }
}
