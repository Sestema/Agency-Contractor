using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Invoices.ViewModels;

public sealed class InvoicesPlaceholderSectionViewModel : ViewModelBase
{
    public InvoicesPlaceholderSectionViewModel(string titleKey, string descriptionKey, string hintKey)
    {
        TitleKey = titleKey;
        DescriptionKey = descriptionKey;
        HintKey = hintKey;
    }

    public string TitleKey { get; }
    public string DescriptionKey { get; }
    public string HintKey { get; }

    public string Title => Res(TitleKey);
    public string Description => Res(DescriptionKey);
    public string Hint => Res(HintKey);
}
