using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Rapr.WinUI
{
    /// <summary>
    /// The few settings of the WinUI front end, kept as JSON next to the exe.
    /// </summary>
    internal sealed class AppSettings
    {
        public string DriverStoreOption { get; set; } = BackendFactory.Native;

        public string Theme { get; set; } = "System";

        public string Layout { get; set; } = "Explorer";

        /// <summary>
        /// A culture name such as "es-ES", or null for the Windows display language.
        /// </summary>
        public string? Language { get; set; }

        /// <summary>
        /// Where the settings and the log go: next to the exe, so the app is portable. Only when that
        /// folder can't be written to (an exe left in a protected location) they go to local app data.
        /// </summary>
        public static string Folder { get; } = ChooseFolder();

        private static string ChooseFolder()
        {
            string besideExe = AppContext.BaseDirectory;
            try
            {
                string probe = Path.Combine(besideExe, Path.GetRandomFileName());
                using (File.Create(probe, 1, FileOptions.DeleteOnClose))
                {
                }

                return besideExe;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DriverStoreExplorer");
            }
        }

        private static string FilePath => Path.Combine(Folder, "WinUI.settings.json");

        public static AppSettings Current { get; } = Load();

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Settings could not be saved: {ex}");
            }
        }

        private static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
            {
                Trace.TraceWarning($"Settings could not be read, using the defaults: {ex}");
            }

            return new AppSettings();
        }
    }
}
