using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Docnet.Core;
using Docnet.Core.Models;
using Win11DesktopApp.Converters;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public sealed class ArchiveEmployeeResult
    {
        public string ArchiveFolder { get; init; } = string.Empty;
        public string OperationId { get; init; } = string.Empty;
        public bool SourceCleanupDeferred { get; init; }
        public bool Success => !string.IsNullOrEmpty(ArchiveFolder);
    }

    public sealed class RestoreEmployeeResult
    {
        public string RestoredFolder { get; init; } = string.Empty;
        public string OperationId { get; init; } = string.Empty;
        public bool Success => !string.IsNullOrEmpty(RestoredFolder);
    }

    public sealed class UndoArchiveResult
    {
        public string RestoredFolder { get; init; } = string.Empty;
        public string UndoOperationId { get; init; } = string.Empty;
        public bool Success => !string.IsNullOrEmpty(RestoredFolder);
    }

    public class EmployeeService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly FolderService _folderService;
        private readonly LocalDbService? _localDbService;
        private readonly EmployeeIndexDbService? _employeeIndexDbService;
        private bool _useLocalDbArchiveLog;
        private bool _useLocalDbHistory;
        private static readonly SemaphoreSlim _historyLock = new(1, 1);
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public EmployeeService(AppSettingsService appSettingsService, TagCatalogService tagCatalogService, FolderService folderService, LocalDbService? localDbService = null, EmployeeIndexDbService? employeeIndexDbService = null)
        {
            _appSettingsService = appSettingsService;
            _tagCatalogService = tagCatalogService;
            _folderService = folderService;
            _localDbService = localDbService;
            _employeeIndexDbService = employeeIndexDbService;
            CleanupStaleTempFolders();
        }

        private static T? ReadJson<T>(string path)
        {
            return SafeFileService.ReadJson<T>(path, _jsonOptions, System.Text.Encoding.UTF8);
        }

        private static T ReadJsonOrDefault<T>(string path, T fallback)
        {
            return SafeFileService.ReadJsonOrDefault(path, fallback, _jsonOptions, System.Text.Encoding.UTF8);
        }

        private static void WriteJsonAtomic<T>(string path, T value)
        {
            SafeFileService.WriteJsonAtomic(path, value, _jsonOptions, System.Text.Encoding.UTF8);
        }

        private static void CleanupStaleTempFolders()
        {
            try
            {
                var wizardRoot = Path.Combine(Path.GetTempPath(), "AgencyContractor", "EmployeeWizard");
                if (!Directory.Exists(wizardRoot)) return;
                foreach (var dir in Directory.GetDirectories(wizardRoot))
                {
                    try { Directory.Delete(dir, true); }
                    catch (Exception ex) { LoggingService.LogWarning("EmployeeService.CleanupStaleTempFolders", ex.Message); }
                }
            }
            catch (Exception ex) { LoggingService.LogError("EmployeeService.CleanupStaleTempFolders", ex); }
        }

        public string CreateTempFolder()
        {
            var root = Path.Combine(Path.GetTempPath(), "AgencyContractor", "EmployeeWizard", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            Debug.WriteLine($"EmployeeService.CreateTempFolder: {root}");
            return root;
        }

        public void CleanupTempFolder(string tempFolder)
        {
            if (!Directory.Exists(tempFolder)) return;

            try
            {
                foreach (var file in Directory.GetFiles(tempFolder, "*", SearchOption.AllDirectories))
                {
                    try { SafeFileService.DeleteFile(file); }
                    catch (Exception ex) { LoggingService.LogWarning("EmployeeService.CleanupTempFolder", ex.Message); }
                }

                Directory.Delete(tempFolder, true);
            }
            catch
            {
                // Schedule a delayed retry on a background thread
                System.Threading.Tasks.Task.Run(async () =>
                {
                    await System.Threading.Tasks.Task.Delay(3000);
                    try
                    {
                        if (Directory.Exists(tempFolder))
                            Directory.Delete(tempFolder, true);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("EmployeeService.CleanupTempFolder",
                            $"Deferred cleanup failed, will be cleaned on next startup: {ex.Message}");
                    }
                });
            }
        }

        public EmployeeDocumentTemp PrepareTempDocument(string sourcePath, string tempFolder, string baseName)
        {
            var ext = Path.GetExtension(sourcePath).ToLower();
            var temp = new EmployeeDocumentTemp { OriginalExtension = ext };
            Debug.WriteLine($"EmployeeService.PrepareTempDocument: {sourcePath} -> {tempFolder} ({ext})");

            try
            {
                if (!File.Exists(sourcePath))
                {
                    LoggingService.LogWarning("PrepareTempDocument", $"Source file not found: {sourcePath}");
                    return temp;
                }

                if (ext == ".pdf")
                {
                    var destPdf = Path.Combine(tempFolder, $"{baseName}.pdf");
                    SafeFileService.CopyFile(sourcePath, destPdf);
                    temp.PdfPath = destPdf;
                    temp.IsPdf = true;
                    return temp;
                }

                var destImage = Path.Combine(tempFolder, $"{baseName}.jpg");
                ConvertImageToJpg(sourcePath, destImage);
                temp.ImagePath = destImage;
                temp.IsPdf = false;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.PrepareTempDocument", ex);
                Debug.WriteLine($"PrepareTempDocument error for {sourcePath}: {ex.Message}");
            }
            return temp;
        }

        public List<string> RenderPdfPages(string pdfPath, string tempFolder, string baseName, int? maxPages = null)
        {
            var result = new List<string>();
            try
            {
                using var docReader = DocLib.Instance.GetDocReader(pdfPath, new PageDimensions(2));
                int pageCount = docReader.GetPageCount();
                int pagesToRender = maxPages.HasValue
                    ? Math.Min(pageCount, Math.Max(0, maxPages.Value))
                    : pageCount;

                for (int i = 0; i < pagesToRender; i++)
                {
                    using var pageReader = docReader.GetPageReader(i);
                    var rawBytes = pageReader.GetImage();
                    var width = pageReader.GetPageWidth();
                    var height = pageReader.GetPageHeight();

                    if (width <= 0 || height <= 0) continue;

                    var stride = width * 4;
                    var bmp = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rawBytes, stride);
                    bmp.Freeze();

                    var pagePath = Path.Combine(tempFolder, $"{baseName}_page{i + 1}.png");
                    using var fs = new FileStream(pagePath, FileMode.Create);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    encoder.Save(fs);

                    result.Add(pagePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RenderPdfPages error: {ex.Message}");
                LoggingService.LogError("EmployeeService.RenderPdfPages", ex);
            }
            return result;
        }

        public string SaveEmployee(string firmName, EmployeeData data, EmployeeDocumentTemp passport, EmployeeDocumentTemp visa, EmployeeDocumentTemp insurance, string photoPath,
            EmployeeDocumentTemp? idCardFront = null, EmployeeDocumentTemp? idCardBack = null, EmployeeDocumentTemp? workPermit = null, EmployeeDocumentTemp? passportPage2 = null,
            EmployeeDocumentTemp? visaPage2 = null)
        {
            var employeesFolder = _folderService.GetEmployeesFolder(firmName);
            if (string.IsNullOrEmpty(employeesFolder))
            {
                Debug.WriteLine("EmployeeService.SaveEmployee: employees folder path is empty");
                LoggingService.LogWarning("EmployeeService.SaveEmployee", $"Employees folder path is empty for firm '{firmName}'");
                return string.Empty;
            }

            string employeeFolder = string.Empty;
            var employeeFolderExisted = false;

            try
            {
                Directory.CreateDirectory(employeesFolder);

                employeeFolder = ResolveEmployeeFolder(employeesFolder, data);
                employeeFolderExisted = Directory.Exists(employeeFolder);
                Directory.CreateDirectory(employeeFolder);

                data.Files.Passport = SaveDocument(passport, employeeFolder, $"{data.FirstName} {data.LastName} - Pass");

                var isIdCard = data.VisaDocType == "id_card";
                var visaFileName = isIdCard ? $"{data.FirstName} {data.LastName} - Carta 1" : $"{data.FirstName} {data.LastName} - Viza";
                data.Files.Visa = SaveDocument(visa, employeeFolder, visaFileName);

                if (isIdCard)
                    data.Files.VisaPage2 = SaveDocument(visaPage2, employeeFolder, $"{data.FirstName} {data.LastName} - Carta 2");

                var insName = string.IsNullOrWhiteSpace(data.InsuranceCompanyShort) ? "Insurance" : data.InsuranceCompanyShort;
                data.Files.Insurance = SaveDocument(insurance, employeeFolder, $"{data.FirstName} {data.LastName} - {insName}");

                data.Files.WorkPermit = SaveDocument(workPermit, employeeFolder, $"{data.FirstName} {data.LastName} - Povolení k práci");
                data.Files.PassportPage2 = SaveDocument(passportPage2, employeeFolder, $"{data.FirstName} {data.LastName} - Pass Page2");

                if (!string.IsNullOrEmpty(photoPath))
                {
                    if (!File.Exists(photoPath))
                    {
                        LoggingService.LogWarning("EmployeeService.SaveEmployee",
                            $"Photo file not found: {photoPath}");
                    }
                    else
                    {
                        var photoDest = Path.Combine(employeeFolder, $"{data.FirstName} {data.LastName} - Photo.jpg");
                        SafeFileService.CopyFile(photoPath, photoDest);
                        data.Files.Photo = Path.GetFileName(photoDest);
                    }
                }

                if (!SaveEmployeeData(employeeFolder, data, notifyUser: false))
                {
                    if (!employeeFolderExisted)
                        TryDeleteIncompleteEmployeeFolder(employeeFolder);
                    return string.Empty;
                }

                _tagCatalogService.AddTagsForEmployee(firmName, data);
                LoggingService.LogInfo("EmployeeService", $"Employee saved: {data.FirstName} {data.LastName} in {firmName}");
                Debug.WriteLine($"EmployeeService.SaveEmployee: saved to {employeeFolder}");
                return employeeFolder;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveEmployee error: {ex.Message}");
                LoggingService.LogError("EmployeeService.SaveEmployee", ex);
                if (!employeeFolderExisted && !string.IsNullOrWhiteSpace(employeeFolder))
                    TryDeleteIncompleteEmployeeFolder(employeeFolder);
                return string.Empty;
            }
        }

        public List<EmployeeSummary> GetEmployeesForFirm(string firmName)
        {
            var employeesFolder = _folderService.GetEmployeesFolder(firmName);
            if (string.IsNullOrEmpty(employeesFolder))
            {
                Debug.WriteLine("EmployeeService.GetEmployeesForFirm: employees folder path is empty");
                return new List<EmployeeSummary>();
            }

            if (!Directory.Exists(employeesFolder))
            {
                Debug.WriteLine($"EmployeeService.GetEmployeesForFirm: missing folder {employeesFolder}");
                return new List<EmployeeSummary>();
            }

            if (_employeeIndexDbService != null)
            {
                try
                {
                    var rows = _employeeIndexDbService.GetEmployeesForFirmRows(firmName);
                    if (rows.Count > 0)
                    {
                        return rows
                            .Select(BuildSummaryFromIndexRow)
                            .ToList();
                    }

                    if (!HasAnyEmployeeFolders(employeesFolder))
                        return new List<EmployeeSummary>();

                    LoggingService.LogWarning("EmployeeService.GetEmployeesForFirm",
                        $"Employee index returned no rows for '{firmName}' while employee folders exist. Falling back to file scan.");
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("EmployeeService.GetEmployeesForFirm",
                        $"Employee index read failed for '{firmName}', falling back to file scan. {ex.Message}");
                }
            }

            return GetEmployeesForFirmFromFiles(firmName, employeesFolder);
        }

        public (List<EmployeeSummary> Employees, string Status) GetEmployeesForFirmWithStatus(string firmName)
        {
            var employeesFolder = _folderService.GetEmployeesFolder(firmName);
            if (string.IsNullOrEmpty(employeesFolder))
            {
                return (new List<EmployeeSummary>(), "RootFolderNotSet");
            }

            if (!Directory.Exists(employeesFolder))
            {
                return (new List<EmployeeSummary>(), "EmployeesFolderMissing");
            }

            if (_employeeIndexDbService != null)
            {
                try
                {
                    var rows = _employeeIndexDbService.GetEmployeesForFirmRows(firmName);
                    if (rows.Count > 0)
                    {
                        return (rows.Select(BuildSummaryFromIndexRow).ToList(), "Ok");
                    }

                    if (!HasAnyEmployeeFolders(employeesFolder))
                        return (new List<EmployeeSummary>(), "NoEmployees");

                    LoggingService.LogWarning("EmployeeService.GetEmployeesForFirmWithStatus",
                        $"Employee index returned no rows for '{firmName}' while employee folders exist. Falling back to file scan.");
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("EmployeeService.GetEmployeesForFirmWithStatus",
                        $"Employee index read failed for '{firmName}', falling back to file scan. {ex.Message}");
                }
            }

            return GetEmployeesForFirmWithStatusFromFiles(firmName, employeesFolder);
        }

        public EmployeeData? LoadEmployeeData(string employeeFolder)
        {
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            if (!File.Exists(jsonPath)) return null;
            try
            {
                var data = ReadJson<EmployeeData>(jsonPath);
                if (data == null) return null;

                bool changed = false;
                if (string.IsNullOrEmpty(data.UniqueId))
                {
                    data.UniqueId = Guid.NewGuid().ToString();
                    changed = true;
                }
                if (AutoDiscoverFiles(employeeFolder, data))
                    changed = true;

                if (changed)
                    SaveEmployeeData(employeeFolder, data);

                return data;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.LoadEmployeeData", ex);
                return null;
            }
        }

        private static bool AutoDiscoverFiles(string employeeFolder, EmployeeData data)
        {
            if (!Directory.Exists(employeeFolder)) return false;

            bool changed = false;

            // Clear entries that point to non-existent files
            if (!string.IsNullOrEmpty(data.Files.Passport) && !File.Exists(Path.Combine(employeeFolder, data.Files.Passport)))
            { data.Files.Passport = ""; changed = true; }
            if (!string.IsNullOrEmpty(data.Files.Visa) && !File.Exists(Path.Combine(employeeFolder, data.Files.Visa)))
            { data.Files.Visa = ""; changed = true; }
            if (!string.IsNullOrEmpty(data.Files.Insurance) && !File.Exists(Path.Combine(employeeFolder, data.Files.Insurance)))
            { data.Files.Insurance = ""; changed = true; }
            if (!string.IsNullOrEmpty(data.Files.Photo) && !File.Exists(Path.Combine(employeeFolder, data.Files.Photo)))
            { data.Files.Photo = ""; changed = true; }
            if (!string.IsNullOrEmpty(data.Files.PassportPage2) && !File.Exists(Path.Combine(employeeFolder, data.Files.PassportPage2)))
            { data.Files.PassportPage2 = ""; changed = true; }
            if (!string.IsNullOrEmpty(data.Files.WorkPermit) && !File.Exists(Path.Combine(employeeFolder, data.Files.WorkPermit)))
            { data.Files.WorkPermit = ""; changed = true; }

            // Fix misassigned entries: visa-like file stored as insurance
            if (!string.IsNullOrEmpty(data.Files.Insurance))
            {
                var insLower = data.Files.Insurance.ToLowerInvariant();
                if (insLower.Contains("- vize") || insLower.Contains("- viza") || insLower.Contains("- visa") || insLower.Contains("- víza"))
                {
                    if (string.IsNullOrEmpty(data.Files.Visa))
                        data.Files.Visa = data.Files.Insurance;
                    data.Files.Insurance = "";
                    changed = true;
                }
            }

            bool needsScan = string.IsNullOrEmpty(data.Files.Passport)
                          || string.IsNullOrEmpty(data.Files.Visa)
                          || string.IsNullOrEmpty(data.Files.Insurance)
                          || string.IsNullOrEmpty(data.Files.Photo);
            if (!needsScan) return changed;

            var files = Directory.GetFiles(employeeFolder);
            var fullNameLower = $"{data.FirstName} {data.LastName}".ToLowerInvariant();

            foreach (var file in files)
            {
                var name = Path.GetFileName(file);
                var nameLower = name.ToLowerInvariant();

                if (string.IsNullOrEmpty(data.Files.Passport) && !nameLower.Contains("page2")
                    && (nameLower.Contains("- pass.") || nameLower.Contains("- pass ")))
                {
                    data.Files.Passport = name;
                    changed = true;
                }
                else if (string.IsNullOrEmpty(data.Files.Visa)
                    && (nameLower.Contains("- viza.") || nameLower.Contains("- viza ")
                        || nameLower.Contains("- visa.") || nameLower.Contains("- visa ")
                        || nameLower.Contains("- vize.") || nameLower.Contains("- vize ")
                        || nameLower.Contains("- víza.") || nameLower.Contains("- víza ")))
                {
                    data.Files.Visa = name;
                    changed = true;
                }
                else if (string.IsNullOrEmpty(data.Files.Photo)
                    && (nameLower.Contains("- photo.") || nameLower.Contains("- photo ")
                        || nameLower.Contains("- foto.") || nameLower.Contains("- foto ")))
                {
                    data.Files.Photo = name;
                    changed = true;
                }
                else if (string.IsNullOrEmpty(data.Files.Insurance)
                    && !nameLower.Contains("- pass") && !nameLower.Contains("- viza")
                    && !nameLower.Contains("- visa") && !nameLower.Contains("- vize")
                    && !nameLower.Contains("- víza") && !nameLower.Contains("- photo")
                    && !nameLower.Contains("- foto") && !nameLower.Contains("- id ")
                    && !nameLower.Contains("- povolen") && !nameLower.Contains("work permit")
                    && !nameLower.EndsWith(".json") && !nameLower.EndsWith(".tmp")
                    && !nameLower.EndsWith(".bak") && !nameLower.EndsWith(".xlsx")
                    && !nameLower.EndsWith(".rtf") && !nameLower.EndsWith(".pdf")
                    && !nameLower.EndsWith(".docx")
                    && nameLower.StartsWith(fullNameLower))
                {
                    data.Files.Insurance = name;
                    changed = true;
                }

                if (string.IsNullOrEmpty(data.Files.PassportPage2)
                    && (nameLower.Contains("- pass page2") || nameLower.Contains("- pass_page2")))
                {
                    data.Files.PassportPage2 = name;
                    changed = true;
                }
                if (string.IsNullOrEmpty(data.Files.WorkPermit)
                    && (nameLower.Contains("- povolen") || nameLower.Contains("work permit") || nameLower.Contains("workpermit")))
                {
                    data.Files.WorkPermit = name;
                    changed = true;
                }
            }

            if (changed)
                LoggingService.LogInfo("EmployeeService.AutoDiscoverFiles", $"Discovered files for {data.FirstName} {data.LastName}: P={data.Files.Passport}, V={data.Files.Visa}, I={data.Files.Insurance}, Ph={data.Files.Photo}, WP={data.Files.WorkPermit}");

            return changed;
        }

        public bool SaveEmployeeData(string employeeFolder, EmployeeData data, bool notifyUser = true)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder))
            {
                LoggingService.LogWarning("EmployeeService.SaveEmployeeData", "Employee folder path is empty.");
                if (notifyUser)
                    NotifySaveFailure(Res("MsgProfileSaveFail"));
                return false;
            }

            if (data == null)
            {
                LoggingService.LogWarning("EmployeeService.SaveEmployeeData", $"Employee data is null for {employeeFolder}");
                if (notifyUser)
                    NotifySaveFailure(Res("MsgProfileSaveFail"));
                return false;
            }

            try
            {
                Directory.CreateDirectory(employeeFolder);
                if (string.IsNullOrWhiteSpace(data.UniqueId))
                    data.UniqueId = Guid.NewGuid().ToString();
                var jsonPath = Path.Combine(employeeFolder, "employee.json");
                WriteJsonAtomic(jsonPath, data);
                var firmName = App.AdminMirrorSyncService?.InferFirmNameFromEmployeeFolder(employeeFolder);
                App.AdminMirrorSyncService?.EnqueueEmployeeUpsert(firmName, employeeFolder, data);
                UpsertEmployeeIndex(employeeFolder, data, firmName);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SaveEmployeeData error: {ex.Message}");
                LoggingService.LogError("EmployeeService.SaveEmployeeData", ex);
                if (notifyUser)
                    NotifySaveFailure(Res("MsgProfileSaveFail"), ex.Message);
                return false;
            }
        }

        public void SetIgnoredDocument(string employeeFolder, string docType, string untilDate)
        {
            var data = LoadEmployeeData(employeeFolder);
            if (data == null) return;
            data.IgnoredDocuments ??= new Dictionary<string, string>();
            data.IgnoredDocuments[docType] = untilDate;
            SaveEmployeeData(employeeFolder, data);
        }

        public void ClearIgnoredDocument(string employeeFolder, string docType)
        {
            var data = LoadEmployeeData(employeeFolder);
            if (data == null) return;
            data.IgnoredDocuments ??= new Dictionary<string, string>();
            data.IgnoredDocuments.Remove(docType);
            SaveEmployeeData(employeeFolder, data);
        }

        public bool IsDocumentIgnored(string employeeFolder, string docType)
        {
            var data = LoadEmployeeData(employeeFolder);
            if (data?.IgnoredDocuments == null) return false;
            if (!data.IgnoredDocuments.TryGetValue(docType, out var untilStr)) return false;
            if (DateTime.TryParse(untilStr, out var until)) return DateTime.Now <= until;
            return false;
        }

        public string? GetIgnoredUntil(string employeeFolder, string docType)
        {
            var data = LoadEmployeeData(employeeFolder);
            if (data?.IgnoredDocuments == null) return null;
            data.IgnoredDocuments.TryGetValue(docType, out var val);
            return val;
        }

        public bool DeleteEmployee(string employeeFolder)
        {
            try
            {
                var data = LoadEmployeeData(employeeFolder);
                var employeeId = data?.UniqueId ?? string.Empty;
                var firmName = App.AdminMirrorSyncService?.InferFirmNameFromEmployeeFolder(employeeFolder);
                if (Directory.Exists(employeeFolder))
                {
                    Directory.Delete(employeeFolder, true);
                }
                App.AdminMirrorSyncService?.EnqueueEmployeeDelete(firmName, data);
                DeleteEmployeeIndex(employeeId);
                LoggingService.LogInfo("EmployeeService", $"Employee deleted: {employeeFolder}");
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.DeleteEmployee", ex);
                return false;
            }
        }

        public string SaveCustomDocument(string employeeFolder, string sourcePath, string baseName)
        {
            try
            {
                var customDocsFolder = Path.Combine(employeeFolder, "CustomDocs");
                Directory.CreateDirectory(customDocsFolder);
                var ext = Path.GetExtension(sourcePath).ToLower();
                if (ext == ".pdf")
                {
                    var destPdf = Path.Combine(customDocsFolder, $"{baseName}.pdf");
                    SafeFileService.CopyFile(sourcePath, destPdf);
                    return Path.GetFileName(destPdf);
                }
                var destImage = Path.Combine(customDocsFolder, $"{baseName}.jpg");
                ConvertImageToJpg(sourcePath, destImage);
                return Path.GetFileName(destImage);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.SaveCustomDocument", ex);
                return string.Empty;
            }
        }

        public string GetCustomDocPath(string employeeFolder, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return string.Empty;
            var path = Path.Combine(employeeFolder, "CustomDocs", fileName);
            return File.Exists(path) ? path : string.Empty;
        }

        public void DeleteCustomDocFile(string employeeFolder, string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            try
            {
                var path = Path.Combine(employeeFolder, "CustomDocs", fileName);
                SafeFileService.DeleteFile(path);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.DeleteCustomDocFile", ex);
            }
        }

        public string SaveDocumentFromSource(string sourcePath, string employeeFolder, string baseName)
        {
            var ext = Path.GetExtension(sourcePath).ToLower();
            if (ext == ".pdf")
            {
                var destPdf = Path.Combine(employeeFolder, $"{baseName}.pdf");
                SafeFileService.CopyFile(sourcePath, destPdf);
                return Path.GetFileName(destPdf);
            }

            var destImage = Path.Combine(employeeFolder, $"{baseName}.jpg");
            ConvertImageToJpg(sourcePath, destImage);
            return Path.GetFileName(destImage);
        }

        public string CreateCroppedPhoto(string sourcePath, Int32Rect rect, string outputPath)
        {
            var bitmap = LoadBitmap(sourcePath);
            var cropped = new CroppedBitmap(bitmap, rect);
            SaveJpeg(cropped, outputPath);
            return outputPath;
        }

        public string RotateImage(string sourcePath, int angle, string outputPath)
        {
            var bitmap = LoadBitmap(sourcePath);
            var rotated = new TransformedBitmap(bitmap, new RotateTransform(angle));
            SaveJpeg(rotated, outputPath);
            return outputPath;
        }

        private string SaveDocument(EmployeeDocumentTemp? doc, string employeeFolder, string baseName)
        {
            if (doc == null) return string.Empty;

            if (doc.IsPdf)
            {
                if (string.IsNullOrEmpty(doc.PdfPath))
                    return string.Empty;
                if (!File.Exists(doc.PdfPath))
                {
                    LoggingService.LogWarning("EmployeeService.SaveDocument", $"PDF source file not found: {doc.PdfPath}");
                    return string.Empty;
                }
                var pdfPath = Path.Combine(employeeFolder, $"{baseName}.pdf");
                SafeFileService.CopyFile(doc.PdfPath, pdfPath);
                return Path.GetFileName(pdfPath);
            }

            if (string.IsNullOrEmpty(doc.ImagePath))
                return string.Empty;
            if (!File.Exists(doc.ImagePath))
            {
                LoggingService.LogWarning("EmployeeService.SaveDocument", $"Image source file not found: {doc.ImagePath}");
                return string.Empty;
            }
            var jpgPath = Path.Combine(employeeFolder, $"{baseName}.jpg");
            SafeFileService.CopyFile(doc.ImagePath, jpgPath);
            return Path.GetFileName(jpgPath);
        }

        private static void TryDeleteIncompleteEmployeeFolder(string employeeFolder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(employeeFolder) || !Directory.Exists(employeeFolder))
                    return;

                if (File.Exists(Path.Combine(employeeFolder, "employee.json")))
                    return;

                TryDeleteDirectory(employeeFolder);
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeeService.TryDeleteIncompleteEmployeeFolder", ex.Message);
            }
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        private static void NotifySaveFailure(string message, string? details = null)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var fullMessage = string.IsNullOrWhiteSpace(details)
                ? message
                : $"{message} {details}";

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (Application.Current?.MainWindow?.IsVisible == true)
                {
                    ToastService.Instance.Error(fullMessage);
                    return;
                }

                MessageBox.Show(fullMessage, Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private static void NotifyOperationFailure(string message, string? details = null)
        {
            NotifySaveFailure(message, details);
        }

        private void ConvertImageToJpg(string inputPath, string outputPath)
        {
            var bitmap = LoadBitmap(inputPath);
            SaveJpeg(bitmap, outputPath);
        }

        private static BitmapSource LoadBitmap(string inputPath)
        {
            using var stream = File.OpenRead(inputPath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return decoder.Frames[0];
        }

        private static void SaveJpeg(BitmapSource source, string outputPath)
        {
            var encoder = new JpegBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = File.Open(outputPath, FileMode.Create, FileAccess.Write);
            encoder.Save(stream);
        }

        private EmployeeSummary BuildSummary(string firmName, string employeeFolder, EmployeeData data)
        {
            var photoPath = ResolvePhotoPath(employeeFolder, data);
            if (string.IsNullOrEmpty(data.UniqueId))
            {
                data.UniqueId = Guid.NewGuid().ToString();
                if (!SaveEmployeeData(employeeFolder, data, notifyUser: false))
                    LoggingService.LogWarning("EmployeeService.BuildSummary.PersistId", $"Failed to persist generated UniqueId for {employeeFolder}");
            }

            return new EmployeeSummary
            {
                UniqueId = data.UniqueId,
                FullName = $"{data.FirstName} {data.LastName}",
                PositionTitle = data.PositionTag,
                StartDate = data.StartDate,
                EndDate = data.EndDate,
                ContractType = data.ContractType,
                PhotoPath = File.Exists(photoPath) ? photoPath : string.Empty,
                HasPhoto = File.Exists(photoPath),
                HasPassport = !string.IsNullOrEmpty(data.Files.Passport),
                HasVisa = !string.IsNullOrEmpty(data.Files.Visa),
                HasInsurance = !string.IsNullOrEmpty(data.Files.Insurance),
                PassportNumber = data.PassportNumber,
                VisaNumber = data.VisaNumber,
                InsuranceNumber = data.InsuranceNumber,
                PassportExpiry = data.PassportExpiry,
                VisaExpiry = data.VisaExpiry,
                InsuranceExpiry = data.InsuranceExpiry,
                PassportSeverity = DateParsingHelper.GetSeverity(data.PassportExpiry),
                VisaSeverity = DateParsingHelper.GetSeverity(data.VisaExpiry),
                InsuranceSeverity = DateParsingHelper.GetSeverity(data.InsuranceExpiry),
                WorkPermitSeverity = DateParsingHelper.GetSeverity(data.WorkPermitExpiry),
                Status = StatusHelper.Normalize(data.Status),
                Phone = data.Phone,
                Email = data.Email,
                BankAccountNumber = data.HasBankAccountData ? data.BankAccountNumber : string.Empty,
                BankName = data.HasBankAccountData ? data.BankName : string.Empty,
                EmployeeFolder = employeeFolder,
                FirmName = firmName,
                EmployeeType = data.EmployeeType ?? "visa",
                WorkPermitName = data.WorkPermitName ?? string.Empty,
                WorkPermitExpiry = data.WorkPermitExpiry
            };
        }

        private static EmployeeSummary BuildSummaryFromIndexRow(EmployeeIndexRow row)
        {
            var employeeFolder = ResolveEmployeeFolderFromIndexRow(row);
            var photoPath = ResolvePhotoPathFromIndexRow(row, employeeFolder);
            var hasPhoto = !string.IsNullOrWhiteSpace(photoPath);
            return new EmployeeSummary
            {
                UniqueId = row.UniqueId,
                FullName = row.FullName,
                PositionTitle = row.PositionTitle,
                StartDate = row.StartDate,
                EndDate = row.EndDate,
                ContractType = row.ContractType,
                PhotoPath = hasPhoto ? photoPath : string.Empty,
                HasPhoto = hasPhoto,
                HasPassport = row.HasPassport,
                HasVisa = row.HasVisa,
                HasInsurance = row.HasInsurance,
                PassportNumber = row.PassportNumber,
                VisaNumber = row.VisaNumber,
                InsuranceNumber = row.InsuranceNumber,
                PassportExpiry = row.PassportExpiry,
                VisaExpiry = row.VisaExpiry,
                InsuranceExpiry = row.InsuranceExpiry,
                PassportSeverity = DateParsingHelper.GetSeverity(row.PassportExpiry),
                VisaSeverity = DateParsingHelper.GetSeverity(row.VisaExpiry),
                InsuranceSeverity = DateParsingHelper.GetSeverity(row.InsuranceExpiry),
                WorkPermitSeverity = DateParsingHelper.GetSeverity(row.WorkPermitExpiry),
                Status = StatusHelper.Normalize(row.Status),
                Phone = row.Phone,
                Email = row.Email,
                BankAccountNumber = row.BankAccountNumber,
                BankName = row.BankName,
                EmployeeFolder = employeeFolder,
                FirmName = row.FirmName,
                EmployeeType = row.EmployeeType ?? "visa",
                WorkPermitName = row.WorkPermitName ?? string.Empty,
                WorkPermitExpiry = row.WorkPermitExpiry ?? string.Empty
            };
        }

        private static ArchivedEmployeeSummary BuildArchivedSummaryFromIndexRow(EmployeeIndexRow row)
        {
            var employeeFolder = ResolveEmployeeFolderFromIndexRow(row);
            var photoPath = ResolvePhotoPathFromIndexRow(row, employeeFolder);
            var hasPhoto = !string.IsNullOrWhiteSpace(photoPath);
            return new ArchivedEmployeeSummary
            {
                UniqueId = row.UniqueId,
                FullName = row.FullName,
                PositionTitle = row.PositionTitle,
                FirmName = row.FirmName,
                StartDate = row.StartDate,
                EndDate = row.EndDate,
                EmployeeFolder = employeeFolder,
                PhotoPath = hasPhoto ? photoPath : string.Empty,
                HasPhoto = hasPhoto
            };
        }

        private static string ResolveEmployeeFolderFromIndexRow(EmployeeIndexRow row)
        {
            var employeeFolder = row.EmployeeFolder ?? string.Empty;
            return App.FinanceService?.ResolveEmployeeFolder(employeeFolder, row.UniqueId) ?? employeeFolder;
        }

        private static string ResolvePhotoPathFromIndexRow(EmployeeIndexRow row, string employeeFolder)
        {
            var photoPath = row.PhotoPath ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath))
                return photoPath;

            if (string.IsNullOrWhiteSpace(employeeFolder) || !Directory.Exists(employeeFolder))
                return string.Empty;

            if (!string.IsNullOrWhiteSpace(photoPath))
            {
                var fileName = Path.GetFileName(photoPath);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var rebasedPath = Path.Combine(employeeFolder, fileName);
                    if (File.Exists(rebasedPath))
                        return rebasedPath;
                }
            }

            var fallbackFileName = $"{row.FirstName} {row.LastName} - Photo.jpg".Trim();
            if (!string.IsNullOrWhiteSpace(fallbackFileName))
            {
                var fallbackPath = Path.Combine(employeeFolder, fallbackFileName);
                if (File.Exists(fallbackPath))
                    return fallbackPath;
            }

            return string.Empty;
        }

        private static bool HasAnyEmployeeFolders(string employeesFolder)
        {
            return Directory.Exists(employeesFolder)
                && Directory.GetDirectories(employeesFolder).Length > 0;
        }

        private List<EmployeeSummary> GetEmployeesForFirmFromFiles(string firmName, string employeesFolder)
        {
            var summaries = new List<EmployeeSummary>();
            foreach (var folder in Directory.GetDirectories(employeesFolder))
            {
                var jsonPath = Path.Combine(folder, "employee.json");
                if (!File.Exists(jsonPath))
                {
                    Debug.WriteLine($"EmployeeService.GetEmployeesForFirm: missing {jsonPath}");
                    continue;
                }

                try
                {
                    var data = ReadJson<EmployeeData>(jsonPath);
                    if (data == null)
                        continue;

                    _tagCatalogService.AddTagsForEmployee(firmName, data);
                    summaries.Add(BuildSummary(firmName, folder, data));
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("EmployeeService.GetEmployeesForFirm", ex);
                }
            }

            Debug.WriteLine($"EmployeeService.GetEmployeesForFirm: {summaries.Count} items for {firmName}");
            return summaries;
        }

        private (List<EmployeeSummary> Employees, string Status) GetEmployeesForFirmWithStatusFromFiles(string firmName, string employeesFolder)
        {
            var summaries = GetEmployeesForFirmFromFiles(firmName, employeesFolder);
            if (summaries.Count == 0)
                return (summaries, "NoEmployees");

            return (summaries, "Ok");
        }

        private List<ArchivedEmployeeSummary> GetArchivedEmployeesFromFiles(string archiveFolder)
        {
            var result = new List<ArchivedEmployeeSummary>();
            foreach (var folder in Directory.GetDirectories(archiveFolder))
            {
                var jsonPath = Path.Combine(folder, "employee.json");
                if (!File.Exists(jsonPath))
                    continue;

                try
                {
                    var data = ReadJson<EmployeeData>(jsonPath);
                    if (data == null)
                        continue;

                    var fullName = $"{data.FirstName} {data.LastName}";
                    var photo = ResolvePhotoPath(folder, data);
                    var hasPhoto = !string.IsNullOrEmpty(photo);

                    if (data.FirmHistory != null && data.FirmHistory.Count > 0)
                    {
                        var deduplicated = data.FirmHistory
                            .GroupBy(fh => $"{fh.FirmName}|{fh.StartDate}")
                            .Select(g => g.Last())
                            .ToList();

                        bool needResave = deduplicated.Count != data.FirmHistory.Count;
                        if (needResave)
                        {
                            data.FirmHistory = deduplicated;
                            try
                            {
                                var resavePath = Path.Combine(folder, "employee.json");
                                WriteJsonAtomic(resavePath, data);
                            }
                            catch (Exception ex2)
                            {
                                LoggingService.LogWarning("GetArchivedEmployees.Dedup", ex2.Message);
                            }
                        }

                        var last = deduplicated.Last();
                        result.Add(new ArchivedEmployeeSummary
                        {
                            UniqueId = data.UniqueId,
                            FullName = fullName,
                            PositionTitle = data.PositionTag,
                            FirmName = last.FirmName,
                            StartDate = last.StartDate,
                            EndDate = last.EndDate,
                            EmployeeFolder = folder,
                            PhotoPath = photo,
                            HasPhoto = hasPhoto
                        });
                    }
                    else
                    {
                        result.Add(new ArchivedEmployeeSummary
                        {
                            UniqueId = data.UniqueId,
                            FullName = fullName,
                            PositionTitle = data.PositionTag,
                            FirmName = data.ArchivedFromFirm,
                            StartDate = data.StartDate,
                            EndDate = data.EndDate,
                            EmployeeFolder = folder,
                            PhotoPath = photo,
                            HasPhoto = hasPhoto
                        });
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("EmployeeService.GetArchivedEmployees", ex);
                }
            }

            return result;
        }

        private EmployeeIndexRow BuildEmployeeIndexRow(EmployeeData data, string firmName, string employeeFolder)
        {
            var photoPath = ResolvePhotoPath(employeeFolder, data);
            var hasPhoto = !string.IsNullOrWhiteSpace(photoPath) && File.Exists(photoPath);
            return new EmployeeIndexRow
            {
                UniqueId = data.UniqueId ?? string.Empty,
                FullName = $"{data.FirstName} {data.LastName}".Trim(),
                FirstName = data.FirstName ?? string.Empty,
                LastName = data.LastName ?? string.Empty,
                FirmName = firmName ?? string.Empty,
                EmployeeFolder = employeeFolder ?? string.Empty,
                EmployeeType = data.EmployeeType ?? "visa",
                Status = StatusHelper.Normalize(data.Status),
                StartDate = data.StartDate ?? string.Empty,
                EndDate = data.EndDate ?? string.Empty,
                ContractType = data.ContractType ?? string.Empty,
                PositionTitle = data.PositionTag ?? string.Empty,
                PositionNumber = data.PositionNumber ?? string.Empty,
                Phone = data.Phone ?? string.Empty,
                Email = data.Email ?? string.Empty,
                PassportNumber = data.PassportNumber ?? string.Empty,
                VisaNumber = data.VisaNumber ?? string.Empty,
                InsuranceNumber = data.InsuranceNumber ?? string.Empty,
                PassportExpiry = data.PassportExpiry ?? string.Empty,
                VisaExpiry = data.VisaExpiry ?? string.Empty,
                InsuranceExpiry = data.InsuranceExpiry ?? string.Empty,
                WorkPermitName = data.WorkPermitName ?? string.Empty,
                WorkPermitExpiry = data.WorkPermitExpiry ?? string.Empty,
                BankAccountNumber = data.HasBankAccountData ? data.BankAccountNumber ?? string.Empty : string.Empty,
                BankName = data.HasBankAccountData ? data.BankName ?? string.Empty : string.Empty,
                IsArchived = data.IsArchived,
                ArchivedFromFirm = data.ArchivedFromFirm ?? string.Empty,
                PhotoPath = hasPhoto ? photoPath : string.Empty,
                HasPhoto = hasPhoto,
                HasPassport = !string.IsNullOrEmpty(data.Files.Passport),
                HasVisa = !string.IsNullOrEmpty(data.Files.Visa),
                HasInsurance = !string.IsNullOrEmpty(data.Files.Insurance),
                UpdatedAt = DateTime.UtcNow.ToString("O")
            };
        }

        private void UpsertEmployeeIndex(string employeeFolder, EmployeeData data, string? firmNameOverride = null)
        {
            if (_employeeIndexDbService == null || data == null)
                return;

            if (string.IsNullOrWhiteSpace(data.UniqueId))
                data.UniqueId = Guid.NewGuid().ToString();

            var firmName = firmNameOverride;
            if (string.IsNullOrWhiteSpace(firmName))
            {
                if (data.IsArchived && !string.IsNullOrWhiteSpace(data.ArchivedFromFirm))
                    firmName = data.ArchivedFromFirm;
                else
                    firmName = App.AdminMirrorSyncService?.InferFirmNameFromEmployeeFolder(employeeFolder) ?? ResolveFirmNameForHistory(employeeFolder);
            }

            _employeeIndexDbService.UpsertEmployeeIndex(BuildEmployeeIndexRow(data, firmName ?? string.Empty, employeeFolder));
        }

        private void DeleteEmployeeIndex(string? uniqueId)
        {
            if (_employeeIndexDbService == null || string.IsNullOrWhiteSpace(uniqueId))
                return;

            _employeeIndexDbService.DeleteEmployeeIndex(uniqueId);
        }

        public void RemoveEmployeeIndexEntry(string? uniqueId)
        {
            DeleteEmployeeIndex(uniqueId);
        }

        public void SyncEmployeeIndexForFolder(string employeeFolder, string? firmName = null)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder))
                return;

            var data = LoadEmployeeData(employeeFolder);
            if (data == null)
                return;

            UpsertEmployeeIndex(employeeFolder, data, firmName);
        }

        // ============ DUPLICATE RESOLUTION ============

        /// <summary>
        /// Resolves the correct employee folder, handling duplicates:
        /// - If folder doesn't exist → use it (new employee)
        /// - If folder exists and BirthDate matches → reuse it (update existing)
        /// - If folder exists but BirthDate differs → append .1, .2, etc. (different person)
        /// </summary>
        private string ResolveEmployeeFolder(string employeesFolder, EmployeeData data)
        {
            var baseName = NormalizeFolderName($"{data.FirstName}_{data.LastName} - {data.StartDate}");
            var basePath = Path.Combine(employeesFolder, baseName);

            // Case 1: folder doesn't exist → new employee
            if (!Directory.Exists(basePath))
                return basePath;

            // Case 2: folder exists → check if same person (by BirthDate)
            if (IsSameEmployee(basePath, data))
                return basePath;

            // Case 3: folder exists but different person → find next available suffix
            for (int i = 1; i < 100; i++)
            {
                var suffixedPath = Path.Combine(employeesFolder, $"{baseName}.{i}");

                if (!Directory.Exists(suffixedPath))
                    return suffixedPath;

                if (IsSameEmployee(suffixedPath, data))
                    return suffixedPath;
            }

            // Fallback: should never happen
            return basePath;
        }

        /// <summary>
        /// Checks if the employee in the given folder has the same BirthDate as the incoming data.
        /// </summary>
        private static bool IsSameEmployee(string folderPath, EmployeeData newData)
        {
            try
            {
                var jsonPath = Path.Combine(folderPath, "employee.json");
                if (!File.Exists(jsonPath)) return false;

                var existing = ReadJson<EmployeeData>(jsonPath);
                if (existing == null) return false;

                return string.Equals(existing.BirthDate?.Trim(), newData.BirthDate?.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.IsSameEmployee", ex);
                return false;
            }
        }

        // ============ ARCHIVE LOG ============

        private string GetArchiveLogPath()
        {
            var archiveFolder = _folderService.GetArchiveFolder();
            if (string.IsNullOrEmpty(archiveFolder)) return string.Empty;
            Directory.CreateDirectory(archiveFolder);
            return Path.Combine(archiveFolder, "archive_log.json");
        }

        public LocalDbMigrationResult EnsureArchiveLogMigratedToLocalDb()
        {
            try
            {
                if (_localDbService == null)
                    return new LocalDbMigrationResult { Message = "LocalDbService is not configured." };

                var path = GetArchiveLogPath();
                if (string.IsNullOrWhiteSpace(path))
                    return new LocalDbMigrationResult { Message = "Archive log path is not available." };

                var result = _localDbService.MigrateArchiveLogIfNeeded(path, LoadArchiveLogEntries(path));
                _useLocalDbArchiveLog = result.IsSuccessful;
                return result;
            }
            catch (Exception ex)
            {
                _useLocalDbArchiveLog = false;
                LoggingService.LogError("EmployeeService.EnsureArchiveLogMigratedToLocalDb", ex);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    Message = ex.Message
                };
            }
        }

        public EmployeeIndexRebuildResult EnsureEmployeeIndexBuilt()
        {
            try
            {
                if (_employeeIndexDbService == null)
                    return new EmployeeIndexRebuildResult { Message = "EmployeeIndexDbService is not configured." };

                if (_employeeIndexDbService.HasAnyRows())
                {
                    if (_employeeIndexDbService.HasLegacyAbsolutePaths())
                    {
                        LoggingService.LogWarning("EmployeeService.EnsureEmployeeIndexBuilt",
                            "Employee index contains legacy absolute paths. Rebuilding index for current root.");
                        return RebuildEmployeeIndex();
                    }

                    var existingCount = _employeeIndexDbService.GetEmployeeIndexCount();
                    return new EmployeeIndexRebuildResult
                    {
                        WasRebuildAttempted = false,
                        IsSuccessful = true,
                        RecordsFound = existingCount,
                        RecordsImported = existingCount,
                        Message = "Employee index already exists."
                    };
                }

                return RebuildEmployeeIndex();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.EnsureEmployeeIndexBuilt", ex);
                return new EmployeeIndexRebuildResult
                {
                    WasRebuildAttempted = true,
                    IsSuccessful = false,
                    Message = ex.Message
                };
            }
        }

        public EmployeeIndexRebuildResult RebuildEmployeeIndex()
        {
            if (_employeeIndexDbService == null)
                return new EmployeeIndexRebuildResult { Message = "EmployeeIndexDbService is not configured." };

            var foldersScanned = 0;
            var foldersSkipped = 0;
            var rows = new List<EmployeeIndexRow>();

            foreach (var source in EnumerateEmployeeFolders())
            {
                foldersScanned++;
                var jsonPath = Path.Combine(source.EmployeeFolder, "employee.json");
                if (!File.Exists(jsonPath))
                {
                    LoggingService.LogWarning("EmployeeService.RebuildEmployeeIndex",
                        $"Skipped (no employee.json): {source.EmployeeFolder} [firm: {source.FirmName}]");
                    foldersSkipped++;
                    continue;
                }

                var data = LoadEmployeeData(source.EmployeeFolder);
                if (data == null)
                {
                    LoggingService.LogWarning("EmployeeService.RebuildEmployeeIndex",
                        $"Skipped (unreadable employee.json): {source.EmployeeFolder} [firm: {source.FirmName}]");
                    foldersSkipped++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(data.UniqueId))
                {
                    data.UniqueId = Guid.NewGuid().ToString();
                    if (!SaveEmployeeData(source.EmployeeFolder, data, notifyUser: false))
                    {
                        LoggingService.LogWarning("EmployeeService.RebuildEmployeeIndex",
                            $"Skipped (could not save generated UniqueId): {source.EmployeeFolder} [firm: {source.FirmName}]");
                        foldersSkipped++;
                        continue;
                    }
                }

                _tagCatalogService.AddTagsForEmployee(source.FirmName, data);
                rows.Add(BuildEmployeeIndexRow(data, source.FirmName, source.EmployeeFolder));
            }

            return _employeeIndexDbService.RebuildEmployeeIndex(rows, _localDbService, foldersScanned, foldersSkipped);
        }

        public List<ArchiveLogEntry> LoadArchiveLog()
        {
            try
            {
                if (_useLocalDbArchiveLog && _localDbService != null)
                    return _localDbService.GetAllArchiveLogs();

                var path = GetArchiveLogPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return new List<ArchiveLogEntry>();
                return LoadArchiveLogEntries(path);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.LoadArchiveLog", ex);
                return new List<ArchiveLogEntry>();
            }
        }

        private static List<ArchiveLogEntry> LoadArchiveLogEntries(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return new List<ArchiveLogEntry>();

            try
            {
                var raw = SafeFileService.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    LoggingService.LogWarning("EmployeeService.LoadArchiveLogEntries",
                        $"archive_log.json is empty and will be treated as an empty log: {path}");
                    return new List<ArchiveLogEntry>();
                }

                return ReadJsonOrDefault(path, new List<ArchiveLogEntry>());
            }
            catch (JsonException ex)
            {
                LoggingService.LogWarning("EmployeeService.LoadArchiveLogEntries",
                    $"archive_log.json is invalid and will be treated as an empty log: {path}. {ex.Message}");
                return new List<ArchiveLogEntry>();
            }
        }

        private async Task AppendArchiveLog(ArchiveLogEntry entry)
        {
            await _historyLock.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(entry.OperationId))
                    entry.OperationId = Guid.NewGuid().ToString();

                if (_useLocalDbArchiveLog && _localDbService != null)
                {
                    _localDbService.InsertArchiveLog(entry);
                    return;
                }

                var path = GetArchiveLogPath();
                if (string.IsNullOrEmpty(path)) return;

                var entries = LoadArchiveLogEntries(path);
                entries.Add(entry);
                WriteJsonAtomic(path, entries);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppendArchiveLog error: {ex.Message}");
                LoggingService.LogError("EmployeeService.AppendArchiveLog", ex);
            }
            finally
            {
                _historyLock.Release();
            }
        }

        // ============ ARCHIVE ============

        public List<ArchivedEmployeeSummary> GetArchivedEmployees()
        {
            var archiveFolder = _folderService.GetArchiveFolder();
            if (string.IsNullOrEmpty(archiveFolder) || !Directory.Exists(archiveFolder))
                return new List<ArchivedEmployeeSummary>();

            if (_employeeIndexDbService != null)
            {
                try
                {
                    var rows = _employeeIndexDbService.GetArchivedEmployeeRows();
                    if (rows.Count > 0)
                    {
                        return rows
                            .Select(BuildArchivedSummaryFromIndexRow)
                            .ToList();
                    }

                    if (!HasAnyEmployeeFolders(archiveFolder))
                        return new List<ArchivedEmployeeSummary>();

                    LoggingService.LogWarning("EmployeeService.GetArchivedEmployees",
                        "Employee index returned no archived rows while archive folders exist. Falling back to file scan.");
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("EmployeeService.GetArchivedEmployees",
                        $"Employee index read failed for archive list, falling back to file scan. {ex.Message}");
                }
            }

            return GetArchivedEmployeesFromFiles(archiveFolder);
        }

        public List<ArchivedEmployeeSummary> GetActiveEmployeeFirmHistory()
        {
            var result = new List<ArchivedEmployeeSummary>();
            foreach (var company in App.CompanyService.Companies)
            {
                var employeesFolder = _folderService.GetEmployeesFolder(company.Name);
                if (string.IsNullOrEmpty(employeesFolder) || !Directory.Exists(employeesFolder))
                    continue;

                foreach (var folder in Directory.GetDirectories(employeesFolder))
                {
                    var jsonPath = Path.Combine(folder, "employee.json");
                    if (!File.Exists(jsonPath)) continue;
                    try
                    {
                        var data = ReadJson<EmployeeData>(jsonPath);
                        if (data == null || data.FirmHistory == null || data.FirmHistory.Count == 0) continue;

                        var fullName = $"{data.FirstName} {data.LastName}";
                        var photo = ResolvePhotoPath(folder, data);
                        var hasPhoto = !string.IsNullOrEmpty(photo);
                        var deduplicated = data.FirmHistory
                            .Where(fh => fh.FirmName != company.Name)
                            .GroupBy(fh => $"{fh.FirmName}|{fh.StartDate}")
                            .Select(g => g.Last())
                            .ToList();

                        foreach (var fh in deduplicated)
                        {
                            result.Add(new ArchivedEmployeeSummary
                            {
                                UniqueId = data.UniqueId,
                                FullName = fullName,
                                PositionTitle = data.PositionTag,
                                FirmName = fh.FirmName,
                                StartDate = fh.StartDate,
                                EndDate = fh.EndDate,
                                EmployeeFolder = folder,
                                PhotoPath = photo,
                                HasPhoto = hasPhoto
                            });
                        }
                    }
                    catch (Exception ex) { LoggingService.LogError("EmployeeService.GetActiveEmployeeFirmHistory", ex); }
                }
            }
            return result;
        }

        public async Task<RestoreEmployeeResult> RestoreFromArchive(string archiveEmployeeFolder, string newFirmName, string newStartDate, string newContractSignDate, string positionTag, string positionNumber, string workAddressTag)
        {
            try
            {
                var destFolder = await RestoreArchivedEmployeeCore(
                    archiveEmployeeFolder,
                    newFirmName,
                    newStartDate,
                    newContractSignDate,
                    positionTag,
                    positionNumber,
                    workAddressTag);

                if (string.IsNullOrWhiteSpace(destFolder))
                    return new RestoreEmployeeResult();

                var restoredData = LoadEmployeeData(destFolder);
                var opId = Guid.NewGuid().ToString();
                await AppendArchiveLog(new ArchiveLogEntry
                {
                    OperationId = opId,
                    EmployeeName = restoredData == null
                        ? Path.GetFileName(destFolder)
                        : $"{restoredData.FirstName} {restoredData.LastName}",
                    FirmName = newFirmName,
                    EmployeeFolder = destFolder,
                    Action = "Restored",
                    Date = newStartDate,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });

                LoggingService.LogInfo("EmployeeService", $"Employee restored to {newFirmName}: {destFolder}");
                App.AdminMirrorSyncService?.EnqueueEmployeeUpsert(newFirmName, destFolder, restoredData);
                if (restoredData != null)
                    UpsertEmployeeIndex(destFolder, restoredData, newFirmName);
                App.FinanceService?.CleanupGhostFolders();
                return new RestoreEmployeeResult
                {
                    RestoredFolder = destFolder,
                    OperationId = opId
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreFromArchive error: {ex.Message}");
                LoggingService.LogError("EmployeeService.RestoreFromArchive", ex);
                return new RestoreEmployeeResult();
            }
        }

        public async Task<UndoArchiveResult> UndoArchiveAsync(string operationId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(operationId))
                    return new UndoArchiveResult();

                ArchiveLogEntry? target;
                if (_useLocalDbArchiveLog && _localDbService != null)
                {
                    target = _localDbService.GetActiveArchiveLogEntry(operationId);
                }
                else
                {
                    var archiveEntries = LoadArchiveLog();
                    target = archiveEntries.FirstOrDefault(entry =>
                        string.Equals(entry.OperationId, operationId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(entry.Action, "Archived", StringComparison.OrdinalIgnoreCase)
                        && !entry.IsReverted);
                }

                if (target == null)
                    return new UndoArchiveResult();

                var archiveFolderPath = !string.IsNullOrWhiteSpace(target.EmployeeFolder) && Directory.Exists(target.EmployeeFolder)
                    ? target.EmployeeFolder
                    : Path.Combine(_folderService.GetArchiveFolder(), Path.GetFileName(target.EmployeeFolder ?? string.Empty));
                if (string.IsNullOrWhiteSpace(archiveFolderPath) || !Directory.Exists(archiveFolderPath))
                {
                    LoggingService.LogWarning("EmployeeService.UndoArchiveAsync",
                        $"Archive folder was not found for operation {operationId}");
                    return new UndoArchiveResult();
                }

                var restoredFolder = await RestoreArchivedEmployeeCore(
                    archiveFolderPath,
                    target.FirmName,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    string.Empty,
                    isUndo: true);
                if (string.IsNullOrWhiteSpace(restoredFolder))
                    return new UndoArchiveResult();

                var undoOperationId = Guid.NewGuid().ToString();
                await MarkArchiveLogReverted(operationId, undoOperationId);

                await AddHistoryEntry(restoredFolder, new EmployeeHistoryEntry
                {
                    EventType = "ArchiveUndone",
                    Action = Res("HistoryActionArchiveUndone"),
                    Field = target.FirmName,
                    Description = string.Format(Res("HistoryDescArchiveUndone"), target.FirmName)
                });

                var restoredData = LoadEmployeeData(restoredFolder);
                App.AdminMirrorSyncService?.EnqueueEmployeeUpsert(target.FirmName, restoredFolder, restoredData);
                if (restoredData != null)
                    UpsertEmployeeIndex(restoredFolder, restoredData, target.FirmName);
                return new UndoArchiveResult
                {
                    RestoredFolder = restoredFolder,
                    UndoOperationId = undoOperationId
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.UndoArchiveAsync", ex);
                return new UndoArchiveResult();
            }
        }

        private async Task<string> RestoreArchivedEmployeeCore(
            string archiveEmployeeFolder,
            string targetFirmName,
            string newStartDate,
            string newContractSignDate,
            string positionTag,
            string positionNumber,
            string workAddressTag,
            bool isUndo = false)
        {
            if (string.IsNullOrWhiteSpace(archiveEmployeeFolder) || !Directory.Exists(archiveEmployeeFolder))
            {
                LoggingService.LogWarning("EmployeeService.RestoreArchivedEmployeeCore",
                    $"Archive employee folder not found: {archiveEmployeeFolder}");
                NotifyOperationFailure(Res("MsgRestoreSourceMissing"));
                return string.Empty;
            }

            var jsonPath = Path.Combine(archiveEmployeeFolder, "employee.json");
            if (!File.Exists(jsonPath))
            {
                LoggingService.LogWarning("EmployeeService.RestoreArchivedEmployeeCore",
                    $"Archive employee.json not found: {jsonPath}");
                NotifyOperationFailure(Res("MsgRestoreSourceMissing"));
                return string.Empty;
            }

            var data = ReadJson<EmployeeData>(jsonPath);
            if (data == null)
            {
                LoggingService.LogWarning("EmployeeService.RestoreArchivedEmployeeCore",
                    $"Could not read archived employee data: {jsonPath}");
                NotifyOperationFailure(Res("MsgRestoreError"));
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(data.UniqueId))
                data.UniqueId = Guid.NewGuid().ToString();

            if (!isUndo)
            {
                data.StartDate = newStartDate;
                data.ContractSignDate = newContractSignDate;
                data.PositionTag = positionTag;
                data.PositionNumber = positionNumber;
                data.WorkAddressTag = workAddressTag;
            }

            data.EndDate = string.Empty;
            data.IsArchived = false;
            data.ArchivedFromFirm = string.Empty;
            data.Status = "Active";

            if (isUndo && !string.IsNullOrWhiteSpace(targetFirmName))
            {
                data.FirmHistory ??= new List<FirmHistoryEntry>();
                var latestFirmEntry = data.FirmHistory.LastOrDefault(fh =>
                    string.Equals(fh.FirmName, targetFirmName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(fh.StartDate, data.StartDate, StringComparison.OrdinalIgnoreCase));
                if (latestFirmEntry != null)
                    latestFirmEntry.EndDate = string.Empty;
            }

            var employeesFolder = _folderService.GetEmployeesFolder(targetFirmName);
            if (string.IsNullOrEmpty(employeesFolder))
            {
                LoggingService.LogWarning("EmployeeService.RestoreArchivedEmployeeCore",
                    $"Employees folder path is empty for firm '{targetFirmName}'");
                NotifyOperationFailure(Res("MsgEmployeesRootMissing"));
                return string.Empty;
            }

            Directory.CreateDirectory(employeesFolder);

            var existingFolderWithSameId = FindEmployeeFolderByUniqueId(employeesFolder, data.UniqueId);
            if (!string.IsNullOrWhiteSpace(existingFolderWithSameId))
            {
                LoggingService.LogWarning("EmployeeService.RestoreArchivedEmployeeCore",
                    $"Restore blocked because employee with the same UniqueId already exists: {existingFolderWithSameId}");
                NotifyOperationFailure(Res("MsgRestoreConflict"));
                return string.Empty;
            }

            var destFolder = ResolveAvailableFolder(employeesFolder,
                NormalizeFolderName($"{data.FirstName}_{data.LastName} - {data.StartDate}"));
            if (string.IsNullOrWhiteSpace(destFolder))
            {
                LoggingService.LogWarning("EmployeeService.RestoreArchivedEmployeeCore",
                    $"Could not resolve destination folder for {data.FirstName} {data.LastName} in {employeesFolder}");
                NotifyOperationFailure(Res("MsgRestoreError"));
                return string.Empty;
            }

            CopyDirectory(archiveEmployeeFolder, destFolder);

            var restoredJsonPath = Path.Combine(destFolder, "employee.json");
            WriteJsonAtomic(restoredJsonPath, data);

            var verify = ReadJson<EmployeeData>(restoredJsonPath);
            if (verify == null || string.IsNullOrEmpty(verify.FirstName) || !string.Equals(verify.UniqueId, data.UniqueId, StringComparison.OrdinalIgnoreCase))
            {
                LoggingService.LogError("EmployeeService.RestoreArchivedEmployeeCore", new InvalidOperationException(
                    $"Restored employee.json validation failed for {restoredJsonPath}"));
                TryDeleteDirectory(destFolder);
                NotifyOperationFailure(Res("MsgRestoreError"));
                return string.Empty;
            }

            TryCleanupDeferredDirectory(archiveEmployeeFolder);
            if (Directory.Exists(archiveEmployeeFolder))
            {
                LoggingService.LogWarning("EmployeeService.RestoreArchivedEmployeeCore",
                    $"Archive folder still exists after restore, scheduling cleanup: {archiveEmployeeFolder}");
                await PendingCleanupService.EnqueueAsync(archiveEmployeeFolder, isUndo ? "undo-archive-folder" : "restore-archive-folder");
                ScheduleDeferredCleanupRetry(archiveEmployeeFolder, "EmployeeService.RestoreArchivedEmployeeCore");
            }

            return destFolder;
        }

        private async Task MarkArchiveLogReverted(string operationId, string undoOperationId)
        {
            await _historyLock.WaitAsync();
            try
            {
                if (_useLocalDbArchiveLog && _localDbService != null)
                {
                    _localDbService.MarkArchiveLogReverted(operationId, undoOperationId);
                    return;
                }

                var path = GetArchiveLogPath();
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var entries = LoadArchiveLogEntries(path);
                var target = entries.FirstOrDefault(entry =>
                    string.Equals(entry.OperationId, operationId, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                    return;

                target.IsReverted = true;
                target.RevertedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                target.RevertedByOperationId = undoOperationId;
                WriteJsonAtomic(path, entries);
            }
            finally
            {
                _historyLock.Release();
            }
        }

        public async Task<ArchiveEmployeeResult> ArchiveEmployee(string employeeFolder, string firmName, string endDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(employeeFolder) || !Directory.Exists(employeeFolder))
                {
                    LoggingService.LogWarning("EmployeeService.ArchiveEmployee",
                        $"Employee folder not found: {employeeFolder}");
                    NotifyOperationFailure(Res("MsgArchiveSourceMissing"));
                    return new ArchiveEmployeeResult();
                }

                var archiveFolder = _folderService.GetArchiveFolder();
                if (string.IsNullOrEmpty(archiveFolder))
                {
                    LoggingService.LogWarning("EmployeeService.ArchiveEmployee", "Archive folder path is empty.");
                    NotifyOperationFailure(Res("MsgArchiveError"));
                    return new ArchiveEmployeeResult();
                }
                Directory.CreateDirectory(archiveFolder);

                var jsonPath = Path.Combine(employeeFolder, "employee.json");
                if (!File.Exists(jsonPath))
                {
                    LoggingService.LogWarning("EmployeeService.ArchiveEmployee",
                        $"employee.json not found: {jsonPath}");
                    NotifyOperationFailure(Res("MsgArchiveSourceMissing"));
                    return new ArchiveEmployeeResult();
                }

                string employeeName = "";
                string? originalJson = null;
                string? employeeUniqueId = null;

                if (File.Exists(jsonPath))
                {
                    originalJson = SafeFileService.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                    var data = JsonSerializer.Deserialize<EmployeeData>(originalJson);
                    if (data != null)
                    {
                        if (string.IsNullOrWhiteSpace(data.UniqueId))
                            data.UniqueId = Guid.NewGuid().ToString();

                        employeeUniqueId = data.UniqueId;
                        employeeName = $"{data.FirstName} {data.LastName}";
                        data.FirmHistory ??= new List<FirmHistoryEntry>();

                        var alreadyExists = data.FirmHistory.Any(fh =>
                            fh.FirmName == firmName && fh.StartDate == data.StartDate);
                        if (!alreadyExists)
                        {
                            data.FirmHistory.Add(new FirmHistoryEntry
                            {
                                FirmName = firmName,
                                StartDate = data.StartDate,
                                EndDate = endDate
                            });
                        }
                        else
                        {
                            var existing = data.FirmHistory.Last(fh =>
                                fh.FirmName == firmName && fh.StartDate == data.StartDate);
                            existing.EndDate = endDate;
                        }

                        data.EndDate = endDate;
                        data.IsArchived = true;
                        data.ArchivedFromFirm = firmName;
                        data.Status = "Dismissed";
                        WriteJsonAtomic(jsonPath, data);
                    }
                }

                var folderName = Path.GetFileName(employeeFolder);
                var destFolder = ResolveArchiveDestinationFolder(archiveFolder, folderName, employeeUniqueId);
                if (string.IsNullOrWhiteSpace(destFolder))
                {
                    LoggingService.LogWarning("EmployeeService.ArchiveEmployee",
                        $"Archive blocked because conflicting archive folder already exists for '{employeeFolder}'");
                    if (originalJson != null)
                        SafeFileService.WriteTextAtomic(jsonPath, originalJson, System.Text.Encoding.UTF8);
                    NotifyOperationFailure(Res("MsgArchiveConflict"));
                    return new ArchiveEmployeeResult();
                }

                CopyDirectory(employeeFolder, destFolder);

                var archivedJsonPath = Path.Combine(destFolder, "employee.json");
                if (!Directory.Exists(destFolder) || !File.Exists(archivedJsonPath))
                {
                    LoggingService.LogError("ArchiveEmployee",
                        new IOException($"Copy to archive failed for {employeeFolder}"));
                    if (originalJson != null)
                    {
                        SafeFileService.WriteTextAtomic(jsonPath, originalJson, System.Text.Encoding.UTF8);
                    }
                    return new ArchiveEmployeeResult();
                }

                var verifyData = ReadJson<EmployeeData>(archivedJsonPath);
                if (verifyData == null || string.IsNullOrEmpty(verifyData.FirstName))
                {
                    LoggingService.LogError("ArchiveEmployee",
                        new InvalidOperationException($"Archive verification failed for {destFolder}"));
                    if (originalJson != null)
                    {
                        SafeFileService.WriteTextAtomic(jsonPath, originalJson, System.Text.Encoding.UTF8);
                    }
                    TryDeleteDirectory(destFolder);
                    return new ArchiveEmployeeResult();
                }

                if (!string.IsNullOrWhiteSpace(employeeUniqueId)
                    && !string.Equals(verifyData.UniqueId, employeeUniqueId, StringComparison.OrdinalIgnoreCase))
                {
                    LoggingService.LogError("ArchiveEmployee",
                        new InvalidOperationException($"Archive UniqueId mismatch for {destFolder}"));
                    if (originalJson != null)
                        SafeFileService.WriteTextAtomic(jsonPath, originalJson, System.Text.Encoding.UTF8);
                    TryDeleteDirectory(destFolder);
                    NotifyOperationFailure(Res("MsgArchiveError"));
                    return new ArchiveEmployeeResult();
                }

                var sourceCleanupDeferred = !CleanupArchivedSourceFolder(employeeFolder);
                if (sourceCleanupDeferred)
                {
                    LoggingService.LogWarning("ArchiveEmployee",
                        $"Employee folder still exists after archive, scheduling cleanup: {employeeFolder}");
                    await PendingCleanupService.EnqueueAsync(employeeFolder, "archive-source-folder");
                    ScheduleDeferredCleanupRetry(employeeFolder, "EmployeeService.ArchiveEmployee");
                }

                var opId = Guid.NewGuid().ToString();
                await AppendArchiveLog(new ArchiveLogEntry
                {
                    OperationId = opId,
                    EmployeeName = employeeName,
                    FirmName = firmName,
                    EmployeeFolder = destFolder,
                    Action = "Archived",
                    Date = endDate,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });

                LoggingService.LogInfo("EmployeeService", $"Employee archived: {employeeName} from {firmName}");
                App.AdminMirrorSyncService?.EnqueueEmployeeUpsert(firmName, destFolder, verifyData);
                if (verifyData != null)
                    UpsertEmployeeIndex(destFolder, verifyData, firmName);
                App.FinanceService?.CleanupGhostFolders();
                return new ArchiveEmployeeResult
                {
                    ArchiveFolder = destFolder,
                    OperationId = opId,
                    SourceCleanupDeferred = sourceCleanupDeferred
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ArchiveEmployee error: {ex.Message}");
                LoggingService.LogError("EmployeeService.ArchiveEmployee", ex);
                return new ArchiveEmployeeResult();
            }
        }

        public async Task<ArchiveEmployeeResult> ArchiveEmployeeFromPathAsync(string sourceEmployeeFolder, string firmName, string endDate)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sourceEmployeeFolder) || !Directory.Exists(sourceEmployeeFolder))
                {
                    LoggingService.LogWarning("EmployeeService.ArchiveEmployeeFromPathAsync",
                        $"Employee folder not found: {sourceEmployeeFolder}");
                    NotifyOperationFailure(Res("MsgArchiveSourceMissing"));
                    return new ArchiveEmployeeResult();
                }

                var archiveFolder = _folderService.GetArchiveFolder();
                if (string.IsNullOrEmpty(archiveFolder))
                {
                    LoggingService.LogWarning("EmployeeService.ArchiveEmployeeFromPathAsync", "Archive folder path is empty.");
                    NotifyOperationFailure(Res("MsgArchiveError"));
                    return new ArchiveEmployeeResult();
                }

                Directory.CreateDirectory(archiveFolder);

                var jsonPath = Path.Combine(sourceEmployeeFolder, "employee.json");
                if (!File.Exists(jsonPath))
                {
                    LoggingService.LogWarning("EmployeeService.ArchiveEmployeeFromPathAsync",
                        $"employee.json not found: {jsonPath}");
                    NotifyOperationFailure(Res("MsgArchiveSourceMissing"));
                    return new ArchiveEmployeeResult();
                }

                string employeeName = "";
                string? originalJson = null;
                string? employeeUniqueId = null;

                originalJson = SafeFileService.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                var data = JsonSerializer.Deserialize<EmployeeData>(originalJson);
                if (data != null)
                {
                    if (string.IsNullOrWhiteSpace(data.UniqueId))
                        data.UniqueId = Guid.NewGuid().ToString();

                    employeeUniqueId = data.UniqueId;
                    employeeName = $"{data.FirstName} {data.LastName}";
                    data.FirmHistory ??= new List<FirmHistoryEntry>();

                    var alreadyExists = data.FirmHistory.Any(fh =>
                        fh.FirmName == firmName && fh.StartDate == data.StartDate);
                    if (!alreadyExists)
                    {
                        data.FirmHistory.Add(new FirmHistoryEntry
                        {
                            FirmName = firmName,
                            StartDate = data.StartDate,
                            EndDate = endDate
                        });
                    }
                    else
                    {
                        var existing = data.FirmHistory.Last(fh =>
                            fh.FirmName == firmName && fh.StartDate == data.StartDate);
                        existing.EndDate = endDate;
                    }

                    data.EndDate = endDate;
                    data.IsArchived = true;
                    data.ArchivedFromFirm = firmName;
                    data.Status = "Dismissed";
                    WriteJsonAtomic(jsonPath, data);
                }

                var folderName = Path.GetFileName(sourceEmployeeFolder);
                var destFolder = ResolveArchiveDestinationFolder(archiveFolder, folderName, employeeUniqueId);
                if (string.IsNullOrWhiteSpace(destFolder))
                {
                    LoggingService.LogWarning("EmployeeService.ArchiveEmployeeFromPathAsync",
                        $"Archive blocked because conflicting archive folder already exists for '{sourceEmployeeFolder}'");
                    if (originalJson != null)
                        SafeFileService.WriteTextAtomic(jsonPath, originalJson, System.Text.Encoding.UTF8);
                    NotifyOperationFailure(Res("MsgArchiveConflict"));
                    return new ArchiveEmployeeResult();
                }

                CopyDirectory(sourceEmployeeFolder, destFolder);

                var archivedJsonPath = Path.Combine(destFolder, "employee.json");
                if (!Directory.Exists(destFolder) || !File.Exists(archivedJsonPath))
                {
                    LoggingService.LogError("ArchiveEmployeeFromPathAsync",
                        new IOException($"Copy to archive failed for {sourceEmployeeFolder}"));
                    if (originalJson != null)
                        SafeFileService.WriteTextAtomic(jsonPath, originalJson, System.Text.Encoding.UTF8);
                    return new ArchiveEmployeeResult();
                }

                var verifyData = ReadJson<EmployeeData>(archivedJsonPath);
                if (verifyData == null || string.IsNullOrEmpty(verifyData.FirstName))
                {
                    LoggingService.LogError("ArchiveEmployeeFromPathAsync",
                        new InvalidOperationException($"Archive verification failed for {destFolder}"));
                    if (originalJson != null)
                        SafeFileService.WriteTextAtomic(jsonPath, originalJson, System.Text.Encoding.UTF8);
                    TryDeleteDirectory(destFolder);
                    return new ArchiveEmployeeResult();
                }

                if (!string.IsNullOrWhiteSpace(employeeUniqueId)
                    && !string.Equals(verifyData.UniqueId, employeeUniqueId, StringComparison.OrdinalIgnoreCase))
                {
                    LoggingService.LogError("ArchiveEmployeeFromPathAsync",
                        new InvalidOperationException($"Archive UniqueId mismatch for {destFolder}"));
                    if (originalJson != null)
                        SafeFileService.WriteTextAtomic(jsonPath, originalJson, System.Text.Encoding.UTF8);
                    TryDeleteDirectory(destFolder);
                    NotifyOperationFailure(Res("MsgArchiveError"));
                    return new ArchiveEmployeeResult();
                }

                var sourceCleanupDeferred = !CleanupArchivedSourceFolder(sourceEmployeeFolder);
                if (sourceCleanupDeferred)
                {
                    LoggingService.LogWarning("ArchiveEmployeeFromPathAsync",
                        $"Employee folder still exists after archive, scheduling cleanup: {sourceEmployeeFolder}");
                    await PendingCleanupService.EnqueueAsync(sourceEmployeeFolder, "archive-rd-source-folder");
                    ScheduleDeferredCleanupRetry(sourceEmployeeFolder, "EmployeeService.ArchiveEmployeeFromPathAsync");
                }

                var opId = Guid.NewGuid().ToString();
                await AppendArchiveLog(new ArchiveLogEntry
                {
                    OperationId = opId,
                    EmployeeName = employeeName,
                    FirmName = firmName,
                    EmployeeFolder = destFolder,
                    Action = "Archived",
                    Date = endDate,
                    Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });

                LoggingService.LogInfo("EmployeeService", $"Employee archived from Recently Deleted: {employeeName} from {firmName}");
                App.AdminMirrorSyncService?.EnqueueEmployeeUpsert(firmName, destFolder, verifyData);
                if (verifyData != null)
                    UpsertEmployeeIndex(destFolder, verifyData, firmName);
                return new ArchiveEmployeeResult
                {
                    ArchiveFolder = destFolder,
                    OperationId = opId,
                    SourceCleanupDeferred = sourceCleanupDeferred
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ArchiveEmployeeFromPathAsync error: {ex.Message}");
                LoggingService.LogError("EmployeeService.ArchiveEmployeeFromPathAsync", ex);
                return new ArchiveEmployeeResult();
            }
        }

        // ============ HISTORY ============

        public LocalDbMigrationResult EnsureEmployeeHistoryMigratedToLocalDb()
        {
            try
            {
                if (_localDbService == null)
                    return new LocalDbMigrationResult { Message = "LocalDbService is not configured." };

                var result = _localDbService.MigrateEmployeeHistoryIfNeeded(BuildEmployeeHistoryMigrationSources());
                _useLocalDbHistory = result.IsSuccessful;
                return result;
            }
            catch (Exception ex)
            {
                _useLocalDbHistory = false;
                LoggingService.LogError("EmployeeService.EnsureEmployeeHistoryMigratedToLocalDb", ex);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    Message = ex.Message
                };
            }
        }

        public async Task AddHistoryEntry(string employeeFolder, string employeeId, EmployeeHistoryEntry entry)
        {
            await _historyLock.WaitAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(entry.ActorName))
                {
                    var profile = App.CurrentProfile;
                    if (profile != null)
                        entry.ActorName = $"{profile.FirstName} {profile.LastName}".Trim();
                }

                if (_useLocalDbHistory && _localDbService != null && !string.IsNullOrWhiteSpace(employeeId))
                {
                    _localDbService.InsertEmployeeHistory(employeeId, employeeFolder, ResolveFirmNameForHistory(employeeFolder), entry);
                    return;
                }

                await AddHistoryEntryToJsonAsync(employeeFolder, entry);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AddHistoryEntry error: {ex.Message}");
                LoggingService.LogError("EmployeeService.AddHistoryEntry", ex);
            }
            finally
            {
                _historyLock.Release();
            }
        }

        public async Task AddHistoryEntry(string employeeFolder, EmployeeHistoryEntry entry)
        {
            var employeeId = ReadEmployeeUniqueId(employeeFolder) ?? string.Empty;
            await AddHistoryEntry(employeeFolder, employeeId, entry);
        }

        public List<EmployeeHistoryEntry> LoadHistory(string employeeFolder)
        {
            try
            {
                var employeeId = ReadEmployeeUniqueId(employeeFolder) ?? string.Empty;
                if (_useLocalDbHistory && _localDbService != null && !string.IsNullOrWhiteSpace(employeeId))
                {
                    var dbEntries = _localDbService.GetEmployeeHistory(employeeId);
                    if (dbEntries.Count > 0)
                        return dbEntries;
                }

                var historyFile = Path.Combine(employeeFolder, "history.json");
                if (!File.Exists(historyFile) || File.Exists(historyFile + ".migrated"))
                    return new List<EmployeeHistoryEntry>();

                return LoadHistoryEntries(historyFile);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.LoadHistory", ex);
                return new List<EmployeeHistoryEntry>();
            }
        }

        public async Task DeleteHistoryEntry(string employeeFolder, string employeeId, EmployeeHistoryEntry entry)
        {
            if (entry == null)
                return;

            await _historyLock.WaitAsync();
            try
            {
                if (_useLocalDbHistory && _localDbService != null && !string.IsNullOrWhiteSpace(employeeId) && entry.Id > 0)
                {
                    _localDbService.DeleteEmployeeHistoryEntry(employeeId, entry.Id);
                    return;
                }

                var historyFile = Path.Combine(employeeFolder, "history.json");
                if (!File.Exists(historyFile))
                    return;

                var entries = LoadHistoryEntries(historyFile);
                var removed = entries.RemoveAll(x =>
                    x.Timestamp == entry.Timestamp
                    && string.Equals(x.EventType, entry.EventType, StringComparison.Ordinal)
                    && string.Equals(x.Action, entry.Action, StringComparison.Ordinal)
                    && string.Equals(x.Description, entry.Description, StringComparison.Ordinal)
                    && string.Equals(x.ActorName, entry.ActorName, StringComparison.Ordinal));

                if (removed > 0)
                    WriteJsonAtomic(historyFile, entries);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.DeleteHistoryEntry", ex);
            }
            finally
            {
                _historyLock.Release();
            }
        }

        /// <summary>
        /// Compares old and new employee data, logging any changed fields to history.
        /// </summary>
        public async Task RecordChanges(string employeeFolder, EmployeeData oldData, EmployeeData newData)
        {
            try
            {
                var changes = new List<(string Field, string OldVal, string NewVal)>();

                void Check(string field, string oldVal, string newVal)
                {
                    if (oldVal != newVal) changes.Add((field, oldVal, newVal));
                }

                Check(Res("HistFieldFirstName"), oldData.FirstName, newData.FirstName);
                Check(Res("HistFieldLastName"), oldData.LastName, newData.LastName);
                Check(Res("HistFieldBirthDate"), oldData.BirthDate, newData.BirthDate);
                Check(Res("HistFieldGender"),
                    oldData.Gender == "female" ? Res("GenderFemale") : Res("GenderMale"),
                    newData.Gender == "female" ? Res("GenderFemale") : Res("GenderMale"));
                Check(Res("HistFieldPassportNum"), oldData.PassportNumber, newData.PassportNumber);
                Check(Res("HistFieldPassportAuthority"), oldData.PassportAuthority, newData.PassportAuthority);
                Check(Res("HistFieldPassportExp"), oldData.PassportExpiry, newData.PassportExpiry);
                Check(Res("HistFieldPassportCity"), oldData.PassportCity, newData.PassportCity);
                Check(Res("HistFieldPassportCountry"), oldData.PassportCountry, newData.PassportCountry);
                Check(Res("HistFieldCitizenship"), oldData.Citizenship, newData.Citizenship);
                Check(Res("HistFieldIssuingCountry"), oldData.IssuingCountry, newData.IssuingCountry);
                Check(Res("HistFieldVisaNum"), oldData.VisaNumber, newData.VisaNumber);
                Check(Res("HistFieldVisaAuthority"), oldData.VisaAuthority, newData.VisaAuthority);
                Check(Res("HistFieldVisaType"), oldData.VisaType, newData.VisaType);
                Check(Res("HistFieldVisaExp"), oldData.VisaExpiry, newData.VisaExpiry);
                Check(Res("HistFieldInsNum"), oldData.InsuranceNumber, newData.InsuranceNumber);
                Check(Res("HistFieldInsCompany"), oldData.InsuranceCompanyShort, newData.InsuranceCompanyShort);
                Check(Res("HistFieldInsCompanyFull"), oldData.InsuranceCompanyFull, newData.InsuranceCompanyFull);
                Check(Res("HistFieldInsExp"), oldData.InsuranceExpiry, newData.InsuranceExpiry);
                Check(Res("HistFieldPhone"), oldData.Phone, newData.Phone);
                Check(Res("HistFieldEmail"), oldData.Email, newData.Email);
                Check(Res("HistFieldStatus"), oldData.Status, newData.Status);
                Check(Res("HistFieldPosition"), oldData.PositionTag, newData.PositionTag);
                Check(Res("HistFieldPosNumber"), oldData.PositionNumber, newData.PositionNumber);
                Check(Res("HistFieldWorkAddr"), oldData.WorkAddressTag, newData.WorkAddressTag);
                Check(Res("HistFieldWorkPermitName"), oldData.WorkPermitName, newData.WorkPermitName);
                Check(Res("HistFieldSignDate"), oldData.ContractSignDate, newData.ContractSignDate);
                Check(Res("HistFieldContractType"), oldData.ContractType, newData.ContractType);
                Check(Res("HistFieldSalary"), oldData.MonthlySalaryBrutto.ToString(), newData.MonthlySalaryBrutto.ToString());
                Check(Res("HistFieldHourly"), oldData.HourlySalary.ToString(), newData.HourlySalary.ToString());
                Check(Res("HistFieldDepartment"), oldData.Department, newData.Department);
                Check(Res("HistFieldStartDate"), oldData.StartDate, newData.StartDate);

                foreach (var (field, oldVal, newVal) in changes)
                {
                    var evtType = field == Res("HistFieldStatus") ? "StatusChanged" : "ProfileChanged";
                    var employeeId = LoadEmployeeData(employeeFolder)?.UniqueId ?? string.Empty;
                    await AddHistoryEntry(employeeFolder, employeeId, new EmployeeHistoryEntry
                    {
                        EventType = evtType,
                        Action = Res("HistoryActionProfileChange"),
                        Field = field,
                        OldValue = oldVal,
                        NewValue = newVal,
                        Description = $"{field}: {oldVal} → {newVal}"
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RecordChanges error: {ex.Message}");
                LoggingService.LogError("EmployeeService.RecordChanges", ex);
            }
        }

        private async Task AddHistoryEntryToJsonAsync(string employeeFolder, EmployeeHistoryEntry entry)
        {
            var historyFile = Path.Combine(employeeFolder, "history.json");
            var entries = LoadHistoryEntries(historyFile);

            entries.Add(entry);
            WriteJsonAtomic(historyFile, entries);
            await Task.CompletedTask;
        }

        private IEnumerable<EmployeeHistoryMigrationSource> BuildEmployeeHistoryMigrationSources()
        {
            foreach (var source in EnumerateEmployeeFolders())
            {
                var historyJsonPath = Path.Combine(source.EmployeeFolder, "history.json");
                if (!File.Exists(historyJsonPath) || File.Exists(historyJsonPath + ".migrated"))
                    continue;

                var data = LoadEmployeeData(source.EmployeeFolder);
                if (data == null || string.IsNullOrWhiteSpace(data.UniqueId))
                {
                    LoggingService.LogWarning("EmployeeService.BuildEmployeeHistoryMigrationSources",
                        $"Skipped history migration because employee.json or UniqueId is missing: {source.EmployeeFolder}");
                    yield return new EmployeeHistoryMigrationSource
                    {
                        EmployeeFolder = source.EmployeeFolder,
                        FirmName = source.FirmName,
                        HistoryJsonPath = historyJsonPath
                    };
                    continue;
                }

                yield return new EmployeeHistoryMigrationSource
                {
                    EmployeeId = data.UniqueId,
                    EmployeeFolder = source.EmployeeFolder,
                    FirmName = source.FirmName,
                    HistoryJsonPath = historyJsonPath,
                    Entries = LoadHistoryEntries(historyJsonPath)
                };
            }
        }

        private IEnumerable<EmployeeHistoryMigrationSource> BuildEmployeeHistoryCleanupSources()
        {
            foreach (var source in EnumerateEmployeeFolders())
            {
                var historyJsonPath = Path.Combine(source.EmployeeFolder, "history.json");
                if (File.Exists(historyJsonPath) || !File.Exists(historyJsonPath + ".migrated"))
                    continue;

                var data = LoadEmployeeData(source.EmployeeFolder);
                if (data == null || string.IsNullOrWhiteSpace(data.UniqueId))
                {
                    LoggingService.LogWarning("EmployeeService.BuildEmployeeHistoryCleanupSources",
                        $"Skipped history cleanup because employee.json or UniqueId is missing: {source.EmployeeFolder}");
                    continue;
                }

                yield return new EmployeeHistoryMigrationSource
                {
                    EmployeeId = data.UniqueId,
                    EmployeeFolder = source.EmployeeFolder,
                    FirmName = source.FirmName,
                    HistoryJsonPath = historyJsonPath
                };
            }
        }

        public int CleanupMigratedEmployeeHistoryBackups()
        {
            if (_localDbService == null)
                return 0;

            try
            {
                return _localDbService.CleanupMigratedEmployeeHistoryBackups(BuildEmployeeHistoryCleanupSources());
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeeService.CleanupMigratedEmployeeHistoryBackups", ex.Message);
                return 0;
            }
        }

        private IEnumerable<(string EmployeeFolder, string FirmName)> EnumerateEmployeeFolders()
        {
            var companies = App.CompanyService?.Companies;
            if (companies != null)
            {
                foreach (var company in companies)
                {
                    var employeesFolder = _folderService.GetEmployeesFolder(company.Name);
                    if (string.IsNullOrWhiteSpace(employeesFolder) || !Directory.Exists(employeesFolder))
                        continue;

                    foreach (var folder in Directory.GetDirectories(employeesFolder))
                        yield return (folder, company.Name);
                }
            }

            var archiveFolder = _folderService.GetArchiveFolder();
            if (string.IsNullOrWhiteSpace(archiveFolder) || !Directory.Exists(archiveFolder))
                yield break;

            foreach (var folder in Directory.GetDirectories(archiveFolder))
            {
                var data = LoadEmployeeData(folder);
                var firmName = data?.ArchivedFromFirm;
                yield return (folder, string.IsNullOrWhiteSpace(firmName) ? "archive" : firmName);
            }
        }

        private string ResolveFirmNameForHistory(string employeeFolder)
        {
            var data = LoadEmployeeData(employeeFolder);
            if (data?.IsArchived == true && !string.IsNullOrWhiteSpace(data.ArchivedFromFirm))
                return data.ArchivedFromFirm;

            var companies = App.CompanyService?.Companies;
            if (companies == null)
                return string.Empty;

            foreach (var company in companies)
            {
                var employeesFolder = _folderService.GetEmployeesFolder(company.Name);
                if (!string.IsNullOrWhiteSpace(employeesFolder)
                    && employeeFolder.StartsWith(employeesFolder, StringComparison.OrdinalIgnoreCase))
                {
                    return company.Name;
                }
            }

            return string.Empty;
        }

        private static List<EmployeeHistoryEntry> LoadHistoryEntries(string path)
        {
            if (!File.Exists(path))
                return new();

            try
            {
                var raw = SafeFileService.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    LoggingService.LogWarning("EmployeeService.LoadHistoryEntries",
                        $"history.json is empty and will be treated as an empty history: {path}");
                    return new();
                }

                return SafeFileService.ReadJsonOrDefault(path, new List<EmployeeHistoryEntry>(), _jsonOptions, Encoding.UTF8);
            }
            catch (JsonException ex)
            {
                LoggingService.LogWarning("EmployeeService.LoadHistoryEntries",
                    $"history.json is invalid and will be treated as an empty history: {path}. {ex.Message}");
                return new();
            }
        }

        // ============ HELPERS ============

        private static string ResolvePhotoPath(string employeeFolder, EmployeeData data)
        {
            if (!string.IsNullOrEmpty(data.Files.Photo))
            {
                var fromJson = Path.Combine(employeeFolder, data.Files.Photo);
                if (File.Exists(fromJson)) return fromJson;
            }

            var fallback = Path.Combine(employeeFolder, $"{data.FirstName} {data.LastName} - Photo.jpg");
            if (File.Exists(fallback)) return fallback;

            return string.Empty;
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                SafeFileService.CopyFile(file, destFile);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(300);

            if (TryBulkDelete(dir)) return;

            TryDeleteFilesIndividually(dir);
            TryRemoveEmptyRoot(dir);

            if (Directory.Exists(dir))
            {
                var message = IsDirectoryEmpty(dir)
                    ? $"Empty folder cleanup deferred: {dir}"
                    : $"Directory cleanup incomplete: {dir}";
                LoggingService.LogWarning("TryDeleteDirectory", message);
            }
        }

        private static bool TryBulkDelete(string dir)
        {
            Exception? lastError = null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!Directory.Exists(dir)) return true;
                    foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    Directory.Delete(dir, true);
                    return true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Thread.Sleep(500 * (attempt + 1));
                }
            }

            if (Directory.Exists(dir) && lastError != null)
                LoggingService.LogWarning("EmployeeService.TryBulkDelete", $"Bulk delete deferred for '{dir}': {lastError.Message}");

            return false;
        }

        private static void TryDeleteFilesIndividually(string dir)
        {
            try
            {
                foreach (var file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        SafeFileService.DeleteFile(file);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogWarning("TryDeleteDirectory.File", $"Cannot delete {file}: {ex.Message}");
                    }
                }

                Thread.Sleep(200);
                DeleteEmptyDirectories(dir);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.TryDeleteDirectory", ex);
            }
        }

        private static bool TryRemoveEmptyRoot(string dir)
        {
            for (int i = 0; i < 3; i++)
            {
                if (!Directory.Exists(dir)) return true;
                try
                {
                    if (IsDirectoryEmpty(dir))
                    {
                        Directory.Delete(dir, false);
                        return !Directory.Exists(dir);
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    if (i == 2)
                        LoggingService.LogWarning("EmployeeService.TryRemoveEmptyRoot", $"Cannot remove empty root '{dir}': {ex.Message}");
                }
                Thread.Sleep(400);
            }
            return !Directory.Exists(dir);
        }

        private static void DeleteEmptyDirectories(string dir)
        {
            if (!Directory.Exists(dir)) return;

            foreach (var subDir in Directory.GetDirectories(dir))
                DeleteEmptyDirectories(subDir);

            try
            {
                if (Directory.Exists(dir)
                    && Directory.GetFiles(dir).Length == 0
                    && Directory.GetDirectories(dir).Length == 0)
                {
                    Directory.Delete(dir, false);
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("TryDeleteDirectory.EmptyDir", $"Cannot delete {dir}: {ex.Message}");
            }
        }

        private static bool IsDirectoryEmpty(string dir)
        {
            try
            {
                return Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any();
            }
            catch
            {
                return false;
            }
        }

        public static bool TryCleanupDeferredDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return true;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Thread.Sleep(300);

            TryDeleteFilesIndividually(dir);
            DeleteEmptyDirectories(dir);

            if (TryRemoveEmptyRoot(dir)) return true;

            if (TryBulkDelete(dir)) return true;

            return TryForceDeleteViaCmd(dir);
        }

        private static void ScheduleDeferredCleanupRetry(string directory, string context)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(15000);
                    if (TryCleanupDeferredDirectory(directory))
                        await PendingCleanupService.RemoveAsync(directory);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning(context, $"Deferred cleanup retry failed: {ex.Message}");
                }
            });
        }

        private static bool TryForceDeleteViaCmd(string dir)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c rd /s /q \"{dir}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardError = true
                };

                var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit(10000);
                    if (!Directory.Exists(dir))
                    {
                        LoggingService.LogInfo("EmployeeService", $"Force-deleted via cmd: {dir}");
                        return true;
                    }
                    var error = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(error))
                        LoggingService.LogWarning("EmployeeService.TryForceDeleteViaCmd", error.Trim());
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeeService.TryForceDeleteViaCmd", ex.Message);
            }

            return false;
        }

        private static bool CleanupArchivedSourceFolder(string dir)
        {
            return TryCleanupDeferredDirectory(dir);
        }

        private static string? FindEmployeeFolderByUniqueId(string employeesFolder, string? uniqueId)
        {
            if (string.IsNullOrWhiteSpace(employeesFolder) || string.IsNullOrWhiteSpace(uniqueId) || !Directory.Exists(employeesFolder))
                return null;

            try
            {
                foreach (var folder in Directory.GetDirectories(employeesFolder))
                {
                    var folderUniqueId = ReadEmployeeUniqueId(folder);
                    if (string.Equals(folderUniqueId, uniqueId, StringComparison.OrdinalIgnoreCase))
                        return folder;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeeService.FindEmployeeFolderByUniqueId", ex.Message);
            }

            return null;
        }

        private static string ResolveArchiveDestinationFolder(string archiveFolder, string folderName, string? uniqueId)
        {
            if (string.IsNullOrWhiteSpace(archiveFolder) || string.IsNullOrWhiteSpace(folderName))
                return string.Empty;

            for (var i = 0; i < 100; i++)
            {
                var candidateName = i == 0 ? folderName : $"{folderName}.{i}";
                var candidatePath = Path.Combine(archiveFolder, candidateName);
                if (!Directory.Exists(candidatePath))
                    return candidatePath;

                var existingUniqueId = ReadEmployeeUniqueId(candidatePath);
                if (!string.IsNullOrWhiteSpace(uniqueId)
                    && string.Equals(existingUniqueId, uniqueId, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Empty;
                }
            }

            return string.Empty;
        }

        private static string ResolveAvailableFolder(string parentFolder, string baseFolderName)
        {
            if (string.IsNullOrWhiteSpace(parentFolder) || string.IsNullOrWhiteSpace(baseFolderName))
                return string.Empty;

            for (var i = 0; i < 100; i++)
            {
                var candidateName = i == 0 ? baseFolderName : $"{baseFolderName}.{i}";
                var candidatePath = Path.Combine(parentFolder, candidateName);
                if (!Directory.Exists(candidatePath))
                    return candidatePath;
            }

            return string.Empty;
        }

        private static string? ReadEmployeeUniqueId(string folderPath)
        {
            try
            {
                var jsonPath = Path.Combine(folderPath, "employee.json");
                if (!File.Exists(jsonPath))
                    return null;

                return ReadJson<EmployeeData>(jsonPath)?.UniqueId;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("EmployeeService.ReadEmployeeUniqueId", ex.Message);
                return null;
            }
        }

        private static string NormalizeFolderName(string name)
        {
            return FolderService.NormalizeFolderName(name);
        }
    }
}
