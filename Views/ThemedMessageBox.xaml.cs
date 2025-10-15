using System.Windows;

namespace YellowBlackEditor.Views
{
    public partial class ThemedMessageBox : Window
    {
        public ThemedMessageBox(string title, string message, MessageBoxImage image, bool showCancel)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;

            IconText.Text = image switch
            {
                MessageBoxImage.Error => "❌",
                MessageBoxImage.Warning => "⚠️",
                MessageBoxImage.Information => "ℹ️",
                MessageBoxImage.Question => "❓",
                _ => "ℹ️"
            };

            CancelBtn.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        public static bool? Show(Window owner, string title, string message, MessageBoxImage image = MessageBoxImage.Information, bool showCancel = false)
        {
            var dlg = new ThemedMessageBox(title, message, image, showCancel) { Owner = owner };
            return dlg.ShowDialog();
        }
    }
}