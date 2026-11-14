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
            if (_appSettings.AutoUpdate && DateTime.Now - _startedAt < TimeSpan.FromMinutes(3) && _appSettings.UpdateAttemptVersion != v)
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

        private async void RunUpdate(bool auto)
        {
            if (_updateTarget is not { } target || _updateStarted) return;
            if (!auto && !ThemedMessageBox.Confirm(this, $"Обновить до версии {target.Version.ToString(3)}?",
                    "Программа закроется, поставит новую версию и сразу откроется снова. Windows может спросить разрешение на установку.", "Обновить"))
                return;
            SaveAll(quiet: true);
            _updateStarted = true;
            _appSettings.UpdateAttemptVersion = target.Version.ToString(3);
            _appSettings.Save();
            Status("Готовлю обновление " + target.Version.ToString(3) + "…");
            bool started;
            var toTray = !IsVisible || auto;
            try
            {
                started = await Task.Run(() => Updates.Launch(target, toTray));
            }
            catch (Exception ex)
            {
                LogError("Не удалось запустить обновление", ex);
                Status("Не удалось запустить обновление: " + ex.Message);
                _updateStarted = false;
                return;
            }
            if (!started)
            {
                Status("Обновление не запустилось. Нажмите «Обновить» вверху, когда будет удобно");
                _updateStarted = false;
                return;
            }
            ExitApp();
        }
    }
}
