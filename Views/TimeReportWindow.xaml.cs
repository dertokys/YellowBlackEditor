using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;

namespace YellowBlackEditor.Views
{
    public partial class TimeReportWindow : Window
    {
        public sealed record Row(string Left, string Main, string Comment, Visibility CommentVisibility, string MinutesText,
            Visibility TagVisibility, int Ticket, System.Windows.Input.Cursor? Cursor, string? Hint);
        public sealed record Group(string Title, string TotalText, List<Row> Rows);

        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private readonly List<TimeRow> _all;
        private DateTime _start;
        private bool _month;
        private bool _byClient;
        private readonly Func<DateTime, DateTime, CancellationToken, Task<List<TimeRow>>>? _zammad;
        private readonly Action<int>? _openTicket;
        private readonly Dictionary<(DateTime, DateTime), List<TimeRow>> _zammadRows = new();
        private readonly HashSet<(DateTime, DateTime)> _zammadFailed = new();
        private (DateTime, DateTime)? _zammadLoading;
        private CancellationTokenSource? _zammadCts;
        private string _zammadNote = "";

        public bool ZammadOn => ZammadBtn.IsChecked == true;

        public TimeReportWindow(List<TimeRow> all, Func<DateTime, DateTime, CancellationToken, Task<List<TimeRow>>>? zammad = null,
            Action<int>? openTicket = null, bool zammadOn = true)
        {
            InitializeComponent();
            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            Closed += (_, _) => _zammadCts?.Cancel();
            _all = all;
            _zammad = zammad;
            _openTicket = openTicket;
            ZammadCard.Visibility = zammad == null ? Visibility.Collapsed : Visibility.Visible;
            ZammadBtn.IsChecked = zammad != null && zammadOn;
            _start = WeekStart(DateTime.Today);
            Refresh();
        }

        private bool UseZammad => _zammad != null && ZammadBtn.IsChecked == true;

        private (DateTime, DateTime) Key => (_start, End);

        private static DateTime WeekStart(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

        private DateTime End => _month ? _start.AddMonths(1) : _start.AddDays(7);

        private static string ClientTitle(TimeRow r) => r.Client.Length > 0 ? r.Client : r.Section.Length > 0 ? r.Section : "Без клиента";

        private List<TimeRow> Current()
        {
            var rows = _all.Where(r => r.Entry.Date >= _start && r.Entry.Date < End);
            if (UseZammad && _zammadRows.TryGetValue(Key, out var extra)) rows = rows.Concat(extra);
            return rows.OrderBy(r => r.Entry.Date).ThenBy(r => r.Entry.CreatedAt).ToList();
        }

        private string PeriodTitle()
        {
            if (_month) return Cap(_start.ToString("MMMM yyyy", Ru));
            var end = End.AddDays(-1);
            return _start.Month == end.Month
                ? $"{_start.Day}–{end.ToString("d MMMM yyyy", Ru)}"
                : $"{_start.ToString("d MMMM", Ru)} – {end.ToString("d MMMM yyyy", Ru)}";
        }

        private void Refresh()
        {
            WeekBtn.IsChecked = !_month;
            MonthBtn.IsChecked = _month;
            ByDayBtn.IsChecked = !_byClient;
            ByClientBtn.IsChecked = _byClient;
            NowBtn.Content = _month ? "Текущий месяц" : "Текущая неделя";
            PeriodText.Text = PeriodTitle();

            var rows = Current();
            var total = rows.Sum(r => r.Entry.Minutes);
            TotalText.Text = TimeText.Human(total);
            CountText.Text = rows.Count.ToString();
            ClientsText.Text = rows.Select(ClientTitle).Distinct().Count().ToString();
            var workDays = Enumerable.Range(0, (End - _start).Days).Select(i => _start.AddDays(i))
                .Count(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && d <= DateTime.Today);
            AvgText.Text = workDays == 0 ? "—" : TimeText.Human(total / workDays);
            EmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            Row MakeRow(TimeRow r, string left, string main)
            {
                var ticket = ZammadApi.TicketOf(r.Folder);
                return new(left, main, r.Entry.Comment,
                    string.IsNullOrWhiteSpace(r.Entry.Comment) ? Visibility.Collapsed : Visibility.Visible, TimeText.Human(r.Entry.Minutes),
                    ticket > 0 ? Visibility.Visible : Visibility.Collapsed, ticket,
                    ticket > 0 && _openTicket != null ? System.Windows.Input.Cursors.Hand : null,
                    ticket > 0 ? "Заявка из Zammad, списано " + r.Entry.CreatedAt.ToString("dd.MM HH:mm") + ". Нажмите, чтобы открыть" : null);
            }

            List<Group> groups;
            if (_byClient)
            {
                groups = rows.GroupBy(ClientTitle)
                    .OrderByDescending(g => g.Sum(r => r.Entry.Minutes))
                    .Select(g => new Group(g.Key, TimeText.Human(g.Sum(r => r.Entry.Minutes)),
                        g.Select(r => MakeRow(r, DateText.HumanDate(r.Entry.Date), r.Title)).ToList()))
                    .ToList();
            }
            else
            {
                groups = rows.GroupBy(r => r.Entry.Date.Date)
                    .OrderBy(g => g.Key)
                    .Select(g => new Group(Cap(g.Key.ToString("dddd, d MMMM", Ru)), TimeText.Human(g.Sum(r => r.Entry.Minutes)),
                        g.Select(r => MakeRow(r, ClientTitle(r), r.Title)).ToList()))
                    .ToList();
            }
            Groups.ItemsSource = groups;
            if (UseZammad && !_zammadRows.ContainsKey(Key) && !_zammadFailed.Contains(Key) && _zammadLoading != Key) LoadZammad(Key);
            ShowZammadState();
            StatusText.Text = "";
        }

        private void ShowZammadState()
        {
            if (_zammad == null) return;
            if (!UseZammad)
            {
                ZammadValue.Text = "не учитывается";
                ZammadNote.Text = "Поставьте галочку, чтобы добавить время из Zammad";
            }
            else if (_zammadRows.TryGetValue(Key, out var rows))
            {
                var tickets = rows.Select(r => r.Folder).Distinct().Count();
                ZammadValue.Text = TimeText.Human(rows.Sum(r => r.Entry.Minutes));
                ZammadNote.Text = tickets == 0 ? "За этот период время не списано" : $"По {tickets} {(tickets % 10 == 1 && tickets % 100 != 11 ? "заявке" : "заявкам")}, уже в отчёте";
            }
            else if (_zammadLoading == Key)
            {
                ZammadValue.Text = "…";
                ZammadNote.Text = "Загружаю из Zammad";
            }
            else
            {
                ZammadValue.Text = "—";
                ZammadNote.Text = _zammadNote;
            }
        }

        private async void LoadZammad((DateTime From, DateTime To) key)
        {
            _zammadCts?.Cancel();
            var cts = new CancellationTokenSource();
            _zammadCts = cts;
            _zammadLoading = key;
            ShowZammadState();
            try
            {
                var rows = await _zammad!(key.From, key.To, cts.Token);
                _zammadRows[key] = rows;
            }
            catch (OperationCanceledException) when (cts.IsCancellationRequested)
            {
                return;
            }
            catch (ZammadAuthException)
            {
                _zammadFailed.Add(key);
                _zammadNote = "Нет входа в Zammad. Войдите в разделе «Заявки» и поставьте галочку заново";
            }
            catch (Exception ex)
            {
                _zammadFailed.Add(key);
                _zammadNote = "Не загрузилось: " + ex.Message;
            }
            finally
            {
                if (_zammadLoading == key) _zammadLoading = null;
            }
            if (!IsLoaded || _zammadCts != cts) return;
            Refresh();
        }

        private void Zammad_Click(object sender, RoutedEventArgs e)
        {
            _zammadFailed.Clear();
            _zammadNote = "";
            Refresh();
        }

        private void Row_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not Row { Ticket: > 0 } row || _openTicket == null) return;
            WindowState = WindowState.Minimized;
            _openTicket(row.Ticket);
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            _start = _month ? _start.AddMonths(-1) : _start.AddDays(-7);
            Refresh();
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            _start = _month ? _start.AddMonths(1) : _start.AddDays(7);
            Refresh();
        }

