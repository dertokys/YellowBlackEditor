using System.IO;
using System.Windows;
using System.Windows.Documents;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;
using WF = System.Windows.Forms;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private const string InboxSectionName = "Входящие";
        private List<Transfer> _inbox = new();
        private NodeViewModel? _transferSelect;
        private bool _balloonTransfer;

        private void TransferButton_Click(object sender, RoutedEventArgs e) => OpenTransfer(null, _inbox.Count > 0);

        private void OpenTransfer(NodeViewModel? node, bool inbox)
        {
            if (!SharingAvailable(out var exchange)) return;
            SaveAll(quiet: true);
            node ??= _currentDocNode ?? _currentClientNode;
            NodeViewModel? note = node?.Type == NodeType.Document ? node : null;
            NodeViewModel? client = node?.Type == NodeType.Client ? node : null;
            for (var p = note?.Parent; client == null && p != null; p = p.Parent)
                if (p.Type == NodeType.Client) client = p;

            var selected = "";
            if (note != null && ReferenceEquals(note, _currentDocNode))
                selected = (Editor.Selection?.Text ?? "").Trim();

            if (_people.Count == 0)
            {
                try { _people = SharedProjects.LoadPeople(exchange); }
                catch { }
            }

            var ctx = new TransferWindow.Context
            {
                Exchange = exchange,
                MyName = _appSettings.DisplayName,
                NoteFolder = note?.FullPath,
                NoteName = note?.Name,
                NoteClient = note == null ? "" : TimeStore.Describe(_workspacePath, note.FullPath).Client,
                ClientFolder = client?.FullPath,
                ClientName = client?.Name,
                SelectedText = selected,
                People = _people,
                Accept = AcceptTransfer
            };
            _transferSelect = null;
            var w = new TransferWindow(ctx, inbox) { Owner = this };
            w.ShowDialog();
            if (w.Changed)
            {
                try { UpdateInbox(Transfers.LoadInbox(exchange, AppSettings.UserId), notify: false); }
                catch { }
            }
            if (_transferSelect != null) SelectNode(_transferSelect);
        }

        private NodeViewModel EnsureInboxSection()
        {
            var section = RootNodes.FirstOrDefault(n => !n.IsVirtual && n.Type == NodeType.Section &&
                                                       n.Name.Equals(InboxSectionName, StringComparison.CurrentCultureIgnoreCase));
            if (section != null) return section;
            var dir = Path.Combine(_workspacePath, InboxSectionName);
            Directory.CreateDirectory(dir);
            section = new NodeViewModel { Name = InboxSectionName, FullPath = dir, Type = NodeType.Section };
            RootNodes.Add(section);
            return section;
        }

        private NodeViewModel AddFolderNode(NodeViewModel parent, string dir, NodeType type)
        {
            var node = new NodeViewModel { Name = Path.GetFileName(dir), FullPath = dir, Type = type, Parent = parent };
            parent.Children.Insert(InsertIndex(parent, type, node.Name), node);
            if (type == NodeType.Document) LoadAttachments(node);
            parent.IsExpanded = true;
            return node;
        }

        private string AcceptTransfer(Transfer t)
        {
            var payload = Transfers.PayloadDir(t);
            var title = SanitizeName(string.IsNullOrWhiteSpace(t.Title) ? t.KindTitle : t.Title);
            switch (t.Kind)
            {
                case Transfer.KindNote:
                {
                    var parent = (t.ClientName.Length > 0
                                     ? Flatten(RootNodes.Where(n => !n.IsVirtual)).FirstOrDefault(n =>
                                         n.Type == NodeType.Client && n.Name.Equals(t.ClientName, StringComparison.CurrentCultureIgnoreCase))
                                     : null)
                                 ?? EnsureInboxSection();
                    var dir = Path.Combine(parent.FullPath, MakeUniqueName(parent.FullPath, title));
                    if (Directory.Exists(payload)) Transfers.CopyDirectory(payload, dir);
                    else Directory.CreateDirectory(dir);
                    EnsureContentExists(dir);
                    _transferSelect = AddFolderNode(parent, dir, NodeType.Document);
                    return $"Заметка сохранена в «{parent.Name}»";
                }
                case Transfer.KindClient:
                {
                    Directory.CreateDirectory(ClientsRoot);
                    var section = RootNodes.FirstOrDefault(n => !n.IsVirtual && n.FullPath.Equals(ClientsRoot, StringComparison.OrdinalIgnoreCase));
                    if (section == null)
                    {
                        section = new NodeViewModel { Name = ClientsSectionName, FullPath = ClientsRoot, Type = NodeType.Section };
                        RootNodes.Add(section);
                    }
                    var name = Directory.Exists(Path.Combine(ClientsRoot, title)) ? $"{title} (от {SanitizeName(t.FromName)})" : title;
                    var dir = Path.Combine(ClientsRoot, MakeUniqueName(ClientsRoot, name));
                    Transfers.CopyDirectory(payload, dir);
                    if (!ClientStore.IsClientFolder(dir)) ClientStore.Save(dir, new ClientCard());
                    _transferSelect = AddFolderNode(section, dir, NodeType.Client);
                    SyncSoon();
                    return $"Клиент «{Path.GetFileName(dir)}» добавлен";
                }
                case Transfer.KindFile:
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Куда сохранить файл",
                        FileName = t.FileName,
                        InitialDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
                    };
                    if (dlg.ShowDialog(this) != true) return "";
                    File.Copy(Path.Combine(payload, t.FileName), dlg.FileName, true);
                    return "Файл сохранён: " + dlg.FileName;
                }
                default:
                {
                    var parent = EnsureInboxSection();
                    var dir = Path.Combine(parent.FullPath, MakeUniqueName(parent.FullPath, title));
                    Directory.CreateDirectory(dir);
                    var doc = new FlowDocument();
                    doc.Blocks.Add(new Paragraph(new Run($"От {t.FromName}, {DateText.Human(t.CreatedAt.ToLocalTime())}")) { Foreground = System.Windows.Media.Brushes.Gray });
                    foreach (var line in (t.Text ?? "").Replace("\r\n", "\n").Split('\n'))
                        doc.Blocks.Add(new Paragraph(new Run(line)));
                    SaveFlowDocumentAsXamlPackage(doc, GetXamlPackPath(dir));
                    _transferSelect = AddFolderNode(parent, dir, NodeType.Document);
                    return $"Текст сохранён заметкой в «{InboxSectionName}»";
                }
            }
        }

        private void UpdateInbox(List<Transfer> inbox, bool notify)
        {
            _inbox = inbox.Where(t => t.IsNew).ToList();
            TransferBadge.Visibility = _inbox.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            TransferCount.Text = _inbox.Count.ToString();
            TransferBtn.ToolTip = _inbox.Count > 0
                ? $"Передать коллеге. Входящих: {_inbox.Count}"
                : "Передать коллеге заметку, клиента, текст или файл. Здесь же входящие";

            var fresh = _inbox.Where(t => !_appSettings.SeenTransfers.Contains(t.Id)).ToList();
            if (fresh.Count == 0) return;
            foreach (var t in fresh) _appSettings.SeenTransfers.Add(t.Id);
            if (_appSettings.SeenTransfers.Count > 300) _appSettings.SeenTransfers.RemoveRange(0, _appSettings.SeenTransfers.Count - 300);
            _appSettings.Save();
            if (!notify) return;

            var first = fresh[0];
            var text = fresh.Count == 1
                ? $"Вам передали {first.KindTitle.ToLower()} «{first.Title}» от {first.FromName}"
                : $"Вам передали: {fresh.Count}. Первое от {first.FromName}: «{first.Title}»";
            Status(text);
            try
            {
                _lastBalloonIsReminder = false;
                _balloonSharedId = null;
                _balloonTransfer = true;
                _tray?.ShowBalloonTip(8000, "Передали вам", text, WF.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                LogError("Не удалось показать уведомление", ex);
            }
        }

        private void OpenBalloonTransfer()
        {
            _balloonTransfer = false;
            BringToFront();
            OpenTransfer(null, true);
        }
    }
}
