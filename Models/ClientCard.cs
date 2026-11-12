using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace YellowBlackEditor.Models
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        [JsonIgnore]
        public virtual IEnumerable<IList> ChildLists => Array.Empty<IList>();
    }

    public interface ISecret
    {
        string Password { get; set; }
        string? ProtectedPassword { get; set; }
        bool PasswordUnreadable { get; set; }
    }

    public class ClientContact : Observable
    {
        private string _name = "";
        private string _phone = "";
        private string _role = "";

        public string Name { get => _name; set => Set(ref _name, value); }
        public string Phone { get => _phone; set => Set(ref _phone, value); }
        public string Role { get => _role; set => Set(ref _role, value); }
    }

    public class Credential : Observable, ISecret
    {
        private string _title = "";
        private string _url = "";
        private string _login = "";
        private string _password = "";
        private string _comment = "";
        private bool _editingUrl;

        public string Title { get => _title; set => Set(ref _title, value); }
        public string Login { get => _login; set => Set(ref _login, value); }
        public string Comment { get => _comment; set => Set(ref _comment, value); }

        public string Url
        {
            get => _url;
            set
            {
                if (Set(ref _url, value)) Raise(nameof(ShowLink));
            }
        }

        public string Password { get => _password; set => Set(ref _password, value); }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProtectedPassword { get; set; }

        [JsonIgnore]
        public bool PasswordUnreadable { get; set; }

        [JsonIgnore]
        public bool EditingUrl
        {
            get => _editingUrl;
            set
            {
                if (Set(ref _editingUrl, value)) Raise(nameof(ShowLink));
            }
        }

        [JsonIgnore]
        public bool ShowLink => !_editingUrl && !string.IsNullOrWhiteSpace(_url);
    }

    public class RemoteAccess : Observable, ISecret
    {
        public static readonly string[] Programs = { "RuDesktop", "AnyDesk", "Ассистент", "Helper", "Другое" };

        private string _point = "";
        private string _program = "AnyDesk";
        private string _accessId = "";
        private string _password = "";
        private string _phones = "";
        private string _comment = "";

        public string Point { get => _point; set => Set(ref _point, value); }
        public string Program { get => _program; set => Set(ref _program, value); }
        public string AccessId { get => _accessId; set => Set(ref _accessId, value); }
        public string Phones { get => _phones; set => Set(ref _phones, value); }
        public string Comment { get => _comment; set => Set(ref _comment, value); }
        public ObservableCollection<Credential> Logins { get; set; } = new();

        public string Password { get => _password; set => Set(ref _password, value); }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ProtectedPassword { get; set; }

        [JsonIgnore]
        public bool PasswordUnreadable { get; set; }

        [JsonIgnore]
        public override IEnumerable<IList> ChildLists => new IList[] { Logins };
    }

    public class ClientCard : Observable
    {
        private string _inn = "";
        private string _kpp = "";
        private string _ogrn = "";
        private string _okpo = "";
        private string _address = "";
        private string _email = "";
        private string _comment = "";

        public string Inn { get => _inn; set => Set(ref _inn, value); }
        public string Kpp { get => _kpp; set => Set(ref _kpp, value); }
        public string Ogrn { get => _ogrn; set => Set(ref _ogrn, value); }
        public string Okpo { get => _okpo; set => Set(ref _okpo, value); }
        public string Address { get => _address; set => Set(ref _address, value); }
        public string Email { get => _email; set => Set(ref _email, value); }
        public string Comment { get => _comment; set => Set(ref _comment, value); }

        public ObservableCollection<ClientContact> Contacts { get; set; } = new();
        public ObservableCollection<RemoteAccess> Accesses { get; set; } = new();
        public ObservableCollection<Credential> Credentials { get; set; } = new();
        [JsonIgnore]
        public ObservableCollection<ClientProject> Projects { get; set; } = new();
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ObservableCollection<Reminder>? Reminders { get; set; }

        public event EventHandler? Changed;

        public IEnumerable<ISecret> Secrets()
        {
            foreach (var a in Accesses)
            {
                yield return a;
                foreach (var l in a.Logins) yield return l;
            }
            foreach (var c in Credentials) yield return c;
        }

        public void Track()
        {
            PropertyChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
            Hook(Contacts);
            Hook(Accesses);
            Hook(Credentials);
            Hook(Projects);
        }

        private static readonly HashSet<string> ViewOnlyProperties = new()
        {
            nameof(Reminder.IsOverdue), nameof(Reminder.DueHuman), nameof(Reminder.DueInvalid),
            nameof(ClientProject.DeadlineInvalid), nameof(ClientProject.DeadlineHuman), nameof(ClientProject.IsExpanded),
            nameof(ClientProject.Progress), nameof(ClientProject.IsClosed),
            nameof(Credential.EditingUrl), nameof(Credential.ShowLink)
        };

        private void Hook(IList items)
        {
            foreach (var it in items.OfType<Observable>()) HookItem(it);
            if (items is INotifyCollectionChanged ncc)
                ncc.CollectionChanged += (_, e) =>
                {
                    if (e.NewItems != null)
                        foreach (var it in e.NewItems.OfType<Observable>()) HookItem(it);
                    if (e.OldItems != null)
                        foreach (var it in e.OldItems.OfType<Observable>()) it.PropertyChanged -= Item_PropertyChanged;
                    Changed?.Invoke(this, EventArgs.Empty);
                };
        }

        private void HookItem(Observable it)
        {
            it.PropertyChanged -= Item_PropertyChanged;
            it.PropertyChanged += Item_PropertyChanged;
            foreach (var list in it.ChildLists) Hook(list);
        }

        private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != null && ViewOnlyProperties.Contains(e.PropertyName)) return;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public string SearchText()
        {
            var parts = new List<string> { Inn, Kpp, Ogrn, Okpo, Address, Email, Comment };
            foreach (var c in Contacts) parts.AddRange(new[] { c.Name, c.Phone, Digits(c.Phone), c.Role });
            foreach (var a in Accesses)
            {
                parts.AddRange(new[] { a.Point, a.Program, a.AccessId, Digits(a.AccessId), a.Phones, Digits(a.Phones), a.Comment });
                foreach (var l in a.Logins) parts.AddRange(new[] { l.Title, l.Login });
            }
            foreach (var c in Credentials) parts.AddRange(new[] { c.Title, c.Url, c.Login, c.Comment });
            foreach (var p in Projects)
            {
                parts.AddRange(new[] { p.Name, p.Comment });
                parts.AddRange(p.AllTasks().Select(t => t.Title));
            }
            return string.Join("\n", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
        }

        private static string Digits(string s) => new string(s.Where(char.IsDigit).ToArray());
    }
}
