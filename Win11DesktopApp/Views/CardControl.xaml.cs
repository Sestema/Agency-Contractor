using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Win11DesktopApp.Helpers;

namespace Win11DesktopApp.Views
{
    public partial class CardControl : UserControl
    {
        public CardControl()
        {
            MenuTilePalette.EnsureApplied();
            InitializeComponent();
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register("Title", typeof(string), typeof(CardControl), new PropertyMetadata(string.Empty));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        /// <summary>Tile id; selects the icon and the theme-dependent hover colour.</summary>
        public static readonly DependencyProperty TileIdProperty =
            DependencyProperty.Register("TileId", typeof(string), typeof(CardControl),
                new PropertyMetadata(string.Empty, OnTileIdChanged));

        public string TileId
        {
            get { return (string)GetValue(TileIdProperty); }
            set { SetValue(TileIdProperty, value); }
        }

        private static void OnTileIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not CardControl card || e.NewValue is not string id || id.Length == 0)
                return;

            card.SetResourceReference(IconGradientProperty, MenuTilePalette.GradientKey(id));
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
