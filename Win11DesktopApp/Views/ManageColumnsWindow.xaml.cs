using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public class FieldDisplayItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public FieldOperation Operation { get; set; }
        public string FirmName { get; set; } = string.Empty;
        public int Order { get; set; }

        public string OperationSymbol => Operation switch
        {
            FieldOperation.Add => "+",
            FieldOperation.Subtract => "−",
            FieldOperation.Multiply => "×",
            FieldOperation.Divide => "÷",
            _ => "?"
        };

        public string FirmDisplay => string.IsNullOrEmpty(FirmName) || FirmName == FinanceService.AllFirmsKey
            ? L("FinFilterAll") ?? "All firms"
            : FirmName;

        private static string? L(string key)
        {
            try { return Application.Current.FindResource(key) as string; } catch { return null; }
        }
    }

    public partial class ManageColumnsWindow : Window
    {
        private readonly FinanceService _financeService;
        private readonly ObservableCollection<FieldDisplayItem> _items = new();

        public ManageColumnsWindow(FinanceService financeService, List<string> firmNames)
        {
            _financeService = financeService;
            InitializeComponent();

            FieldsList.DataContext = _items;

            var allLabel = TryL("FinFilterAll") ?? "All firms";
            NewFieldFirm.Items.Add(new ComboBoxItem { Content = allLabel, Tag = FinanceService.AllFirmsKey });
            foreach (var f in firmNames)
                NewFieldFirm.Items.Add(new ComboBoxItem { Content = f, Tag = f });
            NewFieldFirm.SelectedIndex = 0;

            LoadFields();
        }

        private void LoadFields()
        {
            _items.Clear();
            foreach (var f in _financeService.GetCustomFields())
            {
                _items.Add(new FieldDisplayItem
                {
                    Id = f.Id,
                    Name = f.Name,
                    Operation = f.Operation,
                    FirmName = f.FirmName,
                    Order = f.Order
                });
            }
        }

        private void AddField_Click(object sender, RoutedEventArgs e)
        {
            var name = NewFieldName.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            var opItem = NewFieldOperation.SelectedItem as ComboBoxItem;
            var op = opItem?.Tag?.ToString() switch
            {
                "Add" => FieldOperation.Add,
                "Multiply" => FieldOperation.Multiply,
                "Divide" => FieldOperation.Divide,
                _ => FieldOperation.Subtract
            };

            var firmItem = NewFieldFirm.SelectedItem as ComboBoxItem;
            var firmName = firmItem?.Tag?.ToString() ?? FinanceService.AllFirmsKey;

            var maxOrder = _items.Count > 0 ? _items.Max(i => i.Order) + 1 : 0;

            var field = new CustomSalaryField
            {
                Name = name,
                Operation = op,
                FirmName = firmName,
                Order = maxOrder
            };

            _financeService.AddCustomField(field);
            LoadFields();
            NewFieldName.Text = string.Empty;
        }

        private void EditField_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string fieldId)
            {
                var item = _items.FirstOrDefault(i => i.Id == fieldId);
                if (item == null) return;

                var editWin = new EditFieldWindow(item.Name, item.Operation, item.FirmName,
                    _items.Select(i => i.FirmName).Distinct().Where(f => f != FinanceService.AllFirmsKey).ToList());
                editWin.Owner = this;

                if (editWin.ShowDialog() == true)
                {
                    var updated = new CustomSalaryField
                    {
                        Id = fieldId,
                        Name = editWin.FieldName,
                        Operation = editWin.FieldOperation,
                        FirmName = editWin.FieldFirmName,
                        Order = item.Order
                    };
                    _financeService.UpdateCustomField(updated);
                    LoadFields();
                }
            }
        }

        private void DeleteField_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string fieldId)
            {
                var item = _items.FirstOrDefault(i => i.Id == fieldId);
                if (item == null) return;

                var msg = TryL("FinConfirmDeleteField") ?? $"Delete field '{item.Name}'?";
                if (MessageBox.Show(string.Format(msg, item.Name), TryL("TitleConfirmDelete") ?? "Confirm",
                        MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _financeService.RemoveCustomField(fieldId);
                    LoadFields();
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private static string? TryL(string key)
        {
            try { return Application.Current.FindResource(key) as string; } catch { return null; }
        }
    }
}
