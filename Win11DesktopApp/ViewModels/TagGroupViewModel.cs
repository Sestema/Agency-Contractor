using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.ViewModels
{
    public class TagGroupViewModel
    {
        public string GroupName { get; set; } = string.Empty;
        public ObservableCollection<TagEntry> Tags { get; set; } = new();

        private static readonly Dictionary<string, string> CategoryResourceKeys = new()
        {
            { "Company", "TagGrpCompany" },
            { "Agency", "TagGrpAgency" },
            { "Employee.Personal", "TagGrpEmpPersonal" },
            { "Employee.Passport", "TagGrpEmpPassport" },
            { "Employee.Visa", "TagGrpEmpVisa" },
            { "Employee.Insurance", "TagGrpEmpInsurance" },
            { "Employee.WorkPermit", "TagGrpEmpWorkPermit" },
            { "Employee.LocalAddress", "TagGrpEmpLocalAddr" },
            { "Employee.AbroadAddress", "TagGrpEmpAbroadAddr" },
            { "Employee.Work", "TagGrpEmpWork" },
            { "Employee", "TagGrpEmployee" }
        };

        private static string GetCategoryDisplayName(string category)
        {
            if (CategoryResourceKeys.TryGetValue(category, out var key))
            {
                try { return Application.Current.FindResource(key) as string ?? category; }
                catch { return category; }
            }
            return category;
        }

        private static readonly string[] CategoryOrder = new[]
        {
            "Company", "Agency",
            "Employee.Personal", "Employee.Passport", "Employee.Visa",
            "Employee.Insurance", "Employee.WorkPermit",
            "Employee.LocalAddress", "Employee.AbroadAddress",
            "Employee.Work", "Employee"
        };

        public static ObservableCollection<TagGroupViewModel> BuildTagGroups(List<TagEntry> allTags)
        {
            var groups = new ObservableCollection<TagGroupViewModel>();
            var grouped = allTags.GroupBy(t => t.Category ?? "Other").ToDictionary(g => g.Key, g => g.ToList());

            foreach (var cat in CategoryOrder)
            {
                if (grouped.TryGetValue(cat, out var tags) && tags.Count > 0)
                {
                    var displayName = GetCategoryDisplayName(cat);
                    groups.Add(new TagGroupViewModel
                    {
                        GroupName = displayName,
                        Tags = new ObservableCollection<TagEntry>(tags)
                    });
                }
            }

            foreach (var kvp in grouped.Where(g => !CategoryOrder.Contains(g.Key)))
            {
                if (kvp.Value.Count > 0)
                {
                    groups.Add(new TagGroupViewModel
                    {
                        GroupName = kvp.Key,
                        Tags = new ObservableCollection<TagEntry>(kvp.Value)
                    });
                }
            }

            return groups;
        }

        public static ObservableCollection<TagGroupViewModel> ApplyHiddenTagsFilter(ObservableCollection<TagGroupViewModel> groups, ICollection<string> hiddenTags)
        {
            if (groups == null || hiddenTags == null || hiddenTags.Count == 0) return groups;

            var result = new ObservableCollection<TagGroupViewModel>();
            foreach (var group in groups)
            {
                var visibleTags = group.Tags.Where(t => !hiddenTags.Contains(t.Tag)).ToList();
                if (visibleTags.Count > 0)
                {
                    result.Add(new TagGroupViewModel
                    {
                        GroupName = group.GroupName,
                        Tags = new ObservableCollection<TagEntry>(visibleTags)
                    });
                }
            }
            return result;
        }

        public static ObservableCollection<TagGroupViewModel> FilterTagGroups(ObservableCollection<TagGroupViewModel> allGroups, string query)
        {
            if (allGroups == null) return new ObservableCollection<TagGroupViewModel>();
            if (string.IsNullOrWhiteSpace(query)) return allGroups;

            var q = query.Trim().ToLower();
            var result = new ObservableCollection<TagGroupViewModel>();

            foreach (var group in allGroups)
            {
                var filteredTags = group.Tags
                    .Where(t =>
                        (!string.IsNullOrEmpty(t.Tag) && t.Tag.ToLower().Contains(q)) ||
                        (!string.IsNullOrEmpty(t.Description) && t.Description.ToLower().Contains(q)))
                    .ToList();

                if (filteredTags.Count > 0)
                {
                    result.Add(new TagGroupViewModel
                    {
                        GroupName = group.GroupName,
                        Tags = new ObservableCollection<TagEntry>(filteredTags)
                    });
                }
            }

            return result;
        }
    }
}
