using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Threading;

namespace Win11DesktopApp.Views
{
    public partial class ActivityLogView : UserControl
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> LangMap = new()
        {
            ["uk"] = "uk-UA",
            ["en"] = "en-US",
            ["ru"] = "ru-RU",
            ["cs"] = "cs-CZ",
        };

        public ActivityLogView()
        {
            InitializeComponent();

            var code = Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName;
            var xmlLang = LangMap.TryGetValue(code, out var v) ? v : "uk-UA";
            Language = XmlLanguage.GetLanguage(xmlLang);
        }
    }
}
