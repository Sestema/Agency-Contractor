using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenCvSharp;
using Win11DesktopApp.Services;
using Window = System.Windows.Window;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace Win11DesktopApp.Views
{
    public partial class ImageEditorWindow : Window
    {
        private readonly ImageEnhancementService _service = new();
        private readonly string _originalPath;
        private Mat _originalMat;
        private Mat _currentMat;

        private bool _isDragging;
        private Point _dragStart;
        private bool _hasCropSelection;
        private bool _isLoaded;

        private readonly DispatcherTimer _debounceTimer;
        private const int PreviewMaxDim = 900;
        private Mat? _previewMat;
        private CancellationTokenSource? _renderCts;
        private bool _isRendering;

        public bool Saved { get; private set; }
        public string? ResultPath { get; private set; }
        public bool LoadFailed { get; private set; }

        public ImageEditorWindow(string imagePath)
        {
            InitializeComponent();
            _originalPath = imagePath;

            try
            {
                _originalMat = LoadWithRetry(imagePath);
                _currentMat = _originalMat.Clone();
                BuildPreviewMat();
            }
            catch (Exception ex)
            {
                LoadFailed = true;
                _originalMat = new Mat(100, 100, MatType.CV_8UC3, new Scalar(255, 255, 255));
                _currentMat = _originalMat.Clone();
                Loaded += (_, _) =>
                {
                    MessageBox.Show(
                        $"Не вдалося відкрити файл:\n{ex.Message}",
                        "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                    Close();
                };
                return;
            }

            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _debounceTimer.Tick += DebounceTimer_Tick;

            Loaded += OnLoaded;
            SizeChanged += (_, _) => { if (_isLoaded) ScheduleRefresh(); };
        }

        private Mat LoadWithRetry(string path, int maxAttempts = 3)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                try
                {
                    return _service.LoadImage(path);
                }
                catch when (i < maxAttempts)
                {
                    Thread.Sleep(500 * i);
                }
            }
            return _service.LoadImage(path);
        }

        private void BuildPreviewMat()
        {
            _previewMat?.Dispose();
            int maxDim = Math.Max(_currentMat.Cols, _currentMat.Rows);
            if (maxDim > PreviewMaxDim)
            {
                double s = (double)PreviewMaxDim / maxDim;
                _previewMat = new Mat();
                Cv2.Resize(_currentMat, _previewMat, new OpenCvSharp.Size(0, 0), s, s, InterpolationFlags.Area);
            }
            else
            {
                _previewMat = _currentMat.Clone();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = true;
            RefreshPreviewAsync();
        }

        private void ScheduleRefresh()
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void DebounceTimer_Tick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            RefreshPreviewAsync();
        }

        private async void RefreshPreviewAsync()
        {
            if (!_isLoaded || _previewMat == null || _previewMat.Empty()) return;
            if (_isRendering)
            {
                _renderCts?.Cancel();
                ScheduleRefresh();
                return;
            }

            _isRendering = true;
            _renderCts?.Dispose();
            _renderCts = new CancellationTokenSource();
            var token = _renderCts.Token;

            int brightness = (int)BrightnessSlider.Value;
            int contrast = (int)ContrastSlider.Value;
            double deskew = DeskewSlider.Value;
            double gamma = GammaSlider.Value;
            int denoise = (int)DenoiseSlider.Value;
            var srcMat = _previewMat;

            try
            {
                var (bitmap, pixW, pixH) = await Task.Run(() =>
                {
                    Mat result = srcMat;
                    bool needsDispose = false;

                    if (token.IsCancellationRequested) return (null, 0, 0);

                    if (brightness != 0 || contrast != 0)
                    {
                        var tmp = _service.AdjustBrightnessContrast(result, brightness, contrast);
                        if (needsDispose) result.Dispose();
                        result = tmp;
                        needsDispose = true;
                    }

                    if (Math.Abs(gamma - 1.0) > 0.05)
                    {
                        var tmp = _service.GammaCorrection(result, gamma);
                        if (needsDispose) result.Dispose();
                        result = tmp;
                        needsDispose = true;
                    }

                    if (token.IsCancellationRequested) { if (needsDispose) result.Dispose(); return (null, 0, 0); }

                    if (denoise > 0)
                    {
                        var tmp = _service.Denoise(result, denoise);
                        if (needsDispose) result.Dispose();
                        result = tmp;
                        needsDispose = true;
                    }

                    if (token.IsCancellationRequested) { if (needsDispose) result.Dispose(); return (null, 0, 0); }

                    if (Math.Abs(deskew) > 0.01)
                    {
                        var tmp = _service.Deskew(result, deskew);
                        if (needsDispose) result.Dispose();
                        result = tmp;
                        needsDispose = true;
                    }

                    if (!needsDispose)
                        result = srcMat.Clone();

                    Cv2.ImEncode(".bmp", result, out byte[] buf);
                    int w = result.Cols, h = result.Rows;
                    if (needsDispose) result.Dispose();

                    var ms = new MemoryStream(buf);
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    ms.Dispose();

                    return (bi, w, h);
                }, token);

                if (token.IsCancellationRequested || bitmap == null) return;

                PreviewImage.Source = bitmap;

                double canvasW = ImageCanvas.ActualWidth > 0 ? ImageCanvas.ActualWidth : 600;
                double canvasH = ImageCanvas.ActualHeight > 0 ? ImageCanvas.ActualHeight : 400;
                double scaleX = canvasW / pixW;
                double scaleY = canvasH / pixH;
                double scale = Math.Min(scaleX, scaleY);
                double imgW = pixW * scale;
                double imgH = pixH * scale;
                Canvas.SetLeft(PreviewImage, (canvasW - imgW) / 2);
                Canvas.SetTop(PreviewImage, (canvasH - imgH) / 2);
                PreviewImage.Width = imgW;
                PreviewImage.Height = imgH;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LoggingService.LogWarning("ImageEditor.RefreshPreview", ex.Message); }
            finally { _isRendering = false; }
        }

        private Mat ApplySlidersFull(Mat src)
        {
            int brightness = (int)BrightnessSlider.Value;
            int contrast = (int)ContrastSlider.Value;
            double deskew = DeskewSlider.Value;
            double gamma = GammaSlider.Value;
            int denoise = (int)DenoiseSlider.Value;

            Mat result = src;
            bool needsDispose = false;

            if (brightness != 0 || contrast != 0)
            {
                var tmp = _service.AdjustBrightnessContrast(result, brightness, contrast);
                if (needsDispose) result.Dispose();
                result = tmp;
                needsDispose = true;
            }

            if (Math.Abs(gamma - 1.0) > 0.05)
            {
                var tmp = _service.GammaCorrection(result, gamma);
                if (needsDispose) result.Dispose();
                result = tmp;
                needsDispose = true;
            }

            if (denoise > 0)
            {
                var tmp = _service.Denoise(result, denoise);
                if (needsDispose) result.Dispose();
                result = tmp;
                needsDispose = true;
            }

            if (Math.Abs(deskew) > 0.01)
            {
                var tmp = _service.Deskew(result, deskew);
                if (needsDispose) result.Dispose();
                result = tmp;
                needsDispose = true;
            }

            if (!needsDispose)
                result = src.Clone();

            return result;
        }

        private void ShowMat(Mat mat)
        {
            try
            {
                Cv2.ImEncode(".bmp", mat, out byte[] buf);
                using var ms = new MemoryStream(buf);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = ms;
                bi.EndInit();
                bi.Freeze();

                PreviewImage.Source = bi;

                double canvasW = ImageCanvas.ActualWidth > 0 ? ImageCanvas.ActualWidth : 600;
                double canvasH = ImageCanvas.ActualHeight > 0 ? ImageCanvas.ActualHeight : 400;

                double scaleX = canvasW / bi.PixelWidth;
                double scaleY = canvasH / bi.PixelHeight;
                double scale = Math.Min(scaleX, scaleY);

                double imgW = bi.PixelWidth * scale;
                double imgH = bi.PixelHeight * scale;
                double offsetX = (canvasW - imgW) / 2;
                double offsetY = (canvasH - imgH) / 2;

                Canvas.SetLeft(PreviewImage, offsetX);
                Canvas.SetTop(PreviewImage, offsetY);
                PreviewImage.Width = imgW;
                PreviewImage.Height = imgH;
            }
            catch (Exception ex) { LoggingService.LogWarning("ImageEditor.RefreshPreview", ex.Message); }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!_isLoaded) return;
            BrightnessValueText.Text = ((int)BrightnessSlider.Value).ToString();
            ContrastValueText.Text = ((int)ContrastSlider.Value).ToString();
            DeskewValueText.Text = $"{DeskewSlider.Value:F1}°";
            GammaValueText.Text = $"{GammaSlider.Value:F1}";
            DenoiseValueText.Text = ((int)DenoiseSlider.Value).ToString();
            ScheduleRefresh();
        }

        // --- Crop selection ---
        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _dragStart = e.GetPosition(ImageCanvas);
            CropRect.Visibility = Visibility.Visible;
            Canvas.SetLeft(CropRect, _dragStart.X);
            Canvas.SetTop(CropRect, _dragStart.Y);
            CropRect.Width = 0;
            CropRect.Height = 0;
            _hasCropSelection = false;
            ImageCanvas.CaptureMouse();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;
            var pos = e.GetPosition(ImageCanvas);
            double x = Math.Min(pos.X, _dragStart.X);
            double y = Math.Min(pos.Y, _dragStart.Y);
            double w = Math.Abs(pos.X - _dragStart.X);
            double h = Math.Abs(pos.Y - _dragStart.Y);

            Canvas.SetLeft(CropRect, x);
            Canvas.SetTop(CropRect, y);
            CropRect.Width = w;
            CropRect.Height = h;
        }

        private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ImageCanvas.ReleaseMouseCapture();
            _hasCropSelection = CropRect.Width > 5 && CropRect.Height > 5;
            if (!_hasCropSelection)
                CropRect.Visibility = Visibility.Collapsed;
        }

        private void BtnCrop_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasCropSelection)
            {
                StatusText.Text = Application.Current.TryFindResource("ImgEditorDrawCrop") as string
                    ?? "Намалюйте область для обрізки на фото.";
                return;
            }

            double imgLeft = Canvas.GetLeft(PreviewImage);
            double imgTop = Canvas.GetTop(PreviewImage);
            double imgW = PreviewImage.Width;
            double imgH = PreviewImage.Height;

            double cropL = Canvas.GetLeft(CropRect);
            double cropT = Canvas.GetTop(CropRect);
            double cropW = CropRect.Width;
            double cropH = CropRect.Height;

            var adjusted = ApplySlidersFull(_currentMat);
            int matW = adjusted.Cols;
            int matH = adjusted.Rows;

            double scaleX = matW / imgW;
            double scaleY = matH / imgH;

            int rx = (int)((cropL - imgLeft) * scaleX);
            int ry = (int)((cropT - imgTop) * scaleY);
            int rw = (int)(cropW * scaleX);
            int rh = (int)(cropH * scaleY);

            var cropped = _service.CropRegion(adjusted, rx, ry, rw, rh);
            adjusted.Dispose();

            _currentMat.Dispose();
            _currentMat = cropped;
            ResetSlidersQuiet();
            BuildPreviewMat();

            CropRect.Visibility = Visibility.Collapsed;
            _hasCropSelection = false;
            RefreshPreviewAsync();
            StatusText.Text = Application.Current.TryFindResource("ImgEditorCropped") as string ?? "Обрізано!";
        }

        private void BtnRotateLeft_Click(object sender, RoutedEventArgs e)
        {
            var rotated = _service.Rotate90(_currentMat, false);
            _currentMat.Dispose();
            _currentMat = rotated;
            BuildPreviewMat();
            RefreshPreviewAsync();
        }

        private void BtnRotateRight_Click(object sender, RoutedEventArgs e)
        {
            var rotated = _service.Rotate90(_currentMat, true);
            _currentMat.Dispose();
            _currentMat = rotated;
            BuildPreviewMat();
            RefreshPreviewAsync();
        }

        private void BtnSharpen_Click(object sender, RoutedEventArgs e)
        {
            var sharpened = _service.Sharpen(_currentMat);
            _currentMat.Dispose();
            _currentMat = sharpened;
            BuildPreviewMat();
            RefreshPreviewAsync();
            StatusText.Text = Application.Current.TryFindResource("ImgEditorSharpened") as string ?? "Різкість збільшено!";
        }

        private void BtnAutoEnhance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var enhanced = _service.AutoEnhance(_currentMat);
                _currentMat.Dispose();
                _currentMat = enhanced;
                ResetSlidersQuiet();
                BuildPreviewMat();
                RefreshPreviewAsync();
                StatusText.Text = Application.Current.TryFindResource("ImgEditorAutoEnhanceDone") as string ?? "Авто-покращення застосовано!";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void BtnPerspective_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var corrected = _service.PerspectiveCorrect(_currentMat);
                if (corrected == null)
                {
                    StatusText.Text = Application.Current.TryFindResource("ImgEditorPerspectiveNotFound") as string
                        ?? "Контур документа не знайдено. Спробуйте зробити фото з більшим контрастом.";
                    return;
                }
                _currentMat.Dispose();
                _currentMat = corrected;
                _isLoaded = false;
                DeskewSlider.Value = 0;
                _isLoaded = true;
                BuildPreviewMat();
                RefreshPreviewAsync();
                StatusText.Text = Application.Current.TryFindResource("ImgEditorPerspectiveDone") as string ?? "Перспективу виправлено!";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _currentMat.Dispose();
            _currentMat = _originalMat.Clone();
            ResetSlidersQuiet();
            BuildPreviewMat();
            CropRect.Visibility = Visibility.Collapsed;
            _hasCropSelection = false;
            RefreshPreviewAsync();
            StatusText.Text = Application.Current.TryFindResource("ImgEditorResetDone") as string ?? "Відновлено оригінал.";
        }

        private void ResetSlidersQuiet()
        {
            _isLoaded = false;
            BrightnessSlider.Value = 0;
            ContrastSlider.Value = 0;
            DeskewSlider.Value = 0;
            GammaSlider.Value = 1.0;
            DenoiseSlider.Value = 0;
            _isLoaded = true;
            BrightnessValueText.Text = "0";
            ContrastValueText.Text = "0";
            DeskewValueText.Text = "0.0°";
            GammaValueText.Text = "1.0";
            DenoiseValueText.Text = "0";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var final = ApplySlidersFull(_currentMat);
                var dir = Path.GetDirectoryName(_originalPath) ?? Path.GetTempPath();
                var outPath = Path.Combine(dir, $"edited_{Guid.NewGuid():N}{Path.GetExtension(_originalPath)}");
                _service.SaveImage(final, outPath);
                final.Dispose();

                ResultPath = outPath;
                Saved = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error: {ex.Message}";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Saved = false;
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _debounceTimer.Stop();
            _renderCts?.Cancel();
            _renderCts?.Dispose();
            _previewMat?.Dispose();
            _currentMat?.Dispose();
            _originalMat?.Dispose();
            base.OnClosed(e);
        }
    }
}
