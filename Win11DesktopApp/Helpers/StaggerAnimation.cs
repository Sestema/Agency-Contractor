using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Win11DesktopApp.Helpers
{
    public static class StaggerAnimation
    {
        public static readonly DependencyProperty EnableProperty =
            DependencyProperty.RegisterAttached("Enable", typeof(bool), typeof(StaggerAnimation),
                new PropertyMetadata(false, OnEnableChanged));

        public static bool GetEnable(DependencyObject obj) => (bool)obj.GetValue(EnableProperty);
        public static void SetEnable(DependencyObject obj, bool value) => obj.SetValue(EnableProperty, value);

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                element.Loaded += OnElementLoaded;
            }
        }

        private static void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element) return;
            element.Loaded -= OnElementLoaded;

            int index = ResolveIndex(element);
            if (index < 0) index = 0;
            int cappedIndex = Math.Min(index, 15);

            var translate = EnsureTranslateTransform(element);

            element.Opacity = 0;

            var delay = TimeSpan.FromMilliseconds(cappedIndex * 40);

            var fadeIn = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(260)))
            {
                BeginTime = delay,
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };

            element.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            if (translate != null)
            {
                var slideUp = new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(320)))
                {
                    BeginTime = delay,
                    EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
                };
                translate.BeginAnimation(TranslateTransform.YProperty, slideUp);
            }
        }

        private static int ResolveIndex(FrameworkElement element)
        {
            var parentDep = VisualTreeHelper.GetParent(element) as DependencyObject ?? element;
            var itemsControl = ItemsControl.ItemsControlFromItemContainer(parentDep);

            if (itemsControl != null)
            {
                var container = itemsControl.ItemContainerGenerator.ContainerFromItem(element.DataContext);
                if (container != null)
                    return itemsControl.ItemContainerGenerator.IndexFromContainer(container);
            }
            return 0;
        }

        private static TranslateTransform? EnsureTranslateTransform(FrameworkElement element)
        {
            if (element.RenderTransform is TranslateTransform tt)
            {
                if (!tt.IsFrozen) return tt;
                var unfrozen = new TranslateTransform();
                element.RenderTransform = unfrozen;
                return unfrozen;
            }

            if (element.RenderTransform is TransformGroup group)
            {
                foreach (var t in group.Children)
                {
                    if (t is TranslateTransform existing && !existing.IsFrozen)
                        return existing;
                }
                var cloned = group.IsFrozen ? group.Clone() : group;
                var added = new TranslateTransform();
                cloned.Children.Add(added);
                if (group.IsFrozen)
                    element.RenderTransform = cloned;
                return added;
            }

            var existing2 = element.RenderTransform;

            if (existing2 == null || existing2 == Transform.Identity)
            {
                var translate = new TranslateTransform();
                element.RenderTransform = translate;
                return translate;
            }

            var newGroup = new TransformGroup();
            newGroup.Children.Add(existing2.Clone());
            var newTranslate = new TranslateTransform();
            newGroup.Children.Add(newTranslate);
            element.RenderTransform = newGroup;
            return newTranslate;
        }
    }
}
