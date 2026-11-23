using System;

namespace Win11DesktopApp.ViewModels
{
    /// <summary>One firm in the "copy template" dialog list.</summary>
    public sealed class CopyTargetFirmItem : ViewModelBase
    {
        private readonly Action _onSelectionChanged;

        public CopyTargetFirmItem(string name, Action onSelectionChanged)
        {
            Name = name;
            Initial = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim()[0].ToString().ToUpperInvariant();
            _onSelectionChanged = onSelectionChanged;
        }

        public string Name { get; }
        public string Initial { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value))
                    _onSelectionChanged();
            }
        }

        private bool _hasSameName;
        public bool HasSameName
        {
            get => _hasSameName;
            set => SetProperty(ref _hasSameName, value);
        }
    }
}
