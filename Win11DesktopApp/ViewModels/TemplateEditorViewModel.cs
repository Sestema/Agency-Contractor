using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.ViewModels
{
    public class TemplateEditorViewModel : ViewModelBase
    {
        private readonly string _firmName;
        private readonly TemplateEntry _template;
        private readonly TemplateService _templateService;
        private readonly TagCatalogService _tagCatalogService;

        public string Title => $"Редактор: {_template.Name}";
        public string RtfFilePath { get; }
        public string TemplateFolderPath { get; }

        public ObservableCollection<TagGroupViewModel> TagGroups { get; }

        private string _tagSearchQuery = string.Empty;
        public string TagSearchQuery
        {
            get => _tagSearchQuery;
            set
            {
                if (SetProperty(ref _tagSearchQuery, value))
                    OnPropertyChanged(nameof(FilteredTagGroups));
            }
        }

        public ObservableCollection<TagGroupViewModel> FilteredTagGroups
            => TagGroups != null ? TagGroupViewModel.FilterTagGroups(TagGroups, TagSearchQuery) : new ObservableCollection<TagGroupViewModel>();

        public ICommand GoBackCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand InsertTagCommand { get; }
        public ICommand CopyTagCommand { get; }

        public event Action<string, string>? SendMessageToWebView;
        public event Action<string>? RequestInsertTag;
        public Func<string?>? RequestGetRtfContent { get; set; }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _editorStatus = string.Empty;
        public string EditorStatus
        {
            get => _editorStatus;
            set => SetProperty(ref _editorStatus, value);
        }

        public TemplateEditorViewModel(string firmName, TemplateEntry template, TemplateService? templateService = null)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService ?? App.TemplateService;
            _tagCatalogService = App.TagCatalogService;

            try
            {
                var fullPath = _templateService.GetTemplateFullPath(firmName, template.FilePath);
                TemplateFolderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                RtfFilePath = Path.Combine(TemplateFolderPath, "content.rtf");
            }
            catch
            {
                TemplateFolderPath = string.Empty;
                RtfFilePath = string.Empty;
            }

            try
            {
                var allTags = _tagCatalogService.GetAllTagDefinitions();
                TagGroups = TagGroupViewModel.BuildTagGroups(allTags);
            }
            catch
            {
                TagGroups = new ObservableCollection<TagGroupViewModel>();
            }

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new RelayCommand(o => Save());
            InsertTagCommand = new RelayCommand(o => InsertTag(o));
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
        }

        public TemplateEditorViewModel(string firmName, TemplateEntry template, TagCatalogService tagCatalogService, TemplateService templateService)
        {
            _firmName = firmName;
            _template = template;
            _templateService = templateService;
            _tagCatalogService = tagCatalogService;

            try
            {
                var fullPath = _templateService.GetTemplateFullPath(firmName, template.FilePath);
                TemplateFolderPath = Path.GetDirectoryName(fullPath) ?? string.Empty;
                RtfFilePath = Path.Combine(TemplateFolderPath, "content.rtf");
            }
            catch
            {
                TemplateFolderPath = string.Empty;
                RtfFilePath = string.Empty;
            }

            try
            {
                var allTags = tagCatalogService.GetAllTagDefinitions();
                TagGroups = TagGroupViewModel.BuildTagGroups(allTags);
            }
            catch
            {
                TagGroups = new ObservableCollection<TagGroupViewModel>();
            }

            GoBackCommand = new RelayCommand(o => NavigateBack());
            SaveCommand = new RelayCommand(o => Save());
            InsertTagCommand = new RelayCommand(o => InsertTag(o));
            CopyTagCommand = new RelayCommand(o => CopyTag(o));
        }

        private void NavigateBack()
        {
            var company = App.CompanyService.Companies.FirstOrDefault(c => c.Name == _firmName);
            if (company != null)
                App.NavigationService.NavigateTo(new TemplatesViewModel(company));
            else
                App.NavigationService.NavigateTo(new MainViewModel());
        }

        private void Save()
        {
            try
            {
                if (string.IsNullOrEmpty(TemplateFolderPath))
                {
                    StatusMessage = "Помилка: шлях до шаблону невідомий.";
                    return;
                }

                Directory.CreateDirectory(TemplateFolderPath);
                var rtfContent = RequestGetRtfContent?.Invoke();
                if (string.IsNullOrEmpty(rtfContent))
                {
                    StatusMessage = "Немає вмісту для збереження.";
                    return;
                }

                File.WriteAllText(RtfFilePath, rtfContent);
                StatusMessage = "Збережено!";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Помилка: {ex.Message}";
            }
        }

        private void InsertTag(object? parameter)
        {
            if (parameter is string tag)
            {
                var tagText = $"${{{tag}}}";
                RequestInsertTag?.Invoke(tagText);
            }
            else if (parameter is TagEntry entry)
            {
                var tagText = $"${{{entry.Tag}}}";
                RequestInsertTag?.Invoke(tagText);
            }
        }

        private void CopyTag(object? parameter)
        {
            try
            {
                string tagText;
                if (parameter is string tag)
                    tagText = $"${{{tag}}}";
                else if (parameter is TagEntry entry)
                    tagText = $"${{{entry.Tag}}}";
                else
                    return;

                Clipboard.SetText(tagText);
                StatusMessage = $"Скопійовано: {tagText}";
            }
            catch { }
        }
    }
}
