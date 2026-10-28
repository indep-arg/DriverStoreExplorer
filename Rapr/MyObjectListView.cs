using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;

using BrightIdeasSoftware;

using Rapr.Utils;

namespace Rapr
{
    public sealed class MyObjectListView : TypedObjectListView<DriverStoreEntry>
    {
        private const int LVM_SETEXTENDEDLISTVIEWSTYLE = 0x1036;
        private const int LVS_EX_GRIDLINES = 0x1;

        private int layoutDpi;

        private readonly DarkCheckBoxRenderer checkBoxRenderer = new DarkCheckBoxRenderer();

        public MyObjectListView()
        {
            // ObjectListView's renderers draw checkboxes with CheckBoxRenderer, which is always the
            // light one. The search swaps the default renderer for one with a text filter, so the
            // filter is carried over to this renderer on every lookup.
            this.CellRendererGetter = (model, column) =>
            {
                if (column.Renderer != null)
                {
                    return column.Renderer;
                }

                this.checkBoxRenderer.Filter = (this.DefaultRenderer as HighlightTextRenderer)?.Filter;
                return this.checkBoxRenderer;
            };
        }

        /// <summary>
        /// Gets whether the app runs with WinForms' dark color mode (Options > Theme).
        /// </summary>
#pragma warning disable WFO5001 // Dark mode is still marked experimental in .NET 10.
        internal static bool IsDarkMode => Application.IsDarkModeEnabled;
#pragma warning restore WFO5001

        /// <summary>
        /// Builds the checkbox images. ObjectListView attaches an empty ImageList to the ListView and
        /// then adds the images to it, but with UseCompatibleStateImageBehavior off, attaching it
        /// recreates the native list view, which destroys that image list (see the comments in
        /// ListView.StateImageList), so on .NET 10 the adds failed with "cannot add the image to the
        /// ImageList" and the app crashed at startup. Here the list is filled before it is attached.
        /// The images are also sized for the monitor's DPI instead of a fixed 16 pixels.
        /// </summary>
        protected override void InitializeStateImageList()
        {
            if (this.DesignMode || !this.CheckBoxes)
            {
                return;
            }

            int width = this.LogicalToDeviceUnits(16);
            var size = new Size(width, this.RowHeight == -1 ? width : this.RowHeight);
            int imageCount = this.TriStateCheckBoxes ? 3 : 2;

            ImageList current = this.StateImageList;
            if (current != null && current.ImageSize == size && current.Images.Count == imageCount)
            {
                return;
            }

            var images = new ImageList
            {
                ImageSize = size,
                ColorDepth = ColorDepth.Depth32Bit,
            };

            AddCheckBoxImage(images, "checkbox-unchecked", CheckBoxState.UncheckedNormal);
            AddCheckBoxImage(images, "checkbox-checked", CheckBoxState.CheckedNormal);
            if (this.TriStateCheckBoxes)
            {
                AddCheckBoxImage(images, "checkbox-indeterminate", CheckBoxState.MixedNormal);
            }

            this.StateImageList = images;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            this.layoutDpi = this.DeviceDpi;

            if (IsDarkMode)
            {
                this.ApplyDarkStyle();
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            // The designer sets GridLines after the list's window may already exist, which undid
            // the change made in OnHandleCreated; apply the dark style again once it's shown.
            if (this.Visible && this.IsHandleCreated && IsDarkMode)
            {
                this.ApplyDarkStyle();
            }
        }

        /// <summary>
        /// ObjectListView draws its header and items itself, so WinForms' dark mode only reaches part
        /// of it: the header stayed white and the group titles were dark blue on black.
        /// </summary>
        private void ApplyDarkStyle()
        {
            // The header is custom drawn whenever the list is sorted, so give it dark colors.
            var header = new HeaderFormatStyle();
            header.SetBackColor(Color.FromArgb(40, 40, 40));
            header.SetForeColor(Color.FromArgb(222, 222, 222));
            header.Hot.BackColor = Color.FromArgb(55, 55, 55);
            header.Pressed.BackColor = Color.FromArgb(70, 70, 70);
            foreach (HeaderStateStyle state in new[] { header.Normal, header.Hot, header.Pressed })
            {
                // A faint outline just separates the columns.
                state.FrameColor = Color.FromArgb(52, 52, 52);
                state.FrameWidth = 1;
            }

            this.HeaderUsesThemes = false;
            this.HeaderFormatStyle = header;

            // Grid lines are drawn light across the dark group rows.
            this.GridLines = false;

            // Explorer's dark items view draws readable group titles. WinForms sets its own dark
            // theme on the next message, so this is queued after it.
            this.BeginInvoke(new Action(() =>
            {
                if (this.IsHandleCreated)
                {
                    _ = SafeNativeMethods.SetWindowTheme(this.Handle, "DarkMode_ItemsView", null);

                    // WinForms re-applies the extended styles for dark mode; make sure the grid
                    // lines it drew across the group rows stay off.
                    _ = SafeNativeMethods.SendMessage(this.Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, (IntPtr)LVS_EX_GRIDLINES, IntPtr.Zero);
                }
            }));
        }

        /// <summary>
        /// WinForms rescales fonts and controls when the window moves to a monitor with another
        /// DPI, but not the list's column widths or checkbox images.
        /// </summary>
        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);

