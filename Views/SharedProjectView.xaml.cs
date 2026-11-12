using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using UserControl = System.Windows.Controls.UserControl;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;

namespace YellowBlackEditor.Views
{
    public partial class SharedProjectView : UserControl
    {
        public sealed record MemberItem(string Name, string Initial, string Role, string Tip, Brush AvatarBrush);
        public sealed record TaskItem(string Title, bool Done, Thickness Indent);

        private static readonly string[] AvatarColors = { "#FFC107", "#4FC3F7", "#81C784", "#FF8A65", "#BA68C8", "#F06292", "#AED581", "#FFD54F", "#4DB6AC", "#9575CD" };

        public event Action? TakeRequested;
        public event Action? OpenMineRequested;
        public event Action? LeaveRequested;
        public event Action? UnshareRequested;

        public SharedProject? Project { get; private set; }

        public SharedProjectView()
        {
            InitializeComponent();
        }

        public void Show(SharedProject p, bool takenByMe)
        {
            Project = p;
            var me = AppSettings.UserId;
            var isOwner = p.OwnerId == me;
            var isMember = p.Members.Any(m => m.Id == me);

            ClientText.Text = string.IsNullOrWhiteSpace(p.ClientName) ? "Не указан" : p.ClientName;
            StatusText.Text = p.Status;
            StatusChip.Background = (Brush)FindResource(p.IsClosed ? "BorderBrush1" : "AccentSoftBrush");
            DeadlineText.Text = p.Deadline is { } d
                ? (d.Date < DateTime.Today && !p.IsClosed ? "срок был " : "до ") + DateText.HumanDate(d)
                : "без срока";
            DeadlineText.Foreground = p.Deadline is { } dd && dd.Date < DateTime.Today && !p.IsClosed
                ? (Brush)FindResource("DangerBrush")
                : (Brush)FindResource("TextBrush");
            ProgressText.Text = p.Progress.Length == 0 ? "нет" : p.Progress;

            var members = p.Members.OrderBy(m => m.Id == p.OwnerId ? 0 : 1).ThenBy(m => m.JoinedAt).ToList();
            if (!members.Any(m => m.Id == p.OwnerId))
                members.Insert(0, new SharedMember { Id = p.OwnerId, Name = p.OwnerName });
            MembersList.ItemsSource = members.Select(m =>
            {
                var role = m.Id == p.OwnerId ? "ведёт проект" : "участвует";
                if (m.Id == me) role += " · это вы";
                var name = string.IsNullOrWhiteSpace(m.Name) ? m.Id : m.Name;
                var tip = string.IsNullOrWhiteSpace(m.Machine) ? name : $"{name}, компьютер {m.Machine}";
                return new MemberItem(name, name.Substring(0, 1).ToUpper(), role, tip, AvatarFor(m.Id));
            }).ToList();

            UpdatedText.Text = p.UpdatedAt == default
                ? ""
                : $"Последняя правка: {p.MemberName(p.UpdatedById)}, {DateText.Human(p.UpdatedAt.ToLocalTime())}";

            TakeBtn.Visibility = takenByMe ? Visibility.Collapsed : Visibility.Visible;
            OpenMineBtn.Visibility = takenByMe ? Visibility.Visible : Visibility.Collapsed;
            LeaveBtn.Visibility = isMember && !isOwner ? Visibility.Visible : Visibility.Collapsed;
            UnshareBtn.Visibility = isOwner ? Visibility.Visible : Visibility.Collapsed;
            HintText.Text = takenByMe
                ? "Проект у вас в карточке клиента. Правьте его там: изменения увидят все участники при следующем обмене."
                : "Здесь только просмотр. Заберите проект к себе, чтобы вести его вместе со всеми.";

            CommentCard.Visibility = string.IsNullOrWhiteSpace(p.Comment) ? Visibility.Collapsed : Visibility.Visible;
            CommentText.Text = p.Comment;

            var tasks = new List<TaskItem>();
            void Walk(IEnumerable<ProjectTask> list, int depth)
            {
                foreach (var t in list)
                {
                    tasks.Add(new TaskItem(string.IsNullOrWhiteSpace(t.Title) ? "Без названия" : t.Title, t.Done, new Thickness(depth * 28, 0, 0, 10)));
                    Walk(t.Children, depth + 1);
                }
            }
            Walk(p.Tasks, 0);
            TasksList.ItemsSource = tasks;
            TasksEmpty.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void Clear()
        {
            Project = null;
            MembersList.ItemsSource = null;
            TasksList.ItemsSource = null;
        }

        private static Brush AvatarFor(string id)
        {
            var h = 0;
            foreach (var ch in id) h = unchecked(h * 31 + ch);
            var hex = AvatarColors[Math.Abs(h % AvatarColors.Length)];
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            b.Freeze();
            return b;
        }

        private void Take_Click(object sender, RoutedEventArgs e) => TakeRequested?.Invoke();
        private void OpenMine_Click(object sender, RoutedEventArgs e) => OpenMineRequested?.Invoke();
        private void Leave_Click(object sender, RoutedEventArgs e) => LeaveRequested?.Invoke();
        private void Unshare_Click(object sender, RoutedEventArgs e) => UnshareRequested?.Invoke();
    }
}
