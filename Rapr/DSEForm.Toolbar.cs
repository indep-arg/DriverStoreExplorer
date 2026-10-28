using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Windows.Forms;

namespace Rapr
{
    /// <summary>
    /// A toolbar with icons in place of the Operations panel. The panel's buttons and checkboxes
    /// stay the source of truth: the toolbar buttons call the same handlers and follow their
    /// Enabled and Text, and the two checkboxes are moved into the toolbar as they are.
    /// </summary>
    public partial class DSEForm
    {
        // Segoe Fluent Icons (Windows 11) and Segoe MDL2 Assets (Windows 10) share these code points.
        private const char IconRefresh = '';
        private const char IconAdd = '';
        private const char IconDelete = '';
        private const char IconHistory = '';
        private const char IconFilter = '';
        private const char IconSave = '';
        private const char IconDownload = '';

        private static readonly string IconFontName = FindIconFont();

        private readonly Dictionary<ToolStripItem, char> toolbarIcons = new Dictionary<ToolStripItem, char>();
        private ToolStrip operationsToolStrip;

        private void SetupToolbar()
        {
            this.toolbarIcons.Clear();

            var toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Dock = DockStyle.Top,
                Padding = new Padding(this.LogicalToDeviceUnits(4), this.LogicalToDeviceUnits(2), this.LogicalToDeviceUnits(4), this.LogicalToDeviceUnits(2)),
                ImageScalingSize = new Size(this.LogicalToDeviceUnits(16), this.LogicalToDeviceUnits(16)),
            };

            toolbar.Items.Add(this.MirrorButton(this.buttonEnumerate, IconRefresh, this.ButtonEnumerate_Click));
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(this.MirrorButton(this.buttonAddDriver, IconAdd, this.ButtonAddDriver_Click));
            toolbar.Items.Add(HostCheckBox(this.cbAddInstall));
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(this.MirrorButton(this.buttonDeleteDriver, IconDelete, this.ButtonDelete_Click));
            toolbar.Items.Add(HostCheckBox(this.cbForceDeletion));
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(this.MirrorButton(this.buttonSelectOldDrivers, IconHistory, this.ButtonSelectOldDrivers_Click));
            toolbar.Items.Add(this.MirrorButton(this.buttonSelectUnusedDrivers, IconFilter, this.ButtonSelectUnusedDrivers_Click));
            toolbar.Items.Add(new ToolStripSeparator());
            toolbar.Items.Add(this.MirrorButton(this.buttonExportDrivers, IconSave, this.ButtonExportDrivers_Click));
            toolbar.Items.Add(this.MirrorButton(this.buttonExportAllDrivers, IconDownload, this.ButtonExportAllDrivers_Click));

            // The list takes the panel's column; the hidden buttons keep tracking state.
            this.tableLayoutPanel1.Controls.Remove(this.gbOptions);

            // Docking goes from the back of the z-order, so this lands below the menu and above the list.
            this.Controls.Add(toolbar);
            this.Controls.SetChildIndex(toolbar, this.Controls.GetChildIndex(this.tableLayoutPanel1) + 1);

            this.operationsToolStrip = toolbar;
            this.RefreshToolbarIcons();

            // Blend the title bar into the menu bar (Windows 11; ignored elsewhere).
#pragma warning disable WFO5001 // Still marked experimental in .NET 10.
            this.FormCaptionBackColor = this.menuStrip1.BackColor;
#pragma warning restore WFO5001
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);

            if (this.operationsToolStrip != null)
            {
                int size = this.LogicalToDeviceUnits(16);
                this.operationsToolStrip.ImageScalingSize = new Size(size, size);
                this.RefreshToolbarIcons();
            }
        }

        private ToolStripButton MirrorButton(Button source, char icon, EventHandler handler)
        {
            var item = new ToolStripButton(source.Text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                TextImageRelation = TextImageRelation.ImageBeforeText,
                ImageScaling = ToolStripItemImageScaling.None,
                Enabled = source.Enabled,
            };

            item.Click += (sender, e) => handler(source, e);
            source.EnabledChanged += (sender, e) => item.Enabled = source.Enabled;
            source.TextChanged += (sender, e) => item.Text = source.Text;

            this.toolbarIcons[item] = icon;
            return item;
        }

        private static ToolStripControlHost HostCheckBox(CheckBox checkBox)
        {
            checkBox.Dock = DockStyle.None;
            checkBox.AutoSize = true;
            checkBox.BackColor = Color.Transparent;

            return new ToolStripControlHost(checkBox)
            {
                Margin = new Padding(2, 1, 6, 1),
            };
        }

        private void RefreshToolbarIcons()
        {
            if (IconFontName == null)
            {
                return;
            }

            int size = this.LogicalToDeviceUnits(16);
            Color color = this.operationsToolStrip.ForeColor;

            foreach (KeyValuePair<ToolStripItem, char> pair in this.toolbarIcons)
            {
                Image old = pair.Key.Image;
                pair.Key.Image = CreateIcon(pair.Value, size, color);
                old?.Dispose();
            }
        }

        private static Bitmap CreateIcon(char glyph, int size, Color color)
        {
            var bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (var font = new Font(IconFontName, size * 0.8f, GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(color))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), format);
            }

            return bitmap;
        }

        private static string FindIconFont()
        {
            using (var fonts = new InstalledFontCollection())
            {
                string[] installed = fonts.Families.Select(family => family.Name).ToArray();
                return new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" }
                    .FirstOrDefault(name => installed.Contains(name, StringComparer.OrdinalIgnoreCase));
            }
        }
    }
}
