using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Win11DesktopApp.EmployeeModels;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    /// <summary>A person shown in the history cleanup window.</summary>
    public sealed class HistoryCleanupPerson
    {
        public string FullName { get; init; } = string.Empty;
        public string UniqueId { get; init; } = string.Empty;
        public string EmployeeFolder { get; init; } = string.Empty;
        public string CurrentFirm { get; init; } = string.Empty;
        public bool IsArchived { get; init; }
        public string Initials { get; init; } = string.Empty;
    }

    /// <summary>One line inside a firm period: what happened and when.</summary>
    public sealed class HistoryCleanupEvent
    {
        public DateTime When { get; init; }
        public string Text { get; init; } = string.Empty;

        /// <summary>"added", "archived", "restored", "salary" or "other".</summary>
        public string Kind { get; init; } = "other";
    }

    /// <summary>A firm period of one person and everything that would be removed with it.</summary>
    public sealed class HistoryCleanupPeriod
    {
        public FirmHistoryEntry Entry { get; init; } = new();
        public string FirmName => Entry.FirmName;
        public bool IsCurrent { get; init; }
        public bool CanDelete => string.IsNullOrEmpty(BlockReason);
        public string BlockReason { get; set; } = string.Empty;

        public List<HistoryCleanupEvent> Events { get; } = new();
        public List<EmployeeHistoryEntry> HistoryEntries { get; } = new();
        public List<ActivityLogEntry> ActivityEntries { get; } = new();
        public List<ArchiveLogEntry> ArchiveEntries { get; } = new();
        public List<SalaryDbService.EmployeeFirmSalaryRow> SalaryRows { get; } = new();

        /// <summary>Money already recorded for this period (paid months, advances, filled salary rows).</summary>
        public List<string> MoneyFound { get; } = new();
    }

    /// <summary>
    /// Records about a firm that has no period in the profile (for example a move whose end date was before its start).
    /// Only log records are removed for them; the profile and the payroll are not touched.
    /// </summary>
    public sealed class HistoryCleanupOrphanGroup
    {
        public string FirmName { get; init; } = string.Empty;
        public List<HistoryCleanupEvent> Events { get; } = new();
        public List<EmployeeHistoryEntry> HistoryEntries { get; } = new();
        public List<ActivityLogEntry> ActivityEntries { get; } = new();
        public List<ArchiveLogEntry> ArchiveEntries { get; } = new();
    }

    public sealed class HistoryCleanupDetails
    {
        public List<HistoryCleanupPeriod> Periods { get; init; } = new();
        public List<HistoryCleanupOrphanGroup> Orphans { get; init; } = new();
    }

    public sealed class HistoryCleanupResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string BackupPath { get; init; } = string.Empty;
    }

    /// <summary>
    /// Erases a firm period that was added by mistake (for example: added to the wrong firm, then archived and restored elsewhere).
    /// Removes the period from employee.json, the matching history, activity and archive log entries and empty salary rows.
    /// Never touches the current period and stops when money is already recorded for the period.
    /// </summary>
    public sealed class EmployeeHistoryCleanupService
    {
        private const int DaysBeforeStart = 31;
        private const int DaysAfterEnd = 7;

        private readonly EmployeeService _employeeService;
        private readonly CompanyService _companyService;
        private readonly ActivityLogService _activityLogService;
        private readonly SalaryDbService _salaryDbService;
        private readonly FinanceService _financeService;
        private readonly FolderService _folderService;
        private readonly AppDataStorageFactory _storageFactory;

        public EmployeeHistoryCleanupService(
            EmployeeService employeeService,
            CompanyService companyService,
            ActivityLogService activityLogService,
            SalaryDbService salaryDbService,
            FinanceService financeService,
            FolderService folderService,
            AppDataStorageFactory storageFactory)
        {
            _employeeService = employeeService;
            _companyService = companyService;
            _activityLogService = activityLogService;
            _salaryDbService = salaryDbService;
            _financeService = financeService;
            _folderService = folderService;
            _storageFactory = storageFactory;
        }

        /// <summary>Only local SQLite storage is supported; the server (PostgreSQL) mode is not.</summary>
        public bool IsSupported => _storageFactory.IsSqliteMode;

        public List<HistoryCleanupPerson> LoadPeople()
        {
            var people = new List<HistoryCleanupPerson>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var company in _companyService.Companies.ToList())
            {
                foreach (var employee in _employeeService.GetEmployeesForFirm(company.Name) ?? new List<EmployeeSummary>())
                {
                    if (string.IsNullOrWhiteSpace(employee.EmployeeFolder) || !seen.Add(employee.EmployeeFolder))
                        continue;
                    people.Add(new HistoryCleanupPerson
                    {
                        FullName = employee.FullName ?? string.Empty,
                        UniqueId = employee.UniqueId ?? string.Empty,
                        EmployeeFolder = employee.EmployeeFolder,
                        CurrentFirm = company.Name,
                        Initials = BuildInitials(employee.FullName)
                    });
                }
            }

            foreach (var archived in _employeeService.GetArchivedEmployees() ?? new List<ArchivedEmployeeSummary>())
            {
                if (string.IsNullOrWhiteSpace(archived.EmployeeFolder) || !seen.Add(archived.EmployeeFolder))
                    continue;
                people.Add(new HistoryCleanupPerson
                {
                    FullName = archived.FullName ?? string.Empty,
                    UniqueId = archived.UniqueId ?? string.Empty,
                    EmployeeFolder = archived.EmployeeFolder,
                    CurrentFirm = archived.FirmName ?? string.Empty,
                    IsArchived = true,
                    Initials = BuildInitials(archived.FullName)
                });
            }

            return people
                .OrderBy(person => person.FullName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        /// <summary>All firm periods of the person, newest first, each with what would be removed.</summary>
        public List<HistoryCleanupPeriod> LoadPeriods(HistoryCleanupPerson person)
            => LoadDetails(person).Periods;

        /// <summary>Periods plus records about firms that have no period in the profile.</summary>
        public HistoryCleanupDetails LoadDetails(HistoryCleanupPerson person)
        {
            var data = _employeeService.LoadEmployeeData(person.EmployeeFolder);
            if (data == null)
                return new HistoryCleanupDetails();

            var history = _employeeService.LoadHistory(person.EmployeeFolder);
            var activity = _activityLogService.GetAll();
            var archive = _employeeService.LoadArchiveLog();
            var periods = BuildPeriods(data, person, history, activity, archive);

            foreach (var period in periods.Where(period => period.CanDelete))
                AddSalaryFacts(person, data, period);

            return new HistoryCleanupDetails
            {
                Periods = periods,
                Orphans = BuildOrphans(periods, person, history, activity, archive)
            };
        }

        /// <summary>Removes only the log records about a firm that has no period in the profile.</summary>
        public async Task<HistoryCleanupResult> DeleteOrphansAsync(HistoryCleanupPerson person, string firmName)
        {
            if (!IsSupported)
                return Fail(Res("HistCleanupPostgresBlocked") ?? "Not available in server mode.");

            var data = _employeeService.LoadEmployeeData(person.EmployeeFolder);
            if (data == null)
                return Fail(Res("MsgProfileSaveFail") ?? "Profile not found.");

            var group = LoadDetails(person).Orphans.FirstOrDefault(candidate => SameFirm(candidate.FirmName, firmName));
            if (group == null)
                return Fail(Res("HistCleanupPeriodGone") ?? "The records were not found.");

            var backupPath = WriteBackup(person, new
            {
                CreatedAt = DateTime.Now,
                Person = person,
                OrphanFirm = group.FirmName,
                group.HistoryEntries,
                group.ActivityEntries,
                group.ArchiveEntries
            });

            foreach (var entry in group.HistoryEntries)
                await _employeeService.DeleteHistoryEntry(person.EmployeeFolder, data.UniqueId ?? string.Empty, entry);
            _activityLogService.RemoveEntriesByIds(group.ActivityEntries.Select(entry => entry.Id).ToList());
            await _employeeService.RemoveArchiveLogEntriesAsync(group.ArchiveEntries);

            LoggingService.LogInfo("EmployeeHistoryCleanupService",
                $"Removed records without a period for {group.FirmName} of {person.FullName}: " +
                $"history={group.HistoryEntries.Count}, activity={group.ActivityEntries.Count}, archive={group.ArchiveEntries.Count}. Backup: {backupPath}");

            return new HistoryCleanupResult { Success = true, BackupPath = backupPath };
        }

        public async Task<HistoryCleanupResult> DeletePeriodAsync(HistoryCleanupPerson person, FirmHistoryEntry target)
        {
            if (!IsSupported)
                return Fail(Res("HistCleanupPostgresBlocked") ?? "Not available in server mode.");

            // Re-read everything right before deleting, so nothing stale is removed.
            var data = _employeeService.LoadEmployeeData(person.EmployeeFolder);
            if (data == null)
                return Fail(Res("MsgProfileSaveFail") ?? "Profile not found.");

            var period = LoadPeriods(person).FirstOrDefault(candidate => SameEntry(candidate.Entry, target));
            if (period == null)
                return Fail(Res("HistCleanupPeriodGone") ?? "The period was not found.");
            if (!period.CanDelete)
                return Fail(period.BlockReason);

            var employeeJsonPath = Path.Combine(person.EmployeeFolder, "employee.json");
            var backupPath = WriteBackup(person, new
            {
                CreatedAt = DateTime.Now,
                Person = person,
                Period = period.Entry,
                EmployeeJsonBefore = File.Exists(employeeJsonPath) ? SafeFileService.ReadAllText(employeeJsonPath, Encoding.UTF8) : string.Empty,
                period.HistoryEntries,
                period.ActivityEntries,
                period.ArchiveEntries,
                period.SalaryRows
            });

            data.FirmHistory = (data.FirmHistory ?? new List<FirmHistoryEntry>())
                .Where(entry => !SameEntry(entry, period.Entry))
                .ToList();
            if (!_employeeService.SaveEmployeeData(person.EmployeeFolder, data, notifyUser: false))
                return Fail(Res("MsgProfileSaveFail") ?? "Profile was not saved.", backupPath);

            foreach (var entry in period.HistoryEntries)
                await _employeeService.DeleteHistoryEntry(person.EmployeeFolder, data.UniqueId ?? string.Empty, entry);

            _activityLogService.RemoveEntriesByIds(period.ActivityEntries.Select(entry => entry.Id).ToList());
            await _employeeService.RemoveArchiveLogEntriesAsync(period.ArchiveEntries);

            foreach (var month in period.SalaryRows.GroupBy(row => (row.Year, row.Month)))
                _salaryDbService.DeleteSalaryEntries(month.Key.Year, month.Key.Month, month.Select(row => row.Id).ToList());

            LoggingService.LogInfo("EmployeeHistoryCleanupService",
                $"Removed period {period.FirmName} {period.Entry.StartDate}-{period.Entry.EndDate} of {person.FullName}: " +
                $"history={period.HistoryEntries.Count}, activity={period.ActivityEntries.Count}, archive={period.ArchiveEntries.Count}, " +
                $"salaryRows={period.SalaryRows.Count}. Backup: {backupPath}");

            return new HistoryCleanupResult { Success = true, BackupPath = backupPath };
        }

        // ---------------------------------------------------------------- transfer undo

        private static readonly TimeSpan TransferMatchWindow = TimeSpan.FromMinutes(30);

        private sealed class TransferUndoPlan
        {
            public string Reason { get; set; } = string.Empty;
            public TransferSnapshot? Snapshot { get; set; }
            public EmployeeData? Current { get; set; }
            public EmployeeData? Archived { get; set; }
            public string CurrentFolder { get; set; } = string.Empty;
            public HistoryCleanupPerson Person { get; set; } = new();
            public List<EmployeeHistoryEntry> HistoryEntries { get; } = new();
            public List<ArchiveLogEntry> ArchiveEntries { get; } = new();
            public List<SalaryDbService.EmployeeFirmSalaryRow> SalaryRows { get; } = new();
        }

        /// <summary>Whether the restore with this operation id can be put back, and why not.</summary>
        public TransferUndoCheck CheckTransferUndo(string restoreOperationId)
        {
            var plan = PrepareTransferUndo(restoreOperationId);
            return new TransferUndoCheck
            {
                Reason = plan.Reason,
                EmployeeName = plan.Snapshot?.EmployeeName ?? string.Empty,
                OriginalFirm = plan.Snapshot?.OriginalFirm ?? string.Empty,
                RestoredFirm = plan.Snapshot?.RestoredFirm ?? string.Empty
            };
        }

        /// <summary>
        /// Puts a person restored into firm B back into firm A as if the archive and the restore never happened:
        /// the folder goes back to A, the A period is open again with its old dates and position, and the
        /// "archived" / "restored" records disappear from the employee history and the archive list.
        /// The activity log keeps everything; the caller adds one record about the undo.
        /// </summary>
        public async Task<TransferUndoResult> UndoTransferAsync(string restoreOperationId)
        {
            var plan = PrepareTransferUndo(restoreOperationId);
            if (!string.IsNullOrEmpty(plan.Reason) || plan.Snapshot == null || plan.Current == null || plan.Archived == null)
                return new TransferUndoResult { Message = plan.Reason };

            var snapshot = plan.Snapshot;
            var employeeJsonPath = Path.Combine(plan.CurrentFolder, "employee.json");
            var backupPath = WriteBackup(plan.Person, new
            {
                CreatedAt = DateTime.Now,
                Kind = "transfer-undo",
                Snapshot = snapshot,
                EmployeeJsonBefore = File.Exists(employeeJsonPath) ? SafeFileService.ReadAllText(employeeJsonPath, Encoding.UTF8) : string.Empty,
                plan.HistoryEntries,
                plan.ArchiveEntries,
                plan.SalaryRows
            });

            var profile = BuildUndoneProfile(plan.Current, plan.Archived, snapshot.OriginalFirm);
            var newFolder = await _employeeService.MoveEmployeeToFirmAsync(plan.CurrentFolder, snapshot.OriginalFirm, profile);
            if (string.IsNullOrWhiteSpace(newFolder))
                return new TransferUndoResult { Message = Res("TransferUndoMoveFailed") ?? "The folder could not be moved.", BackupPath = backupPath };

            foreach (var entry in plan.HistoryEntries)
                await _employeeService.DeleteHistoryEntry(newFolder, profile.UniqueId ?? string.Empty, entry);
            await _employeeService.RemoveArchiveLogEntriesAsync(plan.ArchiveEntries);
            foreach (var month in plan.SalaryRows.GroupBy(row => (row.Year, row.Month)))
                _salaryDbService.DeleteSalaryEntries(month.Key.Year, month.Key.Month, month.Select(row => row.Id).ToList());
            _employeeService.DeleteTransferSnapshot(restoreOperationId);

            LoggingService.LogInfo("EmployeeHistoryCleanupService",
                $"Transfer undone for {snapshot.EmployeeName}: {snapshot.OriginalFirm} -> {snapshot.RestoredFirm} -> {snapshot.OriginalFirm}. " +
                $"history={plan.HistoryEntries.Count}, archive={plan.ArchiveEntries.Count}, salaryRows={plan.SalaryRows.Count}. Backup: {backupPath}");

            return new TransferUndoResult
            {
                Success = true,
                NewFolder = newFolder,
                BackupPath = backupPath,
                EmployeeName = snapshot.EmployeeName,
                OriginalFirm = snapshot.OriginalFirm,
                RestoredFirm = snapshot.RestoredFirm
            };
        }

        private TransferUndoPlan PrepareTransferUndo(string restoreOperationId)
        {
            var plan = new TransferUndoPlan();
            string Stop(string key, string fallback) => plan.Reason = Res(key) ?? fallback;

            if (!IsSupported)
            {
                Stop("HistCleanupPostgresBlocked", "Not available in server mode.");
                return plan;
            }

            var snapshot = _employeeService.LoadTransferSnapshot(restoreOperationId);
            plan.Snapshot = snapshot;
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.ArchivedJson) || string.IsNullOrWhiteSpace(snapshot.OriginalFirm))
            {
                Stop("TransferUndoNoSnapshot", "There is no saved copy for this restore.");
                return plan;
            }

            var archive = _employeeService.LoadArchiveLog();
            var restored = archive.FirstOrDefault(entry =>
                string.Equals(entry.Action, "Restored", StringComparison.OrdinalIgnoreCase)
                && string.Equals(entry.OperationId, restoreOperationId, StringComparison.OrdinalIgnoreCase));
            var currentFolder = !string.IsNullOrWhiteSpace(restored?.EmployeeFolder) ? restored!.EmployeeFolder : snapshot.RestoredFolder;
            if (restored == null || string.IsNullOrWhiteSpace(currentFolder) || !Directory.Exists(currentFolder))
            {
                Stop("TransferUndoGone", "The person is no longer in this firm.");
                return plan;
            }

            var current = _employeeService.LoadEmployeeData(currentFolder);
            if (current == null || current.IsArchived
                || !string.Equals(current.UniqueId, snapshot.UniqueId, StringComparison.OrdinalIgnoreCase))
            {
                Stop("TransferUndoGone", "The person is no longer in this firm.");
                return plan;
            }

            EmployeeData? archived;
            try { archived = System.Text.Json.JsonSerializer.Deserialize<EmployeeData>(snapshot.ArchivedJson); }
            catch { archived = null; }
            if (archived == null)
            {
                Stop("TransferUndoNoSnapshot", "There is no saved copy for this restore.");
                return plan;
            }

            if (!string.IsNullOrWhiteSpace(_employeeService.FindEmployeeFolderInFirm(snapshot.OriginalFirm, snapshot.UniqueId)))
            {
                Stop("TransferUndoAlreadyThere", "The person is already in the first firm.");
                return plan;
            }

            var newFiles = NewFilesSince(snapshot.Files, EmployeeService.ListRelativeFiles(currentFolder));
            if (newFiles.Count > 0)
            {
                plan.Reason = string.Format(Res("TransferUndoBlockedFilesFmt") ?? "New files were added in the new firm: {0}",
                    string.Join(", ", newFiles.Take(3).Select(Path.GetFileName)));
                return plan;
            }

            if ((current.RequiredDocumentStatuses ?? new List<RequiredDocumentStatus>()).Any(status => !status.IsPeriodClosed))
            {
                Stop("TransferUndoBlockedDocs", "Documents were already marked in the new firm.");
                return plan;
            }

            var person = new HistoryCleanupPerson
            {
                FullName = snapshot.EmployeeName,
                UniqueId = snapshot.UniqueId,
                EmployeeFolder = currentFolder,
                CurrentFirm = snapshot.RestoredFirm,
                // Treated as finished here so the salary check looks at the B months themselves.
                IsArchived = true
            };
            plan.Person = person;

            var start = ParseDate(current.StartDate);
            if (start == null)
            {
                Stop("HistCleanupBlockedNoDates", "The period has no clear dates.");
                return plan;
            }
            var end = DateTime.Today > start.Value ? DateTime.Today : start.Value;
            var salaryCheck = new HistoryCleanupPeriod
            {
                Entry = new FirmHistoryEntry
                {
                    FirmName = snapshot.RestoredFirm,
                    StartDate = current.StartDate,
                    EndDate = end.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                }
            };
            AddSalaryFacts(person, current, salaryCheck);
            if (!string.IsNullOrEmpty(salaryCheck.BlockReason))
            {
                plan.Reason = salaryCheck.MoneyFound.Count > 0
                    ? $"{salaryCheck.BlockReason} {string.Join(", ", salaryCheck.MoneyFound.Take(3))}"
                    : salaryCheck.BlockReason;
                return plan;
            }
            plan.SalaryRows.AddRange(salaryCheck.SalaryRows);

            var restoredAt = ParseTimestamp(restored.Timestamp) ?? ParseTimestamp(snapshot.CreatedAt) ?? DateTime.Now;
            var archivedEntry = archive
                .Where(entry => string.Equals(entry.Action, "Archived", StringComparison.OrdinalIgnoreCase)
                                && !entry.IsReverted
                                && SameFirm(entry.FirmName, snapshot.OriginalFirm)
                                && string.Equals(entry.EmployeeFolder?.TrimEnd('\\', '/'), snapshot.ArchiveFolder.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                                && (ParseTimestamp(entry.Timestamp) ?? DateTime.MinValue) <= restoredAt)
                .OrderByDescending(entry => ParseTimestamp(entry.Timestamp) ?? DateTime.MinValue)
                .FirstOrDefault();
            plan.ArchiveEntries.Add(restored);
            if (archivedEntry != null)
                plan.ArchiveEntries.Add(archivedEntry);

            plan.HistoryEntries.AddRange(SelectTransferHistory(
                _employeeService.LoadHistory(currentFolder),
                snapshot.OriginalFirm,
                snapshot.RestoredFirm,
                archivedEntry == null ? null : ParseTimestamp(archivedEntry.Timestamp),
                restoredAt));

            plan.Current = current;
            plan.Archived = archived;
            plan.CurrentFolder = currentFolder;
            return plan;
        }

        /// <summary>Files that appeared after the restore; service files of the app are ignored.</summary>
        internal static List<string> NewFilesSince(IEnumerable<string>? before, IEnumerable<string> now)
        {
            var known = new HashSet<string>((before ?? Array.Empty<string>()).Select(NormalizeRelative), StringComparer.OrdinalIgnoreCase);
            return now
                .Where(file => !IsServiceFile(file) && !known.Contains(NormalizeRelative(file)))
                .ToList();
        }

        private static string NormalizeRelative(string path) => path.Replace('/', '\\').Trim('\\');

        private static bool IsServiceFile(string relativePath)
        {
            var name = Path.GetFileName(relativePath);
            return name.Equals("employee.json", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("history.json", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                   || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)
                   || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("~$", StringComparison.Ordinal);
        }

        /// <summary>
        /// The profile after the undo: today's profile with the firm A values from the archive copy
        /// (start date, contract, position, address, document package and firm periods) and the A period open again.
        /// </summary>
        internal static EmployeeData BuildUndoneProfile(EmployeeData current, EmployeeData archived, string originalFirm)
        {
            current.StartDate = archived.StartDate;
            current.ContractSignDate = archived.ContractSignDate;
            current.PositionTag = archived.PositionTag;
            current.PositionNumber = archived.PositionNumber;
            current.WorkAddressTag = archived.WorkAddressTag;
            current.RequiredDocumentStatuses = archived.RequiredDocumentStatuses ?? new List<RequiredDocumentStatus>();
            current.FirmHistory = (archived.FirmHistory ?? new List<FirmHistoryEntry>())
                .Select(entry => new FirmHistoryEntry { FirmName = entry.FirmName, StartDate = entry.StartDate, EndDate = entry.EndDate })
                .ToList();

            // Same as undoing an archive: the A period has no end any more.
            var open = current.FirmHistory.LastOrDefault(entry =>
                SameFirm(entry.FirmName, originalFirm)
                && string.Equals(entry.StartDate?.Trim(), archived.StartDate?.Trim(), StringComparison.Ordinal));
            if (open != null)
                open.EndDate = string.Empty;

            current.EndDate = string.Empty;
            current.IsArchived = false;
            current.ArchivedFromFirm = string.Empty;
            current.Status = "Active";
            return current;
        }

        /// <summary>The "archived from A" and "restored into B" records of this one transfer.</summary>
        internal static List<EmployeeHistoryEntry> SelectTransferHistory(
            IEnumerable<EmployeeHistoryEntry> history,
            string originalFirm,
            string restoredFirm,
            DateTime? archivedAt,
            DateTime restoredAt)
        {
            var items = history.ToList();
            var result = new List<EmployeeHistoryEntry>();

            var restoredItem = items
                .Where(item => item.EventType == "Restored" && SameFirm(item.Field, restoredFirm)
                               && (item.Timestamp - restoredAt).Duration() <= TransferMatchWindow)
                .OrderBy(item => (item.Timestamp - restoredAt).Duration())
                .FirstOrDefault();
            if (restoredItem != null)
                result.Add(restoredItem);

            var archivedCandidates = items
                .Where(item => item.EventType == "Archived" && SameFirm(item.OldValue, originalFirm)
                               && item.Timestamp <= restoredAt + TransferMatchWindow);
            var archivedItem = archivedAt == null
                ? archivedCandidates.OrderByDescending(item => item.Timestamp).FirstOrDefault()
                : archivedCandidates
                    .Where(item => (item.Timestamp - archivedAt.Value).Duration() <= TransferMatchWindow)
                    .OrderBy(item => (item.Timestamp - archivedAt.Value).Duration())
                    .FirstOrDefault();
            if (archivedItem != null)
                result.Add(archivedItem);

            return result;
        }

        // ---------------------------------------------------------------- matching

        internal static List<HistoryCleanupPeriod> BuildPeriods(
            EmployeeData data,
            HistoryCleanupPerson person,
            IReadOnlyList<EmployeeHistoryEntry> history,
            IReadOnlyList<ActivityLogEntry> activity,
            IReadOnlyList<ArchiveLogEntry> archive)
        {
            var all = (data.FirmHistory ?? new List<FirmHistoryEntry>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry.FirmName))
                .ToList();
            var distinct = all
                .GroupBy(entry => (entry.FirmName.Trim().ToLowerInvariant(), entry.StartDate.Trim(), entry.EndDate.Trim()))
                .Select(group => group.First())
                .ToList();

            // FirmHistory keeps only finished periods; the current firm of an active person lives in the profile itself.
            var closed = distinct.Where(entry => !string.IsNullOrWhiteSpace(entry.EndDate)).ToList();
            var hasOpenEntry = closed.Count != distinct.Count;
            var current = hasOpenEntry ? null : BuildCurrentEntry(data, person);
            var timeline = current == null ? distinct : distinct.Append(current).ToList();

            var result = new List<HistoryCleanupPeriod>();
            foreach (var entry in timeline)
            {
                var isCurrent = string.IsNullOrWhiteSpace(entry.EndDate);
                var period = new HistoryCleanupPeriod { Entry = entry, IsCurrent = isCurrent };
                period.BlockReason = GetBlockReason(entry, isCurrent, timeline.Count, data);

                if (TryGetWindows(entry, timeline, out var windows))
                    MatchEntries(period, person, windows, history, activity, archive);

                result.Add(period);
            }

            return result
                .OrderByDescending(period => period.IsCurrent)
                .ThenByDescending(period => ParseDate(period.Entry.StartDate) ?? DateTime.MinValue)
                .ToList();
        }

        /// <summary>The current firm of an active person as a period without an end date; null for archived people.</summary>
        internal static FirmHistoryEntry? BuildCurrentEntry(EmployeeData data, HistoryCleanupPerson person)
        {
            if (person.IsArchived || data.IsArchived || string.IsNullOrWhiteSpace(person.CurrentFirm))
                return null;
            return new FirmHistoryEntry { FirmName = person.CurrentFirm, StartDate = data.StartDate ?? string.Empty, EndDate = string.Empty };
        }

        private static string GetBlockReason(FirmHistoryEntry entry, bool isCurrent, int periodCount, EmployeeData data)
        {
            if (isCurrent)
                return Res("HistCleanupBlockedCurrent") ?? "This is the current firm.";
            if (periodCount <= 1)
                return Res("HistCleanupBlockedOnly") ?? "This is the only period of the person.";
            if (ParseDate(entry.StartDate) == null || ParseDate(entry.EndDate) == null)
                return Res("HistCleanupBlockedNoDates") ?? "The period has no clear dates.";
            if (data.IsArchived && string.Equals(data.ArchivedFromFirm, entry.FirmName, StringComparison.OrdinalIgnoreCase))
            {
                var latestEnd = (data.FirmHistory ?? new List<FirmHistoryEntry>())
                    .Select(item => ParseDate(item.EndDate) ?? DateTime.MinValue)
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                if ((ParseDate(entry.EndDate) ?? DateTime.MinValue) >= latestEnd)
                    return Res("HistCleanupBlockedArchivedHere") ?? "The person is in the archive of this firm now.";
            }

            return string.Empty;
        }

        /// <summary>
        /// Time windows for one period, cut so they never reach another period in the same firm:
        /// "start" events (added, restored into the firm) belong before the period ends,
        /// "end" events (archived) belong between the start and a few days after the end.
        /// </summary>
        internal readonly record struct PeriodWindows(DateTime StartFrom, DateTime StartTo, DateTime EndFrom, DateTime EndTo);

        internal static bool TryGetWindows(FirmHistoryEntry entry, IReadOnlyList<FirmHistoryEntry> all, out PeriodWindows windows)
        {
            windows = default;
            var start = ParseDate(entry.StartDate);
            if (start == null)
                return false;
            var end = ParseDate(entry.EndDate);
            var endDay = end ?? DateTime.MaxValue.Date.AddDays(-DaysAfterEnd - 1);

            var startFrom = start.Value.AddDays(-DaysBeforeStart);
            var startTo = endDay.AddDays(1).AddTicks(-1);
            var endFrom = start.Value;
            var endTo = endDay.AddDays(DaysAfterEnd + 1).AddTicks(-1);

            foreach (var other in all)
            {
                if (ReferenceEquals(other, entry) || !SameFirm(other.FirmName, entry.FirmName))
                    continue;
                var otherStart = ParseDate(other.StartDate);
                if (otherStart == null)
                    continue;
                var otherEnd = ParseDate(other.EndDate) ?? DateTime.MaxValue.Date.AddDays(-1);

                if (otherEnd < start.Value && otherEnd.AddDays(1) > startFrom)
                    startFrom = otherEnd.AddDays(1);
                if (otherStart.Value > start.Value)
                {
                    var limit = otherStart.Value.AddDays(-DaysBeforeStart).AddTicks(-1);
                    if (limit < startTo) startTo = limit < start.Value ? start.Value.AddDays(1).AddTicks(-1) : limit;
                    if (otherStart.Value.AddTicks(-1) < endTo) endTo = otherStart.Value.AddTicks(-1);
                }
            }

            windows = new PeriodWindows(startFrom, startTo, endFrom, endTo);
            return true;
        }

        private static void MatchEntries(
            HistoryCleanupPeriod period,
            HistoryCleanupPerson person,
            PeriodWindows windows,
            IReadOnlyList<EmployeeHistoryEntry> history,
            IReadOnlyList<ActivityLogEntry> activity,
            IReadOnlyList<ArchiveLogEntry> archive)
        {
            var firm = period.FirmName;
            bool NearStart(DateTime when) => when >= windows.StartFrom && when <= windows.StartTo;
            bool NearEnd(DateTime when) => when >= windows.EndFrom && when <= windows.EndTo;
            var endDate = period.Entry.EndDate?.Trim() ?? string.Empty;
            bool IsEndDate(string? text) => endDate.Length > 0 && string.Equals(text?.Trim(), endDate, StringComparison.Ordinal);

            foreach (var item in history)
            {
                var when = item.Timestamp;
                var matches = item.EventType switch
                {
                    "Created" => SameFirm(item.Field, firm) && NearStart(when),
                    "Restored" => SameFirm(item.Field, firm) && NearStart(when),
                    "Archived" => SameFirm(item.OldValue, firm) && endDate.Length > 0 && (IsEndDate(item.NewValue) || NearEnd(when)),
                    "ArchiveUndone" => (SameFirm(item.Field, firm) || SameFirm(item.OldValue, firm) || SameFirm(item.NewValue, firm)) && NearEnd(when),
                    _ => false
                };
                if (!matches)
                    continue;

                period.HistoryEntries.Add(item);
                period.Events.Add(new HistoryCleanupEvent { When = when, Text = Describe(item), Kind = KindOf(item.EventType) });
            }

            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { person.EmployeeFolder };
            foreach (var item in archive)
            {
                if (!SameFirm(item.FirmName, firm) || !SamePerson(item.EmployeeName, item.EmployeeFolder, person))
                    continue;
                var when = ParseDate(item.Date) ?? ParseTimestamp(item.Timestamp) ?? DateTime.MinValue;
                var matches = string.Equals(item.Action, "Archived", StringComparison.OrdinalIgnoreCase)
                    ? endDate.Length > 0 && (IsEndDate(item.Date) || NearEnd(when))
                    : NearStart(when);
                if (!matches)
                    continue;

                period.ArchiveEntries.Add(item);
                if (!string.IsNullOrWhiteSpace(item.EmployeeFolder))
                    folders.Add(item.EmployeeFolder);
            }

            foreach (var item in activity)
            {
                if (item.ActionType is not ("EmployeeAdded" or "EmployeeArchived" or "EmployeeRestored" or "ArchiveUndone"))
                    continue;
                if (!SameFirm(item.FirmName, firm) || !SamePerson(item.EmployeeName, item.EmployeeFolder, person, folders))
                    continue;
                var when = ParseTimestamp(item.Timestamp);
                if (when == null)
                    continue;
                var isEndEvent = item.ActionType is "EmployeeArchived" or "ArchiveUndone";
                var endMatches = endDate.Length > 0
                    && (NearEnd(when.Value) || (item.Description ?? string.Empty).Contains(endDate, StringComparison.Ordinal));
                if (isEndEvent ? !endMatches : !NearStart(when.Value))
                    continue;

                period.ActivityEntries.Add(item);
                if (period.HistoryEntries.Count == 0)
                    period.Events.Add(new HistoryCleanupEvent { When = when.Value, Text = item.Description ?? string.Empty, Kind = KindOfActivity(item.ActionType) });
            }

            period.Events.Sort((a, b) => a.When.CompareTo(b.When));
        }

        private static readonly HashSet<string> MoveHistoryEvents = new(StringComparer.Ordinal) { "Created", "Archived", "Restored", "ArchiveUndone" };
        private static readonly HashSet<string> MoveActivityEvents = new(StringComparer.Ordinal) { "EmployeeAdded", "EmployeeArchived", "EmployeeRestored", "ArchiveUndone" };

        /// <summary>Firm named by a move record in the profile history ("Архів" is not a firm).</summary>
        internal static string FirmOfHistoryEntry(EmployeeHistoryEntry item) => item.EventType switch
        {
            "Created" or "Restored" => item.Field?.Trim() ?? string.Empty,
            "Archived" => item.OldValue?.Trim() ?? string.Empty,
            "ArchiveUndone" => !string.IsNullOrWhiteSpace(item.NewValue) ? item.NewValue.Trim() : item.OldValue?.Trim() ?? string.Empty,
            _ => string.Empty
        };

        /// <summary>
        /// Move records about firms that have no period at all (for example a move whose end date was before its start).
        /// Firms with a period are never listed here, so records of real periods can't be removed this way.
        /// </summary>
        internal static List<HistoryCleanupOrphanGroup> BuildOrphans(
            IReadOnlyList<HistoryCleanupPeriod> periods,
            HistoryCleanupPerson person,
            IReadOnlyList<EmployeeHistoryEntry> history,
            IReadOnlyList<ActivityLogEntry> activity,
            IReadOnlyList<ArchiveLogEntry> archive)
        {
            bool HasPeriod(string firm) => periods.Any(period => SameFirm(period.FirmName, firm));
            var groups = new Dictionary<string, HistoryCleanupOrphanGroup>(StringComparer.OrdinalIgnoreCase);
            HistoryCleanupOrphanGroup GroupFor(string firm)
            {
                var key = firm.Trim();
                if (!groups.TryGetValue(key, out var group))
                    groups[key] = group = new HistoryCleanupOrphanGroup { FirmName = key };
                return group;
            }

            foreach (var item in history.Where(item => MoveHistoryEvents.Contains(item.EventType)))
            {
                var firm = FirmOfHistoryEntry(item);
                if (string.IsNullOrWhiteSpace(firm) || HasPeriod(firm))
                    continue;
                var group = GroupFor(firm);
                group.HistoryEntries.Add(item);
                group.Events.Add(new HistoryCleanupEvent { When = item.Timestamp, Text = Describe(item), Kind = KindOf(item.EventType) });
            }

            foreach (var item in activity.Where(item => MoveActivityEvents.Contains(item.ActionType)))
            {
                if (string.IsNullOrWhiteSpace(item.FirmName) || HasPeriod(item.FirmName)
                    || !SamePerson(item.EmployeeName, item.EmployeeFolder, person))
                    continue;
                var group = GroupFor(item.FirmName);
                group.ActivityEntries.Add(item);
                if (group.HistoryEntries.Count == 0)
                    group.Events.Add(new HistoryCleanupEvent { When = ParseTimestamp(item.Timestamp) ?? DateTime.MinValue, Text = item.Description ?? string.Empty, Kind = KindOfActivity(item.ActionType) });
            }

            foreach (var item in archive)
            {
                if (string.IsNullOrWhiteSpace(item.FirmName) || HasPeriod(item.FirmName)
                    || !SamePerson(item.EmployeeName, item.EmployeeFolder, person))
                    continue;
                GroupFor(item.FirmName).ArchiveEntries.Add(item);
            }

            foreach (var group in groups.Values)
                group.Events.Sort((a, b) => a.When.CompareTo(b.When));

            return groups.Values
                .Where(group => group.HistoryEntries.Count + group.ActivityEntries.Count + group.ArchiveEntries.Count > 0)
                .OrderBy(group => group.FirmName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void AddSalaryFacts(HistoryCleanupPerson person, EmployeeData data, HistoryCleanupPeriod period)
        {
            var start = ParseDate(period.Entry.StartDate);
            var end = ParseDate(period.Entry.EndDate);
            if (start == null || end == null)
                return;

            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { person.EmployeeFolder };
            foreach (var item in period.ArchiveEntries.Where(item => !string.IsNullOrWhiteSpace(item.EmployeeFolder)))
                folders.Add(item.EmployeeFolder);
            foreach (var item in period.ActivityEntries.Where(item => !string.IsNullOrWhiteSpace(item.EmployeeFolder)))
                folders.Add(item.EmployeeFolder);

            // A month stays untouched when another period in the same firm also covers it.
            var otherSameFirm = (data.FirmHistory ?? new List<FirmHistoryEntry>())
                .Where(entry => !SameEntry(entry, period.Entry) && SameFirm(entry.FirmName, period.FirmName))
                .ToList();
            var current = BuildCurrentEntry(data, person);
            if (current != null && SameFirm(current.FirmName, period.FirmName))
                otherSameFirm.Add(current);

            var monthText = new CultureInfo("uk-UA");
            for (var month = new DateTime(start.Value.Year, start.Value.Month, 1); month <= end.Value; month = month.AddMonths(1))
            {
                if (otherSameFirm.Any(other => CoversMonth(other, month)))
                    continue;

                var label = month.ToString("MM.yyyy", CultureInfo.InvariantCulture);
                List<SalaryDbService.EmployeeFirmSalaryRow> rows;
                try
                {
                    rows = _salaryDbService.FindEmployeeFirmRows(month.Year, month.Month, period.FirmName, person.UniqueId, folders.ToList());
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("EmployeeHistoryCleanupService.Salary", ex.Message);
                    period.BlockReason = Res("HistCleanupBlockedSalaryUnreadable") ?? "Salary data could not be read.";
                    return;
                }

                foreach (var row in rows)
                {
                    if (row.HasMeaningfulData)
                        period.MoneyFound.Add(string.Format(Res("HistCleanupMoneySalaryFmt") ?? "Salary {0}: hours or amounts entered", label));
                    else
                        period.SalaryRows.Add(row);
                }

                foreach (var folder in folders)
                {
                    if (_financeService.GetAdvancesForEmployeeFirmMonth(folder, period.FirmName, $"{month.Year:D4}-{month.Month:D2}").Count > 0)
                    {
                        period.MoneyFound.Add(string.Format(Res("HistCleanupMoneyAdvanceFmt") ?? "Advance {0}", label));
                        break;
                    }
                }
            }

            foreach (var folder in folders)
            {
                foreach (var record in _financeService.LoadSalaryHistory(folder))
                {
                    if (!SameFirm(record.FirmName, period.FirmName))
                        continue;
                    var month = new DateTime(record.Year, Math.Clamp(record.Month, 1, 12), 1);
                    if (month < new DateTime(start.Value.Year, start.Value.Month, 1) || month > end.Value)
                        continue;
                    period.MoneyFound.Add(string.Format(Res("HistCleanupMoneyPaidFmt") ?? "Paid {0}", month.ToString("MM.yyyy", CultureInfo.InvariantCulture)));
                }
            }

            if (period.MoneyFound.Count > 0)
            {
                period.MoneyFound.Sort(StringComparer.Ordinal);
                var unique = period.MoneyFound.Distinct().ToList();
                period.MoneyFound.Clear();
                period.MoneyFound.AddRange(unique);
                period.BlockReason = Res("HistCleanupBlockedMoney") ?? "Money is already recorded for this period.";
            }

            foreach (var row in period.SalaryRows)
            {
                period.Events.Add(new HistoryCleanupEvent
                {
                    When = new DateTime(row.Year, row.Month, 1),
                    Text = string.Format(Res("HistCleanupSalaryRowFmt") ?? "Salary {0}: empty row", new DateTime(row.Year, row.Month, 1).ToString("MM.yyyy", CultureInfo.InvariantCulture)),
                    Kind = "salary"
                });
            }
        }

        // ---------------------------------------------------------------- backup

        private string WriteBackup(HistoryCleanupPerson person, object payload)
        {
            var folder = Path.Combine(_folderService.GetBackupsFolder(), "history-cleanup");
            Directory.CreateDirectory(folder);
            var safeName = string.Concat((person.FullName ?? "employee").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == ' ' ? '_' : ch));
            var path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd_HHmmss}_{safeName}.json");
            SafeFileService.WriteJsonAtomic(path, payload);
            return path;
        }

        // ---------------------------------------------------------------- helpers

        private static bool SameEntry(FirmHistoryEntry a, FirmHistoryEntry b)
            => SameFirm(a.FirmName, b.FirmName)
               && string.Equals(a.StartDate?.Trim(), b.StartDate?.Trim(), StringComparison.Ordinal)
               && string.Equals(a.EndDate?.Trim(), b.EndDate?.Trim(), StringComparison.Ordinal);

        internal static bool SameFirm(string? a, string? b)
            => !string.IsNullOrWhiteSpace(a) && string.Equals(a.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool SamePerson(string? name, string? folder, HistoryCleanupPerson person, ISet<string>? folders = null)
        {
            if (!string.IsNullOrWhiteSpace(folder)
                && (string.Equals(folder, person.EmployeeFolder, StringComparison.OrdinalIgnoreCase) || (folders?.Contains(folder) ?? false)))
                return true;
            return !string.IsNullOrWhiteSpace(name) && string.Equals(name.Trim(), person.FullName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool CoversMonth(FirmHistoryEntry entry, DateTime monthStart)
        {
            var start = ParseDate(entry.StartDate);
            if (start == null)
                return false;
            var end = ParseDate(entry.EndDate) ?? DateTime.MaxValue.Date;
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            return start.Value <= monthEnd && end >= monthStart;
        }

        private static DateTime? ParseDate(string? text)
            => string.IsNullOrWhiteSpace(text) ? null : DateParsingHelper.TryParseDate(text);

        private static DateTime? ParseTimestamp(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value))
                return value;
            return DateParsingHelper.TryParseDate(text);
        }

        private static string Describe(EmployeeHistoryEntry item)
            => !string.IsNullOrWhiteSpace(item.Description) ? item.Description : item.Action;

        private static string KindOf(string eventType) => eventType switch
        {
            "Created" => "added",
            "Archived" => "archived",
            "Restored" => "restored",
            _ => "other"
        };

        private static string KindOfActivity(string actionType) => actionType switch
        {
            "EmployeeAdded" => "added",
            "EmployeeArchived" => "archived",
            "EmployeeRestored" => "restored",
            _ => "other"
        };

        private static string BuildInitials(string? fullName)
        {
            var parts = (fullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return string.Empty;
            var first = char.ToUpperInvariant(parts[0][0]).ToString();
            return parts.Length == 1 ? first : first + char.ToUpperInvariant(parts[^1][0]);
        }

        private static HistoryCleanupResult Fail(string message, string backupPath = "")
            => new() { Success = false, Message = message, BackupPath = backupPath };

        private static string? Res(string key)
            => System.Windows.Application.Current?.TryFindResource(key) as string;
    }
}
