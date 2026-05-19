using System.Windows;

namespace Win11DesktopApp.Views
{
    public partial class NasPostgresGuideWindow : Window
    {
        public NasPostgresGuideWindow()
        {
            InitializeComponent();
            GuideTextBlock.Text = Application.Current?.TryFindResource("SettingsNasPostgresGuideBody") as string
                ?? string.Empty;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
