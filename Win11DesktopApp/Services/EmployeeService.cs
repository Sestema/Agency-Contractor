using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media.Imaging;
using Win11DesktopApp.EmployeeModels;

namespace Win11DesktopApp.Services
{
    public class EmployeeService
    {
        private readonly AppSettingsService _appSettingsService;
        private readonly TagCatalogService _tagCatalogService;
        private readonly FolderService _folderService;

        public EmployeeService(AppSettingsService appSettingsService, TagCatalogService tagCatalogService, FolderService folderService)
        {
            _appSettingsService = appSettingsService;
            _tagCatalogService = tagCatalogService;
            _folderService = folderService;
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
            try
            {
                if (Directory.Exists(tempFolder))
                {
                    Directory.Delete(tempFolder, true);
                }
            }
            catch { }
        }

        public EmployeeDocumentTemp PrepareTempDocument(string sourcePath, string tempFolder, string baseName)
        {
            var ext = Path.GetExtension(sourcePath).ToLower();
            var temp = new EmployeeDocumentTemp { OriginalExtension = ext };
            Debug.WriteLine($"EmployeeService.PrepareTempDocument: {sourcePath} -> {tempFolder} ({ext})");

            if (ext == ".pdf")
            {
                var destPdf = Path.Combine(tempFolder, $"{baseName}.pdf");
                File.Copy(sourcePath, destPdf, true);
                temp.PdfPath = destPdf;
                temp.IsPdf = true;
                return temp;
            }

            var destImage = Path.Combine(tempFolder, $"{baseName}.jpg");
            ConvertImageToJpg(sourcePath, destImage);
            temp.ImagePath = destImage;
            temp.IsPdf = false;
            return temp;
        }