        private void Now_Click(object sender, RoutedEventArgs e)
        {
            _start = _month ? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1) : WeekStart(DateTime.Today);
            Refresh();
        }

        private void Week_Click(object sender, RoutedEventArgs e)
        {
            _month = false;
            _start = WeekStart(_start);
            Refresh();
        }

        private void Month_Click(object sender, RoutedEventArgs e)
        {
            _month = true;
            _start = new DateTime(_start.Year, _start.Month, 1);
            Refresh();
        }

        private void ByDay_Click(object sender, RoutedEventArgs e)
        {
            _byClient = false;
            Refresh();
        }

        private void ByClient_Click(object sender, RoutedEventArgs e)
        {
            _byClient = true;
            Refresh();
        }

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            var rows = Current();
            var end = End.AddDays(-1);
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Сохранить отчёт",
                Filter = "Таблица для Excel (*.csv)|*.csv",
                FileName = $"Трудозатраты {_start:dd.MM}–{end:dd.MM.yyyy}.csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };
            if (dlg.ShowDialog(this) != true) return;
            var sb = new StringBuilder();
            sb.AppendLine("Дата;Клиент;Заметка или проект;Что делали;Минут;Часов");
            foreach (var r in rows)
            {
                sb.Append(r.Entry.Date.ToString("dd.MM.yyyy")).Append(';')
                  .Append(Csv(ClientTitle(r))).Append(';')
                  .Append(Csv(r.Title)).Append(';')
                  .Append(Csv(r.Entry.Comment)).Append(';')
                  .Append(r.Entry.Minutes).Append(';')
                  .AppendLine((r.Entry.Minutes / 60.0).ToString("0.##", Ru));
            }
            var total = rows.Sum(r => r.Entry.Minutes);
            sb.Append(";;;Итого;").Append(total).Append(';').AppendLine((total / 60.0).ToString("0.##", Ru));
            try
            {
                File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(true));
                StatusText.Text = "Сохранено: " + dlg.FileName;
            }
            catch (Exception ex)
            {
                StatusText.Text = "Не удалось сохранить: " + ex.Message;
            }
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Трудозатраты: " + PeriodTitle());
            sb.AppendLine("Всего: " + TotalText.Text);
            foreach (var g in (List<Group>)Groups.ItemsSource)
            {
                sb.AppendLine();
                sb.AppendLine($"{g.Title} — {g.TotalText}");
                foreach (var r in g.Rows)
                    sb.AppendLine($"  {r.Left} · {r.Main}{(string.IsNullOrWhiteSpace(r.Comment) ? "" : ": " + r.Comment)} — {r.MinutesText}");
            }
            try
            {
                System.Windows.Clipboard.SetText(sb.ToString());
                StatusText.Text = "Отчёт скопирован, его можно вставить в письмо или мессенджер";
            }
            catch
            {
                StatusText.Text = "Не удалось скопировать";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
