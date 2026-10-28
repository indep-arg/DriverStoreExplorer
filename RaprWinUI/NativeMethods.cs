using System;
using System.Runtime.InteropServices;

namespace Rapr.WinUI
{
    internal static partial class NativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial uint GetDpiForWindow(IntPtr hwnd);
    }
}
