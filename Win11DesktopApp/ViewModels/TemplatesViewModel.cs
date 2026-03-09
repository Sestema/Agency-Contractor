using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class TemplatesViewModel : ViewModelBase
    {
        private readonly TemplateService _templateService;
        private ObservableCollection<TemplateEntry> _templates = new ObservableCollection<TemplateEntry>();
        public ObservableCollection<TemplateEntry> Templates
        {
            get => _templates;
            set { SetProperty(ref _templates, value); OnPropertyChanged(nameof(HasTemplates)); }
        }
        public bool HasTemplates => Templates.Count > 0;

        private string _firmName;
        public string Title => string.Format(GetString("TitleTemplates") ?? "Шаблони: {0}", _firmName);

        public ICommand AddTemplateCommand { get; }
        public ICommand GoBackCommand { get; }
        public ICommand DeleteTemplateCommand { get; }

        private bool _isAddDialogOpen;
        public bool IsAddDialogOpen
        {
            get => _isAddDialogOpen;
            set => SetProperty(ref _isAddDialogOpen, value);
        }

        private AddTemplateViewModel? _addTemplateVm;
        public AddTemplateViewModel? AddTemplateVm
        {
            get => _addTemplateVm;
            set => SetProperty(ref _addTemplateVm, value);
        }

        public ICommand CloseAddDialogCommand { get; }
        public ICommand EditTemplateCommand { get; }
        public ICommand RenameTemplateCommand { get; }
        public ICommand ConfirmRenameCommand { get; }
        public ICommand CancelRenameCommand { get; }
        public ICommand CopyTemplateToCompanyCommand { get; }
        public ICommand ConfirmCopyTemplateCommand { get; }
        public ICommand CancelCopyTemplateCommand { get; }

        private bool _isRenameDialogOpen;
        public bool IsRenameDialogOpen
        {
            get => _isRenameDialogOpen;
            set => SetProperty(ref _isRenameDialogOpen, value);
        }

        private string _renameText = string.Empty;
        public string RenameText
        {
            get => _renameText;
            set => SetProperty(ref _renameText, value);
        }

        private TemplateEntry? _renamingTemplate;

        private bool _isCopyDialogOpen;
        public bool IsCopyDialogOpen
        {
            get => _isCopyDialogOpen;
            set => SetProperty(ref _isCopyDialogOpen, value);
        }

        private ObservableCollection<EmployerCompany> _copyTargetCompanies = new();
        public ObservableCollection<EmployerCompany> CopyTargetCompanies
        {
            get => _copyTargetCompanies;
            set => SetProperty(ref _copyTargetCompanies, value);
        }

        private EmployerCompany? _selectedCopyTargetCompany;
        public EmployerCompany? SelectedCopyTargetCompany
        {
            get => _selectedCopyTargetCompany;
            set => SetProperty(ref _selectedCopyTargetCompany, value);
        }

        private string _copyTemplateName = string.Empty;
        public string CopyTemplateName
        {
            get => _copyTemplateName;
            set => SetProperty(ref _copyTemplateName, value);
        }

        private TemplateEntry? _copyingTemplate;

        // Tags panel
        private bool _isTagsPanelOpen;
        public bool IsTagsPanelOpen
        {
            get => _isTagsPanelOpen;
            set => SetProperty(ref _isTagsPanelOpen, value);
        }

        private ObservableCollection<TagEntry> _availableTags = new ObservableCollection<TagEntry>();
        public ObservableCollection<TagEntry> AvailableTags
        {
            get => _availableTags;
            set => SetProperty(ref _availableTags, value);
        }

        public ICommand CloseTagsPanelCommand { get; }
        public ICommand CopyTagCommand { get; }
        public ICommand OpenTemplateFileCommand { get; }

        private string _pendingTemplateFilePath = string.Empty;

        public TemplatesViewModel(EmployerCompany? company, TemplateService? templateService = null)
        {
            _templateService = templateService ?? App.TemplateService;

            if (company == null)
            {
                _firmName = "Unknown";
            }
            else
            {
                _firmName = company.Name;
                LoadTemplates();
            }

            GoBackCommand = new RelayCommand(o => App.NavigationService?.NavigateTo(new MainViewModel()));

            AddTemplateCommand = new RelayCommand(o =>
            {
                CleanupAddTemplateVm();
                AddTemplateVm = new AddTemplateViewModel(_firmName);
                AddTemplateVm.RequestClose += OnAddTemplateClose;
                IsAddDialogOpen = true;
            });

            EditTemplateCommand = new RelayCommand(o =>
            {
                if (o is TemplateEntry template)
                {
                    var fullPath = _templateService.GetTemplateFullPath(_firmName, template.FilePath);
                    var format = (template.Format ?? "").ToUpper();

                    if (format == "DOCX")
                    {
                        // DOCX may not have a source file — editor handles this gracefully
                        App.NavigationService?.NavigateTo(new TemplateEditorViewModel(_firmName, template));
                    }
                    else if (format == "XLSX")
                    {
                        if (!File.Exists(fullPath))
                        {
                            MessageBox.Show(GetString("MsgTemplateNotFound") ?? "Template not found.", GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        App.NavigationService?.NavigateTo(new XlsxEditorViewModel(_firmName, template));
                    }
                    else if (format == "PDF")
                    {
                        if (!File.Exists(fullPath))
                        {
                            MessageBox.Show(GetString("MsgTemplateNotFound") ?? "Template not found.", GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        App.NavigationService?.NavigateTo(new PdfEditorViewModel(_firmName, template));
                    }
                    else
                    {
                        if (!File.Exists(fullPath))
                        {
                            MessageBox.Show(GetString("MsgTemplateNotFound") ?? "Template not found.", GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        _pendingTemplateFilePath = fullPath;
                        LoadAvailableTags();
                        IsTagsPanelOpen = true;
                    }
                }
            });

            DeleteTemplateCommand = new RelayCommand(o =>
            {
                if (o is TemplateEntry template)
                {
                    var msgFormat = GetString("MsgConfirmDeleteTemplate") ?? "Ви впевнені, що хочете видалити шаблон '{0}'?";
                    var title = GetString("TitleConfirmDelete") ?? "Підтвердження";

                    var result = MessageBox.Show(string.Format(msgFormat, template.Name),
                                                 title,
                                                 MessageBoxButton.YesNo,
                                                 MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            var tName = template.Name;
                            _templateService.DeleteTemplate(_firmName, template);
                            Templates.Remove(template);
                            App.ActivityLogService?.Log("TemplateDeleted", "Template", _firmName, "",
                                $"Видалено шаблон «{tName}» з {_firmName}");
                        }
                        catch (System.Exception ex)
                        {
                            MessageBox.Show(string.Format(GetString("MsgDeleteError") ?? "Delete error: {0}", ex.Message), GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            });

            CloseAddDialogCommand = new RelayCommand(o =>
            {
                IsAddDialogOpen = false;
                CleanupAddTemplateVm();
            });

            RenameTemplateCommand = new RelayCommand(o =>
            {
                if (o is TemplateEntry template)
                {
                    _renamingTemplate = template;
                    RenameText = template.Name;
                    IsRenameDialogOpen = true;
                }
            });

            ConfirmRenameCommand = new RelayCommand(o =>
            {
                if (_renamingTemplate == null || string.IsNullOrWhiteSpace(RenameText)) return;
                var newName = RenameText.Trim();
                if (newName == _renamingTemplate.Name)
                {
                    IsRenameDialogOpen = false;
                    return;
                }
                try
                {
                    var oldName = _renamingTemplate.Name;
                    _templateService.RenameTemplate(_firmName, _renamingTemplate, newName);
                    App.ActivityLogService?.Log("TemplateRenamed", "Template", _firmName, "",
                        $"Перейменовано шаблон «{oldName}» → «{newName}» ({_firmName})",
                        oldName, newName);
                    LoadTemplates();
                    IsRenameDialogOpen = false;
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(string.Format(GetString("MsgRenameError") ?? "Rename error: {0}", ex.Message), GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });

            CancelRenameCommand = new RelayCommand(o =>
            {
                IsRenameDialogOpen = false;
                _renamingTemplate = null;
            });

            CopyTemplateToCompanyCommand = new RelayCommand(o =>
            {
                if (o is not TemplateEntry template)
                    return;

                var companies = App.CompanyService?.Companies?
                    .Where(c => !string.Equals(c.Name, _firmName, System.StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Name)
                    .ToList() ?? new List<EmployerCompany>();

                if (companies.Count == 0)
                {
                    MessageBox.Show(
                        GetString("TplCopyNoCompanies") ?? "There are no other companies available for copying the template.",
                        GetString("TitleError") ?? "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                _copyingTemplate = template;
                CopyTargetCompanies = new ObservableCollection<EmployerCompany>(companies);
                SelectedCopyTargetCompany = CopyTargetCompanies.FirstOrDefault();
                CopyTemplateName = template.Name;
                IsCopyDialogOpen = true;
            });

            ConfirmCopyTemplateCommand = new RelayCommand(o =>
            {
                if (_copyingTemplate == null)
                {
                    IsCopyDialogOpen = false;
                    return;
                }

                if (SelectedCopyTargetCompany == null)
                {
                    MessageBox.Show(
                        GetString("TplCopySelectCompany") ?? "Select a target company.",
                        GetString("TitleError") ?? "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var targetName = CopyTemplateName?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(targetName))
                    targetName = _copyingTemplate.Name;

                try
                {
                    var created = _templateService.CopyTemplateToCompany(_firmName, _copyingTemplate, SelectedCopyTargetCompany.Name, targetName);
                    App.ActivityLogService?.Log(
                        "TemplateCopiedToCompany",
                        "Template",
                        SelectedCopyTargetCompany.Name,
                        "",
                        $"Скопійовано шаблон «{_copyingTemplate.Name}» з {_firmName} у {SelectedCopyTargetCompany.Name} як «{created.Name}»");

                    CloseCopyDialog();
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(
                        string.Format(GetString("MsgUnexpectedError") ?? "Unexpected error: {0}", ex.Message),
                        GetString("TitleError") ?? "Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            });

            CancelCopyTemplateCommand = new RelayCommand(o => CloseCopyDialog());

            CloseTagsPanelCommand = new RelayCommand(o => IsTagsPanelOpen = false);

            CopyTagCommand = new RelayCommand(o =>
            {
                if (o is TagEntry tag)
                {
                    Clipboard.SetText($"${{{tag.Tag}}}");
                }
            });

            OpenTemplateFileCommand = new RelayCommand(o =>
            {
                if (string.IsNullOrEmpty(_pendingTemplateFilePath) || !File.Exists(_pendingTemplateFilePath)) return;
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = _pendingTemplateFilePath,
                        UseShellExecute = true
                    });
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(string.Format(GetString("MsgOpenFileError") ?? "Could not open file: {0}", ex.Message), GetString("TitleError") ?? "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void OnAddTemplateClose()
        {
            IsAddDialogOpen = false;
            CleanupAddTemplateVm();
            LoadTemplates();
        }

        private void CleanupAddTemplateVm()
        {
            if (AddTemplateVm != null)
                AddTemplateVm.RequestClose -= OnAddTemplateClose;
        }

        private void CloseCopyDialog()
        {
            IsCopyDialogOpen = false;
            _copyingTemplate = null;
            SelectedCopyTargetCompany = null;
            CopyTargetCompanies = new ObservableCollection<EmployerCompany>();
            CopyTemplateName = string.Empty;
        }

        private void LoadTemplates()
        {
            var list = _templateService.GetTemplates(_firmName);
            Templates = new ObservableCollection<TemplateEntry>(list);
        }

        private void LoadAvailableTags()
        {
            var tags = App.TagCatalogService?.GetAllTagDefinitions() ?? new List<TagEntry>();
            var hiddenTags = App.AppSettingsService?.Settings?.HiddenTags;
            if (hiddenTags != null && hiddenTags.Count > 0)
            {
                var hidden = new HashSet<string>(hiddenTags);
                tags = tags.Where(t => !hidden.Contains(t.Tag)).ToList();
            }
            AvailableTags = new ObservableCollection<TagEntry>(tags);
        }

        private string? GetString(string key)
        {
            return Application.Current?.TryFindResource(key) as string;
        }
    }
}
