using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class FirmCheckItem : INotifyPropertyChanged
    {
        public string FirmName { get; set; } = string.Empty;
        public int EmployeeCount { get; set; }

        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public class AdvanceTableViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;
        private readonly NavigationService _navigationService;
        private readonly FinanceModuleViewModelFactory _financeModuleViewModelFactory;
        private readonly CompanyService _companyService;
        private readonly ActivityLogService _activityLogService;
        private readonly DocumentLocalizationService _documentLocalizationService;

        public ObservableCollection<FirmCheckItem> Firms { get; } = new();
        public ICommand GoBackCommand { get; }
        public ICommand GeneratePdfCommand { get; }
        public ICommand SelectAllCommand { get; }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private const int EmptyRowsPerFirm = 4;
        private const int AdvanceColumns = 4;

        public AdvanceTableViewModel(
            EmployeeService? employeeService = null,
            NavigationService? navigationService = null,
            FinanceModuleViewModelFactory? financeModuleViewModelFactory = null,
            CompanyService? companyService = null,
            ActivityLogService? activityLogService = null,
            DocumentLocalizationService? documentLocalizationService = null)
        {
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _financeModuleViewModelFactory = financeModuleViewModelFactory ?? throw new InvalidOperationException("FinanceModuleViewModelFactory is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _documentLocalizationService = documentLocalizationService ?? throw new InvalidOperationException("DocumentLocalizationService is not initialized.");

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo(_financeModuleViewModelFactory.CreateTablesMenu()));
            GeneratePdfCommand = new RelayCommand(o => GeneratePdf());
            SelectAllCommand = new RelayCommand(o => ToggleSelectAll());

            LoadFirms();
        }

        private void LoadFirms()
        {
            Firms.Clear();
            foreach (var company in _companyService.VisibleCompanies)
            {
                var employees = _employeeService.GetEmployeesForFirm(company.Name);
                Firms.Add(new FirmCheckItem
                {
                    FirmName = company.Name,
                    EmployeeCount = employees.Count,
                    IsSelected = true
                });
            }
        }

        private void ToggleSelectAll()
        {
            bool allSelected = Firms.All(f => f.IsSelected);
            foreach (var f in Firms)
                f.IsSelected = !allSelected;
        }

        private string GetString(string key)
        {
            return Application.Current?.TryFindResource(key) as string ?? key;
        }

        private string DocString(string key) =>
            _documentLocalizationService.Get(key) ??
            (Application.Current?.TryFindResource(key) as string ?? key);

        private void GeneratePdf()
        {
            var selectedFirms = Firms.Where(f => f.IsSelected).ToList();
            if (!selectedFirms.Any())
            {
                StatusMessage = GetString("AdvTableNoFirms");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Advance_{DateTime.Now:yyyy-MM-dd}.pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var doc = new PdfDocument();
                doc.Info.Title = DocString("AdvTablePdfTitle");

                var fontTitle = new XFont("Segoe UI", 13, XFontStyleEx.BoldItalic);
                var fontHeader = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
                var fontCell = new XFont("Segoe UI", 12, XFontStyleEx.Bold);
                var fontNum = new XFont("Segoe UI", 10, XFontStyleEx.Regular);
                var fontPage = new XFont("Segoe UI", 11, XFontStyleEx.Bold);

                double marginLeft = 30;
                double marginTop = 30;
                double marginBottom = 30;
                double rowHeight = 34;
                double headerRowHeight = 24;

                XGraphics? gfx = null;
                double pageW = 0, pageH = 0;
                double y = 0;
                int pageNum = 0;

                PdfPage AddPage()
                {
                    var p = doc.AddPage();
                    p.Size = PdfSharp.PageSize.A4;
                    p.Orientation = PdfSharp.PageOrientation.Portrait;
                    gfx?.Dispose();
                    gfx = XGraphics.FromPdfPage(p);
                    pageW = p.Width.Point;
                    pageH = p.Height.Point;
                    y = marginTop;
                    pageNum++;

                    // Page number: odd pages = right, even pages = left (book layout)
                    if (pageNum % 2 == 1)
                        gfx.DrawString(pageNum.ToString(), fontPage, XBrushes.Black,
                            new XRect(pageW - marginLeft - 30, 12, 30, 14), XStringFormats.CenterRight);
                    else
                        gfx.DrawString(pageNum.ToString(), fontPage, XBrushes.Black,
                            new XRect(marginLeft, 12, 30, 14), XStringFormats.CenterLeft);

                    return p;
                }

                void EnsureSpace(double needed)
                {
                    if (y + needed > pageH - marginBottom)
                        AddPage();
                }

                double contentW = 0;
                double colName = 0;
                double colDateAmount = 0;
                double colSign = 0;
                double numW = 20;

                void CalcColumns()
                {
                    contentW = pageW - marginLeft * 2;
                    double remaining = contentW - numW;
                    colName = remaining * 0.24;
                    double pairW = remaining * 0.76 / AdvanceColumns;
                    colDateAmount = pairW * 0.5;
                    colSign = pairW * 0.5;
                }

                AddPage();
                CalcColumns();

                void DrawTableHeader(string firmName, int empCount)
                {
                    double hdrH = headerRowHeight * 2;
                    double titleH = 26;
                    EnsureSpace(titleH + 4 + hdrH + rowHeight * 2);

                    // Firm name — centered, in a frame
                    var penFrame = new XPen(XColors.Black, 1.0);
                    gfx!.DrawRectangle(penFrame, XBrushes.White, marginLeft, y, contentW, titleH);
                    gfx.DrawString(firmName, fontTitle, XBrushes.Black,
                        new XRect(marginLeft, y, contentW, titleH), XStringFormats.Center);

                    y += titleH + 2;

                    var pen = new XPen(XColors.Black, 1.0);
                    var penDiv = new XPen(XColors.Black, 0.5);

                    double hY = y;

                    gfx.DrawRectangle(pen, XBrushes.White, marginLeft, hY, numW + colName, hdrH);
                    gfx.DrawString(DocString("AdvTableColName"), fontHeader, XBrushes.Black,
                        new XRect(marginLeft, hY, numW + colName, hdrH), XStringFormats.Center);

                    double dx = marginLeft + numW + colName;
                    for (int i = 0; i < AdvanceColumns; i++)
                    {
                        // Left column: Data/Částka — one cell, split by horizontal divider
                        gfx.DrawRectangle(pen, XBrushes.White, dx, hY, colDateAmount, hdrH);
                        gfx.DrawLine(penDiv, dx, hY + headerRowHeight, dx + colDateAmount, hY + headerRowHeight);

                        gfx.DrawString(DocString("AdvTableColDate"), fontHeader, XBrushes.Black,
                            new XRect(dx, hY, colDateAmount, headerRowHeight), XStringFormats.Center);
                        gfx.DrawString(DocString("AdvTableColAmount"), fontHeader, XBrushes.Black,
                            new XRect(dx, hY + headerRowHeight, colDateAmount, headerRowHeight), XStringFormats.Center);

                        gfx.DrawRectangle(pen, XBrushes.White, dx + colDateAmount, hY, colSign, hdrH);
                        gfx.DrawString(DocString("AdvTableColSign"), fontHeader, XBrushes.Black,
                            new XRect(dx + colDateAmount, hY, colSign, hdrH), XStringFormats.Center);

                        dx += colDateAmount + colSign;
                    }

                    y = hY + hdrH;
                }

                void DrawEmployeeRow(int num, string name)
                {
                    EnsureSpace(rowHeight);

                    var pen = new XPen(XColors.Black, 0.8);
                    var penDiv = new XPen(XColors.Black, 0.4);
                    double cx = marginLeft;
                    double halfRow = rowHeight / 2;

                    // Number cell — full height
                    gfx!.DrawRectangle(pen, XBrushes.White, cx, y, numW, rowHeight);
                    gfx.DrawString(num.ToString(), fontNum, XBrushes.Black,
                        new XRect(cx, y, numW, rowHeight), XStringFormats.Center);
                    cx += numW;

                    // Name cell — full height
                    gfx.DrawRectangle(pen, XBrushes.White, cx, y, colName, rowHeight);
                    if (!string.IsNullOrEmpty(name))
                    {
                        gfx.DrawString(name, fontCell, XBrushes.Black,
                            new XRect(cx + 5, y, colName - 10, rowHeight), XStringFormats.CenterLeft);
                    }
                    cx += colName;

                    for (int i = 0; i < AdvanceColumns; i++)
                    {
                        // Data/Částka cell — full height, split by horizontal divider
                        gfx.DrawRectangle(pen, XBrushes.White, cx, y, colDateAmount, rowHeight);
                        gfx.DrawLine(penDiv, cx, y + halfRow, cx + colDateAmount, y + halfRow);
                        cx += colDateAmount;

                        // Podpis cell — full height, solid (no split)
                        gfx.DrawRectangle(pen, XBrushes.White, cx, y, colSign, rowHeight);
                        cx += colSign;
                    }

                    y += rowHeight;
                }

                foreach (var firmItem in selectedFirms)
                {
                    var employees = _employeeService.GetEmployeesForFirm(firmItem.FirmName)
                        .Where(e => string.IsNullOrEmpty(e.Status) || e.Status == "Active")
                        .OrderBy(e => e.FullName)
                        .ToList();

                    double neededForFirm = 24 + headerRowHeight * 2 + rowHeight * 2;
                    if (y > marginTop && y + neededForFirm > pageH - marginBottom)
                        AddPage();

                    DrawTableHeader(firmItem.FirmName, employees.Count);

                    int num = 1;
                    foreach (var emp in employees)
                    {
                        DrawEmployeeRow(num++, emp.FullName);
                    }

                    for (int i = 0; i < EmptyRowsPerFirm; i++)
                    {
                        DrawEmployeeRow(num++, string.Empty);
                    }

                    y += 8;
                }

                gfx?.Dispose();
                doc.Save(dlg.FileName);
                _activityLogService.Log("ExportPdf", "Export", "", "",
                    $"Згенеровано авансову відомість → PDF");
                StatusMessage = GetString("AdvTableSuccess");
                MessageBox.Show(GetString("AdvTableSuccess"), GetString("TitleSuccess"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusMessage = string.Format(GetString("MsgErrorGeneric"), ex.Message);
                MessageBox.Show(string.Format(GetString("MsgErrorGeneric"), ex.Message), GetString("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
