using System;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace Rapr
{
    /// <summary>
    /// Small Win32 calls used by the UI. The marshalling code is generated at build time.
    /// </summary>
    internal static partial class SafeNativeMethods
    {
        [LibraryImport("shell32.dll", EntryPoint = "ExtractAssociatedIconW", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial IntPtr ExtractAssociatedIcon(IntPtr hInst, [In, Out] char[] iconPath, ref ushort index);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool DestroyIcon(IntPtr hIcon);

        [LibraryImport("uxtheme.dll", StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

        [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
        internal static partial IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial SafeFileHandle CreateFile(
            string lpFileName,
            int dwDesiredAccess,
            int dwShareMode,
            IntPtr lpSecurityAttributes,
            int dwCreationDisposition,
            int dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        internal static partial int GetFinalPathNameByHandle(
            SafeFileHandle hFile,
            [Out] char[] lpszFilePath,
            int cchFilePath,
            int dwFlags);
    }
}
