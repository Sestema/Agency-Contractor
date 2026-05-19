using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using Win11DesktopApp.Converters;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Win11DesktopApp.ViewModels
{
    public partial class EmployeeDetailsViewModel
    {
        private void OpenGenerateDialog()
        {
            if (!PolicyService.EnsureWriteAllowed("Генерація документа"))
                return;

            GenerateStatusMessage = string.Empty;
            var templates = _templateService.GetTemplates(_firmName);
            AvailableTemplates = new ObservableCollection<TemplateEntry>(templates);
            IsGenerateDialogOpen = true;
        }

        private async Task GenerateDocumentAsync(TemplateEntry? template)
        {
            if (!PolicyService.EnsureWriteAllowed("Генерація документа"))
                return;
            if (template == null) return;
            if (!EnsureEmployeeFolderAvailable("EmployeeDetailsViewModel.GenerateDocument", notifyUser: true))
            {
                GenerateStatusMessage = Res("MsgEmployeeFolderMissing");
                return;
            }
            if (!EnsureDocumentGenerationEmployeeMatch())
                return;

            try
            {
                IsGenerating = true;
                GenerateStatusMessage = Res("MsgGenerating");
                var wasGenerated = false;

                var templateFullPath = _templateService.GetTemplateFullPath(_firmName, template.FilePath) ?? string.Empty;
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

                if (format == "PDF" && hasTemplateFile)
                {
                    var tagValues = _tagCatalogService.GetTagValueMapForEmployee(_firmName, Data) ?? new Dictionary<string, string>();
                    var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.pdf";
                    var sanitized = SanitizeFileName(outputFileName);
                    var outputPath = Path.Combine(_employeeFolder, sanitized);

                    await Task.Run(() => _documentGenerationService.GeneratePdf(templateFullPath, outputPath, tagValues));
                    GenerateStatusMessage = string.Format(Res("MsgDocGenerated"), sanitized);
                    wasGenerated = true;
                    _activityLogService.Log("DocGenerated", "Document", _firmName, FullName,
                        $"Згенеровано документ «{template.Name}» для {FullName}",
                        employeeFolder: _employeeFolder);
                    _appStatisticsService.RecordDocumentGenerated();
                    DocumentGenerationService.OpenFile(outputPath);
                    IsGenerating = false;
                    return;
                }

                if (format == "DOCX" || hasRtfContent)
                {
                    var tagValues = _tagCatalogService.GetTagValueMapForEmployee(_firmName, Data) ?? new Dictionary<string, string>();

                    if (hasRtfContent)
                    {
                        var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.docx";
                        var sanitized = SanitizeFileName(outputFileName);
                        var outputPath = Path.Combine(_employeeFolder, sanitized);

                        await Task.Run(() => _documentGenerationService.GenerateDocxFromRtf(rtfPath, outputPath, tagValues));
                        GenerateStatusMessage = string.Format(Res("MsgDocGenerated"), sanitized);
                        wasGenerated = true;
                        DocumentGenerationService.OpenFile(outputPath);
                    }
                    else if (hasTemplateFile)
                    {
                        var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.docx";
                        var sanitized = SanitizeFileName(outputFileName);
                        var outputPath = Path.Combine(_employeeFolder, sanitized);

                        await Task.Run(() => _documentGenerationService.GenerateDocx(templateFullPath, outputPath, tagValues));
                        GenerateStatusMessage = string.Format(Res("MsgDocGenerated"), sanitized);
                        wasGenerated = true;
                        DocumentGenerationService.OpenFile(outputPath);
                    }
                }
                else if (format == "XLSX" && hasTemplateFile)
                {
                    var tagValues = _tagCatalogService.GetTagValueMapForEmployee(_firmName, Data) ?? new Dictionary<string, string>();
                    var outputFileName = $"{Data.FirstName}_{Data.LastName} - {template.Name}.xlsx";
                    var sanitized = SanitizeFileName(outputFileName);
                    var outputPath = Path.Combine(_employeeFolder, sanitized);

                    await Task.Run(() => _documentGenerationService.GenerateXlsx(templateFullPath, outputPath, tagValues));
                    GenerateStatusMessage = string.Format(Res("MsgDocGenerated"), sanitized);
                    wasGenerated = true;
                    DocumentGenerationService.OpenFile(outputPath);
                }
                else
                {
                    GenerateStatusMessage = string.Format(Res("MsgUnsupportedFmt"), format);
                }

                if (wasGenerated)
                    _appStatisticsService.RecordDocumentGenerated();
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

        private bool EnsureDocumentGenerationEmployeeMatch()
        {
            var diskData = _employeeService.LoadEmployeeData(_employeeFolder);
            if (diskData == null)
            {
                GenerateStatusMessage = Res("MsgEmployeeProfileMissing");
                NotifyProfileUnavailable(GenerateStatusMessage);
                return false;
            }

            var diskId = diskData.UniqueId?.Trim() ?? string.Empty;
            var expectedId = string.IsNullOrWhiteSpace(_expectedEmployeeId)
                ? Data.UniqueId?.Trim() ?? string.Empty
                : _expectedEmployeeId.Trim();

            if (string.IsNullOrWhiteSpace(expectedId) || string.Equals(diskId, expectedId, StringComparison.OrdinalIgnoreCase))
            {
                RefreshEmployeeDataForGeneration(diskData);
                return true;
            }

            var expectedName = $"{Data.FirstName} {Data.LastName}".Trim();
            var actualName = $"{diskData.FirstName} {diskData.LastName}".Trim();
            var message = string.Format(
                Res("MsgEmployeeProfileMismatch"),
                string.IsNullOrWhiteSpace(expectedName) ? expectedId : expectedName,
                string.IsNullOrWhiteSpace(actualName) ? diskId : actualName);

            LoggingService.LogWarning("EmployeeDetailsViewModel.GenerateDocument",
                $"Blocked document generation because selected employee id '{expectedId}' does not match employee.json id '{diskId}' in folder '{_employeeFolder}'.");
            GenerateStatusMessage = message;
            NotifyProfileUnavailable(message);
            return false;
        }

        private void RefreshEmployeeDataForGeneration(EmployeeData diskData)
        {
            if (diskData == null || IsEditMode)
                return;

            Data = diskData;
            Data.Status = StatusHelper.Normalize(Data.Status);
            NormalizeInsuranceCompanyFields();
            NormalizeEducationFields();
            NormalizeDocumentProfileFields();
            NotifyBankAccountStateChanged();
            TryAutofillBankName(Data.BankAccountNumber);
            RefreshExpiryWarnings();
            OnPropertyChanged(nameof(Data));
            OnPropertyChanged(nameof(FullName));
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        }

        private void ExportProfilePdf()
        {
            if (!PolicyService.EnsureExportsAllowed("Експорт профілю в PDF"))
                return;

            var dlg = new SaveFileDialog
            {
                Filter = "PDF|*.pdf",
                FileName = $"{Data.FirstName} {Data.LastName} - Profile.pdf"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var company = _companyService.Companies.FirstOrDefault(c => string.Equals(c.Name, _firmName, StringComparison.OrdinalIgnoreCase));
                var agencyName = company?.Agency?.Name ?? string.Empty;
                var customDocuments = (Data.CustomDocuments ?? new List<CustomSignedDocument>())
                    .Where(d => !d.IsHidden && (!string.IsNullOrWhiteSpace(d.Name) || !string.IsNullOrWhiteSpace(d.SignDate) || !string.IsNullOrWhiteSpace(d.ExpiryDate)))
                    .ToList();

                string ValueOrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
                string Money(decimal value) => value > 0 ? value.ToString("0.##") : string.Empty;
                string Initials()
                {
                    var first = string.IsNullOrWhiteSpace(Data.FirstName) ? string.Empty : Data.FirstName.Trim()[0].ToString();
                    var last = string.IsNullOrWhiteSpace(Data.LastName) ? string.Empty : Data.LastName.Trim()[0].ToString();
                    var initials = (first + last).ToUpperInvariant();
                    return string.IsNullOrWhiteSpace(initials) ? "AC" : initials;
                }

                List<(string Label, string Value)> Fields(params (string Label, string? Value)[] fields)
                    => fields
                        .Where(field => !string.IsNullOrWhiteSpace(field.Value))
                        .Select(field => (field.Label, field.Value!.Trim()))
                        .ToList();

                void AddField(ColumnDescriptor column, string label, string value)
                {
                    column.Item().PaddingTop(4).Row(field =>
                    {
                        field.ConstantItem(78).Text($"{label}:").FontSize(6.7f).FontColor("#7F8F8F");
                        field.RelativeItem().Text(value).FontSize(7.8f).SemiBold().FontColor("#243333");
                    });
                }

                void AddSection(ColumnDescriptor column, string title, IReadOnlyList<(string Label, string Value)> fields)
                {
                    if (fields.Count == 0)
                        return;

                    column.Item().PaddingBottom(10).Background("#F7FBFB").BorderLeft(3).BorderColor("#1E7E7E").PaddingLeft(9).PaddingRight(8).PaddingVertical(8).Column(section =>
                    {
                        section.Item().Text(title.ToUpperInvariant()).FontSize(8.6f).Bold().FontColor("#1E7E7E");

                        foreach (var field in fields)
                            AddField(section, field.Label, field.Value);
                    });
                }

                void AddDocumentRows(ColumnDescriptor column, string title, IReadOnlyList<CustomSignedDocument> documents)
                {
                    if (documents.Count == 0)
                        return;

                    column.Item().PaddingBottom(10).Background("#F7FBFB").BorderLeft(3).BorderColor("#1E7E7E").PaddingLeft(9).PaddingRight(8).PaddingVertical(8).Column(section =>
                    {
                        section.Item().Text(title.ToUpperInvariant()).FontSize(8.6f).Bold().FontColor("#1E7E7E");

                        foreach (var document in documents)
                        {
                            var parts = new[]
                            {
                                string.IsNullOrWhiteSpace(document.SignDate) ? null : $"{DocRes("DetFieldSignDate")}: {document.SignDate}",
                                string.IsNullOrWhiteSpace(document.ExpiryDate) ? null : $"{DocRes("PdfFieldValidTo")}: {document.ExpiryDate}",
                                string.IsNullOrWhiteSpace(document.FileName) ? null : document.FileName
                            }.Where(part => !string.IsNullOrWhiteSpace(part));

                            section.Item().PaddingTop(6).Column(item =>
                            {
                                item.Item().Text(ValueOrDash(document.Name)).FontSize(7.8f).SemiBold().FontColor("#273333");
                                item.Item().Text(string.Join(" • ", parts)).FontSize(6.4f).FontColor("#7F8F8F");
                            });
                        }
                    });
                }

                void PhotoBlock(IContainer container)
                {
                    container.Width(74).Height(74).Border(1).BorderColor("#D7E5E5").Background("#F3F8F8").Padding(3).Element(photo =>
                    {
                        if (HasPhoto && File.Exists(PhotoFilePath))
                        {
                            photo.Image(PhotoFilePath).FitArea();
                        }
                        else
                        {
                            photo.AlignCenter().AlignMiddle().Text(Initials()).FontSize(20).Bold().FontColor("#1E7E7E");
                        }
                    });
                }

                var document = QuestPDF.Fluent.Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(24);
                        page.DefaultTextStyle(style => style.FontFamily("Segoe UI").FontSize(7.5f).FontColor("#273333"));

                        page.Header().Background("#F2FAF9").Padding(9).Row(row =>
                        {
                            row.ConstantItem(84).Element(PhotoBlock);
                            row.RelativeItem().PaddingLeft(12).Column(header =>
                            {
                                header.Item().Text(FullName).FontSize(17).Bold().FontColor("#1E7E7E");
                                header.Item().PaddingTop(2).Text(ValueOrDash(Data.PositionTag)).FontSize(8.5f).FontColor("#516161");
                                header.Item().PaddingTop(2).Text(ValueOrDash(_firmName)).FontSize(8).SemiBold().FontColor("#273333");

                                header.Item().PaddingTop(5).Row(tags =>
                                {
                                    tags.AutoItem().Background("#E4F3F2").PaddingHorizontal(8).PaddingVertical(4)
                                        .Text(ValueOrDash(Data.Status)).FontSize(6.5f).Bold().FontColor("#1E7E7E");
                                    if (!string.IsNullOrWhiteSpace(Data.ContractType))
                                    {
                                        tags.AutoItem().PaddingLeft(6).Background("#F1F5F5").PaddingHorizontal(8).PaddingVertical(4)
                                            .Text(Data.ContractType).FontSize(6.5f).Bold().FontColor("#516161");
                                    }
                                });
                            });
                        });

                        page.Content().PaddingTop(16).Column(content =>
                        {
                            content.Item().Row(columns =>
                            {
                                columns.RelativeItem().Column(left =>
                                {
                                    AddSection(left, DocRes("DetSecPassport"), Fields(
                                        (DocRes("PdfFieldNumber"), Data.PassportNumber),
                                        (DocRes("PdfFieldAuthority"), Data.PassportAuthority),
                                        (DocRes("PdfFieldValidTo"), Data.PassportExpiry),
                                        (DocRes("DetFieldBirthCity"), Data.PassportCity),
                                        (DocRes("DetFieldBirthCountry"), Data.PassportCountry),
                                        (DocRes("DetFieldCitizenship"), Data.Citizenship),
                                        (DocRes("DetFieldIssuingCountry"), Data.IssuingCountry)));

                                    AddSection(left, DocRes("DetSecVisa"), Fields(
                                        (DocRes("PdfFieldNumber"), Data.VisaNumber),
                                        (DocRes("PdfFieldAuthority"), Data.VisaAuthority),
                                        (DocRes("PdfFieldType"), Data.VisaType),
                                        (DocRes("PdfFieldIssued"), Data.VisaStartDate),
                                        (DocRes("PdfFieldValidToF"), Data.VisaExpiry),
                                        (DocRes("PdfFieldPermit"), Data.WorkPermitName)));

                                    AddSection(left, DocRes("DetDocWorkPermit"), Fields(
                                        (DocRes("PdfFieldNumber"), Data.WorkPermitNumber),
                                        (DocRes("PdfFieldType"), Data.WorkPermitType),
                                        (DocRes("PdfFieldIssued"), Data.WorkPermitIssueDate),
                                        (DocRes("PdfFieldValidTo"), Data.WorkPermitExpiry),
                                        (DocRes("PdfFieldAuthority"), Data.WorkPermitAuthority)));

                                    AddSection(left, DocRes("PdfSecInsurance"), Fields(
                                        (DocRes("DetFieldInsCompany"), Data.InsuranceCompanyShort),
                                        (DocRes("DetFieldInsCompanyFull"), Data.InsuranceCompanyFull),
                                        (DocRes("PdfFieldNumber"), Data.InsuranceNumber),
                                        (DocRes("PdfFieldValidToF"), Data.InsuranceExpiry)));
                                });

                                columns.ConstantItem(14);

                                columns.RelativeItem().Column(right =>
                                {
                                    AddSection(right, DocRes("PdfSecContacts"), Fields(
                                        (DocRes("DetFieldPhone"), Data.Phone),
                                        ("Email", Data.Email),
                                        (DocRes("DetFieldStartDate"), Data.StartDate),
                                        (DocRes("DetFieldBirthDate"), Data.BirthDate),
                                        (DocRes("DetFieldRodneCislo"), Data.HasRodneCisloData ? Data.RodneCislo : string.Empty),
                                        (DocRes("DetFieldBankAccount"), Data.BankAccountNumber),
                                        (DocRes("DetFieldBankName"), Data.BankName)));

                                    AddSection(right, DocRes("DetSecAddrLocal"), Fields(
                                        (DocRes("DetFieldStreet"), Data.AddressLocal.Street),
                                        (DocRes("PdfFieldNumber"), Data.AddressLocal.Number),
                                        (DocRes("DetFieldCity"), Data.AddressLocal.City),
                                        (DocRes("DetFieldZip"), Data.AddressLocal.Zip)));

                                    AddSection(right, DocRes("DetSecAddrAbroad"), Fields(
                                        (DocRes("DetFieldStreet"), Data.AddressAbroad.Street),
                                        (DocRes("PdfFieldNumber"), Data.AddressAbroad.Number),
                                        (DocRes("DetFieldCity"), Data.AddressAbroad.City),
                                        (DocRes("DetFieldZip"), Data.AddressAbroad.Zip)));

                                    AddSection(right, DocRes("DetSecWork"), Fields(
                                        ("Firma", _firmName),
                                        ("Agency", agencyName),
                                        (DocRes("DetFieldPosition"), Data.PositionTag),
                                        (DocRes("PdfFieldPosNumber"), Data.PositionNumber),
                                        (DocRes("DetFieldSalary"), Money(Data.MonthlySalaryBrutto)),
                                        (DocRes("DetFieldHourly"), Money(Data.HourlySalary)),
                                        (DocRes("DetFieldContractType"), Data.ContractType),
                                        (DocRes("PdfFieldDepartment"), Data.Department),
                                        (DocRes("DetFieldStartDate"), Data.StartDate),
                                        (DocRes("DetFieldEndDate"), Data.EndDate),
                                        (DocRes("DetFieldSignDate"), Data.ContractSignDate),
                                        ("Work address", Data.WorkAddressTag)));

                                    AddDocumentRows(right, DocRes("DetSecCustomDocuments"), customDocuments);
                                });
                            });
                        });

                        page.Footer().AlignRight().Text(text =>
                        {
                            text.Span("Agency Contractor • ").FontSize(7).FontColor("#7F8F8F");
                            text.Span(DateTime.Now.ToString("dd.MM.yyyy HH:mm")).FontSize(7).FontColor("#7F8F8F");
                        });
                    });
                });

                document.GeneratePdf(dlg.FileName);
                _activityLogService.Log("ExportPdf", "Export", _firmName, FullName,
                    $"Експортовано анкету {FullName} → PDF",
                    details: $"Фірма: {_firmName}; Документ: анкета працівника; Файл: {Path.GetFileName(dlg.FileName)}",
                    employeeFolder: _employeeFolder);
                ToastService.Instance.Success(Res("MsgPdfSaved"));
            }
            catch (Exception ex)
            {
                ErrorHandler.Report("ExportProfilePdf", ex, ErrorSeverity.Error);
            }
        }
    }
}
