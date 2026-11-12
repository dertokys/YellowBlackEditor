using System.Windows;

namespace YellowBlackEditor.Views
{
    public partial class ThemedMessageBox : Window
    {
        public ThemedMessageBox(string header, string message, MessageBoxImage image, string okText, string? cancelText)
        {
            InitializeComponent();
            SourceInitialized += (_, _) => YellowBlackEditor.Utils.DarkTitleBar.Apply(this);
            HeaderText.Text = header;
            MessageText.Text = message;
            MessageText.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;

            IconText.Text = image switch
            {
                MessageBoxImage.Error => "",
                MessageBoxImage.Warning => "",
                MessageBoxImage.Question => "",
                _ => ""
            };
            IconText.Foreground = image == MessageBoxImage.Error
                ? (System.Windows.Media.Brush)FindResource("DangerBrush")
                : (System.Windows.Media.Brush)FindResource("SubTextBrush");

            OkBtn.Content = okText;
            if (cancelText != null)
            {
                CancelBtn.Content = cancelText;
                CancelBtn.Visibility = Visibility.Visible;
                if (image == MessageBoxImage.Warning)
                {
                    OkBtn.IsDefault = false;
                    CancelBtn.IsDefault = true;
                }
            }
            else
            {
                OkBtn.IsCancel = true;
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        public static void Show(Window? owner, string header, string message, MessageBoxImage image = MessageBoxImage.Information)
        {
            var dlg = new ThemedMessageBox(header, message, image, "Понятно", null) { Owner = owner };
            dlg.ShowDialog();
        }

        public static bool Confirm(Window? owner, string header, string message, string okText, string cancelText = "Отмена",
            MessageBoxImage image = MessageBoxImage.Question)
        {
            var dlg = new ThemedMessageBox(header, message, image, okText, cancelText) { Owner = owner };
            return dlg.ShowDialog() == true;
        }
    }
}
