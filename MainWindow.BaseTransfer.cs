using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using YellowBlackEditor.Utils;
using YellowBlackEditor.Views;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private bool _baseTransferRunning;

        private static string AppVersion =>
            System.Reflection.Assembly.GetExecutingAssembly().GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";

        private void BaseTransfer_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            var export = new MenuItem { Header = "Выгрузить базу на рабочий стол" };
            export.Click += (_, _) => ExportBase();
            var import = new MenuItem { Header = "Загрузить базу из файла…" };
            import.Click += (_, _) => ImportBase();
            menu.Items.Add(export);
            menu.Items.Add(import);
            menu.IsOpen = true;
        }

        private bool BaseTransferBusy()
        {
            if (_baseTransferRunning)
            {
                Status("Перенос базы ещё идёт");
                return true;
            }
            if (_syncRunning)
            {
                ThemedMessageBox.Show(this, "Идёт обмен", "Сейчас программа обменивается с общей папкой. Попробуйте через минуту.");
                return true;
            }
            if (!Directory.Exists(_workspacePath))
            {
                ThemedMessageBox.Show(this, "Нет рабочей папки", "Рабочая папка не найдена. Выберите её кнопкой с папкой вверху.", MessageBoxImage.Warning);
                return true;
            }
            return false;
        }

        private void ExportBase()
        {
            if (BaseTransferBusy()) return;
            SaveAll();
            _baseTransferRunning = true;
            Status("Выгружаю базу на рабочий стол…");
            var workspace = _workspacePath;
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var by = _appSettings.DisplayName;
            var version = AppVersion;
            Task.Run(() => BaseTransfer.Export(workspace, desktop, by, version)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                _baseTransferRunning = false;
                if (t.Exception != null)
                {
                    Status("Базу выгрузить не удалось");
                    ShowError("Не удалось выгрузить базу", t.Exception.GetBaseException());
                    return;
                }
                var (path, files, skipped) = t.Result;
                Status("База выгружена: " + Path.GetFileName(path));
                var note = skipped > 0 ? $"\n\nНе удалось прочитать файлов: {skipped}. Возможно, они открыты в другой программе." : "";
                if (ThemedMessageBox.Confirm(this, "База выгружена",
                        $"На рабочем столе лежит файл «{Path.GetFileName(path)}», в нём {files} файлов.\n\n" +
                        "Возьмите его с собой и на другом компьютере нажмите «Загрузить базу из файла». Когда вернётесь, так же выгрузите базу там и загрузите здесь: изменения объединятся." + note,
                        "Показать файл", "Готово", MessageBoxImage.Information))
                {
                    try { Process.Start("explorer.exe", $"/select,\"{path}\""); }
                    catch (Exception ex) { LogError("Показать выгрузку", ex); }
                }
            }));
        }

        private void ImportBase()
        {
            if (BaseTransferBusy()) return;
            SaveAll();
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Выберите выгрузку базы",
                Filter = "Выгрузка базы (*.zip)|*.zip",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            };
            if (dlg.ShowDialog(this) != true) return;
            var zip = dlg.FileName;

            string about;
            try
            {
                var manifest = BaseTransfer.ReadManifest(zip);
                if (manifest is { Format: > BaseTransfer.Format })
                {
                    ThemedMessageBox.Show(this, "Нужна новая версия", $"Базу выгрузила программа версии {manifest.Version}. Обновите программу и загрузите ещё раз.", MessageBoxImage.Warning);
                    return;
                }
                about = manifest != null
                    ? $"Выгрузка от {manifest.Exported:d MMMM yyyy, HH:mm}, {manifest.By} (компьютер {manifest.Machine}), файлов: {manifest.Files}."
                    : $"Архив без описания выгрузки, например резервная копия. Файлов: {BaseTransfer.CountFiles(zip)}.";
            }
            catch (Exception ex)
            {
                ShowError("Файл не читается", ex, "Похоже, это не zip-архив или он повреждён.");
                return;
            }

            if (!ThemedMessageBox.Confirm(this, "Загрузить базу?",
                    about + "\n\nНовые заметки и клиенты добавятся. Если файл менялся только в выгрузке, возьмётся он; если только здесь, останется здешний. " +
                    "Карточку клиента, изменённую и там, и тут, программа объединит по полям, а у заметки оставит более новую версию. Удалённое в выгрузке здесь не удаляется.\n\n" +
                    "Перед загрузкой текущая база сохранится в резервную копию.",
                    "Загрузить"))
                return;

            _baseTransferRunning = true;
            _syncTimer?.Stop();
            Status("Загружаю базу…");
            var workspace = _workspacePath;
            Task.Run(() => BaseTransfer.Import(zip, workspace, BackupDir)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                _baseTransferRunning = false;
                if (_appSettings.SyncEnabled) _syncTimer?.Start();
                _currentDocNode = null;
                _currentClientNode = null;
                _isDirty = _clientDirty = false;
                ShowEmpty(null);
                LoadTree();
                LoadGeneralReminders();
                RefreshReminders();
                if (RootNodes.Contains(_todayNode)) SelectNode(_todayNode);
                else ExpandAndSelectFirstDocument();

                if (t.Exception != null)
                {
                    Status("Базу загрузить не удалось");
                    ShowError("Не удалось загрузить базу", t.Exception.GetBaseException(),
                        "Часть файлов могла успеть загрузиться. Копия базы до загрузки лежит в папке резервных копий (Настройки → «Открыть копии»).");
                    return;
                }
                var r = t.Result;
                var lines = new List<string>();
                if (r.Added > 0) lines.Add($"Добавлено файлов: {r.Added}");
                if (r.Updated > 0) lines.Add($"Обновлено из выгрузки: {r.Updated}");
                if (r.Merged > 0) lines.Add($"Объединено (менялось и там, и тут): {r.Merged}");
                if (r.KeptLocal > 0) lines.Add($"Оставлено здешних, менялись только тут: {r.KeptLocal}");
                if (r.Conflicts > 0) lines.Add($"Менялось и там, и тут, оставлена более новая версия: {r.Conflicts}");
                if (lines.Count == 0) lines.Add("Всё совпадает, загружать нечего");
                var text = string.Join("\n", lines) + "\n\nКопия базы до загрузки: " + Path.GetFileName(r.Backup);
                if (r.ConflictDir.Length > 0) text += "\nЗаменённые версии сохранены в папке «" + Path.GetFileName(r.ConflictDir) + "» рядом с копиями.";
                Status("База загружена");
                if (r.ConflictDir.Length > 0)
                {
                    if (ThemedMessageBox.Confirm(this, "База загружена", text, "Открыть папку", "Готово", MessageBoxImage.Information))
                    {
                        try { Process.Start("explorer.exe", $"\"{r.ConflictDir}\""); }
                        catch (Exception ex) { LogError("Папка конфликтов", ex); }
                    }
                }
                else ThemedMessageBox.Show(this, "База загружена", text);
            }));
        }
    }
}
