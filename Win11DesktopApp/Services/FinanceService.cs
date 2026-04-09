using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using Win11DesktopApp.Models;
using System.Globalization;

namespace Win11DesktopApp.Services
{
    public class FinanceService
    {
        private const string FinanceFileName = "finance_data.json";
        private readonly bool _suppressStartupNotifications;
        private readonly string _filePath;
        private FinanceDatabase _db;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };
        private readonly SalaryDbService? _salaryDbService;
        private readonly LocalDbService? _localDbService;
        private readonly object _paymentsCacheLock = new();
        private readonly Dictionary<(int year, int month), (List<SalaryEntry> entries, List<FirmExpense> expenses)> _paymentsCache = new();

        public const string GlobalKey = "__GLOBAL__";
        public const string AllFirmsKey = "__ALL__";
        public bool WasRecoveredFromBackupOnLoad { get; private set; }
        public bool WasResetToDefaultsOnLoad { get; private set; }
        public string LastSalaryConflictMessage { get; private set; } = string.Empty;
        public string? LastSaveRecoveryPath { get; private set; }

        public FinanceService(FolderService folderService, SalaryDbService? salaryDbService = null, LocalDbService? localDbService = null, bool suppressStartupNotifications = false)
        {
            _suppressStartupNotifications = suppressStartupNotifications;
            _salaryDbService = salaryDbService;
            _localDbService = localDbService;
            var rootPath = folderService.RootPath;
            if (!string.IsNullOrEmpty(rootPath))
            {
                Directory.CreateDirectory(rootPath);
                _filePath = Path.Combine(rootPath, FinanceFileName);
                MigrateFromAppDataIfNeeded(rootPath);
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var appFolder = Path.Combine(appData, "AgencyContractor");
                Directory.CreateDirectory(appFolder);
                _filePath = Path.Combine(appFolder, FinanceFileName);
            }
            _db = Load();
            MigrateIfNeeded();
        }

