using System;
using System.Collections.Generic;
using System.Linq;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class FinanceAdvancesService
    {
        private readonly IFinanceAdvancesStorage? _advancesStorage;
        private readonly Func<string, string?> _resolveEmployeeId;
        private readonly Func<string, string?, string> _resolveEmployeeFolder;

        public FinanceAdvancesService(
            IFinanceAdvancesStorage? advancesStorage,
            Func<string, string?> resolveEmployeeId,
            Func<string, string?, string> resolveEmployeeFolder)
        {
            _advancesStorage = advancesStorage;
            _resolveEmployeeId = resolveEmployeeId;
            _resolveEmployeeFolder = resolveEmployeeFolder;
        }

        #region CRUD

        public void AddAdvance(AdvancePayment advance)
        {
            var storage = RequireStorage();
            var employeeId = _resolveEmployeeId(advance.EmployeeFolder) ?? string.Empty;
            storage.InsertAdvance(employeeId, advance.EmployeeFolder, advance);
        }

        public void RemoveAdvance(string advanceId)
        {
            RequireStorage().DeleteAdvance(advanceId);
        }

        public void RemoveAdvancesForEmployee(string? employeeId, string originalFolder, string deletedFolder)
        {
            RequireStorage().DeleteAdvancesForEmployee(employeeId ?? string.Empty, originalFolder, deletedFolder);
        }

        #endregion

        #region Queries

        public List<AdvancePayment> GetAdvances(string companyId, string monthKey)
        {
            return RequireStorage().GetAdvances(companyId, monthKey);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string companyId, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireStorage().GetTotalAdvancesForEmployee(employeeId, employeeFolder, companyId, monthKey);
        }

        public decimal GetTotalAdvancesForEmployee(string employeeFolder, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireStorage().GetTotalAdvancesForEmployee(employeeId, employeeFolder, monthKey);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeMonth(string employeeFolder, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireStorage().GetAdvancesForEmployeeMonth(employeeId, employeeFolder, monthKey);
        }

        public List<AdvancePayment> GetAdvancesForEmployeeFirmMonth(string employeeFolder, string firmName, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireStorage().GetAdvancesForEmployeeFirmMonth(employeeId, employeeFolder, firmName, FinanceConstants.GlobalKey, monthKey);
        }

        public decimal GetTotalAdvancesForEmployeeFirm(string employeeFolder, string firmName, string monthKey)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireStorage().GetAdvancesForEmployeeFirmMonth(employeeId, employeeFolder, firmName, FinanceConstants.GlobalKey, monthKey).Sum(a => a.Amount);
        }

        public Dictionary<string, decimal> GetTotalAdvancesForEmployeeFirms(
            IReadOnlyList<(string requestKey, string employeeId, string employeeFolder, string firmName)> requests,
            string monthKey)
        {
            var totalSw = System.Diagnostics.Stopwatch.StartNew();
            var storage = RequireStorage();
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

            var storageCallSw = System.Diagnostics.Stopwatch.StartNew();
            var result = storage.GetTotalAdvancesForEmployeeFirms(sqliteRequests, monthKey, FinanceConstants.GlobalKey);
            var storageCallMs = storageCallSw.ElapsedMilliseconds;
            totalSw.Stop();
            LoggingService.LogInfo(
                "Timing.GetTotalAdvancesForEmployeeFirms",
                $"GetTotalAdvancesForEmployeeFirms month={monthKey} total={totalSw.ElapsedMilliseconds}ms | " +
                $"buildRequests={buildRequestsMs}ms | storageCall={storageCallMs}ms | requests={requests.Count} | " +
                $"withEmployeeId={requestsWithEmployeeId} | resolvedFallback={resolvedFallbackCount}");
            return result;
        }

        public List<AdvancePayment> GetAllAdvancesForEmployee(string employeeFolder)
        {
            var employeeId = _resolveEmployeeId(employeeFolder) ?? string.Empty;
            return RequireStorage().GetAllAdvancesForEmployee(employeeId, employeeFolder);
        }

        #endregion

        private IFinanceAdvancesStorage RequireStorage()
        {
            if (_advancesStorage == null)
                throw new InvalidOperationException("Advances storage is required.");

            return _advancesStorage;
        }
    }
}
