using System.Collections.Generic;
using System.Text.Json;
using System.Windows.Forms;

using BrightIdeasSoftware;

using Rapr.Utils;

namespace Rapr
{
    public sealed class MyObjectListView : TypedObjectListView<DriverStoreEntry>
    {
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
