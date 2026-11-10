namespace Win11DesktopApp.ViewModels
{
    /// <summary>One cell of the finance month calendar.</summary>
    public sealed class SalaryPickerMonth : ViewModelBase
    {
        public SalaryPickerMonth(int month, string name)
        {
            Month = month;
            Name = name;
        }

        public int Month { get; }
        public string Name { get; }

        private bool _exists;
        public bool Exists
        {
            get => _exists;
            set => SetProperty(ref _exists, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private string _amountText = string.Empty;
        public string AmountText
        {
            get => _amountText;
            set => SetProperty(ref _amountText, value);
        }

        private string? _amountToolTip;
        public string? AmountToolTip
        {
            get => _amountToolTip;
            set => SetProperty(ref _amountToolTip, value);
        }
    }
}
