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
    public class BatchAIValidationResultItem : ViewModelBase
    {
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeFolder { get; set; } = string.Empty;
        public string DocumentName { get; set; } = string.Empty;
        public string DocumentPath { get; set; } = string.Empty;
        public bool CanOpenDocument => !string.IsNullOrWhiteSpace(DocumentPath) && File.Exists(DocumentPath);
        public string FieldKey { get; set; } = string.Empty;
        public string FieldDisplayName { get; set; } = string.Empty;
        private string _currentValue = string.Empty;
        public string CurrentValue
        {
            get => _currentValue;
            set => SetProperty(ref _currentValue, value);
        }

        public string SuggestedValue { get; set; } = string.Empty;
        private string _severity = "ok";
        public string Severity
        {
            get => _severity;
            set => SetProperty(ref _severity, value);
        }

        private string _message = string.Empty;
        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        private bool _canApply;
        public bool CanApply
        {
            get => _canApply;
            set => SetProperty(ref _canApply, value);
        }

        private bool _isApplied;
        public bool IsApplied
        {
            get => _isApplied;
            set => SetProperty(ref _isApplied, value);
        }
    }

    public partial class EmployeesViewModel
    {
        private void OpenBatchAIValidation()
        {
            if (!_geminiApiService.IsConfigured)
            {
                BatchAIStatusMessage = Res("AIChatNoModel");
                IsBatchAIValidationOpen = true;
                return;
            }

            BatchAICheckOnlySelected = IsSelectionMode && Employees.Any(e => e.IsSelected);
            BatchAIProgressCurrent = 0;
            BatchAIProgressTotal = 0;
            ClearBatchAIAction();
            BatchAIResults.Clear();
            OnPropertyChanged(nameof(HasBatchAIResults));
            ShowBatchAIOptions = true;
            BatchAIStatusMessage = GetString("EmpAIIntroMessage") ?? "Виберіть, які документи перевірити. Якщо увімкнений режим вибору, можна перевірити тільки позначених працівників.";
            IsBatchAIValidationOpen = true;
        }

        private async Task RunBatchAIValidationAsync()
        {
            if (!_geminiApiService.IsConfigured)
            {
                BatchAIStatusMessage = Res("AIChatNoModel");
                return;
            }

            if (!BatchAICheckPassport && !BatchAICheckVisa && !BatchAICheckInsurance && !BatchAICheckPermit)
            {
                BatchAIStatusMessage = GetString("EmpAISelectDocType") ?? "Виберіть хоча б один тип документа.";
                return;
            }

            var employeesToCheck = BatchAICheckOnlySelected
                ? Employees.Where(e => e.IsSelected).ToList()
                : Employees.ToList();

            if (employeesToCheck.Count == 0)
            {
                BatchAIStatusMessage = GetString("EmpAINoEmployeesToCheck") ?? "Немає працівників для перевірки.";
                return;
            }

            _batchAICts?.Cancel();
            _batchAICts = new CancellationTokenSource();
            IsBatchAIValidationRunning = true;
            ShowBatchAIOptions = false;
            BatchAIProgressCurrent = 0;
            BatchAIProgressTotal = employeesToCheck.Count;
            ClearBatchAIAction();
            BatchAIResults.Clear();
            OnPropertyChanged(nameof(HasBatchAIResults));

            var checkedDocuments = 0;
            var skippedDocuments = 0;

            try
            {
                foreach (var employee in employeesToCheck)
                {
                    _batchAICts.Token.ThrowIfCancellationRequested();
                    BatchAIProgressCurrent++;
                    BatchAIStatusMessage = string.Format(GetString("EmpAICheckingProgressFmt") ?? "Перевірка {0}/{1}: {2}", BatchAIProgressCurrent, BatchAIProgressTotal, employee.FullName);
                    SetBatchAIAction(employee.FullName, GetString("EmpAIStageProfile") ?? "Профіль", GetString("EmpAIStageLoadingEmployeeData") ?? "Завантажую дані працівника");
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    var data = _employeeService.LoadEmployeeData(employee.EmployeeFolder);
                    if (data == null)
                    {
                        AddBatchAIResult(employee.FullName, employee.EmployeeFolder, GetString("EmpAIStageProfile") ?? "Профіль", string.Empty, "error", GetString("EmpAIProfileReadError") ?? "Не вдалося прочитати employee.json.");
                        continue;
                    }

                    if (BatchAICheckPassport)
                        await ValidateBatchDocumentAsync(employee, data, "passport", _batchAICts.Token, counters => { checkedDocuments += counters.Checked; skippedDocuments += counters.Skipped; });

                    if (BatchAICheckVisa)
                        await ValidateBatchDocumentAsync(employee, data, "visa", _batchAICts.Token, counters => { checkedDocuments += counters.Checked; skippedDocuments += counters.Skipped; });

                    if (BatchAICheckInsurance)
                        await ValidateBatchDocumentAsync(employee, data, "insurance", _batchAICts.Token, counters => { checkedDocuments += counters.Checked; skippedDocuments += counters.Skipped; });

                    if (BatchAICheckPermit)
                        await ValidateBatchDocumentAsync(employee, data, "permit", _batchAICts.Token, counters => { checkedDocuments += counters.Checked; skippedDocuments += counters.Skipped; });
                }

                if (BatchAIResults.Count == 0)
                    AddBatchAIResult(
                        GetString("EmpAIAllEmployeesLabel") ?? "Усі працівники",
                        string.Empty,
                        GetString("EmpAICheckLabel") ?? "AI перевірка",
                        string.Empty,
                        "ok",
                        GetString("EmpAINoDiscrepancies") ?? "Критичних розбіжностей не знайдено.");

                ClearBatchAIAction();
                BatchAIStatusMessage = string.Format(
                    GetString("EmpAIDoneSummaryFmt") ?? "Готово. Працівників: {0}, документів перевірено: {1}, пропущено без файла: {2}, результатів: {3}.",
                    employeesToCheck.Count, checkedDocuments, skippedDocuments, BatchAIResults.Count);
            }
            catch (OperationCanceledException)
            {
                ClearBatchAIAction();
                BatchAIStatusMessage = string.Format(
                    GetString("EmpAICancelledSummaryFmt") ?? "Скасовано. Перевірено працівників: {0}/{1}.",
                    Math.Max(0, BatchAIProgressCurrent - 1), BatchAIProgressTotal);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeesViewModel.RunBatchAIValidation", ex);
                BatchAIStatusMessage = string.Format(Res("MsgErrorFmt"), ex.Message);
            }
            finally
            {
                IsBatchAIValidationRunning = false;
                _batchAICts?.Dispose();
                _batchAICts = null;
            }
        }

        private async Task ValidateBatchDocumentAsync(
            EmployeeModels.EmployeeSummary employee,
            EmployeeModels.EmployeeData data,
            string documentType,
            CancellationToken token,
            Action<(int Checked, int Skipped)> updateCounters)
        {
            var (docName, docKey, filePath) = GetBatchDocumentInfo(employee.EmployeeFolder, data, documentType);
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                SetBatchAIAction(employee.FullName, docName, GetString("EmpAISkippedFileNotFound") ?? "Пропущено: файл не знайдено");
                updateCounters((0, 1));
                return;
            }

            SetBatchAIAction(employee.FullName, docName, GetString("EmpAIReadingDocument") ?? "AI читає документ");
            await Dispatcher.Yield(DispatcherPriority.Background);
            using var documentCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            documentCts.CancelAfter(TimeSpan.FromSeconds(90));
            using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(documentCts.Token);
            var stageTask = RunBatchReadingStagesAsync(employee.FullName, docName, documentType, stageCts.Token);
            Dictionary<string, string> extracted;
            try
            {
                extracted = await ScanBatchDocumentAsync(filePath, docKey, documentCts.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                SetBatchAIAction(employee.FullName, docName, GetString("EmpAITimeoutStage") ?? "AI не відповів за відведений час");
                AddBatchAIResult(employee.FullName, employee.EmployeeFolder, docName, filePath, "warning", GetString("EmpAITimeoutMessage") ?? "Пропущено: AI не відповів за 90 секунд. Перевірка продовжилась далі.");
                updateCounters((0, 1));
                return;
            }
            finally
            {
                stageCts.Cancel();
                try { await stageTask; } catch (OperationCanceledException) { }
            }
            updateCounters((1, 0));

            if (extracted.Count == 0)
            {
                SetBatchAIAction(employee.FullName, docName, GetString("EmpAICannotReadStage") ?? "AI не зміг прочитати документ");
                AddBatchAIResult(employee.FullName, employee.EmployeeFolder, docName, filePath, "warning", GetString("EmpAICannotReadMessage") ?? "AI не зміг прочитати документ або відповідь була порожня.");
                return;
            }

            SetBatchAIAction(employee.FullName, docName, string.Format(GetString("EmpAIFoundFieldsFmt") ?? "Знайдено: {0}", FormatFoundBatchFields(extracted)));
            await Dispatcher.Yield(DispatcherPriority.Background);

            if (!AIScanPrompts.IsDocumentKindCompatible(docKey, extracted))
            {
                var kind = AIScanPrompts.GetDocumentKind(extracted);
                AddBatchAIResult(employee.FullName, employee.EmployeeFolder, docName, filePath, "warning", string.Format(GetString("EmpAIWrongDocKindFmt") ?? "AI розпізнав інший тип документа: {0}.", kind));
            }

            CheckBatchDocumentOwnership(employee.FullName, employee.EmployeeFolder, docName, filePath, data, extracted);

            switch (documentType)
            {
                case "passport":
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportNumber", data.PassportNumber, GetString("EmpAIFieldPassportNumber") ?? "Номер паспорта/ID");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "RodneCislo", data.HasRodneCisloData ? data.RodneCislo : string.Empty, GetString("DetFieldRodneCislo") ?? "Ідентифікаційний код");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportExpiry", data.PassportExpiry, GetString("PdfFieldValidTo") ?? "Дійсний до");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportAuthority", data.PassportAuthority, GetString("EmpAIFieldPassportAuthority") ?? "Ким виданий паспорт");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportCity", data.PassportCity, GetString("DetFieldBirthCity") ?? "Місто народження");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "PassportCountry", data.PassportCountry, GetString("DetFieldBirthCountry") ?? "Країна народження");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "Citizenship", data.Citizenship, GetString("DetFieldCitizenship") ?? "Громадянство");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "IssuingCountry", data.IssuingCountry, GetString("DetFieldIssuingCountry") ?? "Країна видачі");
                    break;
                case "visa":
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "RodneCislo", data.HasRodneCisloData ? data.RodneCislo : string.Empty, GetString("DetFieldRodneCislo") ?? "Ідентифікаційний код");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "VisaNumber", data.VisaNumber, GetString("EmpAIFieldVisaNumber") ?? "Номер візи/карти");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "VisaStartDate", data.VisaStartDate, GetString("HistFieldVisaStartDate") ?? "Початок візи");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "VisaExpiry", data.VisaExpiry, GetString("EmpAIFieldVisaEnd") ?? "Кінець візи");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "VisaAuthority", data.VisaAuthority, GetString("EmpAIFieldVisaAuthority") ?? "Орган візи");
                    break;
                case "insurance":
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "InsuranceCompanyShort", data.InsuranceCompanyShort, GetString("EmpAIFieldInsuranceShort") ?? "Страхова");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "InsuranceNumber", data.InsuranceNumber, GetString("DetFieldInsNum") ?? "Номер страховки");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "InsuranceExpiry", data.InsuranceExpiry, GetString("EmpAIFieldInsuranceEnd") ?? "Кінець страховки");
                    break;
                case "permit":
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "WorkPermitNumber", data.WorkPermitNumber, GetString("DetFieldWpNumber") ?? "Номер дозволу");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "WorkPermitIssueDate", data.WorkPermitIssueDate, GetString("EmpAIFieldPermitStart") ?? "Початок дозволу");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "WorkPermitExpiry", data.WorkPermitExpiry, GetString("EmpAIFieldPermitEnd") ?? "Кінець дозволу");
                    AddBatchCompare(employee.FullName, employee.EmployeeFolder, docName, filePath, extracted, "WorkPermitAuthority", data.WorkPermitAuthority, GetString("EmpAIFieldPermitAuthority") ?? "Орган дозволу");
                    break;
            }
        }

        private void SetBatchAIAction(string employeeName, string documentName, string fieldName)
        {
            BatchAICurrentEmployee = employeeName;
            BatchAICurrentDocument = documentName;
            BatchAICurrentField = fieldName;
        }

        private void ClearBatchAIAction()
        {
            BatchAICurrentEmployee = string.Empty;
            BatchAICurrentDocument = string.Empty;
            BatchAICurrentField = string.Empty;
        }

        private async Task RunBatchReadingStagesAsync(
            string employeeName,
            string docName,
            string documentType,
            CancellationToken token)
        {
            var stages = GetBatchReadingStages(documentType);
            var index = 0;

            while (!token.IsCancellationRequested)
            {
                SetBatchAIAction(employeeName, docName, stages[index % stages.Length]);
                index++;
                await Task.Delay(1200, token);
            }
        }

        private string[] GetBatchReadingStages(string documentType)
        {
            return documentType switch
            {
                "passport" => new[]
                {
                    GetString("EmpAIStagePassportName") ?? "AI шукає ім'я та прізвище",
                    GetString("EmpAIStagePassportBirthDate") ?? "AI шукає дату народження",
                    GetString("EmpAIStagePassportNumber") ?? "AI шукає номер паспорта / ID",
                    GetString("EmpAIStagePassportExpiry") ?? "AI шукає термін дії",
                    GetString("EmpAIStagePassportCountry") ?? "AI перевіряє країну і громадянство"
                },
                "visa" => new[]
                {
                    GetString("EmpAIStageVisaNumber") ?? "AI шукає номер візи / карти",
                    GetString("EmpAIStageVisaStart") ?? "AI шукає початок візи",
                    GetString("EmpAIStageVisaEnd") ?? "AI шукає кінець візи",
                    GetString("EmpAIStageVisaAuthority") ?? "AI шукає орган видачі",
                    GetString("EmpAIStageVisaName") ?? "AI перевіряє ім'я на документі"
                },
                "insurance" => new[]
                {
                    GetString("EmpAIStageInsuranceCompany") ?? "AI шукає страхову компанію",
                    GetString("EmpAIStageInsuranceNumber") ?? "AI шукає номер страховки",
                    GetString("EmpAIStageInsuranceExpiry") ?? "AI шукає термін дії страховки",
                    GetString("EmpAIStageInsuranceOwner") ?? "AI перевіряє власника страховки"
                },
                "permit" => new[]
                {
                    GetString("EmpAIStagePermitNumber") ?? "AI шукає номер дозволу",
                    GetString("EmpAIStagePermitIssueDate") ?? "AI шукає дату видачі дозволу",
                    GetString("EmpAIStagePermitExpiry") ?? "AI шукає кінець дозволу",
                    GetString("EmpAIStageVisaAuthority") ?? "AI шукає орган видачі",
                    GetString("EmpAIStagePermitName") ?? "AI перевіряє ім'я на дозволі"
                },
                _ => new[] { GetString("EmpAIReadingDocument") ?? "AI читає документ" }
            };
        }

        private string FormatFoundBatchFields(Dictionary<string, string> extracted)
        {
            var visible = extracted
                .Where(kv => !kv.Key.StartsWith("__", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(kv.Value))
                .Take(4)
                .Select(kv => $"{kv.Key}={kv.Value}");

            var text = string.Join("; ", visible);
            return string.IsNullOrWhiteSpace(text) ? (GetString("EmpAINoDataFoundLabel") ?? "дані не знайдено") : text;
        }

        private async Task<Dictionary<string, string>> ScanBatchDocumentAsync(string filePath, string docKey, CancellationToken token)
        {
            var prompt = AIScanPrompts.GetPrompt(docKey);
            var result = string.Equals(Path.GetExtension(filePath), ".pdf", StringComparison.OrdinalIgnoreCase)
                ? await _geminiApiService.ChatWithFileAsync(filePath, prompt, ct: token)
                : await _geminiApiService.ChatWithImageAsync(filePath, prompt, ct: token);

            if (GeminiApiService.IsFailureResponse(result))
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return AIScanPrompts.ValidateAndCleanParsedFields(docKey, AIScanPrompts.ParseResponse(result));
        }

        private (string Name, string DocKey, string FilePath) GetBatchDocumentInfo(
            string employeeFolder,
            EmployeeModels.EmployeeData data,
            string documentType)
        {
            return documentType switch
            {
                "passport" => (
                    GetString("EmpAiCheckPassport") ?? "Паспорт / ID-карта",
                    string.Equals(data.EmployeeType, "eu_citizen", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(data.EuDocumentType, "id_card", StringComparison.OrdinalIgnoreCase)
                            ? "id_card"
                            : "passport",
                    ResolveBatchDocumentPath(employeeFolder, data.Files?.Passport)),
                "visa" => (
                    GetString("EmpAiCheckVisa") ?? "Віза / карта побиту",
                    GetBatchVisaDocKey(data),
                    ResolveBatchDocumentPath(employeeFolder, FirstNonEmpty(data.Files?.Visa, data.Files?.VisaPage2, data.Files?.PassportPage2))),
                "insurance" => (
                    GetString("DetDocInsurance") ?? "Страховка",
                    "insurance",
                    ResolveBatchDocumentPath(employeeFolder, data.Files?.Insurance)),
                "permit" => (
                    GetString("ChkWorkPermit") ?? "Дозвіл на роботу",
                    "permit",
                    ResolveBatchDocumentPath(employeeFolder, data.Files?.WorkPermit)),
                _ => (documentType, documentType, string.Empty)
            };
        }

        private static string GetBatchVisaDocKey(EmployeeModels.EmployeeData data)
        {
            if (string.Equals(data.EmployeeType, "eu_citizen", StringComparison.OrdinalIgnoreCase)
                && string.Equals(data.EuDocumentType, "id_card", StringComparison.OrdinalIgnoreCase))
                return "id_card_back";

            return string.Equals(data.VisaDocType, "id_card", StringComparison.OrdinalIgnoreCase)
                ? "visa2"
                : "visa";
        }

        private static string ResolveBatchDocumentPath(string employeeFolder, string? storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
                return string.Empty;

            if (Path.IsPathRooted(storedPath) && File.Exists(storedPath))
                return storedPath;

            var combined = Path.Combine(employeeFolder, storedPath);
            return File.Exists(combined) ? combined : storedPath;
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        }

        private void CheckBatchDocumentOwnership(
            string employeeName,
            string employeeFolder,
            string docName,
            string documentPath,
            EmployeeModels.EmployeeData data,
            Dictionary<string, string> extracted)
        {
            SetBatchAIAction(employeeName, docName, GetString("EmpAIStageCompareIdentity") ?? "Звіряю ім'я, прізвище і дату народження");
            var hasFirst = TryGetBatchValue(extracted, "FirstName", out var firstName);
            var hasLast = TryGetBatchValue(extracted, "LastName", out var lastName);
            var swapped = hasFirst
                && hasLast
                && BatchNamesMatch(firstName, data.LastName)
                && BatchNamesMatch(lastName, data.FirstName);

            if (hasFirst && !BatchNamesMatch(firstName, data.FirstName) && !swapped)
            {
                var isLikelyOcr = IsLikelyNameOcrSlip(data.FirstName, firstName);
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "warning",
                    isLikelyOcr
                        ? string.Format(GetString("EmpAIFirstNameOcrFmt") ?? "Ім'я схоже на OCR-помилку, перевірте вручну: профіль '{0}', документ '{1}'.", data.FirstName, firstName)
                        : string.Format(GetString("EmpAIFirstNameMismatchFmt") ?? "Ім'я не збігається: профіль '{0}', документ '{1}'.", data.FirstName, firstName),
                    "FirstName",
                    GetString("EmpAIFieldFirstName") ?? "Ім'я",
                    data.FirstName,
                    firstName,
                    canApply: !isLikelyOcr);
            }

            if (hasLast && !BatchNamesMatch(lastName, data.LastName) && !swapped)
            {
                var isLikelyOcr = IsLikelyNameOcrSlip(data.LastName, lastName);
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "warning",
                    isLikelyOcr
                        ? string.Format(GetString("EmpAILastNameOcrFmt") ?? "Прізвище схоже на OCR-помилку, перевірте вручну: профіль '{0}', документ '{1}'.", data.LastName, lastName)
                        : string.Format(GetString("EmpAILastNameMismatchFmt") ?? "Прізвище не збігається: профіль '{0}', документ '{1}'.", data.LastName, lastName),
                    "LastName",
                    GetString("EmpAIFieldLastName") ?? "Прізвище",
                    data.LastName,
                    lastName,
                    canApply: !isLikelyOcr);
            }

            if (TryGetBatchValue(extracted, "BirthDate", out var birthDate) && !BatchValuesMatch("BirthDate", data.BirthDate, birthDate))
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "warning",
                    string.Format(GetString("EmpAIBirthDateMismatchFmt") ?? "Дата народження не збігається: профіль '{0}', документ '{1}'.", data.BirthDate, birthDate),
                    "BirthDate",
                    GetString("EmpAIFieldBirthDate") ?? "Дата народження",
                    data.BirthDate,
                    birthDate,
                    canApply: true);
        }

        private void AddBatchCompare(
            string employeeName,
            string employeeFolder,
            string docName,
            string documentPath,
            Dictionary<string, string> extracted,
            string fieldKey,
            string currentValue,
            string displayName)
        {
            SetBatchAIAction(employeeName, docName, string.Format(GetString("EmpAIStageCompareFieldFmt") ?? "Звіряю: {0}", displayName));

            if (!TryGetBatchValue(extracted, fieldKey, out var suggested))
                return;

            if (AIScanPrompts.IsLowConfidenceField(extracted, fieldKey)
                || AIScanPrompts.IsSuspiciousFieldValue(extracted, fieldKey, suggested, currentValue))
            {
                AddBatchAIResult(employeeName, employeeFolder, docName, documentPath, "warning", string.Format(GetString("EmpAILowConfidenceFmt") ?? "{0}: AI не впевнений у значенні '{1}', пропущено.", displayName, suggested));
                return;
            }

            if (string.IsNullOrWhiteSpace(currentValue))
            {
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "missing",
                    string.Format(GetString("EmpAIFieldEmptyFoundFmt") ?? "{0}: поле порожнє у профілі, у документі знайдено '{1}'.", displayName, suggested),
                    fieldKey,
                    displayName,
                    currentValue,
                    suggested,
                    canApply: true);
                return;
            }

            if (!BatchValuesMatch(fieldKey, currentValue, suggested))
                AddBatchAIResult(
                    employeeName,
                    employeeFolder,
                    docName,
                    documentPath,
                    "warning",
                    string.Format(GetString("EmpAIFieldMismatchFmt") ?? "{0}: профіль '{1}', документ '{2}'.", displayName, currentValue, suggested),
                    fieldKey,
                    displayName,
                    currentValue,
                    suggested,
                    canApply: true);
        }

        private async Task ApplyBatchAISuggestionAsync(BatchAIValidationResultItem item)
        {
            if (item == null || !item.CanApply || item.IsApplied || string.IsNullOrWhiteSpace(item.EmployeeFolder))
                return;

            try
            {
                var data = _employeeService.LoadEmployeeData(item.EmployeeFolder);
                if (data == null)
                {
                    item.Message = $"{item.Message} {GetString("EmpAIApplyReadProfileErrorSuffix") ?? "Не вдалося прочитати профіль для заповнення."}";
                    return;
                }

                var valueToApply = NormalizeBatchApplyValue(item.FieldKey, item.SuggestedValue);
                if (!SetBatchEmployeeField(data, item.FieldKey, valueToApply))
                {
                    item.Message = $"{item.Message} {GetString("EmpAIApplyUnsupportedFieldSuffix") ?? "Це поле поки не підтримує автоматичне заповнення."}";
                    return;
                }

                if (!_employeeService.SaveEmployeeData(item.EmployeeFolder, data))
                {
                    item.Message = $"{item.Message} {GetString("EmpAIApplySaveFailedSuffix") ?? "Не вдалося зберегти зміну."}";
                    return;
                }

                await _employeeService.AddHistoryEntry(item.EmployeeFolder, data.UniqueId, new EmployeeModels.EmployeeHistoryEntry
                {
                    EventType = "ProfileChanged",
                    Action = GetString("EmpAIHistoryActionBulkFill") ?? "AI масове заповнення",
                    Field = item.FieldDisplayName,
                    OldValue = item.CurrentValue,
                    NewValue = valueToApply,
                    Description = string.Format(GetString("EmpAIHistoryDescBulkFillFmt") ?? "AI масово заповнив {0}: {1} → {2}", item.FieldDisplayName, item.CurrentValue, valueToApply)
                });

                item.IsApplied = true;
                item.CanApply = false;
                item.CurrentValue = valueToApply;
                item.Severity = "ok";
                item.Message = string.Format(GetString("EmpAIAppliedMessageFmt") ?? "{0}: заповнено значенням '{1}'.", item.FieldDisplayName, valueToApply);
                SetBatchAIAction(item.EmployeeName, item.DocumentName, string.Format(GetString("EmpAIAppliedStageFmt") ?? "Заповнено: {0}", item.FieldDisplayName));

                await LoadEmployeesAsync();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeesViewModel.ApplyBatchAISuggestion", ex);
                item.Message = $"{item.Message} {string.Format(GetString("MsgErrorFmt") ?? "Помилка: {0}", ex.Message)}";
            }
        }

        private void OpenBatchAIDocument(BatchAIValidationResultItem item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.DocumentPath) || !File.Exists(item.DocumentPath))
                return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = item.DocumentPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeesViewModel.OpenBatchAIDocument", ex.Message);
                item.Message = $"{item.Message} {string.Format(GetString("MsgOpenDocumentFailedFmt") ?? "Не вдалося відкрити документ: {0}", ex.Message)}";
            }
        }

        private static string NormalizeBatchApplyValue(string fieldKey, string value)
        {
            if (string.Equals(fieldKey, "FirstName", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "LastName", StringComparison.OrdinalIgnoreCase))
                return FormatPersonName(value);

            return value?.Trim() ?? string.Empty;
        }

        private static string FormatPersonName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var textInfo = CultureInfo.InvariantCulture.TextInfo;
            var normalized = value.Trim().ToLowerInvariant();
            normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            return string.Join(" ", normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => string.Join("-", part
                    .Split('-', StringSplitOptions.RemoveEmptyEntries)
                    .Select(piece => textInfo.ToTitleCase(piece)))));
        }

        private static bool SetBatchEmployeeField(EmployeeModels.EmployeeData data, string fieldKey, string value)
        {
            switch (fieldKey)
            {
                case "FirstName": data.FirstName = value; return true;
                case "LastName": data.LastName = value; return true;
                case "BirthDate": data.BirthDate = value; return true;
                case "RodneCislo": data.RodneCislo = value; data.HasRodneCisloData = true; return true;
                case "PassportNumber": data.PassportNumber = value; return true;
                case "PassportExpiry": data.PassportExpiry = value; return true;
                case "PassportAuthority": data.PassportAuthority = value; return true;
                case "PassportCity": data.PassportCity = value; return true;
                case "PassportCountry": data.PassportCountry = value; return true;
                case "Citizenship": data.Citizenship = value; return true;
                case "IssuingCountry": data.IssuingCountry = value; return true;
                case "VisaNumber": data.VisaNumber = value; return true;
                case "VisaStartDate": data.VisaStartDate = value; return true;
                case "VisaExpiry": data.VisaExpiry = value; return true;
                case "VisaAuthority": data.VisaAuthority = value; return true;
                case "InsuranceCompanyShort": data.InsuranceCompanyShort = value; return true;
                case "InsuranceNumber": data.InsuranceNumber = value; return true;
                case "InsuranceExpiry": data.InsuranceExpiry = value; return true;
                case "WorkPermitNumber": data.WorkPermitNumber = value; return true;
                case "WorkPermitIssueDate": data.WorkPermitIssueDate = value; return true;
                case "WorkPermitExpiry": data.WorkPermitExpiry = value; return true;
                case "WorkPermitAuthority": data.WorkPermitAuthority = value; return true;
                default: return false;
            }
        }

        private void AddBatchAIResult(
            string employeeName,
            string employeeFolder,
            string docName,
            string documentPath,
            string severity,
            string message,
            string fieldKey = "",
            string fieldDisplayName = "",
            string currentValue = "",
            string suggestedValue = "",
            bool canApply = false)
        {
            BatchAIResults.Add(new BatchAIValidationResultItem
            {
                EmployeeName = employeeName,
                EmployeeFolder = employeeFolder,
                DocumentName = docName,
                DocumentPath = documentPath,
                FieldKey = fieldKey,
                FieldDisplayName = fieldDisplayName,
                CurrentValue = currentValue,
                SuggestedValue = suggestedValue,
                Severity = severity,
                Message = message,
                CanApply = canApply
            });
            OnPropertyChanged(nameof(HasBatchAIResults));
        }

        private static bool TryGetBatchValue(Dictionary<string, string> source, string key, out string value)
        {
            if (source.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                value = raw.Trim();
                return true;
            }

            value = string.Empty;
            return false;
        }

        private static bool BatchValuesMatch(string fieldKey, string current, string suggested)
        {
            if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(suggested))
                return false;

            if (fieldKey.EndsWith("Expiry", StringComparison.OrdinalIgnoreCase)
                || fieldKey.EndsWith("IssueDate", StringComparison.OrdinalIgnoreCase)
                || fieldKey.EndsWith("StartDate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "BirthDate", StringComparison.OrdinalIgnoreCase))
            {
                var currentDate = DateParsingHelper.TryParseDate(current);
                var suggestedDate = DateParsingHelper.TryParseDate(suggested);
                if (currentDate != null && suggestedDate != null)
                    return currentDate.Value.Date == suggestedDate.Value.Date;
            }

            if (fieldKey.Contains("Number", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "RodneCislo", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(NormalizeBatchDocumentNumber(current), NormalizeBatchDocumentNumber(suggested), StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(fieldKey, "FirstName", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "LastName", StringComparison.OrdinalIgnoreCase))
                return BatchNamesMatch(current, suggested);

            return string.Equals(current.Trim(), suggested.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool BatchNamesMatch(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                return true;

            return string.Equals(NormalizeBatchName(left), NormalizeBatchName(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeBatchName(string value)
        {
            var normalized = value.Trim().ToUpperInvariant()
                .Replace('-', ' ')
                .Replace('’', '\'')
                .Replace('`', '\'')
                .Replace('´', '\'')
                .Replace('\u00A0', ' ');

            return string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static string NormalizeBatchDocumentNumber(string value)
        {
            return new string(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        }

        private static bool IsLikelyNameOcrSlip(string profileValue, string documentValue)
        {
            var left = NormalizeBatchName(profileValue).Replace(" ", string.Empty, StringComparison.Ordinal);
            var right = NormalizeBatchName(documentValue).Replace(" ", string.Empty, StringComparison.Ordinal);
            if (left.Length < 4 || right.Length < 4)
                return false;

            // Allow up to a 2-character length gap (e.g. a dropped double letter like "rr" -> "r",
            // or a missing vowel) in addition to a short edit distance, so common AI/OCR misreads
            // of longer names surface as a soft "check manually" warning instead of a hard mismatch.
            if (Math.Abs(left.Length - right.Length) > 2)
                return false;

            return DamerauLevenshteinDistance(left, right) <= 2;
        }

        private static int DamerauLevenshteinDistance(string source, string target)
        {
            var distances = new int[source.Length + 1, target.Length + 1];

            for (var i = 0; i <= source.Length; i++)
                distances[i, 0] = i;

            for (var j = 0; j <= target.Length; j++)
                distances[0, j] = j;

            for (var i = 1; i <= source.Length; i++)
            {
                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distances[i, j] = Math.Min(
                        Math.Min(distances[i - 1, j] + 1, distances[i, j - 1] + 1),
                        distances[i - 1, j - 1] + cost);

                    if (i > 1
                        && j > 1
                        && source[i - 1] == target[j - 2]
                        && source[i - 2] == target[j - 1])
                    {
                        distances[i, j] = Math.Min(distances[i, j], distances[i - 2, j - 2] + 1);
                    }
                }
            }

            return distances[source.Length, target.Length];
        }
    }
}

