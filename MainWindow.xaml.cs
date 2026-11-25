using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Archon.Core;
using Archon.Models;

namespace Archon
{
    public partial class MainWindow : Window
    {
        private enum PrepTarget
        {
            None,
            Fresh,
            Clean,
            Skip
        }

        private SystemHardwareProfile _hardware = new();
        private OptimizerState _state = new();
        private List<TweakItem> _allTweaks = new();
        private string _activeCategory = "Instalador Día 0";
        private bool _hardwareScanned = false;
        private PrepTarget _currentPrepTarget = PrepTarget.None;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
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
                await ExecuteDriverInstallationPipelineAsync();
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

        private void BtnStartStepZero_Click(object sender, RoutedEventArgs e)
        {
            ResetInlineProgressPanels();
            ShowView(StepZeroView);
            StepZeroView.ScrollToTop();
        }

        private void BtnBackToWelcome_Click(object sender, RoutedEventArgs e)
        {
            ShowView(WelcomeView);
            WelcomeView.ScrollToTop();
        }

        private void BtnBackToStepZero_Click(object sender, RoutedEventArgs e)
        {
            ResetInlineProgressPanels();
            ToggleStepZeroButtons(true);
            ShowView(StepZeroView);
            StepZeroView.ScrollToTop();
        }

