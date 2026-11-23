using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Win11DesktopApp.Models;
using Win11DesktopApp.ViewModels;

namespace Win11DesktopApp.Views
{
    public partial class TemplatesView : UserControl
    {
        private Point _dragStart;
        private TemplateEntry? _dragItem;
        private DragGhostAdorner? _ghost;
        private InsertionLineAdorner? _insertionLine;
        private Border? _insertionTarget;

        public TemplatesView()
        {
            InitializeComponent();
        }

        private void DragHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(this);
            _dragItem = (sender as FrameworkElement)?.DataContext as TemplateEntry;
            e.Handled = true;
        }

        private void DragHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed || sender is not FrameworkElement handle)
                return;

            var delta = e.GetPosition(this) - _dragStart;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var item = _dragItem;
            _dragItem = null;

            var card = FindCard(handle);
            var layer = AdornerLayer.GetAdornerLayer(this);
            if (card != null && layer != null)
            {
                _ghost = new DragGhostAdorner(this, card, e.GetPosition(card));
                _ghost.MoveTo(e.GetPosition(this));
                layer.Add(_ghost);
                card.Opacity = 0.35;
            }

            try
            {
                DragDrop.DoDragDrop(handle, new DataObject(typeof(TemplateEntry), item), DragDropEffects.Move);
            }
            finally
            {
                if (card != null)
                    card.Opacity = 1;
                if (_ghost != null)
                    layer?.Remove(_ghost);
                _ghost = null;
                HideInsertionLine();
            }
        }

        private void Page_PreviewDragOver(object sender, DragEventArgs e)
        {
            _ghost?.MoveTo(e.GetPosition(this));
            if (!e.Data.GetDataPresent(typeof(TemplateEntry)))
                return;

            // Outside the cards nothing can be dropped.
            e.Effects = DragDropEffects.None;
        }

        private void TemplateCard_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(TemplateEntry)) || sender is not Border card)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            ShowInsertionLine(card, e.GetPosition(card).Y > card.ActualHeight / 2);
            e.Handled = true;
        }

        private void TemplateCard_DragLeave(object sender, DragEventArgs e)
        {
            if (ReferenceEquals(sender, _insertionTarget))
                HideInsertionLine();
        }

        private void TemplateCard_Drop(object sender, DragEventArgs e)
        {
            var placeAfter = _insertionLine?.Below == true;
            HideInsertionLine();
            if (DataContext is not TemplatesViewModel vm)
                return;

            var source = e.Data.GetData(typeof(TemplateEntry)) as TemplateEntry;
            var target = (sender as FrameworkElement)?.DataContext as TemplateEntry;
            vm.MoveTemplate(source, target, placeAfter);
            e.Handled = true;
        }

        private void ShowInsertionLine(Border card, bool below)
        {
            if (!ReferenceEquals(card, _insertionTarget))
            {
                HideInsertionLine();
                var layer = AdornerLayer.GetAdornerLayer(card);
                if (layer == null)
                    return;

                var brush = TryFindResource("AccentBrush") as Brush ?? Brushes.DodgerBlue;
                _insertionLine = new InsertionLineAdorner(card, brush);
                layer.Add(_insertionLine);
                _insertionTarget = card;
            }

            if (_insertionLine != null)
                _insertionLine.Below = below;
        }

        private void HideInsertionLine()
        {
            if (_insertionLine != null && _insertionTarget != null)
                AdornerLayer.GetAdornerLayer(_insertionTarget)?.Remove(_insertionLine);
            _insertionLine = null;
            _insertionTarget = null;
        }

        private static Border? FindCard(DependencyObject start)
        {
            for (var current = start; current != null; current = VisualTreeHelper.GetParent(current))
            {
                if (current is Border { Name: "TemplateCard" } card)
                    return card;
            }

            return null;
        }
    }
}
