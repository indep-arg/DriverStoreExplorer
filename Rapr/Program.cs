using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Windows.Forms;

using Rapr.Lang;
using Rapr.Utils;

namespace Rapr
{
    public static class Program
    {
        // WinForms dark mode is still marked experimental in .NET 10 (WFO5001). It has to be set
        // before the first window is created, so a change from the Options menu applies on restart.
#pragma warning disable WFO5001
        private static void ApplyColorMode(string colorMode)
        {
            SystemColorMode mode;
            switch (colorMode)
            {
                case "Light":
                    mode = SystemColorMode.Classic;
                    break;
                case "Dark":
                    mode = SystemColorMode.Dark;
                    break;
                default:
                    mode = SystemColorMode.System;
                    break;
            }

            Application.SetColorMode(mode);
        }
#pragma warning restore WFO5001

        private static void AddEnvironmentPaths(string path)
        {
            var paths = new[] { Environment.GetEnvironmentVariable("PATH") ?? string.Empty, path };
            string newPath = string.Join(Path.PathSeparator.ToString(), paths);
            Environment.SetEnvironmentVariable("PATH", newPath);
        }

        private static void CleanUpOldConfig()
        {
            FileInfo fileInfo = new FileInfo(ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.PerUserRoamingAndLocal).FilePath);
            DirectoryInfo? strongAssemblyConfigDirectory = fileInfo.Directory?.Parent;
            if (strongAssemblyConfigDirectory == null)
            {
                return;
            }

            if (strongAssemblyConfigDirectory.Exists)
            {
                // Remove old version of config.
                foreach (var dir in strongAssemblyConfigDirectory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly))
                {
                    if (Version.TryParse(dir.Name, out Version? version) && version < Assembly.GetExecutingAssembly().GetName().Version)
                    {
                        dir.Delete(recursive: true);
                    }
                }
            }

            DirectoryInfo? configDirectory = strongAssemblyConfigDirectory.Parent;

            if (configDirectory?.Exists == true)
            {
                // Remove configurations created by not strong signed app.
                foreach (var dir in configDirectory.EnumerateDirectories(Path.GetFileName(Environment.ProcessPath) + "_Url_*", SearchOption.TopDirectoryOnly))
                {
                    dir.Delete(recursive: true);
                }
            }
        }

        internal static void LogUnhandledException(object exception)
        {
            Trace.TraceError($"Unhandled exception: {exception}");
        }

        private static void OnThreadException(object? sender, ThreadExceptionEventArgs e)
        {
            LogUnhandledException(e.Exception);

            // Same dialog WinForms shows when there is no handler.
            using (var dialog = new ThreadExceptionDialog(e.Exception))
            {
                if (dialog.ShowDialog() == DialogResult.Abort)
                {
                    Application.Exit();
                }
            }
        }

        private static bool TryRestartAsAdministrator()
        {
            try
            {
                return DSEFormHelper.RunAsAdministrator();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                Trace.TraceWarning($"Could not restart as administrator: {ex}");
                return false;
            }
        }

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static void Main()
        {
            // Visual styles, text rendering and PerMonitorV2 DPI awareness, as set in Rapr.csproj.
            ApplicationConfiguration.Initialize();

            Trace.AutoFlush = true;
            Trace.IndentSize = 4;
            Trace.Listeners.Add(new TextFileTraceListener());

            // Write crashes to Rapr.log; otherwise a crash during startup leaves nothing to report.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => LogUnhandledException(e.ExceptionObject);

            AddEnvironmentPaths(@"C:\Windows\System32\CompatTel");

            UpdateManager.CleanUpOldFiles();

            try
            {
                string settingFile = $"{Application.ProductName}.user.config";
                string applicationFolderPath = DSEFormHelper.GetApplicationFolder();
                bool migrationSucceeded = false;
                bool portableSettingsFailed = false;

                try
                {
                    migrationSucceeded = SettingsMigration.UsePortableSettings(
                        Properties.Settings.Default, applicationFolderPath, settingFile, hasDriverStoreOption =>
                        {
                            if (!hasDriverStoreOption)
                            {
                                DriverStoreFactory.MigrateDriverStoreSettings();
                            }
                        });
                }
                catch (Exception ex) when (ex is SecurityException || ex is UnauthorizedAccessException
                    || ex is IOException || ex is ConfigurationException || ex is System.Xml.XmlException
                    || ex is InvalidDataException)
                {
                    portableSettingsFailed = true;
                    Trace.TraceWarning($"Portable settings could not be initialized; retaining per-user settings: {ex}");
                    if (!(ex is SecurityException) && !(ex is UnauthorizedAccessException) && !(ex is IOException))
                    {
                        Dialogs.ShowMessage(null, ex.Message, Language.Product_Name, TaskDialogIcon.Error, ex.ToString());
                    }
                }

                if (Properties.Settings.Default.UpgradeRequired)
                {
                    try
                    {
                        // Upgrade settings from previous version
                        Properties.Settings.Default.Upgrade();
                        if (!SettingsMigration.HasStoredUserSetting(
                            Properties.Settings.Default, nameof(Properties.Settings.DriverStoreOption)))
                        {
                            DriverStoreFactory.MigrateDriverStoreSettings();
                        }

                        // Mark upgrade as completed
                        Properties.Settings.Default.UpgradeRequired = false;
                        SettingsMigration.SaveAndVerify(Properties.Settings.Default);
                        migrationSucceeded = true;

                        Trace.TraceInformation("Settings migration completed successfully");
                    }
                    catch (Exception ex)
                    {
                        // Log the error but don't fail the application startup
                        Trace.TraceError($"Settings migration failed: {ex}");

                        Properties.Settings.Default.UpgradeRequired = true;
                    }
                }

                if (migrationSucceeded && !portableSettingsFailed)
                {
                    try
                    {
                        CleanUpOldConfig();
                    }
                    catch (Exception e) when (
                        e is SecurityException
                        || e is IOException)
                    {
                        Trace.TraceError(e.ToString());
                    }
                }

                // The manifest asks for elevation, so this only runs when that was bypassed.
                if (!DSEFormHelper.IsRunAsAdmin)
                {
                    if (!DSEFormHelper.IsElevationAttempted && TryRestartAsAdministrator())
                    {
                        return;
                    }

                    Dialogs.ShowMessage(null, Language.Message_Requires_Admin, Language.Product_Name, TaskDialogIcon.Error);
                    return;
                }

                DriverStoreFactory.ValidateDriverStoreOption();
                ApplyColorMode(Properties.Settings.Default.ColorMode);

                using (DSEForm mainForm = new DSEForm())
                {
                    Application.Run(mainForm);
                }
            }
            catch (ConfigurationException e)
            {
                Trace.TraceError(e.ToString());
                Dialogs.ShowMessage(null, e.Message, Language.Product_Name, TaskDialogIcon.Error, e.ToString());
            }
        }
    }
}
