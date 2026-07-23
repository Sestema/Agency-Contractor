using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    /// <summary>
    /// Builds the same salary-month employee list that Finance shows in the UI,
    /// so dashboard / control panel counts match the visible table (not raw DB rows).
    /// </summary>
    public sealed class SalaryMonthDisplayService
    {
        private readonly FinanceService _financeService;
        private readonly EmployeeService _employeeService;
        private readonly CompanyService _companyService;

        public SalaryMonthDisplayService(
            FinanceService financeService,
            EmployeeService employeeService,
            CompanyService companyService)
        {
            _financeService = financeService ?? throw new ArgumentNullException(nameof(financeService));
            _employeeService = employeeService ?? throw new ArgumentNullException(nameof(employeeService));
            _companyService = companyService ?? throw new ArgumentNullException(nameof(companyService));
        }

        public sealed class BuildResult
        {
            public List<SalaryEntry> Entries { get; init; } = new();
            public bool NeedResave { get; init; }
            public Dictionary<string, HashSet<string>> ActiveFoldersByFirm { get; init; } =
                new(StringComparer.OrdinalIgnoreCase);
            public BuildTiming Timing { get; init; } = new();
        }

        public sealed class BuildTiming
        {
            public long TotalMs { get; set; }
            public long ArchivedMs { get; set; }
            public long FirmHistoryMs { get; set; }
            public long PeriodMapMs { get; set; }
            public long PrevMonthMs { get; set; }
            public long CurrentMonthMs { get; set; }
            public long CanonicalizeMs { get; set; }
            public long CanonicalizeResolveMs { get; set; }
            public long CanonicalizeIdLookupMs { get; set; }
            public long ActiveMissingMs { get; set; }
            public long ArchivedLoopMs { get; set; }
            public int CompaniesCount { get; set; }
            public int SharedEntriesCount { get; set; }
            public int PrevEntriesCount { get; set; }
            public int HistoryEntriesCount { get; set; }
            public int ActiveEmployeesCount { get; set; }
        }

        public sealed class EmployeesSnapshot
        {
            public Dictionary<string, List<EmployeeSummary>> EmployeesByFirm { get; } =
                new(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, string> FirstEmployeeIdByFullName { get; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Full displayed month list for dashboard / panel (same rules as Finance table).
        /// </summary>
        public List<SalaryEntry> BuildDisplayedEntries(int year, int month)
        {
            var companies = GetVisibleAccessibleCompanies(year, month);
            var snapshot = BuildEmployeesSnapshot(companies);
            var fields = _financeService.GetCustomFields();
            return BuildEntries(fields, year, month, companies, snapshot).Entries;
        }

        public List<EmployerCompany> GetVisibleAccessibleCompanies(int year, int month)
        {
            return _companyService.Companies?
                .Where(c => _companyService.IsCompanyVisibleForPeriod(c, year, month))
                .Where(PolicyService.CanAccessCompany)
                .ToList()
                ?? new List<EmployerCompany>();
        }

        public EmployeesSnapshot BuildEmployeesSnapshot(List<EmployerCompany> companies)
        {
            var snapshot = new EmployeesSnapshot();

            foreach (var company in companies)
            {
                var employees = _employeeService.GetEmployeesForFirm(company.Name).ToList();
                foreach (var firmName in GetKnownCompanyNames(company))
                    snapshot.EmployeesByFirm[firmName] = employees;

                foreach (var employee in employees)
                {
                    if (string.IsNullOrWhiteSpace(employee.FullName) || string.IsNullOrWhiteSpace(employee.UniqueId))
                        continue;

                    snapshot.FirstEmployeeIdByFullName.TryAdd(employee.FullName, employee.UniqueId);
                }
            }

            return snapshot;
        }

        public BuildResult BuildEntries(
            List<CustomSalaryField> fieldList,
            int year,
            int month,
            List<EmployerCompany> companies,
            EmployeesSnapshot employeesSnapshot)
        {
            var totalSw = Stopwatch.StartNew();
            var timing = new BuildTiming
            {
                CompaniesCount = companies.Count,
                ActiveEmployeesCount = employeesSnapshot.EmployeesByFirm.Values.Sum(v => v.Count)
            };
            var entries = new List<SalaryEntry>();
            var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allowedFirmNames = companies
                .SelectMany(GetKnownCompanyNames)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var needResave = false;
            var activeFoldersByFirm = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

            var allHistory = new List<ArchivedEmployeeSummary>();
            var archivedSw = Stopwatch.StartNew();
            allHistory.AddRange(_employeeService.GetArchivedEmployees());
            timing.ArchivedMs = archivedSw.ElapsedMilliseconds;

            var firmHistorySw = Stopwatch.StartNew();
            allHistory.AddRange(_employeeService.GetActiveEmployeeFirmHistory());
            allHistory.AddRange(_employeeService.GetArchivedEmployeeFirmHistory());
            timing.FirmHistoryMs = firmHistorySw.ElapsedMilliseconds;
            timing.HistoryEntriesCount = allHistory.Count;

            var periodMapSw = Stopwatch.StartNew();
            var employmentByKey = new Dictionary<string, List<(string StartDate, string EndDate)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var company in companies)
            {
                var employees = GetEmployeesForFirmSnapshot(company.Name, employeesSnapshot);
                foreach (var firmName in GetKnownCompanyNames(company))
                {
                    foreach (var emp in employees)
                    {
                        var key = BuildEmployeeFirmKey(emp.UniqueId, emp.EmployeeFolder, firmName);
                        AddEmploymentPeriod(employmentByKey, key, emp.StartDate, emp.EndDate);
                    }
                }
            }

            foreach (var arc in allHistory)
            {
                if (string.IsNullOrEmpty(arc.FirmName))
                    continue;
                if (!allowedFirmNames.Contains(arc.FirmName))
                    continue;

                var key = BuildEmployeeFirmKey(arc.UniqueId, arc.EmployeeFolder, arc.FirmName);
                AddEmploymentPeriod(employmentByKey, key, arc.StartDate, arc.EndDate);
            }
            timing.PeriodMapMs = periodMapSw.ElapsedMilliseconds;

            int prevYear = month == 1 ? year - 1 : year;
            int prevMonth = month == 1 ? 12 : month - 1;
            var prevMonthSw = Stopwatch.StartNew();
            var prevMonthResult = _financeService.TryLoadAllFirmPayments(prevYear, prevMonth);
            var prevEntries = prevMonthResult.success ? prevMonthResult.entries : new List<SalaryEntry>();
            timing.PrevMonthMs = prevMonthSw.ElapsedMilliseconds;
            timing.PrevEntriesCount = prevEntries.Count;
            var prevNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pe in prevEntries)
            {
                if (!string.IsNullOrEmpty(pe.Note))
                    prevNotes[BuildEmployeeFirmKey(pe.EmployeeId, pe.EmployeeFolder, pe.FirmName)] = pe.Note;
            }

            var currentMonthSw = Stopwatch.StartNew();
            var currentMonthResult = _financeService.TryLoadAllFirmPayments(year, month);
            var sharedEntries = currentMonthResult.success ? currentMonthResult.entries : new List<SalaryEntry>();
            timing.CurrentMonthMs = currentMonthSw.ElapsedMilliseconds;
            timing.SharedEntriesCount = sharedEntries.Count;

            var canonicalizeSw = Stopwatch.StartNew();
            foreach (var entry in sharedEntries)
            {
                if (!allowedFirmNames.Contains(entry.FirmName))
                    continue;

                var idLookupSw = Stopwatch.StartNew();
                var canonicalId = TryResolveEmployeeIdBackground(entry.EmployeeFolder, entry.FullName, employeesSnapshot, out var resolveInsideLookupMs);
                timing.CanonicalizeResolveMs += resolveInsideLookupMs;
                timing.CanonicalizeIdLookupMs += Math.Max(0, idLookupSw.ElapsedMilliseconds - resolveInsideLookupMs);
                if (!string.IsNullOrEmpty(canonicalId)
                    && !string.Equals(entry.EmployeeId, canonicalId, StringComparison.OrdinalIgnoreCase))
                {
                    entry.EmployeeId = canonicalId;
                    needResave = true;
                }

                var resolveSw = Stopwatch.StartNew();
                var resolved = _financeService.ResolveEmployeeFolder(entry.EmployeeFolder, entry.EmployeeId);
                timing.CanonicalizeResolveMs += resolveSw.ElapsedMilliseconds;
                if (resolved != entry.EmployeeFolder)
                {
                    entry.EmployeeFolder = resolved;
                    needResave = true;
                }

                var canonicalFirmName = ResolveCanonicalFirmName(entry.FirmName, companies);
                if (!string.Equals(entry.FirmName, canonicalFirmName, StringComparison.OrdinalIgnoreCase))
                {
                    entry.FirmName = canonicalFirmName;
                    needResave = true;
                }

                var key = BuildEmployeeFirmKey(entry.EmployeeId, entry.EmployeeFolder, entry.FirmName);
                if (!employmentByKey.TryGetValue(key, out var employmentPeriods)
                    || !WorkedInAnyEmploymentPeriod(employmentPeriods, year, month))
                {
                    if (entry.HasMeaningfulSalaryData)
                    {
                        if (existingKeys.Contains(key))
                            continue;
                        entry.FieldDefinitions = fieldList;
                        entry.RecalcNet();
                        entries.Add(entry);
                        existingKeys.Add(key);
                        continue;
                    }

                    needResave = true;
                    continue;
                }

                if (existingKeys.Contains(key))
                {
                    needResave = true;
                    continue;
                }

                entry.FieldDefinitions = fieldList;
                entry.RecalcNet();
                entries.Add(entry);
                existingKeys.Add(key);
            }
            timing.CanonicalizeMs = canonicalizeSw.ElapsedMilliseconds;

            var activeMissingSw = Stopwatch.StartNew();
            foreach (var company in companies)
            {
                var employees = GetEmployeesForFirmSnapshot(company.Name, employeesSnapshot);
                var activeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var emp in employees)
                {
                    var normalizedFolder = NormalizeEmployeePath(emp.EmployeeFolder);
                    if (emp.Status == "Active") activeNames.Add(normalizedFolder);
                    if (emp.Status != "Active") continue;
                    if (!WorkedInMonth(emp.StartDate, emp.EndDate, year, month)) continue;

                    if (HasExistingSalaryEntryForCompanyEmployee(emp.UniqueId, emp.EmployeeFolder, company, existingKeys))
                        continue;

                    var key = BuildEmployeeFirmKey(emp.UniqueId, emp.EmployeeFolder, company.Name);
                    TryGetInheritedNoteForCompanyEmployee(prevNotes, emp.UniqueId, emp.EmployeeFolder, company, out var inheritedNote);
                    var entry = new SalaryEntry
                    {
                        EmployeeId = emp.UniqueId,
                        EmployeeFolder = emp.EmployeeFolder,
                        FullName = emp.FullName,
                        FirmName = company.Name,
                        HourlyRate = TryGetHourlyRateFromEntriesForCompany(prevEntries, emp.UniqueId, emp.EmployeeFolder, company, out var previousRate)
                            ? previousRate
                            : GetDefaultRate(emp.EmployeeFolder),
                        HoursWorked = 0,
                        Note = inheritedNote ?? string.Empty,
                        FieldDefinitions = fieldList
                    };
                    entry.RecalcNet();
                    entries.Add(entry);
                    existingKeys.Add(key);
                }
                activeFoldersByFirm[company.Name] = activeNames;
            }
            timing.ActiveMissingMs = activeMissingSw.ElapsedMilliseconds;

            var archivedLoopSw = Stopwatch.StartNew();
            foreach (var arc in allHistory)
            {
                if (!WorkedInMonth(arc.StartDate, arc.EndDate, year, month)) continue;
                if (string.IsNullOrEmpty(arc.FirmName)) continue;
                if (!allowedFirmNames.Contains(arc.FirmName)) continue;

                var firmName = ResolveCanonicalFirmName(arc.FirmName, companies);
                var key = BuildEmployeeFirmKey(arc.UniqueId, arc.EmployeeFolder, firmName);
                if (existingKeys.Contains(key)) continue;

                prevNotes.TryGetValue(BuildEmployeeFirmKey(arc.UniqueId, arc.EmployeeFolder, arc.FirmName), out var inheritedNote);
                if (string.IsNullOrEmpty(inheritedNote))
                    prevNotes.TryGetValue(key, out inheritedNote);

                var historyCompany = FindCompanyByName(firmName);
                var historyRecord = TryGetSalaryHistoryRecord(arc.EmployeeFolder, arc.UniqueId, arc.FirmName, year, month)
                    ?? (historyCompany == null || string.Equals(arc.FirmName, firmName, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : TryGetSalaryHistoryRecord(arc.EmployeeFolder, arc.UniqueId, firmName, year, month));
                var entry = historyRecord != null
                    ? CreateSalaryEntryFromHistory(historyRecord, arc.UniqueId, arc.EmployeeFolder, arc.FullName, firmName, fieldList)
                    : new SalaryEntry
                    {
                        EmployeeId = arc.UniqueId,
                        EmployeeFolder = arc.EmployeeFolder,
                        FullName = arc.FullName,
                        FirmName = firmName,
                        HourlyRate = historyCompany != null
                            && TryGetHourlyRateFromEntriesForCompany(prevEntries, arc.UniqueId, arc.EmployeeFolder, historyCompany, out var previousRate)
                            ? previousRate
                            : TryGetHourlyRateFromEntries(prevEntries, arc.UniqueId, arc.EmployeeFolder, arc.FirmName, out previousRate)
                                ? previousRate
                                : GetDefaultRate(arc.EmployeeFolder),
                        HoursWorked = 0,
                        Note = inheritedNote ?? string.Empty,
                        FieldDefinitions = fieldList
                    };
                if (historyRecord == null)
                    entry.RecalcNet();
                entries.Add(entry);
                existingKeys.Add(key);
            }
            timing.ArchivedLoopMs = archivedLoopSw.ElapsedMilliseconds;
            timing.TotalMs = totalSw.ElapsedMilliseconds;

            return new BuildResult
            {
                Entries = entries,
                NeedResave = needResave,
                ActiveFoldersByFirm = activeFoldersByFirm,
                Timing = timing
            };
        }

        private static List<EmployeeSummary> GetEmployeesForFirmSnapshot(string companyName, EmployeesSnapshot snapshot)
        {
            return snapshot.EmployeesByFirm.TryGetValue(companyName, out var employees)
                ? employees
                : new List<EmployeeSummary>();
        }

        private string? TryResolveEmployeeIdBackground(
            string employeeFolder,
            string fullName,
            EmployeesSnapshot employeesSnapshot,
            out long resolveMs)
        {
            var resolveSw = Stopwatch.StartNew();
            var folder = _financeService.ResolveEmployeeFolder(employeeFolder);
            resolveMs = resolveSw.ElapsedMilliseconds;
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                var data = _employeeService.LoadEmployeeData(folder);
                if (data != null && !string.IsNullOrEmpty(data.UniqueId))
                    return data.UniqueId;
            }

            return employeesSnapshot.FirstEmployeeIdByFullName.TryGetValue(fullName, out var employeeId)
                ? employeeId
                : null;
        }

        private SalaryHistoryRecord? TryGetSalaryHistoryRecord(string employeeFolder, string? employeeId, string firmName, int year, int month)
        {
            try
            {
                var resolvedFolder = _financeService.ResolveEmployeeFolder(employeeFolder, employeeId);
                var history = _financeService.SalaryHistoryService.LoadSalaryHistoryFromResolvedFolder(resolvedFolder, employeeId);
                return history.FirstOrDefault(record =>
                    record.Year == year
                    && record.Month == month
                    && string.Equals(record.FirmName, firmName, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                LoggingService.LogError("SalaryMonthDisplayService.TryGetSalaryHistoryRecord", ex);
                return null;
            }
        }

        private static SalaryEntry CreateSalaryEntryFromHistory(
            SalaryHistoryRecord record,
            string employeeId,
            string employeeFolder,
            string fullName,
            string firmName,
            List<CustomSalaryField> fieldList)
        {
            var entry = new SalaryEntry
            {
                EmployeeId = employeeId,
                EmployeeFolder = employeeFolder,
                FullName = string.IsNullOrWhiteSpace(record.FullName) ? fullName : record.FullName,
                FirmName = firmName,
                HoursWorked = record.HoursWorked,
                HourlyRate = record.HourlyRate,
                Advance = record.Advance,
                SavedNetSalary = record.NetSalary,
                Status = "paid",
                Note = record.Note ?? string.Empty,
                CustomValues = new Dictionary<string, decimal>(record.CustomValues, StringComparer.OrdinalIgnoreCase),
                FieldDefinitions = fieldList
            };
            entry.RecalcNet();
            return entry;
        }

        private decimal GetDefaultRate(string employeeFolder)
        {
            var jsonPath = Path.Combine(employeeFolder, "employee.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    var json = SafeFileService.ReadAllText(jsonPath);
                    var data = JsonSerializer.Deserialize<EmployeeData>(json);
                    if (data != null && data.HourlySalary > 0)
                        return data.HourlySalary;
                }
                catch (Exception ex)
                {
                    LoggingService.LogError("SalaryMonthDisplayService.GetDefaultRate", ex);
                }
            }
            return 160;
        }

        private EmployerCompany? FindCompanyByName(string firmName)
        {
            return _companyService.Companies.FirstOrDefault(company =>
                GetKnownCompanyNames(company).Any(name =>
                    string.Equals(name, firmName, StringComparison.OrdinalIgnoreCase)));
        }

        private bool MatchesSalaryEntry(SalaryEntry existingEntry, string? employeeId, string employeeFolder, string firmName)
        {
            if (!string.Equals(existingEntry.FirmName, firmName, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.IsNullOrWhiteSpace(employeeId)
                && !string.IsNullOrWhiteSpace(existingEntry.EmployeeId)
                && string.Equals(existingEntry.EmployeeId, employeeId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var existingFolder = NormalizeEmployeePath(_financeService.ResolveEmployeeFolder(existingEntry.EmployeeFolder, existingEntry.EmployeeId));
            var currentFolder = NormalizeEmployeePath(_financeService.ResolveEmployeeFolder(employeeFolder, employeeId));
            return string.Equals(existingFolder, currentFolder, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetHourlyRateFromEntries(IReadOnlyList<SalaryEntry> sourceEntries, string? employeeId, string employeeFolder, string firmName, out decimal hourlyRate)
        {
            for (int i = sourceEntries.Count - 1; i >= 0; i--)
            {
                var entry = sourceEntries[i];
                if (!MatchesSalaryEntry(entry, employeeId, employeeFolder, firmName))
                    continue;

                hourlyRate = entry.HourlyRate;
                return true;
            }

            hourlyRate = 0;
            return false;
        }

        private bool TryGetHourlyRateFromEntriesForCompany(
            IReadOnlyList<SalaryEntry> sourceEntries,
            string? employeeId,
            string employeeFolder,
            EmployerCompany company,
            out decimal hourlyRate)
        {
            foreach (var firmName in GetKnownCompanyNames(company))
            {
                if (TryGetHourlyRateFromEntries(sourceEntries, employeeId, employeeFolder, firmName, out hourlyRate))
                    return true;
            }

            hourlyRate = 0;
            return false;
        }

        private static bool TryGetInheritedNoteForCompanyEmployee(
            IReadOnlyDictionary<string, string> prevNotes,
            string? employeeId,
            string? employeeFolder,
            EmployerCompany company,
            out string note)
        {
            foreach (var firmName in GetKnownCompanyNames(company))
            {
                if (prevNotes.TryGetValue(BuildEmployeeFirmKey(employeeId, employeeFolder, firmName), out note)
                    && !string.IsNullOrEmpty(note))
                {
                    return true;
                }
            }

            note = string.Empty;
            return false;
        }

        private static bool HasExistingSalaryEntryForCompanyEmployee(
            string? employeeId,
            string? employeeFolder,
            EmployerCompany company,
            ISet<string> existingKeys)
        {
            foreach (var firmName in GetKnownCompanyNames(company))
            {
                if (existingKeys.Contains(BuildEmployeeFirmKey(employeeId, employeeFolder, firmName)))
                    return true;
            }

            return false;
        }

        private static IEnumerable<string> GetKnownCompanyNames(EmployerCompany company)
        {
            if (!string.IsNullOrWhiteSpace(company.Name))
                yield return company.Name;

            foreach (var period in company.NameHistory ?? new List<CompanyNamePeriod>())
            {
                if (!string.IsNullOrWhiteSpace(period.Name))
                    yield return period.Name;
            }
        }

        private static string ResolveCanonicalFirmName(string firmName, IReadOnlyList<EmployerCompany> companies)
        {
            if (string.IsNullOrWhiteSpace(firmName))
                return firmName;

            foreach (var company in companies)
            {
                foreach (var knownName in GetKnownCompanyNames(company))
                {
                    if (string.Equals(knownName, firmName, StringComparison.OrdinalIgnoreCase))
                        return company.Name;
                }
            }

            return firmName;
        }

        private static string FolderKey(string path) =>
            Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        private static string NormalizeEmployeePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return path.Replace('/', '\\').Trim().TrimEnd('\\');
        }

        private static string BuildEmployeeFirmKey(string? employeeId, string? employeeFolder, string? firmName)
        {
            var identity = !string.IsNullOrWhiteSpace(employeeId)
                ? employeeId.Trim()
                : NormalizeEmployeePath(employeeFolder);

            if (string.IsNullOrWhiteSpace(identity))
                identity = FolderKey(employeeFolder ?? string.Empty);

            return identity + "|" + (firmName ?? string.Empty);
        }

        private static bool WorkedInMonth(string? startDate, string? endDate, int year, int month)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            var start = DateParsingHelper.TryParseDate(startDate ?? string.Empty);
            if (start == null)
                return false;

            if (start.Value > monthEnd)
                return false;

            if (string.IsNullOrWhiteSpace(endDate))
                return true;

            var end = DateParsingHelper.TryParseDate(endDate ?? string.Empty);
            if (end == null)
                return true;

            return end.Value >= monthStart;
        }

        private static void AddEmploymentPeriod(
            Dictionary<string, List<(string StartDate, string EndDate)>> employmentByKey,
            string key,
            string? startDate,
            string? endDate)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(startDate))
                return;

            if (!employmentByKey.TryGetValue(key, out var periods))
            {
                periods = new List<(string StartDate, string EndDate)>();
                employmentByKey[key] = periods;
            }

            if (!periods.Any(period =>
                    string.Equals(period.StartDate, startDate, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(period.EndDate, endDate ?? string.Empty, StringComparison.OrdinalIgnoreCase)))
            {
                periods.Add((startDate, endDate ?? string.Empty));
            }
        }

        private static bool WorkedInAnyEmploymentPeriod(
            IReadOnlyList<(string StartDate, string EndDate)> periods,
            int year,
            int month)
        {
            return periods.Any(period => WorkedInMonth(period.StartDate, period.EndDate, year, month));
        }
    }
}
