using System;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class XlsxEditorView : UserControl
    {
        private XlsxEditorViewModel? _vm;
        private int _selectedRow = -1;
        private int _selectedCol = -1;
        private bool _isLoaded;
        private AITemplateOverlayWindow? _aiOverlay;

        // Brushes (pre-created for performance)
        private static readonly Brush SlaveBgBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
        private static readonly Brush MasterBgBrush = new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE));
        private static readonly Brush NormalBgBrush = Brushes.Transparent;
        private static readonly Brush SlaveFgBrush = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA));
        private static readonly Brush TagFgBrush = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
        private static readonly Brush NormalFgBrush = new SolidColorBrush(Colors.Black);

        static XlsxEditorView()
        {
            SlaveBgBrush.Freeze();
            MasterBgBrush.Freeze();
            SlaveFgBrush.Freeze();
            TagFgBrush.Freeze();
            NormalFgBrush.Freeze();
        }

        public XlsxEditorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
            {
                _vm.RequestGetSelectedCell = null;
                _vm.RequestRefreshGrid = null;
            }

            if (DataContext is XlsxEditorViewModel vm)
            {
                _vm = vm;
                _vm.RequestGetSelectedCell = GetSelectedCell;
                _vm.RequestRefreshGrid = RefreshGrid;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;

            // Auto-check first sheet tab
            if (_vm != null && _vm.SheetNames.Count > 0)
            {
                try
                {
                    var buttons = FindAllVisualChildren<RadioButton>(this);
                    foreach (var rb in buttons)
                    {
                        if (rb.GroupName == "SheetTabs")
                        {
                            rb.IsChecked = true;
                            break;
                        }
                    }
                }
                catch { /* ignore */ }
            }
        }

        private (int row, int col) GetSelectedCell()
        {
            return (_selectedRow, _selectedCol);
        }

        private void RefreshGrid()
        {
            try { XlsxGrid.Items.Refresh(); }
            catch { /* ignore */ }
        }

        // ===== DataGrid events =====

        private void XlsxGrid_CurrentCellChanged(object? sender, EventArgs e)
        {
            if (!_isLoaded || _vm == null) return;

            try
            {
                var cell = XlsxGrid.CurrentCell;
                if (cell.Column == null) return;

                _selectedCol = cell.Column.DisplayIndex;
                _selectedRow = XlsxGrid.Items.IndexOf(cell.Item);

                _vm.OnCellSelected(_selectedRow, _selectedCol);
            }
            catch
            {
                _selectedRow = -1;
                _selectedCol = -1;
            }
        }

        private void XlsxGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (!_isLoaded || _vm == null) return;

            try
            {
                if (e.EditAction == DataGridEditAction.Commit)
                {
                    var row = e.Row.GetIndex();
                    var col = e.Column.DisplayIndex;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _vm.OnCellSelected(row, col);
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Block editing on slave (merged) cells.
        /// </summary>
        private void XlsxGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
            if (_vm == null) return;

            try
            {
                var rowIndex = e.Row.GetIndex();
                var colIndex = e.Column.DisplayIndex;

                if (_vm.CurrentMergeInfo.TryGetValue((rowIndex, colIndex), out var info) && info.IsSlave)
                {
                    e.Cancel = true;
                    var masterAddr = $"{XlsxEditorViewModel.GetColumnLetter(info.MasterCol + 1)}{info.MasterRow + 1}";
                    _vm.StatusMessage = $"Комірка об'єднана. Редагуйте головну комірку {masterAddr}";
                }
            }
            catch { /* ignore */ }
        }

        private void XlsxGrid_LoadingRow(object? sender, DataGridRowEventArgs e)
        {
            // Row numbers (1-based, like Excel)
            e.Row.Header = (e.Row.GetIndex() + 1).ToString();
        }

        /// <summary>
        /// Replace default text columns with template columns that support:
        /// - Merge cell highlighting (background/foreground)
        /// - Tag highlighting (blue text for ${...})
        /// - Tooltip for merged cells
        /// </summary>
        private void XlsxGrid_AutoGeneratingColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            if (e.Column is not DataGridTextColumn) return;

            var propName = e.PropertyName;
            var colIndex = XlsxEditorViewModel.ColumnLetterToIndex(propName);

            var templateCol = new DataGridTemplateColumn
            {
                Header = propName,
                MinWidth = 70,
                Width = new DataGridLength(100),
                SortMemberPath = propName,
            };

            // ===== DISPLAY TEMPLATE: Border > TextBlock =====
            var converter = new CellDisplayConverter(_vm, colIndex, propName);

            // Border (for background + tooltip)
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetBinding(Border.BackgroundProperty,
                new Binding() { Converter = converter, ConverterParameter = "bg" });
            borderFactory.SetBinding(Border.ToolTipProperty,
                new Binding() { Converter = converter, ConverterParameter = "tooltip" });
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(4, 2, 4, 2));

            // TextBlock (text + foreground + fontweight)
            var textFactory = new FrameworkElementFactory(typeof(TextBlock));
            textFactory.SetBinding(TextBlock.TextProperty,
                new Binding() { Converter = converter, ConverterParameter = "text" });
            textFactory.SetBinding(TextBlock.ForegroundProperty,
                new Binding() { Converter = converter, ConverterParameter = "fg" });
            textFactory.SetBinding(TextBlock.FontWeightProperty,
                new Binding() { Converter = converter, ConverterParameter = "fw" });
            textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
            textFactory.SetValue(TextBlock.FontSizeProperty, 12.0);
            textFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);

            borderFactory.AppendChild(textFactory);
            templateCol.CellTemplate = new DataTemplate { VisualTree = borderFactory };

            // ===== EDITING TEMPLATE: TextBox =====
            var editFactory = new FrameworkElementFactory(typeof(TextBox));
            editFactory.SetBinding(TextBox.TextProperty,
                new Binding($"[{propName}]") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            editFactory.SetValue(TextBox.FontSizeProperty, 12.0);
            editFactory.SetValue(TextBox.PaddingProperty, new Thickness(3, 2, 3, 2));
            editFactory.SetValue(TextBox.BorderThicknessProperty, new Thickness(0));
            editFactory.SetValue(TextBox.VerticalAlignmentProperty, VerticalAlignment.Stretch);

            templateCol.CellEditingTemplate = new DataTemplate { VisualTree = editFactory };

            e.Column = templateCol;
        }

        private void AIOverlay_Click(object sender, RoutedEventArgs e)
        {
            if (_aiOverlay == null || !_aiOverlay.IsLoaded)
            {
                _aiOverlay = new AITemplateOverlayWindow();
                _aiOverlay.Owner = Window.GetWindow(this);
                _aiOverlay.SetContentProviders(GetSpreadsheetContent, GetTagCatalogText);
            }

            if (_aiOverlay.IsVisible)
                _aiOverlay.Hide();
            else
                _aiOverlay.Show();
        }

        private string? GetSpreadsheetContent()
        {
            if (_vm?.CurrentData == null) return null;
            var dt = _vm.CurrentData;
            var sb = new StringBuilder();
            sb.AppendLine($"Sheet: {_vm.SelectedSheet}");
            for (int r = 0; r < Math.Min(dt.Rows.Count, 50); r++)
            {
                for (int c = 0; c < dt.Columns.Count; c++)
                {
                    var val = dt.Rows[r][c]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(val))
                        sb.Append($"{XlsxEditorViewModel.GetColumnLetter(c + 1)}{r + 1}={val}  ");
                }
                if (sb.Length > 0 && sb[sb.Length - 1] != '\n')
                    sb.AppendLine();
            }
            return sb.ToString();
        }

        private string? GetTagCatalogText()
        {
            if (_vm == null) return null;
            var sb = new StringBuilder();
            foreach (var group in _vm.TagGroups)
            {
                sb.AppendLine($"[{group.GroupName}]");
                foreach (var tag in group.Tags)
                    sb.AppendLine($"  ${{{tag.Tag}}} — {tag.Description}");
            }
            return sb.ToString();
        }

        private void SheetTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            if (sender is RadioButton rb && rb.Content is string sheetName && _vm != null)
            {
                _vm.SelectedSheet = sheetName;
                _selectedRow = -1;
                _selectedCol = -1;
            }
        }

        private static System.Collections.Generic.List<T> FindAllVisualChildren<T>(DependencyObject parent) where T : DependencyObject
        {
            var result = new System.Collections.Generic.List<T>();
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) result.Add(found);
                result.AddRange(FindAllVisualChildren<T>(child));
            }
            return result;
        }
    }

    /// <summary>
    /// Multi-purpose converter for DataGrid cell display.
    /// Handles: background (merge), foreground (merge + tag), text (merge indicator),
    /// fontweight (tag), tooltip (merge info).
    /// 
    /// The converter receives the DataRowView (bound with no path) and uses the column index
    /// (baked in at construction) to determine merge/tag status.
    /// </summary>
    public class CellDisplayConverter : IValueConverter
    {
        private readonly WeakReference<XlsxEditorViewModel>? _vmRef;
        private readonly int _colIndex;
        private readonly string _propName;

        private static readonly Regex TagPattern = new(@"\$\{[^}]+\}", RegexOptions.Compiled);

        // Pre-created frozen brushes
        private static readonly Brush SlaveBg = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8)));
        private static readonly Brush MasterBg = Freeze(new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFE)));
        private static readonly Brush SlaveFg = Freeze(new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)));
        private static readonly Brush TagFg = Freeze(new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0)));
        private static readonly Brush NormalFg = Freeze(new SolidColorBrush(Colors.Black));

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public CellDisplayConverter(XlsxEditorViewModel? vm, int colIndex, string propName)
        {
            _vmRef = vm != null ? new WeakReference<XlsxEditorViewModel>(vm) : null;
            _colIndex = colIndex;
            _propName = propName;
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var mode = parameter?.ToString() ?? "";

            if (_vmRef == null || !_vmRef.TryGetTarget(out var vm))
                return GetDefault(mode);

            if (value is not DataRowView drv)
                return GetDefault(mode);

            int rowIndex;
            try { rowIndex = drv.Row.Table.Rows.IndexOf(drv.Row); }
            catch { return GetDefault(mode); }

            if (rowIndex < 0) return GetDefault(mode);

            // Check merge info
            vm.CurrentMergeInfo.TryGetValue((rowIndex, _colIndex), out var mergeInfo);
            bool isSlave = mergeInfo?.IsSlave == true;
            bool isMerge = mergeInfo != null;

            // Get actual cell value
            string cellText;
            try { cellText = drv.Row[_propName]?.ToString() ?? ""; }
            catch { cellText = ""; }

            bool hasTag = !isSlave && TagPattern.IsMatch(cellText);

            switch (mode)
            {
                case "bg":
                    if (isSlave) return SlaveBg;
                    if (isMerge) return MasterBg; // master of merge
                    return Brushes.Transparent;

                case "fg":
                    if (isSlave) return SlaveFg;
                    if (hasTag) return TagFg;
                    return NormalFg;

                case "fw":
                    if (hasTag) return FontWeights.SemiBold;
                    return FontWeights.Normal;

                case "text":
                    if (isSlave)
                    {
                        // Show merge indicator: "◈" with direction to master
                        return "◈";
                    }
                    return cellText;

                case "tooltip":
                    if (isSlave)
                    {
                        var masterAddr = $"{XlsxEditorViewModel.GetColumnLetter(mergeInfo!.MasterCol + 1)}{mergeInfo.MasterRow + 1}";
                        return $"Об'єднана комірка ({mergeInfo.RangeLabel})\nГоловна комірка: {masterAddr}";
                    }
                    if (isMerge)
                    {
                        return $"Об'єднана комірка: {mergeInfo!.RangeLabel}";
                    }
                    if (hasTag) return cellText; // Show full tag text in tooltip
                    return null!;

                default:
                    return DependencyProperty.UnsetValue;
            }
        }

        private static object GetDefault(string mode)
        {
            return mode switch
            {
                "bg" => Brushes.Transparent,
                "fg" => NormalFg,
                "fw" => FontWeights.Normal,
                "text" => "",
                "tooltip" => null!,
                _ => DependencyProperty.UnsetValue,
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
