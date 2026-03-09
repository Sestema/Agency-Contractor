using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceService
    {
        private const string FinanceFileName = "finance_data.json";
        private readonly string _filePath;
        private FinanceDatabase _db;
        private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

        public const string GlobalKey = "__GLOBAL__";
        public const string AllFirmsKey = "__ALL__";

        public FinanceService(FolderService folderService)
        {
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

        private void MigrateFromAppDataIfNeeded(string rootPath)
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var oldPath = Path.Combine(appData, "AgencyContractor", FinanceFileName);
                var newPath = Path.Combine(rootPath, FinanceFileName);

                if (File.Exists(oldPath) && !File.Exists(newPath))
                {
                    File.Copy(oldPath, newPath);
                    File.Move(oldPath, oldPath + ".migrated", true);
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
            if (!File.Exists(_filePath)) return new FinanceDatabase();
            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<FinanceDatabase>(json) ?? new FinanceDatabase();
            }
            catch (Exception ex)
            {
                LoggingService.LogError("FinanceService.Load", ex);
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
                var json = JsonSerializer.Serialize(_db, _jsonOptions);
                var tmp = _filePath + ".tmp";
                File.WriteAllText(tmp, json);
                try
                {
                    File.Move(tmp, _filePath, true);
                }
                catch (Exception moveEx)
                {
                    LoggingService.LogError("FinanceService.Save", $"File.Move failed: {moveEx.Message}. Attempting rollback.");
                    try { if (File.Exists(tmp)) File.Copy(tmp, _filePath, true); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.Save", $"Cleanup failed: {ex.Message}"); }
                    throw;
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.Save", $"Cleanup failed: {ex.Message}"); }
                }
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
                File.Copy(_filePath, backupFile, true);

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
                try
                {
                    var folderService = App.FolderService;
                    if (folderService == null) return result;
                    var paymentFolder = folderService.GetPaymentFolder(firmName);
                    if (!string.IsNullOrEmpty(paymentFolder) && Directory.Exists(paymentFolder))
                    {
                        foreach (var file in Directory.GetFiles(paymentFolder, "salary_*.json"))
                        {
                            var fn = Path.GetFileNameWithoutExtension(file);
                            var parts = fn.Split('_');
                            if (parts.Length != 3) continue;
                            if (!int.TryParse(parts[1], out var y) || !int.TryParse(parts[2], out var m)) continue;
                            var mk = $"{y:D4}-{m:D2}";
                            if (string.Compare(mk, beforeMonthKey, StringComparison.Ordinal) >= 0) continue;
                            if (result.ContainsKey(mk)) continue;

                            var pd = LoadFirmPaymentFromFolder(firmName, y, m);
                            if (pd == null) continue;
                            var entry = pd.Entries.FirstOrDefault(e => e.EmployeeFolder == employeeFolder);
                            if (entry == null) continue;

                            var paid = entry.Status == "paid";
                            result.TryAdd(mk, (entry.SavedNetSalary, paid));
                        }
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
            Save();
        }

        public void UpdateFirmExpense(FirmExpense updated)
        {
            var idx = _db.FirmExpenses.FindIndex(e => e.Id == updated.Id);
            if (idx >= 0)
                _db.FirmExpenses[idx] = updated;
            Save();
        }

        public void RemoveFirmExpense(string expenseId)
        {
            _db.FirmExpenses.RemoveAll(e => e.Id == expenseId);
            Save();
        }

        public void SaveFirmExpenses(List<FirmExpense> expenses, int year, int month)
        {
            _db.FirmExpenses.RemoveAll(e => e.Year == year && e.Month == month);
            _db.FirmExpenses.AddRange(expenses);
            Save();
        }

        #endregion

        #region Per-Firm Shared Folder

        public void SaveFirmPaymentToFolder(string firmName, int year, int month,
            List<SalaryEntry> entries, List<FirmExpense> expenses)
        {
            var folderService = App.FolderService;
            if (folderService == null || string.IsNullOrEmpty(folderService.RootPath)) return;

            var paymentFolder = folderService.GetPaymentFolder(firmName);
            if (string.IsNullOrEmpty(paymentFolder)) return;

            Directory.CreateDirectory(paymentFolder);

            var data = new FirmPaymentData
            {
                Year = year,
                Month = month,
                FirmName = firmName,
                Entries = entries,
                Expenses = expenses,
                UpdatedAt = DateTime.Now
            };

            var fileName = $"salary_{year}_{month:D2}.json";
            var filePath = Path.Combine(paymentFolder, fileName);

            try
            {
                var json = JsonSerializer.Serialize(data, _jsonOptions);
                var tmp = filePath + ".tmp";
                File.WriteAllText(tmp, json);
                try
                {
                    File.Move(tmp, filePath, true);
                }
                catch (Exception moveEx)
                {
                    LoggingService.LogError("FinanceService.SaveFirmPaymentToFolder", $"File.Move failed: {moveEx.Message}. Attempting rollback.");
                    try { if (File.Exists(tmp)) File.Copy(tmp, filePath, true); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.SaveFirmPaymentToFolder", $"Cleanup failed: {ex.Message}"); }
                    throw;
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.SaveFirmPaymentToFolder", $"Cleanup failed: {ex.Message}"); }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveFirmPayment error ({firmName}): {ex.Message}");
                LoggingService.LogError("FinanceService.SaveFirmPaymentToFolder", ex);
            }
        }

        public FirmPaymentData? LoadFirmPaymentFromFolder(string firmName, int year, int month)
        {
            var folderService = App.FolderService;
            if (folderService == null || string.IsNullOrEmpty(folderService.RootPath)) return null;

            var paymentFolder = folderService.GetPaymentFolder(firmName);
            if (string.IsNullOrEmpty(paymentFolder)) return null;

            var fileName = $"salary_{year}_{month:D2}.json";
            var filePath = Path.Combine(paymentFolder, fileName);

            if (!File.Exists(filePath)) return null;

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<FirmPaymentData>(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadFirmPayment error ({firmName}): {ex.Message}");
                LoggingService.LogError("FinanceService.LoadFirmPaymentFromFolder", ex);
                return null;
            }
        }

        public void UpdateHourlyRateForward(string employeeFolder, string firmName, decimal newRate, int fromYear, int fromMonth)
        {
            var folderService = App.FolderService;
            if (folderService == null) return;
            var paymentFolder = folderService.GetPaymentFolder(firmName);
            if (string.IsNullOrEmpty(paymentFolder) || !Directory.Exists(paymentFolder)) return;

            var fromKey = $"{fromYear:D4}-{fromMonth:D2}";

            foreach (var file in Directory.GetFiles(paymentFolder, "salary_*.json"))
            {
                var fn = Path.GetFileNameWithoutExtension(file);
                var parts = fn.Split('_');
                if (parts.Length != 3) continue;
                if (!int.TryParse(parts[1], out var y) || !int.TryParse(parts[2], out var m)) continue;
                var mk = $"{y:D4}-{m:D2}";
                if (string.Compare(mk, fromKey, StringComparison.Ordinal) <= 0) continue;

                try
                {
                    var json = File.ReadAllText(file);
                    var data = JsonSerializer.Deserialize<FirmPaymentData>(json, _jsonOptions);
                    if (data == null) continue;

                    bool changed = false;
                    foreach (var entry in data.Entries)
                    {
                        if (entry.EmployeeFolder != employeeFolder) continue;
                        entry.HourlyRate = newRate;
                        changed = true;
                    }

                    if (changed)
                    {
                        var newJson = JsonSerializer.Serialize(data, _jsonOptions);
                        var tmp = file + ".tmp";
                        File.WriteAllText(tmp, newJson);
                        try
                        {
                            File.Move(tmp, file, true);
                        }
                        catch (Exception moveEx)
                        {
                            LoggingService.LogError("FinanceService.UpdateHourlyRateForward", $"File.Move failed: {moveEx.Message}. Attempting rollback.");
                            try { if (File.Exists(tmp)) File.Copy(tmp, file, true); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.UpdateHourlyRateForward", $"Cleanup failed: {ex.Message}"); }
                            throw;
                        }
                        finally
                        {
                            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.UpdateHourlyRateForward", $"Cleanup failed: {ex.Message}"); }
                        }
                    }
                }
                catch (Exception ex) { LoggingService.LogError("FinanceService.UpdateHourlyRateForward", ex); }
            }
        }

        public void SaveAllFirmPayments(int year, int month, List<SalaryEntry> allEntries, List<FirmExpense> allExpenses)
        {
            var firmGroups = allEntries.GroupBy(e => e.FirmName);
            foreach (var group in firmGroups)
            {
                var firmExpenses = allExpenses.Where(e => e.FirmName == group.Key).ToList();
                SaveFirmPaymentToFolder(group.Key, year, month, group.ToList(), firmExpenses);
            }
        }

        public (List<SalaryEntry> entries, List<FirmExpense> expenses) LoadAllFirmPayments(int year, int month)
        {
            var folderService = App.FolderService;
            var entries = new List<SalaryEntry>();
            var expenses = new List<FirmExpense>();

            if (folderService == null || string.IsNullOrEmpty(folderService.RootPath)) return (entries, expenses);

            var companies = App.CompanyService?.Companies;
            if (companies == null) return (entries, expenses);
            foreach (var company in companies)
            {
                var data = LoadFirmPaymentFromFolder(company.Name, year, month);
                if (data != null)
                {
                    entries.AddRange(data.Entries);
                    expenses.AddRange(data.Expenses);
                }
            }

            var archiveFolder = folderService.GetArchiveFolder();
            if (Directory.Exists(archiveFolder))
            {
                foreach (var dir in Directory.GetDirectories(archiveFolder))
                {
                    var firmName = Path.GetFileName(dir);
                    var paymentFolder = FindPaymentFolder(dir);
                    if (string.IsNullOrEmpty(paymentFolder)) continue;

                    var fileName = $"salary_{year}_{month:D2}.json";
                    var filePath = Path.Combine(paymentFolder, fileName);
                    if (!File.Exists(filePath)) continue;

                    try
                    {
                        var json = File.ReadAllText(filePath);
                        var data = JsonSerializer.Deserialize<FirmPaymentData>(json);
                        if (data != null)
                        {
                            foreach (var e in data.Entries.Where(e => !entries.Any(x => x.EmployeeFolder == e.EmployeeFolder && x.FirmName == e.FirmName)))
                                entries.Add(e);
                            foreach (var e in data.Expenses.Where(e => !expenses.Any(x => x.Id == e.Id)))
                                expenses.Add(e);
                        }
                    }
                    catch (Exception ex) { LoggingService.LogError("FinanceService.LoadAllFirmPayments", ex); }
                }
            }

            return (entries, expenses);
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

        #endregion

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
                var json = File.ReadAllText(jsonPath);
                var data = JsonSerializer.Deserialize<EmployeeModels.EmployeeData>(json);
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
                            var json = File.ReadAllText(jsonPath);
                            var data = JsonSerializer.Deserialize<EmployeeModels.EmployeeData>(json);
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
                            var json = File.ReadAllText(jsonPath);
                            var data = JsonSerializer.Deserialize<EmployeeModels.EmployeeData>(json);
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
                                        var d = JsonSerializer.Deserialize<EmployeeModels.EmployeeData>(File.ReadAllText(cJson));
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

                var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
                var tmp = filePath + ".tmp";
                File.WriteAllText(tmp, json);
                try
                {
                    File.Move(tmp, filePath, true);
                }
                catch (Exception moveEx)
                {
                    LoggingService.LogError("FinanceService.SaveSalaryHistoryRecord", $"File.Move failed: {moveEx.Message}. Attempting rollback.");
                    try { if (File.Exists(tmp)) File.Copy(tmp, filePath, true); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.SaveSalaryHistoryRecord", $"Cleanup failed: {ex.Message}"); }
                    throw;
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.SaveSalaryHistoryRecord", $"Cleanup failed: {ex.Message}"); }
                }
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

                var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
                var tmp = filePath + ".tmp";
                File.WriteAllText(tmp, json);
                try
                {
                    File.Move(tmp, filePath, true);
                }
                catch (Exception moveEx)
                {
                    LoggingService.LogError("FinanceService.RemoveSalaryHistoryRecord", $"File.Move failed: {moveEx.Message}. Attempting rollback.");
                    try { if (File.Exists(tmp)) File.Copy(tmp, filePath, true); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.RemoveSalaryHistoryRecord", $"Cleanup failed: {ex.Message}"); }
                    throw;
                }
                finally
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch (Exception ex) { LoggingService.LogWarning("FinanceService.RemoveSalaryHistoryRecord", $"Cleanup failed: {ex.Message}"); }
                }
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
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<List<SalaryHistoryRecord>>(json) ?? new List<SalaryHistoryRecord>();
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
