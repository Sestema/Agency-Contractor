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
            PersistenceService persistenceService, FolderService folderService)
        {
            _tagCatalogService = tagCatalogService;
            _appSettingsService = appSettingsService;
            _persistenceService = persistenceService;
            _folderService = folderService;

            LoadCompanies();
            MigrateLegacyHiddenCompanies();
            ApplySavedSelection();
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
            employer.Agency = agency;
            _companies.Add(employer);
            _tagCatalogService.AddTagsForCompany(employer, agency);
            _folderService.EnsureCompanyStructure(employer.Name);
            await _persistenceService.SaveCompaniesAsync(_companies);
            _adminMirrorSyncService?.EnqueueEmployerUpsert(employer);
            LoggingService.LogInfo("CompanyService", $"Company added: {employer.Name}");
            VisibilityChanged?.Invoke();
        }

        public async Task UpdateCompanyAsync(EmployerCompany company, string oldName)
        {
            try
            {
                company.LastModified = DateTime.Now;

                if (!string.IsNullOrEmpty(oldName) && oldName != company.Name)
                    _folderService.RenameCompanyFolder(oldName, company.Name);

                _folderService.EnsureCompanyStructure(company.Name);

                _tagCatalogService.RemoveTagsForCompany(oldName);
                if (company.Agency != null && !string.IsNullOrEmpty(company.Agency.Name))
                    _tagCatalogService.AddTagsForCompany(company, company.Agency);
                else
                    _tagCatalogService.AddTagsForEmployerOnly(company);

                await _persistenceService.SaveCompaniesAsync(_companies);
                _adminMirrorSyncService?.EnqueueEmployerUpsert(company);
                LoggingService.LogInfo("CompanyService", $"Company updated: {company.Name} (was: {oldName})");
                SelectedCompanyChanged?.Invoke(_selectedCompany);
                VisibilityChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CompanyService.UpdateCompany error: {ex.Message}");
                MessageBox.Show(string.Format(Res("MsgCompanySaveError"), ex.Message), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
    }
}
