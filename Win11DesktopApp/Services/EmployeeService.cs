using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
        private static readonly SemaphoreSlim _historyLock = new(1, 1);
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public EmployeeService(AppSettingsService appSettingsService, TagCatalogService tagCatalogService, FolderService folderService)
        {
            _appSettingsService = appSettingsService;
            _tagCatalogService = tagCatalogService;
            _folderService = folderService;
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
                    if (data == null) continue;
                    _tagCatalogService.AddTagsForEmployee(firmName, data);
                    summaries.Add(BuildSummary(firmName, folder, data));
                }
                catch (Exception ex) { LoggingService.LogError("EmployeeService.GetEmployeesForFirm", ex); }
            }
            Debug.WriteLine($"EmployeeService.GetEmployeesForFirm: {summaries.Count} items for {firmName}");
            return summaries;
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

            var summaries = new List<EmployeeSummary>();
            foreach (var folder in Directory.GetDirectories(employeesFolder))
            {
                var jsonPath = Path.Combine(folder, "employee.json");
                if (!File.Exists(jsonPath))
                {
                    continue;
                }
                try
                {
                    var data = ReadJson<EmployeeData>(jsonPath);
                    if (data == null) continue;
                    _tagCatalogService.AddTagsForEmployee(firmName, data);
                    summaries.Add(BuildSummary(firmName, folder, data));
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("EmployeeService.GetEmployeesForFirmWithStatus", ex);
                    continue;
                }
            }

            if (summaries.Count == 0)
            {
                return (summaries, "NoEmployees");
            }

            return (summaries, "Ok");
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
                var jsonPath = Path.Combine(employeeFolder, "employee.json");
                WriteJsonAtomic(jsonPath, data);
                var firmName = App.AdminMirrorSyncService?.InferFirmNameFromEmployeeFolder(employeeFolder);
                App.AdminMirrorSyncService?.EnqueueEmployeeUpsert(firmName, employeeFolder, data);
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
                var firmName = App.AdminMirrorSyncService?.InferFirmNameFromEmployeeFolder(employeeFolder);
                if (Directory.Exists(employeeFolder))
                {
                    Directory.Delete(employeeFolder, true);
                }
                App.AdminMirrorSyncService?.EnqueueEmployeeDelete(firmName, data);
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
                data.UniqueId = Guid.NewGuid().ToString();

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
                EmployeeFolder = employeeFolder,
                FirmName = firmName,
                EmployeeType = data.EmployeeType ?? "visa",
                WorkPermitName = data.WorkPermitName ?? string.Empty,
                WorkPermitExpiry = data.WorkPermitExpiry
            };
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

        public List<ArchiveLogEntry> LoadArchiveLog()
        {
            try
            {
                var path = GetArchiveLogPath();
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return new List<ArchiveLogEntry>();
                return ReadJsonOrDefault(path, new List<ArchiveLogEntry>());
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.LoadArchiveLog", ex);
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

                var path = GetArchiveLogPath();
                if (string.IsNullOrEmpty(path)) return;
                var entries = LoadArchiveLog();
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

            var result = new List<ArchivedEmployeeSummary>();
            foreach (var folder in Directory.GetDirectories(archiveFolder))
            {
                var jsonPath = Path.Combine(folder, "employee.json");
                if (!File.Exists(jsonPath)) continue;
                try
                {
                    var data = ReadJson<EmployeeData>(jsonPath);
                    if (data == null) continue;

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
                            catch (Exception ex2) { LoggingService.LogWarning("GetArchivedEmployees.Dedup", ex2.Message); }
                        }

                        var last = deduplicated.Last();
                        result.Add(new ArchivedEmployeeSummary
                        {
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
                catch (Exception ex) { LoggingService.LogError("EmployeeService.GetArchivedEmployees", ex); }
            }
            return result;
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

                        foreach (var fh in data.FirmHistory)
                        {
                            if (fh.FirmName == company.Name) continue;
                            result.Add(new ArchivedEmployeeSummary
                            {
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

                var archiveEntries = LoadArchiveLog();
                var target = archiveEntries.FirstOrDefault(entry =>
                    string.Equals(entry.OperationId, operationId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(entry.Action, "Archived", StringComparison.OrdinalIgnoreCase)
                    && !entry.IsReverted);
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
                var path = GetArchiveLogPath();
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var entries = LoadArchiveLog();
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

        public async Task AddHistoryEntry(string employeeFolder, EmployeeHistoryEntry entry)
        {
            await _historyLock.WaitAsync();
            try
            {
                var historyFile = Path.Combine(employeeFolder, "history.json");
                var entries = new List<EmployeeHistoryEntry>();

                if (File.Exists(historyFile))
                {
                    entries = ReadJsonOrDefault(historyFile, new List<EmployeeHistoryEntry>());
                }

                if (string.IsNullOrWhiteSpace(entry.ActorName))
                {
                    var profile = App.CurrentProfile;
                    if (profile != null)
                        entry.ActorName = $"{profile.FirstName} {profile.LastName}".Trim();
                }

                entries.Add(entry);

                WriteJsonAtomic(historyFile, entries);
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

        public List<EmployeeHistoryEntry> LoadHistory(string employeeFolder)
        {
            try
            {
                var historyFile = Path.Combine(employeeFolder, "history.json");
                if (!File.Exists(historyFile)) return new List<EmployeeHistoryEntry>();

                return ReadJsonOrDefault(historyFile, new List<EmployeeHistoryEntry>());
            }
            catch (Exception ex)
            {
                LoggingService.LogError("EmployeeService.LoadHistory", ex);
                return new List<EmployeeHistoryEntry>();
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
                Check(Res("HistFieldPassportExp"), oldData.PassportExpiry, newData.PassportExpiry);
                Check(Res("HistFieldPassportCity"), oldData.PassportCity, newData.PassportCity);
                Check(Res("HistFieldPassportCountry"), oldData.PassportCountry, newData.PassportCountry);
                Check(Res("HistFieldVisaNum"), oldData.VisaNumber, newData.VisaNumber);
                Check(Res("HistFieldVisaType"), oldData.VisaType, newData.VisaType);
                Check(Res("HistFieldVisaExp"), oldData.VisaExpiry, newData.VisaExpiry);
                Check(Res("HistFieldInsNum"), oldData.InsuranceNumber, newData.InsuranceNumber);
                Check(Res("HistFieldInsCompany"), oldData.InsuranceCompanyShort, newData.InsuranceCompanyShort);
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
                    await AddHistoryEntry(employeeFolder, new EmployeeHistoryEntry
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
