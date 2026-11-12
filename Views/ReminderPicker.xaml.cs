using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Views
{
    public partial class ReminderPicker : System.Windows.Controls.UserControl
    {
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        private DateTime? _date;
        private TimeSpan? _time;
        private bool _syncing;
        private Popup? _popup;
        private Action<DateTime, string>? _onOk;
        private Action? _onRemove;
        private bool _dateOnly;

        public ReminderPicker()
        {
            InitializeComponent();
            PreviewKeyDown += Picker_PreviewKeyDown;
        }

        public static void Open(FrameworkElement anchor, DateTime? initial, string? text, bool showText, bool canRemove,
            Action<DateTime, string> onOk, Action? onRemove = null, bool dateOnly = false, string removeText = "Убрать напоминание")
        {
            var picker = new ReminderPicker();
            picker._dateOnly = dateOnly;
            picker.RemoveBtn.Content = removeText;
            picker.Init(initial, text, showText, canRemove, onOk, onRemove);
            var popup = new Popup
            {
                PlacementTarget = anchor,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                Child = picker,
                VerticalOffset = 6
            };
            picker._popup = popup;
            popup.Opened += (_, _) => picker.Dispatcher.BeginInvoke(() =>
            {
                if (showText)
                {
                    picker.ReminderText.Focus();
                    picker.ReminderText.SelectAll();
                }
                else
                {
                    picker.FocusSelectedChip();
                }
            }, System.Windows.Threading.DispatcherPriority.Input);
            popup.IsOpen = true;
        }

        private void Init(DateTime? initial, string? text, bool showText, bool canRemove, Action<DateTime, string> onOk, Action? onRemove)
        {
            _onOk = onOk;
            _onRemove = onRemove;
            TextBlockPanel.Visibility = showText ? Visibility.Visible : Visibility.Collapsed;
            ReminderText.Text = text ?? "";
            RemoveBtn.Visibility = canRemove ? Visibility.Visible : Visibility.Collapsed;

            var start = initial ?? (_dateOnly ? DateTime.Today : DateText.DefaultReminderTime());
            _date = start.Date;
            _time = _dateOnly ? TimeSpan.Zero : new TimeSpan(start.Hour, start.Minute, 0);
            if (_dateOnly)
            {
                TimeLabel.Visibility = Visibility.Collapsed;
                Times.Visibility = Visibility.Collapsed;
                CustomTimePanel.Visibility = Visibility.Collapsed;
            }

            BuildDays();
            BuildTimes();
            SyncChips();
            UpdateResult();
        }

        private void BuildDays()
        {
            var today = DateTime.Today;
            for (var i = 0; i < 12; i++)
            {
                var d = today.AddDays(i);
                var label = i == 0 ? "Сегодня" : i == 1 ? "Завтра" : Ru.TextInfo.ToTitleCase(d.ToString("ddd d", Ru));
                if (i > 1 && d.Day == 1) label = d.ToString("d MMM", Ru);
                var chip = new System.Windows.Controls.RadioButton
                {
                    Content = label,
                    Tag = d,
                    GroupName = "day" + GetHashCode(),
                    Style = (Style)Resources["Chip"],
                    ToolTip = d.ToString("dddd, d MMMM", Ru)
                };
                chip.Checked += (_, _) =>
                {
                    if (_syncing) return;
                    _date = d;
                    SetCustomDate("");
                    UpdateResult();
                };
                Days.Children.Add(chip);
            }
        }

        private void BuildTimes()
        {
            for (var h = 8; h <= 19; h++)
            {
                var t = new TimeSpan(h, 0, 0);
                var chip = new System.Windows.Controls.RadioButton
                {
                    Content = $"{h:00}:00",
                    Tag = t,
                    GroupName = "time" + GetHashCode(),
                    Style = (Style)Resources["Chip"]
                };
                chip.Checked += (_, _) =>
                {
                    if (_syncing) return;
                    _time = t;
                    SetCustomTime("");
                    UpdateResult();
                };
                Times.Children.Add(chip);
            }
        }

        private void SyncChips()
        {
            _syncing = true;
            var dayFound = false;
            foreach (System.Windows.Controls.RadioButton c in Days.Children)
            {
                var hit = _date != null && (DateTime)c.Tag == _date.Value;
                c.IsChecked = hit;
                dayFound |= hit;
            }
            var timeFound = false;
            foreach (System.Windows.Controls.RadioButton c in Times.Children)
            {
                var hit = _time != null && (TimeSpan)c.Tag == _time.Value;
                c.IsChecked = hit;
                timeFound |= hit;
            }
            CustomDate.Text = dayFound || _date == null ? "" : _date.Value.ToString("dd.MM.yyyy");
            CustomTime.Text = timeFound || _time == null ? "" : $"{_time.Value.Hours:00}:{_time.Value.Minutes:00}";
            _syncing = false;
        }

        private void FocusSelectedChip()
        {
            foreach (System.Windows.Controls.RadioButton c in Days.Children)
                if (c.IsChecked == true)
                {
                    c.Focus();
                    return;
                }
            CustomDate.Focus();
        }

        private void SetCustomDate(string value)
        {
            _syncing = true;
            CustomDate.Text = value;
            _syncing = false;
        }

        private void SetCustomTime(string value)
        {
            _syncing = true;
            CustomTime.Text = value;
            _syncing = false;
        }

        private void CustomDate_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            var t = CustomDate.Text.Trim();
            if (t.Length == 0) return;
            _date = DateText.TryParse(t, false, out var d) && d != null ? d.Value.Date : null;
            _syncing = true;
            foreach (System.Windows.Controls.RadioButton c in Days.Children) c.IsChecked = _date != null && (DateTime)c.Tag == _date.Value;
            _syncing = false;
            UpdateResult();
        }

        private void CustomTime_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            var t = CustomTime.Text.Trim();
            if (t.Length == 0) return;
            _time = DateText.TryParseTime(t);
            _syncing = true;
            foreach (System.Windows.Controls.RadioButton c in Times.Children) c.IsChecked = _time != null && (TimeSpan)c.Tag == _time.Value;
            _syncing = false;
            UpdateResult();
        }

        private DateTime? Result => _date != null && _time != null ? _date.Value.Add(_time.Value) : null;

        private string? Problem()
        {
            if (_date == null) return "Выберите день или введите дату в виде 15.11.2026.";
            if (_time == null) return "Выберите время или введите его в виде 14:30.";
            if (!_dateOnly && Result <= DateTime.Now) return "Это время уже прошло, выберите позже.";
            return null;
        }

        private void UpdateResult()
        {
            var r = Result;
            ResultText.Text = r == null ? "" : _dateOnly ? "Срок: " + DateText.HumanDate(r.Value) : "Напомню " + DateText.Human(r.Value);
            ErrorText.Visibility = Visibility.Collapsed;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

        private void Confirm()
        {
            var problem = Problem();
            if (problem != null)
            {
                ErrorText.Text = problem;
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            var text = ReminderText.Text.Trim();
            if (_popup != null) _popup.IsOpen = false;
            _onOk?.Invoke(Result!.Value, text);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_popup != null) _popup.IsOpen = false;
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (_popup != null) _popup.IsOpen = false;
            _onRemove?.Invoke();
        }

        private void Picker_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (_popup != null) _popup.IsOpen = false;
                e.Handled = true;
            }
        }
    }
}
