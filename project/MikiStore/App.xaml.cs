using Microsoft.Toolkit.Uwp.Notifications;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace MikiStore
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnActivated(EventArgs e)
        {
            // Check if we were activated by a toast
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                // Get the arguments we added in Step 1
                ToastArguments args = ToastArguments.Parse(toastArgs.Argument);

                if (args.TryGetValue("action", out string action) && action == "launchApp")
                {
                    string appId = args["appId"];

                    // Call your existing launch logic!
                    // This is the same logic you use for your "Play" button
                    LaunchInstalledApp(appId);
                }
            };
        }
        private void LaunchInstalledApp(string appId)
        {
            // 1. Path to where the app was unzipped
            string appFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                MikiStore.MainWindow.appDataFolder, "apps", appId);

            if (!Directory.Exists(appFolder)) return;

            // 2. Find the .exe (looking for the one that isn't a 'UnityCrashHandler' or similar)
            var exeFiles = Directory.GetFiles(appFolder, "*.exe", SearchOption.AllDirectories);

            // Pick the first .exe that isn't a helper/installer
            string targetExe = exeFiles.FirstOrDefault(f => !f.Contains("UnityCrashHandler") && !f.Contains("install"));

            if (!string.IsNullOrEmpty(targetExe))
            {
                string processName = Path.GetFileNameWithoutExtension(targetExe);

                // CHECK: Is this app already running?
                var existingProcesses = Process.GetProcessesByName(processName);
                if (existingProcesses.Length > 0)
                {
                    // Optional: Bring the existing window to the front instead of launching new
                    return;
                }

                Process.Start(new ProcessStartInfo(targetExe)
                {
                    WorkingDirectory = Path.GetDirectoryName(targetExe),
                    UseShellExecute = true
                });
            }
        }
    }
}
