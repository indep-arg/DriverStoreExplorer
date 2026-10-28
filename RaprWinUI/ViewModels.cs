using System.ComponentModel;
using System.Globalization;

using Microsoft.UI.Xaml;

using Rapr.Utils;

namespace Rapr.WinUI
{
    /// <summary>
    /// One package in the list, with its columns already formatted.
    /// </summary>
    public sealed class DriverRow : INotifyPropertyChanged
    {
        private bool isChecked;

        public DriverRow(DriverStoreEntry entry, bool isOlderVersion, bool devicesKnown, string olderTagText, string noDeviceText)
        {
            this.Entry = entry;
            this.IsOlderVersion = isOlderVersion;
            this.OlderTagText = olderTagText;
            this.HasDevice = !string.IsNullOrEmpty(entry.DeviceName);
            this.Device = this.HasDevice ? entry.DeviceName! : devicesKnown ? noDeviceText : string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public DriverStoreEntry Entry { get; }

        public string Inf => this.Entry.DriverInfName ?? this.Entry.DriverPublishedName ?? string.Empty;

        public string Provider => this.Entry.DriverPkgProvider ?? string.Empty;

        public string DriverClass => this.Entry.DriverClass ?? string.Empty;

        public string Version => this.Entry.DriverVersion?.ToString() ?? string.Empty;

        public string Date => this.Entry.DriverDate == default ? string.Empty : this.Entry.DriverDate.ToString("d", CultureInfo.CurrentCulture);

        public string Size => this.Entry.DriverSize >= 0 ? DriverStoreEntry.GetBytesReadable(this.Entry.DriverSize) : string.Empty;

        public bool HasDevice { get; }

        public string Device { get; }

        // "No device" is shown dimmed and in italics, as in the sketches.
        public double DeviceOpacity => this.HasDevice ? 1.0 : 0.6;

        public Windows.UI.Text.FontStyle DeviceFontStyle => this.HasDevice ? Windows.UI.Text.FontStyle.Normal : Windows.UI.Text.FontStyle.Italic;

        public bool IsOlderVersion { get; }

        public string OlderTagText { get; }

        public Visibility OlderTagVisibility => this.IsOlderVersion ? Visibility.Visible : Visibility.Collapsed;

        public bool IsChecked
        {
            get => this.isChecked;
            set
            {
                if (this.isChecked != value)
                {
                    this.isChecked = value;
                    this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.IsChecked)));
                    this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(this.CheckedTint)));
                }
            }
        }

        public double CheckedTint => this.isChecked ? 0.16 : 0.0;
    }

    /// <summary>
    /// A view (all packages, older versions...) or a driver class in the Explorer layout.
    /// </summary>
    public sealed class NavigationItem
    {
        public NavigationItem(string text, int count, DriverView view, string? className)
        {
            this.Text = text;
            this.Count = count;
            this.View = view;
            this.ClassName = className;
        }

        public string Text { get; }

        public int Count { get; }

        public string CountText => this.Count.ToString(CultureInfo.CurrentCulture);

        public DriverView View { get; }

        public string? ClassName { get; }
    }

    /// <summary>
    /// A label and value in the details pane.
    /// </summary>
    public sealed class DetailField
    {
        public DetailField(string label, string value)
        {
            this.Label = label;
            this.Value = value;
        }

        public string Label { get; }

        public string Value { get; }
    }
}
