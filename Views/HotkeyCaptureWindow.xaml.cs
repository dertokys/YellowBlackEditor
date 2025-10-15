using System.Text;
using System.Windows;
using System.Windows.Input;

namespace YellowBlackEditor.Views
{
    public partial class HotkeyCaptureWindow : Window
    {
        public string? CapturedGesture { get; private set; }

        public HotkeyCaptureWindow()
        {
            InitializeComponent();

            this.PreviewKeyDown += new System.Windows.Input.KeyEventHandler(this.OnPreviewKeyDown);
            this.Loaded += (s, e) => this.Focus();
        }

        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                this.DialogResult = false;
                return;
            }

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

            this.CapturedGesture = BuildGestureText(key, mods);
            this.DialogResult = true; 
            e.Handled = true;
        }

        private static string BuildGestureText(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys mods)
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