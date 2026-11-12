using System.Windows;
using System.Windows.Input;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace YellowBlackEditor.Views
{
    public partial class TimeEntryWindow : Window
    {
        public sealed record HistoryItem(TimeEntry Entry, string DateText, string Comment, string MinutesText);

        private static readonly (string Label, int Minutes)[] Quick =
        {
            ("5 мин", 5), ("10 мин", 10), ("15 мин", 15), ("20 мин", 20), ("30 мин", 30), ("45 мин", 45),
            ("1 ч", 60), ("1,5 ч", 90), ("2 ч", 120), ("3 ч", 180), ("4 ч", 240), ("8 ч", 480)
        };

        private readonly string _folder;
        private readonly List<TimeEntry> _entries;
        private DateTime _date = DateTime.Today;

        public bool Changed { get; private set; }

        private TimeEntryWindow(string folder, string context)
        {
            InitializeComponent();
            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            _folder = folder;
            _entries = TimeStore.Load(folder);
            ContextText.Text = context;
            foreach (var (label, minutes) in Quick)
            {
                var b = new Button { Content = label, Style = (Style)FindResource("Quick"), Tag = minutes };
                b.Click += (_, _) =>
                {
                    MinutesBox.Text = minutes.ToString();
                    CommentBox.Focus();
                };
                QuickPanel.Children.Add(b);
            }
            UpdateDate();
            RefreshHistory();
            Loaded += (_, _) => MinutesBox.Focus();
        }

        public static bool Show(Window owner, string folder, string context)
        {
            var w = new TimeEntryWindow(folder, context) { Owner = owner };
            w.ShowDialog();
            return w.Changed;
        }

        private void UpdateDate()
        {
            DateText.Text = _date == DateTime.Today ? "Сегодня" : _date == DateTime.Today.AddDays(-1) ? "Вчера" : _date.ToString("dd.MM.yyyy");
        }

        private void RefreshHistory()
        {
            var items = _entries.OrderByDescending(e => e.Date).ThenByDescending(e => e.CreatedAt)
                .Select(e => new HistoryItem(e, DateText_(e.Date), string.IsNullOrWhiteSpace(e.Comment) ? "—" : e.Comment, TimeText.Human(e.Minutes)))
                .ToList();
            HistoryList.ItemsSource = items;
            HistoryCard.Visibility = items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            TotalText.Text = "всего " + TimeText.Human(_entries.Sum(e => e.Minutes));
        }

        private static string DateText_(DateTime d) => Models.DateText.HumanDate(d);

        private void MinutesBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ErrorText.Text = "";
            if (MinutesBox.Text.Trim().Length == 0)
            {
                ParsedText.Text = "Можно написать 90, 1:30 или 1,5 ч";
                return;
            }
            ParsedText.Text = TimeText.TryParse(MinutesBox.Text, out var m) ? "Будет записано: " + TimeText.Human(m) : "Не понимаю. Напишите минуты числом, например 40";
        }

        private void Box_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            Save_Click(sender, e);
        }

        private void Date_Click(object sender, RoutedEventArgs e)
        {
            ReminderPicker.Open(DateBtn, _date, null, false, false, (d, _) =>
            {
                _date = d.Date;
                UpdateDate();
            }, () => { }, dateOnly: true);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!TimeText.TryParse(MinutesBox.Text, out var minutes))
            {
                ErrorText.Text = MinutesBox.Text.Trim().Length == 0 ? "Укажите, сколько минут" : "Напишите минуты числом, например 40";
                MinutesBox.Focus();
                return;
            }
            _entries.Add(new TimeEntry { Date = _date, Minutes = minutes, Comment = CommentBox.Text.Trim() });
            if (!Persist()) return;
            DialogResult = true;
        }

        private bool Persist()
        {
            try
            {
                TimeStore.Save(_folder, _entries);
                Changed = true;
                return true;
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Не удалось сохранить: " + ex.Message;
                return false;
            }
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not HistoryItem item) return;
            _entries.Remove(item.Entry);
            if (Persist()) RefreshHistory();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
