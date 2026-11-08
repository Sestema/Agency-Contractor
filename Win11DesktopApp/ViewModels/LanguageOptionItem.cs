using System.Collections.Generic;
using System.Windows;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>One language button in settings. An empty code means "same as the interface".</summary>
    public class LanguageOptionItem : ViewModelBase
    {
        private readonly string _name;
        private bool _isSelected;

        private LanguageOptionItem(string code, string shortCode, string name)
        {
            Code = code;
            ShortCode = shortCode;
            _name = name;
        }

        public string Code { get; }
        public string ShortCode { get; }
        public bool HasFlag => Code.Length > 0;
        public string Name => HasFlag ? _name : Application.Current?.TryFindResource("DocLangSameAsUI") as string ?? _name;

        /// <summary>Text for the compact document row: the code, or the full label when there is no flag.</summary>
        public string CompactLabel => HasFlag ? ShortCode : Name;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public void RefreshLocalization()
        {
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(CompactLabel));
        }

        public static IEnumerable<LanguageOptionItem> CreateLanguages() => new[]
        {
            new LanguageOptionItem("uk", "UA", "Українська"),
            new LanguageOptionItem("cs", "CS", "Čeština"),
            new LanguageOptionItem("ru", "RU", "Русский"),
            new LanguageOptionItem("en", "EN", "English")
        };

        public static IEnumerable<LanguageOptionItem> CreateDocumentLanguages()
        {
            yield return new LanguageOptionItem(string.Empty, string.Empty, string.Empty);
            foreach (var language in CreateLanguages())
                yield return language;
        }
    }
}
