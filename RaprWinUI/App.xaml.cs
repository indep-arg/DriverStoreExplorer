using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

using Microsoft.UI.Xaml;

namespace Rapr.WinUI
{
    public partial class App : Application
    {
        private MainWindow? window;

        public App()
        {
            StartLog();
            ApplyLanguage(AppSettings.Current.Language);

            this.InitializeComponent();

            this.UnhandledException += (sender, e) => Trace.TraceError($"Unhandled exception: {e.Exception}");
            AppDomain.CurrentDomain.UnhandledException += (sender, e) => Trace.TraceError($"Unhandled exception: {e.ExceptionObject}");
        }

        public static string LogFile { get; } = Path.Combine(AppSettings.Folder, "DriverStoreExplorer.WinUI.log");

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            this.window = new MainWindow();
            this.window.Activate();
        }

        private static void ApplyLanguage(string? cultureName)
        {
            if (string.IsNullOrEmpty(cultureName))
            {
                return;
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(cultureName);
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                Lang.Language.Culture = culture;
            }
            catch (CultureNotFoundException ex)
            {
                Trace.TraceWarning($"Unknown language '{cultureName}': {ex.Message}");
            }
        }

        private static void StartLog()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.Folder);
                var writer = new StreamWriter(new FileStream(LogFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
                Trace.Listeners.Add(new TextWriterTraceListener(writer));
                Trace.AutoFlush = true;
                Trace.TraceInformation($"{DateTime.UtcNow:u} Driver Store Explorer (WinUI) started");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                // Without a log the app still works.
            }
        }
    }
}
