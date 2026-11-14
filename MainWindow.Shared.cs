using System.IO;
using System.Windows;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private const string SharedGroupPath = "::shared";
        private readonly NodeViewModel _sharedGroup = new() { Name = "Общие проекты", FullPath = SharedGroupPath, Type = NodeType.SharedGroup, IsExpanded = true };
        private List<SharedProject> _shared = new();
        private List<SharedPerson> _people = new();
        private SharedProject? _currentShared;

        private sealed record LocalShared(NodeViewModel Client, IList<ClientProject> List, int Index, bool InMemory)
        {
            public ClientProject Project => List[Index];
        }

        private void InitShared()
        {
            ClientView.ShareProjectRequested += ShareProject;
            ClientView.OpenSharedRequested += p => { if (p.SharedId != null) SelectSharedNode(p.SharedId); };
            ClientView.ProjectRemoved += OnLocalProjectRemoved;
            SharedView.TakeRequested += TakeShared;
            SharedView.OpenMineRequested += OpenMineShared;
            SharedView.LeaveRequested += LeaveShared;
            SharedView.UnshareRequested += UnshareShared;
            SharedView.AccessRequested += ChangeAccess;
            SharedView.HideRequested += HideSharedProject;
        }

        private string PersonName(string id) =>
            _people.FirstOrDefault(p => p.Id == id)?.Name is { Length: > 0 } n ? n : id;

        private HashSet<string> TakenIds() => new(LocalSharedCopies().Select(x => x.Project.SharedId!));

        private bool IsHiddenForMe(SharedProject sp, HashSet<string> taken) =>
            sp.OwnerId != AppSettings.UserId && !taken.Contains(sp.Id) && _appSettings.HiddenShared.Contains(sp.Id);

        private bool IsShownToMe(SharedProject sp, HashSet<string> taken) =>
            sp.OwnerId == AppSettings.UserId || taken.Contains(sp.Id) || (sp.VisibleTo(AppSettings.UserId) && !_appSettings.HiddenShared.Contains(sp.Id));

        private int HiddenSharedCount()
        {
            var taken = TakenIds();
            return _shared.Count(sp => sp.VisibleTo(AppSettings.UserId) && IsHiddenForMe(sp, taken));
        }

        private void UnhideAllShared()
        {
            _appSettings.HiddenShared.Clear();
            _appSettings.Save();
            RefreshSharedGroup();
            Status("Скрытые проекты снова видны");
        }

        private bool SharingAvailable(out string exchange)
        {
            exchange = _appSettings.SyncFolder;
            if (_appSettings.SyncEnabled && !string.IsNullOrWhiteSpace(exchange)) return true;
            Status("Чтобы делиться проектами, включите обмен через общую папку в настройках");
            return false;
        }

        private static string SharedNodePath(string id) => SharedGroupPath + "|" + id;

        private void SelectSharedNode(string id)
        {
            var node = _sharedGroup.Children.FirstOrDefault(n => n.FullPath == SharedNodePath(id));
            if (node != null) SelectNode(node);
            else Status("Проект ещё не появился в общих. Нажмите «Обменяться»");
        }

        private IEnumerable<LocalShared> LocalProjects(Func<ClientProject, bool> match)
        {
            if (_currentClient != null && _currentClientNode != null)
                for (var i = 0; i < _currentClient.Projects.Count; i++)
                    if (match(_currentClient.Projects[i]))
                        yield return new LocalShared(_currentClientNode, _currentClient.Projects, i, true);
            foreach (var (client, list) in _clientProjects.ToList())
            {
                if (ReferenceEquals(client, _currentClientNode)) continue;
                for (var i = 0; i < list.Count; i++)
                    if (match(list[i]))
                        yield return new LocalShared(client, list, i, false);
            }
        }

        private IEnumerable<LocalShared> LocalSharedCopies() => LocalProjects(p => p.SharedId != null);

        private LocalShared? FindUnlinkedCopy(string id) => LocalProjects(p => p.SharedId == null && p.LastSharedId == id).FirstOrDefault();

        private static void Unlink(ClientProject p, string note)
        {
            p.LastSharedId ??= p.SharedId;
            p.SharedNote = note;
            p.SharedId = null;
            p.SharedHash = null;
            p.SharedSyncedAt = null;
        }

        private LocalShared? FindLocalCopy(string id) => LocalSharedCopies().FirstOrDefault(x => x.Project.SharedId == id);

        private void SaveLocalCopies(IEnumerable<LocalShared> touched)
        {
            foreach (var g in touched.GroupBy(x => x.Client))
            {
                var first = g.First();
                if (first.InMemory)
                {
                    _clientDirty = true;
                    RestartSaveTimer();
                    continue;
                }
                try
                {
                    ClientStore.SaveProjects(first.Client.FullPath, first.List);
                }
                catch (Exception ex)
                {
                    LogError("Не удалось сохранить проекты клиента " + first.Client.FullPath, ex);
                }
            }
        }

        private void ApplyShared(List<SharedProject> remote, string exchange)
        {
            NotifySharedChanges(remote);
            _shared = remote;
            remote = remote.GroupBy(p => p.Id).Select(g => g.OrderByDescending(p => p.UpdatedAt).First()).ToList();
            _shared = remote;
            var byId = remote.ToDictionary(p => p.Id);
            var me = AppSettings.UserId;
            var touched = new List<LocalShared>();
            var timeCache = new Dictionary<string, List<TimeEntry>>(StringComparer.OrdinalIgnoreCase);
            var merges = 0;
            var mergeConflicts = 0;

            foreach (var local in LocalSharedCopies().ToList())
            {
                var p = local.Project;
                if (!byId.TryGetValue(p.SharedId!, out var sp))
                {
                    Unlink(p, p.SharedOwnerId == me
                        ? "Вы убрали проект из общих"
                        : $"Проект убран из общих (вёл {p.SharedOwnerName}). У вас осталась своя копия");
                    touched.Add(local);
                    continue;
                }

                var localChanged = SharedProjects.Hash(p) != p.SharedHash;
                var remoteChanged = p.SharedSyncedAt != sp.UpdatedAt;
                var push = SharedProjects.EnsureMember(sp, _appSettings);
                if (!timeCache.TryGetValue(local.Client.FullPath, out var entries))
                    timeCache[local.Client.FullPath] = entries = TimeStore.Load(local.Client.FullPath);
                var myMinutes = entries.Where(e => e.ProjectId == p.Id).Sum(e => e.Minutes);
                if (sp.Members.FirstOrDefault(m => m.Id == me) is { } meMember && meMember.Minutes != myMinutes)
                {
                    meMember.Minutes = myMinutes;
                    push = true;
                }
                var seenAt = sp.UpdatedAt;
                if (localChanged && remoteChanged && JsonMerge.Parse(p.SharedBase) is { } baseNode)
                {
                    var merged = JsonMerge.Merge(baseNode, SharedProjects.Content(p), SharedProjects.Content(sp), true);
                    if (merged.Merged != null)
                    {
                        SharedProjects.ApplyContent(merged.Merged, p);
                        SharedProjects.ApplyContent(merged.Merged, sp);
                        if (merged.Conflicts.Count > 0) mergeConflicts++;
                        merges++;
                    }
                    SharedProjects.Touch(sp);
                    sp.UpdatedByName = _appSettings.DisplayName;
                    push = true;
                }
                else if (localChanged)
                {
                    SharedProjects.CopyContent(p, sp);
                    SharedProjects.Touch(sp);
                    sp.UpdatedByName = _appSettings.DisplayName;
                    push = true;
                }
                else if (remoteChanged)
                {
                    SharedProjects.CopyContent(sp, p);
                    touched.Add(local);
                }

                if (push)
                {
                    var current = SharedProjects.LoadOne(exchange, sp.Id);
                    if (current == null || current.UpdatedAt != seenAt)
                    {
                        touched.Add(local);
                        continue;
                    }
                    try
                    {
                        SharedProjects.Save(exchange, sp);
                    }
                    catch (Exception ex)
                    {
                        LogError("Не удалось обновить общий проект " + sp.Id, ex);
                        touched.Add(local);
                        continue;
                    }
                }

                var hash = SharedProjects.Hash(sp);
                if (p.SharedHash != hash || p.SharedSyncedAt != sp.UpdatedAt || remoteChanged || localChanged || p.SharedBase == null)
                    touched.Add(local);
                p.SharedHash = hash;
                p.SharedSyncedAt = sp.UpdatedAt;
                p.SharedBase = SharedProjects.Content(sp).ToJsonString();
                SharedProjects.ApplyInfo(sp, p);
            }

            SaveLocalCopies(touched.Distinct());
            if (merges > 0)
                Status(mergeConflicts > 0
                    ? "Общий проект меняли двое: правки объединены, в одном месте оставлено ваше значение"
                    : "Общий проект меняли двое: правки объединены");

            RefreshProjectsGroup();
            RefreshSharedGroup();
        }

        private void RefreshSharedGroup()
        {
            var me = AppSettings.UserId;
            var taken = TakenIds();
            var ordered = _shared
                .Where(p => IsShownToMe(p, taken))
                .OrderBy(p => p.IsClosed)
                .ThenByDescending(p => p.UpdatedAt)
                .ToList();

            string Hint(SharedProject p)
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(p.ClientName)) parts.Add(p.ClientName);
                parts.Add(p.OwnerId == me ? "ведёте вы" : "ведёт " + p.OwnerName);
                if (p.IsClosed) parts.Add("готово");
                else if (p.OwnerId != me && taken.Contains(p.Id)) parts.Add("у вас");
                else if (p.OwnerId != me && p.Invited.Contains(me) && !p.IsMember(me)) parts.Add("вас пригласили");
                if (p.OwnerId == me && !p.ForEveryone) parts.Add("видят " + p.Invited.Count + " чел.");
                return string.Join(" · ", parts);
            }

            var sameSet = _sharedGroup.Children.Select(n => n.FullPath).SequenceEqual(ordered.Select(p => SharedNodePath(p.Id)));
            if (sameSet)
            {
                for (var i = 0; i < ordered.Count; i++)
                {
                    _sharedGroup.Children[i].Name = ordered[i].DisplayName;
                    _sharedGroup.Children[i].Hint = Hint(ordered[i]);
                }
            }
            else
            {
                var selectedId = (Tree.SelectedItem as NodeViewModel)?.Type == NodeType.SharedProject ? ((NodeViewModel)Tree.SelectedItem).FullPath : null;
                _sharedGroup.Children.Clear();
                foreach (var p in ordered)
                {
                    _sharedGroup.Children.Add(new NodeViewModel
                    {
                        Name = p.DisplayName,
                        Hint = Hint(p),
                        FullPath = SharedNodePath(p.Id),
                        Type = NodeType.SharedProject,
                        Parent = _sharedGroup
                    });
                }
                if (selectedId != null && _sharedGroup.Children.FirstOrDefault(n => n.FullPath == selectedId) is { } again)
                    SelectNode(again);
            }

            var show = (ordered.Count > 0 || HiddenSharedCount() > 0) && _appSettings.SyncEnabled;
            var present = RootNodes.Contains(_sharedGroup);
            if (show && !present)
            {
                _sharedGroup.IsSelected = false;
                RootNodes.Insert(VirtualIndex(_sharedGroup), _sharedGroup);
            }
            else if (!show && present) RootNodes.Remove(_sharedGroup);

            var q = SearchBox.Text.Trim();
            if (show && q.Length > 0) RecalcVisibility(_sharedGroup, q);

            if (_currentShared != null)
            {
                var fresh = _shared.FirstOrDefault(p => p.Id == _currentShared.Id);
                if (fresh != null) ShowSharedProject(fresh);
            }
        }

        private void ShowShared(NodeViewModel node)
        {
            var id = node.FullPath.Substring(SharedGroupPath.Length + 1);
            var sp = _shared.FirstOrDefault(p => p.Id == id);
            if (sp == null)
            {
                ShowEmpty(null);
                return;
            }
            ShowSharedProject(sp);
        }

        private void ShowSharedProject(SharedProject sp)
        {
            _currentShared = sp;
            ClientView.Clear();
            ClientView.Visibility = Visibility.Collapsed;
            EditorHost.Visibility = Visibility.Collapsed;
            FormatBar.IsEnabled = false;
            RenameBtn.Visibility = Visibility.Collapsed;
            SharedView.Visibility = Visibility.Visible;
            SharedView.Show(sp, FindLocalCopy(sp.Id) != null, _people);
            CurrentKindText.Text = "Общий проект";
            CurrentFileText.Text = sp.DisplayName;
            DirtyText.Text = "";
        }

        private void HideShared()
        {
            _currentShared = null;
            SharedView.Clear();
            SharedView.Visibility = Visibility.Collapsed;
        }

        private void ShareProject(ClientProject p)
        {
            if (!SharingAvailable(out var exchange) || _currentClientNode == null || _currentClient == null) return;
            var name = _appSettings.DisplayName;
            var me = AppSettings.UserId;
            var invited = AccessWindow.Ask(this, string.IsNullOrWhiteSpace(p.Name) ? "без названия" : p.Name.Trim(), _people, null, true);
            if (invited == null) return;
            var reuseId = p.LastSharedId != null && p.SharedOwnerId == me && _shared.All(x => x.Id != p.LastSharedId) ? p.LastSharedId : null;
            var sp = new SharedProject
            {
                Id = reuseId ?? Guid.NewGuid().ToString("N"),
                Invited = invited,
                ClientName = _currentClientNode.Name,
                ClientPath = Path.GetRelativePath(ClientsRoot, _currentClientNode.FullPath),
                ClientInn = _currentClient.Inn,
                OwnerId = AppSettings.UserId,
                OwnerName = name,
                CreatedAt = DateTime.UtcNow
            };
            SharedProjects.CopyContent(p, sp);
            SharedProjects.Touch(sp);
            sp.UpdatedByName = name;
            SharedProjects.EnsureMember(sp, _appSettings);
            try
            {
                SharedProjects.Save(exchange, sp);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось поделиться проектом", ex, $"Проверьте, что папка обмена доступна: {exchange}");
                return;
            }
            p.SharedHash = SharedProjects.Hash(sp);
            p.SharedSyncedAt = sp.UpdatedAt;
            p.SharedBase = SharedProjects.Content(sp).ToJsonString();
            SharedProjects.ApplyInfo(sp, p);
            p.LastSharedId = sp.Id;
            p.SharedId = sp.Id;
            _shared.Add(sp);
            RefreshProjectsGroup();
            RefreshSharedGroup();
            Status(invited.Count == 0
                ? "Проект в общих. Коллеги увидят его в разделе «Общие проекты»"
                : "Проект виден только приглашённым: " + string.Join(", ", invited.Select(PersonName)));
        }

        private NodeViewModel? FindOrCreateClientFor(SharedProject sp)
        {
            var clients = Flatten(RootNodes.Where(n => !n.IsVirtual)).Where(n => n.Type == NodeType.Client).ToList();
            var byPath = clients.FirstOrDefault(c => sp.ClientPath.Length > 0 &&
                c.FullPath.Equals(Path.Combine(ClientsRoot, sp.ClientPath), StringComparison.OrdinalIgnoreCase));
            if (byPath != null) return byPath;
            var byName = clients.FirstOrDefault(c => c.Name.Equals(sp.ClientName, StringComparison.CurrentCultureIgnoreCase));
            if (byName != null) return byName;

            var section = RootNodes.FirstOrDefault(n => !n.IsVirtual && n.FullPath.Equals(ClientsRoot, StringComparison.OrdinalIgnoreCase));
            try
            {
                Directory.CreateDirectory(ClientsRoot);
                if (section == null)
                {
                    section = new NodeViewModel { Name = ClientsSectionName, FullPath = ClientsRoot, Type = NodeType.Section };
                    RootNodes.Add(section);
                }
                var name = SanitizeName(string.IsNullOrWhiteSpace(sp.ClientName) ? "Клиент из общего проекта" : sp.ClientName);
                var dir = Path.Combine(ClientsRoot, MakeUniqueName(ClientsRoot, name));
                Directory.CreateDirectory(dir);
                ClientStore.Save(dir, new ClientCard { Inn = sp.ClientInn });
                var node = new NodeViewModel { Name = Path.GetFileName(dir), FullPath = dir, Type = NodeType.Client, Parent = section };
                section.Children.Insert(InsertIndex(section, NodeType.Client, node.Name), node);
                return node;
            }
            catch (Exception ex)
            {
                ShowError("Не удалось создать клиента для проекта", ex);
                return null;
            }
        }

        private void TakeShared()
        {
            if (_currentShared is not { } sp || !SharingAvailable(out var exchange)) return;
            if (FindLocalCopy(sp.Id) != null)
            {
                OpenMineShared();
                return;
            }
            if (FindUnlinkedCopy(sp.Id) is { } old)
            {
                RelinkCopy(sp, old, exchange);
                return;
            }
            var client = FindOrCreateClientFor(sp);
            if (client == null) return;

            SharedProjects.EnsureMember(sp, _appSettings);
            try
            {
                SharedProjects.Save(exchange, sp);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось забрать проект", ex, $"Проверьте, что папка обмена доступна: {exchange}");
                return;
            }

            var p = new ClientProject();
            SharedProjects.CopyContent(sp, p);
            p.SharedHash = SharedProjects.Hash(sp);
            p.SharedSyncedAt = sp.UpdatedAt;
            p.SharedBase = SharedProjects.Content(sp).ToJsonString();
            SharedProjects.ApplyInfo(sp, p);
            p.LastSharedId = sp.Id;
            p.SharedId = sp.Id;

            int index;
            if (ReferenceEquals(client, _currentClientNode) && _currentClient != null)
            {
                _currentClient.Projects.Add(p);
                index = _currentClient.Projects.Count - 1;
            }
            else
            {
                try
                {
                    var list = ClientStore.ReadProjects(client.FullPath);
                    list.Add(p);
                    ClientStore.SaveProjects(client.FullPath, list);
                    _clientProjects[client] = list;
                    index = list.Count - 1;
                }
                catch (Exception ex)
                {
                    ShowError("Не удалось сохранить проект у клиента", ex);
                    return;
                }
            }
            RefreshProjectsGroup();
            RefreshSharedGroup();
            Status($"Проект у вас в карточке «{client.Name}». Правки увидят все участники");
            GoToProject(client, index);
        }

        private void RelinkCopy(SharedProject sp, LocalShared old, string exchange)
        {
            SharedProjects.EnsureMember(sp, _appSettings);
            try
            {
                SharedProjects.Save(exchange, sp);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось забрать проект", ex, $"Проверьте, что папка обмена доступна: {exchange}");
                return;
            }
            var p = old.Project;
            var kept = false;
            if (SharedProjects.Hash(p) != SharedProjects.Hash(sp))
            {
                old.List.Insert(old.Index + 1, SharedProjects.CloneAsSeparate(p, " (ваша прежняя версия)"));
                kept = true;
            }
            SharedProjects.CopyContent(sp, p);
            p.SharedHash = SharedProjects.Hash(sp);
            p.SharedSyncedAt = sp.UpdatedAt;
            p.SharedBase = SharedProjects.Content(sp).ToJsonString();
            SharedProjects.ApplyInfo(sp, p);
            p.LastSharedId = sp.Id;
            p.SharedId = sp.Id;
            SaveLocalCopies(new[] { old });
            if (!old.InMemory) _clientProjects[old.Client] = old.List.ToList();
            RefreshProjectsGroup();
            RefreshSharedGroup();
            Status(kept
                ? "Проект снова общий. Вашу прежнюю версию сохранил рядом отдельным проектом"
                : "Проект снова общий, ваша копия привязана обратно");
            GoToProject(old.Client, old.Index);
        }

        private void ChangeAccess()
        {
            if (_currentShared is not { } sp || !SharingAvailable(out var exchange) || sp.OwnerId != AppSettings.UserId) return;
            var invited = AccessWindow.Ask(this, sp.DisplayName, _people, sp.Invited, false);
            if (invited == null) return;
            sp.Invited = invited;
            try
            {
                SharedProjects.Save(exchange, sp);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось изменить доступ", ex, $"Проверьте, что папка обмена доступна: {exchange}");
                return;
            }
            RefreshSharedGroup();
            Status(invited.Count == 0 ? "Проект виден всем коллегам" : "Проект виден только: " + string.Join(", ", invited.Select(PersonName)));
        }

        private void HideSharedProject()
        {
            if (_currentShared is not { } sp) return;
            var me = AppSettings.UserId;
            var invitedMe = sp.Invited.Contains(me);
            if (!ThemedMessageBox.Confirm(this,
                    invitedMe ? $"Отказаться от «{sp.DisplayName}»?" : $"Скрыть «{sp.DisplayName}»?",
                    invitedMe
                        ? $"{sp.OwnerName} увидит, что вы отказались. Проект пропадёт из вашего списка."
                        : "Проект пропадёт из вашего списка общих. Вернуть его можно в разделе «Общие проекты».",
                    invitedMe ? "Отказаться" : "Скрыть"))
                return;
            if (invitedMe && SharingAvailable(out var exchange))
            {
                sp.Invited.Remove(me);
                if (sp.Invited.Count == 0) sp.Invited.Add(sp.OwnerId);
                try { SharedProjects.Save(exchange, sp); }
                catch (Exception ex) { LogError("Не удалось отказаться от проекта " + sp.Id, ex); }
            }
            if (!_appSettings.HiddenShared.Contains(sp.Id)) _appSettings.HiddenShared.Add(sp.Id);
            _appSettings.Save();
            HideShared();
            RefreshSharedGroup();
            if (RootNodes.Contains(_sharedGroup)) SelectNode(_sharedGroup);
            else ShowEmpty(null);
            Status(invitedMe ? "Вы отказались от проекта" : "Проект скрыт");
        }

        private void GoToProject(NodeViewModel client, int index)
        {
            SelectNode(client);
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(_currentClientNode, client)) ClientView.ShowProject(index);
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        private void OpenMineShared()
        {
            if (_currentShared is not { } sp || FindLocalCopy(sp.Id) is not { } local) return;
            GoToProject(local.Client, local.Index);
        }

        private void LeaveShared()
        {
            if (_currentShared is not { } sp || !SharingAvailable(out var exchange)) return;
            if (!ThemedMessageBox.Confirm(this, "Перестать участвовать в проекте?",
                    "Вас уберут из участников. Ваша копия останется в карточке клиента, но перестанет обновляться.", "Перестать"))
                return;
            sp.Members.RemoveAll(m => m.Id == AppSettings.UserId);
            try
            {
                SharedProjects.Save(exchange, sp);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось выйти из проекта", ex, $"Проверьте, что папка обмена доступна: {exchange}");
                return;
            }
            if (FindLocalCopy(sp.Id) is { } local)
            {
                Unlink(local.Project, "Вы перестали участвовать. Это ваша копия, она больше не обновляется");
                SaveLocalCopies(new[] { local });
            }
            RefreshProjectsGroup();
            RefreshSharedGroup();
            Status("Вы больше не участвуете в проекте");
        }

        private void UnshareShared()
        {
            if (_currentShared is not { } sp || !SharingAvailable(out var exchange)) return;
            if (!ThemedMessageBox.Confirm(this, $"Убрать «{sp.DisplayName}» из общих?",
                    "Проект пропадёт из общих у всех. Копии, которые коллеги уже забрали к себе, останутся у них.", "Убрать",
                    image: MessageBoxImage.Warning))
                return;
            try
            {
                SharedProjects.Remove(exchange, sp.Id);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось убрать проект из общих", ex, $"Проверьте, что папка обмена доступна: {exchange}");
                return;
            }
            if (FindLocalCopy(sp.Id) is { } local)
            {
                Unlink(local.Project, "Вы убрали проект из общих. Если поделитесь снова, у коллег привяжутся их прежние копии");
                SaveLocalCopies(new[] { local });
            }
            _shared.RemoveAll(p => p.Id == sp.Id);
            HideShared();
            RefreshProjectsGroup();
            RefreshSharedGroup();
            if (RootNodes.Contains(_sharedGroup)) SelectNode(_sharedGroup);
            else ShowEmpty(null);
            Status("Проект убран из общих");
        }

        private void OnLocalProjectRemoved(ClientProject p)
        {
            if (p.SharedId is not { } id || !_appSettings.SyncEnabled) return;
            var sp = _shared.FirstOrDefault(x => x.Id == id);
            if (sp == null || sp.OwnerId == AppSettings.UserId) return;
            sp.Members.RemoveAll(m => m.Id == AppSettings.UserId);
            var exchange = _appSettings.SyncFolder;
            Task.Run(() =>
            {
                try { SharedProjects.Save(exchange, sp); }
                catch (Exception ex) { LogError("Не удалось выйти из общего проекта " + id, ex); }
            });
            RefreshSharedGroup();
        }
    }
}
