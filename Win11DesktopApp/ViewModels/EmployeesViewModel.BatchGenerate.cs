using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using ClosedXML.Excel;
using Win11DesktopApp.Models;
using EmployeeModels = Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Converters;
using Win11DesktopApp.Services;
using Win11DesktopApp.Views;

namespace Win11DesktopApp.ViewModels
{
    public partial class EmployeesViewModel
    {
        private void OpenBatchGenerate()
        {
            if (!PolicyService.EnsureWriteAllowed("Пакетна генерація документів"))
                return;
            if (_company == null) return;
            var selected = Employees.Where(e => e.IsSelected).ToList();
            if (selected.Count == 0) return;

            BatchStatusMessage = string.Format(Res("MsgSelectedCount"), selected.Count);
            BatchContractSignDateOverride = string.Empty;
            var templates = _templateService.GetTemplates(_company.Name);
            BatchTemplates = new ObservableCollection<TemplateEntry>(templates);
            IsBatchGenerateOpen = true;
        }

        private void CloseBatchGenerate()
        {
            IsBatchGenerateOpen = false;
            BatchContractSignDateOverride = string.Empty;
        }


        private async Task BatchGenerateToFolderAsync(TemplateEntry? template)
        {
            if (!PolicyService.EnsureWriteAllowed("Пакетна генерація документів"))
                return;
            if (template == null)
                return;

            var dialog = new OpenFolderDialog
            {
                Title = GetString("EmpGenSelectFolderTitle") ?? "Виберіть папку для збереження документів"
            };

            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.FolderName))
                return;

