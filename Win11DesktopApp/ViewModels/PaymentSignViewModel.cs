using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class PaymentSignViewModel : ViewModelBase
    {
        private readonly EmployeeService _employeeService;
        private readonly NavigationService _navigationService;
        private readonly FinanceModuleViewModelFactory _financeModuleViewModelFactory;
        private readonly FinanceService _financeService;
        private readonly CompanyService _companyService;
        private readonly ActivityLogService _activityLogService;
        private readonly DocumentLocalizationService _documentLocalizationService;

        public ObservableCollection<FirmCheckItem> Firms { get; } = new();
        public ObservableCollection<int> Months { get; } = new(Enumerable.Range(1, 12));
        public ObservableCollection<int> Years { get; } = new(Enumerable.Range(DateTime.Now.Year - 1, 7));

        private int _selectedMonth;
        public int SelectedMonth
        {
            get => _selectedMonth;
            set { if (SetProperty(ref _selectedMonth, value)) LoadFirms(); }
        }

        private int _selectedYear;
        public int SelectedYear
        {
            get => _selectedYear;
            set { if (SetProperty(ref _selectedYear, value)) LoadFirms(); }
        }

        private string _paymentDate = string.Empty;
        public string PaymentDate
        {
            get => _paymentDate;
            set => SetProperty(ref _paymentDate, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ICommand GoBackCommand { get; }
        public ICommand GeneratePdfCommand { get; }
        public ICommand SelectAllCommand { get; }

        public PaymentSignViewModel(
            EmployeeService? employeeService = null,
            NavigationService? navigationService = null,
            FinanceModuleViewModelFactory? financeModuleViewModelFactory = null,
            FinanceService? financeService = null,
            CompanyService? companyService = null,
            ActivityLogService? activityLogService = null,
            DocumentLocalizationService? documentLocalizationService = null)
        {
            _employeeService = employeeService ?? throw new InvalidOperationException("EmployeeService is not initialized.");
            _navigationService = navigationService ?? throw new InvalidOperationException("NavigationService is not initialized.");
            _financeModuleViewModelFactory = financeModuleViewModelFactory ?? throw new InvalidOperationException("FinanceModuleViewModelFactory is not initialized.");
            _financeService = financeService ?? throw new InvalidOperationException("FinanceService is not initialized.");
            _companyService = companyService ?? throw new InvalidOperationException("CompanyService is not initialized.");
            _activityLogService = activityLogService ?? throw new InvalidOperationException("ActivityLogService is not initialized.");
            _documentLocalizationService = documentLocalizationService ?? throw new InvalidOperationException("DocumentLocalizationService is not initialized.");

            var now = DateTime.Now;
            _selectedMonth = now.Month;
            _selectedYear = now.Year;
            _paymentDate = now.ToString("dd.MM.yyyy");

            GoBackCommand = new RelayCommand(o => _navigationService.NavigateTo(_financeModuleViewModelFactory.CreateTablesMenu()));
            GeneratePdfCommand = new RelayCommand(o => GeneratePdf());
            SelectAllCommand = new RelayCommand(o => ToggleSelectAll());

            LoadFirms();
        }

        private void LoadFirms()
        {
            var prevSelected = Firms.Where(f => f.IsSelected).Select(f => f.FirmName).ToHashSet();
            Firms.Clear();

            var (salaryEntries, _) = _financeService.LoadAllFirmPayments(_selectedYear, _selectedMonth);

            var countByFirm = salaryEntries
                .GroupBy(e => e.FirmName)
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var company in _companyService.Companies.Where(c => _companyService.IsCompanyVisibleForPeriod(c, _selectedYear, _selectedMonth)))
            {
                countByFirm.TryGetValue(company.Name, out int count);
                Firms.Add(new FirmCheckItem
                {
                    FirmName = company.Name,
                    EmployeeCount = count,
                    IsSelected = prevSelected.Count == 0 || prevSelected.Contains(company.Name)
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
            if (!PolicyService.EnsureExportsAllowed("згенерувати PDF для підписів"))
                return;

            var selectedFirms = Firms.Where(f => f.IsSelected).ToList();
            if (!selectedFirms.Any())
            {
                StatusMessage = GetString("AdvTableNoFirms");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Vyplata_{SelectedMonth:D2}_{SelectedYear}.pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var selectedFirmNames = new HashSet<string>(selectedFirms.Select(f => f.FirmName));
                var (salaryEntries, _) = _financeService.LoadAllFirmPayments(SelectedYear, SelectedMonth);

                var distinctRows = salaryEntries
                    .Where(e => selectedFirmNames.Contains(e.FirmName))
                    .GroupBy(e => e.FullName + "|" + e.FirmName)
                    .Select(g => (Name: g.First().FullName, Firm: g.First().FirmName))
                    .OrderBy(r => r.Firm).ThenBy(r => r.Name)
                    .ToList();

                var doc = new PdfDocument();
                doc.Info.Title = string.Format(DocString("PaySignPdfTitle"), SelectedMonth.ToString("D2"), SelectedYear);

                var fontTitle = new XFont("Segoe UI", 14, XFontStyleEx.Bold);
                var fontHeader = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
                var fontCell = new XFont("Segoe UI", 9, XFontStyleEx.Bold);
                var fontInfo = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
                var fontSmall = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
                var fontPage = new XFont("Segoe UI", 24, XFontStyleEx.Bold);

                double marginLeft = 30;
                double marginTop = 36;
                double marginBottom = 44;
                double rowHeight = 26;

                XGraphics? gfx = null;
                double pageW = 0, pageH = 0;
                double y = 0;
                int pageNum = 0;
                int totalPages = (int)Math.Ceiling(distinctRows.Count / 28.0);
                if (totalPages < 1) totalPages = 1;

                double contentW = 0;
                double colName = 0, colFirm = 0, colDate = 0, colInfo = 0, colSign = 0;

                void CalcCols()
                {
                    contentW = pageW - marginLeft * 2;
                    colName = contentW * 0.19;
                    colFirm = contentW * 0.36;
                    colDate = contentW * 0.11;
                    colInfo = contentW * 0.24;
                    colSign = contentW * 0.10;
                }

                PdfPage AddPage()
                {
                    var p = doc.AddPage();
                    p.Size = PdfSharp.PageSize.A4;
                    p.Orientation = PdfSharp.PageOrientation.Landscape;
                    gfx?.Dispose();
                    gfx = XGraphics.FromPdfPage(p);
                    pageW = p.Width.Point;
                    pageH = p.Height.Point;
                    pageNum++;
                    CalcCols();

                    // Title
                    var pen = new XPen(XColors.Black, 1.0);
                    double titleH = 28;
                    gfx.DrawRectangle(pen, XBrushes.White, marginLeft, marginTop, contentW, titleH);
                    gfx.DrawString(string.Format(DocString("PaySignPdfTitle"), SelectedMonth.ToString("D2"), SelectedYear),
                        fontTitle, XBrushes.Black,
                        new XRect(marginLeft, marginTop, contentW, titleH), XStringFormats.Center);

                    // Page number in a separate visible frame.
                    double pageBoxWidth = 62;
                    double pageBoxHeight = titleH;
                    double pageBoxX = pageNum % 2 == 1
                        ? pageW - marginLeft - pageBoxWidth
                        : marginLeft;
                    var pageBoxRect = new XRect(pageBoxX, marginTop, pageBoxWidth, pageBoxHeight);
                    gfx.DrawRectangle(pen, XBrushes.White, pageBoxRect);
                    gfx.DrawString(pageNum.ToString(), fontPage, XBrushes.Black, pageBoxRect, XStringFormats.Center);

                    y = marginTop + titleH;

                    // Column headers
                    double hY = y;
                    gfx.DrawRectangle(pen, XBrushes.White, marginLeft, hY, colName, rowHeight);
                    gfx.DrawRectangle(pen, XBrushes.White, marginLeft + colName, hY, colFirm, rowHeight);
                    gfx.DrawRectangle(pen, XBrushes.White, marginLeft + colName + colFirm, hY, colDate, rowHeight);
                    gfx.DrawRectangle(pen, XBrushes.White, marginLeft + colName + colFirm + colDate, hY, colInfo, rowHeight);
                    gfx.DrawRectangle(pen, XBrushes.White, marginLeft + colName + colFirm + colDate + colInfo, hY, colSign, rowHeight);

                    gfx.DrawString(DocString("PaySignColName"), fontHeader, XBrushes.Black,
                        new XRect(marginLeft, hY, colName, rowHeight), XStringFormats.Center);
                    gfx.DrawString(DocString("PaySignColFirm"), fontHeader, XBrushes.Black,
                        new XRect(marginLeft + colName, hY, colFirm, rowHeight), XStringFormats.Center);
                    gfx.DrawString(DocString("PaySignColDate"), fontHeader, XBrushes.Black,
                        new XRect(marginLeft + colName + colFirm, hY, colDate, rowHeight), XStringFormats.Center);
                    gfx.DrawString(DocString("PaySignColInfo"), fontHeader, XBrushes.Black,
                        new XRect(marginLeft + colName + colFirm + colDate, hY, colInfo, rowHeight), XStringFormats.Center);
                    gfx.DrawString(DocString("PaySignColSign"), fontHeader, XBrushes.Black,
                        new XRect(marginLeft + colName + colFirm + colDate + colInfo, hY, colSign, rowHeight), XStringFormats.Center);

                    y = hY + rowHeight;
                    return p;
                }

                AddPage();

                string infoText = DocString("PaySignInfoText");

                foreach (var row in distinctRows)
                {
                    if (y + rowHeight > pageH - marginBottom)
                        AddPage();

                    var pen = new XPen(XColors.Black, 0.6);
                    double cx = marginLeft;

                    gfx!.DrawRectangle(pen, XBrushes.White, cx, y, colName, rowHeight);
                    gfx.DrawString(row.Name, fontCell, XBrushes.Black,
                        new XRect(cx, y, colName, rowHeight), XStringFormats.Center);
                    cx += colName;

                    gfx.DrawRectangle(pen, XBrushes.White, cx, y, colFirm, rowHeight);
                    gfx.DrawString(row.Firm, fontCell, XBrushes.Black,
                        new XRect(cx, y, colFirm, rowHeight), XStringFormats.Center);
                    cx += colFirm;

                    gfx.DrawRectangle(pen, XBrushes.White, cx, y, colDate, rowHeight);
                    gfx.DrawString(PaymentDate, fontHeader, XBrushes.Black,
                        new XRect(cx, y, colDate, rowHeight), XStringFormats.Center);
                    cx += colDate;

                    gfx.DrawRectangle(pen, XBrushes.White, cx, y, colInfo, rowHeight);
                    gfx.DrawString(infoText, fontInfo, XBrushes.Black,
                        new XRect(cx, y, colInfo, rowHeight), XStringFormats.Center);
                    cx += colInfo;

                    gfx.DrawRectangle(pen, XBrushes.White, cx, y, colSign, rowHeight);
                    cx += colSign;

                    y += rowHeight;
                }

                // Footer: total count on last page
                if (y + 30 > pageH - marginBottom)
                    AddPage();

                y += 4;
                var penFooter = new XPen(XColors.Black, 0.8);
                gfx!.DrawRectangle(penFooter, XBrushes.White, marginLeft, y, colName, rowHeight);
                gfx.DrawString(distinctRows.Count.ToString(), fontHeader, XBrushes.Black,
                    new XRect(marginLeft, y, colName, rowHeight), XStringFormats.Center);

                gfx?.Dispose();
                doc.Save(dlg.FileName);
                _activityLogService.Log("ExportPdf", "Export", "", "",
                    $"Згенеровано виплату на підписи → PDF");
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
