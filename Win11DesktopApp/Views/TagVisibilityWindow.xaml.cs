using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class TagVisibilityWindow : Window
    {
        private readonly List<TagVisibilityGroup> _groups;

        public TagVisibilityWindow()
        {
            InitializeComponent();

            var hiddenTags = new HashSet<string>(App.AppSettingsService.Settings.HiddenTags ?? new List<string>());
            var allTags = App.TagCatalogService.GetAllTagDefinitions();
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

            App.AppSettingsService.Settings.HiddenTags = hiddenTags;
            App.AppSettingsService.SaveSettings();
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
