using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using YellowBlackEditor.Models;
using UserControl = System.Windows.Controls.UserControl;

namespace YellowBlackEditor.Views
{
    public partial class TodayView : UserControl
    {
        public sealed record Item(string Title, string Sub, string Right, int Level, Action? Open);

        public sealed class Data
        {
            public List<Item> Reminders { get; } = new();
            public List<Item> Projects { get; } = new();
            public List<Item> Subscriptions { get; } = new();
            public List<Item> Time { get; } = new();
            public int MinutesToday { get; set; }
            public int MinutesWeek { get; set; }
        }

        public event Action? ReportRequested;

        public TodayView()
        {
            InitializeComponent();
        }

        public void Show(Data d)
        {
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            DateTitle.Text = Cap(DateTime.Today.ToString("dddd, d MMMM", ru));
            TodayMinutes.Text = TimeText.Human(d.MinutesToday);
            WeekMinutes.Text = TimeText.Human(d.MinutesWeek);

            var urgent = d.Reminders.Count(i => i.Level == 2) + d.Projects.Count(i => i.Level == 2) + d.Subscriptions.Count(i => i.Level == 2);
            var parts = new List<string>();
            if (d.Reminders.Count > 0) parts.Add($"напоминаний: {d.Reminders.Count}");
            if (d.Projects.Count > 0) parts.Add($"проектов со сроком: {d.Projects.Count}");
            if (d.Subscriptions.Count > 0) parts.Add($"подписок к продлению: {d.Subscriptions.Count}");
            SummaryText.Text = parts.Count == 0
                ? "Срочного ничего нет."
                : (urgent > 0 ? $"Просрочено: {urgent}. " : "") + Cap(string.Join(", ", parts)) + ".";

            Fill(RemindersList, RemindersEmpty, RemindersCount, d.Reminders);
            Fill(ProjectsList, ProjectsEmpty, ProjectsCount, d.Projects);
            Fill(SubsList, SubsEmpty, SubsCount, d.Subscriptions);
            Fill(TimeList, TimeEmpty, TimeCount, d.Time);
            TimeCount.Text = d.MinutesToday > 0 ? TimeText.Human(d.MinutesToday) : "";
        }

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

        private static void Fill(ItemsControl list, TextBlock empty, TextBlock count, List<Item> items)
        {
            list.ItemsSource = items;
            empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            count.Text = items.Count > 0 ? items.Count.ToString() : "";
        }

        private void Item_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is Item { Open: { } open }) open();
        }

        private void Report_Click(object sender, RoutedEventArgs e) => ReportRequested?.Invoke();
    }
}
