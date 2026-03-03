using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Win11DesktopApp.Views
{
    public partial class CardControl : UserControl
    {
        public CardControl()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(CardControl), new PropertyMetadata(string.Empty));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public static readonly DependencyProperty IconProperty =
            DependencyProperty.Register("Icon", typeof(Geometry), typeof(CardControl), new PropertyMetadata(null));

        public Geometry Icon
        {
            get { return (Geometry)GetValue(IconProperty); }
            set { SetValue(IconProperty, value); }
        }

        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register("Command", typeof(ICommand), typeof(CardControl), new PropertyMetadata(null));

        public ICommand Command
        {
            get { return (ICommand)GetValue(CommandProperty); }
            set { SetValue(CommandProperty, value); }
        }

        public static readonly DependencyProperty BadgeCountProperty =
            DependencyProperty.Register("BadgeCount", typeof(int), typeof(CardControl), new PropertyMetadata(0));

        public int BadgeCount
        {
            get { return (int)GetValue(BadgeCountProperty); }
            set { SetValue(BadgeCountProperty, value); }
        }

        public static readonly DependencyProperty IconGradientProperty =
            DependencyProperty.Register("IconGradient", typeof(Brush), typeof(CardControl),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0, 120, 212))));

        public Brush IconGradient
        {
            get { return (Brush)GetValue(IconGradientProperty); }
            set { SetValue(IconGradientProperty, value); }
        }
    }
}
