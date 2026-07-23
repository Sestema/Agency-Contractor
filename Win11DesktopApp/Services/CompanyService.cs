using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class CompanyService
    {
        private static string Res(string key) =>
            Application.Current?.TryFindResource(key) as string ?? key;

        private readonly ObservableCollection<EmployerCompany> _companies = new ObservableCollection<EmployerCompany>();
        private readonly TagCatalogService _tagCatalogService;
        private readonly AppSettingsService _appSettingsService;
        private readonly PersistenceService _persistenceService;
        private readonly FolderService _folderService;
        private readonly EmployeeIndexDbService? _employeeIndexDbService;
        private readonly SyncEventService? _syncEventService;
        private readonly FirmFinanceRenameService? _firmFinanceRenameService;
        private AdminMirrorSyncService? _adminMirrorSyncService;
        private EmployerCompany? _selectedCompany;

        public ObservableCollection<EmployerCompany> Companies => _companies;

        public IEnumerable<EmployerCompany> VisibleCompanies =>
            _companies.Where(c => IsCompanyVisible(c));

        public event Action<EmployerCompany?>? SelectedCompanyChanged;
        public event Action? VisibilityChanged;

        public bool IsCompanyVisible(EmployerCompany company)
            => !HasHideSchedule(company);

        public bool IsCompanyVisibleForPeriod(EmployerCompany company, int year, int month)
        {
            if (!HasHideSchedule(company))
                return true;

            if (year <= 0 || month < 1 || month > 12)
                return true;

            return CompareYearMonth(year, month, company.HiddenFromYear, company.HiddenFromMonth) < 0;
        }

        public bool IsCompanyVisibleForPeriod(string companyName, int year, int month)
        {
            var company = _companies.FirstOrDefault(c => string.Equals(c.Name, companyName, StringComparison.OrdinalIgnoreCase));
            return company == null || IsCompanyVisibleForPeriod(company, year, month);
        }

        public bool IsCompanyVisibleForRange(EmployerCompany company, DateTime from, DateTime to)
        {
            if (!HasHideSchedule(company))
                return true;

            var start = from <= to ? from : to;
            return CompareYearMonth(start.Year, start.Month, company.HiddenFromYear, company.HiddenFromMonth) < 0;
        }

        public void SetCompanyVisible(EmployerCompany company, bool visible)
        {
            var id = company.Id.ToString();
            var list = _appSettingsService.Settings.HiddenCompanyIds;

            if (visible)
            {
                company.HiddenFromYear = 0;
                company.HiddenFromMonth = 0;
                list.Remove(id);
            }
            else
            {
                var hiddenFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                company.HiddenFromYear = hiddenFrom.Year;
                company.HiddenFromMonth = hiddenFrom.Month;
                list.Remove(id);

                if (_selectedCompany == company)
                    SelectedCompany = null;
            }

            QueueCompanySave();
            _appSettingsService.SaveSettings();
            VisibilityChanged?.Invoke();
        }

        public int GetActiveEmployeeCount(EmployerCompany company)
        {
            try
            {
                var folder = _folderService.GetEmployeesFolder(company.Name);
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return 0;
                return Directory.GetDirectories(folder).Length;
            }
            catch (Exception ex) { LoggingService.LogWarning("CompanyService.GetActiveEmployeeCount", ex.Message); return 0; }
        }

        public EmployerCompany? SelectedCompany
        {
            get => _selectedCompany;
            set
            {
                if (_selectedCompany == value) return;
                if (value != null && !_companies.Contains(value))
                    _selectedCompany = null;
                else
                    _selectedCompany = value;

                _appSettingsService.Settings.SelectedCompanyId = _selectedCompany?.Id.ToString() ?? string.Empty;
                Task.Run(() =>
                {
                    try { _appSettingsService.SaveSettings(); }
                    catch (Exception ex) { LoggingService.LogError("CompanyService.SaveSettings", ex); }
                });
                SelectedCompanyChanged?.Invoke(_selectedCompany);
            }
        }

        public CompanyService(TagCatalogService tagCatalogService, AppSettingsService appSettingsService,
            PersistenceService persistenceService, FolderService folderService, EmployeeIndexDbService? employeeIndexDbService = null,
            SyncEventService? syncEventService = null,
            FirmFinanceRenameService? firmFinanceRenameService = null)
        {
            _tagCatalogService = tagCatalogService;
            _appSettingsService = appSettingsService;
            _persistenceService = persistenceService;
            _folderService = folderService;
            _employeeIndexDbService = employeeIndexDbService;
            _syncEventService = syncEventService;
            _firmFinanceRenameService = firmFinanceRenameService;
            if (_syncEventService != null)
                _syncEventService.SyncEventReceived += OnSyncEventReceived;

            LoadCompanies();
            MigrateLegacyHiddenCompanies();
            ApplySavedSelection();
            CleanupAutoAdoptedNameHistoryAliases();
        }

        /// <summary>
        /// Versions 0.1.87-0.1.89 auto-adopted firm names discovered in salary storage into
        /// company.NameHistory on every startup. On real data this merged unrelated companies:
        /// employees "jumped" between firms, hours disappeared and whole firms turned red.
        /// Those auto-added aliases are always OPEN periods (ToYear=0, ToMonth=0) whose name
        /// differs from the current company name. Genuine rename history recorded by
        /// RecordCompanyNameChange always CLOSES the old-name period (ToYear/ToMonth set) and
        /// only keeps an open period for the company's current name, so removing foreign open
        /// periods never touches real rename history. Runs on every load (idempotent) because
        /// a polluted companies file can still arrive from another PC via sync.
        /// </summary>
        private void CleanupAutoAdoptedNameHistoryAliases()
        {
            var changed = false;
            foreach (var company in _companies)
            {
                if (company.NameHistory == null || company.NameHistory.Count == 0)
                    continue;

                var removed = company.NameHistory.RemoveAll(period =>
                    period.ToYear == 0
                    && period.ToMonth == 0
                    && !string.Equals(period.Name, company.Name, StringComparison.OrdinalIgnoreCase));
                if (removed <= 0)
                    continue;

                changed = true;
                LoggingService.LogInfo(
                    "CompanyService.CleanupNameHistory",
                    $"Removed {removed} auto-adopted alias(es) from '{company.Name}' name history.");
            }

            if (changed)
            {
                try
                {
                    _persistenceService.SaveCompanies(_companies);
                }
                catch (Exception ex)
                {
                    LoggingService.LogWarning("CompanyService.CleanupNameHistory.Save", ex.Message);
                }
            }
        }

        internal void InitializeAdminMirrorSyncService(AdminMirrorSyncService adminMirrorSyncService)
        {
            _adminMirrorSyncService = adminMirrorSyncService ?? throw new InvalidOperationException("AdminMirrorSyncService is not initialized.");
        }

        private static bool HasHideSchedule(EmployerCompany company)
            => company.HiddenFromYear > 0 && company.HiddenFromMonth is >= 1 and <= 12;

        private static int CompareYearMonth(int yearA, int monthA, int yearB, int monthB)
            => yearA != yearB ? yearA.CompareTo(yearB) : monthA.CompareTo(monthB);

        private void MigrateLegacyHiddenCompanies()
        {
            var legacyHidden = _appSettingsService.Settings.HiddenCompanyIds;
            if (legacyHidden == null || legacyHidden.Count == 0)
                return;

            var hiddenFrom = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1);
            bool changed = false;

            foreach (var company in _companies)
            {
                if (!legacyHidden.Contains(company.Id.ToString()) || HasHideSchedule(company))
                    continue;

                company.HiddenFromYear = hiddenFrom.Year;
                company.HiddenFromMonth = hiddenFrom.Month;
                changed = true;
            }

            legacyHidden.Clear();

            if (changed)
                _persistenceService.SaveCompanies(_companies);

            _appSettingsService.SaveSettings();
        }

        private void LoadCompanies()
        {
            var loaded = _persistenceService.LoadCompanies();
            foreach (var company in loaded)
            {
                _companies.Add(company);
                if (company.Agency != null && !string.IsNullOrEmpty(company.Agency.Name))
                    _tagCatalogService.AddTagsForCompany(company, company.Agency);
                else
                    _tagCatalogService.AddTagsForEmployerOnly(company);
            }
        }

        private void ApplySavedSelection()
        {
            var selectedId = _appSettingsService.Settings.SelectedCompanyId;
            if (string.IsNullOrWhiteSpace(selectedId)) return;
            var match = _companies.FirstOrDefault(c => c.Id.ToString() == selectedId && IsCompanyVisible(c));
            if (match != null) _selectedCompany = match;
        }

        public async Task AddCompanyAsync(EmployerCompany employer, AgencyCompany agency)
        {
            // Safety net against duplicate firm names (also enforced in the UI). Firm name
            // is the identity key for folders/employees, so a duplicate would make two
            // records share one employee folder. Skip instead of creating a conflict.
            if (_companies.Any(c => string.Equals(c.Name?.Trim(), employer.Name?.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                LoggingService.LogWarning("CompanyService.AddCompanyAsync",
                    $"Skipped adding duplicate company name: '{employer.Name}'.");
                return;
            }

            employer.Agency = agency;
            _companies.Add(employer);
            _tagCatalogService.AddTagsForCompany(employer, agency);
            _folderService.EnsureCompanyStructure(employer.Name);
            await _persistenceService.SaveCompaniesAsync(_companies);
            _adminMirrorSyncService?.EnqueueEmployerUpsert(employer);
            _syncEventService?.PublishCompanyChanged("added", employer.Name);
            LoggingService.LogInfo("CompanyService", $"Company added: {employer.Name}");
            VisibilityChanged?.Invoke();
        }

        public async Task<bool> UpdateCompanyAsync(EmployerCompany company, string oldName)
        {
            var newName = company.Name;
            var effectiveMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var previousHistory = (company.NameHistory ?? new List<CompanyNamePeriod>())
                .Select(period => new CompanyNamePeriod
                {
                    Name = period.Name,
                    FromYear = period.FromYear,
                    FromMonth = period.FromMonth,
                    ToYear = period.ToYear,
                    ToMonth = period.ToMonth
                })
                .ToList();
            var folderRenamed = false;
            var indexRenamed = false;
            var financeRenamed = false;

            try
            {
                company.LastModified = DateTime.Now;

                if (!string.IsNullOrEmpty(oldName) && oldName != newName)
                {
                    folderRenamed = _folderService.RenameCompanyFolder(oldName, newName);
                    if (!folderRenamed)
                    {
                        throw new InvalidOperationException(
                            $"Не вдалося перейменувати папку фірми з '{oldName}' на '{newName}'. Дані не змінено.");
                    }

                    var updatedRows = _employeeIndexDbService?.RenameFirmReferences(oldName, newName) ?? 0;
                    indexRenamed = true;
                    LoggingService.LogInfo("CompanyService.UpdateCompany",
                        $"Updated {updatedRows} employee index row(s) after company rename from '{oldName}' to '{newName}'.");

                    if (_firmFinanceRenameService != null)
                    {
                        var financeResult = _firmFinanceRenameService.Rename(
                            company,
                            oldName,
                            newName,
                            effectiveMonth.Year,
                            effectiveMonth.Month);
                        financeRenamed = true;
                        LoggingService.LogInfo(
                            "CompanyService.UpdateCompany",
                            $"Finance rename {oldName} -> {newName}: databases={financeResult.DatabasesUpdated}, " +
                            $"entries={financeResult.EntriesRenamed}, paths={financeResult.EntryPathsUpdated}, " +
                            $"expenses={financeResult.ExpensesRenamed}, emptyDuplicates={financeResult.EmptyDuplicatesRemoved}, " +
                            $"backup='{financeResult.BackupFolderPath}'.");
                    }

                    RecordCompanyNameChange(company, oldName, newName, effectiveMonth);
                    SyncCompanySalaryAliases(company);
                }

                _folderService.EnsureCompanyStructure(newName);

                await _persistenceService.SaveCompaniesAsync(_companies);

                _tagCatalogService.RemoveTagsForCompany(oldName);
                if (company.Agency != null && !string.IsNullOrEmpty(company.Agency.Name))
                    _tagCatalogService.AddTagsForCompany(company, company.Agency);
                else
                    _tagCatalogService.AddTagsForEmployerOnly(company);

                _adminMirrorSyncService?.EnqueueEmployerUpsert(company);
                _syncEventService?.PublishCompanyChanged("updated", newName);
                LoggingService.LogInfo("CompanyService", $"Company updated: {newName} (was: {oldName})");
                SelectedCompanyChanged?.Invoke(_selectedCompany);
                VisibilityChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                if (!string.IsNullOrWhiteSpace(oldName)
                    && !string.Equals(oldName, newName, StringComparison.Ordinal))
                {
                    try
                    {
                        if (financeRenamed && _firmFinanceRenameService != null)
                        {
                            _firmFinanceRenameService.Rename(
                                company,
                                newName,
                                oldName,
                                effectiveMonth.Year,
                                effectiveMonth.Month);
                        }

                        if (indexRenamed)
                            _employeeIndexDbService?.RenameFirmReferences(newName, oldName);

                        if (folderRenamed)
                            _folderService.RenameCompanyFolder(newName, oldName);
                    }
                    catch (Exception rollbackEx)
                    {
                        LoggingService.LogError("CompanyService.UpdateCompany.Rollback", rollbackEx);
                    }
                }

                company.Name = oldName;
                company.NameHistory = previousHistory;
                LoggingService.LogError("CompanyService.UpdateCompany", ex);
                MessageBox.Show(string.Format(Res("MsgCompanySaveError"), ex.Message), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private void SyncCompanySalaryAliases(EmployerCompany company)
        {
            if (_firmFinanceRenameService == null)
                return;

            try
            {
                var otherNames = _companies
                    .Where(existing => !ReferenceEquals(existing, company))
                    .Select(existing => existing.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)
                                   && !string.Equals(name, company.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var repair = _firmFinanceRenameService.RepairCompanySalaryLinks(company, otherNames);
                if (repair.EntryPathsUpdated == 0)
                    return;

                LoggingService.LogInfo(
                    "CompanyService.SyncSalaryAliases",
                    $"Synced salary folder paths for '{company.Name}': pathsUpdated={repair.EntryPathsUpdated}.");
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning(
                    "CompanyService.SyncSalaryAliases",
                    $"Could not sync salary aliases for '{company.Name}': {ex.Message}");
            }
        }

        private static void RecordCompanyNameChange(
            EmployerCompany company,
            string oldName,
            string newName,
            DateTime effectiveMonth)
        {
            company.NameHistory ??= new List<CompanyNamePeriod>();
            var previousMonth = effectiveMonth.AddMonths(-1);
            var activePeriod = company.NameHistory.LastOrDefault(period =>
                period.ToYear == 0
                && period.ToMonth == 0
                && string.Equals(period.Name, oldName, StringComparison.OrdinalIgnoreCase));

            if (activePeriod == null)
            {
                company.NameHistory.Add(new CompanyNamePeriod
                {
                    Name = oldName,
                    FromYear = company.CreatedAt.Year,
                    FromMonth = company.CreatedAt.Month,
                    ToYear = previousMonth.Year,
                    ToMonth = previousMonth.Month
                });
            }
            else
            {
                activePeriod.ToYear = previousMonth.Year;
                activePeriod.ToMonth = previousMonth.Month;
            }

            company.NameHistory.RemoveAll(period =>
                period.FromYear == effectiveMonth.Year
                && period.FromMonth == effectiveMonth.Month
                && string.Equals(period.Name, newName, StringComparison.OrdinalIgnoreCase));
            company.NameHistory.Add(new CompanyNamePeriod
            {
                Name = newName,
                FromYear = effectiveMonth.Year,
                FromMonth = effectiveMonth.Month
            });
        }

        public async Task<bool> DeleteCompanyAsync(EmployerCompany company)
        {
            try
            {
                if (_folderService.GetCompanyEmployeeFolderCount(company.Name) > 0)
                {
                    LoggingService.LogWarning("CompanyService.DeleteCompany", $"Deletion blocked because employee folders still exist for {company.Name}.");
                    return false;
                }

                _tagCatalogService.RemoveTagsForCompany(company.Name);
                if (_selectedCompany == company) SelectedCompany = null;
                _companies.Remove(company);
                await _persistenceService.SaveCompaniesAsync(_companies);
                if (!_folderService.DeleteCompanyFolder(company.Name))
                {
                    LoggingService.LogWarning("CompanyService.DeleteCompany", $"Company deleted, but folder cleanup failed for {company.Name}.");
                }
                _adminMirrorSyncService?.EnqueueEmployerDelete(company);
                _syncEventService?.PublishCompanyChanged("deleted", company.Name);
                LoggingService.LogInfo("CompanyService", $"Company deleted: {company.Name}");
                VisibilityChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CompanyService.DeleteCompany error: {ex.Message}");
                return false;
            }
        }

        public void MoveCompanyUp(EmployerCompany company)
        {
            var idx = _companies.IndexOf(company);
            if (idx <= 0) return;
            _companies.Move(idx, idx - 1);
            QueueCompanySave();
            VisibilityChanged?.Invoke();
        }

        public void MoveCompanyDown(EmployerCompany company)
        {
            var idx = _companies.IndexOf(company);
            if (idx < 0 || idx >= _companies.Count - 1) return;
            _companies.Move(idx, idx + 1);
            QueueCompanySave();
            VisibilityChanged?.Invoke();
        }

        private void QueueCompanySave()
        {
            _ = _persistenceService.SaveCompaniesAsync(_companies);
        }

        private void OnSyncEventReceived(object? sender, SyncEventReceivedEventArgs e)
        {
            if (!string.Equals(e.Record.Type, "CompanyChanged", StringComparison.OrdinalIgnoreCase))
                return;

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                ApplyCompanySyncReload();
                return;
            }

            _ = dispatcher.InvokeAsync(ApplyCompanySyncReload);
        }

        private void ApplyCompanySyncReload()
        {
            try
            {
                _companies.Clear();
                LoadCompanies();
                MigrateLegacyHiddenCompanies();
                ApplySavedSelection();
                SelectedCompanyChanged?.Invoke(_selectedCompany);
                VisibilityChanged?.Invoke();
            }
            catch (Exception ex)
            {
                LoggingService.LogWarning("CompanyService.SyncEvent", ex.Message);
            }
        }
    }
}
