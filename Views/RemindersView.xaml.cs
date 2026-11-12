using System.Windows;
using System.Windows.Input;
using YellowBlackEditor.Models;
using YellowBlackEditor.ViewModels;

namespace YellowBlackEditor.Views
{
    public sealed class ReminderEntry
    {
        public Reminder Item { get; init; } = new();
        public NodeViewModel? Client { get; init; }
        public NodeViewModel? Note { get; init; }
        public string ClientName => Client?.Name ?? "";
        public Visibility ClientVisibility => Client == null ? Visibility.Collapsed : Visibility.Visible;
        public string NoteName => Note?.Name ?? "";
        public Visibility NoteVisibility => Note == null ? Visibility.Collapsed : Visibility.Visible;
    }

    public sealed record ReminderGroup(string Title, int Count, List<ReminderEntry> Items);

    public partial class RemindersView : System.Windows.Controls.UserControl
    {
        public event Action<ReminderEntry>? EntryChanged;
        public event Action<ReminderEntry>? EntryDeleted;
        public event Action<NodeViewModel>? OpenClientRequested;
        public event Action<NodeViewModel>? OpenNoteRequested;
        public event Action<Reminder>? AddRequested;
        public event Action<string>? StatusMessage;

        private Reminder _draft = NewDraft();

        public RemindersView()
        {
            InitializeComponent();
            AddRow.DataContext = _draft;
        }

        private static Reminder NewDraft() => new() { Due = DateText.DefaultReminderTime() };

        public void Show(IReadOnlyList<ReminderEntry> entries)
        {
            var now = DateTime.Now;
            var today = DateTime.Today;
            var open = entries.Where(e => !e.Item.Done).ToList();

            var groups = new List<ReminderGroup>();
            void Add(string title, IEnumerable<ReminderEntry> items)
            {
                var list = items.ToList();
                if (list.Count > 0) groups.Add(new ReminderGroup(title, list.Count, list));
            }

            Add("Просрочено", open.Where(e => e.Item.Due != null && e.Item.Due <= now).OrderBy(e => e.Item.Due));
            Add("Сегодня", open.Where(e => e.Item.Due != null && e.Item.Due > now && e.Item.Due.Value.Date == today).OrderBy(e => e.Item.Due));
            Add("Позже", open.Where(e => e.Item.Due != null && e.Item.Due.Value.Date > today).OrderBy(e => e.Item.Due));
            Add("Без срока", open.Where(e => e.Item.Due == null));
            Add("Выполнено", entries.Where(e => e.Item.Done).OrderByDescending(e => e.Item.Due).Take(20));

            Groups.ItemsSource = groups;
            EmptyText.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static ReminderEntry? EntryOf(object sender) => (sender as FrameworkElement)?.DataContext as ReminderEntry;

        private void Done_Click(object sender, RoutedEventArgs e)
        {
            if (EntryOf(sender) is { } entry) EntryChanged?.Invoke(entry);
        }

        private void Snooze_Click(object sender, RoutedEventArgs e)
        {
            if (EntryOf(sender) is not { } entry) return;
            var t = DateTime.Now.AddHours(1);
            entry.Item.Due = new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0);
            EntryChanged?.Invoke(entry);
            StatusMessage?.Invoke($"Отложено до {entry.Item.Due:HH:mm}");
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (EntryOf(sender) is { } entry) EntryDeleted?.Invoke(entry);
        }

        private void OpenClient_Click(object sender, RoutedEventArgs e)
        {
            if (EntryOf(sender)?.Client is { } client) OpenClientRequested?.Invoke(client);
        }

        private void OpenNote_Click(object sender, RoutedEventArgs e)
        {
            if (EntryOf(sender)?.Note is { } note) OpenNoteRequested?.Invoke(note);
        }

        private void NewPresets_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe) ReminderPicker.Open(fe, _draft.Due, null, false, false, (d, _) => _draft.Due = d);
        }

        private void NewText_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Add_Click(sender, e);
                e.Handled = true;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_draft.Text))
            {
                StatusMessage?.Invoke("Напишите, что напомнить");
                NewText.Focus();
                return;
            }
            var r = _draft;
            r.Text = r.Text.Trim();
            _draft = NewDraft();
            AddRow.DataContext = _draft;
            AddRequested?.Invoke(r);
            NewText.Focus();
        }
    }
}
