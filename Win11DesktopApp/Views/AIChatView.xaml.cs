using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Collections.Specialized;

namespace Win11DesktopApp.Views
{
    public partial class AIChatView : UserControl
    {
        public AIChatView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InputBox.Focus();

            if (DataContext is ViewModels.AIChatViewModel vm)
            {
                ((INotifyCollectionChanged)vm.Messages).CollectionChanged += (_, _) =>
                {
                    MessagesScroll.ScrollToEnd();
                };
            }
        }

        private void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                if (DataContext is ViewModels.AIChatViewModel vm && vm.SendMessageCommand.CanExecute(null))
                {
                    vm.SendMessageCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}
