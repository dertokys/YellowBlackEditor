using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
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
            var t = (text ?? "").Trim().ToLowerInvariant();
            if (t.Length == 0) return true;

            DateTime? baseDay = null;
            if (t.StartsWith("сегодня")) { baseDay = DateTime.Today; t = t.Substring(7); }
            else if (t.StartsWith("завтра")) { baseDay = DateTime.Today.AddDays(1); t = t.Substring(6); }

            var groups = Regex.Matches(t, @"\d+").Select(m => m.Value).ToList();
            if (baseDay != null)
            {
                var time = groups.Count == 0 ? new TimeSpan(10, 0, 0) : TryParseTime(string.Join(" ", groups));
                if (time == null) return false;
                value = baseDay.Value.Add(withTime ? time.Value : TimeSpan.Zero);
                return true;
            }

            if (groups.Count == 1)
            {
                var g = groups[0];
                groups = g.Length switch
                {
                    4 => new List<string> { g[..2], g[2..] },
                    6 => new List<string> { g[..2], g[2..4], g[4..] },
                    8 => new List<string> { g[..2], g[2..4], g[4..] },
                    _ => groups
                };
            }
            if (groups.Count < 2) return false;

            int day = int.Parse(groups[0]), month = int.Parse(groups[1]);
            var year = DateTime.Today.Year;
            var explicitYear = false;
            var rest = groups.Skip(2).ToList();
            if (rest.Count == 1 || rest.Count == 3 || (rest.Count >= 1 && rest[0].Length == 4))
            {
                year = int.Parse(rest[0]);
                if (year < 100) year += 2000;
                explicitYear = true;
                rest = rest.Skip(1).ToList();
            }

            var timeOfDay = TimeSpan.Zero;
            if (withTime)
            {
                timeOfDay = new TimeSpan(10, 0, 0);
                if (rest.Count > 0)
                {
                    var parsed = TryParseTime(string.Join(" ", rest));
                    if (parsed == null) return false;
                    timeOfDay = parsed.Value;
                }
            }
            else if (rest.Count > 0) return false;

            if (month is < 1 or > 12 || year is < 2000 or > 2100 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
            var d = new DateTime(year, month, day).Add(timeOfDay);
            if (!explicitYear && d.Date < DateTime.Today)
                d = new DateTime(year + 1, month, Math.Min(day, DateTime.DaysInMonth(year + 1, month))).Add(timeOfDay);
            value = d;
            return true;
        }

        public static TimeSpan? TryParseTime(string text)
        {
            var groups = Regex.Matches(text ?? "", @"\d+").Select(m => m.Value).ToList();
            int h, m = 0;
            if (groups.Count == 1)
            {
                var g = groups[0];
                if (g.Length <= 2) h = int.Parse(g);
                else if (g.Length is 3 or 4) { h = int.Parse(g[..^2]); m = int.Parse(g[^2..]); }
                else return null;
            }
            else if (groups.Count == 2)
            {
                h = int.Parse(groups[0]);
                m = int.Parse(groups[1]);
            }
            else return null;
            if (h is < 0 or > 23 || m is < 0 or > 59) return null;
            return new TimeSpan(h, m, 0);
        }

        public static DateTime DefaultReminderTime()
        {
            var now = DateTime.Now;
            var next = now.Date.AddHours(now.Hour + 1);
            return next.Hour == 0 || next.Date > now.Date ? now.Date.AddDays(1).AddHours(10) : next;
        }

        public static string HumanDate(DateTime value)
        {
            var today = DateTime.Today;
            if (value.Date == today) return "сегодня";
            if (value.Date == today.AddDays(1)) return "завтра";
            if (value.Date == today.AddDays(-1)) return "вчера";
            return value.ToString(value.Year == today.Year ? "d MMMM" : "d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
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

        public string ClientPath { get; set; } = "";

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

        private bool _isExpanded = true;
        private bool _expandInitialized;
        private string? _sharedId;
        private string? _sharedOwnerId;
        private string? _sharedOwnerName;
        private string? _sharedMembers;
        private string? _sharedNote;

        public string? SharedId
        {
            get => _sharedId;
            set
            {
                if (!Set(ref _sharedId, value)) return;
                Raise(nameof(IsShared));
                Raise(nameof(SharedText));
            }
        }

        public string? SharedHash { get; set; }
        public string? LastSharedId { get; set; }
        public string? SharedBase { get; set; }
        public DateTime? SharedSyncedAt { get; set; }

        public string? SharedOwnerId { get => _sharedOwnerId; set { if (Set(ref _sharedOwnerId, value)) Raise(nameof(SharedText)); } }
        public string? SharedOwnerName { get => _sharedOwnerName; set { if (Set(ref _sharedOwnerName, value)) Raise(nameof(SharedText)); } }
        public string? SharedMembers { get => _sharedMembers; set { if (Set(ref _sharedMembers, value)) Raise(nameof(SharedText)); } }
        public string? SharedNote { get => _sharedNote; set { if (Set(ref _sharedNote, value)) Raise(nameof(SharedText)); } }

        [JsonIgnore]
        public bool IsShared => !string.IsNullOrEmpty(_sharedId);

        [JsonIgnore]
        public string SharedText
        {
            get
            {
                if (!IsShared) return _sharedNote ?? "";
                var text = _sharedOwnerId == YellowBlackEditor.Utils.AppSettings.UserId
                    ? "Общий проект · ведёте вы"
                    : "Общий проект · ведёт " + (_sharedOwnerName ?? "");
                if (!string.IsNullOrWhiteSpace(_sharedMembers)) text += " · участвуют: " + _sharedMembers;
                return text;
            }
        }

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get => _name; set => Set(ref _name, value); }
        public string Comment { get => _comment; set => Set(ref _comment, value); }
        public ObservableCollection<ProjectTask> Tasks { get; set; } = new();

        private int _minutes;

        [JsonIgnore]
        public int Minutes
        {
            get => _minutes;
            set
            {
                if (Set(ref _minutes, value)) Raise(nameof(SpentText));
            }
        }

        [JsonIgnore]
        public string SpentText => _minutes > 0 ? TimeText.Human(_minutes) : "Записать";

        public string Status
        {
            get => _status;
            set
            {
                if (!Set(ref _status, value)) return;
                Raise(nameof(IsClosed));
                if (IsClosed) IsExpanded = false;
            }
        }

        [JsonIgnore]
        public bool IsClosed => _status == "Готово";

        [JsonIgnore]
        public bool IsExpanded
        {
            get
            {
                if (!_expandInitialized)
                {
                    _expandInitialized = true;
                    _isExpanded = !IsClosed;
                }
                return _isExpanded;
            }
            set
            {
                _expandInitialized = true;
                Set(ref _isExpanded, value);
            }
        }

        [JsonIgnore]
        public string Progress
        {
            get
            {
                var all = AllTasks().ToList();
                return all.Count == 0 ? "" : $"{all.Count(t => t.Done)} из {all.Count}";
            }
        }

        public void RefreshProgress() => Raise(nameof(Progress));

        public IEnumerable<ProjectTask> AllTasks()
        {
            foreach (var t in Tasks)
            {
                yield return t;
                foreach (var c in t.AllChildren()) yield return c;
            }
        }

        [JsonIgnore]
        public override IEnumerable<IList> ChildLists => new IList[] { Tasks };

        public DateTime? Deadline
        {
            get => _deadline;
            set
            {
                if (!Set(ref _deadline, value)) return;
                _deadlineText = DateText.Format(value, false);
                Raise(nameof(DeadlineText));
                Raise(nameof(DeadlineHuman));
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

        [JsonIgnore]
        public string DeadlineHuman => _deadline == null ? "Без срока" : "До " + DateText.HumanDate(_deadline.Value);
    }

    public class ProjectTask : Observable
    {
        private string _title = "";
        private bool _done;

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get => _title; set => Set(ref _title, value); }
        public bool Done { get => _done; set => Set(ref _done, value); }
        public ObservableCollection<ProjectTask> Children { get; set; } = new();

        private int _minutes;

        [JsonIgnore]
        public int Minutes
        {
            get => _minutes;
            set
            {
                if (!Set(ref _minutes, value)) return;
                Raise(nameof(SpentText));
                Raise(nameof(HasTime));
            }
        }

        [JsonIgnore]
        public bool HasTime => _minutes > 0;

        [JsonIgnore]
        public string SpentText => _minutes > 0 ? TimeText.Human(_minutes) : "";

        public IEnumerable<ProjectTask> AllChildren()
        {
            foreach (var c in Children)
            {
                yield return c;
                foreach (var d in c.AllChildren()) yield return d;
            }
        }

        [JsonIgnore]
        public override IEnumerable<IList> ChildLists => new IList[] { Children };
    }
}