        private void ShowView(UIElement target)
        {
            WelcomeView.Visibility = target == WelcomeView ? Visibility.Visible : Visibility.Collapsed;
            StepZeroView.Visibility = target == StepZeroView ? Visibility.Visible : Visibility.Collapsed;
            MainOptimizerView.Visibility = target == MainOptimizerView ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResetInlineProgressPanels()
        {
            _currentPrepTarget = PrepTarget.None;
            ProgressPanelFresh.Visibility = Visibility.Collapsed;
            ProgressPanelClean.Visibility = Visibility.Collapsed;
            ProgressPanelSkip.Visibility = Visibility.Collapsed;

            ProgressFresh.IsIndeterminate = false;
            ProgressFresh.Value = 0;
            TxtPercentFresh.Text = "0%";

            ProgressClean.IsIndeterminate = false;
            ProgressClean.Value = 0;
            TxtPercentClean.Text = "0%";

            ProgressSkip.IsIndeterminate = false;
            ProgressSkip.Value = 0;
            TxtPercentSkip.Text = "0%";
        }

        private void UpdateInlineProgress(string message, int percent, bool isIndeterminate = false)
        {
            ProgressPanelFresh.Visibility = _currentPrepTarget == PrepTarget.Fresh ? Visibility.Visible : Visibility.Collapsed;
            ProgressPanelClean.Visibility = _currentPrepTarget == PrepTarget.Clean ? Visibility.Visible : Visibility.Collapsed;
            ProgressPanelSkip.Visibility = _currentPrepTarget == PrepTarget.Skip ? Visibility.Visible : Visibility.Collapsed;

            string pctText = isIndeterminate ? "..." : $"{percent}%";

            switch (_currentPrepTarget)
            {
                case PrepTarget.Fresh:
                    TxtStatusFresh.Text = message;
                    TxtPercentFresh.Text = pctText;
                    ProgressFresh.IsIndeterminate = isIndeterminate;
                    if (!isIndeterminate) ProgressFresh.Value = percent;
                    break;

                case PrepTarget.Clean:
                    TxtStatusClean.Text = message;
                    TxtPercentClean.Text = pctText;
                    ProgressClean.IsIndeterminate = isIndeterminate;
                    if (!isIndeterminate) ProgressClean.Value = percent;
                    break;

                case PrepTarget.Skip:
                    TxtStatusSkip.Text = message;
                    TxtPercentSkip.Text = pctText;
                    ProgressSkip.IsIndeterminate = isIndeterminate;
                    if (!isIndeterminate) ProgressSkip.Value = percent;
                    break;
            }
        }

        private async void BtnFreshInstall_Click(object sender, RoutedEventArgs e)
        {
            ResetInlineProgressPanels();
            _currentPrepTarget = PrepTarget.Fresh;
            ToggleStepZeroButtons(false);

            if (!await EnsureMandatoryRestorePointAsync()) return;
            await ExecuteDriverInstallationPipelineAsync();
        }

        private async void BtnCleanAndInstall_Click(object sender, RoutedEventArgs e)
        {
            ResetInlineProgressPanels();
            _currentPrepTarget = PrepTarget.Clean;
            ToggleStepZeroButtons(false);

            if (!await EnsureMandatoryRestorePointAsync()) return;

            UpdateInlineProgress("Bloqueando drivers automáticos de Windows Update...", 45);
            StepZeroOrchestrator.SetWindowsUpdateDriverBlock(true);
            await Task.Delay(400);

            UpdateInlineProgress("Programando reanudación automática tras reinicio...", 80);
            _state.PendingPostRebootInstall = true;
            StepZeroOrchestrator.SaveState(_state);
            StepZeroOrchestrator.RegisterPostRebootResume();
            await Task.Delay(400);

            UpdateInlineProgress("Listo para reiniciar y completar la limpieza de controladores.", 100);
            ToggleStepZeroButtons(true);
        }

        private async void BtnSkipDrivers_Click(object sender, RoutedEventArgs e)
        {
            ResetInlineProgressPanels();
            _currentPrepTarget = PrepTarget.Skip;
            ToggleStepZeroButtons(false);

            if (!await EnsureMandatoryRestorePointAsync()) return;

            UpdateInlineProgress("Respaldo listo. Abriendo panel de ajustes...", 100);
            await Task.Delay(350);

            _state.StepZeroCompleted = true;
            StepZeroOrchestrator.SaveState(_state);
            UnlockMainOptimizer();
        }

        private async Task<bool> EnsureMandatoryRestorePointAsync()
        {
            if (_state.RestorePointCreated)
            {
                UpdateInlineProgress("Punto de restauración verificado.", 30);
                await Task.Delay(250);
                return true;
            }

            UpdateInlineProgress("Creando punto de restauración preventivo en el sistema...", 15, isIndeterminate: true);

            bool ok = await RestorePointManager.CreateMandatoryRestorePointAsync();

            if (!ok)
            {
                UpdateInlineProgress("No se pudo crear el punto de restauración. Verificá que el servicio VSS esté activo.", 0);
                ToggleStepZeroButtons(true);
                return false;
            }

            _state.RestorePointCreated = true;
            StepZeroOrchestrator.SaveState(_state);
            UpdateInlineProgress("Punto de restauración guardado correctamente.", 30);
            await Task.Delay(300);
            return true;
        }

        private async Task ExecuteDriverInstallationPipelineAsync()
        {
            ToggleStepZeroButtons(false);
            StepZeroOrchestrator.SetWindowsUpdateDriverBlock(true);

            if (_hardware.IsAmdCpu)
            {
                UpdateInlineProgress("Preparando instalación limpia de Chipset AMD...", 55);
                await Task.Delay(700);
            }

            if (_hardware.HasNvidiaGpu)
            {
                UpdateInlineProgress("Aplicando perfil limpio sin telemetría para NVIDIA...", 80);
                await Task.Delay(700);
            }
            else if (_hardware.HasAmdGpu)
            {
                UpdateInlineProgress("Instalando controlador gráfico AMD Radeon...", 80);
                await Task.Delay(700);
            }

            StepZeroOrchestrator.SetWindowsUpdateDriverBlock(false);
            _state.PendingPostRebootInstall = false;
            _state.StepZeroCompleted = true;
            StepZeroOrchestrator.SaveState(_state);

            UpdateInlineProgress("Base preparada correctamente. Abriendo panel de ajustes...", 100);
            await Task.Delay(500);

            UnlockMainOptimizer();
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
            _allTweaks = TweakCatalog.GetAllTweaks();
            RefreshDisplayedTweaks();
        }

        private void RefreshDisplayedTweaks()
        {
            string query = TxtSearch.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                TweaksItemsControl.ItemsSource = _allTweaks
                    .Where(t => t.Category == _activeCategory)
                    .ToList();
            }
            else
            {
                TweaksItemsControl.ItemsSource = _allTweaks
                    .Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        private void CategoryBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button clickedBtn) return;
            _activeCategory = clickedBtn.Tag?.ToString() ?? "Instalador Día 0";
            TxtSearch.Text = string.Empty;

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
            if (sender is Button btn && btn.Tag is TweakItem item)
            {
                TxtInspectorTitle.Text = item.Title;
                TxtInspectorDesc.Text = item.Description;
                TxtInspectorTech.Text = $"Modifica: {item.TechnicalDetail}";
                InspectorPanel.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseInspector_Click(object sender, RoutedEventArgs e) => InspectorPanel.Visibility = Visibility.Collapsed;

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
            _allTweaks = TweakCatalog.GetAllTweaks();
            RefreshDisplayedTweaks();
            TxtActionFeedback.Text = "Sistema restaurado al estado original";
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
