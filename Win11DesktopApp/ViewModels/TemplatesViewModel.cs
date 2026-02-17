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
            set => SetProperty(ref _templates, value);
        }

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

            GoBackCommand = new RelayCommand(o => App.NavigationService.NavigateTo(new MainViewModel()));

            AddTemplateCommand = new RelayCommand(o =>
            {
                AddTemplateVm = new AddTemplateViewModel(_firmName);
                AddTemplateVm.RequestClose += () =>
                {
                    IsAddDialogOpen = false;
                    LoadTemplates();
                };
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
                        App.NavigationService.NavigateTo(new TemplateEditorViewModel(_firmName, template));
                    }
                    else if (format == "XLSX")
                    {
                        if (!File.Exists(fullPath))
                        {
                            MessageBox.Show("Файл шаблону не знайдено.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        // Navigate to built-in XLSX editor (DataGrid)
                        App.NavigationService.NavigateTo(new XlsxEditorViewModel(_firmName, template));
                    }
                    else if (format == "PDF")
                    {
                        if (!File.Exists(fullPath))
                        {
                            MessageBox.Show("Файл шаблону не знайдено.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                        App.NavigationService.NavigateTo(new PdfEditorViewModel(_firmName, template));
                    }
                    else
                    {
                        if (!File.Exists(fullPath))
                        {
                            MessageBox.Show("Файл шаблону не знайдено.", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
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
                            _templateService.DeleteTemplate(_firmName, template);
                            Templates.Remove(template);
                        }
                        catch (System.Exception ex)
                        {
                            MessageBox.Show($"Помилка видалення: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
            });

            CloseAddDialogCommand = new RelayCommand(o => IsAddDialogOpen = false);

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
                    MessageBox.Show($"Не вдалося відкрити файл: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            });
        }

        private void LoadTemplates()
        {
            var list = _templateService.GetTemplates(_firmName);
            Templates = new ObservableCollection<TemplateEntry>(list);
        }

        private void LoadAvailableTags()
        {
            var tags = App.TagCatalogService.GetAllTagDefinitions();
            AvailableTags = new ObservableCollection<TagEntry>(tags);
        }

        private string? GetString(string key)
        {
            return Application.Current?.TryFindResource(key) as string;
        }
    }
}
