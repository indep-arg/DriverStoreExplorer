using System.Runtime.InteropServices;

// Every P/Invoke in this app, including the driver store code linked from ..\Rapr, targets a
// Windows DLL. Load them only from System32, so a DLL with the same name next to the exe (in
// Downloads, say) is never loaded into this elevated process.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
