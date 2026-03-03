using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using PdfSharp.Pdf;
using PdfSharp.Drawing;

namespace Win11DesktopApp.ViewModels
{
    public partial class EmployeeDetailsViewModel
    {
        private void OpenGenerateDialog()
        {
            GenerateStatusMessage = string.Empty;
            var templates = App.TemplateService?.GetTemplates(_firmName);
            AvailableTemplates = new ObservableCollection<TemplateEntry>(templates ?? new List<TemplateEntry>());
            IsGenerateDialogOpen = true;
        }

        private void GenerateDocument(TemplateEntry? template)
        {
            if (template == null) return;

            try
            {
                IsGenerating = true;
                GenerateStatusMessage = Res("MsgGenerating");

                var templateFullPath = App.TemplateService?.GetTemplateFullPath(_firmName, template.FilePath) ?? string.Empty;
                var templateFolder = Path.GetDirectoryName(templateFullPath) ?? string.Empty;
                var ext = Path.GetExtension(templateFullPath).ToLower();
                var format = template.Format?.ToUpper() ?? ext.TrimStart('.').ToUpper();

                var rtfPath = Path.Combine(templateFolder, "content.rtf");
                bool hasTemplateFile = File.Exists(templateFullPath);
                bool hasRtfContent = File.Exists(rtfPath);

                if (!hasTemplateFile && !hasRtfContent)
                {
                    GenerateStatusMessage = Res("MsgTemplateNotFound");
                    IsGenerating = false;
                    return;
                }

                if (App.DocumentGenerationService == null)
                {
                    GenerateStatusMessage = "[Error] Document generation service unavailable";
                    IsGenerating = false;
                    return;
                }

                if (format == "PDF" && hasTemplateFile)
                {
                    var tagValues = App.TagCatalogService?.GetTagValueMapForEmployee(_firmName, Data);
                    var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.pdf";
                    var sanitized = SanitizeFileName(outputFileName);
                    var outputPath = Path.Combine(_employeeFolder, sanitized);

                    App.DocumentGenerationService?.GeneratePdf(templateFullPath, outputPath, tagValues);
                    GenerateStatusMessage = string.Format(Res("MsgDocGenerated"), sanitized);
                    App.ActivityLogService?.Log("DocGenerated", "Document", _firmName, FullName,
                        $"Згенеровано документ «{template.Name}» для {FullName}",
                        employeeFolder: _employeeFolder);
                    DocumentGenerationService.OpenFile(outputPath);
                    IsGenerating = false;
                    return;
                }

                if (format == "DOCX" || hasRtfContent)
                {
                    var tagValues = App.TagCatalogService?.GetTagValueMapForEmployee(_firmName, Data);

                    if (hasRtfContent)
                    {
                        var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.docx";
                        var sanitized = SanitizeFileName(outputFileName);
                        var outputPath = Path.Combine(_employeeFolder, sanitized);

                        App.DocumentGenerationService?.GenerateDocxFromRtf(rtfPath, outputPath, tagValues);
                        GenerateStatusMessage = string.Format(Res("MsgDocGenerated"), sanitized);
                        DocumentGenerationService.OpenFile(outputPath);
                    }
                    else if (hasTemplateFile)
                    {
                        var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.docx";
                        var sanitized = SanitizeFileName(outputFileName);
                        var outputPath = Path.Combine(_employeeFolder, sanitized);

                        App.DocumentGenerationService?.GenerateDocx(templateFullPath, outputPath, tagValues);
                        GenerateStatusMessage = string.Format(Res("MsgDocGenerated"), sanitized);
                        DocumentGenerationService.OpenFile(outputPath);
                    }
                }
                else if (format == "XLSX" && hasTemplateFile)
                {
                    var tagValues = App.TagCatalogService?.GetTagValueMapForEmployee(_firmName, Data);
                    var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.xlsx";
                    var sanitized = SanitizeFileName(outputFileName);
                    var outputPath = Path.Combine(_employeeFolder, sanitized);

                    App.DocumentGenerationService?.GenerateXlsx(templateFullPath, outputPath, tagValues);
                    GenerateStatusMessage = string.Format(Res("MsgDocGenerated"), sanitized);
                    DocumentGenerationService.OpenFile(outputPath);
                }
                else
                {
                    GenerateStatusMessage = string.Format(Res("MsgUnsupportedFmt"), format);
                }
            }
            catch (Exception ex)
            {
                GenerateStatusMessage = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        private void ExportProfilePdf()
        {
            var dlg = new SaveFileDialog
            {
                Filter = "PDF|*.pdf",
                FileName = $"{Data.FirstName} {Data.LastName} - Profile.pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var doc = new PdfDocument();
                doc.Info.Title = $"{Data.FirstName} {Data.LastName}";
                var page = doc.AddPage();
                page.Size = PdfSharp.PageSize.A4;
                var gfx = XGraphics.FromPdfPage(page);

                double pageW = page.Width.Point;
                double pageH = page.Height.Point;

                var accent = XColor.FromArgb(30, 126, 126);
                var accentBrush = new XSolidBrush(accent);
                var accentLight = XColor.FromArgb(55, 160, 160);
                var sideTextLight = new XSolidBrush(XColor.FromArgb(200, 235, 235));
                var sideTextMuted = new XSolidBrush(XColor.FromArgb(150, 210, 210));
                var darkText = new XSolidBrush(XColor.FromArgb(51, 51, 51));
                var grayLabel = new XSolidBrush(XColor.FromArgb(150, 150, 150));

                double sideW = 200;
                double sidePad = 24;

                var fNameBig = new XFont("Segoe UI", 20, XFontStyleEx.Bold);
                var fSub = new XFont("Segoe UI", 9, XFontStyleEx.Regular);
                var fSideSection = new XFont("Segoe UI", 10, XFontStyleEx.Bold);
                var fSideLabel = new XFont("Segoe UI", 7.5, XFontStyleEx.Regular);
                var fSideValue = new XFont("Segoe UI", 9, XFontStyleEx.Regular);
                var fSection = new XFont("Segoe UI", 11, XFontStyleEx.Bold);
                var fLabel = new XFont("Segoe UI", 7.5, XFontStyleEx.Regular);
                var fValue = new XFont("Segoe UI", 9, XFontStyleEx.Bold);

                List<string> WrapText(string text, XFont font, double maxWidth)
                {
                    var lines = new List<string>();
                    var words = text.Split(' ');
                    string currentLine = "";
                    foreach (var word in words)
                    {
                        string test = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                        if (gfx.MeasureString(test, font).Width > maxWidth && !string.IsNullOrEmpty(currentLine))
                        {
                            lines.Add(currentLine);
                            currentLine = word;
                        }
                        else
                        {
                            currentLine = test;
                        }
                    }
                    if (!string.IsNullOrEmpty(currentLine))
                        lines.Add(currentLine);
                    return lines.Count > 0 ? lines : new List<string> { text };
                }

                double DrawWrapped(string text, XFont font, XBrush brush, double x, double y, double maxW, double lineH)
                {
                    var lines = WrapText(text, font, maxW);
                    foreach (var line in lines)
                    {
                        gfx.DrawString(line, font, brush, new XRect(x, y, maxW, lineH), XStringFormats.TopLeft);
                        y += lineH;
                    }
                    return lines.Count * lineH;
                }

                gfx.DrawRectangle(accentBrush, 0, 0, sideW, pageH);

                double sy = 40;

                gfx.DrawString(Data.FirstName ?? "", fNameBig, XBrushes.White,
                    new XRect(sidePad, sy, sideW - sidePad * 2, 26), XStringFormats.TopLeft);
                sy += 26;
                gfx.DrawString(Data.LastName ?? "", fNameBig, XBrushes.White,
                    new XRect(sidePad, sy, sideW - sidePad * 2, 26), XStringFormats.TopLeft);
                sy += 32;

                if (!string.IsNullOrEmpty(Data.PositionTag))
                {
                    double h = DrawWrapped(Data.PositionTag, fSub, sideTextLight, sidePad, sy, sideW - sidePad * 2, 13);
                    sy += h + 2;
                }
                if (!string.IsNullOrEmpty(Data.ContractType))
                {
                    double h = DrawWrapped(Data.ContractType, fSub, sideTextMuted, sidePad, sy, sideW - sidePad * 2, 13);
                    sy += h + 2;
                }
                sy += 14;

                double photoR = 54;
                double photoDia = photoR * 2;
                double pcx = sideW / 2;
                double pcy = sy + photoR;
                double photoX = pcx - photoR;
                double photoY = pcy - photoR;

                gfx.DrawEllipse(new XPen(XColors.White, 4),
                    photoX - 6, photoY - 6, photoDia + 12, photoDia + 12);

                if (HasPhoto && File.Exists(PhotoFilePath))
                {
                    try
                    {
                        using var img = XImage.FromFile(PhotoFilePath);
                        gfx.DrawImage(img, photoX, photoY, photoDia, photoDia);

                        var mask = new XGraphicsPath();
                        mask.FillMode = XFillMode.Alternate;
                        mask.AddRectangle(photoX - 1, photoY - 1, photoDia + 2, photoDia + 2);
                        mask.AddEllipse(photoX, photoY, photoDia, photoDia);
                        gfx.DrawPath(accentBrush, mask);
                    }
                    catch (Exception ex) { LoggingService.LogWarning("EmployeeDetailsViewModel.ExportPdf", $"Photo render failed: {ex.Message}"); }
                }
                else
                {
                    gfx.DrawEllipse(new XSolidBrush(XColor.FromArgb(45, 148, 148)),
                        photoX, photoY, photoDia, photoDia);
                }

                gfx.DrawEllipse(new XPen(XColors.White, 2.5), photoX, photoY, photoDia, photoDia);
                sy = pcy + photoR + 20;

                var sepPen = new XPen(accentLight, 0.5);
                gfx.DrawLine(sepPen, sidePad, sy, sideW - sidePad, sy);
                sy += 16;

                gfx.DrawString(DocRes("PdfSecContacts"), fSideSection, XBrushes.White, new XPoint(sidePad, sy));
                sy += 20;

                void SideContact(string label, string? val, ref double y)
                {
                    if (string.IsNullOrWhiteSpace(val)) return;
                    gfx.DrawEllipse(new XSolidBrush(accentLight), sidePad, y - 5, 7, 7);
                    gfx.DrawString(label, fSideLabel, sideTextMuted, new XPoint(sidePad + 14, y - 3));
                    y += 11;
                    gfx.DrawString(val, fSideValue, XBrushes.White, new XPoint(sidePad + 14, y));
                    y += 18;
                }

                SideContact(DocRes("DetFieldPhone"), Data.Phone, ref sy);
                SideContact("Email", Data.Email, ref sy);
                SideContact(DocRes("DetFieldStartDate"), Data.StartDate, ref sy);

                sy += 8;
                gfx.DrawLine(sepPen, sidePad, sy, sideW - sidePad, sy);
                sy += 16;

                if (!string.IsNullOrEmpty(Data.InsuranceNumber) || !string.IsNullOrEmpty(Data.InsuranceExpiry))
                {
                    gfx.DrawString(DocRes("PdfSecInsurance"), fSideSection, XBrushes.White, new XPoint(sidePad, sy));
                    sy += 20;

                    void SideField(string lbl, string? val, ref double y)
                    {
                        if (string.IsNullOrWhiteSpace(val)) return;
                        gfx.DrawString(lbl, fSideLabel, sideTextMuted, new XPoint(sidePad, y));
                        y += 11;
                        gfx.DrawString(val, fSideValue, XBrushes.White, new XPoint(sidePad, y));
                        y += 16;
                    }

                    SideField(DocRes("DetFieldInsCompany"), Data.InsuranceCompanyShort, ref sy);
                    SideField(DocRes("PdfFieldNumber"), Data.InsuranceNumber, ref sy);
                    SideField(DocRes("PdfFieldValidToF"), Data.InsuranceExpiry, ref sy);
                }

                double cx = sideW + 28;
                double cw = pageW - sideW - 56;
                double subW = cw / 2 - 10;
                double lx = cx;
                double rxx = cx + subW + 20;

                void ContentSection(string title, double sx, double lineW, ref double y)
                {
                    gfx.DrawString(title.ToUpper(), fSection, accentBrush, new XPoint(sx, y));
                    y += 14;
                    gfx.DrawLine(new XPen(XColor.FromArgb(220, 220, 220), 0.5), sx, y, sx + lineW, y);
                    y += 10;
                }

                void ContentField(string label, string? value, double sx, ref double y)
                {
                    if (string.IsNullOrWhiteSpace(value)) return;
                    gfx.DrawString(label, fLabel, grayLabel, new XPoint(sx, y));
                    y += 11;
                    double h = DrawWrapped(value, fValue, darkText, sx, y, subW, 13);
                    y += h + 2;
                }

                double yL = 40, yR = 40;

                ContentSection(DocRes("DetSecPassport"), lx, subW, ref yL);
                ContentField(DocRes("PdfFieldNumber"), Data.PassportNumber, lx, ref yL);
                ContentField(DocRes("PdfFieldValidTo"), Data.PassportExpiry, lx, ref yL);
                ContentField(DocRes("DetFieldBirthCity"), Data.PassportCity, lx, ref yL);
                ContentField(DocRes("DetFieldBirthCountry"), Data.PassportCountry, lx, ref yL);
                yL += 10;

                if (!string.IsNullOrEmpty(Data.VisaNumber) || !string.IsNullOrEmpty(Data.VisaExpiry))
                {
                    ContentSection(DocRes("DetSecVisa"), lx, subW, ref yL);
                    ContentField(DocRes("PdfFieldNumber"), Data.VisaNumber, lx, ref yL);
                    ContentField(DocRes("PdfFieldType"), Data.VisaType, lx, ref yL);
                    ContentField(DocRes("PdfFieldValidToF"), Data.VisaExpiry, lx, ref yL);
                    if (!string.IsNullOrEmpty(Data.WorkPermitName))
                        ContentField(DocRes("PdfFieldPermit"), Data.WorkPermitName, lx, ref yL);
                    yL += 10;
                }

                if (Data.EmployeeType == "work_permit")
                {
                    ContentSection(DocRes("DetDocWorkPermit"), lx, subW, ref yL);
                    ContentField(DocRes("PdfFieldNumber"), Data.WorkPermitNumber, lx, ref yL);
                    ContentField(DocRes("PdfFieldType"), Data.WorkPermitType, lx, ref yL);
                    ContentField(DocRes("PdfFieldIssued"), Data.WorkPermitIssueDate, lx, ref yL);
                    ContentField(DocRes("PdfFieldValidTo"), Data.WorkPermitExpiry, lx, ref yL);
                    ContentField(DocRes("PdfFieldAuthority"), Data.WorkPermitAuthority, lx, ref yL);
                    yL += 10;
                }

                ContentSection(DocRes("DetSecAddrLocal"), rxx, subW, ref yR);
                ContentField(DocRes("DetFieldStreet"), Data.AddressLocal.Street, rxx, ref yR);
                ContentField(DocRes("PdfFieldNumber"), Data.AddressLocal.Number, rxx, ref yR);
                ContentField(DocRes("DetFieldCity"), Data.AddressLocal.City, rxx, ref yR);
                ContentField(DocRes("DetFieldZip"), Data.AddressLocal.Zip, rxx, ref yR);
                yR += 10;

                ContentSection(DocRes("DetSecAddrAbroad"), rxx, subW, ref yR);
                ContentField(DocRes("DetFieldStreet"), Data.AddressAbroad.Street, rxx, ref yR);
                ContentField(DocRes("PdfFieldNumber"), Data.AddressAbroad.Number, rxx, ref yR);
                ContentField(DocRes("DetFieldCity"), Data.AddressAbroad.City, rxx, ref yR);
                ContentField(DocRes("DetFieldZip"), Data.AddressAbroad.Zip, rxx, ref yR);
                yR += 10;

                ContentSection(DocRes("DetSecWork"), rxx, subW, ref yR);
                ContentField(DocRes("DetFieldPosition"), Data.PositionTag, rxx, ref yR);
                ContentField(DocRes("PdfFieldPosNumber"), Data.PositionNumber, rxx, ref yR);
                ContentField(DocRes("DetFieldSalary"), Data.MonthlySalaryBrutto > 0 ? Data.MonthlySalaryBrutto.ToString() : "", rxx, ref yR);
                ContentField(DocRes("DetFieldHourly"), Data.HourlySalary > 0 ? Data.HourlySalary.ToString() : "", rxx, ref yR);
                ContentField(DocRes("DetFieldContractType"), Data.ContractType, rxx, ref yR);
                ContentField(DocRes("PdfFieldDepartment"), Data.Department, rxx, ref yR);
                ContentField(DocRes("DetFieldStartDate"), Data.StartDate, rxx, ref yR);
                ContentField(DocRes("DetFieldSignDate"), Data.ContractSignDate, rxx, ref yR);

                gfx.Dispose();
                doc.Save(dlg.FileName);
                App.ActivityLogService?.Log("ExportPdf", "Export", _firmName, FullName,
                    $"Експортовано анкету {FullName} → PDF", employeeFolder: _employeeFolder);
                ToastService.Instance.Success(Res("MsgPdfSaved"));
            }
            catch (Exception ex)
            {
                ErrorHandler.Report("ExportProfilePdf", ex, ErrorSeverity.Error);
            }
        }
    }
}
