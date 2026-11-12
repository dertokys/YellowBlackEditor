using System.IO;
using System.Windows;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;

namespace YellowBlackEditor.Views
{
    public partial class TransferWindow : Window
    {
        public sealed class Context
        {
            public string Exchange { get; init; } = "";
            public string MyName { get; init; } = "";
            public string? NoteFolder { get; init; }
            public string? NoteName { get; init; }
            public string NoteClient { get; init; } = "";
            public string? ClientFolder { get; init; }
            public string? ClientName { get; init; }
            public string SelectedText { get; init; } = "";
            public List<SharedPerson> People { get; init; } = new();
            public Func<Transfer, string>? Accept { get; init; }
        }

        public sealed record PersonItem(string Id, string Name, string Sub);
        public sealed record InboxItem(Transfer T, string Glyph, string Title, string Sub, string Message, Visibility MessageVisibility,
            string Preview, Visibility PreviewVisibility, string AcceptText);
        public sealed record SentItem(string Glyph, string Title, string Sub, string State);

        private readonly Context _ctx;
        private string _kind = Transfer.KindText;
        private string? _file;

        public bool Changed { get; private set; }

        public TransferWindow(Context ctx, bool openInbox)
        {
            InitializeComponent();
            SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
            _ctx = ctx;

            KindNote.Content = Padded(ctx.NoteName != null ? $"Заметку «{Short(ctx.NoteName)}»" : "Заметку");
            KindNote.IsEnabled = ctx.NoteFolder != null;
            KindClient.Content = Padded(ctx.ClientName != null ? $"Клиента «{Short(ctx.ClientName)}»" : "Клиента");
            KindClient.IsEnabled = ctx.ClientFolder != null;
            TextBox.Text = ctx.SelectedText;

            var people = ctx.People.Where(p => p.Id != AppSettings.UserId)
                .GroupBy(p => p.Id).Select(g => g.OrderByDescending(x => x.LastSeen).First())
                .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(p => new PersonItem(p.Id, string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name,
                    "компьютер " + p.Machine + ", был в программе " + DateText.Human(p.LastSeen.ToLocalTime())))
                .ToList();
            PeopleList.ItemsSource = people;
            NobodyText.Visibility = people.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            SetKind(ctx.SelectedText.Length > 0 ? Transfer.KindText : ctx.NoteFolder != null ? Transfer.KindNote : ctx.ClientFolder != null ? Transfer.KindClient : Transfer.KindText);
            RefreshInbox();
            RefreshSent();
            ShowTab(openInbox ? 1 : 0);
        }

        private static string Short(string s) => s.Length > 28 ? s[..27] + "…" : s;

        private static System.Windows.Controls.TextBlock Padded(string text) => new() { Text = text, Margin = new Thickness(14, 0, 14, 0) };

        private void ShowTab(int i)
        {
            SendTab.IsChecked = i == 0;
            InboxTab.IsChecked = i == 1;
            SentTab.IsChecked = i == 2;
            SendPanel.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            InboxPanel.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
            SentPanel.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (i == 2) RefreshSent();
        }

        private void SendTab_Click(object sender, RoutedEventArgs e) => ShowTab(0);
        private void InboxTab_Click(object sender, RoutedEventArgs e) => ShowTab(1);
        private void SentTab_Click(object sender, RoutedEventArgs e) => ShowTab(2);

        private void SetKind(string kind)
        {
            _kind = kind;
            KindNote.IsChecked = kind == Transfer.KindNote;
            KindClient.IsChecked = kind == Transfer.KindClient;
            KindText.IsChecked = kind == Transfer.KindText;
            KindFile.IsChecked = kind == Transfer.KindFile;
            TextPanel.Visibility = kind == Transfer.KindText ? Visibility.Visible : Visibility.Collapsed;
            FilePanel.Visibility = kind == Transfer.KindFile ? Visibility.Visible : Visibility.Collapsed;
            KindHint.Text = kind switch
            {
                Transfer.KindNote => "Коллега получит копию заметки вместе с картинками. Ваша заметка останется у вас без изменений.",
                Transfer.KindClient => "Коллега получит копию карточки: реквизиты, контакты, точки, доступы, ПО и договоры. Заметки, проекты и время не передаются.",
                Transfer.KindFile => "Любой файл до 200 МБ. Коллега сохранит его, куда захочет.",
                _ => "Коллега получит текст и сможет сохранить его заметкой или скопировать."
            };
            SendStatus.Text = "";
        }

