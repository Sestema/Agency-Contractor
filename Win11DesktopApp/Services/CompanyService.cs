using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public class CompanyService
    {
        private readonly ObservableCollection<EmployerCompany> _companies = new ObservableCollection<EmployerCompany>();
        private readonly TagCatalogService _tagCatalogService;
        private readonly AppSettingsService _appSettingsService;
        private readonly PersistenceService _persistenceService;
        private readonly FolderService _folderService;
        private EmployerCompany? _selectedCompany;

        public ObservableCollection<EmployerCompany> Companies => _companies;
        public event Action<EmployerCompany?>? SelectedCompanyChanged;

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
                _appSettingsService.SaveSettings();
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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CompanyService.UpdateCompany error: {ex.Message}");
                MessageBox.Show($"Помилка при збереженні: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"CompanyService.DeleteCompany error: {ex.Message}");
                return false;
            }
        }
    }
}
