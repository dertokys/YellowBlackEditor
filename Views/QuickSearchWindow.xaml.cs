using System.Windows;
using System.Windows.Input;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace YellowBlackEditor.Views
{
    public partial class QuickSearchWindow : Window
    {
        public sealed record Result(NodeViewModel Node, string Title, string Sub);
        public sealed record PointItem(string Title, string Sub, string Id, string Password, Visibility IdVisibility, Visibility PasswordVisibility);

        private readonly Func<string, List<NodeViewModel>> _search;
        private readonly Action<NodeViewModel> _open;
        private bool _closing;
        private bool _wasActive;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr pid);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        private static void ForceForeground(IntPtr hwnd)
        {
            var fg = GetForegroundWindow();
            if (fg == hwnd) return;
            var me = GetCurrentThreadId();
            var other = fg == IntPtr.Zero ? 0 : GetWindowThreadProcessId(fg, IntPtr.Zero);
            var attached = other != 0 && other != me && AttachThreadInput(me, other, true);
            try
            {
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached) AttachThreadInput(me, other, false);
            }
        }

        public void BringUp()
        {
            ForceForeground(new System.Windows.Interop.WindowInteropHelper(this).Handle);
            Activate();
            QueryBox.Focus();
            Keyboard.Focus(QueryBox);
        }

        public QuickSearchWindow(Func<string, List<NodeViewModel>> search, string hotkey, Action<NodeViewModel> open)
        {
            InitializeComponent();
            HintText.Text = "↑↓ — выбрать клиента, Enter — открыть в программе, Esc — закрыть." +
                            (hotkey.Length > 0 ? $" {hotkey} открывает это окно из любой программы." : "");
            _search = search;
            _open = open;
            Loaded += (_, _) => BringUp();
            Activated += (_, _) =>
            {
                _wasActive = true;
                Topmost = true;
            };
            Closing += (_, _) => _closing = true;
        }

        private void QueryBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var q = QueryBox.Text.Trim();
            CopiedText.Text = "";
            if (q.Length < 2)
            {
                Results.ItemsSource = null;
                Points.ItemsSource = null;
                EmptyText.Text = "";
                return;
            }
            var found = _search(q).Take(30).Select(n => new Result(n, n.Name, n.Type == NodeType.Client ? "клиент" : "заметка" + (n.Parent != null ? " · " + n.Parent.Name : ""))).ToList();
            Results.ItemsSource = found;
            Results.SelectedIndex = found.Count > 0 ? 0 : -1;
            EmptyText.Text = found.Count == 0 ? "Ничего не нашлось" : "";
        }

        private void Results_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            Points.ItemsSource = null;
            CopiedText.Text = "";
            if (Results.SelectedItem is not Result r) return;
            if (r.Node.Type != NodeType.Client)
            {
                EmptyText.Text = "Enter — открыть заметку";
                return;
            }
            try
            {
                var card = ClientStore.Load(r.Node.FullPath);
                var items = card.Accesses.Select(a => new PointItem(
                    string.IsNullOrWhiteSpace(a.Point) ? "Точка" : a.Point,
                    (a.Program + (string.IsNullOrWhiteSpace(a.AccessId) ? "" : " · " + a.AccessId)).Trim(' ', '·'),
                    a.AccessId, a.Password,
                    string.IsNullOrWhiteSpace(a.AccessId) ? Visibility.Collapsed : Visibility.Visible,
                    string.IsNullOrWhiteSpace(a.Password) ? Visibility.Collapsed : Visibility.Visible)).ToList();
                Points.ItemsSource = items;
                var inn = string.IsNullOrWhiteSpace(card.Inn) ? "" : "ИНН " + card.Inn + ". ";
                EmptyText.Text = items.Count == 0 ? inn + "Точек нет. Enter — открыть карточку" : inn + "Enter — открыть карточку";
            }
            catch
            {
                EmptyText.Text = "Карточку не удалось прочитать";
            }
        }

        private void Copy(string text, string what)
        {
            try
            {
                System.Windows.Clipboard.SetText(text);
                CopiedText.Text = what + " скопирован";
            }
            catch
            {
                CopiedText.Text = "Не удалось скопировать";
            }
        }

        private void CopyId_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PointItem p) Copy(p.Id, "ID");
        }

        private void CopyPassword_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is PointItem p) Copy(p.Password, "Пароль");
        }

        private void OpenSelected()
        {
            if (Results.SelectedItem is not Result r) return;
            Close();
            _open(r.Node);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    OpenSelected();
                    e.Handled = true;
                    break;
                case Key.Down when Results.Items.Count > 0:
                    Results.SelectedIndex = Math.Min(Results.Items.Count - 1, Results.SelectedIndex + 1);
                    Results.ScrollIntoView(Results.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Up when Results.Items.Count > 0:
                    Results.SelectedIndex = Math.Max(0, Results.SelectedIndex - 1);
                    Results.ScrollIntoView(Results.SelectedItem);
                    e.Handled = true;
                    break;
            }
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (_wasActive && !_closing) Topmost = false;
        }
    }
}