        private void Kind_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: string k }) SetKind(k);
        }

        private void PickFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Какой файл передать" };
            if (dlg.ShowDialog(this) != true) return;
            var info = new FileInfo(dlg.FileName);
            if (info.Length > 200L * 1024 * 1024)
            {
                SendStatus.Text = "Файл больше 200 МБ, такой лучше передать иначе";
                return;
            }
            _file = dlg.FileName;
            FileText.Text = info.Name + $" ({info.Length / 1024.0 / 1024.0:0.#} МБ)";
            SendStatus.Text = "";
        }

        private void PeopleList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => SendStatus.Text = "";

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            if (PeopleList.SelectedItem is not PersonItem to)
            {
                SendStatus.Text = "Выберите, кому передать";
                return;
            }
            var t = new Transfer
            {
                FromId = AppSettings.UserId,
                FromName = _ctx.MyName,
                Kind = _kind,
                Message = MessageBox.Text.Trim()
            };
            Action<string> fill;
            switch (_kind)
            {
                case Transfer.KindNote when _ctx.NoteFolder != null:
                    t.Title = _ctx.NoteName ?? "Заметка";
                    t.ClientName = _ctx.NoteClient;
                    var noteFolder = _ctx.NoteFolder;
                    fill = dir =>
                    {
                        foreach (var name in new[] { "content.xamlpack", "content.rtf" })
                        {
                            var f = Path.Combine(noteFolder, name);
                            if (File.Exists(f)) File.Copy(f, Path.Combine(dir, name), true);
                        }
                        var assets = Path.Combine(noteFolder, "_assets");
                        if (Directory.Exists(assets)) Transfers.CopyDirectory(assets, Path.Combine(dir, "_assets"));
                    };
                    break;
                case Transfer.KindClient when _ctx.ClientFolder != null:
                    t.Title = _ctx.ClientName ?? "Клиент";
                    t.ClientName = t.Title;
                    var clientFolder = _ctx.ClientFolder;
                    fill = dir =>
                    {
                        foreach (var name in new[] { ClientStore.FileName, ClientStore.SoftwareFileName, ClientStore.ContractsFileName })
                        {
                            var f = Path.Combine(clientFolder, name);
                            if (File.Exists(f)) File.Copy(f, Path.Combine(dir, name), true);
                        }
                    };
                    break;
                case Transfer.KindFile:
                    if (_file == null)
                    {
                        SendStatus.Text = "Выберите файл";
                        return;
                    }
                    t.Title = Path.GetFileName(_file);
                    t.FileName = Path.GetFileName(_file);
                    var file = _file;
                    fill = dir => File.Copy(file, Path.Combine(dir, Path.GetFileName(file)), true);
                    break;
                default:
                    if (TextBox.Text.Trim().Length == 0)
                    {
                        SendStatus.Text = "Напишите текст";
                        return;
                    }
                    t.Kind = Transfer.KindText;
                    t.Text = TextBox.Text;
                    var firstLine = TextBox.Text.Trim().Split('\n')[0].Trim();
                    t.Title = firstLine.Length > 60 ? firstLine[..59] + "…" : firstLine;
                    fill = _ => { };
                    break;
            }
            try
            {
                SendBtn.IsEnabled = false;
                Transfers.Send(_ctx.Exchange, t, to.Id, to.Name, fill);
                SendStatus.Text = $"Отправлено: {to.Name} получит «{Short(t.Title)}» при следующем обмене";
                MessageBox.Text = "";
                Changed = true;
                RefreshSent();
            }
            catch (Exception ex)
            {
                SendStatus.Text = "Не удалось отправить: " + ex.Message;
            }
            finally
            {
                SendBtn.IsEnabled = true;
            }
        }

        private void RefreshInbox()
        {
            List<Transfer> inbox;
            try { inbox = Transfers.LoadInbox(_ctx.Exchange, AppSettings.UserId).Where(t => t.IsNew).ToList(); }
            catch { inbox = new(); }
            InboxList.ItemsSource = inbox.Select(t => new InboxItem(t, t.Glyph, string.IsNullOrWhiteSpace(t.Title) ? t.KindTitle : t.Title,
                $"{t.KindTitle} от {t.FromName}, {DateText.Human(t.CreatedAt.ToLocalTime())}" + (t.ClientName.Length > 0 && t.Kind == Transfer.KindNote ? $" · клиент {t.ClientName}" : ""),
                t.Message, string.IsNullOrWhiteSpace(t.Message) ? Visibility.Collapsed : Visibility.Visible,
                t.Text, t.Kind == Transfer.KindText ? Visibility.Visible : Visibility.Collapsed,
                t.Kind switch
                {
                    Transfer.KindFile => "Сохранить файл…",
                    Transfer.KindClient => "Добавить клиента",
                    _ => "Сохранить к себе"
                })).ToList();
            InboxEmpty.Visibility = inbox.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            InboxTabText.Text = inbox.Count > 0 ? $"Входящие ({inbox.Count})" : "Входящие";
        }

        private void RefreshSent()
        {
            List<Transfer> sent;
            try { sent = Transfers.LoadSent(_ctx.Exchange, AppSettings.UserId); }
            catch { sent = new(); }
            SentList.ItemsSource = sent.Select(t => new SentItem(t.Glyph, string.IsNullOrWhiteSpace(t.Title) ? t.KindTitle : t.Title,
                $"{t.KindTitle} → {t.ToName}, {DateText.Human(t.CreatedAt.ToLocalTime())}", t.StateText)).ToList();
            SentEmpty.Visibility = sent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not InboxItem item || _ctx.Accept == null) return;
            try
            {
                var msg = _ctx.Accept(item.T);
                if (msg.Length == 0) return;
                Transfers.SetState(item.T, "accepted");
                InboxStatus.Text = msg;
                Changed = true;
            }
            catch (Exception ex)
            {
                InboxStatus.Text = "Не удалось сохранить: " + ex.Message;
            }
            RefreshInbox();
        }

        private void Decline_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not InboxItem item) return;
            if (!ThemedMessageBox.Confirm(this, $"Удалить «{item.Title}»?", $"{item.T.FromName} увидит, что вы удалили передачу.", "Удалить")) return;
            try
            {
                Transfers.SetState(item.T, "declined");
                var payload = Transfers.PayloadDir(item.T);
                if (Directory.Exists(payload)) Directory.Delete(payload, true);
                Changed = true;
            }
            catch (Exception ex)
            {
                InboxStatus.Text = "Не удалось удалить: " + ex.Message;
            }
            RefreshInbox();
        }
    }
}
