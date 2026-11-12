using System.IO;
using System.Windows;
using System.Windows.Controls;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;

namespace YellowBlackEditor.Views
{
    public partial class ClientCardView : System.Windows.Controls.UserControl
    {
        public sealed record NoteItem(NodeViewModel Node, string Name, string Modified);

        public event Action<string>? StatusMessage;
        public event Action? AddNoteRequested;
        public event Action<NodeViewModel>? OpenNoteRequested;

        private NodeViewModel? _node;
        private ClientCard? _card;

        public ClientCardView()
        {
            InitializeComponent();
        }

        public void Show(NodeViewModel node, ClientCard card)
        {
            if (_card != null) _card.Changed -= Card_Changed;
            _node = node;
            _card = card;
            _card.Changed += Card_Changed;
            DataContext = card;
            RefreshNotes();
        }

        private void Card_Changed(object? sender, EventArgs e) => RefreshReminderCount();

        public void Clear()
        {
            if (_card != null) _card.Changed -= Card_Changed;
            _card = null;
            _node = null;
            DataContext = null;
            NotesList.ItemsSource = null;
        }

        public void RefreshNotes()
        {
            if (_node == null) return;
            var items = _node.Children
                .Where(c => c.Type == NodeType.Document)
                .Select(c => new NoteItem(c, c.Name, ModifiedText(c.FullPath)))
                .ToList();
            NotesList.ItemsSource = items;
            NotesEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NotesCount.Text = items.Count > 0 ? items.Count.ToString() : "";
            NotesCount.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RefreshReminderCount();
        }

        private static string ModifiedText(string folder)
        {
            try
            {
                var f = Path.Combine(folder, "content.xamlpack");
                var t = File.Exists(f) ? File.GetLastWriteTime(f) : Directory.GetLastWriteTime(folder);
                return t.Date == DateTime.Today ? "сегодня, " + t.ToString("HH:mm") : t.ToString("dd.MM.yyyy");
            }
            catch
            {
                return "";
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var text = ((sender as FrameworkElement)?.Tag as string ?? "").Trim();
            if (text.Length == 0)
            {
                StatusMessage?.Invoke("Поле пустое, копировать нечего");
                return;
            }
            if (!TrySetClipboard(text)) return;
            StatusMessage?.Invoke("\u0421\u043A\u043E\u043F\u0438\u0440\u043E\u0432\u0430\u043D\u043E \u0432 \u0431\u0443\u0444\u0435\u0440 \u043E\u0431\u043C\u0435\u043D\u0430");
            if (sender is System.Windows.Controls.Button b) FlashCopied(b);
        }

        public static readonly DependencyProperty CopiedProperty =
            DependencyProperty.RegisterAttached("Copied", typeof(bool), typeof(ClientCardView), new PropertyMetadata(false));

        public static bool GetCopied(DependencyObject d) => (bool)d.GetValue(CopiedProperty);
        public static void SetCopied(DependencyObject d, bool value) => d.SetValue(CopiedProperty, value);

        private static void FlashCopied(System.Windows.Controls.Button b)
        {
            SetCopied(b, true);
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                SetCopied(b, false);
            };
            t.Start();
        }

