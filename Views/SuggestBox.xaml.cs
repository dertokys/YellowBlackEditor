using System.Collections;
using System.Windows;
using System.Windows.Input;
using UserControl = System.Windows.Controls.UserControl;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace YellowBlackEditor.Views
{
    public partial class SuggestBox : UserControl
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(SuggestBox),
            new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

        public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
            nameof(Suggestions), typeof(IEnumerable), typeof(SuggestBox), new PropertyMetadata(null));

        public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
            nameof(Placeholder), typeof(string), typeof(SuggestBox), new PropertyMetadata("", (d, e) => ((SuggestBox)d).Box.Tag = e.NewValue));

        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public IEnumerable? Suggestions { get => (IEnumerable?)GetValue(SuggestionsProperty); set => SetValue(SuggestionsProperty, value); }
        public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }

        private bool _sync;

        public SuggestBox()
        {
            InitializeComponent();
        }

        public void FocusBox()
        {
            Box.Focus();
            Box.CaretIndex = Box.Text.Length;
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var s = (SuggestBox)d;
            var v = e.NewValue as string ?? "";
            if (s.Box.Text == v) return;
            s._sync = true;
            s.Box.Text = v;
            s._sync = false;
        }

        private void Box_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_sync) return;
            SetCurrentValue(TextProperty, Box.Text);
            if (Box.IsKeyboardFocused) ShowMatches(Box.Text, false);
        }

        private void ShowMatches(string query, bool all)
        {
            var q = (query ?? "").Trim();
            var items = (Suggestions ?? Array.Empty<string>()).OfType<string>()
                .Where(s => all || q.Length == 0 || s.Contains(q, StringComparison.CurrentCultureIgnoreCase))
                .Where(s => all || !s.Equals(q, StringComparison.CurrentCultureIgnoreCase))
                .Take(30)
                .ToList();
            if (!all && q.Length == 0)
            {
                Pop.IsOpen = false;
                return;
            }
            List.ItemsSource = items;
            List.SelectedIndex = -1;
            Pop.IsOpen = items.Count > 0;
        }

        private void Accept(string value)
        {
            Box.Text = value;
            Box.CaretIndex = value.Length;
            Pop.IsOpen = false;
        }

        private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!Pop.IsOpen)
            {
                if (e.Key == Key.Down && (Keyboard.Modifiers & ModifierKeys.Alt) != 0)
                {
                    ShowMatches(Box.Text, true);
                    e.Handled = true;
                }
                return;
            }
            var count = List.Items.Count;
            switch (e.Key)
            {
                case Key.Down:
                    List.SelectedIndex = Math.Min(count - 1, List.SelectedIndex + 1);
                    List.ScrollIntoView(List.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Up:
                    List.SelectedIndex = Math.Max(0, List.SelectedIndex - 1);
                    List.ScrollIntoView(List.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Enter:
                case Key.Tab:
                    if (List.SelectedItem is string s)
                    {
                        Accept(s);
                        e.Handled = e.Key == Key.Enter;
                    }
                    else Pop.IsOpen = false;
                    break;
                case Key.Escape:
                    Pop.IsOpen = false;
                    e.Handled = true;
                    break;
            }
        }

        private void Box_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!Pop.IsMouseOver) Pop.IsOpen = false;
        }

        private void Arrow_Click(object sender, RoutedEventArgs e)
        {
            if (Pop.IsOpen)
            {
                Pop.IsOpen = false;
                return;
            }
            Box.Focus();
            ShowMatches(Box.Text, true);
        }

        private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if ((e.OriginalSource as FrameworkElement)?.DataContext is string s)
            {
                Accept(s);
                Box.Focus();
                e.Handled = true;
            }
        }
    }
}
