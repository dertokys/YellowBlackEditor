using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;

namespace YellowBlackEditor.Views
{
    public partial class ClientCardView : System.Windows.Controls.UserControl
    {
        public sealed record NoteItem(NodeViewModel Node, string Name, string Modified, DateTime Created);

        public event Action<string>? StatusMessage;
        public event Action? AddNoteRequested;
        public event Action<NodeViewModel>? OpenNoteRequested;
        public event Action<ClientProject>? ShareProjectRequested;
        public event Action<ClientProject>? OpenSharedRequested;
        public event Action<ClientProject>? ProjectRemoved;

        private NodeViewModel? _node;
        private ClientCard? _card;
        private ObservableCollection<Reminder>? _reminders;

        public ClientCardView()
        {
            InitializeComponent();
        }

        public void Show(NodeViewModel node, ClientCard card, ObservableCollection<Reminder> reminders)
        {
            Detach();
            _node = node;
            _card = card;
            _reminders = reminders;
            _card.Changed += Card_Changed;
            _reminders.CollectionChanged += Reminders_Changed;
            foreach (var r in _reminders) r.PropertyChanged += Reminder_PropertyChanged;
            DataContext = card;

            var projects = new ListCollectionView(card.Projects) { IsLiveSorting = true };
            projects.SortDescriptions.Add(new SortDescription(nameof(ClientProject.IsClosed), ListSortDirection.Ascending));
            projects.LiveSortingProperties.Add(nameof(ClientProject.IsClosed));
            ProjectsList.ItemsSource = projects;

            var rem = new ListCollectionView(reminders) { IsLiveSorting = true };
            rem.SortDescriptions.Add(new SortDescription(nameof(Reminder.Done), ListSortDirection.Ascending));
            rem.SortDescriptions.Add(new SortDescription(nameof(Reminder.Due), ListSortDirection.Ascending));
            rem.LiveSortingProperties.Add(nameof(Reminder.Done));
            RemindersList.ItemsSource = rem;

            RefreshNotes();
            RefreshCounts();
        }

        private void Detach()
        {
            if (_card != null) _card.Changed -= Card_Changed;
            if (_reminders != null)
            {
                _reminders.CollectionChanged -= Reminders_Changed;
                foreach (var r in _reminders) r.PropertyChanged -= Reminder_PropertyChanged;
            }
        }

        public void Clear()
        {
            Detach();
            _card = null;
            _node = null;
            _reminders = null;
            DataContext = null;
            NotesList.ItemsSource = null;
            ProjectsList.ItemsSource = null;
            RemindersList.ItemsSource = null;
        }

        private void Card_Changed(object? sender, EventArgs e)
        {
            if (_card == null) return;
            foreach (var p in _card.Projects) p.RefreshProgress();
            RefreshCounts();
        }

