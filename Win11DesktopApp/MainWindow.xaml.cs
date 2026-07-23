using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Shell;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;
using Win11DesktopApp.Views;

namespace Win11DesktopApp;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly AppSettingsService _appSettingsService;
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

    public MainWindow(AppSettingsService appSettingsService)
    {
        _appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
        InitializeComponent();
        SizeChanged += OnWindowSizeChanged;
        StateChanged += OnWindowStateChanged;

        SourceInitialized += (_, _) =>
        {
            var themeName = _appSettingsService.Settings.ThemeName;
            if (!string.IsNullOrWhiteSpace(themeName))
            {
                ThemeService.ApplyBackdrop(this, themeName);
            }
        };

        Loaded += (_, _) =>
        {
            var settings = _appSettingsService.Settings;
            InterfaceSizeMultiplier = SettingsViewModel.GetInterfaceSizeMultiplier(settings.InterfaceSize ?? "Medium");
            SettingsViewModel.ApplyTextSize(settings.TextSize ?? "Medium");
            RestoreWindowBounds(settings);
            UpdateMaximizedVisuals();
        };

        Closing += (_, args) =>
        {
            LoggingService.LogInfo("MainWindow.Closing",
                $"Main window closing. Cancel={args.Cancel}; State={WindowState}; DataContext={DataContext?.GetType().Name ?? "null"}.");

            if (Application.Current?.Windows
                    .OfType<DocumentScanWindow>()
                    .Any(w => w.DataContext is DocumentScanViewModel vm && vm.IsScanning) == true)
            {
                args.Cancel = true;
                Application.Current?.Windows
                    .OfType<DocumentScanWindow>()
                    .FirstOrDefault(w => w.DataContext is DocumentScanViewModel vm && vm.IsScanning)
                    ?.Activate();
                return;
            }

            SaveWindowBoundsAndFlushSettings();
        };
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
        => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
        => SystemCommands.CloseWindow(this);

    private void OnWindowStateChanged(object? sender, EventArgs e)
        => UpdateMaximizedVisuals();

    private void UpdateMaximizedVisuals()
    {
        if (WindowState == WindowState.Maximized)
        {
            // WindowChrome quirk: maximized WPF window extends ~8px beyond screen on each edge.
            RootLayout.Margin = new Thickness(8);
            if (BtnMaximize != null)
            {
                BtnMaximize.Content = "\uE923";
                BtnMaximize.ToolTip = "Restore";
            }
        }
        else
        {
            RootLayout.Margin = new Thickness(0);
            if (BtnMaximize != null)
            {
                BtnMaximize.Content = "\uE922";
                BtnMaximize.ToolTip = "Maximize";
            }
        }
    }

    private void RestoreWindowBounds(AppSettingsService.AppSettings settings)
    {
        var hasStoredSize = settings.WindowWidth > 100 && settings.WindowHeight > 100;
        if (hasStoredSize)
        {
            Width = settings.WindowWidth;
            Height = settings.WindowHeight;
        }

        var canRestorePosition =
            settings.WindowLeft >= 0 &&
            settings.WindowTop >= 0 &&
            IsWindowRectVisible(settings.WindowLeft, settings.WindowTop, Width, Height);

        if (canRestorePosition)
        {
            Left = settings.WindowLeft;
            Top = settings.WindowTop;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (settings.WindowMaximized && canRestorePosition)
            WindowState = WindowState.Maximized;
    }

    private static bool IsWindowRectVisible(double left, double top, double width, double height)
    {
        var screenLeft = SystemParameters.VirtualScreenLeft;
        var screenTop = SystemParameters.VirtualScreenTop;
        var screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
        var screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

        var right = left + Math.Max(width, 200);
        var bottom = top + Math.Max(height, 150);

        return right > screenLeft + 40 &&
               bottom > screenTop + 40 &&
               left < screenRight - 40 &&
               top < screenBottom - 40;
    }

    /// <summary>
    /// Called during Closing. Updates window bounds AND flushes any pending debounced
    /// settings writes (e.g. DataGrid column widths) with a hard timeout so a slow disk
    /// or lock contention cannot hang the UI / prevent shutdown.
    /// </summary>
    private void SaveWindowBoundsAndFlushSettings()
    {
        var sw = Stopwatch.StartNew();
        try
        {
            LoggingService.LogInfo("MainWindow.SaveWindowBounds", "begin");

            var settings = _appSettingsService.Settings;
            settings.WindowMaximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                settings.WindowLeft = Left;
                settings.WindowTop = Top;
                settings.WindowWidth = Width;
                settings.WindowHeight = Height;
            }

            var ok = _appSettingsService.SaveSettingsForShutdown(TimeSpan.FromSeconds(3));
            LoggingService.LogInfo(
                "MainWindow.SaveWindowBounds",
                $"end ms={sw.ElapsedMilliseconds}; ok={ok}.");
        }
        catch (Exception ex)
        {
            LoggingService.LogError("MainWindow.SaveWindowBoundsAndFlushSettings", ex);
            LoggingService.LogInfo(
                "MainWindow.SaveWindowBounds",
                $"end ms={sw.ElapsedMilliseconds}; ok=false (exception).");
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        => RecalculateScale();

    // Snapping the scale to a coarse grid (instead of a continuously-changing ratio tied to
    // window pixels) keeps ClearType-hinted text glyphs landing on the same fractional pixel
    // offset across resizes, which noticeably reduces the "soft"/blurry look of small text
    // under the global LayoutTransform in MainWindow.xaml (PageHost.LayoutTransform).
    // Five-percent steps keep common results (1.00, 1.25, 1.50) aligned much
    // better than values such as 1.26, which soften small ClearType glyphs.
    private const double ScaleSnapStep = 0.05;

    private void RecalculateScale()
    {
        double scaleX = ActualWidth / BaseWidth;
        double scaleY = ActualHeight / BaseHeight;
        double raw = Math.Min(scaleX, scaleY);
        double damped = 1.0 + (raw - 1.0) * 0.55;
        double autoScale = Math.Clamp(damped, 0.85, 1.55);
        double target = Math.Clamp(autoScale * _interfaceSizeMultiplier, 0.55, 2.0);
        ScaleFactor = Math.Round(target / ScaleSnapStep) * ScaleSnapStep;
    }
}
