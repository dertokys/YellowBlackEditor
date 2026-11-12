using System;
using System.Windows;

namespace YellowBlackEditor.Views
{
    public partial class TextPromptWindow : Window
    {
        public string Prompt { get; set; } = "Название";
        public string InputText { get; set; } = "";
        public Func<string?>? GetSelection { get; set; }

        public TextPromptWindow(string prompt, string initial = "", double? width = null, Func<string?>? getSelection = null, string okText = "Создать")
        {
            InitializeComponent();
            SourceInitialized += (_, _) => YellowBlackEditor.Utils.DarkTitleBar.Apply(this);
            DataContext = this;
            Prompt = prompt;
            InputText = initial;
            GetSelection = getSelection;
            OkBtn.Content = okText;

            if (width.HasValue) this.Width = width.Value;


            Loaded += (s, e) =>
            {
                InputBox.Text = InputText;
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            InputText = ExpandMacros(InputBox.Text ?? "");
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Ok_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape)
            {
                Cancel_Click(sender, e);
                e.Handled = true;
            }
        }

        private void FromSelection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string? t = GetSelection?.Invoke();
                if (!string.IsNullOrWhiteSpace(t))
                {
                    InputBox.Text = MakeInline(t!);
                    InputBox.CaretIndex = InputBox.Text.Length;
                }
            }
            catch { }
        }

        private void FromClipboard_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (System.Windows.Clipboard.ContainsText())
                {
                    var t = System.Windows.Clipboard.GetText() ?? "";
                    InputBox.Text = MakeInline(t);
                    InputBox.CaretIndex = InputBox.Text.Length;
                }
            }
            catch { }
        }

        private static string MakeInline(string s)
        {
            s = s.Replace("\r", " ").Replace("\n", " ");
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Trim();
        }

        private static string ExpandMacros(string s)
        {
            var now = DateTime.Now;
            return s.Replace("{date}", now.ToString("yyyy-MM-dd"))
                    .Replace("{time}", now.ToString("HH-mm-ss"))
                    .Replace("{datetime}", now.ToString("yyyy-MM-dd HH-mm-ss"));
        }

        public static string? Show(Window owner, string prompt, string initial = "", double? width = null, Func<string?>? getSelection = null, string okText = "Создать")
        {
            var dlg = new TextPromptWindow(prompt, initial, width, getSelection, okText) { Owner = owner };
            return dlg.ShowDialog() == true ? dlg.InputText : null;
        }
    }
}