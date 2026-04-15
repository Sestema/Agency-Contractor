using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceAdvancesService
    {
        private readonly LocalDbService? _localDbService;
        private readonly Func<string, string?> _resolveEmployeeId;
        private readonly Func<string, string?, string> _resolveEmployeeFolder;

        public FinanceAdvancesService(
            LocalDbService? localDbService,
            Func<string, string?> resolveEmployeeId,
            Func<string, string?, string> resolveEmployeeFolder)
        {
            _localDbService = localDbService;
            _resolveEmployeeId = resolveEmployeeId;
            _resolveEmployeeFolder = resolveEmployeeFolder;
        }

        #region CRUD

        public void AddAdvance(AdvancePayment advance)
        {
            var localDb = RequireLocalDb();
            var employeeId = _resolveEmployeeId(advance.EmployeeFolder) ?? string.Empty;
            localDb.InsertAdvance(employeeId, advance.EmployeeFolder, advance);
        }

        public void RemoveAdvance(string advanceId)
        {
            RequireLocalDb().DeleteAdvance(advanceId);
        }

        public void RemoveAdvancesForEmployee(string? employeeId, string originalFolder, string deletedFolder)
        {
            RequireLocalDb().DeleteAdvancesForEmployee(employeeId ?? string.Empty, originalFolder, deletedFolder);
        }

        #endregion

        #region Queries

        public List<AdvancePayment> GetAdvances(string companyId, string monthKey)
        {
            return RequireLocalDb().GetAdvances(companyId, monthKey);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string companyId, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireLocalDb().GetTotalAdvancesForEmployee(employeeId, employeeFolder, companyId, monthKey);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireLocalDb().GetTotalAdvancesForEmployee(employeeId, employeeFolder, monthKey);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeMonth(string employeeFolder, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireLocalDb().GetAdvancesForEmployeeMonth(employeeId, employeeFolder, monthKey);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeFirmMonth(string employeeFolder, string firmName, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireLocalDb().GetAdvancesForEmployeeFirmMonth(employeeId, employeeFolder, firmName, FinanceConstants.GlobalKey, monthKey);
        }

        public decimal GetTotalAdvancesForEmployeeFirm(string employeeFolder, string firmName, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireLocalDb().GetAdvancesForEmployeeFirmMonth(employeeId, employeeFolder, firmName, FinanceConstants.GlobalKey, monthKey).Sum(a => a.Amount);
        }

        public Dictionary<string, decimal> GetTotalAdvancesForEmployeeFirms(
            IReadOnlyList<(string requestKey, string employeeId, string employeeFolder, string firmName)> requests,
            string monthKey)
        {
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            var localDb = RequireLocalDb();
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
            var sqliteResult = localDb.GetTotalAdvancesForEmployeeFirms(sqliteRequests, monthKey, FinanceConstants.GlobalKey);
            var localDbCallMs = localDbCallSw.ElapsedMilliseconds;
            totalSw.Stop();
            LoggingService.LogInfo(
                "Timing.GetTotalAdvancesForEmployeeFirms",
                $"GetTotalAdvancesForEmployeeFirms month={monthKey} total={totalSw.ElapsedMilliseconds}ms | " +
                $"buildRequests={buildRequestsMs}ms | localDbCall={localDbCallMs}ms | requests={requests.Count} | " +
                $"withEmployeeId={requestsWithEmployeeId} | resolvedFallback={resolvedFallbackCount}");
            return sqliteResult;
        }

        public List<AdvancePayment> GetAllAdvancesForEmployee(string employeeFolder)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireLocalDb().GetAllAdvancesForEmployee(employeeId, employeeFolder);
        }

        #endregion

        private LocalDbService RequireLocalDb()
        {
            if (_localDbService == null)
                throw new InvalidOperationException("LocalDbService is required for advances storage.");

            return _localDbService;
        }
    }
}
