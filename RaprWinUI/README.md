# Driver Store Explorer: WinUI 3 front end (experimental)

A WinUI 3 front end with two layouts, picked in Options > Layout:

- **Explorer with details**: views (all packages, older versions, no device, boot critical,
  extensions) and driver classes on the left, the selected package on the right.
- **Compact with filters**: the same views as chips above the list, and a bar for the
  checked packages.

It shares the driver store code and the translations with the WinForms app: the files are
linked from `..\Rapr`, and nothing there is changed for it. Its own strings are in
`Strings\WinUIStrings.resx`.

## Build and run

Windows 10 1809 or later with the .NET 10 SDK. The XAML compiler only runs on Windows.

```powershell
dotnet run --project RaprWinUI -c Release -r win-x64 -p:Platform=x64
```

Publish a folder to copy elsewhere (needs the .NET 10 Runtime; the Windows App SDK is included):

```powershell
dotnet publish RaprWinUI -c Release -r win-x64 -p:Platform=x64 --self-contained false -o publish-winui
```

The app is portable: the settings (`WinUI.settings.json`) and the log (`DriverStoreExplorer.WinUI.log`)
are kept in the folder of the exe. Only if that folder can't be written to they go to
`%LOCALAPPDATA%\DriverStoreExplorer`.
