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
            var templates = _templateService.GetTemplates(_company.Name);
            BatchTemplates = new ObservableCollection<TemplateEntry>(templates);
            IsBatchGenerateOpen = true;
        }


        private void BatchGenerateToFolder(TemplateEntry? template)
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

            BatchGenerate(template, dialog.FolderName);
        }

        private void BatchGenerate(TemplateEntry? template, string? outputFolder = null)
        {
            if (!PolicyService.EnsureWriteAllowed("Пакетна генерація документів"))
                return;
            if (template == null || _company == null) return;
            try
            {
                IsLoading = true;
                if (!string.IsNullOrWhiteSpace(outputFolder))
                    Directory.CreateDirectory(outputFolder);

                var selected = Employees.Where(e => e.IsSelected).ToList();
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
                        var data = _employeeService.LoadEmployeeData(emp.EmployeeFolder);
                        if (data == null)
                        {
                            fail++;
                            resultLines.Add(string.Format(GetString("EmpGenErrorProfileNotFoundFmt") ?? "[ПОМИЛКА] {0}: анкета не знайдена", employeeName));
                            continue;
                        }

                        if (!IsBatchEmployeeIdentityMatch(emp, data))
                        {
                            fail++;
                            resultLines.Add(string.Format(GetString("EmpGenErrorIdentityMismatchFmt") ?? "[ПОМИЛКА] {0}: дані не співпадають з вибраним працівником", employeeName));
                            LoggingService.LogWarning("EmployeesViewModel.BatchGenerate",
                                $"Skipped batch document generation because selected employee id '{emp.UniqueId}' does not match employee.json id '{data.UniqueId}' in folder '{emp.EmployeeFolder}'.");
                            continue;
                        }

                        var templateFullPath = _templateService.GetTemplateFullPath(_company.Name, template.FilePath) ?? string.Empty;
                        var templateFolder = Path.GetDirectoryName(templateFullPath) ?? string.Empty;
                        var docxSource = _templateService.ResolveDocxGenerationSource(templateFolder, templateFullPath);
                        bool hasTemplateFile = File.Exists(templateFullPath);
                        bool hasDocxSource = docxSource.Kind != TemplateDocxSourceKind.None;

                        if (!hasTemplateFile && !hasDocxSource)
                        {
                            fail++;
                            resultLines.Add(string.Format(GetString("EmpGenErrorTemplateNotFoundFmt") ?? "[ПОМИЛКА] {0}: шаблон не знайдено", employeeName));
                            continue;
                        }

                        var tagValues = _tagCatalogService.GetTagValueMapForEmployee(_company.Name, data)
                            ?? new Dictionary<string, string>();
                        var format = template.Format?.ToUpper() ?? Path.GetExtension(templateFullPath).TrimStart('.').ToUpper();
                        var generatedFileName = string.Empty;

                        string SanitizeFn(string n) => string.Join("_", n.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
                        string BuildOutputPath(string fileName)
                        {
                            var targetFolder = string.IsNullOrWhiteSpace(outputFolder)
                                ? emp.EmployeeFolder
                                : outputFolder;
                            return EnsureUniqueBatchOutputPath(Path.Combine(targetFolder, fileName));
                        }

                        if (format == "DOCX" || hasDocxSource)
                        {
                            if (!hasDocxSource)
                            {
                                fail++;
                                var err = GetString(docxSource.ErrorResourceKey ?? "EditorWordDocxNotReady")
                                    ?? (docxSource.ErrorResourceKey ?? "EditorWordDocxNotReady");
                                resultLines.Add($"[ПОМИЛКА] {employeeName}: {err}");
                                continue;
                            }

                            var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.docx");
                            var outPath = BuildOutputPath(outName);
                            if (docxSource.Kind == TemplateDocxSourceKind.Rtf)
                                _documentGenerationService.GenerateDocxFromRtf(docxSource.Path, outPath, tagValues);
                            else
                                _documentGenerationService.GenerateDocx(docxSource.Path, outPath, tagValues);
                            generatedFileName = Path.GetFileName(outPath);
                        }
                        else if (format == "XLSX" && hasTemplateFile)
                        {
                            var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.xlsx");
                            var outPath = BuildOutputPath(outName);
                            _documentGenerationService.GenerateXlsx(templateFullPath, outPath, tagValues);
                            generatedFileName = Path.GetFileName(outPath);
                        }
                        else if (format == "PDF" && hasTemplateFile)
                        {
                            var outName = SanitizeFn($"{data.FirstName}_{data.LastName} - {template.Name}.pdf");
                            var outPath = BuildOutputPath(outName);
                            _documentGenerationService.GeneratePdf(templateFullPath, outPath, tagValues);
                            generatedFileName = Path.GetFileName(outPath);
                        }

                        if (string.IsNullOrWhiteSpace(generatedFileName))
                        {
                            fail++;
                            resultLines.Add(string.Format(GetString("EmpGenErrorUnsupportedFormatFmt") ?? "[ПОМИЛКА] {0}: формат не підтримується ({1})", employeeName, format));
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

                LogBatchGeneration(template, selected.Count, success, fail, outputFolder, resultLines);

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

        private void LogBatchGeneration(
            TemplateEntry template,
            int selectedCount,
            int success,
            int fail,
            string? outputFolder,
            IReadOnlyList<string> resultLines)
        {
            var target = string.IsNullOrWhiteSpace(outputFolder)
                ? "папки працівників"
                : outputFolder;
            var details = string.Join(Environment.NewLine,
                new[]
                {
                    $"Шаблон: {template.Name}",
                    $"Обрано: {selectedCount}",
                    $"Успішно: {success}",
                    $"Помилки: {fail}",
                    $"Куди: {target}",
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

