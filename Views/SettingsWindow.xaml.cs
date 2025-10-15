using System;
using System.Text;
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
        private string? _captureTarget;
        private readonly KeyGestureConverter _conv = new KeyGestureConverter();

        public SettingsWindow(string themePath, string hotkeysPath)
        {
            InitializeComponent();
            _themePath = themePath;
            _hotkeysPath = hotkeysPath;

            _originalTheme = Clone(ThemeManager.Current);
            _hotkeysWork = HotkeyManager.Load(_hotkeysPath);

            AccentBox.Text = ThemeManager.Current.Accent;
            TextBoxColor.Text = ThemeManager.Current.Text;
            PanelBox.Text = ThemeManager.Current.Panel;

            HkCreateSection.Text = _hotkeysWork.CreateSection;
            HkCreateDocument.Text = _hotkeysWork.CreateDocument;
            HkNavPrev.Text = _hotkeysWork.NavPrev;
            HkNavNext.Text = _hotkeysWork.NavNext;
            HkNavParent.Text = _hotkeysWork.NavParent;
            HkNavChild.Text = _hotkeysWork.NavChild;

            UpdatePreviews();
            HookEvents();
            UpdateActiveUi();

            Loaded += (s, e) => UpdateActiveUi();
            this.Closing += SettingsWindow_Closing;

            this.PreviewKeyDown += new System.Windows.Input.KeyEventHandler(SettingsWindow_PreviewKeyDown);
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

                _saved = true;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                ThemedMessageBox.Show(this, "Ошибка", $"Не удалось сохранить настройки:\n{ex.Message}", MessageBoxImage.Error);
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

        private void SettingsWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (string.IsNullOrEmpty(_captureTarget)) return;

            var mods = System.Windows.Input.Keyboard.Modifiers;
            var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;

            if (key == System.Windows.Input.Key.LeftCtrl || key == System.Windows.Input.Key.RightCtrl ||
                key == System.Windows.Input.Key.LeftAlt || key == System.Windows.Input.Key.RightAlt ||
                key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
                key == System.Windows.Input.Key.LWin || key == System.Windows.Input.Key.RWin)
            {
                e.Handled = true;
                return;
            }

            string gestureText = BuildGestureText(key, mods);

            switch (_captureTarget)
            {
                case "CreateSection": HkCreateSection.Text = gestureText; break;
                case "CreateDocument": HkCreateDocument.Text = gestureText; break;
                case "NavPrev": HkNavPrev.Text = gestureText; break;
                case "NavNext": HkNavNext.Text = gestureText; break;
                case "NavParent": HkNavParent.Text = gestureText; break;
                case "NavChild": HkNavChild.Text = gestureText; break;
            }

            _captureTarget = null;
            e.Handled = true;
        }

        private string BuildGestureText(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys mods)
        {
            var sb = new StringBuilder();
            if (mods.HasFlag(System.Windows.Input.ModifierKeys.Control)) sb.Append("Ctrl+");
            if (mods.HasFlag(System.Windows.Input.ModifierKeys.Alt)) sb.Append("Alt+");
            if (mods.HasFlag(System.Windows.Input.ModifierKeys.Shift)) sb.Append("Shift+");
            if (mods.HasFlag(System.Windows.Input.ModifierKeys.Windows)) sb.Append("Windows+");

            string keyText;
            if (key >= System.Windows.Input.Key.A && key <= System.Windows.Input.Key.Z)
                keyText = key.ToString();
            else if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9)
                keyText = ((char)('0' + (key - System.Windows.Input.Key.D0))).ToString();
            else if (key >= System.Windows.Input.Key.NumPad0 && key <= System.Windows.Input.Key.NumPad9)
                keyText = "NumPad" + (key - System.Windows.Input.Key.NumPad0);
            else
                keyText = key.ToString();

            return sb.ToString() + keyText;
        }
    }
}