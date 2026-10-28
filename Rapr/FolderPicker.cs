using System;
using System.Windows.Forms;

namespace Rapr
{
    /// <summary>
    /// Shows the Windows folder picker. Each purpose has its own ClientGuid, so Windows
    /// remembers the last folder used for adding drivers separately from exporting them.
    /// </summary>
    internal static class FolderPicker
    {
        internal static readonly Guid AddDrivers = new Guid("6f0b8f34-5c1e-4d2a-9a43-2f7d0b6e1a01");
        internal static readonly Guid ExportDrivers = new Guid("6f0b8f34-5c1e-4d2a-9a43-2f7d0b6e1a02");
        internal static readonly Guid OfflineStore = new Guid("6f0b8f34-5c1e-4d2a-9a43-2f7d0b6e1a03");

        /// <returns>The selected folder, or null if the user cancelled.</returns>
        internal static string? Pick(IWin32Window owner, Guid purpose, string? title = null)
        {
            using (var dialog = new FolderBrowserDialog
            {
                ClientGuid = purpose,
                Description = title ?? string.Empty,
                UseDescriptionForTitle = !string.IsNullOrEmpty(title),
                ShowNewFolderButton = true,
            })
            {
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }
    }
}
