using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

using Microsoft.Win32.SafeHandles;

namespace Rapr
{
    public static class DSEFormHelper
    {
        private static readonly Version Win8Version = new Version(6, 2);
        private static readonly Version Win10Version = new Version(10, 0);
        private static readonly Version Win11Version = new Version(10, 0, 22000);

        public static string GetApplicationFolder()
        {
            // Assembly.Location is empty in a single-file app; the exe path is what we need anyway.
            string? assemblyLocation = Environment.ProcessPath;

            if (!string.IsNullOrEmpty(assemblyLocation))
            {
                // Check if the file is a symbolic link
                FileInfo fileInfo = new FileInfo(assemblyLocation);

                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    // Resolve the real path
                    assemblyLocation = GetFinalPathName(assemblyLocation);
                }
                return Path.GetDirectoryName(assemblyLocation) ?? AppContext.BaseDirectory;
            }
            else
            {
                return AppContext.BaseDirectory;
            }
        }

        private static string GetFinalPathName(string path)
        {
            const int FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
            const int OPEN_EXISTING = 3;

            using (SafeFileHandle handle = SafeNativeMethods.CreateFile(path, 0, 0, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
            {
                if (handle.IsInvalid)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                StringBuilder finalPath = new StringBuilder(512);
                int result = SafeNativeMethods.GetFinalPathNameByHandle(handle, finalPath, finalPath.Capacity, 0);
                if (result > finalPath.Capacity)
                {
                    // The buffer was too small; result is the size needed.
                    finalPath.Capacity = result;
                    result = SafeNativeMethods.GetFinalPathNameByHandle(handle, finalPath, finalPath.Capacity, 0);
                }

                if (result == 0 || result > finalPath.Capacity)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                return RemoveExtendedPathPrefix(finalPath.ToString());
            }
        }

        internal static string RemoveExtendedPathPrefix(string path)
        {
            const string UncPrefix = @"\\?\UNC\";
            const string PathPrefix = @"\\?\";

            if (path.StartsWith(UncPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return @"\\" + path.Substring(UncPrefix.Length);
            }

            if (path.StartsWith(PathPrefix, StringComparison.Ordinal))
            {
                return path.Substring(PathPrefix.Length);
            }

            return path;
        }

        public static bool IsRunAsAdmin { get; } = IsRunAsAdministrator();

        private static bool IsRunAsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (SecurityException)
            {
                return false;
            }
        }

        public static bool IsWin8OrNewer =>
            Environment.OSVersion.Platform == PlatformID.Win32NT
            && Environment.OSVersion.Version >= Win8Version;

        /// <summary>
        /// Gets a value indicating whether the current operating system is Windows 11 or newer.
        /// </summary>
        public static bool IsWin11OrNewer =>
            Environment.OSVersion.Platform == PlatformID.Win32NT
            && Environment.OSVersion.Version >= Win11Version;

        /// <summary>
        /// Gets a value indicating whether the native driver store API is supported.
        /// The native driver store requires Windows 8 or newer.
        /// </summary>
        public static bool IsNativeDriverStoreSupported => IsWin8OrNewer;

        /// <summary>
        /// Gets a value indicating whether the PnPUtil utility is supported.
        /// </summary>
        public static bool IsPnpUtilSupported => !IsWin11OrNewer;

        private const string ElevationAttemptedEnvVar = "DSE_ELEVATION_ATTEMPTED";

        public static bool IsElevationAttempted =>
            Environment.GetEnvironmentVariable(ElevationAttemptedEnvVar) == "1";

        public static bool RunAsAdministrator()
        {
            Environment.SetEnvironmentVariable(ElevationAttemptedEnvVar, "1");

            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                Verb = "runas",
                FileName = Environment.ProcessPath,
                UseShellExecute = true,
            };

            try
            {
                Process.Start(processInfo);
            }
            catch (Win32Exception ex)
            {
                // Ignore error 1223: The operation was canceled by the user.
                if (ex.NativeErrorCode == 1223)
                {
                    return false;
                }

                throw;
            }

            Application.Exit();
            return true;
        }

        /// <summary>
        /// Returns an icon representation of an image contained in the specified file.
        /// This function is identical to System.Drawing.Icon.ExtractAssociatedIcon, except this version works.
        /// </summary>
        /// <param name="filePath">The path to the file that contains an image.</param>
        /// <returns>The System.Drawing.Icon representation of the image contained in the specified file.</returns>
        /// <exception cref="System.ArgumentException">filePath does not indicate a valid file.</exception>
        public static Icon? ExtractAssociatedIcon(string filePath)
        {
            int index = 0;

            Uri uri;

            if (filePath == null)
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            try
            {
                uri = new Uri(filePath);
            }
            catch (UriFormatException)
            {
                filePath = Path.GetFullPath(filePath);
                uri = new Uri(filePath);
            }

            if (uri.IsFile)
            {
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException(filePath);
                }

                StringBuilder iconPath = new StringBuilder(filePath, 260);

                IntPtr handle = SafeNativeMethods.ExtractAssociatedIcon(
                    new HandleRef(null, IntPtr.Zero),
                    iconPath,
                    ref index);

                if (handle != IntPtr.Zero)
                {
                    using (Icon tmpIcon = Icon.FromHandle(handle))
                    {
                        Icon clonedIcon = (Icon)tmpIcon.Clone();
                        SafeNativeMethods.DestroyIcon(handle);
                        return clonedIcon;
                    }
                }
            }

            return null;
        }

        public static ICollection<CultureInfo> GetSupportedLanguage()
        {
            // The list of Lang\Language.*.resx files is written into SupportedCultures at build time
            // (see Rapr.csproj). Satellite assemblies are bundled inside the single-file exe, so they
            // can't be found by scanning the application folder.
            HashSet<CultureInfo> supportedLanguage = new HashSet<CultureInfo>
            {
                new CultureInfo("en")
            };

            const string Prefix = "Language.";
            foreach (string resourceFile in SupportedCultures.ResourceFiles)
            {
                try
                {
                    supportedLanguage.Add(new CultureInfo(resourceFile.Substring(Prefix.Length)));
                }
                catch (CultureNotFoundException)
                {
                }
            }

            return supportedLanguage;
        }
    }
}