        private bool TrySetClipboard(string text)
        {
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    System.Windows.Clipboard.SetDataObject(text, true);
                    return true;
                }
                catch
                {
                    Thread.Sleep(40);
                }
            }
            StatusMessage?.Invoke("Буфер обмена занят другой программой, попробуйте ещё раз");
            return false;
        }

        private void AddContact_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;
            var c = new ClientContact();
            _card.Contacts.Add(c);
            FocusNewItem(ContactsList, c);
        }

        public void FocusFirstField()
        {
            Tabs.SelectedIndex = 0;
            Dispatcher.BeginInvoke(() => InnBox.Focus(), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void FocusNewItem(ItemsControl list, object item)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (list.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject container) return;
                var box = FindFirst<System.Windows.Controls.TextBox>(container);
                if (box == null) return;
                box.Focus();
                box.BringIntoView();
            }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private static T? FindFirst<T>(DependencyObject root) where T : DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T hit && (child as UIElement)?.IsVisible != false) return hit;
                var deeper = FindFirst<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private void RemoveContact_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ClientContact c) return;
            var hasData = !string.IsNullOrWhiteSpace(c.Name) || !string.IsNullOrWhiteSpace(c.Phone) || !string.IsNullOrWhiteSpace(c.Role);
            if (hasData && !ThemedMessageBox.Confirm(Window.GetWindow(this),
                    $"Удалить контакт «{(string.IsNullOrWhiteSpace(c.Name) ? c.Phone : c.Name)}»?", "", "Удалить"))
                return;
            _card?.Contacts.Remove(c);
        }

        private void AddAccess_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;
            var a = new RemoteAccess();
            _card.Accesses.Add(a);
            FocusNewItem(AccessList, a);
        }

        private void RemoveAccess_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not RemoteAccess a) return;
            var title = string.IsNullOrWhiteSpace(a.Point) ? a.Program : a.Point;
            if (ThemedMessageBox.Confirm(Window.GetWindow(this), $"Удалить точку «{title}»?", "ID и пароль этой точки тоже удалятся.", "Удалить"))
                _card?.Accesses.Remove(a);
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not RemoteAccess a) return;
            if (!string.IsNullOrWhiteSpace(a.AccessId)) TrySetClipboard(a.AccessId.Trim());
            try
            {
                StatusMessage?.Invoke(RemoteLauncher.Launch(a.Program, a.AccessId));
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(string.IsNullOrWhiteSpace(a.AccessId) ? ex.Message : ex.Message + " ID скопирован, вставьте его в программу вручную.");
            }
        }

        private void Pwd_Loaded(object sender, RoutedEventArgs e) => SyncPasswordBox(sender as PasswordBox);

        private void Pwd_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e) => SyncPasswordBox(sender as PasswordBox);

        private static void SyncPasswordBox(PasswordBox? pb)
        {
            if (pb?.DataContext is RemoteAccess a && pb.Password != a.Password) pb.Password = a.Password;
        }

        private void Pwd_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox pb && pb.DataContext is RemoteAccess a && a.Password != pb.Password)
                a.Password = pb.Password;
        }

        public void RefreshReminderCount()
        {
            var open = _card?.Reminders.Count(r => !r.Done) ?? 0;
            RemindersCount.Text = open > 0 ? open.ToString() : "";
            RemindersCount.Visibility = open > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddProject_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;
            var p = new ClientProject();
            _card.Projects.Add(p);
            FocusNewItem(ProjectsList, p);
        }

        private void RemoveProject_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not ClientProject p) return;
            var title = string.IsNullOrWhiteSpace(p.Name) ? "без названия" : $"«{p.Name}»";
            if (string.IsNullOrWhiteSpace(p.Name) && string.IsNullOrWhiteSpace(p.Comment) ||
                ThemedMessageBox.Confirm(Window.GetWindow(this), $"Удалить проект {title}?", "", "Удалить"))
                _card?.Projects.Remove(p);
        }

        private void AddReminder_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;
            var r = new Reminder { Due = DateTime.Today.AddDays(1).AddHours(10) };
            _card.Reminders.Add(r);
            RefreshReminderCount();
            FocusNewItem(RemindersList, r);
        }

        private void RemoveReminder_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Reminder r || _card == null) return;
            _card.Reminders.Remove(r);
            RefreshReminderCount();
        }

        private void DuePresets_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Reminder r) return;
            ReminderPicker.Open(fe, r.Due, null, false, false, (d, _) => r.Due = d);
        }

        private void AddNote_Click(object sender, RoutedEventArgs e) => AddNoteRequested?.Invoke();

        private void OpenNote_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is NoteItem n) OpenNoteRequested?.Invoke(n.Node);
        }
    }
}
