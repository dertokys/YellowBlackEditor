using System.Windows;
using YellowBlackEditor.Utils;
using YellowBlackEditor.Views;
using WF = System.Windows.Forms;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private readonly DateTime _startedAt = DateTime.Now;
        private Updates.Installer? _updateTarget;
        private bool _updateStarted;

        private void InitUpdates()
        {
            if (_appSettings.AutostartApplied) return;
            try { AppSettings.StartsWithWindows = true; }
            catch (Exception ex) { LogError("Не удалось включить автозапуск", ex); }
            _appSettings.AutostartApplied = true;
            _appSettings.Save();
        }

        private void ApplyUpdateInfo(Updates.Installer? target)
        {
            _updateTarget = target;
            if (target == null)
            {
                UpdateBtn.Visibility = Visibility.Collapsed;
                return;
            }
            var v = target.Version.ToString(3);
            UpdateText.Text = "Обновить до " + v;
            UpdateBtn.Visibility = Visibility.Visible;
            UpdateBtn.ToolTip = $"В папке обмена лежит версия {v}. У вас {Updates.Current.ToString(3)}";

            if (_updateStarted) return;
            if (_appSettings.AutoUpdate && DateTime.Now - _startedAt < TimeSpan.FromMinutes(3))
            {
                RunUpdate(auto: true);
                return;
            }
            if (_appSettings.SeenUpdate == v) return;
            _appSettings.SeenUpdate = v;
            _appSettings.Save();
            var text = _appSettings.AutoUpdate
                ? $"Вышла версия {v}. Установится сама при следующем запуске, или нажмите «Обновить» вверху"
                : $"Вышла версия {v}. Нажмите «Обновить» вверху";
            Status(text);
            try
            {
                _lastBalloonIsReminder = false;
                _balloonTransfer = false;
                _balloonSharedId = null;
                _tray?.ShowBalloonTip(8000, "Обновление Yellow Black Notes", text, WF.ToolTipIcon.Info);
            }
            catch { }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e) => RunUpdate(auto: false);

        private void RunUpdate(bool auto)
        {
            if (_updateTarget is not { } target || _updateStarted) return;
            if (!auto && !ThemedMessageBox.Confirm(this, $"Обновить до версии {target.Version.ToString(3)}?",
                    "Программа закроется, поставит новую версию и сразу откроется снова. Windows может спросить разрешение на установку.", "Обновить"))
                return;
            SaveAll(quiet: true);
            bool started;
            try
            {
                started = Updates.Launch(target, !IsVisible || auto);
            }
            catch (Exception ex)
            {
                LogError("Не удалось запустить обновление", ex);
                Status("Не удалось запустить обновление: " + ex.Message);
                return;
            }
            if (!started)
            {
                Status("Обновление отменено. Нажмите «Обновить» вверху, когда будет удобно");
                return;
            }
            _updateStarted = true;
            ExitApp();
        }
    }
}
