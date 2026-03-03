using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class ProblemsViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;
        private readonly Action _onProbDetailsClose;
        private readonly Action _onProbDetailsChanged;

        public ICommand GoBackCommand { get; }
        public ICommand OpenEmployeeCommand { get; }
        public ICommand IgnoreProblemCommand { get; }
        public ICommand RestoreProblemCommand { get; }
        public ICommand ToggleIgnoredListCommand { get; }
        public ICommand ExportToPdfCommand { get; }
        public ICommand FilterAllCommand { get; }
        public ICommand FilterExpiredCommand { get; }
        public ICommand FilterWarningCommand { get; }

        private List<EmployeeProblemGroup> _allGroups = new();

        private ObservableCollection<EmployeeProblemGroup> _problemGroups = new();
        public ObservableCollection<EmployeeProblemGroup> ProblemGroups
        {
            get => _problemGroups;
            set => SetProperty(ref _problemGroups, value);
        }

        private ObservableCollection<DocumentExpiryInfo> _ignoredProblems = new();
        public ObservableCollection<DocumentExpiryInfo> IgnoredProblems
        {
            get => _ignoredProblems;
            set => SetProperty(ref _ignoredProblems, value);
        }

        private bool _isIgnoredListVisible;
        public bool IsIgnoredListVisible
        {
            get => _isIgnoredListVisible;
            set => SetProperty(ref _isIgnoredListVisible, value);
        }

        private int _totalProblems;
        public int TotalProblems
        {
            get => _totalProblems;
            set => SetProperty(ref _totalProblems, value);
        }

        private int _expiredCount;
        public int ExpiredCount
        {
            get => _expiredCount;
            set => SetProperty(ref _expiredCount, value);
        }

        private int _warningCount;
        public int WarningCount
        {
            get => _warningCount;
            set => SetProperty(ref _warningCount, value);
        }

        private int _ignoredCount;
        public int IgnoredCount
        {
            get => _ignoredCount;
            set => SetProperty(ref _ignoredCount, value);
        }

        private bool _hasProblems;
        public bool HasProblems
        {
            get => _hasProblems;
            set => SetProperty(ref _hasProblems, value);
        }

        private bool _isEmployeeDetailsOpen;
        public bool IsEmployeeDetailsOpen
        {
            get => _isEmployeeDetailsOpen;
            set => SetProperty(ref _isEmployeeDetailsOpen, value);
        }

        private EmployeeDetailsViewModel? _employeeDetailsVm;
        public EmployeeDetailsViewModel? EmployeeDetailsVm
        {
            get => _employeeDetailsVm;
            set => SetProperty(ref _employeeDetailsVm, value);
        }

        private string _activeFilter = "All";
        public string ActiveFilter
        {
            get => _activeFilter;
            set
            {
                if (SetProperty(ref _activeFilter, value))
                {
                    OnPropertyChanged(nameof(IsFilterAll));
                    OnPropertyChanged(nameof(IsFilterExpired));
                    OnPropertyChanged(nameof(IsFilterWarning));
                    ApplyFilter();
                }
            }
        }

        public bool IsFilterAll => ActiveFilter == "All";
        public bool IsFilterExpired => ActiveFilter == "Expired";
        public bool IsFilterWarning => ActiveFilter == "Warning";

        public string Title => Res("ProbTitle") ?? "Problems — all companies";

        private static new string? Res(string key)
        {
            try { return Application.Current.FindResource(key) as string; }
            catch (Exception ex) { LoggingService.LogError("ProblemsViewModel.Res", ex); return null; }
        }

        private static string ResF(string key, params object[] args)
        {
            var fmt = Res(key);
            return fmt != null ? string.Format(fmt, args) : string.Join(" ", args);
        }

        private static string? DocRes(string key)
        {
            try { return App.DocumentLocalizationService?.Get(key) ?? Res(key); }
            catch (Exception ex) { LoggingService.LogError("ProblemsViewModel.DocRes", ex); return Res(key); }
        }

        private static string DocResF(string key, params object[] args)
        {
            var fmt = DocRes(key);
            return fmt != null ? string.Format(fmt, args) : string.Join(" ", args);
        }

        internal const string DocKeyPassport = "Паспорт";
        internal const string DocKeyVisa = "Віза";
        internal const string DocKeyInsurance = "Страховка";
        internal const string DocKeyWorkPermit = "Дозвіл на роботу";

        internal static string LocalizeDocType(string internalKey) => internalKey switch
        {
            DocKeyPassport => Res("ProbDocPassport") ?? internalKey,
            DocKeyVisa => Res("ProbDocVisa") ?? internalKey,
            DocKeyInsurance => Res("ProbDocInsurance") ?? internalKey,
            DocKeyWorkPermit => Res("ProbDocWorkPermit") ?? internalKey,
            _ => internalKey
        };

        internal static string DocLocalizeDocType(string internalKey) => internalKey switch
        {
            DocKeyPassport => DocRes("ProbDocPassport") ?? internalKey,
            DocKeyVisa => DocRes("ProbDocVisa") ?? internalKey,
            DocKeyInsurance => DocRes("ProbDocInsurance") ?? internalKey,
            DocKeyWorkPermit => DocRes("ProbDocWorkPermit") ?? internalKey,
            _ => internalKey
        };

        public static string DaysRemainingText(int days)
        {
            if (days < 0)
                return ResF("ProbDaysExpired", Math.Abs(days));
            if (days == 0)
                return Res("ProbDaysToday") ?? "expires today";
            return ResF("ProbDaysLeft", days);
        }

        private static string DocDaysRemainingText(int days)
        {
            if (days < 0)
                return DocResF("ProbDaysExpired", Math.Abs(days));
            if (days == 0)
                return DocRes("ProbDaysToday") ?? "expires today";
            return DocResF("ProbDaysLeft", days);
        }

        public ProblemsViewModel()
        {
            _employeeService = App.EmployeeService;
            _onProbDetailsClose = () => IsEmployeeDetailsOpen = false;
            _onProbDetailsChanged = () => LoadProblems();

            GoBackCommand = new RelayCommand(o => App.NavigationService?.NavigateTo(new MainViewModel()));
            OpenEmployeeCommand = new RelayCommand(o =>
            {
                if (o is EmployeeProblemGroup group)
                {
                    if (EmployeeDetailsVm != null)
                    {
                        EmployeeDetailsVm.RequestClose -= _onProbDetailsClose;
                        EmployeeDetailsVm.DataChanged -= _onProbDetailsChanged;
                    }
                    EmployeeDetailsVm = new EmployeeDetailsViewModel(group.FirmName, group.EmployeeFolder);
                    EmployeeDetailsVm.RequestClose += _onProbDetailsClose;
                    EmployeeDetailsVm.DataChanged += _onProbDetailsChanged;
                    IsEmployeeDetailsOpen = true;
                }
            });

            FilterAllCommand = new RelayCommand(o => ActiveFilter = "All");
            FilterExpiredCommand = new RelayCommand(o => ActiveFilter = "Expired");
            FilterWarningCommand = new RelayCommand(o => ActiveFilter = "Warning");

            IgnoreProblemCommand = new RelayCommand(o =>
            {
                if (o is DocumentExpiryInfo info)
                    ShowIgnoreMenu(info);
            });

            RestoreProblemCommand = new RelayCommand(o =>
            {
                if (o is DocumentExpiryInfo info)
                {
                    _employeeService.ClearIgnoredDocument(info.EmployeeFolder, info.DocumentType);
                    LoadProblems();
                }
            });

            ToggleIgnoredListCommand = new RelayCommand(o =>
            {
                IsIgnoredListVisible = !IsIgnoredListVisible;
            });

            ExportToPdfCommand = new RelayCommand(o => ExportToPdf());

            LoadProblems();
        }

        private void ShowIgnoreMenu(DocumentExpiryInfo info)
        {
            var snoozeOptions = new[] { 7, 14, 30, 60, 90 };

            var contextMenu = new System.Windows.Controls.ContextMenu();
            foreach (var days in snoozeOptions)
            {
                var item = new System.Windows.Controls.MenuItem
                {
                    Header = ResF("ProbIgnoreForDays", days),
                    Tag = days
                };
                item.Click += (s, e) =>
                {
                    var d = (int)((System.Windows.Controls.MenuItem)s!).Tag;
                    var untilDate = DateTime.Now.AddDays(d).ToString("yyyy-MM-dd");
                    _employeeService.SetIgnoredDocument(info.EmployeeFolder, info.DocumentType, untilDate);
                    LoadProblems();
                };
                contextMenu.Items.Add(item);
            }

            if (System.Windows.Input.Mouse.DirectlyOver is System.Windows.UIElement target)
                contextMenu.PlacementTarget = target;

            contextMenu.IsOpen = true;
        }

        private void LoadProblems()
        {
            try
            {
                var cs = App.CompanyService;
                var allCompanies = cs?.Companies;
                if (allCompanies == null) return;
                var activeProblems = new List<DocumentExpiryInfo>();
                var ignoredProblems = new List<DocumentExpiryInfo>();

                foreach (var company in allCompanies.Where(c => cs!.IsCompanyVisible(c)))
                {
                    try
                    {
                        var employees = _employeeService.GetEmployeesForFirm(company.Name);
                        foreach (var emp in employees)
                        {
                            CollectProblem(activeProblems, ignoredProblems, emp, DocKeyPassport, emp.PassportExpiry);
                            if (emp.EmployeeType != "eu_citizen")
                                CollectProblem(activeProblems, ignoredProblems, emp, DocKeyVisa, emp.VisaExpiry);
                            CollectProblem(activeProblems, ignoredProblems, emp, DocKeyInsurance, emp.InsuranceExpiry);
                            if (emp.EmployeeType == "work_permit")
                                CollectProblem(activeProblems, ignoredProblems, emp, DocKeyWorkPermit, emp.WorkPermitExpiry);

                            try
                            {
                                var empData = _employeeService.LoadEmployeeData(emp.EmployeeFolder);
                                if (empData?.CustomDocuments != null)
                                {
                                    foreach (var cd in empData.CustomDocuments)
                                    {
                                        if (!cd.IsHidden && !string.IsNullOrWhiteSpace(cd.ExpiryDate))
                                            CollectProblem(activeProblems, ignoredProblems, emp, cd.Name, cd.ExpiryDate);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LoggingService.LogError("ProblemsViewModel.LoadCustomDocs", ex);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("ProblemsViewModel.LoadProblems", ex);
                        Debug.WriteLine($"ProblemsViewModel: error loading {company.Name}: {ex.Message}");
                    }
                }

                // Group active problems by employee
                var groups = activeProblems
                    .GroupBy(p => p.EmployeeFolder)
                    .Select(g =>
                    {
                        var first = g.First();
                        return new EmployeeProblemGroup
                        {
                            EmployeeName = first.EmployeeName,
                            EmployeeFolder = first.EmployeeFolder,
                            FirmName = first.FirmName,
                            Issues = new ObservableCollection<DocumentExpiryInfo>(
                                g.OrderBy(x => x.DaysRemaining))
                        };
                    })
                    .OrderBy(g => g.Issues.Min(i => i.DaysRemaining))
                    .ToList();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    _allGroups = groups;
                    IgnoredProblems = new ObservableCollection<DocumentExpiryInfo>(ignoredProblems.OrderBy(p => p.EmployeeName));
                    TotalProblems = activeProblems.Count;
                    ExpiredCount = activeProblems.Count(p => p.Severity == "Expired" || p.Severity == "Critical");
                    WarningCount = activeProblems.Count(p => p.Severity == "Warning");
                    IgnoredCount = ignoredProblems.Count;
                    ApplyFilter();
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogError("ProblemsViewModel.LoadProblems", ex);
                Debug.WriteLine($"ProblemsViewModel.LoadProblems error: {ex.Message}");
            }
        }

        private void ApplyFilter()
        {
            IEnumerable<EmployeeProblemGroup> filtered;

            if (_activeFilter == "Expired")
            {
                filtered = _allGroups
                    .Select(g =>
                    {
                        var expiredIssues = g.Issues
                            .Where(i => i.Severity == "Expired" || i.Severity == "Critical")
                            .ToList();
                        if (expiredIssues.Count == 0) return null;
                        return new EmployeeProblemGroup
                        {
                            EmployeeName = g.EmployeeName,
                            EmployeeFolder = g.EmployeeFolder,
                            FirmName = g.FirmName,
                            Issues = new ObservableCollection<DocumentExpiryInfo>(expiredIssues)
                        };
                    })
                    .Where(g => g != null)!;
            }
            else if (_activeFilter == "Warning")
            {
                filtered = _allGroups
                    .Select(g =>
                    {
                        var warningIssues = g.Issues
                            .Where(i => i.Severity == "Warning")
                            .ToList();
                        if (warningIssues.Count == 0) return null;
                        return new EmployeeProblemGroup
                        {
                            EmployeeName = g.EmployeeName,
                            EmployeeFolder = g.EmployeeFolder,
                            FirmName = g.FirmName,
                            Issues = new ObservableCollection<DocumentExpiryInfo>(warningIssues)
                        };
                    })
                    .Where(g => g != null)!;
            }
            else
            {
                filtered = _allGroups;
            }

            var list = filtered.ToList();
            Application.Current.Dispatcher.Invoke(() =>
            {
                ProblemGroups = new ObservableCollection<EmployeeProblemGroup>(list!);
                HasProblems = list.Count > 0;
            });
        }

        private void CollectProblem(List<DocumentExpiryInfo> activeList, List<DocumentExpiryInfo> ignoredList,
            EmployeeSummary emp, string docType, string expiryDate)
        {
            if (string.IsNullOrWhiteSpace(expiryDate)) return;

            var severity = DateParsingHelper.GetSeverity(expiryDate);
            if (severity == "Ok" || severity == "Unknown") return;

            var days = DateParsingHelper.GetDaysRemaining(expiryDate);
            var info = new DocumentExpiryInfo
            {
                EmployeeName = emp.FullName,
                EmployeeFolder = emp.EmployeeFolder,
                FirmName = emp.FirmName,
                DocumentType = docType,
                DocumentTypeDisplay = LocalizeDocType(docType),
                ExpiryDateStr = expiryDate,
                ExpiryDate = DateParsingHelper.TryParseDate(expiryDate) ?? DateTime.MinValue,
                DaysRemaining = days,
                Severity = severity
            };

            if (_employeeService.IsDocumentIgnored(emp.EmployeeFolder, docType))
            {
                var untilStr = _employeeService.GetIgnoredUntil(emp.EmployeeFolder, docType);
                info.IgnoredUntil = untilStr ?? string.Empty;
                ignoredList.Add(info);
            }
            else
            {
                activeList.Add(info);
            }
        }

        /// <summary>
        /// Static helper to count problems across ALL companies (used by MainViewModel for badge).
        /// </summary>
        public static int CountAllProblems()
        {
            try
            {
                var cs2 = App.CompanyService;
                var companies = cs2?.Companies;
                var empService = App.EmployeeService;
                if (companies == null || empService == null) return 0;

                int count = 0;
                foreach (var company in companies.Where(c => cs2!.IsCompanyVisible(c)))
                {
                    try
                    {
                        var employees = empService.GetEmployeesForFirm(company.Name);
                        foreach (var emp in employees)
                        {
                            if (IsProblematic(emp.PassportExpiry) && !empService.IsDocumentIgnored(emp.EmployeeFolder, DocKeyPassport)) count++;
                            if (emp.EmployeeType != "eu_citizen" && IsProblematic(emp.VisaExpiry) && !empService.IsDocumentIgnored(emp.EmployeeFolder, DocKeyVisa)) count++;
                            if (IsProblematic(emp.InsuranceExpiry) && !empService.IsDocumentIgnored(emp.EmployeeFolder, DocKeyInsurance)) count++;
                            if (emp.EmployeeType == "work_permit" && IsProblematic(emp.WorkPermitExpiry) && !empService.IsDocumentIgnored(emp.EmployeeFolder, DocKeyWorkPermit)) count++;
                        }
                    }
                    catch (Exception ex) { LoggingService.LogError("ProblemsViewModel.CountAllProblems", ex); }
                }
                return count;
            }
            catch (Exception ex) { LoggingService.LogError("ProblemsViewModel.CountAllProblems", ex); return 0; }
        }

        private static bool IsProblematic(string dateStr)
        {
            var s = DateParsingHelper.GetSeverity(dateStr);
            return s == "Expired" || s == "Critical" || s == "Warning";
        }

        private void ExportToPdf()
        {
            try
            {
                var dialog = new SaveFileDialog
                {
                    Filter = "PDF (*.pdf)|*.pdf",
                    FileName = $"{DocRes("ProbPdfTitle") ?? "Problems"}_{DateTime.Now:yyyy-MM-dd}.pdf",
                    Title = Res("ProbPdfSaveTitle") ?? "Save problems report"
                };

                if (dialog.ShowDialog() != true) return;

                var doc = new PdfDocument();
                doc.Info.Title = DocRes("ProbPdfTitle") ?? "Problems — report";

                var page = doc.AddPage();
                page.Width = XUnit.FromMillimeter(210);
                page.Height = XUnit.FromMillimeter(297);
                var gfx = XGraphics.FromPdfPage(page);

                var fontTitle = new XFont("Arial", 18, XFontStyleEx.Bold);
                var fontSubtitle = new XFont("Arial", 11);
                var fontEmployeeName = new XFont("Arial", 13, XFontStyleEx.Bold);
                var fontFirm = new XFont("Arial", 10);
                var fontIssue = new XFont("Arial", 10);
                var fontIssueBold = new XFont("Arial", 10, XFontStyleEx.Bold);
                var fontBadge = new XFont("Arial", 9, XFontStyleEx.Bold);

                var topLeftFormat = new XStringFormat
                {
                    Alignment = XStringAlignment.Near,
                    LineAlignment = XLineAlignment.Near
                };

                double marginLeft = 40;
                double marginRight = 40;
                double pageWidth = page.Width.Point;
                double pageHeight = page.Height.Point;
                double contentWidth = pageWidth - marginLeft - marginRight;
                double y = 40;

                var brushBlack = XBrushes.Black;
                var brushGray = new XSolidBrush(XColor.FromArgb(128, 128, 128));
                var brushRed = new XSolidBrush(XColor.FromArgb(198, 40, 40));
                var brushOrange = new XSolidBrush(XColor.FromArgb(239, 108, 0));
                var brushRedBg = new XSolidBrush(XColor.FromArgb(255, 235, 238));
                var brushOrangeBg = new XSolidBrush(XColor.FromArgb(255, 243, 224));
                var penBorder = new XPen(XColor.FromArgb(200, 200, 200), 0.5);

                // --- Title ---
                gfx.DrawString(DocRes("ProbPdfTitle") ?? "Problems — report", fontTitle, brushBlack,
                    new XPoint(marginLeft, y), topLeftFormat);
                y += 26;

                gfx.DrawString(DocResF("ProbPdfDate", DateTime.Now.ToString("dd.MM.yyyy")), fontSubtitle, brushGray,
                    new XPoint(marginLeft, y), topLeftFormat);
                y += 20;

                // --- Stats line ---
                var statsText = $"{DocRes("ProbPdfExpired")}: {ExpiredCount}    {DocRes("ProbPdfWarning")}: {WarningCount}    {DocRes("ProbPdfTotal")}: {TotalProblems}    {DocRes("ProbPdfIgnored")}: {IgnoredCount}";
                gfx.DrawString(statsText, fontSubtitle, brushBlack,
                    new XPoint(marginLeft, y), topLeftFormat);
                y += 10;

                // Separator
                gfx.DrawLine(new XPen(XColor.FromArgb(220, 220, 220), 1), marginLeft, y, marginLeft + contentWidth, y);
                y += 14;

                // --- Employee groups ---
                foreach (var group in ProblemGroups)
                {
                    double blockHeight = 24 + group.Issues.Count * 20 + 14;
                    if (y + blockHeight > pageHeight - 40)
                    {
                        page = doc.AddPage();
                        page.Width = XUnit.FromMillimeter(210);
                        page.Height = XUnit.FromMillimeter(297);
                        gfx = XGraphics.FromPdfPage(page);
                        y = 40;
                    }

                    // Employee name
                    gfx.DrawString(group.EmployeeName, fontEmployeeName, brushBlack,
                        new XPoint(marginLeft, y), topLeftFormat);
                    y += 17;

                    // Firm name
                    gfx.DrawString(group.FirmName, fontFirm, brushGray,
                        new XPoint(marginLeft + 12, y), topLeftFormat);
                    y += 16;

                    // Issues
                    foreach (var issue in group.Issues)
                    {
                        if (y > pageHeight - 40)
                        {
                            page = doc.AddPage();
                            page.Width = XUnit.FromMillimeter(210);
                            page.Height = XUnit.FromMillimeter(297);
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;
                        }

                        var isExpired = issue.Severity == "Expired" || issue.Severity == "Critical";
                        var severityBrush = isExpired ? brushRed : brushOrange;
                        var bgBrush = isExpired ? brushRedBg : brushOrangeBg;

                        // Background stripe
                        gfx.DrawRectangle(bgBrush, marginLeft + 8, y - 1, contentWidth - 8, 16);

                        // Severity dot
                        var dotBrush = isExpired ? brushRed : brushOrange;
                        gfx.DrawEllipse(dotBrush, marginLeft + 14, y + 2, 6, 6);

                        // Document type
                        gfx.DrawString(DocLocalizeDocType(issue.DocumentType), fontIssueBold, brushBlack,
                            new XPoint(marginLeft + 26, y), topLeftFormat);

                        // Expiry date
                        gfx.DrawString(issue.ExpiryDateStr, fontIssue, brushGray,
                            new XPoint(marginLeft + 120, y), topLeftFormat);

                        // Status text
                        var statusText = DocDaysRemainingText(issue.DaysRemaining);
                        gfx.DrawString(statusText, fontBadge, severityBrush,
                            new XPoint(marginLeft + 220, y), topLeftFormat);

                        y += 18;
                    }

                    // Separator after group
                    y += 4;
                    gfx.DrawLine(penBorder, marginLeft, y, marginLeft + contentWidth, y);
                    y += 10;
                }

                // --- Ignored section ---
                if (IgnoredProblems.Count > 0)
                {
                    if (y + 40 > pageHeight - 40)
                    {
                        page = doc.AddPage();
                        page.Width = XUnit.FromMillimeter(210);
                        page.Height = XUnit.FromMillimeter(297);
                        gfx = XGraphics.FromPdfPage(page);
                        y = 40;
                    }

                    var brushIndigo = new XSolidBrush(XColor.FromArgb(57, 73, 171));

                    y += 6;
                    gfx.DrawString(DocResF("ProbPdfIgnoredSection", IgnoredProblems.Count), fontEmployeeName, brushIndigo,
                        new XPoint(marginLeft, y), topLeftFormat);
                    y += 20;

                    foreach (var ign in IgnoredProblems)
                    {
                        if (y > pageHeight - 40)
                        {
                            page = doc.AddPage();
                            page.Width = XUnit.FromMillimeter(210);
                            page.Height = XUnit.FromMillimeter(297);
                            gfx = XGraphics.FromPdfPage(page);
                            y = 40;
                        }

                        gfx.DrawString($"{ign.EmployeeName} — {DocLocalizeDocType(ign.DocumentType)}", fontIssue, brushBlack,
                            new XPoint(marginLeft + 12, y), topLeftFormat);
                        var untilPrefix = DocRes("ProbUntil") ?? "until ";
                        gfx.DrawString($"{untilPrefix}{ign.IgnoredUntil}", fontFirm, brushIndigo,
                            new XPoint(marginLeft + 300, y), topLeftFormat);
                        y += 16;
                    }
                }

                doc.Save(dialog.FileName);
                App.ActivityLogService?.Log("ExportPdf", "Export", "", "",
                    $"Експортовано звіт проблем → PDF");
                DocumentGenerationService.OpenFile(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ResF("ProbPdfExportError", ex.Message), Res("ProbPdfError") ?? "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
