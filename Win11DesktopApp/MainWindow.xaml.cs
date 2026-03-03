using System;
using System.ComponentModel;
using System.Windows;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private const double BaseWidth = 900;
    private const double BaseHeight = 600;

    private double _interfaceSizeMultiplier = 1.0;
    public double InterfaceSizeMultiplier
    {
        get => _interfaceSizeMultiplier;
        set
        {
            if (Math.Abs(_interfaceSizeMultiplier - value) > 0.001)
            {
                _interfaceSizeMultiplier = value;
                RecalculateScale();
            }
        }
    }

    private double _scaleFactor = 1.0;
    public double ScaleFactor
    {
        get => _scaleFactor;
        set
        {
            if (Math.Abs(_scaleFactor - value) > 0.001)
            {
                _scaleFactor = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScaleFactor)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainWindow()
    {
        InitializeComponent();
        SizeChanged += OnWindowSizeChanged;

        Loaded += (_, _) =>
        {
            var settings = App.AppSettingsService.Settings;
            _interfaceSizeMultiplier = SettingsViewModel.GetInterfaceSizeMultiplier(settings.InterfaceSize ?? "Medium");
            SettingsViewModel.ApplyTextSize(settings.TextSize ?? "Medium");
            RestoreWindowBounds(settings);
        };

        Closing += (_, _) => SaveWindowBounds();
    }

    private void RestoreWindowBounds(AppSettingsService.AppSettings settings)
    {
        if (settings.WindowWidth > 100)
            Width = settings.WindowWidth;
        if (settings.WindowHeight > 100)
            Height = settings.WindowHeight;
        if (settings.WindowLeft >= 0 && settings.WindowTop >= 0)
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        if (settings.WindowMaximized)
            WindowState = WindowState.Maximized;
    }

    private async void SaveWindowBounds()
    {
        try
        {
            var settings = App.AppSettingsService.Settings;
            settings.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
                settings.WindowWidth = Width;
                settings.WindowHeight = Height;
            }
            await App.AppSettingsService.SaveSettingsImmediate();
        }
        catch (Exception ex)
        {
            LoggingService.LogError("MainWindow.SaveWindowBounds", ex);
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        RecalculateScale();
    }

    private void RecalculateScale()
    {
        double scaleX = ActualWidth / BaseWidth;
        double scaleY = ActualHeight / BaseHeight;
        double raw = Math.Min(scaleX, scaleY);
        double damped = 1.0 + (raw - 1.0) * 0.55;
        double autoScale = Math.Clamp(damped, 0.85, 1.55);
        ScaleFactor = Math.Clamp(autoScale * _interfaceSizeMultiplier, 0.55, 2.0);
    }
}
