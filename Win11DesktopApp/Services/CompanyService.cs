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
        private EmployerCompany? _selectedCompany;

        public ObservableCollection<EmployerCompany> Companies => _companies;

        public IEnumerable<EmployerCompany> VisibleCompanies =>
            _companies.Where(c => IsCompanyVisible(c));

        public event Action<EmployerCompany?>? SelectedCompanyChanged;
        public event Action? VisibilityChanged;

        public bool IsCompanyVisible(EmployerCompany company)
            => !_appSettingsService.Settings.HiddenCompanyIds.Contains(company.Id.ToString());

        public void SetCompanyVisible(EmployerCompany company, bool visible)
        {
            var id = company.Id.ToString();
            var list = _appSettingsService.Settings.HiddenCompanyIds;
            if (!visible && !list.Contains(id))
                list.Add(id);
            else if (visible)
                list.Remove(id);
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
            catch { return 0; }
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
            ApplySavedSelection();
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
            var match = _companies.FirstOrDefault(c => c.Id.ToString() == selectedId);
            if (match != null) _selectedCompany = match;
        }

        public void AddCompany(EmployerCompany employer, AgencyCompany agency)
        {
            employer.Agency = agency;
            _companies.Add(employer);
            _tagCatalogService.AddTagsForCompany(employer, agency);
            _folderService.EnsureCompanyStructure(employer.Name);
            _persistenceService.SaveCompanies(_companies);
            VisibilityChanged?.Invoke();
        }

        public void UpdateCompany(EmployerCompany company, string oldName)
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

                _persistenceService.SaveCompanies(_companies);
                SelectedCompanyChanged?.Invoke(_selectedCompany);
                VisibilityChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CompanyService.UpdateCompany error: {ex.Message}");
                MessageBox.Show(string.Format(Res("MsgCompanySaveError"), ex.Message), Res("TitleError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public bool DeleteCompany(EmployerCompany company)
        {
            try
            {
                _tagCatalogService.RemoveTagsForCompany(company.Name);
                if (_selectedCompany == company) SelectedCompany = null;
                _companies.Remove(company);
                _persistenceService.SaveCompanies(_companies);
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
            _persistenceService.SaveCompanies(_companies);
            VisibilityChanged?.Invoke();
        }

        public void MoveCompanyDown(EmployerCompany company)
        {
            var idx = _companies.IndexOf(company);
            if (idx < 0 || idx >= _companies.Count - 1) return;
            _companies.Move(idx, idx + 1);
            _persistenceService.SaveCompanies(_companies);
            VisibilityChanged?.Invoke();
        }
    }
}
