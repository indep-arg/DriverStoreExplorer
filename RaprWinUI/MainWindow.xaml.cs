using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.Storage.Pickers;

using Rapr.Lang;
using Rapr.Utils;

using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace Rapr.WinUI
{
    public sealed partial class MainWindow : Window
    {
        private const string ExplorerLayoutName = "Explorer";
        private const string CompactLayoutName = "Compact";

        private readonly AppSettings settings = AppSettings.Current;
        private readonly Dictionary<Button, (string Key, string Label)> sortHeaders = new Dictionary<Button, (string, string)>();

        private List<DriverRow> allRows = new List<DriverRow>();
        private List<DriverRow> visibleRows = new List<DriverRow>();
        private DriverInsights insights = DriverInsights.Empty;
        private IDriverStore driverStore;
        private string backend;
        private DriverView viewFilter = DriverView.All;
        private string? classFilter;
        private string sortKey = "Class";
        private bool sortDescending;
        private bool busy;
        private bool updatingNavigation;
        private bool dialogOpen;
        private bool loaded;

        // Below this the list gets squeezed into a few pixels and the menu runs into the caption buttons.
        private const int MinWindowWidth = 1200;
        private const int MinWindowHeight = 600;

        public MainWindow()
        {
            this.InitializeComponent();

            this.backend = BackendFactory.Validate(this.settings.DriverStoreOption);
            this.driverStore = BackendFactory.CreateOnline(this.backend);

            this.Title = Language.Product_Name;
            this.ExtendsContentIntoTitleBar = true;
            this.SetTitleBar(this.AppTitleBar);
            this.AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            this.SystemBackdrop = new MicaBackdrop();
            this.SetWindowIconAndSize();

            // The receiver has to be in the visual tree, which it only is once the compact layout is shown.
            this.CompactListCard.Loaded += (sender, e) =>
            {
                try
                {
                    if (this.SelectionBarShadow.Receivers.Count == 0)
                    {
                        this.SelectionBarShadow.Receivers.Add(this.CompactListCard);
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is System.Runtime.InteropServices.COMException)
                {
                    Trace.TraceWarning($"Selection bar shadow not attached: {ex.Message}");
                }
            };

            this.ApplyStrings();
            this.BuildLanguageMenu();
            this.ApplyTheme(this.settings.Theme);
            this.ApplyLayout(this.settings.Layout, save: false);
            this.UpdateMenuChecks();
            this.UpdateTitle();

            var findAccelerator = new KeyboardAccelerator { Key = VirtualKey.F, Modifiers = VirtualKeyModifiers.Control };
            findAccelerator.Invoked += (sender, e) =>
            {
                this.SearchBox.Focus(FocusState.Keyboard);
                e.Handled = true;
            };
            this.RootGrid.KeyboardAccelerators.Add(findAccelerator);

            // Without this WinUI pops a "Ctrl+F" tooltip over whatever element has the focus.
            this.RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

            this.RootGrid.ActualThemeChanged += (sender, e) => this.UpdateCaptionButtonColors();
            this.AppTitleBar.SizeChanged += (sender, e) => this.UpdateTitleBarRegions();
            this.MainMenu.SizeChanged += (sender, e) => this.UpdateTitleBarRegions();
            this.RootGrid.SizeChanged += (sender, e) => this.UpdateExplorerColumns();
            this.RootGrid.Loaded += async (sender, e) =>
            {
                this.UpdateTitleBarRegions();

                // The minimum is in device pixels, so it has to follow the scale when the window changes monitor.
                this.ApplyMinimumSize();
                this.RootGrid.XamlRoot.Changed += (root, args) => this.ApplyMinimumSize();

                await this.RefreshAsync();
            };
        }

        private static string Plain(string text) => text.Replace("&", string.Empty, StringComparison.Ordinal);

        private static IReadOnlyList<(DriverView View, string Text)> ViewNames(bool shortAll) => new[]
        {
            (DriverView.All, Strings.Get(shortAll ? "View_All_Short" : "View_All")),
            (DriverView.OlderVersions, Strings.Get("View_Older_Versions")),
            (DriverView.NoDevice, Strings.Get("View_No_Device")),
            (DriverView.BootCritical, Strings.Get("View_Boot_Critical")),
            (DriverView.Extensions, Strings.Get("View_Extensions")),
        };

        private bool IsCompact => this.settings.Layout == CompactLayoutName;

        #region Window setup

        private void SetWindowIconAndSize()
        {
            string icon = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(icon))
            {
                this.AppWindow.SetIcon(icon);
            }

            // The sketches are 1440 x 900; keep that, but never larger than the screen.
            IntPtr hwnd = Win32Interop.GetWindowFromWindowId(this.AppWindow.Id);
            double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
            RectInt32 workArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            int width = Math.Min((int)(1440 * scale), (int)(workArea.Width * 0.95));
            int height = Math.Min((int)(900 * scale), (int)(workArea.Height * 0.95));
            this.AppWindow.MoveAndResize(new RectInt32(
                workArea.X + ((workArea.Width - width) / 2),
                workArea.Y + ((workArea.Height - height) / 2),
                width,
                height));
        }

        private void ApplyMinimumSize()
        {
            if (!(this.AppWindow.Presenter is OverlappedPresenter presenter))
            {
                return;
            }

            IntPtr hwnd = Win32Interop.GetWindowFromWindowId(this.AppWindow.Id);
            double scale = this.RootGrid.XamlRoot?.RasterizationScale ?? (NativeMethods.GetDpiForWindow(hwnd) / 96.0);
            RectInt32 workArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

            // On a small screen the minimum can't be larger than the screen itself.
            presenter.PreferredMinimumWidth = Math.Min((int)Math.Ceiling(MinWindowWidth * scale), workArea.Width);
            presenter.PreferredMinimumHeight = Math.Min((int)Math.Ceiling(MinWindowHeight * scale), workArea.Height);
        }

        // With the window near its minimum the side panels give up some width to the list.
        private void UpdateExplorerColumns()
        {
            bool narrow = this.RootGrid.ActualWidth < 1400;
            this.ExplorerNavColumn.Width = new GridLength(narrow ? 220 : 260);
            this.ExplorerDetailsColumn.Width = new GridLength(narrow ? 290 : 340);
        }

        // The menu sits in the title bar; let it get the clicks instead of dragging the window.
        private void UpdateTitleBarRegions()
        {
            if (this.RootGrid.XamlRoot == null || this.MainMenu.ActualWidth <= 0)
            {
                return;
            }

            double scale = this.RootGrid.XamlRoot.RasterizationScale;
            this.AppTitleBar.Padding = new Thickness(16, 0, this.AppWindow.TitleBar.RightInset / scale, 0);

            Rect bounds = this.MainMenu.TransformToVisual(null).TransformBounds(
                new Rect(0, 0, this.MainMenu.ActualWidth, this.MainMenu.ActualHeight));
            var menuRect = new RectInt32(
                (int)Math.Round(bounds.X * scale),
                (int)Math.Round(bounds.Y * scale),
                (int)Math.Round(bounds.Width * scale),
                (int)Math.Round(bounds.Height * scale));
            InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id)
                .SetRegionRects(NonClientRegionKind.Passthrough, new[] { menuRect });
        }

        private void ApplyStrings()
        {
            this.FileMenu.Title = Plain(Language.Menu_File);
            this.OptionsMenu.Title = Plain(Language.Menu_Options);
            this.LanguageMenu.Title = Plain(Language.Menu_Language);
            this.HelpMenu.Title = Plain(Language.Menu_Help);
            this.ChooseStoreItem.Text = Plain(Language.Menu_File_Choose_Driver_Store);
            this.ExportListItem.Text = Plain(Language.Menu_File_Export_All_Driver_List);
            this.ExitItem.Text = Plain(Language.Menu_File_Exit);
            this.BackendMenu.Text = Strings.Get("Menu_Options_Backend");
            this.NativeBackendItem.Text = BackendFactory.MenuText(BackendFactory.Native);
            this.DismBackendItem.Text = BackendFactory.MenuText(BackendFactory.Dism);
            this.DismBackendItem.IsEnabled = DismUtil.IsDismAvailable;
            this.PnpUtilBackendItem.Text = BackendFactory.MenuText(BackendFactory.PnpUtil);
            this.LayoutMenu.Text = Strings.Get("Menu_Options_Layout");
            this.ExplorerLayoutItem.Text = Strings.Get("Menu_Options_Layout_Explorer");
            this.CompactLayoutItem.Text = Strings.Get("Menu_Options_Layout_Compact");
            this.ThemeMenu.Text = Strings.Get("Menu_Options_Theme");
            this.SystemThemeItem.Text = Strings.Get("Menu_Options_Theme_System");
            this.LightThemeItem.Text = Strings.Get("Menu_Options_Theme_Light");
            this.DarkThemeItem.Text = Strings.Get("Menu_Options_Theme_Dark");
            this.AboutItem.Text = Plain(Language.Menu_Help_About);

            this.RefreshButton.Label = Plain(Language.Button_Refresh);
            this.AddButton.Label = Strings.Get("WinUI_Toolbar_Add");
            this.SelectOldButton.Label = Strings.Get("WinUI_Toolbar_Select_Old");
            this.ExportAllButton.Label = Strings.Get("WinUI_Toolbar_Export_All");
            this.SearchBox.PlaceholderText = Strings.Get("WinUI_Search_Placeholder");

            this.ViewsHeader.Text = Strings.Get("Navigation_Views");
            this.ClassesHeader.Text = Strings.Get("Navigation_Classes");

            this.sortHeaders[this.ExplorerInfHeader] = ("Inf", Language.Column_Inf);
            this.sortHeaders[this.ExplorerProviderHeader] = ("Provider", Language.Column_Provider);
            this.sortHeaders[this.ExplorerVersionHeader] = ("Version", Language.Column_Version);
            this.sortHeaders[this.ExplorerDateHeader] = ("Date", Language.Column_Date);
            this.sortHeaders[this.ExplorerSizeHeader] = ("Size", Language.Column_Size);
            this.sortHeaders[this.ExplorerDeviceHeader] = ("Device", Language.Column_DeviceName);
            this.sortHeaders[this.CompactInfHeader] = ("Inf", Language.Column_Inf);
            this.sortHeaders[this.CompactClassHeader] = ("Class", Language.Column_Driver_Class);
            this.sortHeaders[this.CompactProviderHeader] = ("Provider", Language.Column_Provider);
            this.sortHeaders[this.CompactVersionHeader] = ("Version", Language.Column_Version);
            this.sortHeaders[this.CompactDateHeader] = ("Date", Language.Column_Date);
            this.sortHeaders[this.CompactSizeHeader] = ("Size", Language.Column_Size);
            this.sortHeaders[this.CompactDeviceHeader] = ("Device", Language.Column_DeviceName);
            this.UpdateSortHeaders();

            this.DetailsOlderTagText.Text = Strings.Get("Tag_Older_Version");
            this.DetailsNoDeviceTagText.Text = Strings.Get("View_No_Device");
            this.DetailsBootCriticalTagText.Text = Strings.Get("View_Boot_Critical");
            this.DetailsExtensionTagText.Text = Strings.Get("Tag_Extension");
            this.DetailsEmpty.Text = Strings.Get("Details_Empty");
            this.DetailsFolderLabel.Text = Strings.Get("Details_Folder");
            this.DetailsExportButton.Content = Strings.Get("Action_Export");
            this.DetailsDeleteButton.Content = Language.Context_Delete + "…";

            this.SelectionExportButton.Content = Strings.Get("Action_Export");
            this.SelectionDeleteButton.Content = Language.Context_Delete + "…";
            this.SelectionClearButton.Content = Strings.Get("Selection_Clear");
        }

        private void UpdateTitle()
        {
            bool online = this.driverStore.Type == DriverStoreType.Online;
            this.WindowTitle.Text = online
                ? Language.Product_Name
                : $"{Language.Product_Name} - {Language.DriverStore_Offline} ({this.driverStore.OfflineStoreLocation})";

            string store = online
                ? Strings.Get("WinUI_Status_Local")
                : $"{Strings.Get("WinUI_Status_Offline")} ({this.driverStore.OfflineStoreLocation})";
            string backendInUse = !online && this.backend == BackendFactory.PnpUtil ? BackendFactory.Dism : this.backend;
            this.BackendText.Text = $"{store} · {BackendFactory.DisplayName(backendInUse)}";
        }

        private void BuildLanguageMenu()
        {
            var cultures = new List<(string Tag, string Text)> { (string.Empty, Strings.Get("Menu_Options_Theme_System")) };
            foreach (string name in new[] { "en-US" }.Concat(TranslatedCultures()))
            {
                try
                {
                    cultures.Add((name, CultureInfo.GetCultureInfo(name).NativeName));
                }
                catch (CultureNotFoundException)
                {
                }
            }

            foreach ((string tag, string text) in cultures)
            {
                var item = new RadioMenuFlyoutItem
                {
                    Text = text,
                    Tag = tag,
                    GroupName = "Language",
                    IsChecked = string.Equals(tag, this.settings.Language ?? string.Empty, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += this.Language_Click;
                this.LanguageMenu.Items.Add(item);

                if (tag.Length == 0)
                {
                    this.LanguageMenu.Items.Add(new MenuFlyoutSeparator());
                }
            }
        }

        // One folder per translation next to the exe, each with DriverStoreExplorer.resources.dll.
        private static IEnumerable<string> TranslatedCultures()
        {
            string resources = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "DriverStoreExplorer") + ".resources.dll";
            try
            {
                return Directory.GetDirectories(AppContext.BaseDirectory)
                    .Where(folder => File.Exists(Path.Combine(folder, resources)))
                    .Select(folder => Path.GetFileName(folder))
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Translations could not be listed: {ex.Message}");
                return Array.Empty<string>();
            }
        }

        private void UpdateMenuChecks()
        {
            this.NativeBackendItem.IsChecked = this.backend == BackendFactory.Native;
            this.DismBackendItem.IsChecked = this.backend == BackendFactory.Dism;
            this.PnpUtilBackendItem.IsChecked = this.backend == BackendFactory.PnpUtil;
            this.ExplorerLayoutItem.IsChecked = !this.IsCompact;
            this.CompactLayoutItem.IsChecked = this.IsCompact;
            this.SystemThemeItem.IsChecked = this.settings.Theme != "Light" && this.settings.Theme != "Dark";
            this.LightThemeItem.IsChecked = this.settings.Theme == "Light";
            this.DarkThemeItem.IsChecked = this.settings.Theme == "Dark";
        }

        private void ApplyTheme(string theme)
        {
            this.RootGrid.RequestedTheme = theme == "Light" ? ElementTheme.Light : theme == "Dark" ? ElementTheme.Dark : ElementTheme.Default;
            this.UpdateCaptionButtonColors();
        }

        // The minimize, maximize and close buttons follow the app's theme, not only Windows'.
        private void UpdateCaptionButtonColors()
        {
            AppWindowTitleBar titleBar = this.AppWindow.TitleBar;
            bool dark = this.RootGrid.ActualTheme == ElementTheme.Dark;
            Windows.UI.Color foreground = dark ? Colors.White : Colors.Black;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = dark ? Windows.UI.Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A) : Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80);
            titleBar.ButtonHoverBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x10, 0x00, 0x00, 0x00);
            titleBar.ButtonPressedBackgroundColor = dark ? Windows.UI.Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF) : Windows.UI.Color.FromArgb(0x0A, 0x00, 0x00, 0x00);
        }

        private void ApplyLayout(string layout, bool save)
        {
            bool compact = layout == CompactLayoutName;
            this.settings.Layout = compact ? CompactLayoutName : ExplorerLayoutName;
            if (save)
            {
                this.settings.Save();
            }

            this.ExplorerLayout.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            this.CompactLayout.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

            // Each layout starts from all packages. Compact shows the class column, so it sorts by class;
            // Explorer has the classes in the side panel, so it sorts by name.
            this.sortKey = compact ? "Class" : "Inf";
            this.sortDescending = false;
            this.UpdateSortHeaders();
            this.viewFilter = DriverView.All;
            this.classFilter = null;
            this.UpdateNavigation();
            this.UpdateChips();
            this.ApplyFilter();
        }

        #endregion

        #region Loading, filtering and sorting

        private async Task RefreshAsync()
        {
            if (this.busy)
            {
                return;
            }

            this.SetBusy(true, Language.Message_Scanning_Driver_Store);
            try
            {
                IDriverStore store = this.driverStore;
                List<DriverStoreEntry> entries = await Task.Run(() => store.EnumeratePackages());
                this.LoadEntries(entries);
                this.SetBusy(false, string.Format(CultureInfo.CurrentCulture, Language.Status_Packages_Found, entries.Count));
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                this.SetBusy(false, ex.Message);
                await this.ShowMessageAsync(Language.Message_Error, ex.Message, ex.ToString());
            }
        }

        private void LoadEntries(IEnumerable<DriverStoreEntry> entries)
        {
            foreach (DriverRow row in this.allRows)
            {
                row.PropertyChanged -= this.Row_PropertyChanged;
            }

            this.loaded = true;
            this.insights = new DriverInsights(entries, this.driverStore.SupportDeviceNameColumn);
            string olderText = Strings.Get("WinUI_Tag_Older_Short");
            string noDeviceText = Strings.Get("View_No_Device");
            this.allRows = this.insights.Entries
                .Select(entry => new DriverRow(entry, this.insights.IsOlderVersion(entry), this.insights.DevicesKnown, olderText, noDeviceText))
                .ToList();

            foreach (DriverRow row in this.allRows)
            {
                row.PropertyChanged += this.Row_PropertyChanged;
            }

            this.UpdateNavigation();
            this.UpdateChips();
            this.ApplyFilter();
        }

        private bool IsViewAvailable(DriverView view) =>
            this.insights.DevicesKnown || (view != DriverView.OlderVersions && view != DriverView.NoDevice);

        private void UpdateNavigation()
        {
            // Until the first scan there are no counts to show, only zeros and a shorter list of views.
            if (!this.loaded)
            {
                return;
            }

            this.updatingNavigation = true;
            try
            {
                var views = ViewNames(shortAll: false)
                    .Where(pair => this.IsViewAvailable(pair.View))
                    .Select(pair => new NavigationItem(pair.Text, this.insights.Count(pair.View), pair.View, null))
                    .ToList();
                string unknownClass = Strings.Get("Navigation_Unknown_Class");
                var classes = this.insights.CountByClass()
                    .Select(pair => new NavigationItem(pair.Key.Length == 0 ? unknownClass : pair.Key, pair.Value, DriverView.All, pair.Key))
                    .ToList();

                this.ViewsList.ItemsSource = views;
                this.ClassesList.ItemsSource = classes;

                NavigationItem? selectedClass = this.classFilter == null ? null : classes.FirstOrDefault(item => item.ClassName == this.classFilter);
                if (selectedClass != null)
                {
                    this.ClassesList.SelectedItem = selectedClass;
                    return;
                }

                // The class or view is gone (deleted, or an offline image): back to all packages.
                this.classFilter = null;
                NavigationItem selectedView = views.FirstOrDefault(item => item.View == this.viewFilter) ?? views[0];
                this.viewFilter = selectedView.View;
                this.ViewsList.SelectedItem = selectedView;
            }
            finally
            {
                this.updatingNavigation = false;
            }
        }

        private void UpdateChips()
        {
            this.ChipPanel.Children.Clear();
            if (!this.loaded)
            {
                return;
            }

            var style = (Style)Application.Current.Resources["ChipStyle"];

            foreach ((DriverView view, string text) in ViewNames(shortAll: true).Where(pair => this.IsViewAvailable(pair.View)))
            {
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                content.Children.Add(new TextBlock { Text = text });
                content.Children.Add(new TextBlock { Text = this.insights.Count(view).ToString(CultureInfo.CurrentCulture), Opacity = 0.75 });

                var chip = new ToggleButton
                {
                    Style = style,
                    Content = content,
                    Tag = view,
                    IsChecked = view == this.viewFilter && this.classFilter == null,
                };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(chip, text);
                chip.Click += this.Chip_Click;
                this.ChipPanel.Children.Add(chip);
            }
        }

        private void SetFilter(DriverView view, string? className)
        {
            this.viewFilter = view;
            this.classFilter = className;
            this.ApplyFilter();
        }

        private void ApplyFilter()
        {
            string search = this.SearchBox.Text?.Trim() ?? string.Empty;
            DriverRow? selected = this.ExplorerList.SelectedItem as DriverRow;

            IEnumerable<DriverRow> rows = this.allRows.Where(row =>
                this.insights.Matches(row.Entry, this.viewFilter)
                && (this.classFilter == null || string.Equals(row.Entry.DriverClass ?? string.Empty, this.classFilter, StringComparison.Ordinal))
                && MatchesSearch(row.Entry, search));

            this.visibleRows = this.Sort(rows).ToList();
            this.ExplorerList.ItemsSource = this.visibleRows;
            this.CompactList.ItemsSource = this.visibleRows;

            if (selected != null && this.visibleRows.Contains(selected))
            {
                this.ExplorerList.SelectedItem = selected;
            }

            this.UpdateEmptyState();
            this.UpdateListHeader();
            this.UpdateCheckAll();
            this.UpdateDetails();
            this.UpdateSelectionBar();
        }

        // Not while scanning: the list is empty then, but only because it has not been read yet.
        private void UpdateEmptyState()
        {
            string emptyText = this.allRows.Count == 0 ? Language.Message_No_Entries : Language.Message_No_Match_Result;
            Visibility emptyVisibility = this.visibleRows.Count == 0 && !this.busy ? Visibility.Visible : Visibility.Collapsed;
            this.ExplorerEmpty.Text = emptyText;
            this.CompactEmpty.Text = emptyText;
            this.ExplorerEmpty.Visibility = emptyVisibility;
            this.CompactEmpty.Visibility = emptyVisibility;
        }

        private static bool MatchesSearch(DriverStoreEntry entry, string search)
        {
            if (search.Length == 0)
            {
                return true;
            }

            bool Contains(string? value) => value?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;

            return Contains(entry.DriverPublishedName)
                || Contains(entry.DriverInfName)
                || Contains(entry.DriverClass)
                || Contains(entry.DriverPkgProvider)
                || Contains(entry.DriverSignerName)
                || Contains(entry.DriverVersion?.ToString())
                || Contains(entry.DriverDate.ToString("d", CultureInfo.CurrentCulture))
                || Contains(entry.DeviceName)
                || Contains(entry.DeviceId)
                || Contains(entry.InstallDate?.ToString("d", CultureInfo.CurrentCulture))
                || Contains(entry.DriverExtensionId.ToString())
                || (entry.DriverFiles?.Any(Contains) ?? false);
        }

        private IEnumerable<DriverRow> Sort(IEnumerable<DriverRow> rows)
        {
            Func<DriverRow, IComparable?> key = this.sortKey switch
            {
                "Inf" => row => row.Inf,
                "Provider" => row => row.Provider,
                "Version" => row => row.Entry.DriverVersion,
                "Date" => row => row.Entry.DriverDate,
                "Size" => row => row.Entry.DriverSize,
                "Device" => row => row.Entry.DeviceName ?? string.Empty,
                _ => row => row.DriverClass,
            };

            IComparer<IComparable?> comparer = Comparer<IComparable?>.Create(CompareValues);
            IOrderedEnumerable<DriverRow> ordered = this.sortDescending
                ? rows.OrderByDescending(key, comparer)
                : rows.OrderBy(key, comparer);

            // Newest first within the same value, like the WinForms list.
            return ordered.ThenByDescending(row => row.Entry.DriverDate);
        }

        private static int CompareValues(IComparable? x, IComparable? y)
        {
            if (x is string a && y is string b)
            {
                return string.Compare(a, b, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
            }

            if (x == null)
            {
                return y == null ? 0 : -1;
            }

            return y == null ? 1 : x.CompareTo(y);
        }

        private void UpdateSortHeaders()
        {
            foreach (KeyValuePair<Button, (string Key, string Label)> pair in this.sortHeaders)
            {
                // The label trims with an ellipsis when a translation is longer than its column.
                var content = new Grid
                {
                    ColumnSpacing = 4,
                    HorizontalAlignment = pair.Key.HorizontalContentAlignment == HorizontalAlignment.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                };
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                content.Children.Add(new TextBlock { Text = pair.Value.Label, TextTrimming = TextTrimming.CharacterEllipsis });

                if (pair.Value.Key == this.sortKey)
                {
                    // Same chevrons Windows uses in its own lists (Segoe Fluent Icons: ChevronUpSmall / ChevronDownSmall).
                    var chevron = new FontIcon
                    {
                        Glyph = this.sortDescending ? "\uE70D" : "\uE70E",
                        FontSize = 10,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    Grid.SetColumn(chevron, 1);
                    content.Children.Add(chevron);
                }

                ToolTipService.SetToolTip(pair.Key, pair.Value.Label);
                pair.Key.Content = content;
            }
        }

        private void UpdateListHeader()
        {
            string title = this.classFilter != null
                ? (this.classFilter.Length == 0 ? Strings.Get("Navigation_Unknown_Class") : this.classFilter)
                : ViewNames(shortAll: false).First(pair => pair.View == this.viewFilter).Text;
            this.ExplorerTitle.Text = title;
            this.ExplorerSummary.Text = this.loaded ? Summary(this.visibleRows) : string.Empty;
        }

        private static string Summary(IReadOnlyCollection<DriverRow> rows) =>
            string.Format(CultureInfo.CurrentCulture, Strings.Get("Summary_Packages"), rows.Count,
                DriverStoreEntry.GetBytesReadable(rows.Sum(row => Math.Max(row.Entry.DriverSize, 0))));

        #endregion

        #region Checked packages

        private List<DriverStoreEntry> CheckedEntries() =>
            this.visibleRows.Where(row => row.IsChecked).Select(row => row.Entry).ToList();

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DriverRow.IsChecked))
            {
                this.UpdateCheckAll();
                this.UpdateSelectionBar();
                this.UpdateCheckedStatus();
            }
        }

        private void UpdateCheckAll()
        {
            int checkedCount = this.visibleRows.Count(row => row.IsChecked);
            bool? state = checkedCount == 0 ? false : checkedCount == this.visibleRows.Count ? true : null;
            this.ExplorerCheckAll.IsChecked = state;
            this.CompactCheckAll.IsChecked = state;
        }

        private void UpdateCheckedStatus()
        {
            if (this.busy)
            {
                return;
            }

            List<DriverStoreEntry> entries = this.CheckedEntries();
            this.StatusText.Text = entries.Count == 0
                ? Summary(this.allRows)
                : string.Format(CultureInfo.CurrentCulture, Language.Status_Selected_Drivers, entries.Count,
                    DriverStoreEntry.GetBytesReadable(entries.Sum(entry => Math.Max(entry.DriverSize, 0))));
        }

        private void UpdateSelectionBar()
        {
            List<DriverStoreEntry> entries = this.CheckedEntries();
            bool show = this.IsCompact && entries.Count > 0 && !this.busy;
            this.SelectionBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show)
            {
                return;
            }

            int bootCritical = entries.Count(DriverInsights.IsBootCritical);
            string detail = DriverStoreEntry.GetBytesReadable(entries.Sum(entry => Math.Max(entry.DriverSize, 0)));
            if (bootCritical > 0)
            {
                detail += " · " + string.Format(CultureInfo.CurrentCulture, Strings.Get("Selection_Boot_Critical"), bootCritical);
            }

            this.SelectionCount.Text = string.Format(CultureInfo.CurrentCulture, Strings.Get("Selection_Count"), entries.Count);
            this.SelectionDetail.Text = detail;
            this.SelectionExportButton.IsEnabled = this.driverStore.SupportExportDriver;
        }

        #endregion

        #region Details pane

        private void UpdateDetails()
        {
            DriverRow? row = this.IsCompact ? null : this.ExplorerList.SelectedItem as DriverRow;
            this.DetailsEmptyPanel.Visibility = row == null ? Visibility.Visible : Visibility.Collapsed;
            this.DetailsPanel.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
            if (row == null)
            {
                return;
            }

            DriverStoreEntry entry = row.Entry;
            this.DetailsTitle.Text = row.Inf;
            this.DetailsSubtitle.Text = string.Join(" · ", new[] { row.Provider, row.Version, row.Date }.Where(part => part.Length > 0));

            static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
            this.DetailsOlderTag.Visibility = Show(this.insights.IsOlderVersion(entry));
            this.DetailsNoDeviceTag.Visibility = Show(this.insights.HasNoDevice(entry));
            this.DetailsBootCriticalTag.Visibility = Show(DriverInsights.IsBootCritical(entry));
            this.DetailsExtensionTag.Visibility = Show(DriverInsights.IsExtension(entry));

            var notes = new List<string>();
            DriverStoreEntry? newer = this.insights.GetNewerVersion(entry);
            if (newer != null)
            {
                notes.Add(string.IsNullOrEmpty(newer.DeviceName)
                    ? string.Format(CultureInfo.CurrentCulture, Strings.Get("Details_Newer_Installed"), newer.DriverVersion)
                    : string.Format(CultureInfo.CurrentCulture, Strings.Get("Details_Newer_In_Use"), newer.DriverVersion, newer.DeviceName));
            }

            if (!string.IsNullOrEmpty(entry.DeviceName))
            {
                notes.Add(string.Format(CultureInfo.CurrentCulture, Strings.Get("Details_In_Use"), entry.DeviceName));
            }
            else if (this.insights.HasNoDevice(entry))
            {
                notes.Add(Strings.Get("Details_No_Device"));
            }

            if (DriverInsights.IsBootCritical(entry))
            {
                notes.Add(Strings.Get("Details_Boot_Critical"));
            }

            this.DetailsNotes.Text = string.Join(Environment.NewLine + Environment.NewLine, notes);
            this.DetailsNotes.Visibility = Show(notes.Count > 0);

            var fields = new List<DetailField>();
            void AddField(string label, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                {
                    fields.Add(new DetailField(label, value));
                }
            }

            AddField(Language.Column_Oem_Inf, entry.DriverPublishedName);
            AddField(Language.Column_Driver_Class, entry.DriverClass);
            AddField(Language.Column_Size, row.Size);
            AddField(Language.Column_InstallDate, entry.InstallDate?.ToString("d", CultureInfo.CurrentCulture));
            AddField(Language.Column_Signed_By, entry.DriverSignerName);
            AddField(Language.Column_DeviceId, entry.DeviceId);
            AddField(Language.Column_Extension_Id, DriverInsights.IsExtension(entry) ? entry.DriverExtensionId.ToString("B") : null);
            this.DetailsFields.ItemsSource = fields;

            bool hasFolder = !string.IsNullOrEmpty(entry.DriverFolderLocation);
            this.DetailsFolderLabel.Visibility = Show(hasFolder);
            this.DetailsFolderLink.Visibility = Show(hasFolder);
            string? folderName = Path.GetFileName(entry.DriverFolderLocation);
            this.DetailsFolderLink.Content = new TextBlock { Text = folderName, TextTrimming = TextTrimming.CharacterEllipsis };
            ToolTipService.SetToolTip(this.DetailsFolderLink, entry.DriverFolderLocation);

            this.DetailsExportButton.IsEnabled = !this.busy && this.driverStore.SupportExportDriver;
            this.DetailsDeleteButton.IsEnabled = !this.busy;
        }

        #endregion

        #region Operations

        private void SetBusy(bool value, string? status)
        {
            this.busy = value;
            this.BusyBar.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            this.Commands.IsEnabled = !value;
            this.MainMenu.IsEnabled = !value;
            this.SearchBox.IsEnabled = !value;
            this.ExportAllButton.IsEnabled = !value && this.driverStore.SupportExportAllDrivers;
            this.AddButton.IsEnabled = !value;
            this.SelectOldButton.IsEnabled = !value && this.driverStore.SupportDeviceNameColumn;

            if (status != null)
            {
                this.StatusText.Text = status;
            }

            this.UpdateEmptyState();
            this.UpdateSelectionBar();
            this.UpdateDetails();
        }

        private async Task DeleteAsync(IReadOnlyList<DriverStoreEntry> entries)
        {
            if (entries.Count == 0 || this.busy)
            {
                return;
            }

            DriverStoreEntry first = entries[0];
            string heading = entries.Count == 1
                ? string.Format(CultureInfo.CurrentCulture, Language.Message_Delete_Single_Package, first.DriverInfName,
                    $"{first.DriverVersion}, {DriverStoreEntry.GetBytesReadable(first.DriverSize)}")
                : string.Format(CultureInfo.CurrentCulture, Language.Message_Delete_Multiple_Packages, entries.Count);

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = heading, TextWrapping = TextWrapping.Wrap });
            if (entries.Count > 1)
            {
                content.Children.Add(PackageList(entries));
            }

            int bootCritical = entries.Count(DriverInsights.IsBootCritical);
            if (bootCritical > 0)
            {
                content.Children.Add(new InfoBar
                {
                    Severity = InfoBarSeverity.Warning,
                    IsOpen = true,
                    IsClosable = false,
                    Message = string.Format(CultureInfo.CurrentCulture, Strings.Get("Delete_Boot_Critical_Warning"), bootCritical),
                });
            }

            CheckBox? forceBox = null;
            if (this.driverStore.SupportForceDeletion)
            {
                forceBox = new CheckBox { Content = Plain(Language.Check_Force_Delete) };
                content.Children.Add(forceBox);
            }

            if (await this.ShowDialogAsync(Plain(Language.Button_Delete_Package), content, Language.Context_Delete, primaryIsDefault: false) != ContentDialogResult.Primary)
            {
                return;
            }

            bool force = forceBox?.IsChecked == true;
            this.SetBusy(true, Language.Status_Deleting_Packages);
            IDriverStore store = this.driverStore;
            List<(DriverStoreEntry Entry, bool Succeeded)> results = await Task.Run(() =>
                entries.Select(entry => (entry, Run(() => store.DeleteDriver(entry, force)))).ToList());
            this.SetBusy(false, null);

            await this.RefreshAsync();

            var failed = results.Where(result => !result.Succeeded).Select(result => result.Entry).ToList();
            if (failed.Count == 0)
            {
                this.StatusText.Text = entries.Count == 1
                    ? string.Format(CultureInfo.CurrentCulture, Language.Message_Delete_Package, first.DriverInfName, first.DriverPublishedName)
                    : string.Format(CultureInfo.CurrentCulture, Language.Message_Delete_Packages, entries.Count);
                return;
            }

            string message = entries.Count == 1
                ? string.Format(CultureInfo.CurrentCulture, Language.Message_Delete_Package_Error, first.DriverInfName, first.DriverPublishedName)
                : Language.Message_Delete_Packages_Error;
            if (!force)
            {
                message += Environment.NewLine + Environment.NewLine + Language.Tip_Driver_In_Use;
            }

            this.StatusText.Text = message;
            await this.ShowMessageAsync(Language.Message_Error, message,
                string.Join(Environment.NewLine, failed.Select(entry => $"{entry.DriverInfName} ({entry.DriverPublishedName})")));
        }

        private async Task ExportAsync(IReadOnlyList<DriverStoreEntry> entries)
        {
            if (entries.Count == 0 || this.busy)
            {
                return;
            }

            string? folder = await this.PickFolderAsync();
            if (folder == null)
            {
                return;
            }

            this.SetBusy(true, Language.Status_Exporting_Drivers);
            IDriverStore store = this.driverStore;
            List<DriverStoreEntry> failed = await Task.Run(() =>
                entries.Where(entry => !Run(() => store.ExportDriver(entry, folder))).ToList());
            this.SetBusy(false, failed.Count == 0 ? Language.Message_Export_Drivers_Success : Language.Message_Export_Drivers_Error);

            if (failed.Count > 0)
            {
                await this.ShowMessageAsync(Language.Message_Error, Language.Message_Export_Drivers_Error,
                    string.Join(Environment.NewLine, failed.Select(entry => $"{entry.DriverInfName} ({entry.DriverPublishedName})")));
            }
        }

        private static bool Run(Func<bool> operation)
        {
            try
            {
                return operation();
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.ToString());
                return false;
            }
        }

        private static ScrollViewer PackageList(IEnumerable<DriverStoreEntry> entries) => new ScrollViewer
        {
            MaxHeight = 200,
            Content = new TextBlock
            {
                Text = string.Join(Environment.NewLine, entries.Select(item =>
                    $"{item.DriverInfName} - {item.DriverVersion} - {DriverStoreEntry.GetBytesReadable(item.DriverSize)}")),
                IsTextSelectionEnabled = true,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            },
        };

        private static IEnumerable<string> FindInfFiles(string folder)
        {
            // A package folder can hold several INFs; if there are none at the top, look below.
            string[] top = Directory.GetFiles(folder, "*.inf", SearchOption.TopDirectoryOnly);
            return top.Length > 0 ? top : Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories);
        }

        private async Task<string?> PickFolderAsync()
        {
            // These pickers work in an elevated process, unlike the Windows.Storage ones.
            var picker = new FolderPicker(this.AppWindow.Id);
            PickFolderResult? result = await picker.PickSingleFolderAsync();
            return result?.Path;
        }

        private async Task<ContentDialogResult> ShowDialogAsync(string title, object content, string? primaryText, bool primaryIsDefault, string? closeText = null)
        {
            if (this.dialogOpen || this.RootGrid.XamlRoot == null)
            {
                return ContentDialogResult.None;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = this.RootGrid.XamlRoot,
                RequestedTheme = this.RootGrid.ActualTheme,
                Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                Title = title,
                Content = content,
                CloseButtonText = closeText ?? Plain(Language.Button_Cancel),
            };

            if (primaryText != null)
            {
                dialog.PrimaryButtonText = primaryText;
                dialog.DefaultButton = primaryIsDefault ? ContentDialogButton.Primary : ContentDialogButton.Close;
            }

            this.dialogOpen = true;
            try
            {
                return await dialog.ShowAsync();
            }
            finally
            {
                this.dialogOpen = false;
            }
        }

        private Task ShowMessageAsync(string title, string message, string? details = null)
        {
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            if (!string.IsNullOrWhiteSpace(details) && details.Trim() != message.Trim())
            {
                content.Children.Add(new ScrollViewer
                {
                    MaxHeight = 240,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock
                    {
                        Text = details,
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = 12,
                        IsTextSelectionEnabled = true,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                    },
                });
            }

            return this.ShowDialogAsync(title, content, null, primaryIsDefault: false, closeText: Strings.Get("Dialog_Close"));
        }

        #endregion

        #region Event handlers

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await this.RefreshAsync();

        private async void Add_Click(object sender, RoutedEventArgs e)
        {
            string? folder = await this.PickFolderAsync();
            if (folder == null)
            {
                return;
            }

            List<string> infFiles;
            try
            {
                infFiles = FindInfFiles(folder).ToList();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                await this.ShowMessageAsync(Language.Message_Error, ex.Message, ex.ToString());
                return;
            }

            if (infFiles.Count == 0)
            {
                await this.ShowMessageAsync(Plain(Language.Button_Add_Package), string.Format(CultureInfo.CurrentCulture, Language.Message_No_Inf_Found, folder));
                return;
            }

            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock
            {
                Text = string.Format(CultureInfo.CurrentCulture, Strings.Get("Add_Found_Infs"), infFiles.Count, folder),
                TextWrapping = TextWrapping.Wrap,
            });
            content.Children.Add(new ScrollViewer
            {
                MaxHeight = 200,
                Content = new TextBlock
                {
                    Text = string.Join(Environment.NewLine, infFiles.Select(file => Path.GetRelativePath(folder, file))),
                    Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                },
            });

            CheckBox? installBox = null;
            if (this.driverStore.SupportAddInstall)
            {
                installBox = new CheckBox { Content = Plain(Language.Check_Install_Driver) };
                content.Children.Add(installBox);
            }

            if (await this.ShowDialogAsync(Plain(Language.Button_Add_Package), content, Plain(Language.Button_Add_Package), primaryIsDefault: true) != ContentDialogResult.Primary)
            {
                return;
            }

            bool install = installBox?.IsChecked == true;
            this.SetBusy(true, Language.Status_Adding_Package);
            IDriverStore store = this.driverStore;
            List<string> failed = await Task.Run(() => infFiles.Where(file => !Run(() => store.AddDriver(file, install))).ToList());
            this.SetBusy(false, null);
            await this.RefreshAsync();

            string name = Path.GetFileName(infFiles[0]);
            string message;
            if (failed.Count == 0)
            {
                message = infFiles.Count == 1
                    ? string.Format(CultureInfo.CurrentCulture, install ? Language.Message_Driver_Added_Installed : Language.Message_Driver_Added, name)
                    : string.Format(CultureInfo.CurrentCulture, install ? Language.Message_Drivers_Added_Installed : Language.Message_Drivers_Added, infFiles.Count);
                this.StatusText.Text = message;
                return;
            }

            message = infFiles.Count == 1
                ? string.Format(CultureInfo.CurrentCulture, install ? Language.Message_Driver_Added_Installed_Error : Language.Message_Driver_Added_Error, name)
                : install ? Language.Message_Drivers_Added_Installed_Error : Language.Message_Drivers_Added_Error;
            this.StatusText.Text = message;
            await this.ShowMessageAsync(Language.Message_Error, message, string.Join(Environment.NewLine, failed));
        }

        private void SelectOld_Click(object sender, RoutedEventArgs e)
        {
            // The same packages Select Old Driver(s) picks in the WinForms app, boot-critical ones left out.
            int count = 0;
            foreach (DriverRow row in this.allRows)
            {
                row.IsChecked = row.IsOlderVersion && !DriverInsights.IsBootCritical(row.Entry);
                count += row.IsChecked ? 1 : 0;
            }

            if (count == 0)
            {
                this.StatusText.Text = Language.Message_No_Old_Drivers_Found;
                return;
            }

            this.SetFilter(DriverView.OlderVersions, null);
            this.UpdateNavigation();
            this.UpdateChips();
        }

        private async void ExportAll_Click(object sender, RoutedEventArgs e)
        {
            string? folder = await this.PickFolderAsync();
            if (folder == null)
            {
                return;
            }

            this.SetBusy(true, Language.Status_Exporting_All_Drivers);
            IDriverStore store = this.driverStore;
            bool succeeded = await Task.Run(() => Run(() => store.ExportAllDrivers(folder)));
            this.SetBusy(false, succeeded ? Language.Message_Export_All_Drivers_Success : Language.Message_Export_All_Drivers_Fail);
            if (!succeeded)
            {
                await this.ShowMessageAsync(Language.Message_Error, Language.Message_Export_All_Drivers_Fail);
            }
        }

        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => this.ApplyFilter();

        private void Navigation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (this.updatingNavigation)
            {
                return;
            }

            this.updatingNavigation = true;
            try
            {
                if (ReferenceEquals(sender, this.ViewsList) && this.ViewsList.SelectedItem is NavigationItem view)
                {
                    this.ClassesList.SelectedItem = null;
                    this.SetFilter(view.View, null);
                }
                else if (ReferenceEquals(sender, this.ClassesList) && this.ClassesList.SelectedItem is NavigationItem driverClass)
                {
                    this.ViewsList.SelectedItem = null;
                    this.SetFilter(DriverView.All, driverClass.ClassName);
                }
            }
            finally
            {
                this.updatingNavigation = false;
            }
        }

        private void Chip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton clicked && clicked.Tag is DriverView view)
            {
                foreach (ToggleButton chip in this.ChipPanel.Children.OfType<ToggleButton>())
                {
                    chip.IsChecked = chip == clicked;
                }

                this.SetFilter(view, null);
            }
        }

        private void SortHeader_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && this.sortHeaders.TryGetValue(button, out var header))
            {
                this.sortDescending = header.Key == this.sortKey && !this.sortDescending;
                this.sortKey = header.Key;
                this.UpdateSortHeaders();
                this.ApplyFilter();
            }
        }

        private void CheckAll_Click(object sender, RoutedEventArgs e)
        {
            // Check all unless all are checked already; the box's own toggle state doesn't matter.
            bool check = this.visibleRows.Any(row => !row.IsChecked);
            foreach (DriverRow row in this.visibleRows)
            {
                row.IsChecked = check;
            }

            this.UpdateCheckAll();
        }

        private void ExplorerList_SelectionChanged(object sender, SelectionChangedEventArgs e) => this.UpdateDetails();

        private void CompactList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is DriverRow row)
            {
                row.IsChecked = !row.IsChecked;
            }
        }

        private async void DetailsExport_Click(object sender, RoutedEventArgs e)
        {
            if (this.ExplorerList.SelectedItem is DriverRow row)
            {
                await this.ExportAsync(new[] { row.Entry });
            }
        }

        private async void DetailsDelete_Click(object sender, RoutedEventArgs e)
        {
            if (this.ExplorerList.SelectedItem is DriverRow row)
            {
                await this.DeleteAsync(new[] { row.Entry });
            }
        }

        private void DetailsFolder_Click(object sender, RoutedEventArgs e)
        {
            if (this.ExplorerList.SelectedItem is DriverRow row && !string.IsNullOrEmpty(row.Entry.DriverFolderLocation))
            {
                string target = string.IsNullOrEmpty(row.Entry.DriverInfName)
                    ? row.Entry.DriverFolderLocation
                    : Path.Combine(row.Entry.DriverFolderLocation, row.Entry.DriverInfName);
                using Process? explorer = Process.Start("explorer.exe", "/select, \"" + target + "\"");
            }
        }

        private async void SelectionExport_Click(object sender, RoutedEventArgs e) => await this.ExportAsync(this.CheckedEntries());

        private async void SelectionDelete_Click(object sender, RoutedEventArgs e) => await this.DeleteAsync(this.CheckedEntries());

        private void SelectionClear_Click(object sender, RoutedEventArgs e)
        {
            foreach (DriverRow row in this.allRows)
            {
                row.IsChecked = false;
            }
        }

        private async void ChooseStore_Click(object sender, RoutedEventArgs e)
        {
            var online = new RadioButton { Content = Language.DriverStore_LocalMachine, GroupName = "Store", IsChecked = this.driverStore.Type == DriverStoreType.Online };
            var offline = new RadioButton { Content = Language.DriverStore_Offline, GroupName = "Store", IsChecked = this.driverStore.Type == DriverStoreType.Offline };
            var path = new TextBox
            {
                Text = this.driverStore.OfflineStoreLocation ?? string.Empty,
                PlaceholderText = Language.Dialog_Select_Offline_Store_Title,
                IsEnabled = offline.IsChecked == true,
            };
            var browse = new Button { Content = Strings.Get("Dialog_Browse"), IsEnabled = offline.IsChecked == true };
            offline.Checked += (s, args) => { path.IsEnabled = true; browse.IsEnabled = true; };
            online.Checked += (s, args) => { path.IsEnabled = false; browse.IsEnabled = false; };
            browse.Click += async (s, args) =>
            {
                string? folder = await this.PickFolderAsync();
                if (folder != null)
                {
                    path.Text = folder;
                }
            };

            var pathRow = new Grid { ColumnSpacing = 8 };
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathRow.Children.Add(path);
            Grid.SetColumn(browse, 1);
            pathRow.Children.Add(browse);

            var content = new StackPanel { Spacing = 8, MinWidth = 420 };
            content.Children.Add(online);
            content.Children.Add(offline);
            content.Children.Add(pathRow);

            if (await this.ShowDialogAsync(Plain(Language.ChooseDriverStore_Title), content, Plain(Language.Button_OK), primaryIsDefault: true) != ContentDialogResult.Primary)
            {
                return;
            }

            if (offline.IsChecked == true)
            {
                string imagePath = path.Text.Trim();
                if (imagePath.Length == 0 || !Directory.Exists(Path.Combine(imagePath, "Windows")))
                {
                    await this.ShowMessageAsync(Language.Message_Invalid_Offline_Store_Title, Language.Message_Invalid_Offline_Store_Location);
                    return;
                }

                this.driverStore = BackendFactory.CreateOffline(this.backend, imagePath);
            }
            else
            {
                this.driverStore = BackendFactory.CreateOnline(this.backend);
            }

            this.UpdateTitle();
            await this.RefreshAsync();
        }

        private async void ExportList_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileSavePicker(this.AppWindow.Id)
            {
                SuggestedFileName = "DriverStoreExplorer",
                DefaultFileExtension = ".csv",
            };
            picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });

            PickFileResult? result = await picker.PickSaveFileAsync();
            if (result == null)
            {
                return;
            }

            try
            {
                CsvWriter.Write(result.Path, this.allRows.Select(row => row.Entry));
                this.StatusText.Text = string.Format(CultureInfo.CurrentCulture, Language.Export_Complete, result.Path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                await this.ShowMessageAsync(Language.Export_Error, string.Format(CultureInfo.CurrentCulture, Language.Export_Failed, ex.Message), ex.ToString());
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e) => this.Close();

        private async void Backend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement item && item.Tag is string option && option != this.backend)
            {
                this.backend = BackendFactory.Validate(option);
                this.settings.DriverStoreOption = this.backend;
                this.settings.Save();
                this.driverStore = this.driverStore.Type == DriverStoreType.Offline && this.driverStore.OfflineStoreLocation is string imagePath
                    ? BackendFactory.CreateOffline(this.backend, imagePath)
                    : BackendFactory.CreateOnline(this.backend);
                this.UpdateMenuChecks();
                this.UpdateTitle();
                await this.RefreshAsync();
            }
        }

        private void Layout_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement item && item.Tag is string layout)
            {
                this.ApplyLayout(layout, save: true);
                this.UpdateMenuChecks();
            }
        }

        private void Theme_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement item && item.Tag is string theme)
            {
                this.settings.Theme = theme;
                this.settings.Save();
                this.ApplyTheme(theme);
                this.UpdateMenuChecks();
            }
        }

        private async void Language_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is FrameworkElement item) || !(item.Tag is string tag))
            {
                return;
            }

            string? language = tag.Length == 0 ? null : tag;
            if (string.Equals(language, this.settings.Language, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            this.settings.Language = language;
            this.settings.Save();

            if (await this.ShowDialogAsync(Language.Product_Name, Strings.Get("Message_Language_Restart"), Strings.Get("Dialog_Restart_Now"),
                primaryIsDefault: false, closeText: Strings.Get("Dialog_Close")) == ContentDialogResult.Primary
                && Environment.ProcessPath is string exe)
            {
                // Already elevated, so the new process starts without asking again.
                Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
                this.Close();
            }
        }

        private async void About_Click(object sender, RoutedEventArgs e)
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            var content = new StackPanel { Spacing = 8 };
            content.Children.Add(new TextBlock { Text = $"{Language.Product_Name} v{version}", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            content.Children.Add(new TextBlock { Text = Language.About_Project_Description, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
            content.Children.Add(new HyperlinkButton
            {
                Content = "github.com/lostindark/DriverStoreExplorer",
                NavigateUri = new Uri("https://github.com/lostindark/DriverStoreExplorer"),
                Padding = new Thickness(0),
            });

            await this.ShowDialogAsync(string.Format(CultureInfo.CurrentCulture, Language.Product_About_Title, Language.Product_Name), content, null,
                primaryIsDefault: false, closeText: Strings.Get("Dialog_Close"));
        }

        #endregion
    }
}
