using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceAdvancesService
    {
        private readonly LocalDbService? _localDbService;
        private readonly FinanceDatabase _db;
        private readonly Func<string, string?> _resolveEmployeeId;
        private readonly Func<string, string?, string> _resolveEmployeeFolder;
        private bool _useLocalDb;

        public FinanceAdvancesService(
            LocalDbService? localDbService,
            FinanceDatabase db,
            Func<string, string?> resolveEmployeeId,
            Func<string, string?, string> resolveEmployeeFolder)
        {
            _localDbService = localDbService;
            _db = db;
            _resolveEmployeeId = resolveEmployeeId;
            _resolveEmployeeFolder = resolveEmployeeFolder;
        }

        public bool UseLocalDb => _useLocalDb;

        #region Migration

        public LocalDbMigrationResult EnsureMigratedToLocalDb()
        {
            try
            {
                if (_localDbService == null)
                    return new LocalDbMigrationResult { Message = "LocalDbService is not configured." };

                var sources = BuildMigrationSources().ToList();
                var result = _localDbService.MigrateAdvancesIfNeeded(sources);
                _useLocalDb = result.IsSuccessful;

                if (result.IsSuccessful && result.WasMigrationAttempted && _db.Advances.Count > 0)
                    ClearLegacy();

                if (!result.WasMigrationAttempted && _localDbService.IsAdvancesMigrationCompleted())
                    _useLocalDb = true;

                return result;
            }
            catch (Exception ex)
            {
                _useLocalDb = false;
                LoggingService.LogError("FinanceAdvancesService.EnsureMigratedToLocalDb", ex);
                return new LocalDbMigrationResult
                {
                    WasMigrationAttempted = true,
                    IsSuccessful = false,
                    Message = ex.Message
                };
            }
        }

        private IEnumerable<AdvanceMigrationSource> BuildMigrationSources()
        {
            foreach (var advance in _db.Advances)
            {
                var employeeFolder = _resolveEmployeeFolder(advance.EmployeeFolder, null);
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                yield return new AdvanceMigrationSource
                {
                    EmployeeId = employeeId,
                    EmployeeFolder = employeeFolder,
                    Advance = new AdvancePayment
                    {
                        Id = advance.Id,
                        EmployeeFolder = employeeFolder,
                        EmployeeName = advance.EmployeeName,
                        CompanyId = advance.CompanyId,
                        Date = advance.Date,
                        Amount = advance.Amount,
                        Month = advance.Month,
                        Note = advance.Note
                    }
                };
            }
        }

        private void ClearLegacy()
        {
            _db.Advances.Clear();
        }

        #endregion

        #region CRUD

        public void AddAdvance(AdvancePayment advance)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var employeeId = _resolveEmployeeId(advance.EmployeeFolder) ?? string.Empty;
                _localDbService.InsertAdvance(employeeId, advance.EmployeeFolder, advance);
                return;
            }

            _db.Advances.Add(advance);
        }

        public void RemoveAdvance(string advanceId)
        {
            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.DeleteAdvance(advanceId);
                return;
            }

            _db.Advances.RemoveAll(a => a.Id == advanceId);
        }

        public void RemoveAdvancesForEmployee(string? employeeId, string originalFolder, string deletedFolder)
        {
            if (_useLocalDb && _localDbService != null)
            {
                _localDbService.DeleteAdvancesForEmployee(employeeId ?? string.Empty, originalFolder, deletedFolder);
                return;
            }

            bool Matches(string? folder)
            {
                if (!string.IsNullOrWhiteSpace(employeeId))
                    return false;

                return (!string.IsNullOrWhiteSpace(originalFolder) && string.Equals(folder, originalFolder, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrWhiteSpace(deletedFolder) && string.Equals(folder, deletedFolder, StringComparison.OrdinalIgnoreCase));
            }

            _db.Advances.RemoveAll(a => Matches(a.EmployeeFolder));
        }

        #endregion

        #region Queries

        public List<AdvancePayment> GetAdvances(string companyId, string monthKey)
        {
            if (_useLocalDb && _localDbService != null)
                return _localDbService.GetAdvances(companyId, monthKey);

            return _db.Advances.Where(a => a.CompanyId == companyId && a.Month == monthKey).ToList();
        }

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string companyId, string monthKey)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                return _localDbService.GetTotalAdvancesForEmployee(employeeId, employeeFolder, companyId, monthKey);
            }

            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && a.CompanyId == companyId && a.Month == monthKey)
                .Sum(a => a.Amount);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string monthKey)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                return _localDbService.GetTotalAdvancesForEmployee(employeeId, employeeFolder, monthKey);
            }

            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && a.Month == monthKey)
                .Sum(a => a.Amount);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeMonth(string employeeFolder, string monthKey)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                return _localDbService.GetAdvancesForEmployeeMonth(employeeId, employeeFolder, monthKey);
            }

            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && a.Month == monthKey)
                .OrderBy(a => a.Date)
                .ToList();
        }

        public List<AdvancePayment> GetAdvancesForEmployeeFirmMonth(string employeeFolder, string firmName, string monthKey)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                return _localDbService.GetAdvancesForEmployeeFirmMonth(employeeId, employeeFolder, firmName, FinanceConstants.GlobalKey, monthKey);
            }

            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && (a.CompanyId == firmName || a.CompanyId == FinanceConstants.GlobalKey) && a.Month == monthKey)
                .OrderBy(a => a.Date)
                .ToList();
        }

        public decimal GetTotalAdvancesForEmployeeFirm(string employeeFolder, string firmName, string monthKey)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                return _localDbService.GetAdvancesForEmployeeFirmMonth(employeeId, employeeFolder, firmName, FinanceConstants.GlobalKey, monthKey).Sum(a => a.Amount);
            }

            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder && (a.CompanyId == firmName || a.CompanyId == FinanceConstants.GlobalKey) && a.Month == monthKey)
                .Sum(a => a.Amount);
        }

        public Dictionary<string, decimal> GetTotalAdvancesForEmployeeFirms(
            IReadOnlyList<(string requestKey, string employeeId, string employeeFolder, string firmName)> requests,
            string monthKey)
        {
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            if (_useLocalDb && _localDbService != null)
            {
                var buildRequestsSw = System.Diagnostics.Stopwatch.StartNew();
                var requestsWithEmployeeId = 0;
                var resolvedFallbackCount = 0;
                var sqliteRequests = requests
                    .Select(request =>
                    {
                        var employeeId = request.employeeId;
                        if (!string.IsNullOrWhiteSpace(employeeId))
                        {
                            requestsWithEmployeeId++;
                        }
                        else
                        {
                            employeeId = _resolveEmployeeId(request.employeeFolder) ?? string.Empty;
                            if (!string.IsNullOrWhiteSpace(employeeId))
                                resolvedFallbackCount++;
                        }

                        return (
                            request.requestKey,
                            employeeId,
                            request.employeeFolder,
                            request.firmName);
                    })
                    .ToList();
                var buildRequestsMs = buildRequestsSw.ElapsedMilliseconds;

                var localDbCallSw = System.Diagnostics.Stopwatch.StartNew();
                var sqliteResult = _localDbService.GetTotalAdvancesForEmployeeFirms(sqliteRequests, monthKey, FinanceConstants.GlobalKey);
                var localDbCallMs = localDbCallSw.ElapsedMilliseconds;
                totalSw.Stop();
                LoggingService.LogInfo(
                    "Timing.GetTotalAdvancesForEmployeeFirms",
                    $"GetTotalAdvancesForEmployeeFirms month={monthKey} total={totalSw.ElapsedMilliseconds}ms | " +
                    $"buildRequests={buildRequestsMs}ms | localDbCall={localDbCallMs}ms | requests={requests.Count} | " +
                    $"withEmployeeId={requestsWithEmployeeId} | resolvedFallback={resolvedFallbackCount}");
                return sqliteResult;
            }

            var legacySw = System.Diagnostics.Stopwatch.StartNew();
            var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (requests.Count == 0 || string.IsNullOrWhiteSpace(monthKey))
                return result;

            var requestsByFolder = new Dictionary<string, List<(string requestKey, string firmName)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var request in requests)
            {
                result[request.requestKey] = 0m;

                var normalizedFolder = FinanceService.NormalizeEmployeePath(request.employeeFolder);
                if (!requestsByFolder.TryGetValue(normalizedFolder, out var folderRequests))
                {
                    folderRequests = new List<(string requestKey, string firmName)>();
                    requestsByFolder[normalizedFolder] = folderRequests;
                }

                folderRequests.Add((request.requestKey, request.firmName));
            }

            foreach (var advance in _db.Advances)
            {
                if (!string.Equals(advance.Month, monthKey, StringComparison.Ordinal))
                    continue;

                var normalizedFolder = FinanceService.NormalizeEmployeePath(advance.EmployeeFolder);
                if (!requestsByFolder.TryGetValue(normalizedFolder, out var folderRequests))
                    continue;

                foreach (var folderRequest in folderRequests)
                {
                    if (advance.CompanyId == FinanceConstants.GlobalKey || string.Equals(advance.CompanyId, folderRequest.firmName, StringComparison.Ordinal))
                    {
                        result[folderRequest.requestKey] += advance.Amount;
                    }
                }
            }

            var legacyMs = legacySw.ElapsedMilliseconds;
            totalSw.Stop();
            LoggingService.LogInfo(
                "Timing.GetTotalAdvancesForEmployeeFirms",
                $"GetTotalAdvancesForEmployeeFirms month={monthKey} total={totalSw.ElapsedMilliseconds}ms | " +
                $"legacyPath={legacyMs}ms | requests={requests.Count} | withEmployeeId=0 | resolvedFallback=0");
            return result;
        }

        public List<AdvancePayment> GetAllAdvancesForEmployee(string employeeFolder)
        {
            if (_useLocalDb && _localDbService != null)
            {
                var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
                return _localDbService.GetAllAdvancesForEmployee(employeeId, employeeFolder);
            }

            return _db.Advances
                .Where(a => a.EmployeeFolder == employeeFolder)
                .OrderByDescending(a => a.Date)
                .ToList();
        }

        #endregion
    }
}
