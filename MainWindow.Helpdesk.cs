using System.IO;
using System.Windows;
using YellowBlackEditor.Models;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private const string HelpdeskPath = "::helpdesk";
        private readonly NodeViewModel _helpdeskNode = new() { Name = "Заявки", FullPath = HelpdeskPath, Type = NodeType.Helpdesk };
        private bool _helpdeskShown;

        private string HelpdeskUrl => HelpdeskView.Normalize(_appSettings.HelpdeskUrl);

        private void InitHelpdesk()
        {
            HelpdeskPanel.Failed += ex => LogError("Заявки", ex);
        }

        private void EnsureHelpdeskNode()
        {
            var want = HelpdeskUrl.Length > 0 && Directory.Exists(_workspacePath);
            if (want == RootNodes.Contains(_helpdeskNode)) return;
            if (want)
            {
                _helpdeskNode.IsSelected = false;
                RootNodes.Insert(VirtualIndex(_helpdeskNode), _helpdeskNode);
            }
            else
            {
                if (_helpdeskNode.IsSelected && RootNodes.Contains(_todayNode)) SelectNode(_todayNode);
                RootNodes.Remove(_helpdeskNode);
            }
        }

        private void ShowHelpdesk(string? route = null)
        {
            ClientView.Clear();
            ClientView.Visibility = Visibility.Collapsed;
            EditorHost.Visibility = Visibility.Collapsed;
            FormatBar.IsEnabled = false;
            RenameBtn.Visibility = Visibility.Collapsed;
            HeaderBar.Visibility = Visibility.Collapsed;
            HelpdeskPanel.Visibility = Visibility.Visible;
            System.Windows.Controls.Grid.SetRow(HelpdeskPanel, 0);
            System.Windows.Controls.Grid.SetRowSpan(HelpdeskPanel, 2);
            _helpdeskShown = true;
            CurrentKindText.Text = "";
            CurrentFileText.Text = "";
            DirtyText.Text = "";
            HelpdeskPanel.Open(HelpdeskUrl, route);
        }

        private void HideHelpdesk()
        {
            if (!_helpdeskShown) return;
            _helpdeskShown = false;
            HelpdeskPanel.Visibility = Visibility.Collapsed;
            HeaderBar.Visibility = Visibility.Visible;
        }

        private void OpenClientTickets()
        {
            if (_currentClientNode == null || HelpdeskUrl.Length == 0) return;
            var query = ClientTicketsQuery(_currentClientNode);
            EnsureHelpdeskNode();
            SelectNode(_helpdeskNode);
            if (_helpdeskShown) HelpdeskPanel.Open(HelpdeskUrl, HelpdeskView.SearchRoute(query));
        }

        private static string ClientTicketsQuery(NodeViewModel client)
        {
            try
            {
                var inn = new string((Utils.ClientStore.Load(client.FullPath).Inn ?? "").Where(char.IsDigit).ToArray());
                if (inn.Length is 10 or 12) return inn;
            }
            catch { }
            var name = client.Name;
            var q = name.Replace("\"", " ").Replace("«", " ").Replace("»", " ");
            foreach (var form in new[] { "ООО ", "ИП ", "АО ", "ПАО ", "ЗАО ", "ОАО " })
                if (q.TrimStart().StartsWith(form, StringComparison.OrdinalIgnoreCase)) q = q.TrimStart()[form.Length..];
            q = string.Join(" ", q.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return q.Length > 0 ? q : name;
        }

        private void UpdateTicketsButton()
        {
            ClientTicketsBtn.Visibility = _currentClientNode != null && HelpdeskUrl.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClientTickets_Click(object sender, RoutedEventArgs e) => OpenClientTickets();
    }
}
