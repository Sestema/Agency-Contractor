using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Win11DesktopApp.Views
{
    public partial class CardControl : UserControl
    {
        private Border? _cardBorder;
        private Border? _revealLayer;
        private Border? _revealBorder;
        private RadialGradientBrush? _revealBrush;
        private RadialGradientBrush? _revealBorderBrush;
        private bool _revealReady;

        public CardControl()
        {
            InitializeComponent();
            MouseEnter += OnCardMouseEnter;
            MouseLeave += OnCardMouseLeave;
            MouseMove += OnCardMouseMove;
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

        private void EnsureRevealHandles()
        {
            if (_revealReady) return;
            var btn = FindVisualChild<Button>(this);
            if (btn?.Template == null) return;

            _cardBorder = btn.Template.FindName("CardBorder", btn) as Border;
            _revealLayer = btn.Template.FindName("RevealLayer", btn) as Border;
            _revealBorder = btn.Template.FindName("RevealBorder", btn) as Border;
            _revealBrush = btn.Template.FindName("RevealBrush", btn) as RadialGradientBrush;
            _revealBorderBrush = btn.Template.FindName("RevealBorderBrush", btn) as RadialGradientBrush;

            _revealReady = _cardBorder != null && _revealLayer != null && _revealBrush != null;
        }

        private void OnCardMouseEnter(object sender, MouseEventArgs e)
        {
            EnsureRevealHandles();
            if (!_revealReady) return;

            AnimateOpacity(_revealLayer!, 1.0, 150);
            AnimateOpacity(_revealBorder!, 1.0, 150);
        }

        private void OnCardMouseLeave(object sender, MouseEventArgs e)
        {
            if (!_revealReady) return;
            AnimateOpacity(_revealLayer!, 0.0, 220);
            AnimateOpacity(_revealBorder!, 0.0, 220);
        }

        private void OnCardMouseMove(object sender, MouseEventArgs e)
        {
            EnsureRevealHandles();
            if (!_revealReady || _cardBorder == null) return;

            var pos = e.GetPosition(_cardBorder);
            double w = _cardBorder.ActualWidth;
            double h = _cardBorder.ActualHeight;
            if (w <= 0 || h <= 0) return;

            double cx = Math.Max(0, Math.Min(1, pos.X / w));
            double cy = Math.Max(0, Math.Min(1, pos.Y / h));
            var center = new Point(cx, cy);

            if (_revealBrush != null)
            {
                _revealBrush.Center = center;
                _revealBrush.GradientOrigin = center;
            }
            if (_revealBorderBrush != null)
            {
                _revealBorderBrush.Center = center;
                _revealBorderBrush.GradientOrigin = center;
            }
        }

        private static void AnimateOpacity(UIElement target, double to, double ms)
        {
            var anim = new DoubleAnimation
            {
                To = to,
                Duration = TimeSpan.FromMilliseconds(ms),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            target.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
