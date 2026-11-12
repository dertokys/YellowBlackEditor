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
                    var path = ClientStore.CardPath(client.FullPath);
                    var raw = File.ReadAllText(path);
                    if (!raw.Contains("\"ProtectedPassword\"") && !raw.Contains("\"Reminders\"")) continue;
                    var card = ClientStore.Load(client.FullPath);
                    var protectedBefore = raw.Split("\"ProtectedPassword\"").Length - 1;
                    var protectedAfter = card.Secrets().Count(x => x.ProtectedPassword != null);
                    if (card.Reminders == null && protectedAfter == protectedBefore) continue;
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
            }
        }

        private void SyncSoon()
        {
            if (_syncSoonTimer == null || !_appSettings.SyncEnabled) return;
            _syncSoonTimer.Stop();
            _syncSoonTimer.Start();
        }

        private void SyncNow_Click(object sender, RoutedEventArgs e) => RunSync();

        private void RunSync()
        {
            if (_syncRunning || !_appSettings.SyncEnabled || string.IsNullOrWhiteSpace(_appSettings.SyncFolder)) return;
            SaveAll(quiet: true);
            _syncRunning = true;
            SyncText.Text = "Обмен…";
            var sync = new FolderSync(ClientsRoot, _appSettings.SyncFolder, _appConfigDir);

            Task.Run(sync.Run).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                _syncRunning = false;
                if (t.Exception != null)
                {
                    var ex = t.Exception.GetBaseException();
                    SyncText.Text = ex is DirectoryNotFoundException or IOException ? "Обмен: папка недоступна" : "Обмен: ошибка";
                    SyncBtn.ToolTip = ex.Message;
                    LogError("Обмен через папку", ex);
                    return;
                }
                var r = t.Result;
                SyncText.Text = "Обмен: " + DateTime.Now.ToString("HH:mm");
                SyncBtn.ToolTip = $"Папка обмена: {_appSettings.SyncFolder}. Нажмите, чтобы обменяться сейчас";
                if (r.Conflicts > 0)
                    Status($"Обмен: одну заметку меняли двое, оставлена более свежая версия. Вторая сохранена в {Path.Combine(_appConfigDir, "conflicts")}");
                else if (r.Downloaded > 0)
                    Status($"Обмен: получено изменений — {r.Downloaded}");
                ApplySyncChanges(r);
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
