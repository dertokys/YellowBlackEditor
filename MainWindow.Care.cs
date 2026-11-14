using System.IO;
using System.IO.Compression;
using System.Windows.Threading;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using WF = System.Windows.Forms;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private DispatcherTimer? _careTimer;
        private bool _balloonToday;

        public static string BackupDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YellowBlackNotes", "Резервные копии");

        private void InitCare()
        {
            _careTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
            _careTimer.Tick += (_, _) =>
            {
                _careTimer.Interval = TimeSpan.FromHours(1);
                CheckSubscriptionAlerts();
                RunDailyBackup();
            };
            _careTimer.Start();
        }

        private void CheckSubscriptionAlerts()
        {
            var alerts = new List<(string Key, string Text)>();
            foreach (var (client, list) in _clientSoftware.ToList())
            {
                IEnumerable<ClientSoftware> src = ReferenceEquals(client, _currentClientNode) && _currentClient != null ? _currentClient.Software : list;
                foreach (var s in src)
                    foreach (var a in s.Addons)
                    {
                        if (a.DaysLeft is not { } days || a.Ends is not { } ends) continue;
                        int? stage = days < -7 ? null : days < 0 ? -1 : days <= 1 ? 1 : days <= 7 ? 7 : days <= 30 ? 30 : null;
                        if (stage == null) continue;
                        var key = $"{a.Id}|{stage}|{ends:yyyyMMdd}";
                        if (_appSettings.SeenSubAlerts.Contains(key)) continue;
                        var name = string.IsNullOrWhiteSpace(a.Name) ? "подписка" : a.Name;
                        alerts.Add((key, $"{client.Name}: {name} — {a.StateText}"));
                    }
            }
            if (alerts.Count == 0) return;
            _appSettings.SeenSubAlerts.AddRange(alerts.Select(a => a.Key));
            if (_appSettings.SeenSubAlerts.Count > 2000) _appSettings.SeenSubAlerts.RemoveRange(0, _appSettings.SeenSubAlerts.Count - 2000);
            _appSettings.Save();

            var title = alerts.Count == 1 ? "Подписка заканчивается" : $"Заканчиваются подписки: {alerts.Count}";
            var text = string.Join("\n", alerts.Take(4).Select(a => a.Text));
            Status(alerts[0].Text);
            try
            {
                _lastBalloonIsReminder = false;
                _balloonTransfer = false;
                _balloonSharedId = null;
                _balloonToday = true;
                _tray?.ShowBalloonTip(10000, title, text, WF.ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                LogError("Не удалось показать уведомление", ex);
            }
        }

        private void OpenBalloonToday()
        {
            _balloonToday = false;
            BringToFront();
            if (RootNodes.Contains(_todayNode)) SelectNode(_todayNode);
        }

        private void RunDailyBackup()
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            if (_appSettings.LastBackup == today || !Directory.Exists(_workspacePath)) return;
            var workspace = _workspacePath;
            Task.Run(() =>
            {
                Directory.CreateDirectory(BackupDir);
                var target = Path.Combine(BackupDir, $"{today} {SanitizeName(Path.GetFileName(workspace))}.zip");
                var tmp = target + ".tmp";
                var skipped = 0;
                try
                {
                    using (var fs = new FileStream(tmp, FileMode.Create))
                    using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, System.Text.Encoding.UTF8))
                    {
                        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
                        foreach (var file in Directory.EnumerateFiles(workspace, "*", options))
                        {
                            var name = Path.GetFileName(file);
                            if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".synctmp", StringComparison.OrdinalIgnoreCase)) continue;
                            try
                            {
                                if (new FileInfo(file).Length > 50L * 1024 * 1024) { skipped++; continue; }
                                zip.CreateEntryFromFile(file, Path.GetRelativePath(workspace, file), CompressionLevel.Fastest);
                            }
                            catch
                            {
                                skipped++;
                            }
                        }
                    }
                    File.Move(tmp, target, true);
                }
                catch
                {
                    try { File.Delete(tmp); } catch { }
                    throw;
                }
                foreach (var old in new DirectoryInfo(BackupDir).GetFiles("*.zip").OrderByDescending(f => f.Name).Skip(14))
                {
                    try { old.Delete(); } catch { }
                }
                return skipped;
            }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                if (t.Exception != null)
                {
                    LogError("Резервная копия", t.Exception.GetBaseException());
                    return;
                }
                _appSettings.LastBackup = today;
                _appSettings.Save();
            }));
        }
    }
}