            int oldDpi = this.layoutDpi;
            this.layoutDpi = this.DeviceDpi;
            if (oldDpi == 0 || oldDpi == this.DeviceDpi)
            {
                return;
            }

            foreach (OLVColumn column in this.AllColumns)
            {
                if (column.Width > 0)
                {
                    column.Width = (int)Math.Round(column.Width * (double)this.DeviceDpi / oldDpi);
                }
            }

            // Builds new checkbox images if the size changed.
            this.InitializeStateImageList();
        }

        private static void AddCheckBoxImage(ImageList images, string key, CheckBoxState state)
        {
            var bitmap = new Bitmap(images.ImageSize.Width, images.ImageSize.Height);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(images.TransparentColor);
                if (IsDarkMode)
                {
                    DrawDarkCheckBox(graphics, new Rectangle(Point.Empty, bitmap.Size), state);
                }
                else
                {
                    Size glyph = CheckBoxRenderer.GetGlyphSize(graphics, state);
                    var location = new Point((bitmap.Width - glyph.Width) / 2, (bitmap.Height - glyph.Height) / 2);
                    CheckBoxRenderer.DrawCheckBox(graphics, location, state);
                }
            }

            images.Images.Add(key, bitmap);
        }

        // The themed checkbox is always the light one, so in dark mode draw a simple dark version.
        private static void DrawDarkCheckBox(Graphics graphics, Rectangle bounds, CheckBoxState state)
        {
            int side = Math.Max(9, (int)Math.Round(Math.Min(bounds.Width, bounds.Height) * 0.9));
            var box = new Rectangle(bounds.X + (bounds.Width - side) / 2, bounds.Y + (bounds.Height - side) / 2, side - 1, side - 1);
            // Each of these has Normal, Hot, Pressed and Disabled variants, in that order.
            bool isChecked = state >= CheckBoxState.CheckedNormal && state <= CheckBoxState.CheckedDisabled;
            bool isMixed = state >= CheckBoxState.MixedNormal && state <= CheckBoxState.MixedDisabled;

            System.Drawing.Drawing2D.GraphicsState saved = graphics.Save();
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(isChecked || isMixed ? Color.FromArgb(76, 160, 224) : Color.FromArgb(32, 32, 32)))
            using (var border = new Pen(isChecked || isMixed ? Color.FromArgb(76, 160, 224) : Color.FromArgb(160, 160, 160)))
            {
                graphics.FillRectangle(fill, box);
                graphics.DrawRectangle(border, box);
            }

            using (var mark = new Pen(Color.Black, Math.Max(1.5f, side / 8f)))
            {
                if (isChecked)
                {
                    graphics.DrawLines(mark, new[]
                    {
                        new PointF(box.Left + side * 0.22f, box.Top + side * 0.52f),
                        new PointF(box.Left + side * 0.42f, box.Top + side * 0.72f),
                        new PointF(box.Left + side * 0.78f, box.Top + side * 0.30f),
                    });
                }
                else if (isMixed)
                {
                    graphics.DrawLine(mark, box.Left + side * 0.25f, box.Top + side * 0.5f, box.Left + side * 0.75f, box.Top + side * 0.5f);
                }
            }

            graphics.Restore(saved);
        }

        /// <summary>
        /// Saves the view, sort and column layout. ObjectListView's own implementation uses
        /// BinaryFormatter, which .NET 9 and later no longer support, so this writes JSON instead.
        /// It also keeps the secondary sort column, which the original dropped.
        /// </summary>
        public override byte[] SaveState()
        {
            if (this.AllColumns.Count > 0 && this.AllColumns[0].LastDisplayIndex == -1)
            {
                foreach (OLVColumn column in this.AllColumns)
                {
                    column.LastDisplayIndex = column.DisplayIndex;
                }
            }

            var state = new ViewState
            {
                View = this.View,
                ShowGroups = this.ShowGroups,
                PrimarySortColumn = this.AllColumns.IndexOf(this.PrimarySortColumn),
                PrimarySortOrder = this.PrimarySortOrder,
                SecondarySortColumn = this.AllColumns.IndexOf(this.SecondarySortColumn),
                SecondarySortOrder = this.SecondarySortOrder,
            };

            foreach (OLVColumn column in this.AllColumns)
            {
                state.Columns.Add(new ColumnState
                {
                    IsVisible = column.IsVisible,
                    DisplayIndex = column.LastDisplayIndex,
                    Width = column.Width,
                });
            }

            return JsonSerializer.SerializeToUtf8Bytes(state);
        }

        /// <summary>
        /// Restores a state written by <see cref="SaveState"/>. Returns false, leaving the view
        /// as it is, for anything else, including states saved by older versions.
        /// </summary>
        public override bool RestoreState(byte[] state)
        {
            ViewState viewState;
            try
            {
                viewState = JsonSerializer.Deserialize<ViewState>(state);
            }
            catch (JsonException)
            {
                return false;
            }

            if (viewState?.Columns == null || viewState.Columns.Count != this.AllColumns.Count)
            {
                return false;
            }

            this.PrimarySortColumn = this.GetColumnOrNull(viewState.PrimarySortColumn);
            this.PrimarySortOrder = this.PrimarySortColumn == null ? SortOrder.None : viewState.PrimarySortOrder;
            this.SecondarySortColumn = this.GetColumnOrNull(viewState.SecondarySortColumn);
            this.SecondarySortOrder = this.SecondarySortColumn == null ? SortOrder.None : viewState.SecondarySortOrder;

            for (int i = 0; i < viewState.Columns.Count; i++)
            {
                OLVColumn column = this.AllColumns[i];
                column.Width = viewState.Columns[i].Width;
                column.IsVisible = viewState.Columns[i].IsVisible;
                column.LastDisplayIndex = viewState.Columns[i].DisplayIndex;
            }

            if (viewState.ShowGroups != this.ShowGroups)
            {
                this.ShowGroups = viewState.ShowGroups;
            }

            if (this.View == viewState.View)
            {
                this.RebuildColumns();
            }
            else
            {
                this.View = viewState.View;
            }

            return true;
        }

        /// <summary>
        /// Draws the row checkboxes dark in dark mode. ObjectListView lays out the cell for the
        /// standard checkbox glyph, so this keeps that size and position (a bigger box pushed the
        /// INF names into an ellipsis) and only changes how it looks; in light mode it's untouched.
        /// </summary>
        private sealed class DarkCheckBoxRenderer : HighlightTextRenderer
        {
            protected override int DrawCheckBox(Graphics g, Rectangle r)
            {
                if (!IsDarkMode || this.IsPrinting)
                {
                    return base.DrawCheckBox(g, r);
                }

                Size size = this.CalculateCheckBoxSize(g);
                var bounds = new Rectangle(r.X, this.AlignVertically(r, size.Height), size.Width, size.Height);
                DrawDarkCheckBox(g, bounds, this.GetCheckBoxState(this.ListItem.CheckState));
                return size.Width;
            }
        }

        private OLVColumn GetColumnOrNull(int index) =>
            index >= 0 && index < this.AllColumns.Count ? this.AllColumns[index] : null;

        private sealed class ViewState
        {
            public View View { get; set; }

            public bool ShowGroups { get; set; }

            public int PrimarySortColumn { get; set; } = -1;

            public SortOrder PrimarySortOrder { get; set; }

            public int SecondarySortColumn { get; set; } = -1;

            public SortOrder SecondarySortOrder { get; set; }

            public List<ColumnState> Columns { get; set; } = new List<ColumnState>();
        }

        private sealed class ColumnState
        {
            public bool IsVisible { get; set; }

            public int DisplayIndex { get; set; }

            public int Width { get; set; }
        }
    }
}