        public string SaveEmployee(string firmName, EmployeeData data, EmployeeDocumentTemp passport, EmployeeDocumentTemp visa, EmployeeDocumentTemp insurance, string photoPath)
        {
            var employeesFolder = _folderService.GetEmployeesFolder(firmName);
            if (string.IsNullOrEmpty(employeesFolder))
            {
                Debug.WriteLine("EmployeeService.SaveEmployee: employees folder path is empty");
                return string.Empty;
            }
            Directory.CreateDirectory(employeesFolder);

            var safeName = NormalizeFolderName($"{data.FirstName}_{data.LastName} - {data.StartDate}");
            var employeeFolder = Path.Combine(employeesFolder, safeName);
            Directory.CreateDirectory(employeeFolder);

            data.Files.Passport = SaveDocument(passport, employeeFolder, $"{data.FirstName} {data.LastName} - Pass");
            data.Files.Visa = SaveDocument(visa, employeeFolder, $"{data.FirstName} {data.LastName} - Viza");
            data.Files.Insurance = SaveDocument(insurance, employeeFolder, $"{data.FirstName} {data.LastName} - {data.InsuranceCompanyShort}");

            if (!string.IsNullOrEmpty(photoPath))
            {
                var photoDest = Path.Combine(employeeFolder, $"{data.FirstName} {data.LastName} - Photo.jpg");
                File.Copy(photoPath, photoDest, true);
                data.Files.Photo = Path.GetFileName(photoDest);
            }

            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(jsonPath, json);

            _tagCatalogService.AddTagsForEmployee(firmName, data);
            Debug.WriteLine($"EmployeeService.SaveEmployee: saved to {employeeFolder}");
            return employeeFolder;
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
                    var json = File.ReadAllText(jsonPath);
                    var data = JsonSerializer.Deserialize<EmployeeData>(json);
                    if (data == null) continue;
                    _tagCatalogService.AddTagsForEmployee(firmName, data);
                    summaries.Add(BuildSummary(firmName, folder, data));
                }
                catch { }
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
                    var json = File.ReadAllText(jsonPath);
                    var data = JsonSerializer.Deserialize<EmployeeData>(json);
                    if (data == null) continue;
                    _tagCatalogService.AddTagsForEmployee(firmName, data);
                    summaries.Add(BuildSummary(firmName, folder, data));
                }
                catch
                {
                    return (new List<EmployeeSummary>(), "LoadError");
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
                var json = File.ReadAllText(jsonPath);
                return JsonSerializer.Deserialize<EmployeeData>(json);
            }
            catch
            {
                return null;
            }
        }

        public bool SaveEmployeeData(string employeeFolder, EmployeeData data)
        {
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            try
            {
                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(jsonPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool DeleteEmployee(string employeeFolder)
        {
            try
            {
                if (Directory.Exists(employeeFolder))
                {
                    Directory.Delete(employeeFolder, true);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        public string SaveDocumentFromSource(string sourcePath, string employeeFolder, string baseName)
        {
            var ext = Path.GetExtension(sourcePath).ToLower();
            if (ext == ".pdf")
            {
                var destPdf = Path.Combine(employeeFolder, $"{baseName}.pdf");
                File.Copy(sourcePath, destPdf, true);
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

        private string SaveDocument(EmployeeDocumentTemp doc, string employeeFolder, string baseName)
        {
            if (doc.IsPdf)
            {
                var pdfPath = Path.Combine(employeeFolder, $"{baseName}.pdf");
                if (!string.IsNullOrEmpty(doc.PdfPath))
                {
                    File.Copy(doc.PdfPath, pdfPath, true);
                    return Path.GetFileName(pdfPath);
                }
                return string.Empty;
            }

            var jpgPath = Path.Combine(employeeFolder, $"{baseName}.jpg");
            if (!string.IsNullOrEmpty(doc.ImagePath))
            {
                File.Copy(doc.ImagePath, jpgPath, true);
                return Path.GetFileName(jpgPath);
            }
            return string.Empty;
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
            var photoName = $"{data.FirstName} {data.LastName} - Photo.jpg";
            var photoPath = Path.Combine(employeeFolder, photoName);
            return new EmployeeSummary
            {
                FullName = $"{data.FirstName} {data.LastName}",
                PositionTitle = data.PositionTag,
                StartDate = data.StartDate,
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
                Status = data.Status,
                Phone = data.Phone,
                Email = data.Email,
                EmployeeFolder = employeeFolder,
                FirmName = firmName
            };
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
                    var json = File.ReadAllText(jsonPath);
                    var data = JsonSerializer.Deserialize<EmployeeData>(json);
                    if (data == null) continue;

                    var photoPath = Path.Combine(folder, $"{data.FirstName} {data.LastName} - Photo.jpg");
                    result.Add(new ArchivedEmployeeSummary
                    {
                        FullName = $"{data.FirstName} {data.LastName}",
                        PositionTitle = data.PositionTag,
                        FirmName = string.Empty,
                        StartDate = data.StartDate,
                        EndDate = data.EndDate,
                        EmployeeFolder = folder,
                        PhotoPath = File.Exists(photoPath) ? photoPath : string.Empty,
                        HasPhoto = File.Exists(photoPath)
                    });
                }
                catch { }
            }
            return result;
        }

        public string RestoreFromArchive(string archiveEmployeeFolder, string newFirmName, string newStartDate, string newContractSignDate, string positionTag, string workAddressTag)
        {
            try
            {
                var jsonPath = Path.Combine(archiveEmployeeFolder, "employee.json");
                if (!File.Exists(jsonPath)) return string.Empty;

                var json = File.ReadAllText(jsonPath);
                var data = JsonSerializer.Deserialize<EmployeeData>(json);
                if (data == null) return string.Empty;

                data.StartDate = newStartDate;
                data.ContractSignDate = newContractSignDate;
                data.PositionTag = positionTag;
                data.WorkAddressTag = workAddressTag;
                data.EndDate = string.Empty;

                var employeesFolder = _folderService.GetEmployeesFolder(newFirmName);
                if (string.IsNullOrEmpty(employeesFolder)) return string.Empty;
                Directory.CreateDirectory(employeesFolder);

                var folderName = Path.GetFileName(archiveEmployeeFolder);
                var destFolder = Path.Combine(employeesFolder, folderName);

                CopyDirectory(archiveEmployeeFolder, destFolder);

                var newJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(destFolder, "employee.json"), newJson);

                TryDeleteDirectory(archiveEmployeeFolder);

                return destFolder;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RestoreFromArchive error: {ex.Message}");
                return string.Empty;
            }
        }

        public string ArchiveEmployee(string employeeFolder, string firmName, string endDate)
        {
            try
            {
                var archiveFolder = _folderService.GetArchiveFolder();
                if (string.IsNullOrEmpty(archiveFolder)) return string.Empty;
                Directory.CreateDirectory(archiveFolder);

                var jsonPath = Path.Combine(employeeFolder, "employee.json");
                if (File.Exists(jsonPath))
                {
                    var json = File.ReadAllText(jsonPath);
                    var data = JsonSerializer.Deserialize<EmployeeData>(json);
                    if (data != null)
                    {
                        data.EndDate = endDate;
                        var newJson = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(jsonPath, newJson);
                    }
                }

                var folderName = Path.GetFileName(employeeFolder);
                var destFolder = Path.Combine(archiveFolder, folderName);

                CopyDirectory(employeeFolder, destFolder);
                TryDeleteDirectory(employeeFolder);

                return destFolder;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ArchiveEmployee error: {ex.Message}");
                return string.Empty;
            }
        }

        // ============ HISTORY ============

        public void AddHistoryEntry(string employeeFolder, EmployeeHistoryEntry entry)
        {
            try
            {
                var historyFile = Path.Combine(employeeFolder, "history.json");
                var entries = new List<EmployeeHistoryEntry>();

                if (File.Exists(historyFile))
                {
                    var json = File.ReadAllText(historyFile);
                    entries = JsonSerializer.Deserialize<List<EmployeeHistoryEntry>>(json) ?? new List<EmployeeHistoryEntry>();
                }

                entries.Add(entry);

                var newJson = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(historyFile, newJson);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AddHistoryEntry error: {ex.Message}");
            }
        }

        public List<EmployeeHistoryEntry> LoadHistory(string employeeFolder)
        {
            try
            {
                var historyFile = Path.Combine(employeeFolder, "history.json");
                if (!File.Exists(historyFile)) return new List<EmployeeHistoryEntry>();

                var json = File.ReadAllText(historyFile);
                return JsonSerializer.Deserialize<List<EmployeeHistoryEntry>>(json) ?? new List<EmployeeHistoryEntry>();
            }
            catch
            {
                return new List<EmployeeHistoryEntry>();
            }
        }

        /// <summary>
        /// Compares old and new employee data, logging any changed fields to history.
        /// </summary>
        public void RecordChanges(string employeeFolder, EmployeeData oldData, EmployeeData newData)
        {
            try
            {
                var changes = new List<(string Field, string OldVal, string NewVal)>();

                void Check(string field, string oldVal, string newVal)
                {
                    if (oldVal != newVal) changes.Add((field, oldVal, newVal));
                }

                Check("Ім'я", oldData.FirstName, newData.FirstName);
                Check("Прізвище", oldData.LastName, newData.LastName);
                Check("Дата народження", oldData.BirthDate, newData.BirthDate);
                Check("Номер паспорту", oldData.PassportNumber, newData.PassportNumber);
                Check("Термін паспорту", oldData.PassportExpiry, newData.PassportExpiry);
                Check("Номер візи", oldData.VisaNumber, newData.VisaNumber);
                Check("Термін візи", oldData.VisaExpiry, newData.VisaExpiry);
                Check("Номер страховки", oldData.InsuranceNumber, newData.InsuranceNumber);
                Check("Термін страховки", oldData.InsuranceExpiry, newData.InsuranceExpiry);
                Check("Телефон", oldData.Phone, newData.Phone);
                Check("Email", oldData.Email, newData.Email);
                Check("Статус", oldData.Status, newData.Status);
                Check("Позиція", oldData.PositionTag, newData.PositionTag);
                Check("Адреса роботи", oldData.WorkAddressTag, newData.WorkAddressTag);

                foreach (var (field, oldVal, newVal) in changes)
                {
                    AddHistoryEntry(employeeFolder, new EmployeeHistoryEntry
                    {
                        Action = "Зміна анкети",
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
            }
        }

        // ============ HELPERS ============

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        private static void TryDeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"TryDeleteDirectory error: {ex.Message}");
            }
        }

        private static string NormalizeFolderName(string name)
        {
            return FolderService.NormalizeFolderName(name);
        }
    }
}