            await BatchGenerateAsync(template, dialog.FolderName);
        }

        private async Task BatchGenerateAsync(TemplateEntry? template, string? outputFolder = null)
        {
            if (!PolicyService.EnsureWriteAllowed("Пакетна генерація документів"))
                return;
            if (template == null || _company == null) return;
            if (!TryResolveBatchSignDateOverride(out var signDateOverride, out var signDateError))
            {
                BatchStatusMessage = signDateError ?? string.Empty;
                ToastService.Instance.Warning(signDateError ?? string.Empty);
                return;
            }

            try
            {
                IsLoading = true;
                if (!string.IsNullOrWhiteSpace(outputFolder))
                    Directory.CreateDirectory(outputFolder);

                var selected = Employees.Where(e => e.IsSelected).ToList();
                var companyName = _company.Name;
                int success = 0;
                int fail = 0;
                var resultLines = new List<string>();

                foreach (var emp in selected)
                {
                    var employeeName = string.IsNullOrWhiteSpace(emp.FullName)
                        ? Path.GetFileName(emp.EmployeeFolder)
                        : emp.FullName;

                    try
                    {
                        var generatedFileName = await Task.Run(() => GenerateBatchDocumentForEmployee(
                            emp,
                            template,
                            companyName,
                            outputFolder,
                            employeeName,
                            resultLines,
                            signDateOverride));

                        if (string.IsNullOrWhiteSpace(generatedFileName))
                        {
                            fail++;
                            continue;
                        }

                        success++;
                        resultLines.Add($"[OK] {employeeName}: {generatedFileName}");
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("EmployeesViewModel.BatchGenerate", ex);
                        fail++;
                        resultLines.Add(string.Format(GetString("EmpGenErrorGenericFmt") ?? "[ПОМИЛКА] {0}: {1}", employeeName, ex.Message));
                    }
                }

                BatchStatusMessage = string.Join(Environment.NewLine,
                    new[] { string.Format(Res("MsgBatchResult"), success, fail) }.Concat(resultLines));

                LogBatchGeneration(template, selected.Count, success, fail, outputFolder, resultLines, signDateOverride);

                if (!string.IsNullOrWhiteSpace(outputFolder) && success > 0)
                    OpenFolderAfterBatchGeneration(outputFolder);
            }
            catch (Exception ex)
            {
                BatchStatusMessage = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private string? GenerateBatchDocumentForEmployee(
            EmployeeModels.EmployeeSummary emp,
            TemplateEntry template,
            string companyName,
            string? outputFolder,
            string employeeName,
            List<string> resultLines,
            string? signDateOverride)
        {
            var data = _employeeService.LoadEmployeeData(emp.EmployeeFolder);
            if (data == null)
            {
                resultLines.Add(string.Format(GetString("EmpGenErrorProfileNotFoundFmt") ?? "[ПОМИЛКА] {0}: анкета не знайдена", employeeName));
                return null;
            }

            if (!IsBatchEmployeeIdentityMatch(emp, data))
            {
                resultLines.Add(string.Format(GetString("EmpGenErrorIdentityMismatchFmt") ?? "[ПОМИЛКА] {0}: дані не співпадають з вибраним працівником", employeeName));
                LoggingService.LogWarning("EmployeesViewModel.BatchGenerate",
                    $"Skipped batch document generation because selected employee id '{emp.UniqueId}' does not match employee.json id '{data.UniqueId}' in folder '{emp.EmployeeFolder}'.");
                return null;
            }

            var templateFullPath = _templateService.GetTemplateFullPath(companyName, template.FilePath) ?? string.Empty;
            var templateFolder = Path.GetDirectoryName(templateFullPath) ?? string.Empty;
            var hasTemplateFile = File.Exists(templateFullPath);
            var format = (template.Format?.ToUpperInvariant()
                ?? Path.GetExtension(templateFullPath).TrimStart('.').ToUpperInvariant());

            var tagValues = _tagCatalogService.GetTagValueMapForEmployee(companyName, data)
                ?? new Dictionary<string, string>();
            ApplyBatchSignDateOverride(tagValues, signDateOverride);

            string SanitizeFn(string n) => string.Join("_", n.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
            string BuildOutputPath(string fileName)
            {
                var targetFolder = string.IsNullOrWhiteSpace(outputFolder)
                    ? emp.EmployeeFolder
                    : outputFolder;
                return EnsureUniqueBatchOutputPath(Path.Combine(targetFolder, fileName));
            }

            if (format == "PDF" && hasTemplateFile)
            {
                var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.pdf");
                var outPath = BuildOutputPath(outName);
                _documentGenerationService.GeneratePdf(templateFullPath, outPath, tagValues);
                return Path.GetFileName(outPath);
            }

            if (format == "XLSX" && hasTemplateFile)
            {
                var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.xlsx");
                var outPath = BuildOutputPath(outName);
                _documentGenerationService.GenerateXlsx(templateFullPath, outPath, tagValues);
                return Path.GetFileName(outPath);
            }

            var docxSource = _templateService.ResolveDocxGenerationSource(templateFolder, templateFullPath);
            var hasDocxSource = docxSource.Kind != TemplateDocxSourceKind.None;

            if (format == "DOCX" || hasDocxSource)
            {
                if (!hasDocxSource)
                {
                    var err = GetString(docxSource.ErrorResourceKey ?? "EditorWordDocxNotReady")
                        ?? (docxSource.ErrorResourceKey ?? "EditorWordDocxNotReady");
                    resultLines.Add($"[ПОМИЛКА] {employeeName}: {err}");
                    return null;
                }

                var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.docx");
                var outPath = BuildOutputPath(outName);
                if (docxSource.Kind == TemplateDocxSourceKind.Rtf)
                    _documentGenerationService.GenerateDocxFromRtf(docxSource.Path, outPath, tagValues);
                else
                    _documentGenerationService.GenerateDocx(docxSource.Path, outPath, tagValues);
                return Path.GetFileName(outPath);
            }

            if (!hasTemplateFile && !hasDocxSource)
            {
                resultLines.Add(string.Format(GetString("EmpGenErrorTemplateNotFoundFmt") ?? "[ПОМИЛКА] {0}: шаблон не знайдено", employeeName));
                return null;
            }

            resultLines.Add(string.Format(GetString("EmpGenErrorUnsupportedFormatFmt") ?? "[ПОМИЛКА] {0}: формат не підтримується ({1})", employeeName, format));
            return null;
        }

        private static string EnsureUniqueBatchOutputPath(string path)
        {
            if (!File.Exists(path))
                return path;

            var folder = Path.GetDirectoryName(path) ?? string.Empty;
            var fileName = Path.GetFileNameWithoutExtension(path);
            var extension = Path.GetExtension(path);

            for (var i = 1; i < 1000; i++)
            {
                var candidate = Path.Combine(folder, $"{fileName} ({i}){extension}");
                if (!File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(folder, $"{fileName} ({DateTime.Now:yyyyMMddHHmmss}){extension}");
        }

        private static bool IsBatchEmployeeIdentityMatch(EmployeeModels.EmployeeSummary summary, EmployeeModels.EmployeeData data)
        {
            var expectedId = summary.UniqueId?.Trim() ?? string.Empty;
            var actualId = data.UniqueId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(expectedId) || string.IsNullOrWhiteSpace(actualId))
                return true;

            return string.Equals(expectedId, actualId, StringComparison.OrdinalIgnoreCase);
        }

        private void OpenFolderAfterBatchGeneration(string folder)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeesViewModel.OpenBatchOutputFolder", ex.Message);
            }
        }

        private bool TryResolveBatchSignDateOverride(out string? formattedDate, out string? error)
        {
            formattedDate = null;
            error = null;

            var raw = BatchContractSignDateOverride?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(raw))
                return true;

            var parsed = DateParsingHelper.TryParseDate(raw);
            if (parsed == null)
            {
                error = Res("EmpBatchSignDateInvalid");
                return false;
            }

            formattedDate = parsed.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            return true;
        }

        private static void ApplyBatchSignDateOverride(Dictionary<string, string> tagValues, string? signDateOverride)
        {
            if (string.IsNullOrWhiteSpace(signDateOverride) || tagValues == null)
                return;

            tagValues["EMPLOYEE_ContractSignDate"] = signDateOverride;
        }

        private void LogBatchGeneration(
            TemplateEntry template,
            int selectedCount,
            int success,
            int fail,
            string? outputFolder,
            IReadOnlyList<string> resultLines,
            string? signDateOverride)
        {
            var target = string.IsNullOrWhiteSpace(outputFolder)
                ? "папки працівників"
                : outputFolder;
            var signDateLine = string.IsNullOrWhiteSpace(signDateOverride)
                ? "Дата підпису: з карток"
                : $"Дата підпису (лише ця генерація): {signDateOverride}";
            var details = string.Join(Environment.NewLine,
                new[]
                {
                    $"Шаблон: {template.Name}",
                    $"Обрано: {selectedCount}",
                    $"Успішно: {success}",
                    $"Помилки: {fail}",
                    $"Куди: {target}",
                    signDateLine,
                    "Результати:"
                }.Concat(resultLines));

            _activityLogService.Log(
                "BatchDocGenerated",
                "Document",
                _company?.Name ?? string.Empty,
                string.Empty,
                $"Масова генерація «{template.Name}»: успішно {success}, помилки {fail}",
                details: details);
        }
    }
}

