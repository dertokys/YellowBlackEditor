using System.Windows;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;

namespace YellowBlackEditor.Views
{
    public partial class ShareWindow : Window
    {
        public sealed record Target(string Key, string Title);

        public Target? Selected => Targets.SelectedItem as Target;
        public string Comment => CommentBox.Text.Trim();

        public ShareWindow(ChatLink link, List<Target> targets, string? preselect)
        {
            InitializeComponent();
            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            LinkTitle.Text = link.Title;
            (LinkGlyph.Text, LinkKind.Text) = link.Kind switch
            {
                "kb" => ("", "Статья базы знаний"),
                "ticket" => ("", "Zammad"),
                _ => ("", "Ссылка")
            };
            Targets.ItemsSource = targets;
            Targets.SelectedItem = targets.FirstOrDefault(t => t.Key == preselect) ?? targets.FirstOrDefault();
            Loaded += (_, _) => Targets.Focus();
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (Selected == null) return;
            DialogResult = true;
        }

        private void Targets_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (Selected != null) DialogResult = true;
        }
    }
}
