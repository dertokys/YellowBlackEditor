using System.Globalization;
using System.Text.Json.Serialization;

namespace YellowBlackEditor.Models
{
    public static class DateText
    {
        private static readonly string[] Formats =
        {
            "dd.MM.yyyy HH:mm", "d.M.yyyy H:mm", "dd.MM.yy HH:mm", "d.M.yy H:mm",
            "dd.MM HH:mm", "d.M H:mm", "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy", "dd.MM", "d.M"
        };

        public static string Format(DateTime? value, bool withTime)
        {
            if (value == null) return "";
            var v = value.Value;
            if (!withTime) return v.ToString("dd.MM.yyyy");
            return v.ToString("dd.MM.yyyy HH:mm");
        }

        public static bool TryParse(string text, bool withTime, out DateTime? value)
        {
            value = null;
            var t = (text ?? "").Trim();
            if (t.Length == 0) return true;
            if (DateTime.TryParseExact(t, Formats, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.AllowWhiteSpaces, out var d))
            {
                if (!t.Contains(' ') && withTime) d = d.Date.AddHours(10);
                var datePart = t.Split(' ')[0];
                if (datePart.Count(c => c == '.') == 1 && d.Date < DateTime.Today) d = d.AddYears(1);
                value = d;
                return true;
            }
            return false;
        }

        public static string Human(DateTime value)
        {
            var today = DateTime.Today;
            var time = value.ToString("HH:mm");
            if (value.Date == today) return "сегодня, " + time;
            if (value.Date == today.AddDays(1)) return "завтра, " + time;
            if (value.Date == today.AddDays(-1)) return "вчера, " + time;
            return value.ToString(value.Year == today.Year ? "d MMMM, HH:mm" : "d MMMM yyyy, HH:mm", CultureInfo.GetCultureInfo("ru-RU"));
        }
    }

    public class Reminder : Observable
    {
        private string _text = "";
        private DateTime? _due;
        private bool _done;
        private string _dueText = "";
        private bool _dueInvalid;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Text { get => _text; set => Set(ref _text, value); }

        public DateTime? Due
        {
            get => _due;
            set
            {
                if (!Set(ref _due, value)) return;
                Notified = false;
                _dueText = DateText.Format(value, true);
                Raise(nameof(DueText));
                Raise(nameof(DueHuman));
                Raise(nameof(IsOverdue));
            }
        }

        public bool Done
        {
            get => _done;
            set
            {
                if (Set(ref _done, value)) Raise(nameof(IsOverdue));
            }
        }

        public bool Notified { get; set; }

        public string NotePath { get; set; } = "";

        [JsonIgnore]
        public string DueText
        {
            get => _dueText.Length > 0 || _due == null ? _dueText : DateText.Format(_due, true);
            set
            {
                _dueText = value ?? "";
                if (DateText.TryParse(_dueText, true, out var d))
                {
                    DueInvalid = false;
                    if (_due != d)
                    {
                        _due = d;
                        Notified = false;
                        Raise(nameof(Due));
                        Raise(nameof(DueHuman));
                        Raise(nameof(IsOverdue));
                    }
                }
                else
                {
                    DueInvalid = true;
                }
                Raise(nameof(DueText));
            }
        }

        [JsonIgnore]
        public bool DueInvalid { get => _dueInvalid; private set => Set(ref _dueInvalid, value); }

        [JsonIgnore]
        public string DueHuman => _due == null ? "без срока" : DateText.Human(_due.Value);

        [JsonIgnore]
        public bool IsOverdue => !_done && _due != null && _due.Value <= DateTime.Now;

        public void RefreshTime() => Raise(nameof(IsOverdue));
    }

    public class ClientProject : Observable
    {
        public static readonly string[] Statuses = { "Новый", "В работе", "Ждём клиента", "Готово" };

        private string _name = "";
        private string _status = "Новый";
        private DateTime? _deadline;
        private string _deadlineText = "";
        private bool _deadlineInvalid;
        private string _comment = "";

        public string Name { get => _name; set => Set(ref _name, value); }
        public string Status { get => _status; set => Set(ref _status, value); }
        public string Comment { get => _comment; set => Set(ref _comment, value); }

        public DateTime? Deadline
        {
            get => _deadline;
            set
            {
                if (!Set(ref _deadline, value)) return;
                _deadlineText = DateText.Format(value, false);
                Raise(nameof(DeadlineText));
            }
        }

        [JsonIgnore]
        public string DeadlineText
        {
            get => _deadlineText.Length > 0 || _deadline == null ? _deadlineText : DateText.Format(_deadline, false);
            set
            {
                _deadlineText = value ?? "";
                if (DateText.TryParse(_deadlineText, false, out var d))
                {
                    DeadlineInvalid = false;
                    if (_deadline != d)
                    {
                        _deadline = d;
                        Raise(nameof(Deadline));
                    }
                }
                else
                {
                    DeadlineInvalid = true;
                }
                Raise(nameof(DeadlineText));
            }
        }

        [JsonIgnore]
        public bool DeadlineInvalid { get => _deadlineInvalid; private set => Set(ref _deadlineInvalid, value); }
    }
}
