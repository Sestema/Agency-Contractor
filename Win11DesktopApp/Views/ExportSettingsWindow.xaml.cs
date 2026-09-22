using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public partial class ExportSettingsWindow : Window
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly ObservableCollection<ExportColumnItem> _columns;

        public ExportSettingsWindow(AppSettingsService appSettingsService, ObservableCollection<ExportColumnItem> columns)
        {
            _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
            _columns = columns ?? throw new ArgumentNullException(nameof(columns));
            InitializeComponent();

            HideZeroPayoutBox.IsChecked = _appSettingsService.Settings.SalaryExportHideZeroPayout;
            FontSizeSlider.Value = SalaryExportFontScale.Clamp(_appSettingsService.Settings.SalaryExportFontStep);
            UpdateFontSizeLabel();
            ExportColumnsList.ItemsSource = _columns;
            Closing += (_, _) => SaveSettings();
        }

        private void HideZeroPayout_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            ApplyToSettings();
        }

        private void FontSizeSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || FontSizeSlider == null) return;
            ApplyToSettings();
            UpdateFontSizeLabel();
        }

        private void SaveSettings()
        {
            try
            {
                ApplyToSettings();
                _ = _appSettingsService.SaveSettingsImmediate();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ExportSettingsWindow.SaveSettings", ex);
            }
        }

        private void ApplyToSettings()
        {
            var settings = _appSettingsService.Settings;
            settings.SalaryExportHideZeroPayout = HideZeroPayoutBox?.IsChecked == true;
            settings.SalaryExportFontStep = SalaryExportFontScale.Clamp((int)Math.Round(FontSizeSlider?.Value ?? settings.SalaryExportFontStep));
            settings.SalaryExportHiddenColumns = _columns
                .Where(c => !c.IsRequired && !c.IsSelected)
                .Select(c => c.Key)
                .ToList();
        }

        private void UpdateFontSizeLabel()
        {
            if (FontSizeValueText == null || FontSizeSlider == null) return;
            FontSizeValueText.Text = SalaryExportFontScale.Clamp((int)Math.Round(FontSizeSlider.Value)).ToString();
        }
    }
}
