using System;
using System.Collections.Generic;
using ClosedXML.Excel;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    internal sealed record EmployeeExportLabels(string SheetName, IReadOnlyList<string> Columns, string FirmColumn);

    internal sealed record EmployeeExportResult(int ExportedCount, IReadOnlyList<string> SkippedNames);

    /// <summary>Builds the employee Excel file. Touches no UI, so it can run on a background thread.</summary>
    internal static class EmployeeExcelExporter
    {
        // 1-based positions of the passport, visa and insurance expiry columns, without the firm column.
        private static readonly int[] ExpiryColumns = { 7, 9, 11 };

        public static EmployeeExportResult Export(
            IReadOnlyList<EmployeeSummary> employees,
            Func<string, EmployeeData?> loadData,
            EmployeeExportLabels labels,
            bool includeFirmColumn,
            string filePath)
        {
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(labels.SheetName);
            var firstDataColumn = includeFirmColumn ? 2 : 1;

            if (includeFirmColumn)
                sheet.Cell(1, 1).Value = labels.FirmColumn;

            for (var i = 0; i < labels.Columns.Count; i++)
                sheet.Cell(1, firstDataColumn + i).Value = labels.Columns[i];

            var headerRange = sheet.Range(1, 1, 1, firstDataColumn + labels.Columns.Count - 1);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.CornflowerBlue;
            headerRange.Style.Font.FontColor = XLColor.White;

            var skipped = new List<string>();
            var row = 2;
            foreach (var employee in employees)
            {
                var data = loadData(employee.EmployeeFolder);
                if (data == null)
                {
                    skipped.Add(employee.FullName);
                    continue;
                }

                if (includeFirmColumn)
                    sheet.Cell(row, 1).Value = employee.FirmName ?? string.Empty;

                var values = new[]
                {
                    data.FirstName, data.LastName, data.PositionTag, data.Phone, data.Email,
                    data.PassportNumber, data.PassportExpiry, data.VisaNumber, data.VisaExpiry,
                    data.InsuranceNumber, data.InsuranceExpiry, data.ContractType,
                    data.StartDate, data.Status
                };

                for (var i = 0; i < values.Length; i++)
                    sheet.Cell(row, firstDataColumn + i).Value = values[i] ?? string.Empty;

                foreach (var column in ExpiryColumns)
                    HighlightIfExpired(sheet.Cell(row, firstDataColumn + column - 1));

                row++;
            }

            var exportedCount = row - 2;
            if (exportedCount > 0)
            {
                sheet.Columns().AdjustToContents();
                workbook.SaveAs(filePath);
            }

            return new EmployeeExportResult(exportedCount, skipped);
        }

        private static void HighlightIfExpired(IXLCell cell)
        {
            var severity = DateParsingHelper.GetSeverity(cell.GetString());
            if (severity == "Expired" || severity == "Critical")
            {
                cell.Style.Font.FontColor = XLColor.Red;
                cell.Style.Font.Bold = true;
            }
            else if (severity == "Warning")
            {
                cell.Style.Font.FontColor = XLColor.OrangeRed;
            }
        }
    }
}
