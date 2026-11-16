using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private void Logo_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            FindScroll(Tree)?.ScrollToTop();
            if (EditorHost.Visibility == Visibility.Visible) Editor.ScrollToHome();
            foreach (var panel in new FrameworkElement[] { ClientView, SharedView, TodayPanel })
                if (panel.Visibility == Visibility.Visible)
                    FindScroll(panel)?.ScrollToTop();
            e.Handled = true;
        }

        private static ScrollViewer? FindScroll(DependencyObject root)
        {
            var queue = new Queue<DependencyObject>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var d = queue.Dequeue();
                if (d is ScrollViewer s && s.ScrollableHeight > 0) return s;
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
                    queue.Enqueue(VisualTreeHelper.GetChild(d, i));
            }
            return null;
        }
    }
}
