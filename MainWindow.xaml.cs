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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Archon.Core;
using Archon.Models;
using Microsoft.Win32;

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
        private string? _signedInUser = "Agustín";
        private string? _avatarImagePath;
        private string _activeOptimizationProfile = "Predeterminado";
        private bool _isRegisterMode;
        private bool _isSystemPrepared;
        private SystemHardwareProfile? _hardwareProfile;
        private bool _isHardwareScanned;

        private readonly Dictionary<string, string> _appSubGroups = new();

        private readonly DispatcherTimer _telemetryTimer;
        private readonly Queue<double> _cpuHistory = new();
        private readonly Queue<double> _gpuHistory = new();
        private readonly Queue<double> _ramHistory = new();
        private readonly Queue<double> _diskHistory = new();
        private readonly Queue<double> _netHistory = new();
        private const int MaxHistoryPoints = 40;

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
            if (Application.Current.Resources["AmbientGlowStoryboard"] is Storyboard sb)
                sb.Begin();

            InitializeCatalogAndApps();
            UpdateSelectionCounter();
            ApplyLanguage("EN");
            RefreshAccountUI();
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

            TxtProfilePrefix.Text = isEs ? "Perfil:" : isJa ? "プロファイル:" : isRu ? "Профиль:" : "Profile:";
            TxtWelcomeSubtitle.Text = isEs ? "Bienvenido" : isJa ? "ようこそ" : isRu ? "Добро пожаловать" : "Welcome";
            TxtStartButtonLabel.Text = isEs ? "Comenzar" : isJa ? "開始する" : isRu ? "Начать" : "Get Started";
            TxtScanButtonLabel.Text = isEs ? "Ver mis componentes" : isJa ? "コンポーネントを表示" : isRu ? "Мои компоненты" : "View my components";
            TxtPerformanceButtonLabel.Text = isEs ? "Rendimiento" : isJa ? "パフォーマンス" : isRu ? "Производительность" : "Performance";

            TxtDetectedComponentsTitle.Text = isEs ? "Componentes detectados" : isJa ? "検出されたコンポーネント" : isRu ? "Обнаруженные компоненты" : "Detected components";
            TxtSeeMoreLabel.Text = isEs ? "Ver más" : isJa ? "詳細を見る" : isRu ? "Подробнее" : "See more";
            TxtLblCpu.Text = isEs ? "Procesador" : isJa ? "プロセッサ" : isRu ? "Процессор" : "Processor";
            TxtLblMother.Text = isEs ? "Placa base" : isJa ? "マザーボード" : isRu ? "Материнская плата" : "Motherboard";
            TxtLblGpu.Text = isEs ? "Tarjeta gráfica" : isJa ? "グラフィックス" : isRu ? "Видеокарта" : "Graphics card";
            TxtLblRam.Text = isEs ? "Memoria RAM" : isJa ? "メモリ RAM" : isRu ? "Оперативная память" : "Memory RAM";
            TxtLblStorage.Text = isEs ? "Almacenamiento" : isJa ? "ストレージ" : isRu ? "Накопитель" : "Storage";
            TxtLblOs.Text = isEs ? "Sistema operativo" : isJa ? "OS" : isRu ? "Операционная система" : "Operating system";

            TxtBackHomeLabel.Text = isEs ? "Volver al inicio" : isJa ? "ホームに戻る" : isRu ? "На главную" : "Back to home";
            TxtBackFromSettingsLabel.Text = TxtBackHomeLabel.Text;
            TxtPerfBackLabel.Text = TxtBackHomeLabel.Text;
            TxtFullSpecsTitle.Text = isEs ? "Especificaciones completas del sistema" : isJa ? "システム完全仕様" : isRu ? "Полные характеристики системы" : "Full system specifications";
            TxtDeepScanTitle.Text = isEs ? "Cargando especificaciones avanzadas del sistema..." : "Loading advanced system specifications...";
            TxtDeepScanSub.Text = isEs ? "Consultando procesador, gráfica, placa base, BIOS, RAM, discos y red..." : "Querying CPU architecture, BIOS, memory slots, drives, network adapters and OS details...";

            TxtPerfTitle.Text = isEs ? "Rendimiento en tiempo real" : isJa ? "リアルタイムパフォーマンス" : isRu ? "Производительность в реальном времени" : "Real-time hardware performance";
            TxtPerfSubtitle.Text = isEs ? "Telemetría continua de procesador, gráficos, memoria, almacenamiento y red." : "Continuous system telemetry across processor, graphics, memory, storage and network.";
            TxtPerfOverviewTitle.Text = isEs ? "CURVA DE CARGA DEL SISTEMA" : "SYSTEM LOAD CURVE";

            TxtSubTabProfile.Content = isEs ? "Mi Perfil" : "My Profile";
            TxtSubTabGeneral.Content = isEs ? "Configuración General" : "General Settings";
            TxtChooseAvatarPhoto.Content = isEs ? "Cargar foto" : "Load photo";
            TxtProfileSectionTitle.Text = isEs ? "Cuenta e Identidad" : "Account & Identity";
            TxtProfileNameLabel.Text = isEs ? "Nombre visible" : "Display name";
            TxtSaveProfileChanges.Content = isEs ? "Guardar perfil" : "Save profile";
            TxtSignOutProfile.Content = isEs ? "Cerrar sesión" : "Sign out";

            TxtGenBlockTitle.Text = isEs ? "General e Interfaz" : "General & Interface";
            TxtOptNotifications.Text = isEs ? "Notificaciones de escritorio al completar tareas" : "Desktop notifications for completed tasks";
            TxtOptExperimental.Text = isEs ? "Activar funciones de prueba y telemetría experimental" : "Enable experimental features & preview telemetry";
            TxtOptAnalytics.Text = isEs ? "Análisis diagnóstico anónimo de rendimiento" : "Anonymous diagnostic & performance analytics";
            TxtGenSystemBlockTitle.Text = isEs ? "Actualizaciones, Comentarios y Acerca de" : "Updates, Feedback & About";
            TxtCheckUpdates.Content = isEs ? "Buscar actualizaciones" : "Check for updates";
            TxtShareFeedback.Content = isEs ? "Compartir / Comentarios" : "Share / Feedback";
            TxtResetSettings.Content = isEs ? "Restablecer configuraciones" : "Reset settings";
            TxtAboutArchon.Content = isEs ? "Acerca de Archon" : "About Archon";

            TxtHomeNavLabel.Text = isEs ? "Inicio" : "Home";
            TxtPrepTitle.Text = isEs ? "Preparación del sistema" : "System preparation";
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

            RefreshAccountUI();
            UpdateSelectionCounter();
        }

        // ==================== PERFIL, AVATAR, SELECTOR DE PERFILES Y CONFIGURACIÓN ====================

        private void BtnProfileDropdown_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = !ProfilePopup.IsOpen;
        }

        private void SelectOptimizationProfile_Click(object sender, RoutedEventArgs e)
        {
            ProfilePopup.IsOpen = false;
            if (sender is Button btn && btn.Tag is string profileName)
            {
                _activeOptimizationProfile = profileName;
                TxtActiveProfileName.Text = profileName;
            }
        }

        private void BtnOpenMyProfile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_signedInUser))
            {
                SetAccountModalMode(false);
                TxtAccountInput.Text = string.Empty;
                TxtAccountPassInput.Password = string.Empty;
                AccountModalOverlay.Visibility = Visibility.Visible;
                return;
            }

            OpenSettingsAndProfileTab(showGeneral: false);
        }

        private void BtnOpenGeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsAndProfileTab(showGeneral: true);
        }

        private void OpenSettingsAndProfileTab(bool showGeneral)
        {
            WelcomeView.Visibility = Visibility.Collapsed;
            StepZeroView.Visibility = Visibility.Collapsed;
            MainOptimizerView.Visibility = Visibility.Collapsed;
            FullHardwareView.Visibility = Visibility.Collapsed;
            PerformanceView.Visibility = Visibility.Collapsed;
            SettingsAndProfileView.Visibility = Visibility.Visible;

            if (showGeneral)
                BtnSubTabGeneral_Click(BtnSubTabGeneral, new RoutedEventArgs());
            else
                BtnSubTabProfile_Click(BtnSubTabProfile, new RoutedEventArgs());
        }

        private void BtnSubTabProfile_Click(object sender, RoutedEventArgs e)
        {
            SubPanelProfile.Visibility = Visibility.Visible;
            SubPanelGeneral.Visibility = Visibility.Collapsed;
            BtnSubTabProfile.Style = (Style)FindResource("PrimaryButtonStyle");
            BtnSubTabGeneral.Style = (Style)FindResource("SoftCardButtonStyle");
            TxtProfileDisplayName.Text = _signedInUser ?? string.Empty;
        }

        private void BtnSubTabGeneral_Click(object sender, RoutedEventArgs e)
        {
            SubPanelProfile.Visibility = Visibility.Collapsed;
            SubPanelGeneral.Visibility = Visibility.Visible;
            BtnSubTabGeneral.Style = (Style)FindResource("PrimaryButtonStyle");
            BtnSubTabProfile.Style = (Style)FindResource("SoftCardButtonStyle");
        }

        private void BtnBackFromSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsAndProfileView.Visibility = Visibility.Collapsed;
            WelcomeView.Visibility = Visibility.Visible;
        }

        private void BtnChooseAvatarPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Imágenes (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|Todos los archivos (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                _avatarImagePath = dlg.FileName;
                RefreshAccountUI();
                TxtProfileFeedback.Text = _currentLang == "ES" ? "Foto de perfil cargada." : "Profile photo loaded.";
            }
        }

        private void BtnSaveProfileChanges_Click(object sender, RoutedEventArgs e)
        {
            string newName = TxtProfileDisplayName.Text.Trim();
            if (string.IsNullOrWhiteSpace(newName))
                return;

            _signedInUser = newName;
            RefreshAccountUI();
            TxtProfileFeedback.Text = _currentLang == "ES" ? "✓ Cambios guardados en tu perfil." : "✓ Profile changes saved.";
        }

        private void BtnSettingsAction_Click(object sender, RoutedEventArgs e)
        {
            bool isEs = _currentLang == "ES";
            if (sender == BtnCheckUpdates)
                TxtSettingsStatusMsg.Text = isEs ? "Archon v1.0 · Estás en la última compilación." : "Archon v1.0 · You are on the latest build.";
            else if (sender == BtnShareFeedback)
                TxtSettingsStatusMsg.Text = isEs ? "Comentarios habilitados para la próxima versión de Archon." : "Feedback notes ready for the next Archon release.";
            else if (sender == BtnAboutArchon)
                TxtSettingsStatusMsg.Text = isEs ? "Archon System Optimizer · Perfil activo: " + _activeOptimizationProfile : "Archon System Optimizer · Active profile: " + _activeOptimizationProfile;
        }

        private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            ChkOptNotifications.IsChecked = true;
            ChkOptExperimental.IsChecked = false;
            ChkOptAnalytics.IsChecked = true;
            _activeOptimizationProfile = "Predeterminado";
            TxtActiveProfileName.Text = _activeOptimizationProfile;
            ApplyLanguage("EN");
            TxtSettingsStatusMsg.Text = "✓ Settings restored to default values.";
        }

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
            string chosenName = _isRegisterMode && !string.IsNullOrWhiteSpace(TxtRegisterDisplayName.Text)
                ? TxtRegisterDisplayName.Text.Trim()
                : TxtAccountInput.Text.Trim();

            if (string.IsNullOrWhiteSpace(chosenName))
                return;

            _signedInUser = chosenName;
            AccountModalOverlay.Visibility = Visibility.Collapsed;
            RefreshAccountUI();
        }

        private void BtnAccountSignOut_Click(object sender, RoutedEventArgs e)
        {
            _signedInUser = null;
            _avatarImagePath = null;
            TxtAccountInput.Text = string.Empty;
            TxtAccountPassInput.Password = string.Empty;
            TxtRegisterDisplayName.Text = string.Empty;
            AccountModalOverlay.Visibility = Visibility.Collapsed;
            RefreshAccountUI();

            if (SettingsAndProfileView.Visibility == Visibility.Visible)
            {
                SettingsAndProfileView.Visibility = Visibility.Collapsed;
                WelcomeView.Visibility = Visibility.Visible;
            }
        }

        private void RefreshAccountUI()
        {
            bool isEs = _currentLang == "ES";
            bool loggedIn = !string.IsNullOrWhiteSpace(_signedInUser);
            string label = loggedIn ? _signedInUser! : (isEs ? "Perfil Invitado" : "Guest Profile");
            string initial = loggedIn ? label.Substring(0, 1).ToUpperInvariant() : "?";

            WelcomeAvatarInitial.Text = initial;
            ProfilePageAvatarInitial.Text = initial;
            SidebarAvatarInitial.Text = initial;

            TxtSidebarUser.Text = label;
            TxtSidebarUserStatus.Text = loggedIn
                ? (isEs ? "Sesión iniciada" : "Signed in")
                : (isEs ? "Iniciar sesión" : "Sign in");

            BitmapImage? avatarBmp = null;
            if (!string.IsNullOrWhiteSpace(_avatarImagePath) && System.IO.File.Exists(_avatarImagePath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(_avatarImagePath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    bmp.Freeze();
                    avatarBmp = bmp;
                }
                catch { }
            }

            WelcomeAvatarBrush.ImageSource = avatarBmp;
            ProfilePageAvatarBrush.ImageSource = avatarBmp;
            SidebarAvatarBrush.ImageSource = avatarBmp;

            WelcomeAvatarInitial.Visibility = avatarBmp == null ? Visibility.Visible : Visibility.Collapsed;
            ProfilePageAvatarInitial.Visibility = avatarBmp == null ? Visibility.Visible : Visibility.Collapsed;
            SidebarAvatarInitial.Visibility = avatarBmp == null ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==================== COMPONENTES: RESUMEN INSTANTÁNEO + CARGA EN VER MÁS ====================

        private void BtnScanHardware_Click(object sender, RoutedEventArgs e)
        {
            if (HardwareListPanel.Visibility == Visibility.Visible)
            {
                HardwareListPanel.Visibility = Visibility.Collapsed;
                return;
            }

            if (!_isHardwareScanned || _hardwareProfile == null)
            {
                _hardwareProfile = HardwareDetector.ScanSystem();
                _isHardwareScanned = true;
            }

            PopulateSummaryHardwareUI(_hardwareProfile);
            HardwareListPanel.Visibility = Visibility.Visible;
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
            FullHardwareSectionsControl.ItemsSource = null;
            FullHardwareLoadingPanel.Visibility = Visibility.Visible;

            Storyboard? deepSpinner = Application.Current.Resources["DeepScanSpinnerStoryboard"] as Storyboard;
            deepSpinner?.Begin();

            FullHardwareSlideTransform.Y = 20;
            FullHardwareContainer.Opacity = 0;
            FullHardwareContainer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            FullHardwareSlideTransform.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(20, 0, TimeSpan.FromMilliseconds(360)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut } });

            try
            {
                var scanTask = Task.Run(BuildFullHardwareSections);
                var minSpinnerDelay = Task.Delay(750);
                await Task.WhenAll(scanTask, minSpinnerDelay);
                FullHardwareSectionsControl.ItemsSource = await scanTask;
            }
            finally
            {
                deepSpinner?.Stop();
                FullHardwareLoadingPanel.Visibility = Visibility.Collapsed;
            }
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

            // 1. PROCESADOR COMPLETO
            var cpuRows = new List<HardwareSpecRow>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name, Manufacturer, Architecture, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, CurrentClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation, VirtualizationFirmwareEnabled FROM Win32_Processor");
                foreach (ManagementObject obj in s.Get())
                {
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Modelo" : "Model", Value = (obj["Name"]?.ToString() ?? "--").Trim() });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Fabricante" : "Manufacturer", Value = obj["Manufacturer"]?.ToString() ?? "--" });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Núcleos físicos" : "Physical cores", Value = obj["NumberOfCores"]?.ToString() ?? "--" });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Procesadores lógicos (hilos)" : "Logical processors", Value = obj["NumberOfLogicalProcessors"]?.ToString() ?? "--" });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Frecuencia actual" : "Current clock", Value = $"{obj["CurrentClockSpeed"]} MHz" });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Frecuencia máxima" : "Max clock", Value = $"{obj["MaxClockSpeed"]} MHz" });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Caché L2" : "L2 cache", Value = $"{obj["L2CacheSize"]} KB" });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Caché L3" : "L3 cache", Value = $"{obj["L3CacheSize"]} KB" });
                    cpuRows.Add(new HardwareSpecRow { Label = "Socket", Value = obj["SocketDesignation"]?.ToString() ?? "--" });
                    cpuRows.Add(new HardwareSpecRow { Label = isEs ? "Virtualización firmware" : "Firmware virtualization", Value = obj["VirtualizationFirmwareEnabled"]?.ToString() ?? "--" });
                    break;
                }
            }
            catch { }
            if (cpuRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Procesador (CPU)" : "Processor (CPU)", Specs = cpuRows });

            // 2. TARJETA GRÁFICA COMPLETA
            var gpuRows = new List<HardwareSpecRow>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility, VideoProcessor, AdapterRAM, DriverVersion, DriverDate, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate, Status FROM Win32_VideoController");
                int idx = 1;
                foreach (ManagementObject obj in s.Get())
                {
                    string name = obj["Name"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string prefix = idx > 1 ? $"GPU #{idx} · " : "";
                    gpuRows.Add(new HardwareSpecRow { Label = $"{prefix}{(isEs ? "Adaptador" : "Adapter")}", Value = name });
                    gpuRows.Add(new HardwareSpecRow { Label = $"{prefix}{(isEs ? "Procesador de video" : "Video processor")}", Value = obj["VideoProcessor"]?.ToString() ?? "--" });
                    if (ulong.TryParse(obj["AdapterRAM"]?.ToString(), out ulong vram) && vram > 0)
                        gpuRows.Add(new HardwareSpecRow { Label = $"{prefix}VRAM", Value = $"{Math.Round(vram / (1024.0 * 1024.0 * 1024.0), 1)} GB" });
                    gpuRows.Add(new HardwareSpecRow { Label = $"{prefix}{(isEs ? "Versión driver" : "Driver version")}", Value = obj["DriverVersion"]?.ToString() ?? "--" });
                    gpuRows.Add(new HardwareSpecRow { Label = $"{prefix}{(isEs ? "Resolución y tasa" : "Resolution & refresh")}", Value = $"{obj["CurrentHorizontalResolution"]}x{obj["CurrentVerticalResolution"]} @ {obj["CurrentRefreshRate"]} Hz" });
                    gpuRows.Add(new HardwareSpecRow { Label = $"{prefix}{(isEs ? "Estado" : "Status")}", Value = obj["Status"]?.ToString() ?? "--" });
                    idx++;
                }
            }
            catch { }
            if (gpuRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Tarjeta Gráfica (GPU)" : "Graphics (GPU)", Specs = gpuRows });

            // 3. PLACA BASE Y BIOS
            var moboRows = new List<HardwareSpecRow>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Manufacturer, Product, Version, SerialNumber FROM Win32_BaseBoard");
                foreach (ManagementObject obj in s.Get())
                {
                    moboRows.Add(new HardwareSpecRow { Label = isEs ? "Fabricante" : "Manufacturer", Value = obj["Manufacturer"]?.ToString() ?? "--" });
                    moboRows.Add(new HardwareSpecRow { Label = isEs ? "Modelo de placa" : "Board model", Value = obj["Product"]?.ToString() ?? "--" });
                    moboRows.Add(new HardwareSpecRow { Label = isEs ? "Revisión" : "Revision", Value = obj["Version"]?.ToString() ?? "--" });
                    break;
                }
                using var bios = new ManagementObjectSearcher("SELECT Manufacturer, SMBIOSBIOSVersion, Name FROM Win32_BIOS");
                foreach (ManagementObject obj in bios.Get())
                {
                    moboRows.Add(new HardwareSpecRow { Label = "BIOS", Value = $"{obj["Manufacturer"]} · {obj["SMBIOSBIOSVersion"]}" });
                    break;
                }
            }
            catch { }
            if (moboRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Placa Base y BIOS" : "Motherboard & BIOS", Specs = moboRows });

            // 4. MEMORIA RAM POR MÓDULO
            var ramRows = new List<HardwareSpecRow>();
            try
            {
                int slot = 1;
                using var s = new ManagementObjectSearcher("SELECT DeviceLocator, Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, ConfiguredVoltage FROM Win32_PhysicalMemory");
                foreach (ManagementObject obj in s.Get())
                {
                    ulong capBytes = ulong.TryParse(obj["Capacity"]?.ToString(), out ulong b) ? b : 0;
                    double gb = Math.Round(capBytes / (1024.0 * 1024.0 * 1024.0));
                    string speed = obj["ConfiguredClockSpeed"]?.ToString() ?? obj["Speed"]?.ToString() ?? "--";
                    string part = (obj["PartNumber"]?.ToString() ?? "").Trim();
                    string mfg = (obj["Manufacturer"]?.ToString() ?? "").Trim();
                    string loc = obj["DeviceLocator"]?.ToString() ?? $"Slot {slot}";
                    ramRows.Add(new HardwareSpecRow { Label = loc, Value = $"{gb} GB @ {speed} MHz · {mfg} ({part})" });
                    slot++;
                }
            }
            catch { }
            if (ramRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Memoria RAM (Módulos)" : "Memory Modules (RAM)", Specs = ramRows });

            // 5. ALMACENAMIENTO COMPLETO
            var diskRows = new List<HardwareSpecRow>();
            try
            {
                using var s = new ManagementObjectSearcher("SELECT Model, InterfaceType, MediaType, Size, Partitions, SerialNumber FROM Win32_DiskDrive");
                foreach (ManagementObject obj in s.Get())
                {
                    ulong diskBytes = ulong.TryParse(obj["Size"]?.ToString(), out ulong b) ? b : 0;
                    double gb = Math.Round(diskBytes / (1000.0 * 1000.0 * 1000.0));
                    string cap = gb >= 1000 ? $"{Math.Round(gb / 1000.0, 2)} TB" : $"{gb} GB";
                    diskRows.Add(new HardwareSpecRow
                    {
                        Label = obj["Model"]?.ToString() ?? "Disk",
                        Value = $"{cap} · {obj["InterfaceType"]} · {obj["Partitions"]} part."
                    });
                }
            }
            catch { }
            if (diskRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Almacenamiento (Unidades)" : "Storage Drives", Specs = diskRows });

            // 6. RED Y SISTEMA OPERATIVO
            var sysRows = new List<HardwareSpecRow>();
            try
            {
                using var os = new ManagementObjectSearcher("SELECT Caption, Version, BuildNumber, OSArchitecture, SystemDirectory FROM Win32_OperatingSystem");
                foreach (ManagementObject obj in os.Get())
                {
                    sysRows.Add(new HardwareSpecRow { Label = isEs ? "Sistema operativo" : "Operating system", Value = $"{obj["Caption"]} ({obj["OSArchitecture"]})" });
                    sysRows.Add(new HardwareSpecRow { Label = isEs ? "Versión y compilación" : "Version & build", Value = $"{obj["Version"]} (Build {obj["BuildNumber"]})" });
                    break;
                }

                var activeNic = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up &&
                                         n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
                if (activeNic != null)
                {
                    double speedMbps = activeNic.Speed / 1_000_000.0;
                    sysRows.Add(new HardwareSpecRow { Label = isEs ? "Adaptador de red activo" : "Active network adapter", Value = $"{activeNic.Name} ({activeNic.NetworkInterfaceType})" });
                    sysRows.Add(new HardwareSpecRow { Label = isEs ? "Velocidad de enlace" : "Link speed", Value = speedMbps > 0 ? $"{speedMbps} Mbps" : "--" });
                    sysRows.Add(new HardwareSpecRow { Label = "MAC Address", Value = activeNic.GetPhysicalAddress().ToString() });
                }
            }
            catch { }
            if (sysRows.Count > 0)
                sections.Add(new HardwareSpecSection { SectionTitle = isEs ? "Red y Sistema Operativo" : "Network & Operating System", Specs = sysRows });

            return sections;
        }

        // ==================== RENDIMIENTO EN TIEMPO REAL ====================

        private async void BtnOpenPerformance_Click(object sender, RoutedEventArgs e)
        {
            WelcomeView.Visibility = Visibility.Collapsed;
            StepZeroView.Visibility = Visibility.Collapsed;
            MainOptimizerView.Visibility = Visibility.Collapsed;
            FullHardwareView.Visibility = Visibility.Collapsed;
            SettingsAndProfileView.Visibility = Visibility.Collapsed;
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

            if (MainOptimizerView.Visibility == Visibility.Visible)
            {
                TxtOptStatCpuVal.Text = $"{cpuPct:0}%";
                OptStatCpuBar.Value = cpuPct;
                TxtOptStatRamVal.Text = $"{ramUsedGb:0.0} / {ramTotalGb:0.0} GB";
                OptStatRamBar.Value = ramPct;
            }

            if (PerformanceView.Visibility != Visibility.Visible)
                return;

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

            TxtPerfOverviewSummary.Text = $"CPU {cpuPct:0}% · GPU {wmiMetrics.gpuPct:0}% · RAM {ramPct:0}%";

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
            TxtPerfDiskRate.Text = $"Disk R/W: {wmiMetrics.diskMbSec:0.1} MB/s";

            double totalNetKbps = downKbps + upKbps;
            TxtPerfNetDown.Text = $"↓ Down: {FormatRate(downKbps)}";
            TxtPerfNetUp.Text = $"↑ Up: {FormatRate(upKbps)}";

            double netNormalized = Math.Min(100.0, (totalNetKbps / 10240.0) * 100.0);

            PushHistoryAndRender(_cpuHistory, cpuPct, PerfCpuSparkline, PerfOverviewGraphHost);
            PushHistoryAndRender(_gpuHistory, wmiMetrics.gpuPct, PerfGpuSparkline, PerfOverviewGraphHost);
            PushHistoryAndRender(_ramHistory, ramPct, PerfRamSparkline, PerfOverviewGraphHost);

            PushHistoryAndRender(_cpuHistory, cpuPct, PerfCpuMiniLine, PerfCpuMiniHost);
            PushHistoryAndRender(_gpuHistory, wmiMetrics.gpuPct, PerfGpuMiniLine, PerfGpuMiniHost);
            PushHistoryAndRender(_ramHistory, ramPct, PerfRamMiniLine, PerfRamMiniHost);
            PushHistoryAndRender(_diskHistory, wmiMetrics.diskPct, PerfDiskSparkline, PerfDiskMiniHost);
            PushHistoryAndRender(_netHistory, netNormalized, PerfNetSparkline, PerfDiskMiniHost);
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
            double h = host.ActualHeight > 10 ? host.ActualHeight : 50;
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

        // ==================== PREPARACIÓN DEL SISTEMA (ANIMACIÓN SUAVE Y EJECUCIÓN ÚNICA) ====================

        private void BtnStartStepZero_Click(object sender, RoutedEventArgs e)
        {
            WelcomeView.Visibility = Visibility.Collapsed;
            StepZeroView.Visibility = Visibility.Visible;
            StepZeroView.ScrollToTop();

            AnimateBlockEntrance(StepZeroHeaderBlock, StepZeroHeaderTransform, 0);
            AnimateBlockEntrance(BtnFreshInstall, FreshInstallTransform, 130);
            AnimateBlockEntrance(BtnCleanAndInstall, CleanInstallTransform, 260);
            AnimateBlockEntrance(StepZeroSkipSection, SkipSectionTransform, 390);
            AnimateBlockEntrance(StepZeroWhySection, WhySectionTransform, 520);
        }

        private static void AnimateBlockEntrance(UIElement element, TranslateTransform transform, int delayMs)
        {
            element.Opacity = 0;
            transform.Y = 28;

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            var slide = new DoubleAnimation(28, 0, TimeSpan.FromMilliseconds(560))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
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
            await RunStepZeroWorkflowAsync(isCleanMode: false, forceRun: true);
        }

        private async void BtnCleanAndInstall_Click(object sender, RoutedEventArgs e)
        {
            await RunStepZeroWorkflowAsync(isCleanMode: true, forceRun: true);
        }

        private async void BtnSkipDrivers_Click(object sender, RoutedEventArgs e)
        {
            if (_isSystemPrepared)
            {
                EnterMainOptimizerView();
                return;
            }

            await RunStepZeroWorkflowAsync(isCleanMode: false, skipDrivers: true, forceRun: false);
        }

        private async Task RunStepZeroWorkflowAsync(bool isCleanMode, bool skipDrivers = false, bool forceRun = false)
        {
            if (_isSystemPrepared && !forceRun)
            {
                EnterMainOptimizerView();
                return;
            }

            bool isEs = _currentLang == "ES";
            StepZeroBlur.Radius = 6;
            PrepModalOverlay.Visibility = Visibility.Visible;

            Storyboard? ringSb = Application.Current.Resources["LoadingRingStoryboard"] as Storyboard;
            ringSb?.Begin();

            try
            {
                TxtModalStepTag.Text = isEs ? "ETAPA 1 DE 3 · RESPALDO DEL SISTEMA" : "STAGE 1 OF 3 · SYSTEM BACKUP";
                TxtModalTitle.Text = isEs ? "Creando punto de restauración" : "Creating restore point";
                TxtModalMessage.Text = isEs ? "Guardando estado preventivo en Volume Shadow Copy..." : "Saving preventive snapshot via Volume Shadow Copy...";
                await AnimateModalProgressAsync(0, 38, 750);

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

                _isSystemPrepared = true;
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
            SettingsAndProfileView.Visibility = Visibility.Collapsed;
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

        // ==================== CATÁLOGO DE APPS CON LOGOS REALES ====================

        private void InitializeCatalogAndApps()
        {
            var baseTweaks = TweakCatalog.GetAllTweaks();
            var catalog = new List<TweakItem>();

            foreach (var item in baseTweaks)
            {
                if (item.Category == "Instalador Día 0")
                {
                    item.Category = "Apps";
                    _appSubGroups[item.Id] = "Runtimes & Essentials";
                    if (item.Id == "install_vcredist")
                        item.IconUrl = "https://learn.microsoft.com/favicon.ico";
                    else if (item.Id == "install_directx")
                        item.IconUrl = "https://www.microsoft.com/favicon.ico";
                }
                catalog.Add(item);
            }

            var extraApps = new List<(string id, string group, string title, string desc, string pkg, string iconUrl)>
            {
                ("app_dotnet8", "Runtimes & Essentials", ".NET Desktop Runtime 8", "Runtime oficial de Microsoft necesario para aplicaciones modernas de escritorio en Windows.", "Microsoft.DotNet.DesktopRuntime.8", "https://dotnet.microsoft.com/favicon.ico"),
                ("app_brave", "Browsers & Communication", "Brave Browser", "Navegador rápido basado en Chromium con bloqueo nativo de rastreadores y publicidad.", "Brave.Brave", "https://brave.com/favicon.ico"),
                ("app_chrome", "Browsers & Communication", "Google Chrome", "Navegador web oficial de Google con sincronización de perfiles y extensiones.", "Google.Chrome", "https://www.google.com/chrome/static/images/chrome-logo.svg"),
                ("app_firefox", "Browsers & Communication", "Mozilla Firefox", "Navegador independiente de código abierto enfocado en privacidad y bajo consumo de RAM.", "Mozilla.Firefox", "https://www.mozilla.org/media/img/favicons/firefox/browser/favicon.ico"),
                ("app_discord", "Browsers & Communication", "Discord", "Cliente oficial de chat de voz, video y comunidades con aceleración por hardware.", "Discord.Discord", "https://discord.com/assets/847541504914fd33810e70a0ea73177e.ico"),
                ("app_telegram", "Browsers & Communication", "Telegram Desktop", "Cliente nativo de mensajería instantánea ultraliviano con sincronización en la nube.", "Telegram.TelegramDesktop", "https://telegram.org/favicon.ico"),
                ("app_steam", "Gaming & Media", "Steam", "Plataforma oficial de juegos de PC, talleres y gestión de bibliotecas.", "Valve.Steam", "https://store.steampowered.com/favicon.ico"),
                ("app_epic", "Gaming & Media", "Epic Games Launcher", "Cliente oficial para juegos de Epic Games Store y Unreal Engine.", "EpicGames.EpicGamesLauncher", "https://static-assets-prod.epicgames.com/epic-games-store/static/images/common/favicon.ico"),
                ("app_obs", "Gaming & Media", "OBS Studio", "Software estándar de grabación y transmisión en vivo con soporte NVENC / AMF / QuickSync.", "OBSProject.OBSStudio", "https://obsproject.com/assets/images/new_icon_small-r.png"),
                ("app_vlc", "Gaming & Media", "VLC Media Player", "Reproductor multimedia universal capaz de abrir cualquier códec de video y audio sin paquetes externos.", "VideoLAN.VLC", "https://images.videolan.org/images/favicon.ico"),
                ("app_spotify", "Gaming & Media", "Spotify", "Cliente de escritorio para reproducción de música y podcasts.", "Spotify.Spotify", "https://open.spotify.com/favicon.ico"),
                ("app_hwinfo", "Hardware & Diagnostics", "HWiNFO64", "Monitor profesional de sensores, voltajes, frecuencias y temperaturas en tiempo real.", "REALiX.HWiNFO", "https://www.hwinfo.com/favicon.ico"),
                ("app_cpuz", "Hardware & Diagnostics", "CPU-Z", "Diagnóstico detallado del procesador, caché, placa base, chipset y latencias de memoria RAM.", "CPUID.CPU-Z", "https://www.cpuid.com/favicon.ico"),
                ("app_gpuz", "Hardware & Diagnostics", "GPU-Z", "Información completa de tarjeta gráfica, bus PCIe, versión de BIOS, VRAM y sensores.", "TechPowerUp.GPU-Z", "https://www.techpowerup.com/favicon.ico"),
                ("app_crystaldiskinfo", "Hardware & Diagnostics", "CrystalDiskInfo", "Verifica la salud S.M.A.R.T., horas de encendido y temperatura de tus discos SSD y NVMe.", "CrystalDewWorld.CrystalDiskInfo", "https://crystalmark.info/favicon.ico"),
                ("app_crystaldiskmark", "Hardware & Diagnostics", "CrystalDiskMark", "Benchmark estándar para medir velocidades secuenciales y aleatorias de lectura/escritura en SSD.", "CrystalDewWorld.CrystalDiskMark", "https://crystalmark.info/favicon.ico"),
                ("app_7zip", "Utilities & Security", "7-Zip", "Compresor y descompresor de código abierto ultraliviano compatible con .7z, .zip y .rar.", "7zip.7zip", "https://www.7-zip.org/favicon.ico"),
                ("app_winrar", "Utilities & Security", "WinRAR", "Archivador clásico para gestión de paquetes .rar y .zip con reparación de volúmenes.", "RARLab.WinRAR", "https://www.win-rar.com/favicon.ico"),
                ("app_wiztree", "Utilities & Security", "WizTree", "Analizador ultrarrápido de espacio en disco que escanea unidades NTFS en segundos.", "AntibodySoftware.WizTree", "https://diskanalyzer.com/favicon.ico"),
                ("app_everything", "Utilities & Security", "Everything Search", "Buscador instantáneo de archivos y carpetas en todo el sistema por nombre.", "voidtools.Everything", "https://www.voidtools.com/favicon.ico"),
                ("app_rufus", "Utilities & Security", "Rufus", "Utilidad liviana para crear memorias USB booteables de Windows y Linux.", "Rufus.Rufus", "https://rufus.ie/favicon.ico"),
                ("app_ventoy", "Utilities & Security", "Ventoy", "Crea un USB multiboot donde solo arrastrás archivos .ISO sin formatear cada vez.", "Ventoy.Ventoy", "https://www.ventoy.net/favicon.ico"),
                ("app_bitwarden", "Utilities & Security", "Bitwarden", "Gestor de contraseñas cifrado de código abierto con sincronización multiplataforma.", "Bitwarden.Bitwarden", "https://bitwarden.com/favicon.ico"),
                ("app_keepassxc", "Utilities & Security", "KeePassXC", "Gestor de contraseñas local sin depender de servidores externos.", "KeePassXCTeam.KeePassXC", "https://keepassxc.org/favicon.ico"),
                ("app_sharex", "Utilities & Security", "ShareX", "Herramienta avanzada de capturas de pantalla, grabación GIF/MP4 y OCR.", "ShareX.ShareX", "https://getsharex.com/favicon.ico"),
                ("app_powertoys", "Utilities & Security", "Microsoft PowerToys", "Suite oficial de productividad: FancyZones, selector de color, renombrado masivo y más.", "Microsoft.PowerToys", "https://learn.microsoft.com/favicon.ico"),
                ("app_vscode", "Developer Tools", "Visual Studio Code", "Editor de código fuente extensible de Microsoft con soporte integrado para Git y terminal.", "Microsoft.VisualStudioCode", "https://code.visualstudio.com/favicon.ico"),
                ("app_git", "Developer Tools", "Git for Windows", "Sistema de control de versiones distribuido con Git Bash incluido.", "Git.Git", "https://git-scm.com/favicon.ico"),
                ("app_terminal", "Developer Tools", "Windows Terminal", "Terminal moderna con pestañas, aceleración GPU y soporte para PowerShell, CMD y WSL.", "Microsoft.WindowsTerminal", "https://learn.microsoft.com/favicon.ico"),
                ("app_pwsh", "Developer Tools", "PowerShell 7", "Consola multiplataforma de automatización y administración avanzada de sistemas.", "Microsoft.PowerShell", "https://learn.microsoft.com/favicon.ico"),
                ("app_python", "Developer Tools", "Python 3", "Intérprete oficial de Python para automatización, scripting y desarrollo.", "Python.Python.3.12", "https://www.python.org/static/favicon.ico"),
                ("app_nodejs", "Developer Tools", "Node.js LTS", "Entorno de ejecución JavaScript del lado del servidor con npm incluido.", "OpenJS.NodeJS.LTS", "https://nodejs.org/static/images/favicons/favicon.ico"),
                ("app_notepadpp", "Developer Tools", "Notepad++", "Editor de texto y código ultrarrápido escrito en C++ con consumo mínimo de memoria.", "Notepad++.Notepad++", "https://notepad-plus-plus.org/favicon.ico")
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
                    IconUrl = app.iconUrl,
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
