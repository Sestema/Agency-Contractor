using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Win11DesktopApp.Controls
{
    public partial class MarqueeTextBlock : UserControl
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register(
                nameof(Text),
                typeof(string),
                typeof(MarqueeTextBlock),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure, OnTextChanged));

        private Storyboard? _storyboard;
        private double _overflow;

        public MarqueeTextBlock()
        {
            InitializeComponent();
            Loaded += (_, _) => UpdateOverflow();
            SizeChanged += (_, _) => UpdateOverflow();
        }

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public void StartMarquee()
        {
            UpdateOverflow();
            if (_overflow <= 1)
                return;

            StopMarquee(resetTransform: false);
            DisplayText.TextTrimming = TextTrimming.None;

            var duration = TimeSpan.FromSeconds(Math.Clamp(_overflow / 28.0, 2.5, 8.0));
            var animation = new DoubleAnimation(0, -_overflow, duration)
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            _storyboard = new Storyboard();
            _storyboard.Children.Add(animation);
            Storyboard.SetTarget(animation, ScrollTransform);
            Storyboard.SetTargetProperty(animation, new PropertyPath(TranslateTransform.XProperty));
            _storyboard.Begin();
        }

        public void StopMarquee(bool resetTransform = true)
        {
            _storyboard?.Stop();
            _storyboard = null;

            if (resetTransform)
                ScrollTransform.X = 0;

            DisplayText.TextTrimming = _overflow > 1 ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarqueeTextBlock block)
                block.UpdateOverflow();
        }

        private void UpdateOverflow()
        {
            StopMarquee();

            if (ClipHost.ActualWidth <= 0 || string.IsNullOrEmpty(Text))
            {
                _overflow = 0;
                return;
            }

            var textWidth = MeasureTextWidth(Text);
            _overflow = Math.Max(0, textWidth - ClipHost.ActualWidth);
            DisplayText.TextTrimming = _overflow > 1 ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (sizeInfo.WidthChanged)
                Dispatcher.BeginInvoke(UpdateOverflow, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private double MeasureTextWidth(string value)
        {
            var typeface = new Typeface(
                DisplayText.FontFamily,
                DisplayText.FontStyle,
                DisplayText.FontWeight,
                DisplayText.FontStretch);

            var formatted = new FormattedText(
                value,
                CultureInfo.CurrentUICulture,
                DisplayText.FlowDirection,
                typeface,
                DisplayText.FontSize,
                Brushes.Black,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            return formatted.WidthIncludingTrailingWhitespace;
        }
    }
}
