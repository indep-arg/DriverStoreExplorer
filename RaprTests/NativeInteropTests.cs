using System;
using System.Drawing;
using System.IO;
using System.Reflection;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Rapr.Utils;

namespace Rapr.Tests
{
    /// <summary>
    /// Calls the Win32 functions whose marshalling is generated at build time, so a wrong entry
    /// point or buffer size shows up here instead of on a user's machine.
    /// </summary>
    [TestClass]
    public class NativeInteropTests
    {
        private static readonly Func<string, string> GetFinalPathName =
            (Func<string, string>)typeof(DSEFormHelper)
                .GetMethod("GetFinalPathName", BindingFlags.Static | BindingFlags.NonPublic)
                .CreateDelegate(typeof(Func<string, string>));

        [TestMethod]
        public void ExportsOfSystemLibrariesAreFound()
        {
            RequireWindows();

            Assert.IsTrue(SetupAPI.MethodExists("kernel32.dll", "CreateFileW"));
            Assert.IsFalse(SetupAPI.MethodExists("kernel32.dll", "NoSuchFunction"));
            Assert.IsFalse(SetupAPI.MethodExists("NoSuchLibrary.dll", "CreateFileW"));
        }

        [TestMethod]
        public void FinalPathOfAFileIsItsFullPath()
        {
            RequireWindows();

            // Not the temp folder: on CI its path has 8.3 short names, which the final path expands.
            string file = Path.Combine(AppContext.BaseDirectory, Path.GetRandomFileName());
            File.WriteAllText(file, string.Empty);
            try
            {
                Assert.AreEqual(Path.GetFullPath(file), GetFinalPathName(file), ignoreCase: true);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [TestMethod]
        public void TheAppIconCanBeRead()
        {
            RequireWindows();

            using (Icon icon = DSEFormHelper.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "Rapr.exe")))
            {
                Assert.IsNotNull(icon);
                Assert.IsTrue(icon.Width > 0);
            }
        }

        [TestMethod]
        public void SetupApiListsDevices()
        {
            RequireWindows();

            Assert.IsNotEmpty(SetupAPI.GetDeviceDriverInfo());
        }

        private static void RequireWindows()
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                Assert.Inconclusive("Needs Windows.");
            }
        }
    }
}
