using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class SalaryView : UserControl
    {
        private const int FixedBeforeCount = 5;
        private int _dynamicColumnCount = 0;

        public SalaryView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            PreviewMouseDown += SalaryView_PreviewMouseDown;
            PreviewKeyDown += SalaryView_PreviewKeyDown;
        }

        private void SalaryView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool hasText = !string.IsNullOrEmpty(SearchBox.Text);
            SearchPlaceholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            SearchClearBtn.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SalaryView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!AdvancePopup.IsOpen) return;

            var popupChild = AdvancePopup.Child as FrameworkElement;
            if (popupChild != null)
            {
                var pos = e.GetPosition(popupChild);
                if (pos.X >= 0 && pos.Y >= 0 && pos.X <= popupChild.ActualWidth && pos.Y <= popupChild.ActualHeight)
                    return;
            }

            AdvancePopup.IsOpen = false;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is SalaryViewModel oldVm)
            {
                oldVm.CustomFieldsChanged -= RebuildDynamicColumns;
                oldVm.DataLoaded -= OnViewModelDataLoaded;
                oldVm.PropertyChanged -= OnVmPropertyChanged;
                foreach (var entry in oldVm.Entries)
                    entry.PropertyChanged -= OnEntryPropertyChanged;
                oldVm.Entries.CollectionChanged -= OnEntriesCollectionChanged;
            }

            if (e.NewValue is SalaryViewModel vm)
            {
                vm.CustomFieldsChanged += RebuildDynamicColumns;
                vm.DataLoaded += OnViewModelDataLoaded;
                vm.PropertyChanged += OnVmPropertyChanged;
                RebuildDynamicColumns();
                foreach (var entry in vm.Entries)
                    entry.PropertyChanged += OnEntryPropertyChanged;
                vm.Entries.CollectionChanged += OnEntriesCollectionChanged;
            }
        }

        private int _lastNavDir = 0;

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not SalaryViewModel vm) return;

            // Show overlay immediately when loading starts, reset DataGrid position
            if (e.PropertyName == nameof(SalaryViewModel.IsLoading) && vm.IsLoading)
            {
                LoadingOverlay.BeginAnimation(UIElement.OpacityProperty, null);
                LoadingOverlay.Opacity = 1;
                DataGridSlide.BeginAnimation(TranslateTransform.YProperty, null);
                DataGridSlide.Y = -30;
                return;
            }

            if (e.PropertyName != nameof(SalaryViewModel.MonthDisplay)) return;

            int dir = vm.NavigationDirection;
            if (dir == 0) return;

            _lastNavDir = dir;
            double outX = dir * -90;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            // Slide-out of old month label
            var xOut = new DoubleAnimation(0, outX, new Duration(TimeSpan.FromMilliseconds(140)))
            {
                EasingFunction = ease
            };
            var opOut = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(120)));

            MonthSlide.BeginAnimation(TranslateTransform.XProperty, xOut);
            MonthLabel.BeginAnimation(UIElement.OpacityProperty, opOut);
        }

        private void OnEntriesCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (SalaryEntry entry in e.OldItems)
                    entry.PropertyChanged -= OnEntryPropertyChanged;
            if (e.NewItems != null)
                foreach (SalaryEntry entry in e.NewItems)
                    entry.PropertyChanged += OnEntryPropertyChanged;
        }

        private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SalaryEntry.IsPaid) && DataContext is SalaryViewModel vm)
                vm.OnEntryPaidChanged(sender as SalaryEntry);
        }

        private void RebuildDynamicColumns()
        {
            if (DataContext is not SalaryViewModel vm) return;

            for (int i = 0; i < _dynamicColumnCount; i++)
            {
                if (SalaryGrid.Columns.Count > FixedBeforeCount)
                    SalaryGrid.Columns.RemoveAt(FixedBeforeCount);
            }
            _dynamicColumnCount = 0;

            var fields = vm.ActiveCustomFields.ToList();
            int insertIdx = FixedBeforeCount;

            var operationColors = new Dictionary<FieldOperation, string>
            {
                { FieldOperation.Add, "SuccessBrush" },
                { FieldOperation.Subtract, "ErrorBrush" },
                { FieldOperation.Multiply, "AccentDarkBrush" },
                { FieldOperation.Divide, "WarningBrush" }
            };

            foreach (var field in fields)
            {
                string prefix = field.Operation switch
                {
                    FieldOperation.Add => "+",
                    FieldOperation.Subtract => "−",
                    FieldOperation.Multiply => "×",
                    FieldOperation.Divide => "÷",
                    _ => ""
                };

                string brushKey = operationColors.TryGetValue(field.Operation, out var c) ? c : "SecondaryForegroundBrush";
                var brush = Application.Current.TryFindResource(brushKey) as Brush ?? Brushes.Gray;

                var col = new DataGridTextColumn
                {
                    Binding = new Binding($"[{field.Id}]")
                    {
                        StringFormat = "{0:N0}",
                        Mode = BindingMode.TwoWay,
                        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
                    },
                    Width = new DataGridLength(80),
                    Header = $"{prefix}{field.Name}"
                };

                var secondaryBrush = Application.Current.TryFindResource("SecondaryForegroundBrush") as Brush ?? Brushes.Gray;

                var elemStyle = new Style(typeof(TextBlock));
                elemStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));
                elemStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, brush));

                var zeroTrigger = new DataTrigger { Binding = new Binding($"[{field.Id}]"), Value = 0m };
                zeroTrigger.Setters.Add(new Setter(TextBlock.ForegroundProperty, secondaryBrush));
                zeroTrigger.Setters.Add(new Setter(TextBlock.OpacityProperty, 0.5));
                elemStyle.Triggers.Add(zeroTrigger);

                col.ElementStyle = elemStyle;

                SalaryGrid.Columns.Insert(insertIdx, col);
                insertIdx++;
                _dynamicColumnCount++;
            }
        }

        private void SalaryGrid_Loaded(object sender, RoutedEventArgs e)
        {
            RebuildDynamicColumns();
            RestoreColumnWidths();
        }

        private void SalaryGrid_Unloaded(object sender, RoutedEventArgs e)
        {
            SaveColumnWidths();
        }

        private void SaveColumnWidths()
        {
            var svc = App.AppSettingsService;
            if (svc == null) return;
            svc.Settings.SalaryColumnWidths = SalaryGrid.Columns
                .Select(c => c.ActualWidth)
                .ToList();
            svc.SaveSettings();
        }

        private void RestoreColumnWidths()
        {
            var widths = App.AppSettingsService?.Settings?.SalaryColumnWidths;
            if (widths == null || widths.Count == 0) return;
            for (int i = 0; i < SalaryGrid.Columns.Count && i < widths.Count; i++)
            {
                if (widths[i] > 0)
                    SalaryGrid.Columns[i].Width = new DataGridLength(widths[i]);
            }
        }

        private void OnViewModelDataLoaded()
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                RebuildDynamicColumns();
                SalaryGrid.UpdateLayout();
                if (SalaryGrid.Items.Count > 0)
                    SalaryGrid.Items.Refresh();

                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

                // Fade out the loading overlay
                var overlayFade = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(280)))
                {
                    EasingFunction = ease
                };
                LoadingOverlay.BeginAnimation(UIElement.OpacityProperty, overlayFade);

                // DataGrid slides in from above (Y: -30 → 0), GPU-accelerated via BitmapCache
                var slideEase = new QuarticEase { EasingMode = EasingMode.EaseOut };
                DataGridSlide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(-30, 0, new Duration(TimeSpan.FromMilliseconds(380)))
                        { EasingFunction = slideEase });

                // Slide-in the month label after loading
                if (_lastNavDir != 0)
                {
                    double inX = _lastNavDir * 80;
                    MonthSlide.X = inX;
                    var xIn = new DoubleAnimation(inX, 0, new Duration(TimeSpan.FromMilliseconds(300)))
                    {
                        EasingFunction = ease
                    };
                    var opIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(250)))
                    {
                        EasingFunction = ease
                    };
                    MonthSlide.BeginAnimation(TranslateTransform.XProperty, xIn);
                    MonthLabel.BeginAnimation(UIElement.OpacityProperty, opIn);
                    _lastNavDir = 0;
                }
                else
                {
                    // Initial load — ensure month label is fully visible
                    MonthLabel.BeginAnimation(UIElement.OpacityProperty, null);
                    MonthLabel.Opacity = 1;
                }
            }));
        }

        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is DataGrid dg)
            {
                var sv = FindVisualChild<ScrollViewer>(dg);
                if (sv == null) return;

                if (Keyboard.Modifiers == ModifierKeys.Shift)
                    sv.ScrollToHorizontalOffset(sv.HorizontalOffset - e.Delta / 3.0);
                else
                    sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);

                e.Handled = true;
            }
        }

        private void AdvancePopupClose_Click(object sender, RoutedEventArgs e)
        {
            AdvancePopup.IsOpen = false;
        }

        private static string L(string key)
        {
            return Application.Current.TryFindResource(key) as string ?? key;
        }

        private void AdvanceCell_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not Border border) return;
            if (border.Tag is not SalaryEntry entry) return;
            if (DataContext is not SalaryViewModel vm) return;

            var advances = vm.GetAdvancesForEmployeeFirm(entry.EmployeeFolder, entry.FirmName);
            var debtItems = vm.GetDebtInfoForEmployeeFirm(entry.EmployeeFolder, entry.FirmName);

            AdvancePopupTitle.Text = entry.FullName;
            AdvancePopupList.Children.Clear();

            bool hasContent = false;

            if (debtItems.Count > 0)
            {
                hasContent = true;
                foreach (var debt in debtItems)
                {
                    var errorLight = Application.Current.TryFindResource("ErrorLightBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xEE));
                    var errorBrush = Application.Current.TryFindResource("ErrorBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28));

                    var debtBorder = new Border
                    {
                        Background = errorLight,
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(8, 4, 8, 4),
                        Margin = new Thickness(0, 0, 0, 4)
                    };

                    var debtPanel = new StackPanel { Orientation = Orientation.Horizontal };

                    var icon = new TextBlock
                    {
                        Text = "\uE783",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 11,
                        Foreground = errorBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    debtPanel.Children.Add(icon);

                    var label = new TextBlock
                    {
                        Text = string.Format(L("FinDebtFromMonth"), debt.FromMonthLabel),
                        FontSize = 11,
                        Foreground = errorBrush,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    debtPanel.Children.Add(label);

                    var amountTb = new TextBlock
                    {
                        Text = $"{debt.Amount:N0} Kč",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = errorBrush,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    debtPanel.Children.Add(amountTb);

                    debtBorder.Child = debtPanel;
                    AdvancePopupList.Children.Add(debtBorder);
                }
            }

            if (advances.Count > 0)
            {
                hasContent = true;
                foreach (var adv in advances)
                {
                    var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    var secondaryFg = Application.Current.TryFindResource("SecondaryForegroundBrush") as Brush ?? Brushes.Gray;
                    var warningFg = Application.Current.TryFindResource("WarningBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00));

                    var dateTb = new TextBlock
                    {
                        Text = adv.Date.ToString("dd.MM.yy"),
                        FontSize = 11,
                        Foreground = secondaryFg,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(dateTb, 0);
                    row.Children.Add(dateTb);

                    var noteTb = new TextBlock
                    {
                        Text = adv.Note ?? "",
                        FontSize = 11,
                        Foreground = secondaryFg,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(noteTb, 1);
                    row.Children.Add(noteTb);

                    var amountTb = new TextBlock
                    {
                        Text = $"{adv.Amount:N0} Kč",
                        FontSize = 11,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = warningFg,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    Grid.SetColumn(amountTb, 2);
                    row.Children.Add(amountTb);

                    var errorFg = Application.Current.TryFindResource("ErrorBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xCC, 0x44, 0x44));
                    var deleteBtn = new Button
                    {
                        Content = "\uE74D",
                        FontFamily = new FontFamily("Segoe MDL2 Assets"),
                        FontSize = 11,
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Foreground = errorFg,
                        Cursor = Cursors.Hand,
                        Padding = new Thickness(2),
                        Tag = adv.Id,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    var advAmount = adv.Amount;
                    var advEmpName = entry.FullName;
                    var advFirmName = entry.FirmName;
                    deleteBtn.Click += (s, args) =>
                    {
                        if (s is Button btn && btn.Tag is string advId)
                        {
                            vm.DeleteAdvance(advId, advEmpName, advFirmName, advAmount);
                            AdvancePopup.IsOpen = false;
                        }
                    };
                    Grid.SetColumn(deleteBtn, 3);
                    row.Children.Add(deleteBtn);

                    AdvancePopupList.Children.Add(row);
                }
            }

            AdvancePopupEmpty.Visibility = hasContent ? Visibility.Collapsed : Visibility.Visible;

            AdvancePopup.PlacementTarget = border;
            AdvancePopup.IsOpen = true;
            e.Handled = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }
    }
}
