using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Views
{
    public partial class ReplaceDocumentWindow : Window
    {
        private readonly string _docType;
        private readonly EmployeeData _data;
        private string? _selectedFilePath;
        private readonly Dictionary<string, (TextBox newBox, string oldValue)> _fields = new();
        private readonly List<string> _pdfPreviewPages = new();
        private int _currentPdfPageIndex;
        private string? _pdfPreviewTempFolder;

        public bool Saved { get; private set; }
        public string? ResultFilePath => _selectedFilePath;
        public Dictionary<string, string> NewValues { get; } = new();

        public ReplaceDocumentWindow(string docType, EmployeeData data)
        {
            InitializeComponent();
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
                    Margin = new Thickness(0, 0, 0, 10)
                };
                FieldsPanel.Children.Add(tb);
                _fields[key] = (tb, oldValue);
            }
        }

        private bool IsEuIdCard => _data.EmployeeType == "eu_citizen" && _data.EuDocumentType == "id_card";

        private List<(string key, string label, string oldValue)> GetFieldsForType()
        {
            var passportFields = new List<(string key, string label, string oldValue)>
            {
                ("PassportNumber", Res("DetFieldPassportNum"), _data.PassportNumber),
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
                    ("VisaType", Res("DetFieldVisaType"), _data.VisaType),
                    ("VisaExpiry", Res("DetFieldExpiry"), _data.VisaExpiry),
                    ("WorkPermitName", Res("DetFieldWorkPermitName"), _data.WorkPermitName),
                },
                "insurance" => new()
                {
                    ("InsuranceCompanyShort", Res("DetFieldInsCompany"), _data.InsuranceCompanyShort),
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
                Filter = "Documents|*.jpg;*.jpeg;*.png;*.heic;*.pdf"
            };
            if (dialog.ShowDialog() != true) return;
            LoadPreview(dialog.FileName);
        }

        private void LoadPreview(string path)
        {
            _selectedFilePath = path;
            CleanupPdfTemp();
            PreviewImage.Source = null;
            NoImageText.Text = Res("ReplDocUploadHint");
            NoImageText.Visibility = Visibility.Visible;
            PagerPanel.Visibility = Visibility.Collapsed;
            _pdfPreviewPages.Clear();
            _currentPdfPageIndex = 0;

            if (IsPdfFile(path))
            {
                LoadPdfPreview(path);
                return;
            }

            LoadBitmapPreview(path);
        }

        private void LoadPdfPreview(string path)
        {
            try
            {
                NoImageText.Text = Res("PreviewLoading");
                NoImageText.Visibility = Visibility.Visible;
                _pdfPreviewTempFolder = Path.Combine(Path.GetTempPath(), "AC_ReplDoc_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(_pdfPreviewTempFolder);

                var pages = App.EmployeeService?.RenderPdfPages(path, _pdfPreviewTempFolder, "preview") ?? new List<string>();
                if (pages.Count == 0)
                {
                    CleanupPdfTemp();
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

        private static bool IsPdfFile(string? path) =>
            !string.IsNullOrEmpty(path)
            && string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);

        private void BtnEditor_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedFilePath) || !File.Exists(_selectedFilePath))
            {
                MessageBox.Show(Res("MsgUploadFirst"), Res("MsgHint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (IsPdfFile(_selectedFilePath))
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
                    LoadPreview(_selectedFilePath);
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
            if (!App.GeminiApiService.IsConfigured)
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
                var prompt = AIScanPrompts.GetPrompt(docKey);
                var result = await App.GeminiApiService.ChatWithImageAsync(_selectedFilePath, prompt, null);

                if (result.StartsWith("["))
                {
                    AIScanStatus.Text = FormatAiServiceMessage(result);
                    return;
                }

                var parsed = AIScanPrompts.ParseResponse(result);
                if (parsed.Count == 0)
                {
                    AIScanStatus.Text = Res("AIScanNoData");
                    return;
                }

                int filled = 0;
                foreach (var (key, value) in parsed)
                {
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

        private void CleanupPdfTemp()
        {
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
            CleanupPdfTemp();
            base.OnClosed(e);
        }
    }
}
