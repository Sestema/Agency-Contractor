using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public partial class ReplaceDocumentWindow : Window
    {
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".heic", ".heif", ".pdf" };

        private readonly GeminiApiService _geminiApiService;
        private readonly EmployeeService _employeeService;
        private readonly string _docType;
        private readonly EmployeeData _data;
        private string? _selectedFilePath;
        private bool _selectedIsPdf;
        private string? _sessionTempFolder;
        private readonly Dictionary<string, (TextBox newBox, string oldValue)> _fields = new();
        private readonly List<string> _pdfPreviewPages = new();
        private int _currentPdfPageIndex;
        private string? _pdfPreviewTempFolder;
        private TextBlock? _insuranceNumberMismatchHint;

        public bool Saved { get; private set; }
        public string? ResultFilePath => _selectedFilePath;
        public Dictionary<string, string> NewValues { get; } = new();

        public ReplaceDocumentWindow(
            string docType,
            EmployeeData data,
            GeminiApiService geminiApiService,
            EmployeeService employeeService)
        {
            InitializeComponent();
            _geminiApiService = geminiApiService;
            _employeeService = employeeService;
            _docType = docType;
            _data = data;

            TitleBlock.Text = Res("ReplDocTitle") + " — " + GetDocLabel();
            BuildFieldsUI();
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        private static string FormatAiServiceMessage(string response)
        {
            if (GeminiApiService.IsTimeoutResponse(response))
                return Res("AIChatTimeout");

            if (GeminiApiService.IsNetworkErrorResponse(response))
                return Res("AIChatNetworkError");

            return response;
        }

        private string GetDocLabel() => _docType switch
        {
            "passport" => Res("DetDocPassport"),
            "visa" => Res("DetDocVisa"),
            "passport_page2" => IsEuIdCard ? Res("StepIdCardPage2Data") : Res("StepPassportPage2Data"),
            "insurance" => Res("DetDocInsurance"),
            "work_permit" => Res("DetDocWorkPermit"),
            _ => _docType
        };

        private void BuildFieldsUI()
        {
            var fields = GetFieldsForType();
            foreach (var (key, label, oldValue) in fields)
            {
                var lbl = new TextBlock
                {
                    Text = label,
                    FontSize = 12,
                    Foreground = (System.Windows.Media.Brush)FindResource("ForegroundBrush"),
                    Margin = new Thickness(0, 0, 0, 3),
                    Opacity = 0.7
                };
                FieldsPanel.Children.Add(lbl);

                if (!string.IsNullOrEmpty(oldValue))
                {
                    var oldBlock = new TextBlock
                    {
                        Text = $"{Res("ReplDocOldValue")}: {oldValue}",
                        FontSize = 11,
                        Foreground = System.Windows.Media.Brushes.Gray,
                        Margin = new Thickness(0, 0, 0, 2)
                    };
                    FieldsPanel.Children.Add(oldBlock);
                }

                var tb = new TextBox
                {
                    Text = oldValue,
                    Padding = new Thickness(8, 6, 8, 6),
                    FontSize = 13,
                    Margin = new Thickness(0, 0, 0, _docType == "insurance" && key == "InsuranceNumber" ? 2 : 10)
                };
                FieldsPanel.Children.Add(tb);
                _fields[key] = (tb, oldValue);

                if (_docType == "insurance" && key == "InsuranceNumber")
                {
                    tb.TextChanged += (_, _) => UpdateInsuranceNumberHighlight();
                    _insuranceNumberMismatchHint = new TextBlock
                    {
                        Text = Res("ReplDocInsuranceNumberChanged"),
                        FontSize = 11,
                        Foreground = (Brush)FindResource("ErrorBrush"),
                        Margin = new Thickness(0, 0, 0, 10),
                        Visibility = Visibility.Collapsed,
                        TextWrapping = TextWrapping.Wrap
                    };
                    FieldsPanel.Children.Add(_insuranceNumberMismatchHint);
                    UpdateInsuranceNumberHighlight();
                }
            }
        }

        private static string NormalizeInsuranceNumber(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : new string(value.Where(char.IsDigit).ToArray());

        private static bool InsuranceNumbersDiffer(string oldValue, string newValue)
        {
            var oldNorm = NormalizeInsuranceNumber(oldValue);
            if (string.IsNullOrEmpty(oldNorm))
                return false;

            var newNorm = NormalizeInsuranceNumber(newValue);
            return !string.Equals(oldNorm, newNorm, StringComparison.Ordinal);
        }

        private void UpdateInsuranceNumberHighlight()
        {
            if (_docType != "insurance" || !_fields.TryGetValue("InsuranceNumber", out var field))
                return;

            if (InsuranceNumbersDiffer(field.oldValue, field.newBox.Text))
            {
                field.newBox.BorderBrush = (Brush)FindResource("ErrorBrush");
                field.newBox.BorderThickness = new Thickness(1);
                field.newBox.Background = (Brush)FindResource("ErrorLightBrush");
                if (_insuranceNumberMismatchHint != null)
                    _insuranceNumberMismatchHint.Visibility = Visibility.Visible;
            }
            else
            {
                field.newBox.ClearValue(TextBox.BorderBrushProperty);
                field.newBox.ClearValue(TextBox.BorderThicknessProperty);
                field.newBox.ClearValue(TextBox.BackgroundProperty);
                if (_insuranceNumberMismatchHint != null)
                    _insuranceNumberMismatchHint.Visibility = Visibility.Collapsed;
            }
        }

        private bool IsEuIdCard => _data.EmployeeType == "eu_citizen" && _data.EuDocumentType == "id_card";

        private List<(string key, string label, string oldValue)> GetFieldsForType()
        {
            var passportFields = new List<(string key, string label, string oldValue)>
            {
                ("PassportNumber", Res("DetFieldPassportNum"), _data.PassportNumber),
                ("PassportAuthority", Res("DetFieldPassportAuthority"), _data.PassportAuthority),
                ("PassportCountry", Res("CandPassportCountry"), _data.PassportCountry),
                ("PassportCity", Res("CandPassportCity"), _data.PassportCity),
                ("PassportExpiry", Res("DetFieldExpiry"), _data.PassportExpiry),
            };
            if (IsEuIdCard)
                passportFields.Add(("VisaExpiry", Res("DetFieldExpiry") + " (ČR)", _data.VisaExpiry));

            return _docType switch
            {
                "passport" => passportFields,
                "visa" => new()
                {
                    ("VisaNumber", Res("DetFieldVisaNum"), _data.VisaNumber),
                    ("VisaAuthority", Res("DetFieldVisaAuthority"), _data.VisaAuthority),
                    ("VisaType", Res("DetFieldVisaType"), _data.VisaType),
                    ("VisaStartDate", Res("DetFieldVisaStartDate"), _data.VisaStartDate),
                    ("VisaExpiry", Res("DetFieldExpiry"), _data.VisaExpiry),
                    ("WorkPermitName", Res("DetFieldWorkPermitName"), _data.WorkPermitName),
                },
                "passport_page2" => IsEuIdCard
                    ? new()
                    {
                        ("VisaNumber", $"{Res("DetFieldVisaNum")} ({Res("WizIdCardNumberHint")})", _data.VisaNumber),
                        ("PassportAuthority", $"{Res("DetFieldVisaAuthority")} ({Res("WizIdCardAuthorityHint")})", _data.PassportAuthority),
                        ("PassportCity", Res("CandPassportCity"), _data.PassportCity),
                        ("PassportCountry", Res("CandPassportCountry"), _data.PassportCountry),
                        ("VisaStartDate", Res("DetFieldVisaStartDate"), _data.VisaStartDate),
                        ("VisaExpiry", $"{Res("DetFieldExpiry")} ({Res("WizIdCardExpiryHint")})", _data.VisaExpiry),
                        ("WorkPermitName", Res("DetFieldWorkPermitName"), _data.WorkPermitName),
                    }
                    : new()
                    {
                        ("VisaNumber", Res("DetFieldVisaNum"), _data.VisaNumber),
                        ("VisaAuthority", Res("DetFieldVisaAuthority"), _data.VisaAuthority),
                        ("VisaStartDate", Res("DetFieldVisaStartDate"), _data.VisaStartDate),
                        ("VisaExpiry", Res("DetFieldExpiry"), _data.VisaExpiry),
                        ("WorkPermitName", Res("DetFieldWorkPermitName"), _data.WorkPermitName),
                    },
                "insurance" => new()
                {
                    ("InsuranceCompanyShort", Res("DetFieldInsCompany"), _data.InsuranceCompanyShort),
                    ("InsuranceCompanyFull", Res("DetFieldInsCompanyFull"), _data.InsuranceCompanyFull),
                    ("InsuranceNumber", Res("DetFieldInsNum"), _data.InsuranceNumber),
                    ("InsuranceExpiry", Res("DetFieldExpiry"), _data.InsuranceExpiry),
                },
                "work_permit" => new()
                {
                    ("WorkPermitName", Res("DetFieldWorkPermitName"), _data.WorkPermitName),
                    ("WorkPermitNumber", Res("DetFieldWpNumber"), _data.WorkPermitNumber),
                    ("WorkPermitType", Res("DetFieldWpType"), _data.WorkPermitType),
                    ("WorkPermitIssueDate", Res("DetFieldWpIssueDate"), _data.WorkPermitIssueDate),
                    ("WorkPermitExpiry", Res("DetFieldExpiry"), _data.WorkPermitExpiry),
                    ("WorkPermitAuthority", Res("DetFieldWpAuthority"), _data.WorkPermitAuthority),
                },
                _ => new()
            };
        }

        private void BtnUpload_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Documents|*.jpg;*.jpeg;*.png;*.heic;*.heif;*.pdf"
            };
            if (dialog.ShowDialog() != true) return;
            LoadPreview(dialog.FileName);
        }

        private void EnsureSessionTempFolder()
        {
            if (!string.IsNullOrWhiteSpace(_sessionTempFolder) && Directory.Exists(_sessionTempFolder))
                return;

            _sessionTempFolder = Path.Combine(Path.GetTempPath(), "AC_ReplDoc_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_sessionTempFolder);
        }

        private void LoadPreview(string path)
        {
            ResetPreviewUi();

            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    throw new FileNotFoundException(Res("MsgFileNotFound"));

                var normalizedPath = Path.GetFullPath(path);
                if (!File.Exists(normalizedPath))
                    throw new FileNotFoundException(Res("MsgFileNotFound"), normalizedPath);

                if (new FileInfo(normalizedPath).Length <= 0)
                    throw new IOException(Res("MsgOpenFileFail"));

                var ext = Path.GetExtension(normalizedPath);
                if (!AllowedExtensions.Contains(ext))
                {
                    MessageBox.Show(Res("DragDropInvalidFormat"), Res("TitleError"),
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                EnsureSessionTempFolder();
                var sessionFolder = _sessionTempFolder!;
                var temp = _employeeService.PrepareTempDocument(normalizedPath, sessionFolder, "upload");

                if (temp.IsPdf)
                {
                    if (string.IsNullOrWhiteSpace(temp.PdfPath) || !File.Exists(temp.PdfPath))
                        throw new IOException(Res("MsgOpenFileFail"));

                    _selectedFilePath = temp.PdfPath;
                    _selectedIsPdf = true;
                    LoadPdfPreview(temp.PdfPath);
                    return;
                }

                if (string.IsNullOrWhiteSpace(temp.ImagePath) || !File.Exists(temp.ImagePath))
                    throw new IOException(Res("MsgOpenFileFail"));

                _selectedFilePath = temp.ImagePath;
                _selectedIsPdf = false;
                LoadBitmapPreview(temp.ImagePath);
            }
            catch (Exception ex)
            {
                _selectedFilePath = null;
                _selectedIsPdf = false;
                LoggingService.LogError("ReplaceDoc.LoadPreview", ex);
                NoImageText.Text = Res("ReplDocLoadError");
                NoImageText.Visibility = Visibility.Visible;
                MessageBox.Show(
                    string.Format(Res("MsgOpenFileError"), ex.Message),
                    Res("TitleError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ResetPreviewUi()
        {
            _selectedFilePath = null;
            _selectedIsPdf = false;
            CleanupPdfPagePreviews();
            PreviewImage.Source = null;
            NoImageText.Text = Res("ReplDocUploadHint");
            NoImageText.Visibility = Visibility.Visible;
            PagerPanel.Visibility = Visibility.Collapsed;
            _pdfPreviewPages.Clear();
            _currentPdfPageIndex = 0;
        }

        private void LoadPdfPreview(string path)
        {
            try
            {
                NoImageText.Text = Res("PreviewLoading");
                NoImageText.Visibility = Visibility.Visible;
                EnsureSessionTempFolder();
                _pdfPreviewTempFolder = Path.Combine(_sessionTempFolder!, "pdf_pages");
                Directory.CreateDirectory(_pdfPreviewTempFolder);

                var pages = _employeeService.RenderPdfPages(path, _pdfPreviewTempFolder, "preview");
                if (pages.Count == 0)
                {
                    CleanupPdfPagePreviews();
                    NoImageText.Text = Res("ReplDocLoadError");
                    NoImageText.Visibility = Visibility.Visible;
                    PagerPanel.Visibility = Visibility.Collapsed;
                    return;
                }

                _pdfPreviewPages.AddRange(pages);
                PagerPanel.Visibility = pages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
                ShowPdfPage(0);
                UpdatePager();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ReplaceDoc.LoadPdfPreview", ex);
                NoImageText.Text = Res("ReplDocLoadError");
                NoImageText.Visibility = Visibility.Visible;
                PagerPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void ShowPdfPage(int index)
        {
            if (index < 0 || index >= _pdfPreviewPages.Count)
                return;

            _currentPdfPageIndex = index;
            LoadBitmapPreview(_pdfPreviewPages[index]);
            UpdatePager();
        }

        private void LoadBitmapPreview(string path)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();
                PreviewImage.Source = bi;
                NoImageText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ReplaceDoc.LoadBitmapPreview", ex);
                NoImageText.Text = Res("ReplDocLoadError");
                NoImageText.Visibility = Visibility.Visible;
            }
        }

        private string? GetAiScanImagePath()
        {
            if (_selectedIsPdf)
            {
                if (_pdfPreviewPages.Count == 0)
                    return null;

                var pageIndex = Math.Clamp(_currentPdfPageIndex, 0, _pdfPreviewPages.Count - 1);
                var pagePath = _pdfPreviewPages[pageIndex];
                return File.Exists(pagePath) ? pagePath : null;
            }

            return string.IsNullOrEmpty(_selectedFilePath) || !File.Exists(_selectedFilePath)
                ? null
                : _selectedFilePath;
        }

        private void BtnEditor_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFilePath) || !File.Exists(_selectedFilePath))
            {
                MessageBox.Show(Res("MsgUploadFirst"), Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_selectedIsPdf)
            {
                MessageBox.Show(Res("ReplDocEditorNotForPdf"), Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var editor = new ImageEditorWindow(_selectedFilePath);
                if (editor.LoadFailed) return;
                editor.Owner = this;
                editor.ShowDialog();

                if (editor.Saved && !string.IsNullOrEmpty(editor.ResultPath) && File.Exists(editor.ResultPath))
                {
                    _selectedFilePath = editor.ResultPath;
                    _selectedIsPdf = false;
                    LoadBitmapPreview(_selectedFilePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"{Res("ReplDocLoadError")}\n{ex.Message}",
                    Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnAIScan_Click(object sender, RoutedEventArgs e)
        {
            if (!_geminiApiService.IsConfigured)
            {
                MessageBox.Show(Res("AIChatNoModel"), Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrEmpty(_selectedFilePath) || !File.Exists(_selectedFilePath))
            {
                MessageBox.Show(Res("MsgUploadFirst"), Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            BtnAIScan.IsEnabled = false;
            AIScanStatus.Text = Res("AIScanWorking");
            AIScanStatusPanel.Visibility = Visibility.Visible;
            AIScanSpinner.Visibility = Visibility.Visible;

            try
            {
                var docKey = _docType == "work_permit" ? "permit" : _docType;
                if (docKey == "passport" && _data.EmployeeType == "eu_citizen" && _data.EuDocumentType == "id_card")
                    docKey = "id_card";
                else if (docKey == "passport_page2" && IsEuIdCard)
                    docKey = "id_card_back";
                else if (docKey == "passport_page2")
                    docKey = "passport2";
                var prompt = AIScanPrompts.GetPrompt(docKey);

                string result;
                if (_selectedIsPdf)
                    result = await _geminiApiService.ChatWithFileAsync(_selectedFilePath, prompt, null);
                else
                {
                    var imagePath = GetAiScanImagePath();
                    if (string.IsNullOrEmpty(imagePath))
                    {
                        AIScanStatus.Text = Res("ReplDocLoadError");
                        return;
                    }

                    result = await _geminiApiService.ChatWithImageAsync(imagePath, prompt, null);
                }

                if (result.StartsWith("["))
                {
                    AIScanStatus.Text = FormatAiServiceMessage(result);
                    return;
                }

                var parsed = AIScanPrompts.ValidateAndCleanParsedFields(docKey, AIScanPrompts.ParseResponse(result));
                if (!AIScanPrompts.IsDocumentKindCompatible(docKey, parsed))
                {
                    AIScanStatus.Text = Res("AIScanDocumentTypeMismatch") ?? "AI recognized a different document type. Please check the selected document slot.";
                    return;
                }

                if (!parsed.Any(kv => !kv.Key.StartsWith("__", StringComparison.OrdinalIgnoreCase)))
                {
                    AIScanStatus.Text = Res("AIScanNoData");
                    return;
                }

                int filled = 0;
                foreach (var (key, value) in parsed)
                {
                    if (key.StartsWith("__", StringComparison.OrdinalIgnoreCase)
                        || AIScanPrompts.IsLowConfidenceField(parsed, key)
                        || AIScanPrompts.IsSuspiciousFieldValue(parsed, key, value))
                        continue;

                    if (_docType == "insurance" && (key == "InsuranceCompanyCode" || key == "InsuranceCompanyShort" || key == "InsuranceCompanyFull" || key == "InsuranceCompanyRaw"))
                    {
                        var option = InsuranceCompanyNormalizer.Normalize(
                            parsed.TryGetValue("InsuranceCompanyRaw", out var rawValue) ? rawValue : value,
                            parsed.TryGetValue("InsuranceCompanyCode", out var codeValue) ? codeValue : null,
                            parsed.TryGetValue("InsuranceCompanyShort", out var shortValue) ? shortValue : null,
                            parsed.TryGetValue("InsuranceCompanyFull", out var fullValue) ? fullValue : null);

                        if (option != null)
                        {
                            if (_fields.TryGetValue("InsuranceCompanyShort", out var shortField))
                            {
                                shortField.newBox.Text = option.ShortName;
                                filled++;
                            }
                            if (_fields.TryGetValue("InsuranceCompanyFull", out var fullField))
                            {
                                fullField.newBox.Text = option.FullName;
                                filled++;
                            }
                        }

                        continue;
                    }

                    if (_fields.TryGetValue(key, out var field))
                    {
                        field.newBox.Text = value;
                        filled++;
                    }
                }

                AIScanStatus.Text = string.Format(Res("AIScanDone"), filled);
            }
            catch (Exception ex)
            {
                AIScanStatus.Text = FormatAiServiceMessage($"[Error: {ex.Message}]");
                LoggingService.LogError("ReplaceDoc.AIScan", ex);
            }
            finally
            {
                BtnAIScan.IsEnabled = true;
                AIScanSpinner.Visibility = Visibility.Collapsed;
                UpdateInsuranceNumberHighlight();
            }
        }

        private void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            ShowPdfPage(_currentPdfPageIndex - 1);
        }

        private void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            ShowPdfPage(_currentPdfPageIndex + 1);
        }

        private void UpdatePager()
        {
            PageText.Text = $"{_currentPdfPageIndex + 1} / {_pdfPreviewPages.Count}";
            BtnPrevPage.IsEnabled = _currentPdfPageIndex > 0;
            BtnNextPage.IsEnabled = _currentPdfPageIndex < _pdfPreviewPages.Count - 1;
        }

        private void CleanupPdfPagePreviews()
        {
            _pdfPreviewPages.Clear();
            if (!string.IsNullOrWhiteSpace(_pdfPreviewTempFolder) && Directory.Exists(_pdfPreviewTempFolder))
            {
                try
                {
                    Directory.Delete(_pdfPreviewTempFolder, true);
                }
                catch
                {
                }
            }

            _pdfPreviewTempFolder = null;
        }

        private void CleanupSessionTemp()
        {
            CleanupPdfPagePreviews();
            if (!string.IsNullOrWhiteSpace(_sessionTempFolder) && Directory.Exists(_sessionTempFolder))
            {
                try
                {
                    Directory.Delete(_sessionTempFolder, true);
                }
                catch
                {
                }
            }

            _sessionTempFolder = null;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            foreach (var (key, (box, _)) in _fields)
            {
                NewValues[key] = box.Text.Trim();
            }
            Saved = true;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            CleanupSessionTemp();
            base.OnClosed(e);
        }
    }
}
