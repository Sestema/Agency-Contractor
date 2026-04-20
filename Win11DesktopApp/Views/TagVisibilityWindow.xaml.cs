using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class TagVisibilityWindow : Window
    {
        private readonly List<TagVisibilityGroup> _groups;
        private readonly AppSettingsService _appSettingsService;

        public TagVisibilityWindow(AppSettingsService appSettingsService, TagCatalogService tagCatalogService)
        {
            _appSettingsService = appSettingsService ?? throw new System.ArgumentNullException(nameof(appSettingsService));
            var resolvedTagCatalogService = tagCatalogService ?? throw new System.ArgumentNullException(nameof(tagCatalogService));
            InitializeComponent();

            var hiddenTags = new HashSet<string>(_appSettingsService.Settings.HiddenTags ?? new List<string>());
            var allTags = resolvedTagCatalogService.GetAllTagDefinitions();
            var tagGroups = TagGroupViewModel.BuildTagGroups(allTags);

            _groups = tagGroups.Select(g => new TagVisibilityGroup
            {
                GroupName = g.GroupName,
                Items = g.Tags.Select(t => new TagVisibilityItem
                {
                    Tag = t.Tag,
                    Description = t.Description,
                    IsVisible = !hiddenTags.Contains(t.Tag)
                }).ToList()
            }).ToList();

            TagGroupsControl.ItemsSource = _groups;
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var group in _groups)
                foreach (var item in group.Items)
                    item.IsVisible = true;
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var group in _groups)
                foreach (var item in group.Items)
                    item.IsVisible = false;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var hiddenTags = _groups
                .SelectMany(g => g.Items)
                .Where(i => !i.IsVisible)
                .Select(i => i.Tag)
                .ToList();

            _appSettingsService.Settings.HiddenTags = hiddenTags;
            _appSettingsService.SaveSettings();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class TagVisibilityGroup
    {
        public string GroupName { get; set; } = string.Empty;
        public List<TagVisibilityItem> Items { get; set; } = new();
    }

    public class TagVisibilityItem : ViewModelBase
    {
        public string Tag { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }
    }
}
