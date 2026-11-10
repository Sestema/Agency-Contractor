using System;
using System.Collections.Generic;
using System.IO;
using ClosedXML.Excel;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Services;
using Xunit;

namespace Win11DesktopApp.Tests
{
    public class EmployeeExcelExporterTests : IDisposable
    {
        private static readonly EmployeeExportLabels Labels = new(
            "Sheet",
            new[] { "First", "Last", "Position", "Phone", "Email", "PassNo", "PassExp", "VisaNo", "VisaExp", "InsNo", "InsExp", "Contract", "Start", "Status" },
            "Firm");

        private readonly string _folder = Path.Combine(Path.GetTempPath(), "export-test-" + Guid.NewGuid().ToString("N"));
        private readonly string _file;

        public EmployeeExcelExporterTests()
        {
            Directory.CreateDirectory(_folder);
            _file = Path.Combine(_folder, "out.xlsx");
        }

        public void Dispose() => Directory.Delete(_folder, recursive: true);

        private static EmployeeSummary Summary(string name, string firm) =>
            new() { FullName = name, FirmName = firm, EmployeeFolder = name };

        private static EmployeeData Data(string firstName, string passportExpiry = "") =>
            new() { FirstName = firstName, LastName = "Test", PassportExpiry = passportExpiry };

        [Fact]
        public void SingleFirm_KeepsOriginalColumnsWithoutFirm()
        {
            var result = EmployeeExcelExporter.Export(
                new[] { Summary("a", "F1") },
                _ => Data("Ivan"),
                Labels,
                includeFirmColumn: false,
                _file);

            Assert.Equal(1, result.ExportedCount);
            using var workbook = new XLWorkbook(_file);
            var sheet = workbook.Worksheet("Sheet");
            Assert.Equal("First", sheet.Cell(1, 1).GetString());
            Assert.Equal("Status", sheet.Cell(1, 14).GetString());
            Assert.Equal(string.Empty, sheet.Cell(1, 15).GetString());
            Assert.Equal("Ivan", sheet.Cell(2, 1).GetString());
        }

        [Fact]
        public void AllFirms_PutsFirmFirstAndShiftsExpiryHighlight()
        {
            EmployeeExcelExporter.Export(
                new[] { Summary("a", "RUMPOLD") },
                _ => Data("Ivan", "01.01.2000"),
                Labels,
                includeFirmColumn: true,
                _file);

            using var workbook = new XLWorkbook(_file);
            var sheet = workbook.Worksheet("Sheet");
            Assert.Equal("Firm", sheet.Cell(1, 1).GetString());
            Assert.Equal("First", sheet.Cell(1, 2).GetString());
            Assert.Equal("RUMPOLD", sheet.Cell(2, 1).GetString());
            Assert.Equal("Ivan", sheet.Cell(2, 2).GetString());
            Assert.Equal("01.01.2000", sheet.Cell(2, 8).GetString());
            Assert.True(sheet.Cell(2, 8).Style.Font.Bold);
        }

        [Fact]
        public void UnreadableEmployee_IsReportedAndOthersAreKept()
        {
            var loaded = new Dictionary<string, EmployeeData?> { ["a"] = Data("Ivan"), ["b"] = null };

            var result = EmployeeExcelExporter.Export(
                new[] { Summary("a", "F"), Summary("b", "F") },
                folder => loaded[folder],
                Labels,
                includeFirmColumn: false,
                _file);

            Assert.Equal(1, result.ExportedCount);
            Assert.Equal(new[] { "b" }, result.SkippedNames);
        }

        [Fact]
        public void NobodyReadable_DoesNotCreateFile()
        {
            var result = EmployeeExcelExporter.Export(
                new[] { Summary("a", "F") },
                _ => null,
                Labels,
                includeFirmColumn: false,
                _file);

            Assert.Equal(0, result.ExportedCount);
            Assert.False(File.Exists(_file));
        }
    }
}
