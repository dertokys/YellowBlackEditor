using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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
        private ObservableCollection<Reminder> _generalReminders = new();
        private readonly Dictionary<string, (DateTime MTime, ClientCard Card)> _cardCache = new(StringComparer.OrdinalIgnoreCase);
        private DispatcherTimer? _reminderTimer;
        private WF.NotifyIcon? _tray;
        private bool _remindersMode;
        private bool _exitRequested;
        private AppSettings _appSettings = AppSettings.Load();

        private void InitReminders()
        {
            RemindersPanel.StatusMessage += Status;
            RemindersPanel.OpenClientRequested += n => SelectNode(n);
            RemindersPanel.OpenNoteRequested += n => SelectNode(n);
            RemindersPanel.EntryChanged += e => { PersistReminder(e); RefreshReminders(); };
            RemindersPanel.EntryDeleted += DeleteReminder;
            RemindersPanel.AddRequested += r =>
            {
                _generalReminders.Add(r);
                SaveGeneralReminders();
                RefreshReminders();
                Status("Напоминание добавлено");
            };

            try
            {
                _tray = new WF.NotifyIcon
                {
                    Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
                    Text = AppTitle,
                    Visible = true
                };
                _tray.BalloonTipClicked += (_, _) =>
                {
                    if (_lastBalloonIsReminder) BringToFrontAndShowReminders();
                    else BringToFront();
                };
                _tray.DoubleClick += (_, _) => BringToFront();
                var menu = new WF.ContextMenuStrip();
                menu.Items.Add("Открыть", null, (_, _) => BringToFront());
                menu.Items.Add("Напоминания", null, (_, _) => BringToFrontAndShowReminders());
                menu.Items.Add(new WF.ToolStripSeparator());
                menu.Items.Add("Выход", null, (_, _) => ExitApp());
                _tray.ContextMenuStrip = menu;
                System.Windows.Application.Current.SessionEnding += (_, _) => _exitRequested = true;
            }
            catch (Exception ex)
            {
                LogError("Не удалось создать значок в трее", ex);
            }

            LoadGeneralReminders();
            _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _reminderTimer.Tick += (_, _) => CheckReminders();
            _reminderTimer.Start();
            Dispatcher.BeginInvoke(CheckReminders, DispatcherPriority.ApplicationIdle);
        }

        private void LoadGeneralReminders()
        {
            try
            {
                _generalReminders = ReminderStore.Load(_workspacePath);
            }
            catch (Exception ex)
            {
                _generalReminders = new();
                LogError("Не удалось прочитать общие напоминания", ex);
            }
            _cardCache.Clear();
        }

        private void SaveGeneralReminders()
        {
            try
            {
                ReminderStore.Save(_workspacePath, _generalReminders);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось сохранить напоминания", ex, "Проверьте, что рабочая папка доступна.");
            }
        }

        private ClientCard? CardFor(NodeViewModel client)
        {
            if (ReferenceEquals(client, _currentClientNode) && _currentClient != null) return _currentClient;
            try
            {
                var path = ClientStore.CardPath(client.FullPath);
                if (!File.Exists(path)) return null;
                var mtime = File.GetLastWriteTimeUtc(path);
                if (_cardCache.TryGetValue(client.FullPath, out var c) && c.MTime == mtime) return c.Card;
                var card = ClientStore.Load(client.FullPath);
                _cardCache[client.FullPath] = (mtime, card);
                return card;
            }
            catch (Exception ex)
            {
                LogError($"Не удалось прочитать карточку {client.FullPath}", ex);
                return null;
            }
        }

        private List<ReminderEntry> CollectReminders()
        {
            var notes = Flatten(RootNodes).Where(n => n.Type == NodeType.Document)
                .GroupBy(RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var list = _generalReminders.Select(r => new ReminderEntry
            {
                Item = r,
                Note = r.NotePath.Length > 0 && notes.TryGetValue(r.NotePath, out var note) ? note : null
            }).ToList();
            foreach (var client in Flatten(RootNodes).Where(n => n.Type == NodeType.Client))
            {
                var card = CardFor(client);
                if (card == null) continue;
                list.AddRange(card.Reminders.Select(r => new ReminderEntry { Item = r, Client = client, Card = card }));
            }
            return list;
        }

        private void PersistReminder(ReminderEntry entry)
        {
            if (entry.Client == null || entry.Card == null)
            {
                SaveGeneralReminders();
                return;
            }
            if (ReferenceEquals(entry.Card, _currentClient))
            {
                _clientDirty = true;
                RestartSaveTimer();
                return;
            }
            try
            {
                ClientStore.Save(entry.Client.FullPath, entry.Card);
                _cardCache[entry.Client.FullPath] = (File.GetLastWriteTimeUtc(ClientStore.CardPath(entry.Client.FullPath)), entry.Card);
                _contentCache.Remove(entry.Client.FullPath);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось сохранить напоминание", ex, "Проверьте, что рабочая папка доступна.");
            }
        }

        private void DeleteReminder(ReminderEntry entry)
        {
            if (entry.Client == null) _generalReminders.Remove(entry.Item);
            else entry.Card?.Reminders.Remove(entry.Item);
            PersistReminder(entry);
            RefreshReminders();
            Status("Напоминание удалено");
        }

        private void RefreshReminders()
        {
            var entries = CollectReminders();
            UpdateBell(entries);
            UpdateNoteReminderButton();
            if (_remindersMode) RemindersPanel.Show(entries);
        }

        private string RelativePath(NodeViewModel node) => Path.GetRelativePath(_workspacePath, node.FullPath);

        private Reminder? OpenReminderFor(NodeViewModel note)
        {
            var rel = RelativePath(note);
            return _generalReminders
                .Where(r => !r.Done && string.Equals(r.NotePath, rel, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Due ?? DateTime.MaxValue)
                .FirstOrDefault();
        }

        private void UpdateNoteReminderButton()
        {
            if (_currentDocNode == null || _remindersMode)
            {
                NoteReminderBtn.Visibility = Visibility.Collapsed;
                return;
            }
            NoteReminderBtn.Visibility = Visibility.Visible;
            var r = OpenReminderFor(_currentDocNode);
            if (r?.Due == null)
            {
                NoteReminderText.Text = "Напомнить";
                NoteReminderIcon.ClearValue(ForegroundProperty);
                NoteReminderText.ClearValue(ForegroundProperty);
                return;
            }
            var overdue = r.Due <= DateTime.Now;
            NoteReminderText.Text = (overdue ? "Просрочено: " : "Напомню ") + DateText.Human(r.Due.Value);
            var brush = (System.Windows.Media.Brush)FindResource(overdue ? "DangerBrush" : "AccentBrush");
            NoteReminderIcon.Foreground = brush;
            if (overdue) NoteReminderText.Foreground = brush;
            else NoteReminderText.ClearValue(ForegroundProperty);
        }

        private void NoteReminder_Click(object sender, RoutedEventArgs e)
        {
            var note = _currentDocNode;
            if (note == null) return;
            var existing = OpenReminderFor(note);
            ReminderPicker.Open(NoteReminderBtn, existing?.Due, existing?.Text ?? note.Name, true, existing != null,
                (due, text) =>
                {
                    var r = existing ?? new Reminder { NotePath = RelativePath(note) };
                    r.Text = string.IsNullOrWhiteSpace(text) ? note.Name : text;
                    r.Due = due;
                    r.Notified = false;
                    if (existing == null) _generalReminders.Add(r);
                    SaveGeneralReminders();
                    RefreshReminders();
                    Status("Напомню " + DateText.Human(due));
                },
                () =>
                {
                    if (existing == null) return;
                    _generalReminders.Remove(existing);
                    SaveGeneralReminders();
                    RefreshReminders();
                    Status("Напоминание убрано");
                });
        }

        private void MoveNoteReminders(string oldDir, string newDir)
        {
            var oldRel = Path.GetRelativePath(_workspacePath, oldDir);
            var newRel = Path.GetRelativePath(_workspacePath, newDir);
            var changed = false;
            foreach (var r in _generalReminders)
            {
                if (r.NotePath.Equals(oldRel, StringComparison.OrdinalIgnoreCase))
                    r.NotePath = newRel;
                else if (r.NotePath.StartsWith(oldRel + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    r.NotePath = newRel + r.NotePath.Substring(oldRel.Length);
                else continue;
                changed = true;
            }
            if (changed) SaveGeneralReminders();
        }

        private void UpdateBell(List<ReminderEntry> entries)
        {
            var now = DateTime.Now;
            var overdue = entries.Count(e => !e.Item.Done && e.Item.Due != null && e.Item.Due <= now);
            BellBadge.Visibility = overdue > 0 ? Visibility.Visible : Visibility.Collapsed;
            BellCount.Text = overdue > 99 ? "99+" : overdue.ToString();
            BellBtn.ToolTip = overdue > 0 ? $"Напоминания · просрочено: {overdue}" : "Напоминания";
        }

        private void CheckReminders()
        {
            List<ReminderEntry> entries;
            try
            {
                entries = CollectReminders();
            }
            catch (Exception ex)
            {
                LogError("Не удалось проверить напоминания", ex);
                return;
            }

            var now = DateTime.Now;
            var due = entries.Where(e => !e.Item.Done && !e.Item.Notified && e.Item.Due != null && e.Item.Due <= now).ToList();
            foreach (var e in entries) e.Item.RefreshTime();

            if (due.Count > 0)
            {
                foreach (var e in due)
                {
                    e.Item.Notified = true;
                    PersistReminder(e);
                }
                Notify(due);
            }

            UpdateBell(entries);
            UpdateNoteReminderButton();
            if (_remindersMode && due.Count > 0) RemindersPanel.Show(entries);
        }

        private void Notify(List<ReminderEntry> due)
        {
            var first = due[0];
            var source = first.Client?.Name ?? first.Note?.Name;
            var title = due.Count == 1
                ? (source != null ? $"Напоминание · {source}" : "Напоминание")
                : $"Напоминаний: {due.Count}";
            var text = due.Count == 1
                ? (string.IsNullOrWhiteSpace(first.Item.Text) ? "Без текста" : first.Item.Text)
                : string.Join("\n", due.Take(3).Select(e => (e.Client != null ? e.Client.Name + ": " : "") + e.Item.Text));

            try
            {
                _lastBalloonIsReminder = true;
                _tray?.ShowBalloonTip(10000, title, text, WF.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                LogError("Не удалось показать уведомление", ex);
            }
            Status(due.Count == 1 ? $"Напоминание: {text}" : title);
        }

        private bool _lastBalloonIsReminder;

        public void BringToFront()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }

        private void BringToFrontAndShowReminders()
        {
            BringToFront();
            ShowReminders();
        }

        private void ExitApp()
        {
            _exitRequested = true;
            if (!IsVisible) Show();
            Close();
            if (IsLoaded && IsVisible) _exitRequested = false;
        }

        private bool HideToTrayInsteadOfClosing()
        {
            if (_exitRequested || !_appSettings.MinimizeToTray || _tray == null) return false;
            _saveTimer.Stop();
            SaveAll(quiet: true);
            SaveWindowPlacement();
            Hide();
            if (!_appSettings.TrayHintShown)
            {
                _lastBalloonIsReminder = false;
                _tray.ShowBalloonTip(8000, AppTitle,
                    "Программа продолжает работать в трее, чтобы напомнить вовремя. Выйти: правая кнопка по значку, «Выход».",
                    WF.ToolTipIcon.Info);
                _appSettings.TrayHintShown = true;
                _appSettings.Save();
            }
            return true;
        }

        private void ShowReminders_Click(object sender, RoutedEventArgs e) => ShowReminders();

        private void ShowReminders()
        {
            if (!SaveAll()) return;
            _saveTimer.Stop();
            _remindersMode = true;

            if (Tree.SelectedItem is NodeViewModel sel) sel.IsSelected = false;

            _currentDocNode = null;
            _currentClientNode = null;
            DetachCurrentClient();
            _isDirty = _clientDirty = false;
            _emptyStateNode = null;

            ClientView.Clear();
            ClientView.Visibility = Visibility.Collapsed;
            EditorHost.Visibility = Visibility.Collapsed;
            FormatBar.IsEnabled = false;
            RenameBtn.Visibility = Visibility.Collapsed;
            NoteReminderBtn.Visibility = Visibility.Collapsed;
            CurrentKindText.Text = "";
            CurrentFileText.Text = "Напоминания";
            DirtyText.Text = "";
            RemindersPanel.Visibility = Visibility.Visible;
            UpdateTitle();
            RefreshReminders();
        }

        private void HideReminders()
        {
            _remindersMode = false;
            RemindersPanel.Visibility = Visibility.Collapsed;
        }

        private void DisposeTray()
        {
            if (_tray == null) return;
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}
