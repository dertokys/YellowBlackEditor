using System;
using System.Windows;
using System.Windows.Input;
using YellowBlackEditor.Utils;
using YellowBlackEditor.Models;
using YellowBlackEditor.Views;

namespace YellowBlackEditor.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly string _themePath;
        private readonly string _hotkeysPath;

        private ThemeSettings _originalTheme = new ThemeSettings();
        private HotkeySettings _hotkeysWork = new HotkeySettings();

        private enum TargetField { Accent, Text, Panel }
        private TargetField _active = TargetField.Accent;

        private bool _saved;
        private AppSettings _appSettings = new();
        private readonly KeyGestureConverter _conv = new KeyGestureConverter();

        public SettingsWindow(string themePath, string hotkeysPath)
        {
            InitializeComponent();
            SourceInitialized += (_, _) => YellowBlackEditor.Utils.DarkTitleBar.Apply(this);
            _themePath = themePath;
            _hotkeysPath = hotkeysPath;

            _originalTheme = Clone(ThemeManager.Current);
            _hotkeysWork = HotkeyManager.Load(_hotkeysPath);
            _appSettings = AppSettings.Load();
            TrayCheck.IsChecked = _appSettings.MinimizeToTray;
            try { AutostartCheck.IsChecked = AppSettings.StartsWithWindows; } catch { AutostartCheck.IsEnabled = false; }

            AccentBox.Text = ThemeManager.Current.Accent;
            TextBoxColor.Text = ThemeManager.Current.Text;
            PanelBox.Text = ThemeManager.Current.Panel;

            HkCreateSection.Text = _hotkeysWork.CreateSection;
            HkCreateDocument.Text = _hotkeysWork.CreateDocument;
            HkNavPrev.Text = _hotkeysWork.NavPrev;
            HkNavNext.Text = _hotkeysWork.NavNext;
            HkNavParent.Text = _hotkeysWork.NavParent;
            HkNavChild.Text = _hotkeysWork.NavChild;

            foreach (var size in new[] { 12, 13, 14, 15, 16, 17, 18, 20, 22, 24 })
            {
                EditorFontBox.Items.Add(size);
                TreeFontBox.Items.Add(size);
            }
            EditorFontBox.SelectedItem = (int)Math.Round(ThemeManager.Current.EditorFontSize);
            TreeFontBox.SelectedItem = (int)Math.Round(ThemeManager.Current.TreeFontSize);
            if (EditorFontBox.SelectedIndex < 0) EditorFontBox.SelectedItem = 16;
            if (TreeFontBox.SelectedIndex < 0) TreeFontBox.SelectedItem = 15;

            UpdatePreviews();
            HookEvents();
            UpdateActiveUi();

            Loaded += (s, e) => UpdateActiveUi();
            this.Closing += SettingsWindow_Closing;
        }

        private void HookEvents()
        {
            AccentBox.TextChanged += (s, e) => { UpdatePreviews(); SafePreviewApply(); };
            TextBoxColor.TextChanged += (s, e) => { UpdatePreviews(); SafePreviewApply(); };
            PanelBox.TextChanged += (s, e) => { UpdatePreviews(); SafePreviewApply(); };

            var presets = new[] { "#FFC107", "#FFD54F", "#FF9800", "#F44336", "#9C27B0", "#2196F3", "#4CAF50", "#E91E63" };
            foreach (var hex in presets)
            {
                var r = new System.Windows.Shapes.Rectangle
                {
                    Width = 26,
                    Height = 26,
                    RadiusX = 6,
                    RadiusY = 6,
                    Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!),
                    Stroke = GetThemeBrush("BorderBrush1", "#232323"),
                    StrokeThickness = 1,
                    Margin = new Thickness(4, 4, 0, 0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = hex
                };
                r.MouseLeftButtonUp += (s, e) => ApplyPreset(hex);
                QuickAccents.Children.Add(r);
            }
        }

        private void ApplyPreset(string hex)
        {
            switch (_active)
            {
                case TargetField.Accent: AccentBox.Text = hex; break;
                case TargetField.Text: TextBoxColor.Text = hex; break;
                case TargetField.Panel: PanelBox.Text = hex; break;
            }
            UpdatePreviews();
            SafePreviewApply();
        }

        private void SettingsWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_saved)
            {
                try { ThemeManager.Apply(_originalTheme); } catch { }
            }
        }

        private void UpdatePreviews()
        {
            try { AccentPreview.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(AccentBox.Text)!); } catch { }
            try { TextPreview.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(TextBoxColor.Text)!); } catch { }
            try { PanelPreview.Fill = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(PanelBox.Text)!); } catch { }
        }

        private void SafePreviewApply()
        {
            try
            {
                var s = Clone(ThemeManager.Current);
                s.Accent = AccentBox.Text.Trim();
                s.Text = TextBoxColor.Text.Trim();
                s.Panel = PanelBox.Text.Trim();
                s.Panel2 = ThemeManager.Current.Panel2;
                s.Bg = ThemeManager.Current.Bg;
                s.Border = ThemeManager.Current.Border;
                s.SubText = ThemeManager.Current.SubText;
                s.AccentLight = "";
                ThemeManager.Apply(s);
                UpdateActiveUi();
            }
            catch { }
        }

        private static ThemeSettings Clone(ThemeSettings s) => new ThemeSettings
        {
            Bg = s.Bg,
            Panel = s.Panel,
            Panel2 = s.Panel2,
            Border = s.Border,
            Text = s.Text,
            SubText = s.SubText,
            Accent = s.Accent,
            AccentLight = s.AccentLight,
            EditorFontSize = s.EditorFontSize,
            TreeFontSize = s.TreeFontSize
        };

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            AccentBox.Text = "#FFC107";
            TextBoxColor.Text = "#F5F5F5";
            PanelBox.Text = "#151515";
            UpdatePreviews();
            SafePreviewApply();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            _saved = false;
            try { ThemeManager.Apply(_originalTheme); } catch { }
            DialogResult = false;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var s = Clone(ThemeManager.Current);
                s.Accent = AccentBox.Text.Trim();
                s.Text = TextBoxColor.Text.Trim();
                s.Panel = PanelBox.Text.Trim();
                s.Panel2 = "#111111";
                s.Bg = "#0E0E0E";
                s.Border = "#232323";
                s.SubText = "#BDBDBD";
                s.AccentLight = "";
                if (EditorFontBox.SelectedItem is int ef) s.EditorFontSize = ef;
                if (TreeFontBox.SelectedItem is int tf) s.TreeFontSize = tf;
                ThemeManager.Apply(s);
                ThemeManager.Save(_themePath);

                var hk = new HotkeySettings
                {
                    CreateSection = HkCreateSection.Text.Trim(),
                    CreateDocument = HkCreateDocument.Text.Trim(),
                    NavPrev = HkNavPrev.Text.Trim(),
                    NavNext = HkNavNext.Text.Trim(),
                    NavParent = HkNavParent.Text.Trim(),
                    NavChild = HkNavChild.Text.Trim()
                };
                ValidateGesture(hk.CreateSection);
                ValidateGesture(hk.CreateDocument);
                ValidateGesture(hk.NavPrev);
                ValidateGesture(hk.NavNext);
                ValidateGesture(hk.NavParent);
                ValidateGesture(hk.NavChild);

                HotkeyManager.Save(_hotkeysPath, hk);

                _appSettings.MinimizeToTray = TrayCheck.IsChecked == true;
                _appSettings.Save();
                if (AutostartCheck.IsEnabled && AppSettings.StartsWithWindows != (AutostartCheck.IsChecked == true))
                    AppSettings.StartsWithWindows = AutostartCheck.IsChecked == true;

                _saved = true;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show(this, "Не удалось сохранить настройки",
                    $"Проверьте формат цвета, например #FFC107, и сочетания клавиш.\n\nПодробности: {ex.Message}", MessageBoxImage.Error);
            }
        }

        private void ValidateGesture(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return;
            _ = (System.Windows.Input.KeyGesture)_conv.ConvertFromString(s)!;
        }

        private void SetActive(TargetField f)
        {
            _active = f;
            UpdateActiveUi();
        }
        private void UpdateActiveUi()
        {
            if (AccentRow == null || TextRow == null || PanelRow == null) return;
            var accent = GetThemeBrush("AccentBrush", "#FFC107");
            var border = GetThemeBrush("BorderBrush1", "#232323");

            AccentRow.BorderBrush = (_active == TargetField.Accent) ? accent : border;
            TextRow.BorderBrush = (_active == TargetField.Text) ? accent : border;
            PanelRow.BorderBrush = (_active == TargetField.Panel) ? accent : border;

            AccentRow.BorderThickness = new Thickness(_active == TargetField.Accent ? 2 : 1);
            TextRow.BorderThickness = new Thickness(_active == TargetField.Text ? 2 : 1);
            PanelRow.BorderThickness = new Thickness(_active == TargetField.Panel ? 2 : 1);
        }
        private System.Windows.Media.Brush GetThemeBrush(string key, string fallbackHex)
        {
            var found = this.TryFindResource(key) as System.Windows.Media.Brush;
            if (found != null) return found;
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackHex)!;
            return new System.Windows.Media.SolidColorBrush(c);
        }

        private void AccentRow_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => SetActive(TargetField.Accent);
        private void TextRow_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => SetActive(TargetField.Text);
        private void PanelRow_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => SetActive(TargetField.Panel);
        private void AccentBox_GotFocus(object sender, RoutedEventArgs e) => SetActive(TargetField.Accent);
        private void TextBoxColor_GotFocus(object sender, RoutedEventArgs e) => SetActive(TargetField.Text);
        private void PanelBox_GotFocus(object sender, RoutedEventArgs e) => SetActive(TargetField.Panel);

        private void RecordHotkey_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string tag)
            {
                var cap = new HotkeyCaptureWindow { Owner = this };
                if (cap.ShowDialog() == true && !string.IsNullOrWhiteSpace(cap.CapturedGesture))
                {
                    switch (tag)
                    {
                        case "CreateSection": HkCreateSection.Text = cap.CapturedGesture; break;
                        case "CreateDocument": HkCreateDocument.Text = cap.CapturedGesture; break;
                        case "NavPrev": HkNavPrev.Text = cap.CapturedGesture; break;
                        case "NavNext": HkNavNext.Text = cap.CapturedGesture; break;
                        case "NavParent": HkNavParent.Text = cap.CapturedGesture; break;
                        case "NavChild": HkNavChild.Text = cap.CapturedGesture; break;
                    }
                }
            }
        }
    }
}