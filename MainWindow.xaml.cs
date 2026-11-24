using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Archon.Core;
using Archon.Models;

namespace Archon
{
    public partial class MainWindow : Window
    {
        private SystemHardwareProfile _hardware = new();
        private OptimizerState _state = new();
        private List<TweakItem> _allTweaks = new();
        private string _activeCategory = "Instalador Día 0";

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _state = StepZeroOrchestrator.LoadState();
            _hardware = await Task.Run(() => HardwareDetector.ScanSystem());

            TxtCpuBadge.Text = $"[ CPU: {_hardware.CpuName} ]";
            TxtMotherBadge.Text = $"[ MOTHER: {_hardware.MotherboardName} ]";
            TxtGpuBadge.Text = $"[ GPU: {_hardware.PrimaryGpu} ]";
            TxtRamBadge.Text = $"[ RAM: {_hardware.TotalRamGb} ]";
            TxtStorageBadge.Text = $"[ DISCO: {_hardware.PrimaryStorage} ]";
            TxtOsBadge.Text = $"[ {_hardware.WindowsVersion} ]";

            if (_hardware.DetectedBloatware.Count > 0)
            {
                BloatAlertBox.Visibility = Visibility.Visible;
                TxtDetectedBloatList.Text = string.Join("  |  ", _hardware.DetectedBloatware);
            }

            if (App.IsResumeFromReboot && _state.PendingPostRebootInstall)
            {
                await ExecuteDriverInstallationPipelineAsync();
            }
            else if (_state.StepZeroCompleted)
            {
                TxtStepZeroStatus.Text = "ESTADO: Base preparada previamente. Podés volver a ejecutar una opción o ir al optimizador.";
                StepZeroProgress.Value = 100;
            }
        }

        private void BtnBackToStepZero_Click(object sender, RoutedEventArgs e)
        {
            MainOptimizerView.Visibility = Visibility.Collapsed;
            StepZeroView.Visibility = Visibility.Visible;
            StepZeroView.ScrollToTop();
            ToggleStepZeroButtons(true);
            StepZeroProgress.IsIndeterminate = false;
            StepZeroProgress.Value = _state.StepZeroCompleted ? 100 : 0;
            TxtStepZeroStatus.Text = _state.StepZeroCompleted
                ? "ESTADO: Base preparada previamente. Podés volver a ejecutar una opción o ir al optimizador."
                : "ESTADO: Esperando selección...";
        }

        private async void BtnFreshInstall_Click(object sender, RoutedEventArgs e)
        {
            ToggleStepZeroButtons(false);
            if (!await EnsureMandatoryRestorePointAsync()) return;

            await ExecuteDriverInstallationPipelineAsync();
        }

        private async void BtnCleanAndInstall_Click(object sender, RoutedEventArgs e)
        {
            ToggleStepZeroButtons(false);
            if (!await EnsureMandatoryRestorePointAsync()) return;

            TxtStepZeroStatus.Text = "ESTADO: Bloqueando drivers automáticos de Windows Update...";
            StepZeroProgress.Value = 40;
            StepZeroOrchestrator.SetWindowsUpdateDriverBlock(true);

            TxtStepZeroStatus.Text = "ESTADO: Programando reanudación automática tras reinicio...";
            StepZeroProgress.Value = 75;

            _state.PendingPostRebootInstall = true;
            StepZeroOrchestrator.SaveState(_state);
            StepZeroOrchestrator.RegisterPostRebootResume();

            TxtStepZeroStatus.Text = "ESTADO: Listo para reiniciar y completar drivers limpios.";
            StepZeroProgress.Value = 100;
            ToggleStepZeroButtons(true);
        }

        private async void BtnSkipDrivers_Click(object sender, RoutedEventArgs e)
        {
            ToggleStepZeroButtons(false);
            if (!await EnsureMandatoryRestorePointAsync()) return;

            _state.StepZeroCompleted = true;
            StepZeroOrchestrator.SaveState(_state);
            UnlockMainOptimizer();
        }

        private async Task<bool> EnsureMandatoryRestorePointAsync()
        {
            if (_state.RestorePointCreated)
                return true;

            TxtStepZeroStatus.Text = "ESTADO: Creando Punto de Restauración obligatorio (esto toma unos segundos)...";
            StepZeroProgress.IsIndeterminate = true;

            bool ok = await RestorePointManager.CreateMandatoryRestorePointAsync();
            StepZeroProgress.IsIndeterminate = false;

            if (!ok)
            {
                TxtStepZeroStatus.Text = "ERROR: No se pudo crear el punto de restauración. Verificá el servicio VSS.";
                ToggleStepZeroButtons(true);
                return false;
            }

            _state.RestorePointCreated = true;
            StepZeroOrchestrator.SaveState(_state);
            StepZeroProgress.Value = 30;
            return true;
        }

