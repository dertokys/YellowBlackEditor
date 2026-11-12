using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;
using WF = System.Windows.Forms;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private const string TodayPath = "::today";
        private readonly NodeViewModel _todayNode = new() { Name = "Сегодня", FullPath = TodayPath, Type = NodeType.Today };
        private readonly Dictionary<NodeViewModel, List<ClientSoftware>> _clientSoftware = new();
        private readonly Dictionary<string, DateTime> _sharedSeen = new();
        private bool _sharedSeenReady;
        private string? _balloonSharedId;
        private bool _todayShown;

        public static readonly RoutedUICommand TimeCmd = new("Учёт времени", "Time", typeof(MainWindow));

        private void InitToday()
        {
            InputBindings.Add(new KeyBinding(TimeCmd, Key.T, ModifierKeys.Control));
            CommandBindings.Add(new CommandBinding(TimeCmd, (_, _) => OpenTimeWindow()));
            TodayPanel.ReportRequested += OpenTimeReport;
            ClientView.AddTimeRequested += () =>
            {
                if (_currentClientNode != null) OpenTimeWindow(_currentClientNode.FullPath, $"Клиент «{_currentClientNode.Name}», без заметки");
            };
            ClientView.DeleteTimeRequested += DeleteTimeRow;
            ClientView.ProjectTimeRequested += OpenProjectTime;
        }

        private int VirtualIndex(NodeViewModel group)
        {
            var order = new[] { _todayNode, _projectsGroup, _sharedGroup };
            var i = 0;
            foreach (var g in order)
            {
                if (ReferenceEquals(g, group)) return i;
                if (RootNodes.Contains(g)) i++;
            }
            return i;
        }

        private void EnsureTodayNode()
        {
            if (RootNodes.Contains(_todayNode) || !Directory.Exists(_workspacePath)) return;
            _todayNode.IsSelected = false;
            RootNodes.Insert(0, _todayNode);
        }

        private void LoadClientSoftware()
        {
            _clientSoftware.Clear();
            foreach (var client in Flatten(RootNodes.Where(n => !n.IsVirtual)).Where(n => n.Type == NodeType.Client).ToList())
            {
                var list = ClientStore.ReadSoftware(client.FullPath);
                if (list.Count > 0) _clientSoftware[client] = list;
            }
            SoftwareCatalog.Rebuild(_clientSoftware.Values.SelectMany(v => v));
        }

        private void UpdateClientSoftware(NodeViewModel client, IEnumerable<ClientSoftware> software)
        {
            var list = software.ToList();
            if (list.Count > 0) _clientSoftware[client] = list;
            else _clientSoftware.Remove(client);
            SoftwareCatalog.Add(list);
        }

        private void ShowToday()
        {
            ClientView.Clear();
            ClientView.Visibility = Visibility.Collapsed;
            EditorHost.Visibility = Visibility.Collapsed;
            FormatBar.IsEnabled = false;
            RenameBtn.Visibility = Visibility.Collapsed;
            TodayPanel.Visibility = Visibility.Visible;
            HeaderBar.Visibility = Visibility.Collapsed;
            System.Windows.Controls.Grid.SetRow(TodayPanel, 0);
            System.Windows.Controls.Grid.SetRowSpan(TodayPanel, 2);
            _todayShown = true;
            CurrentKindText.Text = "";
            CurrentFileText.Text = "";
            DirtyText.Text = "";
            RefreshToday();
        }

        private void HideToday()
        {
            _todayShown = false;
            TodayPanel.Visibility = Visibility.Collapsed;
            HeaderBar.Visibility = Visibility.Visible;
        }

        private void RefreshTodayIfShown()
        {
            if (_todayShown) RefreshToday();
        }

        private void RefreshToday()
        {
            var d = new TodayView.Data();
            var now = DateTime.Now;
            var today = DateTime.Today;

            foreach (var e in CollectReminders()
                         .Where(e => !e.Item.Done && e.Item.Due != null && e.Item.Due.Value.Date <= today)
                         .OrderBy(e => e.Item.Due))
            {
                var due = e.Item.Due!.Value;
                var where = e.Client?.Name ?? "";
                if (e.Note != null) where = where.Length > 0 ? where + " · " + e.Note.Name : e.Note.Name;
                var target = e.Note ?? e.Client;
                d.Reminders.Add(new TodayView.Item(
                    string.IsNullOrWhiteSpace(e.Item.Text) ? "Без текста" : e.Item.Text,
                    where.Length > 0 ? where : "Общее напоминание",
                    due.Date < today ? DateText.Human(due) : due.ToString("HH:mm"),
                    due <= now ? 2 : 1,
                    target != null ? () => SelectNode(target) : () => ShowReminders_Click(this, new RoutedEventArgs())));
            }

            var limit = today.AddDays(7);
            var projects = new List<(NodeViewModel Client, ClientProject P, int Index)>();
            foreach (var (client, list) in _clientProjects.ToList())
            {
                IList<ClientProject> src = ReferenceEquals(client, _currentClientNode) && _currentClient != null ? _currentClient.Projects : list;
                for (var i = 0; i < src.Count; i++) projects.Add((client, src[i], i));
            }
            foreach (var x in projects.Where(x => !x.P.IsClosed && x.P.Deadline != null && x.P.Deadline.Value.Date <= limit)
                         .OrderBy(x => x.P.Deadline))
            {
                var dl = x.P.Deadline!.Value.Date;
                var right = dl < today ? "срок был " + DateText.HumanDate(dl) : "до " + DateText.HumanDate(dl);
                var client = x.Client;
                var index = x.Index;
                d.Projects.Add(new TodayView.Item(
                    string.IsNullOrWhiteSpace(x.P.Name) ? "Проект без названия" : x.P.Name,
                    client.Name + " · " + x.P.Status.ToLower() + (x.P.IsShared ? " · общий" : ""),
                    right,
                    dl < today ? 2 : dl <= today.AddDays(2) ? 1 : 0,
                    () => GoToProject(client, index)));
            }

            var subs = new List<(NodeViewModel Client, ClientSoftware S, SoftwareAddon A)>();
            foreach (var (client, list) in _clientSoftware.ToList())
            {
                IEnumerable<ClientSoftware> src = ReferenceEquals(client, _currentClientNode) && _currentClient != null ? _currentClient.Software : list;
                foreach (var s in src)
                    foreach (var a in s.Addons)
                        if (a.DaysLeft is <= 30) subs.Add((client, s, a));
            }
            foreach (var x in subs.OrderBy(x => x.A.Ends))
            {
                var client = x.Client;
                var title = string.IsNullOrWhiteSpace(x.A.Name) ? "Подписка без названия" : x.A.Name;
                var sub = client.Name + (string.IsNullOrWhiteSpace(x.S.Name) ? "" : " · " + x.S.Name);
                d.Subscriptions.Add(new TodayView.Item(title, sub, x.A.StateText, x.A.State == 3 ? 2 : x.A.DaysLeft <= 7 ? 1 : 0,
                    () => GoToSoftware(client)));
            }

            var rows = TimeStore.ScanAll(_workspacePath);
            var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            d.MinutesToday = rows.Where(r => r.Entry.Date == today).Sum(r => r.Entry.Minutes);
            d.MinutesWeek = rows.Where(r => r.Entry.Date >= weekStart && r.Entry.Date <= today).Sum(r => r.Entry.Minutes);
            foreach (var r in rows.Where(r => r.Entry.Date == today).OrderByDescending(r => r.Entry.CreatedAt))
            {
                var folder = r.Folder;
                var named = r.IsProject || r.Note.Length > 0;
                var title = named ? r.Title : r.Client.Length > 0 ? r.Client : "Без заметки";
                var sub = (named && r.Client.Length > 0 ? r.Client : "По клиенту") +
                          (string.IsNullOrWhiteSpace(r.Entry.Comment) ? "" : " · " + r.Entry.Comment);
                d.Time.Add(new TodayView.Item(title, sub, TimeText.Human(r.Entry.Minutes), 0, () => OpenFolderNode(folder)));
            }

            TodayPanel.Show(d);
        }

        private void OpenFolderNode(string folder)
        {
            var node = Flatten(RootNodes.Where(n => !n.IsVirtual))
                .FirstOrDefault(n => n.Type is NodeType.Document or NodeType.Client && n.FullPath.Equals(folder, StringComparison.OrdinalIgnoreCase));
            if (node != null) SelectNode(node);
        }

        private void GoToSoftware(NodeViewModel client)
        {
            SelectNode(client);
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(_currentClientNode, client)) ClientView.ShowSoftware();
            }, DispatcherPriority.Background);
        }

        private (string Folder, string Context)? TimeTarget()
        {
            if (_currentDocNode != null)
            {
                var (client, _, _) = TimeStore.Describe(_workspacePath, _currentDocNode.FullPath);
                return (_currentDocNode.FullPath, client.Length > 0 ? $"{client} · {_currentDocNode.Name}" : _currentDocNode.Name);
            }
            if (_currentClientNode != null)
                return (_currentClientNode.FullPath, $"Клиент «{_currentClientNode.Name}», без заметки");
            return null;
        }

        private void OpenTimeWindow()
        {
            if (TimeTarget() is not { } t)
            {
                Status("Откройте заметку или клиента, чтобы записать время");
                return;
            }
            OpenTimeWindow(t.Folder, t.Context);
        }

        private void OpenTimeWindow(string folder, string context)
        {
            SaveAll(quiet: true);
            if (!TimeEntryWindow.Show(this, folder, context)) return;
            AfterTimeChanged();
            Status("Время записано");
        }

        private void AfterTimeChanged()
        {
            UpdateTimeButton();
            if (_currentClientNode != null) ClientView.ShowTime(TimeStore.ScanFolder(_workspacePath, _currentClientNode.FullPath));
            RefreshTodayIfShown();
        }

        private void OpenProjectTime(ClientProject p, ProjectTask? t)
        {
            if (_currentClientNode == null || _currentClient == null) return;
            SaveAll(quiet: true);
            try
            {
                ClientStore.Save(_currentClientNode.FullPath, _currentClient);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось сохранить карточку клиента", ex);
                return;
            }
            var projectName = string.IsNullOrWhiteSpace(p.Name) ? "без названия" : p.Name.Trim();
            var context = $"{_currentClientNode.Name} · проект «{projectName}»" +
                          (t == null ? ", весь проект" : $" · этап «{(string.IsNullOrWhiteSpace(t.Title) ? "без названия" : t.Title.Trim())}»");
            var taskIds = t == null ? null : new HashSet<string>(new[] { t }.Concat(t.AllChildren()).Select(x => x.Id));
            Func<TimeEntry, bool> filter = t == null
                ? e => e.ProjectId == p.Id
                : e => e.ProjectId == p.Id && e.TaskId != null && taskIds!.Contains(e.TaskId);
            void Stamp(TimeEntry e)
            {
                e.ProjectId = p.Id;
                e.ProjectName = projectName;
                e.TaskId = t?.Id;
                e.TaskTitle = t == null ? null : (string.IsNullOrWhiteSpace(t.Title) ? "без названия" : t.Title.Trim());
            }
            if (!TimeEntryWindow.Show(this, _currentClientNode.FullPath, context, filter, Stamp)) return;
            AfterTimeChanged();
            Status("Время записано на " + (t == null ? "проект" : "этап"));
            if (p.SharedId != null) SyncSoon();
        }

        private void DeleteTimeRow(TimeRow row)
        {
            try
            {
                var list = TimeStore.Load(row.Folder);
                list.RemoveAll(e => e.Id == row.Entry.Id);
                TimeStore.Save(row.Folder, list);
                AfterTimeChanged();
                Status("Запись времени удалена");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось удалить запись времени", ex);
            }
        }

        private void UpdateTimeButton()
        {
            string? folder = _currentDocNode?.FullPath ?? _currentClientNode?.FullPath;
            if (folder == null)
            {
                NoteTimeBtn.Visibility = Visibility.Collapsed;
                return;
            }
            NoteTimeBtn.Visibility = Visibility.Visible;
            var minutes = _currentDocNode != null
                ? TimeStore.Total(folder)
                : TimeStore.ScanFolder(_workspacePath, folder).Sum(r => r.Entry.Minutes);
            NoteTimeText.Text = minutes > 0 ? TimeText.Human(minutes) : "Время";
            NoteTimeBtn.ToolTip = minutes > 0
                ? $"Потрачено {TimeText.Human(minutes)}. Нажмите, чтобы добавить (Ctrl+T)"
                : "Записать, сколько времени ушло (Ctrl+T)";
        }

        private void NoteTime_Click(object sender, RoutedEventArgs e) => OpenTimeWindow();

        private void OpenTimeReport()
        {
            SaveAll(quiet: true);
            var w = new TimeReportWindow(TimeStore.ScanAll(_workspacePath)) { Owner = this };
            w.Show();
        }

        private void TimeReport_Click(object sender, RoutedEventArgs e) => OpenTimeReport();

        private void NotifySharedChanges(List<SharedProject> remote)
        {
            var me = AppSettings.UserId;
            var taken = new HashSet<string>(LocalSharedCopies().Select(x => x.Project.SharedId!));
            var messages = new List<(string Id, string Text)>();
            var hiddenChanged = false;
            foreach (var sp in remote)
            {
                var invitedNow = sp.Invited.Contains(me) && sp.OwnerId != me && !sp.IsMember(me);
                var client = sp.ClientName.Length > 0 ? $" ({sp.ClientName})" : "";
                if (invitedNow && !_appSettings.SeenInvites.Contains(sp.Id))
                {
                    _appSettings.SeenInvites.Add(sp.Id);
                    _appSettings.HiddenShared.Remove(sp.Id);
                    hiddenChanged = true;
                    messages.Add((sp.Id, $"{sp.OwnerName} приглашает вас в проект «{sp.DisplayName}»{client}"));
                }
                else if (!sp.Invited.Contains(me) && _appSettings.SeenInvites.Remove(sp.Id))
                {
                    hiddenChanged = true;
                }
                else if (_sharedSeenReady)
                {
                    var who = string.IsNullOrWhiteSpace(sp.UpdatedByName) ? sp.MemberName(sp.UpdatedById) : sp.UpdatedByName;
                    if (!_sharedSeen.TryGetValue(sp.Id, out var seen))
                    {
                        if (sp.OwnerId != me && sp.ForEveryone && !_appSettings.HiddenShared.Contains(sp.Id))
                            messages.Add((sp.Id, $"{sp.OwnerName} поделился проектом «{sp.DisplayName}»{client}"));
                    }
                    else if (seen != sp.UpdatedAt && sp.UpdatedById != me && (taken.Contains(sp.Id) || sp.OwnerId == me))
                    {
                        messages.Add((sp.Id, $"{who} изменил «{sp.DisplayName}»"));
                    }
                }
                _sharedSeen[sp.Id] = sp.UpdatedAt;
            }
            _sharedSeenReady = true;
            if (hiddenChanged) _appSettings.Save();
            if (messages.Count == 0) return;

            var title = messages.Count == 1 ? "Общие проекты" : $"Общие проекты: изменений {messages.Count}";
            var text = string.Join("\n", messages.Take(4).Select(m => m.Text));
            Status(messages[0].Text);
            try
            {
                _lastBalloonIsReminder = false;
                _balloonSharedId = messages[0].Id;
                _balloonTransfer = false;
                _tray?.ShowBalloonTip(8000, title, text, WF.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                LogError("Не удалось показать уведомление", ex);
            }
        }

        private void OpenBalloonShared()
        {
            var id = _balloonSharedId;
            _balloonSharedId = null;
            BringToFront();
            if (id != null) SelectSharedNode(id);
        }
    }
}