        public SalaryDbMigrationResult EnsureSalaryMigratedToLocalDb()
        {
            try
            {
                if (_salaryDbService == null || _localDbService == null)
                    return new SalaryDbMigrationResult { Message = "SalaryDbService or LocalDbService is not configured." };

                if (IsSalaryMigrationCompleted())
                {
                    return new SalaryDbMigrationResult
                    {
                        WasMigrationAttempted = false,
                        IsSuccessful = true,
                        Message = "Salary migration already completed."
                    };
                }

                return MigrateSalaryJsonToMonthlyDatabases();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.EnsureSalaryMigratedToLocalDb", ex);
                return new SalaryDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    Message = ex.Message
                };
            }
        }

        private static T? ReadJson<T>(string path)
        {
            // Salary files can be read while a save is in flight; allow shared read access
            // so the app is less likely to block its own replace/copy path.
            return SafeFileService.ReadJsonShared<T>(path, _jsonOptions);
        }

        private static T ReadJsonOrDefault<T>(string path, T fallback)
        {
            return SafeFileService.ReadJsonOrDefault(path, fallback, _jsonOptions);
        }

        private static void WriteJsonAtomic<T>(string path, T value)
        {
            SafeFileService.WriteJsonAtomic(path, value, _jsonOptions);
        }

        private void MigrateFromAppDataIfNeeded(string rootPath)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var oldPath = Path.Combine(appData, "AgencyContractor", FinanceFileName);
                var newPath = Path.Combine(rootPath, FinanceFileName);

                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    SafeFileService.CopyFile(oldPath, newPath, overwrite: false);
                    SafeFileService.MoveFile(oldPath, oldPath + ".migrated");
                    LoggingService.LogInfo("FinanceService", $"Migrated {FinanceFileName} from AppData to RootPath");
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.MigrateFromAppData", ex);
            }
        }

        private FinanceDatabase Load()
        {
            if (!File.Exists(_filePath))
            {
                if (TryRestoreFromBackup(out var restoredFromBackup))
                    return restoredFromBackup;

                return new FinanceDatabase();
            }

            try
            {
                return ReadJsonOrDefault(_filePath, new FinanceDatabase());
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.Load", ex);
                BackupUnreadableFile(_filePath, "finance");
                if (TryRestoreFromBackup(out var restoredFromBackup))
                    return restoredFromBackup;

                WasResetToDefaultsOnLoad = true;
                NotifyStartupWarning(Res("MsgFinanceResetToDefaults"));
                return new FinanceDatabase();
            }
        }

        private void MigrateIfNeeded()
        {
            if (_db.Version == "2.0") return;

            bool changed = false;

            CustomSalaryField? surchargeField = null;
            CustomSalaryField? accomField = null;

            foreach (var report in _db.Reports)
            {
                foreach (var entry in report.Entries)
                {
                    if (entry.CustomValues.Count > 0) continue;

#pragma warning disable CS0618
                    if (entry.Surcharge != 0)
                    {
                        if (surchargeField == null)
                        {
                            surchargeField = new CustomSalaryField
                            {
                                Id = "migrated_surcharge",
                                Name = "Doplatek",
                                Operation = FieldOperation.Add,
                                FirmName = AllFirmsKey,
                                Order = 0
                            };
                            if (!_db.CustomFields.Any(f => f.Id == surchargeField.Id))
                                _db.CustomFields.Add(surchargeField);
                        }
                        entry.CustomValues[surchargeField.Id] = entry.Surcharge;
                        changed = true;
                    }

                    if (entry.Accommodation != 0)
                    {
                        if (accomField == null)
                        {
                            accomField = new CustomSalaryField
                            {
                                Id = "migrated_accommodation",
                                Name = "Ubytovna",
                                Operation = FieldOperation.Subtract,
                                FirmName = AllFirmsKey,
                                Order = 1
                            };
                            if (!_db.CustomFields.Any(f => f.Id == accomField.Id))
                                _db.CustomFields.Add(accomField);
                        }
                        entry.CustomValues[accomField.Id] = entry.Accommodation;
                        changed = true;
                    }
#pragma warning restore CS0618
                }
            }

            _db.Version = "2.0";
            if (changed) Save();
        }

        public void Save()
        {
            try
            {
                LoggingService.LogInfo("FinanceService", "Saving finance database");
                CreateBackupIfNeeded();
                WriteJsonAtomic(_filePath, _db);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"FinanceService.Save error: {ex.Message}");
                LoggingService.LogError("FinanceService.Save", ex);
            }
        }

        private void CreateBackupIfNeeded()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var dir = Path.GetDirectoryName(_filePath);
                if (string.IsNullOrEmpty(dir)) return;
                var backupDir = Path.Combine(dir, "backups");
                Directory.CreateDirectory(backupDir);

                var backupFile = Path.Combine(backupDir, $"finance_data_{DateTime.Now:yyyyMMdd_HHmmss}.json.bak");
                SafeFileService.CopyFile(_filePath, backupFile);

                var files = new DirectoryInfo(backupDir)
                    .GetFiles("finance_data_*.json.bak")
                    .OrderByDescending(f => f.CreationTime)
                    .ToList();
                for (int i = 5; i < files.Count; i++)
                {
                    try { files[i].Delete(); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.CreateBackupIfNeeded", $"Cleanup failed: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceService.CreateBackup", ex.Message);
            }
        }

        private bool TryRestoreFromBackup(out FinanceDatabase database)
        {
            database = new FinanceDatabase();

            try
            {
                var backupPath = GetLatestBackupPath();
                if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
                    return false;

                database = ReadJsonOrDefault(backupPath, new FinanceDatabase());
                WasRecoveredFromBackupOnLoad = true;
                WriteJsonAtomic(_filePath, database);
                LoggingService.LogWarning("FinanceService", $"Restored finance data from backup: {backupPath}");
                NotifyStartupWarning(Res("MsgFinanceRecoveredFromBackup"));
                return true;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceService.TryRestoreFromBackup", ex.Message);
                return false;
            }
        }

        private string? GetLatestBackupPath()
        {
            try
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (string.IsNullOrWhiteSpace(dir))
                    return null;

                var backupDir = Path.Combine(dir, "backups");
                if (!Directory.Exists(backupDir))
                    return null;

                return new DirectoryInfo(backupDir)
                    .GetFiles("finance_data_*.json.bak")
                    .OrderByDescending(f => f.CreationTimeUtc)
                    .Select(f => f.FullName)
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceService.GetLatestBackupPath", ex.Message);
                return null;
            }
        }

        private static void BackupUnreadableFile(string path, string label)
        {
            try
            {
                if (!File.Exists(path))
                    return;

                var directory = Path.GetDirectoryName(path);
                var fileName = Path.GetFileName(path);
                var quarantineName = $"{fileName}.corrupt.{DateTime.Now:yyyyMMdd_HHmmss}";
                var quarantinePath = string.IsNullOrWhiteSpace(directory)
                    ? quarantineName
                    : Path.Combine(directory, quarantineName);
                SafeFileService.MoveFile(path, quarantinePath);
                LoggingService.LogWarning("FinanceService.BackupUnreadableFile",
                    $"Moved unreadable {label} file to {quarantinePath}");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceService.BackupUnreadableFile", ex.Message);
            }
        }

        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        private bool IsSalaryMigrationCompleted()
        {
            try
            {
                using var connection = _localDbService!.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = @"
SELECT COUNT(1)
FROM migration_journal
WHERE stage = 'salary_entries'
  AND status = 'completed';";
                var result = command.ExecuteScalar();
                return Convert.ToInt32(result, CultureInfo.InvariantCulture) > 0;
            }
            catch
            {
                return false;
            }
        }

        private SalaryDbMigrationResult MigrateSalaryJsonToMonthlyDatabases()
        {
            if (_salaryDbService == null || _localDbService == null)
                return new SalaryDbMigrationResult { Message = "Salary migration services are not configured." };

            var monthBuckets = new Dictionary<string, (int year, int month, List<SalaryEntry> entries, List<FirmExpense> expenses)>(StringComparer.OrdinalIgnoreCase);
            var filesScanned = 0;
            var filesSkipped = 0;
            var recordsFound = 0;
            var expensesFound = 0;
            var databasesCreated = 0;

            _localDbService.RecordMigrationJournal("salary_entries", "started", 0, 0, null, 0, 0);

            try
            {
                foreach (var paymentFolder in EnumeratePaymentFolders())
                {
                    if (string.IsNullOrWhiteSpace(paymentFolder) || !Directory.Exists(paymentFolder))
                        continue;

                    foreach (var file in Directory.GetFiles(paymentFolder, "salary_*.json"))
                    {
                        filesScanned++;

                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var parts = fileName.Split('_');
                        if (parts.Length != 3
                            || !int.TryParse(parts[1], out var year)
                            || !int.TryParse(parts[2], out var month))
                        {
                            filesSkipped++;
                            LoggingService.LogWarning("FinanceService.SalaryMigration",
                                $"Skipped unreadable salary filename: {file}");
                            continue;
                        }

                        try
                        {
                            var data = ReadJson<FirmPaymentData>(file);
                            if (data == null)
                            {
                                filesSkipped++;
                                LoggingService.LogWarning("FinanceService.SalaryMigration",
                                    $"Skipped unreadable salary JSON: {file}");
                                continue;
                            }

                            var monthKey = $"{year:D4}-{month:D2}";
                            if (!monthBuckets.TryGetValue(monthKey, out var bucket))
                            {
                                bucket = (year, month, new List<SalaryEntry>(), new List<FirmExpense>());
                            }

                            bucket.entries.AddRange(data.Entries.Select(CloneSalaryEntryForMigration));
                            bucket.expenses.AddRange(data.Expenses.Select(CloneFirmExpenseForMigration));
                            monthBuckets[monthKey] = bucket;

                            recordsFound += data.Entries.Count;
                            expensesFound += data.Expenses.Count;
                        }
                        catch (Exception ex)
                        {
                            filesSkipped++;
                            LoggingService.LogError("FinanceService.SalaryMigration.ReadFile",
                                new IOException($"file={file}: {ex.Message}", ex));
                        }
                    }
                }

                var recordsImported = 0;
                var expensesImported = 0;

                foreach (var bucket in monthBuckets.Values.OrderBy(v => v.year).ThenBy(v => v.month))
                {
                    var monthDbExisted = _salaryDbService.MonthDbExists(bucket.year, bucket.month);
                    _salaryDbService.ReplaceMonthData(bucket.year, bucket.month, bucket.entries, bucket.expenses);
                    if (!monthDbExisted)
                        databasesCreated++;

                    var snapshot = _salaryDbService.GetMonthValidationSnapshot(bucket.year, bucket.month);
                    ValidateMonthSnapshot(bucket.year, bucket.month, bucket.entries, bucket.expenses, snapshot);

                    recordsImported += snapshot.EntryCount;
                    expensesImported += snapshot.ExpenseCount;
                }

                _localDbService.RecordMigrationJournal(
                    "salary_entries",
                    "completed",
                    recordsFound + expensesFound,
                    recordsImported + expensesImported,
                    null,
                    filesScanned,
                    filesSkipped);

                return new SalaryDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = true,
                    FilesScanned = filesScanned,
                    FilesSkipped = filesSkipped,
                    RecordsFound = recordsFound,
                    RecordsImported = recordsImported,
                    ExpensesFound = expensesFound,
                    ExpensesImported = expensesImported,
                    DatabasesCreated = databasesCreated,
                    Message = $"Migrated salary JSON to {monthBuckets.Count} monthly SQLite databases."
                };
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.MigrateSalaryJsonToMonthlyDatabases", ex);
                _localDbService.RecordMigrationJournal(
                    "salary_entries",
                    "failed",
                    recordsFound + expensesFound,
                    0,
                    ex.Message,
                    filesScanned,
                    filesSkipped);

                return new SalaryDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    FilesScanned = filesScanned,
                    FilesSkipped = filesSkipped,
                    RecordsFound = recordsFound,
                    ExpensesFound = expensesFound,
                    Message = ex.Message
                };
            }
        }

        private static SalaryEntry CloneSalaryEntryForMigration(SalaryEntry entry)
        {
#pragma warning disable CS0618
            return new SalaryEntry
            {
                EmployeeId = entry.EmployeeId,
                EmployeeFolder = entry.EmployeeFolder,
                FullName = entry.FullName,
                FirmName = entry.FirmName,
                HoursWorked = entry.HoursWorked,
                HourlyRate = entry.HourlyRate,
                Advance = entry.Advance,
                Advances = entry.Advances,
                Surcharge = entry.Surcharge,
                Accommodation = entry.Accommodation,
                OtherDeductions = entry.OtherDeductions,
                CustomValues = new Dictionary<string, decimal>(entry.CustomValues, StringComparer.OrdinalIgnoreCase),
                SavedNetSalary = entry.SavedNetSalary,
                Status = entry.Status,
                Note = entry.Note,
                ColorTag = entry.ColorTag,
                IsFinished = entry.IsFinished
            };
#pragma warning restore CS0618
        }

        private static FirmExpense CloneFirmExpenseForMigration(FirmExpense expense)
        {
            return new FirmExpense
            {
                Id = expense.Id,
                FirmName = expense.FirmName,
                Year = expense.Year,
                Month = expense.Month,
                Name = expense.Name,
                Amount = expense.Amount
            };
        }

        private static void ValidateMonthSnapshot(
            int year,
            int month,
            IReadOnlyCollection<SalaryEntry> expectedEntries,
            IReadOnlyCollection<FirmExpense> expectedExpenses,
            (int EntryCount, int ExpenseCount, decimal SavedNetSalaryTotal, Dictionary<string, int> StatusCounts) snapshot)
        {
            if (snapshot.EntryCount != expectedEntries.Count)
                throw new InvalidOperationException($"Salary entry count mismatch for {year:D4}-{month:D2}: expected {expectedEntries.Count}, got {snapshot.EntryCount}.");

            if (snapshot.ExpenseCount != expectedExpenses.Count)
                throw new InvalidOperationException($"Salary expense count mismatch for {year:D4}-{month:D2}: expected {expectedExpenses.Count}, got {snapshot.ExpenseCount}.");

            var expectedNetTotal = expectedEntries.Sum(e => e.SavedNetSalary);
            if (expectedNetTotal != snapshot.SavedNetSalaryTotal)
                throw new InvalidOperationException($"SavedNetSalary mismatch for {year:D4}-{month:D2}: expected {expectedNetTotal}, got {snapshot.SavedNetSalaryTotal}.");

            var expectedStatusCounts = expectedEntries
                .GroupBy(e => e.Status ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var pair in expectedStatusCounts)
            {
                if (!snapshot.StatusCounts.TryGetValue(pair.Key, out var actual) || actual != pair.Value)
                    throw new InvalidOperationException($"Status distribution mismatch for {year:D4}-{month:D2}: status={pair.Key}, expected {pair.Value}, got {actual}.");
            }
        }

        private void NotifyStartupWarning(string message)
        {
            if (_suppressStartupNotifications)
                return;

            NotifyWarning(message);
        }

        private static void NotifyWarning(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                if (Application.Current?.MainWindow?.IsVisible == true)
                {
                    ToastService.Instance.Warning(message);
                    return;
                }

                MessageBox.Show(message, Res("TitleWarning"), MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        #region Custom Fields

        public List<CustomSalaryField> GetCustomFields()
        {
            return _db.CustomFields.OrderBy(f => f.FirmName).ThenBy(f => f.Order).ToList();
        }

        public List<CustomSalaryField> GetFieldsForFirm(string firmName)
        {
            return _db.CustomFields
                .Where(f => f.FirmName == AllFirmsKey || f.FirmName == firmName)
                .OrderBy(f => f.Order)
                .ToList();
        }

        public List<CustomSalaryField> GetActiveFields(IEnumerable<string> visibleFirms)
        {
            var firmSet = visibleFirms.ToHashSet();
            return _db.CustomFields
                .Where(f => f.FirmName == AllFirmsKey || firmSet.Contains(f.FirmName))
                .OrderBy(f => f.Order)
                .ToList();
        }

        public void AddCustomField(CustomSalaryField field)
        {
            if (string.IsNullOrEmpty(field.Id))
                field.Id = Guid.NewGuid().ToString();
            _db.CustomFields.Add(field);
            Save();
        }

        public void UpdateCustomField(CustomSalaryField updated)
        {
            var idx = _db.CustomFields.FindIndex(f => f.Id == updated.Id);
            if (idx >= 0)
                _db.CustomFields[idx] = updated;
            Save();
        }

        public void RemoveCustomField(string fieldId)
        {
            _db.CustomFields.RemoveAll(f => f.Id == fieldId);
            foreach (var report in _db.Reports)
                foreach (var entry in report.Entries)
                    entry.CustomValues.Remove(fieldId);
            Save();
        }

        public void ReorderCustomFields(List<CustomSalaryField> orderedFields)
        {
            for (int i = 0; i < orderedFields.Count; i++)
            {
                var db = _db.CustomFields.FirstOrDefault(f => f.Id == orderedFields[i].Id);
                if (db != null) db.Order = i;
            }
            Save();
        }

        #endregion

        #region Reports

        public MonthlySalaryReport? GetReport(string companyId, int year, int month)
        {
            return _db.Reports.FirstOrDefault(r => r.CompanyId == companyId && r.Year == year && r.Month == month);
        }

        public MonthlySalaryReport? GetGlobalReport(int year, int month)
        {
            return _db.Reports.FirstOrDefault(r => r.CompanyId == GlobalKey && r.Year == year && r.Month == month);
        }

        public MonthlySalaryReport GetOrCreateReport(string companyId, string companyName, int year, int month)
        {
            var report = GetReport(companyId, year, month);
            if (report == null)
            {
                report = new MonthlySalaryReport
                {
                    CompanyId = companyId,
                    CompanyName = companyName,
                    Year = year,
                    Month = month
                };
                _db.Reports.Add(report);
            }
            return report;
        }

        public MonthlySalaryReport GetOrCreateGlobalReport(int year, int month)
        {
            var report = GetGlobalReport(year, month);
            if (report == null)
            {
                report = new MonthlySalaryReport
                {
                    CompanyId = GlobalKey,
                    CompanyName = "All",
                    Year = year,
                    Month = month
                };
                _db.Reports.Add(report);
            }
            return report;
        }

        public void SaveReport(MonthlySalaryReport report)
        {
            report.UpdatedAt = DateTime.Now;
            var idx = _db.Reports.FindIndex(r => r.Id == report.Id);
            if (idx >= 0)
                _db.Reports[idx] = report;
            else
                _db.Reports.Add(report);
            Save();
        }

        public List<MonthlySalaryReport> GetReportsForCompany(string companyId)
        {
            return _db.Reports.Where(r => r.CompanyId == companyId).OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToList();
        }

        public List<string> GetAvailableMonths(string companyId)
        {
            return _db.Reports
                .Where(r => r.CompanyId == companyId)
                .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
                .Select(r => r.MonthKey)
                .Distinct()
                .ToList();
        }

        #endregion

        #region Advances

        public void AddAdvance(AdvancePayment advance)
        {
            _db.Advances.Add(advance);
            Save();
        }

        public List<AdvancePayment> GetAdvances(string companyId, string monthKey)
        {
            return _db.Advances.Where(a => a.CompanyId == companyId && a.Month == monthKey).ToList();
        }

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string companyId, string monthKey)
        {
            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && a.CompanyId == companyId && a.Month == monthKey)
                .Sum(a => a.Amount);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string monthKey)
        {
            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && a.Month == monthKey)
                .Sum(a => a.Amount);
        }

        public void RemoveAdvance(string advanceId)
        {
            _db.Advances.RemoveAll(a => a.Id == advanceId);
            Save();
        }

        public void RemoveEmployeeReferences(string originalFolder, string deletedFolder, string? employeeId = null)
        {
            bool Matches(string? folder, string? id = null)
            {
                if (!string.IsNullOrWhiteSpace(employeeId) && !string.IsNullOrWhiteSpace(id)
                    && string.Equals(id, employeeId, StringComparison.OrdinalIgnoreCase))
                    return true;

                return (!string.IsNullOrWhiteSpace(originalFolder) && string.Equals(folder, originalFolder, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(deletedFolder) && string.Equals(folder, deletedFolder, StringComparison.OrdinalIgnoreCase));
            }

            var changed = false;

            changed |= _db.Advances.RemoveAll(a => Matches(a.EmployeeFolder)) > 0;
            changed |= _db.Accommodations.RemoveAll(a => Matches(a.EmployeeFolder)) > 0;

            foreach (var report in _db.Reports)
            {
                var removed = report.Entries.RemoveAll(e => Matches(e.EmployeeFolder, e.EmployeeId));
                if (removed > 0)
                {
                    report.UpdatedAt = DateTime.Now;
                    changed = true;
                }
            }

            if (changed)
                Save();

            CleanupPaymentFiles(Matches);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeMonth(string employeeFolder, string monthKey)
        {
            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && a.Month == monthKey)
                .OrderBy(a => a.Date)
                .ToList();
        }

        public List<AdvancePayment> GetAdvancesForEmployeeFirmMonth(string employeeFolder, string firmName, string monthKey)
        {
            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && (a.CompanyId == firmName || a.CompanyId == GlobalKey) && a.Month == monthKey)
                .OrderBy(a => a.Date)
                .ToList();
        }

        public decimal GetTotalAdvancesForEmployeeFirm(string employeeFolder, string firmName, string monthKey)
        {
            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && (a.CompanyId == firmName || a.CompanyId == GlobalKey) && a.Month == monthKey)
                .Sum(a => a.Amount);
        }

        public List<AdvancePayment> GetAllAdvancesForEmployee(string employeeFolder)
        {
            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder)
                .OrderByDescending(a => a.Date)
                .ToList();
        }

        public (decimal totalDebt, List<DebtInfoItem> details) CalculateCarriedDebt(string employeeFolder, int targetYear, int targetMonth)
        {
            return CalculateCarriedDebtForFirm(employeeFolder, null, targetYear, targetMonth);
        }

        public (decimal totalDebt, List<DebtInfoItem> details) CalculateCarriedDebtForFirm(string employeeFolder, string? firmName, int targetYear, int targetMonth)
        {
            var targetKey = $"{targetYear:D4}-{targetMonth:D2}";
            var savedPayments = LoadSavedPaymentsForEmployee(employeeFolder, firmName, targetKey);

            if (savedPayments.Count == 0)
                return (0, new List<DebtInfoItem>());

            var monthKeys = savedPayments.Keys.OrderBy(m => m).ToList();

            decimal runningDebt = 0;
            var debtDetails = new List<DebtInfoItem>();

            foreach (var mk in monthKeys)
            {
                var saved = savedPayments[mk];
                if (!saved.paid)
                    continue;

                if (saved.netSalary < 0)
                {
                    runningDebt = Math.Abs(saved.netSalary);
                    debtDetails.Clear();
                    debtDetails.Add(new DebtInfoItem { FromMonthKey = mk, Amount = runningDebt });
                }
                else
                {
                    runningDebt = 0;
                    debtDetails.Clear();
                }
            }

            return (runningDebt, debtDetails);
        }

        private Dictionary<string, (decimal netSalary, bool paid)> LoadSavedPaymentsForEmployee(
            string employeeFolder, string? firmName, string beforeMonthKey)
        {
            var result = new Dictionary<string, (decimal netSalary, bool paid)>();

            try
            {
                var salaryHistory = LoadSalaryHistory(employeeFolder);
                foreach (var r in salaryHistory)
                {
                    var mk = $"{r.Year:D4}-{r.Month:D2}";
                    if (string.Compare(mk, beforeMonthKey, StringComparison.Ordinal) >= 0) continue;
                    if (firmName != null && r.FirmName != firmName) continue;
                    result[mk] = (r.NetSalary, true);
                }
            }
            catch (Exception ex) { LoggingService.LogError("FinanceService.LoadSavedPaymentsForEmployee", ex); }

            if (firmName != null)
            {
                var resolvedEmployeeFolder = ResolveEmployeeFolder(employeeFolder);
                var employeeId = ResolveEmployeeId(resolvedEmployeeFolder);

                try
                {
                    if (_salaryDbService != null)
                    {
                        var sqliteSavedPayments = _salaryDbService.GetSavedPaymentsForEmployee(
                            resolvedEmployeeFolder,
                            employeeId,
                            firmName,
                            beforeMonthKey);

                        foreach (var pair in sqliteSavedPayments)
                            result.TryAdd(pair.Key, pair.Value);

                        return result;
                    }
                }
                catch (Exception ex) { LoggingService.LogError("FinanceService.LoadSavedPaymentsForEmployee", ex); }
            }

            return result;
        }

        #endregion

        #region Accommodations

        public void AddAccommodation(AccommodationRecord rec)
        {
            _db.Accommodations.Add(rec);
            Save();
        }

        public decimal GetAccommodationForEmployee(string employeeFolder, string companyId, int year, int month)
        {
            return _db.Accommodations
                .Where(a => a.EmployeeFolder == employeeFolder && a.CompanyId == companyId && a.Year == year && a.Month == month)
                .Sum(a => a.Amount);
        }

        public decimal GetAccommodationForEmployee(string employeeFolder, int year, int month)
        {
            return _db.Accommodations
                .Where(a => a.EmployeeFolder == employeeFolder && a.Year == year && a.Month == month)
                .Sum(a => a.Amount);
        }

        #endregion

        #region Firm Expenses

        public List<FirmExpense> GetFirmExpenses(int year, int month)
        {
            return _db.FirmExpenses.Where(e => e.Year == year && e.Month == month).ToList();
        }

        public List<FirmExpense> GetFirmExpenses(int year, int month, string firmName)
        {
            return _db.FirmExpenses.Where(e => e.Year == year && e.Month == month && e.FirmName == firmName).ToList();
        }

        public List<FirmExpense> GetFirmExpensesForFirms(int year, int month, IEnumerable<string> firmNames)
        {
            var set = new HashSet<string>(firmNames);
            return _db.FirmExpenses.Where(e => e.Year == year && e.Month == month && set.Contains(e.FirmName)).ToList();
        }

        public void AddFirmExpense(FirmExpense expense)
        {
            if (string.IsNullOrEmpty(expense.Id))
                expense.Id = Guid.NewGuid().ToString();
            _db.FirmExpenses.Add(expense);
            InvalidatePaymentsCache(expense.Year, expense.Month);
            Save();
        }

        public void UpdateFirmExpense(FirmExpense updated)
        {
            var idx = _db.FirmExpenses.FindIndex(e => e.Id == updated.Id);
            if (idx >= 0)
                _db.FirmExpenses[idx] = updated;
            InvalidatePaymentsCache(updated.Year, updated.Month);
            Save();
        }

        public void RemoveFirmExpense(string expenseId)
        {
            var removed = _db.FirmExpenses.FirstOrDefault(e => e.Id == expenseId);
            _db.FirmExpenses.RemoveAll(e => e.Id == expenseId);
            if (removed != null)
                InvalidatePaymentsCache(removed.Year, removed.Month);
            Save();
        }

        public void SaveFirmExpenses(List<FirmExpense> expenses, int year, int month)
        {
            _db.FirmExpenses.RemoveAll(e => e.Year == year && e.Month == month);
            _db.FirmExpenses.AddRange(expenses);
            InvalidatePaymentsCache(year, month);
            Save();
        }

        #endregion

        public void UpdateHourlyRateForward(string? employeeId, string employeeFolder, string firmName, decimal newRate, int fromYear, int fromMonth, CancellationToken cancellationToken = default)
        {
            var fromKey = $"{fromYear:D4}-{fromMonth:D2}";
            var resolvedEmployeeFolder = ResolveEmployeeFolder(employeeFolder, employeeId);

            try
            {
                _salaryDbService?.UpdateHourlyRateForward(employeeId, resolvedEmployeeFolder, firmName, newRate, fromKey, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("FinanceService.UpdateHourlyRateForward.SQLite", ex.Message);
            }

            InvalidatePaymentsCache();
        }

        private static string NormalizeEmployeePath(string? path)
            => (path ?? string.Empty).Replace('/', '\\').Trim().TrimEnd('\\');

        private static bool MatchesEmployee(SalaryEntry entry, string? employeeId, string normalizedEmployeeFolder)
        {
            if (!string.IsNullOrWhiteSpace(employeeId) && !string.IsNullOrWhiteSpace(entry.EmployeeId))
                return string.Equals(entry.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase);

            return string.Equals(NormalizeEmployeePath(entry.EmployeeFolder), normalizedEmployeeFolder, StringComparison.OrdinalIgnoreCase);
        }

        public bool SaveAllFirmPayments(int year, int month, List<SalaryEntry> allEntries, List<FirmExpense> allExpenses)
        {
            LastSaveRecoveryPath = null;
            var folderService = App.FolderService;
            if (folderService == null || string.IsNullOrEmpty(folderService.RootPath))
                return false;

            try
            {
                _salaryDbService?.SaveMonthPayments(year, month, CloneSalaryEntries(allEntries), CloneFirmExpenses(allExpenses));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.SaveAllFirmPayments.SQLite", ex);
                return false;
            }

            InvalidatePaymentsCache(year, month);
            LastSalaryConflictMessage = string.Empty;
            LastSaveRecoveryPath = null;
            return true;
        }

        public (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadAllFirmPayments(int year, int month, bool forceReload = false)
        {
            var cacheKey = (year, month);
            if (!forceReload)
            {
                lock (_paymentsCacheLock)
                {
                    if (_paymentsCache.TryGetValue(cacheKey, out var cached))
                        return (CloneSalaryEntries(cached.entries), CloneFirmExpenses(cached.expenses));
                }
            }

            if (_salaryDbService != null)
            {
                try
                {
                    if (_salaryDbService.MonthDbExists(year, month))
                    {
                        var sqliteResult = _salaryDbService.LoadMonthPayments(year, month);
                        var sqliteEntries = CloneSalaryEntries(sqliteResult.entries);
                        var sqliteExpenses = CloneFirmExpenses(sqliteResult.expenses);
                        lock (_paymentsCacheLock)
                        {
                            _paymentsCache[cacheKey] = (sqliteEntries, sqliteExpenses);
                        }

                        return (CloneSalaryEntries(sqliteEntries), CloneFirmExpenses(sqliteExpenses));
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("FinanceService.LoadAllFirmPayments.SQLite", ex.Message);
                }
            }
            return (new List<SalaryEntry>(), new List<FirmExpense>());
        }

        private string? ResolveEmployeeId(string employeeFolder)
        {
            if (string.IsNullOrWhiteSpace(employeeFolder))
                return null;

            try
            {
                return App.EmployeeService?.LoadEmployeeData(employeeFolder)?.UniqueId;
            }
            catch
            {
                return null;
            }
        }

        public void InvalidatePaymentsCache(int? year = null, int? month = null)
        {
            lock (_paymentsCacheLock)
            {
                if (year.HasValue && month.HasValue)
                {
                    _paymentsCache.Remove((year.Value, month.Value));
                    return;
                }

                _paymentsCache.Clear();
            }
        }

        private static List<SalaryEntry> CloneSalaryEntries(IEnumerable<SalaryEntry> source)
        {
#pragma warning disable CS0618
            return source.Select(entry => new SalaryEntry
            {
                EmployeeId = entry.EmployeeId,
                EmployeeFolder = entry.EmployeeFolder,
                FullName = entry.FullName,
                FirmName = entry.FirmName,
                HoursWorked = entry.HoursWorked,
                HourlyRate = entry.HourlyRate,
                Advance = entry.Advance,
                Advances = entry.Advances,
                Surcharge = entry.Surcharge,
                Accommodation = entry.Accommodation,
                OtherDeductions = entry.OtherDeductions,
                CustomValues = new Dictionary<string, decimal>(entry.CustomValues, StringComparer.OrdinalIgnoreCase),
                SavedNetSalary = entry.SavedNetSalary,
                Status = entry.Status,
                Note = entry.Note,
                ColorTag = entry.ColorTag,
                IsFinished = entry.IsFinished
            }).ToList();
#pragma warning restore CS0618
        }

        private static List<FirmExpense> CloneFirmExpenses(IEnumerable<FirmExpense> source)
        {
            return source.Select(expense => new FirmExpense
            {
                Id = expense.Id,
                FirmName = expense.FirmName,
                Year = expense.Year,
                Month = expense.Month,
                Name = expense.Name,
                Amount = expense.Amount
            }).ToList();
        }

        private void CleanupPaymentFiles(Func<string?, string?, bool> matches)
        {
            var processedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var paymentFolder in EnumeratePaymentFolders())
            {
                if (string.IsNullOrWhiteSpace(paymentFolder) || !Directory.Exists(paymentFolder))
                    continue;

                foreach (var file in Directory.GetFiles(paymentFolder, "salary_*.json"))
                {
                    if (!processedFiles.Add(file))
                        continue;

                    try
                    {
                        var data = ReadJson<FirmPaymentData>(file);
                        if (data == null)
                            continue;

                        var removed = data.Entries.RemoveAll(e => matches(e.EmployeeFolder, e.EmployeeId));
                        if (removed <= 0)
                            continue;

                        data.UpdatedAt = DateTime.Now;
                        WriteJsonAtomic(file, data);
                    }
                    catch (Exception ex)
                    {
                        LoggingService.LogError("FinanceService.CleanupPaymentFiles", ex);
                    }
                }
            }
        }

        private IEnumerable<string> EnumeratePaymentFolders()
        {
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var folderService = App.FolderService;
            var companies = App.CompanyService?.Companies;

            if (folderService == null || companies == null)
                return folders;

            foreach (var company in companies)
            {
                var paymentFolder = folderService.GetPaymentFolder(company.Name);
                if (!string.IsNullOrWhiteSpace(paymentFolder))
                    folders.Add(paymentFolder);
            }

            var archiveFolder = folderService.GetArchiveFolder();
            if (!string.IsNullOrWhiteSpace(archiveFolder) && Directory.Exists(archiveFolder))
            {
                foreach (var dir in Directory.GetDirectories(archiveFolder))
                {
                    var paymentFolder = FindPaymentFolder(dir);
                    if (!string.IsNullOrWhiteSpace(paymentFolder))
                        folders.Add(paymentFolder);
                }
            }

            return folders;
        }

        private static string? FindPaymentFolder(string parentDir)
        {
            foreach (var name in Helpers.FolderNames.AllPaymentFolderNames)
            {
                var path = Path.Combine(parentDir, name);
                if (Directory.Exists(path)) return path;
            }
            return null;
        }

        #region Salary History per Employee

        private const string SalaryHistoryFile = "salary_history.json";

        private readonly Dictionary<string, string> _idToFolderCache = new();
        private readonly HashSet<string> _ghostFolders = new(StringComparer.OrdinalIgnoreCase);

        private static bool IsGhostFolder(string dir)
        {
            var jsonPath = Path.Combine(dir, "employee.json");
            if (!File.Exists(jsonPath)) return false;
            try
            {
                var data = SafeFileService.ReadJson<EmployeeModels.EmployeeData>(jsonPath);
                return data != null && data.IsArchived;
            }
            catch (Exception ex) { LoggingService.LogError("FinanceService.IsGhostFolder", ex); return false; }
        }

        public void BuildEmployeeIdIndex()
        {
            _idToFolderCache.Clear();
            _ghostFolders.Clear();
            var folderService = App.FolderService;
            if (folderService == null || string.IsNullOrEmpty(folderService.RootPath)) return;

            var archiveFolder = folderService.GetArchiveFolder();

            var companies = App.CompanyService?.Companies;
            if (companies == null) return;
            foreach (var company in companies)
            {
                var empFolder = folderService.GetEmployeesFolder(company.Name);
                if (string.IsNullOrEmpty(empFolder) || !Directory.Exists(empFolder)) continue;
                try
                {
                    foreach (var dir in Directory.GetDirectories(empFolder))
                    {
                        var jsonPath = Path.Combine(dir, "employee.json");
                        if (!File.Exists(jsonPath)) continue;
                        try
                        {
                            var data = SafeFileService.ReadJson<EmployeeModels.EmployeeData>(jsonPath);
                            if (data == null) continue;
                            if (data.IsArchived)
                            {
                                _ghostFolders.Add(dir);
                                continue;
                            }
                            if (!string.IsNullOrEmpty(data.UniqueId))
                                _idToFolderCache[data.UniqueId] = dir;
                        }
                        catch (Exception innerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", innerEx); }
                    }
                }
                catch (Exception outerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", outerEx); }
            }

            if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
            {
                try
                {
                    foreach (var dir in Directory.GetDirectories(archiveFolder))
                    {
                        var jsonPath = Path.Combine(dir, "employee.json");
                        if (!File.Exists(jsonPath)) continue;
                        try
                        {
                            var data = SafeFileService.ReadJson<EmployeeModels.EmployeeData>(jsonPath);
                            if (data != null && !string.IsNullOrEmpty(data.UniqueId) && !_idToFolderCache.ContainsKey(data.UniqueId))
                                _idToFolderCache[data.UniqueId] = dir;
                        }
                        catch (Exception innerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", innerEx); }
                    }
                }
                catch (Exception outerEx) { LoggingService.LogError("FinanceService.BuildEmployeeIdIndex", outerEx); }
            }
        }

        public void CleanupGhostFolders()
        {
            foreach (var ghost in _ghostFolders.ToList())
            {
                try
                {
                    if (!Directory.Exists(ghost)) continue;
                    var folderName = Path.GetFileName(ghost.TrimEnd('\\', '/'));

                    var folderService = App.FolderService;
                    if (folderService == null) continue;
                    var archiveFolder = folderService.GetArchiveFolder();
                    bool existsElsewhere = false;

                    if (!string.IsNullOrEmpty(archiveFolder))
                    {
                        var archCandidate = Path.Combine(archiveFolder, folderName);
                        if (Directory.Exists(archCandidate) && !string.Equals(archCandidate, ghost, StringComparison.OrdinalIgnoreCase))
                            existsElsewhere = true;
                    }

                    if (!existsElsewhere)
                    {
                        var ghostCompanies = App.CompanyService?.Companies;
                        if (ghostCompanies != null)
                        foreach (var company in ghostCompanies)
                        {
                            var empFolder = folderService.GetEmployeesFolder(company.Name);
                            if (string.IsNullOrEmpty(empFolder)) continue;
                            var candidate = Path.Combine(empFolder, folderName);
                            if (Directory.Exists(candidate) && !string.Equals(candidate, ghost, StringComparison.OrdinalIgnoreCase))
                            {
                                var cJson = Path.Combine(candidate, "employee.json");
                                if (File.Exists(cJson))
                                {
                                    try
                                    {
                                        var d = SafeFileService.ReadJson<EmployeeModels.EmployeeData>(cJson);
                                        if (d != null && !d.IsArchived) { existsElsewhere = true; break; }
                                    }
                                    catch (Exception innerEx) { LoggingService.LogError("FinanceService.CleanupGhostFolders", innerEx); }
                                }
                            }
                        }
                    }

                    if (existsElsewhere)
                    {
                        foreach (var file in Directory.GetFiles(ghost, "*", SearchOption.AllDirectories))
                            File.SetAttributes(file, System.IO.FileAttributes.Normal);
                        Directory.Delete(ghost, true);
                        System.Diagnostics.Debug.WriteLine($"Cleaned ghost folder: {ghost}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CleanupGhostFolders error: {ex.Message}");
                    LoggingService.LogError("FinanceService.CleanupGhostFolders", ex);
                }
            }
            _ghostFolders.Clear();
        }

        public string? ResolveByEmployeeId(string employeeId)
        {
            if (string.IsNullOrEmpty(employeeId)) return null;
            if (_idToFolderCache.TryGetValue(employeeId, out var cached) && Directory.Exists(cached))
                return cached;

            BuildEmployeeIdIndex();
            if (_idToFolderCache.TryGetValue(employeeId, out cached) && Directory.Exists(cached))
                return cached;

            return null;
        }

        public string ResolveEmployeeFolder(string originalFolder, string? employeeId = null)
        {
            if (!string.IsNullOrEmpty(employeeId))
            {
                var byId = ResolveByEmployeeId(employeeId);
                if (byId != null) return byId;
            }

            if (!string.IsNullOrEmpty(originalFolder) && Directory.Exists(originalFolder))
            {
                if (!_ghostFolders.Contains(originalFolder))
                    return originalFolder;
            }

            var trimmed = originalFolder?.TrimEnd('\\', '/') ?? "";
            var folderName = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(folderName)) return originalFolder ?? "";

            var folderService = App.FolderService;
            if (folderService == null || string.IsNullOrEmpty(folderService.RootPath)) return originalFolder ?? "";

            var companies = App.CompanyService?.Companies;
            if (companies == null) return originalFolder ?? "";
            foreach (var company in companies)
            {
                var empFolder = folderService.GetEmployeesFolder(company.Name);
                if (string.IsNullOrEmpty(empFolder) || !Directory.Exists(empFolder)) continue;
                var candidate = Path.Combine(empFolder, folderName);
                if (Directory.Exists(candidate) && !_ghostFolders.Contains(candidate))
                    return candidate;
            }

            var archiveFolder = folderService.GetArchiveFolder();
            if (!string.IsNullOrEmpty(archiveFolder) && Directory.Exists(archiveFolder))
            {
                var candidate = Path.Combine(archiveFolder, folderName);
                if (Directory.Exists(candidate)) return candidate;
            }

            return originalFolder ?? "";
        }

        public void SaveSalaryHistoryRecord(string employeeFolder, SalaryHistoryRecord record)
        {
            employeeFolder = ResolveEmployeeFolder(employeeFolder);
            if (string.IsNullOrEmpty(employeeFolder) || !Directory.Exists(employeeFolder)) return;
            try
            {
                var filePath = Path.Combine(employeeFolder, SalaryHistoryFile);
                var records = LoadSalaryHistory(employeeFolder);

                records.RemoveAll(r => r.Year == record.Year && r.Month == record.Month && r.FirmName == record.FirmName);
                records.Add(record);
                records = records.OrderByDescending(r => r.Year).ThenByDescending(r => r.Month).ToList();

                WriteJsonAtomic(filePath, records);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveSalaryHistoryRecord error: {ex.Message}");
                LoggingService.LogError("FinanceService.SaveSalaryHistoryRecord", ex);
            }
        }

        public void RemoveSalaryHistoryRecord(string employeeFolder, int year, int month, string firmName)
        {
            employeeFolder = ResolveEmployeeFolder(employeeFolder);
            if (string.IsNullOrEmpty(employeeFolder) || !Directory.Exists(employeeFolder)) return;
            try
            {
                var filePath = Path.Combine(employeeFolder, SalaryHistoryFile);
                var records = LoadSalaryHistory(employeeFolder);
                var before = records.Count;
                records.RemoveAll(r => r.Year == year && r.Month == month && r.FirmName == firmName);
                if (records.Count == before) return;

                WriteJsonAtomic(filePath, records);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RemoveSalaryHistoryRecord error: {ex.Message}");
                LoggingService.LogError("FinanceService.RemoveSalaryHistoryRecord", ex);
            }
        }

        public List<SalaryHistoryRecord> LoadSalaryHistory(string employeeFolder)
        {
            try
            {
                employeeFolder = ResolveEmployeeFolder(employeeFolder);
                var filePath = Path.Combine(employeeFolder, SalaryHistoryFile);
                if (!File.Exists(filePath)) return new List<SalaryHistoryRecord>();
                return ReadJsonOrDefault(filePath, new List<SalaryHistoryRecord>());
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.LoadSalaryHistory", ex);
                return new List<SalaryHistoryRecord>();
            }
        }

        public SalaryHistoryRecord BuildHistoryRecord(SalaryEntry entry, int year, int month, List<CustomSalaryField>? fields)
        {
            var record = new SalaryHistoryRecord
            {
                Year = year,
                Month = month,
                FirmName = entry.FirmName,
                FullName = entry.FullName,
                HoursWorked = entry.HoursWorked,
                HourlyRate = entry.HourlyRate,
                GrossSalary = entry.GrossSalary,
                Advance = entry.Advance,
                NetSalary = entry.NetSalary,
                Note = entry.Note,
                CustomValues = new Dictionary<string, decimal>(entry.CustomValues)
            };

            if (fields != null)
            {
                foreach (var f in fields.Where(fd => fd.FirmName == AllFirmsKey || fd.FirmName == entry.FirmName))
                {
                    if (entry.CustomValues.TryGetValue(f.Id, out var val) && val != 0)
                    {
                        record.CustomFields.Add(new CustomFieldSnapshot
                        {
                            Name = f.Name,
                            Operation = f.Operation.ToString(),
                            Value = val
                        });
                    }
                }
            }

            return record;
        }

        #endregion
    }
}
