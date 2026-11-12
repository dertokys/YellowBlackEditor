using System.IO;
using System.Windows;
using System.Windows.Threading;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private DispatcherTimer? _syncTimer;
        private DispatcherTimer? _syncSoonTimer;
        private bool _syncRunning;
        private bool _rebindOnly;

        private string ClientsRoot => Path.Combine(_workspacePath, ClientsSectionName);

        private void InitSync()
        {
            _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _syncTimer.Tick += (_, _) => RunSync();
            _syncSoonTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _syncSoonTimer.Tick += (_, _) =>
            {
                _syncSoonTimer.Stop();
                RunSync();
            };
            MigrateClientCards();
            ApplySyncSettings();
        }

        private void MigrateClientCards()
        {
            var moved = false;
            foreach (var client in Flatten(RootNodes).Where(n => n.Type == NodeType.Client).ToList())
            {
                try
                {
                    if (!ClientStore.NeedsMigration(client.FullPath)) continue;
                    var card = ClientStore.Load(client.FullPath);
                    if (card.Reminders is { Count: > 0 })
                    {
                        var rel = RelativePath(client);
                        foreach (var r in card.Reminders)
                        {
                            r.ClientPath = rel;
                            _generalReminders.Add(r);
                        }
                        moved = true;
                    }
                    card.Reminders = null;
                    ClientStore.Save(client.FullPath, card);
                }
                catch (Exception ex)
                {
                    LogError($"Не удалось обновить карточку {client.FullPath}", ex);
                }
            }
            if (moved) SaveGeneralReminders();
        }

        private void ApplySyncSettings()
        {
            var enabled = _appSettings.SyncEnabled && !string.IsNullOrWhiteSpace(_appSettings.SyncFolder);
            SyncBtn.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
            if (enabled)
            {
                _syncTimer?.Start();
                SyncSoon();
            }
            else
            {
                _syncTimer?.Stop();
                _shared.Clear();
                RefreshSharedGroup();
            }
        }

        private void SyncSoon()
        {
            if (_syncSoonTimer == null || !_appSettings.SyncEnabled) return;
            _syncSoonTimer.Stop();
            _syncSoonTimer.Start();
        }

        private void SyncNow_Click(object sender, RoutedEventArgs e) => RunSync(manual: true);

        private void SyncNowButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_appSettings.SyncEnabled || string.IsNullOrWhiteSpace(_appSettings.SyncFolder))
            {
                Status("Сначала укажите папку обмена в настройках");
                OpenSettings_Click(sender, e);
                return;
            }
            RunSync(manual: true);
        }

        private void RunSync(bool manual = false)
        {
            if (_syncRunning || !_appSettings.SyncEnabled || string.IsNullOrWhiteSpace(_appSettings.SyncFolder)) return;
            SaveAll(quiet: true);
            _syncRunning = true;
            SyncText.Text = "Обмен…";
            SyncNowText.Text = "Обмен…";
            SyncNowBtn.IsEnabled = false;
            var sync = new FolderSync(ClientsRoot, _appSettings.SyncFolder, _appConfigDir);
            var exchange = _appSettings.SyncFolder;
            var myName = _appSettings.DisplayName;

            Task.Run(() =>
            {
                var result = sync.Run();
                List<SharedProject>? shared = null;
                List<SharedPerson>? people = null;
                List<Transfer>? inbox = null;
                try
                {
                    SharedProjects.TouchPerson(exchange, myName);
                    people = SharedProjects.LoadPeople(exchange);
                }
                catch (Exception ex) { LogError("Список коллег", ex); }
                try { shared = SharedProjects.LoadAll(exchange); }
                catch (Exception ex) { LogError("Общие проекты", ex); }
                try { inbox = Transfers.LoadInbox(exchange, AppSettings.UserId); }
                catch (Exception ex) { LogError("Входящие передачи", ex); }
                return (result, shared, people, inbox);
            }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                _syncRunning = false;
                SyncNowText.Text = "Обменяться";
                SyncNowBtn.IsEnabled = true;
                if (t.Exception != null)
                {
                    var ex = t.Exception.GetBaseException();
                    var unavailable = ex is DirectoryNotFoundException or IOException;
                    SyncText.Text = unavailable ? "Обмен: папка недоступна" : "Обмен: ошибка";
                    SyncBtn.ToolTip = ex.Message;
                    LogError("Обмен через папку", ex);
                    if (manual)
                        Status(unavailable ? $"Папка обмена недоступна: {_appSettings.SyncFolder}" : "Обмен не удался: " + ex.Message);
                    return;
                }
                var (r, shared, people, inbox) = t.Result;
                if (people != null) _people = people;
                if (inbox != null && exchange == _appSettings.SyncFolder) UpdateInbox(inbox, notify: true);
                SyncText.Text = "Обмен: " + DateTime.Now.ToString("HH:mm");
                SyncBtn.ToolTip = $"Папка обмена: {_appSettings.SyncFolder}. Нажмите, чтобы обменяться сейчас";
                if (r.Conflicts > 0)
                    Status($"Обмен: карточку клиента меняли двое, оставлена более свежая версия. Вторая сохранена в {Path.Combine(_appConfigDir, "conflicts")}");
                else if (manual)
                    Status(r.Uploaded == 0 && r.Downloaded == 0
                        ? "Обмен завершён: всё уже совпадает"
                        : $"Обмен завершён: отправлено {r.Uploaded}, получено {r.Downloaded}");
                else if (r.Downloaded > 0)
                    Status($"Обмен: получено изменений — {r.Downloaded}");
                ApplySyncChanges(r);
                if (shared != null && exchange == _appSettings.SyncFolder) ApplyShared(shared, exchange);
            }));
        }

        private void ApplySyncChanges(SyncResult r)
        {
            if (r.LocalChanged.Count == 0 && !r.StructureChanged) return;
            var changed = r.LocalChanged.Select(rel => Path.Combine(ClientsRoot, rel)).ToList();
            var current = _currentDocNode ?? _currentClientNode;
            var currentChanged = current != null && changed.Any(f =>
                f.StartsWith(current.FullPath + "\\", StringComparison.OrdinalIgnoreCase) &&
                Path.GetDirectoryName(f)!.Equals(current.FullPath, StringComparison.OrdinalIgnoreCase));
            var canReload = currentChanged && !_isDirty && !_clientDirty;

            if (r.StructureChanged)
            {
                ReloadTreePreserving(reloadCurrent: canReload);
                return;
            }
            if (canReload && current != null)
            {
                if (current.Type == NodeType.Document) LoadDocument(current);
                else if (current.Type == NodeType.Client) LoadClient(current);
                UpdateNoteReminderButton();
            }
        }

        private void ReloadTreePreserving(bool reloadCurrent)
        {
            var expanded = new HashSet<string>(Flatten(RootNodes).Where(n => n.IsExpanded).Select(n => n.FullPath), StringComparer.OrdinalIgnoreCase);
            var selectedPath = (Tree.SelectedItem as NodeViewModel)?.FullPath;
            LoadTree();
            foreach (var n in Flatten(RootNodes))
                if (expanded.Contains(n.FullPath)) n.IsExpanded = true;

            var target = selectedPath == null ? null
                : Flatten(RootNodes).FirstOrDefault(n => n.FullPath.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                if (_currentDocNode != null || _currentClientNode != null)
                {
                    _currentDocNode = null;
                    _currentClientNode = null;
                    DetachCurrentClient();
                    _isDirty = _clientDirty = false;
                    ShowEmpty(null);
                    Status("Открытую запись удалили в папке обмена");
                }
                RefreshReminders();
                return;
            }
            _rebindOnly = !reloadCurrent;
            SelectNode(target);
            RefreshReminders();
        }

        private bool TryRebind(NodeViewModel? node)
        {
            if (!_rebindOnly) return false;
            _rebindOnly = false;
            var current = _currentDocNode ?? _currentClientNode;
            if (node == null || current == null || node.Type != current.Type ||
                !node.FullPath.Equals(current.FullPath, StringComparison.OrdinalIgnoreCase))
                return false;
            if (node.Type == NodeType.Document) _currentDocNode = node;
            else
            {
                _currentClientNode = node;
                ClientView.RebindNode(node);
            }
            return true;
        }
    }
}
