using System.Windows;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private List<TeamMonth> _teamMonth = new();
        private List<PresenceInfo> _presence = new();
        private readonly Dictionary<NodeViewModel, ClientContracts> _clientContracts = new();
        private string _presenceClient = "";
        private DateTime _presenceSince = DateTime.UtcNow;

        private void LoadClientContracts()
        {
            _clientContracts.Clear();
            foreach (var client in Flatten(RootNodes.Where(n => !n.IsVirtual)).Where(n => n.Type == NodeType.Client).ToList())
            {
                var c = ClientStore.ReadContracts(client.FullPath);
                if (!c.IsEmpty) _clientContracts[client] = c;
            }
        }

        private void UpdateClientContracts(NodeViewModel client, ClientContracts contracts)
        {
            if (contracts.IsEmpty) _clientContracts.Remove(client);
            else _clientContracts[client] = contracts;
        }

        private void SetPresenceClient(string client)
        {
            if (_presenceClient == client) return;
            _presenceClient = client;
            _presenceSince = DateTime.UtcNow;
            if (!_appSettings.SyncEnabled || string.IsNullOrWhiteSpace(_appSettings.SyncFolder)) return;
            var exchange = _appSettings.SyncFolder;
            var name = _appSettings.DisplayName;
            var since = _presenceSince;
            Task.Run(() =>
            {
                try { TeamData.PublishPresence(exchange, name, client, since); }
                catch { }
            });
        }

        private static Dictionary<string, int> MonthMinutesByClient(IEnumerable<TimeRow> rows, DateTime month)
        {
            var start = new DateTime(month.Year, month.Month, 1);
            var end = start.AddMonths(1);
            return rows.Where(r => r.Client.Length > 0 && r.Entry.Date >= start && r.Entry.Date < end)
                .GroupBy(r => r.Client, StringComparer.CurrentCultureIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(r => r.Entry.Minutes), StringComparer.CurrentCultureIgnoreCase);
        }

        private List<(string Name, int Minutes)> OthersMinutes(string client) =>
            _teamMonth.Where(t => t.UserId != AppSettings.UserId)
                .Select(t => (t.Name, Minutes: t.Clients.FirstOrDefault(kv => kv.Key.Equals(client, StringComparison.CurrentCultureIgnoreCase)).Value))
                .Where(x => x.Minutes > 0)
                .OrderByDescending(x => x.Minutes)
                .ToList();

        private string _teamSignature = "";

        private void ApplyTeamData(List<TeamMonth>? month, List<PresenceInfo>? presence)
        {
            if (month != null) _teamMonth = month;
            if (presence != null) _presence = presence;
            UpdatePresenceBadge();
            if (_currentClientNode != null) ClientView.SetTeamTime(OthersMinutes(_currentClientNode.Name));
            var signature = string.Join("|", _teamMonth.OrderBy(t => t.UserId).Select(t => t.UserId + ":" + string.Join(",", t.Clients.Select(kv => kv.Key + "=" + kv.Value))));
            if (signature == _teamSignature) return;
            _teamSignature = signature;
            RefreshTodayIfShown();
        }

        private void ClearPresenceNow()
        {
            _presenceClient = "";
            if (!_appSettings.SyncEnabled || string.IsNullOrWhiteSpace(_appSettings.SyncFolder)) return;
            try { TeamData.PublishPresence(_appSettings.SyncFolder, _appSettings.DisplayName, "", DateTime.UtcNow); }
            catch { }
        }

        private void UpdatePresenceBadge()
        {
            var client = _currentClientNode?.Name ?? (_currentDocNode != null ? TimeStore.Describe(_workspacePath, _currentDocNode.FullPath).Client : "");
            var here = client.Length == 0
                ? new List<PresenceInfo>()
                : _presence.Where(p => p.IsFresh && p.Client.Equals(client, StringComparison.CurrentCultureIgnoreCase)).ToList();
            if (here.Count == 0)
            {
                PresenceBadge.Visibility = Visibility.Collapsed;
                return;
            }
            PresenceBadge.Visibility = Visibility.Visible;
            PresenceText.Text = here.Count == 1
                ? $"Сейчас с клиентом работает {here[0].Name}"
                : "Сейчас с клиентом работают: " + string.Join(", ", here.Select(p => p.Name));
            PresenceBadge.ToolTip = string.Join("\n", here.Select(p => $"{p.Name} — открыл карточку в {p.Since.ToLocalTime():HH:mm}"));
        }

        private List<TodayView.Item> ContractItems(Dictionary<string, int> myMonth)
        {
            var items = new List<TodayView.Item>();
            foreach (var (client, contracts) in _clientContracts.ToList())
            {
                var c = ReferenceEquals(client, _currentClientNode) && _currentClient != null ? _currentClient.Contracts : contracts;
                double hours = 0;
                var names = new List<string>();
                if (c.Spo.Has && c.Spo.HoursValue is { } spo) { hours += spo; names.Add("СПО"); }
                if (c.Ko.Has && c.Ko.HoursValue is { } ko) { hours += ko; names.Add("КО"); }
                if (hours <= 0) continue;
                var used = myMonth.GetValueOrDefault(client.Name) + OthersMinutes(client.Name).Sum(x => x.Minutes);
                var share = used / (hours * 60);
                if (share < 0.8) continue;
                var node = client;
                items.Add(new TodayView.Item(client.Name, string.Join(" + ", names) + (share >= 1 ? " · часы закончились" : " · часы скоро закончатся"),
                    $"{TimeText.Human(used)} из {hours:0.#} ч", share >= 1 ? 2 : 1, () => GoToContracts(node)));
            }
            return items.OrderByDescending(i => i.Level).ThenBy(i => i.Title).ToList();
        }

        private void GoToContracts(NodeViewModel client)
        {
            SelectNode(client);
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(_currentClientNode, client)) ClientView.ShowContracts();
            }, System.Windows.Threading.DispatcherPriority.Background);
        }
    }
}
