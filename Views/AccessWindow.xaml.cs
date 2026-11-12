using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;

namespace YellowBlackEditor.Views
{
    public partial class AccessWindow : Window
    {
        public sealed class PersonItem : INotifyPropertyChanged
        {
            private bool _selected;
            public string Id { get; init; } = "";
            public string Name { get; init; } = "";
            public string Sub { get; init; } = "";
            public bool Selected
            {
                get => _selected;
                set
                {
                    _selected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
                }
            }
            public event PropertyChangedEventHandler? PropertyChanged;
        }

        private readonly List<PersonItem> _people;
        private bool _pick;

        public List<string>? Result { get; private set; }

        private AccessWindow(string projectName, IEnumerable<SharedPerson> people, IEnumerable<string>? invited, bool isNew)
        {
            InitializeComponent();
            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            var current = new HashSet<string>(invited ?? Enumerable.Empty<string>());
            ProjectText.Text = "Проект «" + projectName + "»";
            TitleText.Text = isNew ? "Кому показать проект?" : "Кому видно проект";
            OkBtn.Content = isNew ? "Поделиться" : "Сохранить";
            _people = people
                .Where(p => p.Id != AppSettings.UserId)
                .GroupBy(p => p.Id).Select(g => g.OrderByDescending(x => x.LastSeen).First())
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new PersonItem
                {
                    Id = p.Id,
                    Name = string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name,
                    Sub = "компьютер " + p.Machine + (p.LastSeen == default ? "" : ", был в программе " + DateText.Human(p.LastSeen.ToLocalTime())),
                    Selected = current.Contains(p.Id)
                })
                .ToList();
            foreach (var id in current.Where(id => _people.All(p => p.Id != id)))
                _people.Add(new PersonItem { Id = id, Name = id, Sub = "давно не заходил", Selected = true });
            PeopleList.ItemsSource = _people;
            NobodyText.Visibility = _people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SetPick(current.Count > 0);
        }

        public static List<string>? Ask(Window owner, string projectName, IEnumerable<SharedPerson> people, IEnumerable<string>? invited, bool isNew)
        {
            var w = new AccessWindow(projectName, people, invited, isNew) { Owner = owner };
            return w.ShowDialog() == true ? w.Result : null;
        }

        private void SetPick(bool pick)
        {
            _pick = pick;
            AllBtn.IsChecked = !pick;
            PickBtn.IsChecked = pick;
            AllHint.Visibility = pick ? Visibility.Collapsed : Visibility.Visible;
            PickPanel.Visibility = pick ? Visibility.Visible : Visibility.Collapsed;
            ErrorText.Text = "";
        }

        private void All_Click(object sender, RoutedEventArgs e) => SetPick(false);
        private void Pick_Click(object sender, RoutedEventArgs e) => SetPick(true);

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!_pick)
            {
                Result = new List<string>();
                DialogResult = true;
                return;
            }
            var chosen = _people.Where(p => p.Selected).Select(p => p.Id).ToList();
            if (chosen.Count == 0)
            {
                ErrorText.Text = "Отметьте хотя бы одного человека";
                return;
            }
            Result = chosen;
            DialogResult = true;
        }
    }
}