        private void Reminders_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null) foreach (Reminder r in e.NewItems) r.PropertyChanged += Reminder_PropertyChanged;
            if (e.OldItems != null) foreach (Reminder r in e.OldItems) r.PropertyChanged -= Reminder_PropertyChanged;
            RefreshCounts();
        }

        private void Reminder_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Reminder.Done)) RefreshCounts();
        }

        private static void SetCount(TextBlock target, int count)
        {
            target.Text = count > 0 ? count.ToString() : "";
            target.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RefreshCounts()
        {
            if (_card == null) return;
            SetCount(ContactsCount, _card.Contacts.Count);
            SetCount(PointsCount, _card.Accesses.Count);
            SetCount(CredentialsCount, _card.Credentials.Count);
            SetCount(ProjectsCount, _card.Projects.Count(p => !p.IsClosed));
            SetCount(RemindersCount, _reminders?.Count(r => !r.Done) ?? 0);

            ContactsEmpty.Visibility = _card.Contacts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ContactsHeader.Visibility = _card.Contacts.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            PointsEmpty.Visibility = _card.Accesses.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            CredentialsEmpty.Visibility = _card.Credentials.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ProjectsEmpty.Visibility = _card.Projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RemindersEmpty.Visibility = (_reminders?.Count ?? 0) == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void RebindNode(NodeViewModel node)
        {
            _node = node;
            RefreshNotes();
        }

        public void RefreshNotes()
        {
            if (_node == null) return;
            var items = _node.Children
                .Where(c => c.Type == NodeType.Document)
                .Select(c => new NoteItem(c, c.Name, ModifiedText(c.FullPath), CreatedAt(c.FullPath)))
                .OrderByDescending(n => n.Created)
                .ToList();
            NotesList.ItemsSource = items;
            NotesEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SetCount(NotesCount, items.Count);
        }

        private static DateTime CreatedAt(string folder)
        {
            try { return Directory.GetCreationTime(folder); } catch { return DateTime.MinValue; }
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

        private void ShareProject_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<ClientProject>(sender) is { } p) ShareProjectRequested?.Invoke(p);
        }

        private void OpenSharedProject_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<ClientProject>(sender) is { } p) OpenSharedRequested?.Invoke(p);
        }

        public void ShowProject(int index)
        {
            if (_card == null || index < 0 || index >= _card.Projects.Count) return;
            var p = _card.Projects[index];
            p.IsExpanded = true;
            Tabs.SelectedItem = ProjectsTab;
            FocusBoxFor(p);
        }

        public void FocusFirstField()
        {
            Tabs.SelectedIndex = 0;
            Dispatcher.BeginInvoke(() => InnBox.Focus(), DispatcherPriority.ContextIdle);
        }

        private static T? Ctx<T>(object sender) where T : class => (sender as FrameworkElement)?.DataContext as T;

        private bool ConfirmDelete(string header, string message = "") =>
            ThemedMessageBox.Confirm(Window.GetWindow(this), header, message, "Удалить");

        private void FocusNewItem(ItemsControl list, object item)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (list.ItemContainerGenerator.ContainerFromItem(item) is not DependencyObject container) return;
                var box = FindFirst<System.Windows.Controls.TextBox>(container);
                if (box == null) return;
                box.Focus();
                box.BringIntoView();
            }, DispatcherPriority.ContextIdle);
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

        private static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null && d is not T) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            return d as T;
        }

        private void FocusBoxFor(object item)
        {
            Dispatcher.BeginInvoke(() =>
            {
                var box = FindBoxFor(this, item);
                box?.Focus();
                box?.BringIntoView();
            }, DispatcherPriority.ContextIdle);
        }

        private static System.Windows.Controls.TextBox? FindBoxFor(DependencyObject root, object item)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is System.Windows.Controls.TextBox tb && ReferenceEquals(tb.DataContext, item)) return tb;
                var deeper = FindBoxFor(child, item);
                if (deeper != null) return deeper;
            }
            return null;
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
            StatusMessage?.Invoke("Скопировано в буфер обмена");
            if (sender is System.Windows.Controls.Button b) FlashCopied(b);
        }

        public static readonly DependencyProperty CopiedProperty =
            DependencyProperty.RegisterAttached("Copied", typeof(bool), typeof(ClientCardView), new PropertyMetadata(false));

        public static bool GetCopied(DependencyObject d) => (bool)d.GetValue(CopiedProperty);
        public static void SetCopied(DependencyObject d, bool value) => d.SetValue(CopiedProperty, value);

        private static void FlashCopied(System.Windows.Controls.Button b)
        {
            SetCopied(b, true);
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
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

        private void RemoveContact_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<ClientContact>(sender) is not { } c) return;
            var hasData = !string.IsNullOrWhiteSpace(c.Name) || !string.IsNullOrWhiteSpace(c.Phone) || !string.IsNullOrWhiteSpace(c.Role);
            if (hasData && !ConfirmDelete($"Удалить контакт «{(string.IsNullOrWhiteSpace(c.Name) ? c.Phone : c.Name)}»?")) return;
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
            if (Ctx<RemoteAccess>(sender) is not { } a) return;
            var title = string.IsNullOrWhiteSpace(a.Point) ? a.Program : a.Point;
            if (ConfirmDelete($"Удалить точку «{title}»?", "ID, телефоны и пароли этой точки тоже удалятся."))
                _card?.Accesses.Remove(a);
        }

        private void AddPointPhone_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<RemoteAccess>(sender) is not { } a) return;
            var c = new ClientContact();
            a.People.Add(c);
            FocusBoxFor(c);
        }

        private void RemovePointPhone_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<ClientContact>(sender) is not { } c || _card == null) return;
            var owner = _card.Accesses.FirstOrDefault(a => a.People.Contains(c));
            if (owner == null) return;
            var empty = string.IsNullOrWhiteSpace(c.Name) && string.IsNullOrWhiteSpace(c.Phone) && string.IsNullOrWhiteSpace(c.Role);
            if (empty || ConfirmDelete($"Удалить телефон «{(string.IsNullOrWhiteSpace(c.Name) ? c.Phone : c.Name)}»?"))
                owner.People.Remove(c);
        }

        private void AddPointLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<RemoteAccess>(sender) is not { } a) return;
            var l = new Credential();
            a.Logins.Add(l);
            FocusBoxFor(l);
        }

        private void RemovePointLogin_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<Credential>(sender) is not { } l || _card == null) return;
            var owner = _card.Accesses.FirstOrDefault(a => a.Logins.Contains(l));
            if (owner == null) return;
            var empty = string.IsNullOrWhiteSpace(l.Title) && string.IsNullOrWhiteSpace(l.Login) && string.IsNullOrWhiteSpace(l.Password);
            if (empty || ConfirmDelete($"Удалить пароль «{(string.IsNullOrWhiteSpace(l.Title) ? l.Login : l.Title)}»?"))
                owner.Logins.Remove(l);
        }

        private void Connect_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<RemoteAccess>(sender) is not { } a) return;
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

        private void AddCredential_Click(object sender, RoutedEventArgs e)
        {
            if (_card == null) return;
            var c = new Credential();
            _card.Credentials.Add(c);
            FocusNewItem(CredentialsList, c);
        }

        private void RemoveCredential_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<Credential>(sender) is not { } c) return;
            var empty = string.IsNullOrWhiteSpace(c.Title) && string.IsNullOrWhiteSpace(c.Login) && string.IsNullOrWhiteSpace(c.Password);
            if (empty || ConfirmDelete($"Удалить доступ «{(string.IsNullOrWhiteSpace(c.Title) ? c.Url : c.Title)}»?"))
                _card?.Credentials.Remove(c);
        }

        private void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<Credential>(sender) is not { } c) return;
            var url = c.Url.Trim();
            if (url.Length == 0) return;
            if (!url.Contains("://")) url = "https://" + url;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke("Не удалось открыть ссылку: " + ex.Message);
            }
        }

        private void EditLink_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<Credential>(sender) is not { } c) return;
            c.EditingUrl = true;
            var grid = FindAncestor<Grid>(FindAncestor<Border>((DependencyObject)sender));
            var box = grid?.Children.OfType<System.Windows.Controls.TextBox>().FirstOrDefault();
            Dispatcher.BeginInvoke(() =>
            {
                box?.Focus();
                box?.SelectAll();
            }, DispatcherPriority.Input);
        }

        private void UrlBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (Ctx<Credential>(sender) is { } c) c.EditingUrl = false;
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
            if (Ctx<ClientProject>(sender) is not { } p) return;
            var empty = string.IsNullOrWhiteSpace(p.Name) && string.IsNullOrWhiteSpace(p.Comment) && p.Tasks.Count == 0;
            var message = p.IsShared ? "Этапы проекта тоже удалятся. Общий проект у коллег останется." : "Этапы проекта тоже удалятся.";
            if (empty || ConfirmDelete($"Удалить проект «{(string.IsNullOrWhiteSpace(p.Name) ? "без названия" : p.Name)}»?", message))
            {
                _card?.Projects.Remove(p);
                ProjectRemoved?.Invoke(p);
            }
        }

        private void Deadline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not ClientProject p) return;
            ReminderPicker.Open(fe, p.Deadline, null, false, p.Deadline != null, (d, _) => p.Deadline = d.Date,
                () => p.Deadline = null, dateOnly: true, removeText: "Убрать срок");
        }

        private void AddTask_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<ClientProject>(sender) is not { } p) return;
            var t = new ProjectTask();
            p.Tasks.Add(t);
            p.IsExpanded = true;
            FocusBoxFor(t);
        }

        private void AddSubtask_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<ProjectTask>(sender) is not { } parent) return;
            var t = new ProjectTask();
            parent.Children.Add(t);
            FocusBoxFor(t);
        }

        private ObservableCollection<ProjectTask>? OwnerList(ProjectTask task)
        {
            if (_card == null) return null;
            foreach (var p in _card.Projects)
            {
                if (p.Tasks.Contains(task)) return p.Tasks;
                foreach (var t in p.AllTasks())
                    if (t.Children.Contains(task)) return t.Children;
            }
            return null;
        }

        private void RemoveTask_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<ProjectTask>(sender) is not { } task) return;
            var list = OwnerList(task);
            if (list == null) return;
            if (task.Children.Count > 0 && !ConfirmDelete($"Удалить этап «{task.Title}»?", "Вложенные этапы тоже удалятся.")) return;
            list.Remove(task);
        }

        private void TaskTitle_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Ctx<ProjectTask>(sender) is not { } task) return;
            var list = OwnerList(task);
            if (list == null) return;
            var t = new ProjectTask();
            list.Insert(list.IndexOf(task) + 1, t);
            FocusBoxFor(t);
            e.Handled = true;
        }

        private void AddReminder_Click(object sender, RoutedEventArgs e)
        {
            if (_reminders == null) return;
            var r = new Reminder { Due = DateText.DefaultReminderTime() };
            _reminders.Add(r);
            FocusNewItem(RemindersList, r);
        }

        private void RemoveReminder_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<Reminder>(sender) is { } r) _reminders?.Remove(r);
        }

        private void DuePresets_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not Reminder r) return;
            ReminderPicker.Open(fe, r.Due, null, false, false, (d, _) =>
            {
                r.Due = d;
                r.Notified = false;
            });
        }

        private void AddNote_Click(object sender, RoutedEventArgs e) => AddNoteRequested?.Invoke();

        private void OpenNote_Click(object sender, RoutedEventArgs e)
        {
            if (Ctx<NoteItem>(sender) is { } n) OpenNoteRequested?.Invoke(n.Node);
        }
    }
}