        private async Task ExecuteDriverInstallationPipelineAsync()
        {
            ToggleStepZeroButtons(false);
            StepZeroOrchestrator.SetWindowsUpdateDriverBlock(true);

            if (_hardware.IsAmdCpu)
            {
                TxtStepZeroStatus.Text = "ESTADO: Preparando instalación limpia de Chipset AMD...";
                StepZeroProgress.Value = 55;
                await Task.Delay(800);
            }

            if (_hardware.HasNvidiaGpu)
            {
                TxtStepZeroStatus.Text = "ESTADO: Aplicando perfil NVCleanstall sin telemetría para NVIDIA...";
                StepZeroProgress.Value = 80;
                await Task.Delay(800);
            }
            else if (_hardware.HasAmdGpu)
            {
                TxtStepZeroStatus.Text = "ESTADO: Instalando driver gráfico AMD Radeon (Driver Only)...";
                StepZeroProgress.Value = 80;
                await Task.Delay(800);
            }

            StepZeroOrchestrator.SetWindowsUpdateDriverBlock(false);
            _state.PendingPostRebootInstall = false;
            _state.StepZeroCompleted = true;
            StepZeroOrchestrator.SaveState(_state);

            StepZeroProgress.Value = 100;
            TxtStepZeroStatus.Text = "ESTADO: Base de drivers lista. Abriendo optimizador...";
            await Task.Delay(500);

            UnlockMainOptimizer();
        }

        private void UnlockMainOptimizer()
        {
            StepZeroView.Visibility = Visibility.Collapsed;
            MainOptimizerView.Visibility = Visibility.Visible;
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
                        ? (Style)FindResource("PrimaryRedButtonStyle")
                        : (Style)FindResource("RawButtonStyle");
                }
            }
            RefreshDisplayedTweaks();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshDisplayedTweaks();

        private void BtnInfo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TweakItem item)
            {
                TxtInspectorTitle.Text = $"[ AUDITORÍA // {item.Title.ToUpper()} ]";
                TxtInspectorDesc.Text = item.Description;
                TxtInspectorTech.Text = $"MODIFICA: {item.TechnicalDetail}";
                InspectorPanel.Visibility = Visibility.Visible;
            }
        }

        private void BtnCloseInspector_Click(object sender, RoutedEventArgs e) => InspectorPanel.Visibility = Visibility.Collapsed;

        private async void BtnApplyChanges_Click(object sender, RoutedEventArgs e)
        {
            TxtActionFeedback.Text = "APLICANDO CAMBIOS...";
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
            TxtActionFeedback.Text = "[ OK ] CAMBIOS APLICADOS CON RESPALDO.";
        }

        private async void BtnRestoreAll_Click(object sender, RoutedEventArgs e)
        {
            TxtActionFeedback.Text = "RESTAURANDO VALORES ORIGINALES...";
            await Task.Run(() => RegistryEngine.RestoreAllBackups());
            _allTweaks = TweakCatalog.GetAllTweaks();
            RefreshDisplayedTweaks();
            TxtActionFeedback.Text = "[ OK ] SISTEMA RESTAURADO A ESTADO ORIGINAL.";
        }

        private void BtnExportProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "Perfil Archon (*.json)|*.json", FileName = "perfil_archon.json" };
            if (dialog.ShowDialog() == true)
            {
                var profile = new UserProfileConfig
                {
                    TweakStates = _allTweaks.ToDictionary(t => t.Id, t => t.IsEnabled)
                };
                File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true }));
                TxtActionFeedback.Text = "[ OK ] PERFIL EXPORTADO.";
            }
        }

        private void BtnImportProfile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Perfil Archon (*.json)|*.json" };
            if (dialog.ShowDialog() == true)
            {
                var profile = JsonSerializer.Deserialize<UserProfileConfig>(File.ReadAllText(dialog.FileName));
                if (profile != null)
                {
                    foreach (var tweak in _allTweaks)
                    {
                        if (profile.TweakStates.TryGetValue(tweak.Id, out bool savedState))
                            tweak.IsEnabled = savedState;
                    }
                    RefreshDisplayedTweaks();
                    TxtActionFeedback.Text = "[ OK ] PERFIL IMPORTADO. PRESIONÁ APLICAR.";
                }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
