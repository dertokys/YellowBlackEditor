using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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
        private ObservableCollection<Reminder>? _clientReminders;
        private string _clientRemindersPath = "";
        private DispatcherTimer? _reminderTimer;
        private DispatcherTimer? _reminderSaveTimer;
        private WF.NotifyIcon? _tray;
        private bool _exitRequested;
        private bool _lastBalloonIsReminder;
        private AppSettings _appSettings = AppSettings.Load();

        private void InitReminders()
        {
            RemindersPanel.StatusMessage += Status;
            RemindersPanel.OpenClientRequested += n => { RemindersPopup.IsOpen = false; SelectNode(n); };
            RemindersPanel.OpenNoteRequested += n => { RemindersPopup.IsOpen = false; SelectNode(n); };
            RemindersPanel.EntryChanged += _ => { SaveGeneralReminders(); RefreshReminders(); };
            RemindersPanel.EntryDeleted += DeleteReminder;
            RemindersPanel.ColleagueRequested += () =>
            {
                RemindersPopup.IsOpen = false;
                OpenTransfer(null, false, Transfer.KindReminder);
            };
            RemindersPanel.AddRequested += r =>
            {
                _generalReminders.Add(r);
                SaveGeneralReminders();
                RefreshReminders();
                Status("Напоминание добавлено");
            };

            _reminderSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _reminderSaveTimer.Tick += (_, _) =>
            {
                _reminderSaveTimer.Stop();
                SaveGeneralReminders();
                RefreshReminders();
            };

            PreviewMouseDown += (_, _) => { if (RemindersPopup.IsOpen && !BellBtn.IsMouseOver && !RemindersPanel.IsMouseOver) RemindersPopup.IsOpen = false; };
            Deactivated += (_, _) => RemindersPopup.IsOpen = false;
            LocationChanged += (_, _) => RemindersPopup.IsOpen = false;

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
                    else if (_balloonTransfer) OpenBalloonTransfer();
                    else if (_balloonToday) OpenBalloonToday();
                    else if (_balloonSharedId != null) OpenBalloonShared();
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
                LogError("Не удалось прочитать напоминания", ex);
            }
        }

        private void SaveGeneralReminders()
        {
            try
            {
                ReminderStore.Save(_workspacePath, _generalReminders);
            }
            catch (Exception ex)
            {
                LogError("Не удалось сохранить напоминания", ex);
                Status("Не удалось сохранить напоминания: рабочая папка недоступна");
            }
        }

        private void ScheduleReminderSave()
        {
            if (_reminderSaveTimer == null) return;
            _reminderSaveTimer.Stop();
            _reminderSaveTimer.Start();
        }

        private ObservableCollection<Reminder> ClientRemindersFor(NodeViewModel client, ClientCard card)
        {
            DetachClientReminders();
            var rel = RelativePath(client);

            if (card.Reminders is { Count: > 0 } legacy)
            {
                foreach (var r in legacy)
                {
                    r.ClientPath = rel;
                    _generalReminders.Add(r);
                }
                card.Reminders = null;
                SaveGeneralReminders();
                _clientDirty = true;
                RestartSaveTimer();
            }

            var list = new ObservableCollection<Reminder>(
                _generalReminders.Where(r => string.Equals(r.ClientPath, rel, StringComparison.OrdinalIgnoreCase)));
            foreach (var r in list) r.PropertyChanged += ClientReminder_PropertyChanged;
            list.CollectionChanged += ClientReminders_CollectionChanged;
            _clientReminders = list;
            _clientRemindersPath = rel;
            return list;
        }

        private void DetachClientReminders()
        {
            if (_clientReminders == null) return;
            _clientReminders.CollectionChanged -= ClientReminders_CollectionChanged;
            foreach (var r in _clientReminders) r.PropertyChanged -= ClientReminder_PropertyChanged;
            _clientReminders = null;
        }

        private void ClientReminders_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
                foreach (Reminder r in e.NewItems)
                {
                    r.ClientPath = _clientRemindersPath;
                    r.PropertyChanged += ClientReminder_PropertyChanged;
                    if (!_generalReminders.Contains(r)) _generalReminders.Add(r);
                }
            if (e.OldItems != null)
                foreach (Reminder r in e.OldItems)
                {
                    r.PropertyChanged -= ClientReminder_PropertyChanged;
                    _generalReminders.Remove(r);
                }
            ScheduleReminderSave();
        }

        private void ClientReminder_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(Reminder.IsOverdue) or nameof(Reminder.DueHuman) or nameof(Reminder.DueInvalid)) return;
            ScheduleReminderSave();
        }

        private Dictionary<string, NodeViewModel> NodesByPath(NodeType type) =>
            Flatten(RootNodes).Where(n => n.Type == type)
                .GroupBy(RelativePath, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        private List<ReminderEntry> CollectReminders()
        {
            var notes = NodesByPath(NodeType.Document);
            var clients = NodesByPath(NodeType.Client);
            return _generalReminders.Select(r => new ReminderEntry
            {
                Item = r,
                Note = r.NotePath.Length > 0 && notes.TryGetValue(r.NotePath, out var note) ? note : null,
                Client = r.ClientPath.Length > 0 && clients.TryGetValue(r.ClientPath, out var client) ? client : null
            }).ToList();
        }

        private void DeleteReminder(ReminderEntry entry)
        {
            _generalReminders.Remove(entry.Item);
            _clientReminders?.Remove(entry.Item);
            SaveGeneralReminders();
            RefreshReminders();
            Status("Напоминание удалено");
        }

        private void RefreshReminders()
        {
            var entries = CollectReminders();
            UpdateBell(entries);
            UpdateTreeBells(entries);
            UpdateNoteReminderButton();
            if (RemindersPopup.IsOpen) RemindersPanel.Show(entries);
            RefreshTodayIfShown();
        }

        private void UpdateTreeBells(List<ReminderEntry> entries)
        {
            var now = DateTime.Now;
            var state = new Dictionary<NodeViewModel, int>();
            foreach (var e in entries.Where(e => !e.Item.Done))
            {
                var node = e.Note ?? e.Client;
                if (node == null) continue;
                var s = e.Item.Due != null && e.Item.Due <= now ? 2 : 1;
                state[node] = Math.Max(state.GetValueOrDefault(node), s);
            }
            foreach (var n in Flatten(RootNodes))
                n.ReminderState = state.GetValueOrDefault(n);
        }

        private string RelativePath(NodeViewModel node) => Path.GetRelativePath(_workspacePath, node.FullPath);

        private void UpdateNoteReminderButton()
        {
            if (_currentDocNode == null)
            {
                NoteReminderBtn.Visibility = Visibility.Collapsed;
                return;
            }
            NoteReminderBtn.Visibility = Visibility.Visible;
            var all = NoteReminders(_currentDocNode);
            var r = all.FirstOrDefault(x => x.Due != null);
            NoteReminderBtn.ToolTip = all.Count switch
            {
                0 => "Напомнить об этой заметке",
                1 => "Нажмите, чтобы изменить или добавить ещё одно напоминание",
                _ => $"Напоминаний по заметке: {all.Count}. Нажмите, чтобы посмотреть все или добавить"
            };
            if (r?.Due == null)
            {
                NoteReminderText.Text = all.Count > 0 ? $"Напоминаний: {all.Count}" : "Напомнить";
                NoteReminderIcon.ClearValue(ForegroundProperty);
                NoteReminderText.ClearValue(ForegroundProperty);
                return;
            }
            var overdue = r.Due <= DateTime.Now;
            NoteReminderText.Text = (overdue ? "Просрочено: " : "Напомню ") + DateText.Human(r.Due.Value) + (all.Count > 1 ? $" и ещё {all.Count - 1}" : "");
            var brush = (System.Windows.Media.Brush)FindResource(overdue ? "DangerBrush" : "AccentTextBrush");
            NoteReminderIcon.Foreground = brush;
            if (overdue) NoteReminderText.Foreground = brush;
            else NoteReminderText.ClearValue(ForegroundProperty);
        }

        private static string MovedPath(string path, string oldRel, string newRel)
        {
            if (path.Equals(oldRel, StringComparison.OrdinalIgnoreCase)) return newRel;
            if (path.StartsWith(oldRel + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return newRel + path.Substring(oldRel.Length);
            return path;
        }

        private void MoveNoteReminders(string oldDir, string newDir)
        {
            var oldRel = Path.GetRelativePath(_workspacePath, oldDir);
            var newRel = Path.GetRelativePath(_workspacePath, newDir);
            var changed = false;
            foreach (var r in _generalReminders)
            {
                var note = MovedPath(r.NotePath, oldRel, newRel);
                var client = MovedPath(r.ClientPath, oldRel, newRel);
                if (note == r.NotePath && client == r.ClientPath) continue;
                r.NotePath = note;
                r.ClientPath = client;
                changed = true;
            }
            if (_clientRemindersPath.Length > 0) _clientRemindersPath = MovedPath(_clientRemindersPath, oldRel, newRel);
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
                foreach (var e in due) e.Item.Notified = true;
                SaveGeneralReminders();
                Notify(due);
            }

            UpdateBell(entries);
            UpdateTreeBells(entries);
            UpdateNoteReminderButton();
            if (RemindersPopup.IsOpen && due.Count > 0) RemindersPanel.Show(entries);
        }

        private void Notify(List<ReminderEntry> due)
        {
            var first = due[0];
            var source = first.Note?.Name ?? first.Client?.Name;
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
            Dispatcher.BeginInvoke(ShowReminders, DispatcherPriority.ApplicationIdle);
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
            RemindersPopup.IsOpen = false;
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

        private void ShowReminders_Click(object sender, RoutedEventArgs e)
        {
            if (RemindersPopup.IsOpen) RemindersPopup.IsOpen = false;
            else ShowReminders();
        }

        private void ShowReminders()
        {
            RemindersPanel.Show(CollectReminders());
            RemindersPopup.IsOpen = true;
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
