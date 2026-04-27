using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public static class SalaryExcelExportService
    {
        public static byte[] GenerateFirmSalaryExcel(
            string firmName,
            int year,
            int month,
            List<SalaryEntry> entries,
            List<CustomSalaryField> fields,
            List<FirmExpense> expenses,
            string monthDisplay)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.AddWorksheet("Salary");
            var orderedFields = (fields ?? new List<CustomSalaryField>())
                .OrderBy(field => field.Order)
                .ThenBy(field => field.Name)
                .ToList();
            var dataEntries = (entries ?? new List<SalaryEntry>())
                .OrderBy(entry => entry.FullName)
                .ToList();
            var exportExpenses = (expenses ?? new List<FirmExpense>())
                .OrderBy(expense => expense.Name)
                .ToList();

            worksheet.Cell(1, 1).Value = $"Зарплата по фірмі {firmName}";
            worksheet.Range(1, 1, 1, 6 + orderedFields.Count + 3).Merge();
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 18;
            worksheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            worksheet.Cell(2, 1).Value = $"Місяць: {monthDisplay}";
            worksheet.Cell(2, 2).Value = $"Рік: {year}";
            worksheet.Cell(2, 3).Value = $"Місяць №: {month}";
            worksheet.Cell(2, 4).Value = $"Працівників: {dataEntries.Count}";

            var headers = new List<string>
            {
                "Фірма",
                "Працівник",
                "Години",
                "Ставка",
                "Брутто",
                "Аванс"
            };
            headers.AddRange(orderedFields.Select(field => $"{MapFieldOperation(field.Operation)}{field.Name}"));
            headers.Add("До виплати");
            headers.Add("Примітка");
            headers.Add("Статус");

            var headerRow = 4;
            for (var i = 0; i < headers.Count; i++)
            {
                var cell = worksheet.Cell(headerRow, i + 1);
                cell.Value = headers[i];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4472C4");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            }

            var row = headerRow + 1;
            foreach (var entry in dataEntries)
            {
                var column = 1;
                worksheet.Cell(row, column++).Value = entry.FirmName;
                worksheet.Cell(row, column++).Value = entry.FullName;
                worksheet.Cell(row, column).Value = entry.HoursWorked;
                worksheet.Cell(row, column++).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(row, column).Value = entry.HourlyRate;
                worksheet.Cell(row, column++).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(row, column).Value = entry.GrossSalary;
                worksheet.Cell(row, column++).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(row, column).Value = entry.Advance;
                worksheet.Cell(row, column++).Style.NumberFormat.Format = "#,##0.00";

                foreach (var field in orderedFields)
                {
                    worksheet.Cell(row, column).Value = entry.CustomValues.TryGetValue(field.Id, out var value) ? value : 0m;
                    worksheet.Cell(row, column++).Style.NumberFormat.Format = "#,##0.00";
                }

                worksheet.Cell(row, column).Value = entry.NetSalary;
                worksheet.Cell(row, column++).Style.NumberFormat.Format = "#,##0.00";
                worksheet.Cell(row, column++).Value = entry.Note;
                worksheet.Cell(row, column).Value = string.Equals(entry.Status, "paid", System.StringComparison.OrdinalIgnoreCase) ? "Оплачено" : "Не оплачено";

                row++;
            }

            var totalsRow = row;
            worksheet.Cell(totalsRow, 1).Value = "Разом";
            worksheet.Cell(totalsRow, 1).Style.Font.Bold = true;
            worksheet.Cell(totalsRow, 3).Value = dataEntries.Sum(entry => entry.HoursWorked);
            worksheet.Cell(totalsRow, 3).Style.NumberFormat.Format = "#,##0.00";
            worksheet.Cell(totalsRow, 5).Value = dataEntries.Sum(entry => entry.GrossSalary);
            worksheet.Cell(totalsRow, 5).Style.NumberFormat.Format = "#,##0.00";
            worksheet.Cell(totalsRow, 6).Value = dataEntries.Sum(entry => entry.Advance);
            worksheet.Cell(totalsRow, 6).Style.NumberFormat.Format = "#,##0.00";

            var fieldColumn = 7;
            foreach (var field in orderedFields)
            {
                worksheet.Cell(totalsRow, fieldColumn).Value = dataEntries.Sum(entry => entry.CustomValues.TryGetValue(field.Id, out var value) ? value : 0m);
                worksheet.Cell(totalsRow, fieldColumn).Style.NumberFormat.Format = "#,##0.00";
                fieldColumn++;
            }

            worksheet.Cell(totalsRow, fieldColumn).Value = dataEntries.Sum(entry => entry.NetSalary);
            worksheet.Cell(totalsRow, fieldColumn).Style.NumberFormat.Format = "#,##0.00";

            row += 2;
            worksheet.Cell(row, 1).Value = "Витрати фірми";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            row++;
            worksheet.Cell(row, 1).Value = "Назва";
            worksheet.Cell(row, 2).Value = "Сума";
            worksheet.Range(row, 1, row, 2).Style.Font.Bold = true;
            row++;

            foreach (var expense in exportExpenses)
            {
                worksheet.Cell(row, 1).Value = expense.Name;
                worksheet.Cell(row, 2).Value = expense.Amount;
                worksheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0.00";
                row++;
            }

            worksheet.Cell(row, 1).Value = "Разом витрати";
            worksheet.Cell(row, 1).Style.Font.Bold = true;
            worksheet.Cell(row, 2).Value = exportExpenses.Sum(expense => expense.Amount);
            worksheet.Cell(row, 2).Style.NumberFormat.Format = "#,##0.00";

            worksheet.Columns().AdjustToContents();
            worksheet.SheetView.FreezeRows(headerRow);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static string MapFieldOperation(FieldOperation operation)
        {
            return operation switch
            {
                FieldOperation.Add => "+",
                FieldOperation.Subtract => "-",
                FieldOperation.Multiply => "*",
                FieldOperation.Divide => "/",
                _ => string.Empty
            };
        }
    }
}
