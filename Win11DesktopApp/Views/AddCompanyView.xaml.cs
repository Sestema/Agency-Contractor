using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Win11DesktopApp.Views
{
    public partial class AddCompanyView : UserControl
    {
        public AddCompanyView()
        {
            InitializeComponent();
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (MainBorder != null)
            {
                double newWidth = MainBorder.ActualWidth + e.HorizontalChange;
                double newHeight = MainBorder.ActualHeight + e.VerticalChange;

                if (newWidth >= MainBorder.MinWidth && newWidth <= MainBorder.MaxWidth)
                {
                    MainBorder.Width = newWidth;
                }

                if (newHeight >= MainBorder.MinHeight && newHeight <= MainBorder.MaxHeight)
                {
                    MainBorder.Height = newHeight;
                }
            }
        }
    }
}
