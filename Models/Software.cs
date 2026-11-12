using System.Collections;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace YellowBlackEditor.Models
{
    public class ClientSoftware : Observable
    {
        private string _name = "";
        private string _note = "";

        public string Name { get => _name; set => Set(ref _name, value); }
        public string Note { get => _note; set => Set(ref _note, value); }
        public ObservableCollection<SoftwareAddon> Addons { get; set; } = new();

        [JsonIgnore]
        public override IEnumerable<IList> ChildLists => new IList[] { Addons };
    }

    public class SoftwareAddon : Observable
    {
        public static readonly string[] Terms = { "Без срока", "1 месяц", "3 месяца", "6 месяцев", "1 год", "2 года", "3 года", "Своя дата" };
        private static readonly int?[] TermMonthsList = { null, 1, 3, 6, 12, 24, 36, null };

        private string _name = "";
        private DateTime? _activated;
        private DateTime? _ends;
        private int? _termMonths;

        public string Name { get => _name; set => Set(ref _name, value); }

        public DateTime? Activated
        {
            get => _activated;
            set
            {
                if (!Set(ref _activated, value)) return;
                if (_termMonths is { } m && value != null) Ends = value.Value.Date.AddMonths(m);
                RaiseView();
            }
        }

        public DateTime? Ends
        {
            get => _ends;
            set
            {
                if (!Set(ref _ends, value)) return;
                RaiseView();
            }
        }

        public int? TermMonths
        {
            get => _termMonths;
            set
            {
                if (!Set(ref _termMonths, value)) return;
                if (value is { } m) Ends = (_activated ?? DateTime.Today).Date.AddMonths(m);
                RaiseView();
            }
        }

        [JsonIgnore]
        public string Term
        {
            get
            {
                if (_termMonths is { } m)
                {
                    var i = Array.IndexOf(TermMonthsList, (int?)m);
                    if (i > 0) return Terms[i];
                }
                return _ends == null ? Terms[0] : Terms[^1];
            }
            set
            {
                var i = Array.IndexOf(Terms, value);
                if (i < 0) return;
                if (i == 0)
                {
                    TermMonths = null;
                    Ends = null;
                }
                else if (i == Terms.Length - 1)
                {
                    TermMonths = null;
                    Ends ??= (_activated ?? DateTime.Today).Date.AddYears(1);
                }
                else
                {
                    TermMonths = TermMonthsList[i];
                }
                RaiseView();
            }
        }

        public void SetEndsManually(DateTime? date)
        {
            if (_termMonths is { } m && date != null && _activated != null && _activated.Value.Date.AddMonths(m) == date.Value.Date)
            {
                Ends = date;
                return;
            }
            TermMonths = null;
            Ends = date;
            RaiseView();
        }

        [JsonIgnore]
        public string ActivatedText => _activated == null ? "Дата активации" : "с " + _activated.Value.ToString("dd.MM.yyyy");

        [JsonIgnore]
        public string EndsText => _ends == null ? "Без окончания" : "до " + _ends.Value.ToString("dd.MM.yyyy");

        [JsonIgnore]
        public int? DaysLeft => _ends == null ? null : (_ends.Value.Date - DateTime.Today).Days;

        [JsonIgnore]
        public int State => DaysLeft switch
        {
            null => 0,
            < 0 => 3,
            <= 30 => 2,
            _ => 1
        };

        [JsonIgnore]
        public string StateText => DaysLeft switch
        {
            null => "",
            < 0 and var d => "истекло " + Ago(-d),
            0 => "истекает сегодня",
            1 => "истекает завтра",
            <= 60 and var d => $"осталось {d} дн.",
            _ => "действует"
        };

        private static string Ago(int days) => days switch
        {
            1 => "вчера",
            < 31 => $"{days} дн. назад",
            _ => $"{days / 30} мес. назад"
        };

        public void RefreshState() => RaiseView();

        private void RaiseView()
        {
            Raise(nameof(Term));
            Raise(nameof(ActivatedText));
            Raise(nameof(EndsText));
            Raise(nameof(DaysLeft));
            Raise(nameof(State));
            Raise(nameof(StateText));
        }
    }
}
