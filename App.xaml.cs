using System.Linq;
using System.Runtime.Versioning;
using System.Windows;

[assembly: SupportedOSPlatform("windows")]

namespace OptimizadorFrankenn
{
    public partial class App : Application
    {
        public static bool IsResumeFromReboot { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            IsResumeFromReboot = e.Args.Contains("--resume-step0");

            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}
