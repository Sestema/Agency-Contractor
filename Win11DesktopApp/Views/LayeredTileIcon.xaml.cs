using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Win11DesktopApp.Helpers;

namespace Win11DesktopApp.Views
{
    public partial class LayeredTileIcon : UserControl
    {
        public LayeredTileIcon()
        {
            MenuTilePalette.EnsureApplied();
            InitializeComponent();
        }

        /// <summary>
        /// Icon id. Links the control to its MenuTile* geometries and colours; resource
        /// references keep the colours in step with the active theme.
        /// </summary>
        public static readonly DependencyProperty TileIdProperty =
            DependencyProperty.Register("TileId", typeof(string), typeof(LayeredTileIcon),
                new PropertyMetadata(string.Empty, OnTileIdChanged));

        public string TileId
        {
            get { return (string)GetValue(TileIdProperty); }
            set { SetValue(TileIdProperty, value); }
        }

        private static void OnTileIdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not LayeredTileIcon icon || e.NewValue is not string id || id.Length == 0)
                return;

            icon.SetResourceReference(IconBodyProperty, $"MenuTileBody.{id}");
            icon.SetResourceReference(IconAccentProperty, $"MenuTileAccent.{id}");
            icon.SetResourceReference(IconInnerProperty, $"MenuTileInner.{id}");
            icon.SetResourceReference(IconGradientProperty, MenuTilePalette.GradientKey(id));
            icon.SetResourceReference(IconAccentBrushProperty, MenuTilePalette.AccentKey(id));
        }

        public static readonly DependencyProperty IconBodyProperty =
            DependencyProperty.Register("IconBody", typeof(Geometry), typeof(LayeredTileIcon), new PropertyMetadata(null));

        public Geometry? IconBody
        {
            get { return (Geometry?)GetValue(IconBodyProperty); }
            set { SetValue(IconBodyProperty, value); }
        }

        public static readonly DependencyProperty IconAccentProperty =
            DependencyProperty.Register("IconAccent", typeof(Geometry), typeof(LayeredTileIcon), new PropertyMetadata(null));

        public Geometry? IconAccent
        {
            get { return (Geometry?)GetValue(IconAccentProperty); }
            set { SetValue(IconAccentProperty, value); }
        }

        public static readonly DependencyProperty IconInnerProperty =
            DependencyProperty.Register("IconInner", typeof(Geometry), typeof(LayeredTileIcon), new PropertyMetadata(null));

        public Geometry? IconInner
        {
            get { return (Geometry?)GetValue(IconInnerProperty); }
            set { SetValue(IconInnerProperty, value); }
        }

        public static readonly DependencyProperty IconGradientProperty =
            DependencyProperty.Register("IconGradient", typeof(Brush), typeof(LayeredTileIcon), new PropertyMetadata(null));

        public Brush? IconGradient
        {
            get { return (Brush?)GetValue(IconGradientProperty); }
            set { SetValue(IconGradientProperty, value); }
        }

        public static readonly DependencyProperty IconAccentBrushProperty =
            DependencyProperty.Register("IconAccentBrush", typeof(Brush), typeof(LayeredTileIcon), new PropertyMetadata(null));

        public Brush? IconAccentBrush
        {
            get { return (Brush?)GetValue(IconAccentBrushProperty); }
            set { SetValue(IconAccentBrushProperty, value); }
        }
    }
}
