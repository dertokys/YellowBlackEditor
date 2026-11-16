using System.IO;
using System.Windows;
using System.Windows.Threading;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;
using WF = System.Windows.Forms;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private const string ChatPath = "::chat";
        private const string ChatBalloonTitle = "Новое сообщение";
        private readonly NodeViewModel _chatNode = new() { Name = "Сообщения", FullPath = ChatPath, Type = NodeType.Chat };
        private readonly List<ChatMessage> _chatMessages = new();
        private readonly HashSet<string> _chatIds = new();
        private readonly HashSet<string> _chatFiles = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, DateTime> _chatRead = new();
        private DispatcherTimer? _chatTimer;
        private bool _chatShown;
        private bool _chatScanning;
        private bool _chatBaseline;
        private string _chatExchange = "";
        private string? _balloonChat;

        private string ChatExchange => _appSettings.SyncEnabled && !string.IsNullOrWhiteSpace(_appSettings.SyncFolder) ? _appSettings.SyncFolder : "";

        private void InitChat()
        {
            _chatRead = ChatStore.LoadRead();
            ChatPanel.SendRequested += SendChat;
            ChatPanel.LinkClicked += OpenChatLink;
            ChatPanel.ChatSelected += key =>
            {
                MarkChatRead(key);
                RenderChat();
                ChatPanel.FocusInput();
            };
            ChatPanel.SettingsRequested += () => OpenSettings_Click(this, new RoutedEventArgs());
            HelpdeskPanel.ShareRequested += ShareToChat;
            Activated += (_, _) =>
            {
                if (!_chatShown || ChatPanel.CurrentKey == null) return;
                MarkChatRead(ChatPanel.CurrentKey);
                RenderChat();
            };
            _chatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            _chatTimer.Tick += (_, _) => ScanChat();
            _chatTimer.Start();
            ScanChat();
        }

        private void EnsureChatNode()
        {
            var want = Directory.Exists(_workspacePath);
            if (want == RootNodes.Contains(_chatNode)) return;
            if (want)
            {
                _chatNode.IsSelected = false;
                RootNodes.Insert(VirtualIndex(_chatNode), _chatNode);
            }
            else
            {
                if (_chatNode.IsSelected && RootNodes.Contains(_todayNode)) SelectNode(_todayNode);
                RootNodes.Remove(_chatNode);
            }
        }

        private void ShowChat()
        {
            ClientView.Clear();
            ClientView.Visibility = Visibility.Collapsed;
            EditorHost.Visibility = Visibility.Collapsed;
            FormatBar.IsEnabled = false;
            RenameBtn.Visibility = Visibility.Collapsed;
            HeaderBar.Visibility = Visibility.Collapsed;
            ChatPanel.Visibility = Visibility.Visible;
            System.Windows.Controls.Grid.SetRow(ChatPanel, 0);
            System.Windows.Controls.Grid.SetRowSpan(ChatPanel, 2);
            _chatShown = true;
            CurrentKindText.Text = "";
            CurrentFileText.Text = "";
            DirtyText.Text = "";
            RenderChat();
            if (ChatPanel.CurrentKey != null)
            {
                MarkChatRead(ChatPanel.CurrentKey);
                RenderChat();
            }
            ChatPanel.FocusInput();
            ScanChat();
        }

        private void HideChat()
        {
            if (!_chatShown) return;
            _chatShown = false;
            ChatPanel.Visibility = Visibility.Collapsed;
            HeaderBar.Visibility = Visibility.Visible;
        }

        private void ScanChat()
        {
            var exchange = ChatExchange;
            if (exchange != _chatExchange)
            {
                _chatExchange = exchange;
                _chatMessages.Clear();
                _chatIds.Clear();
                _chatFiles.Clear();
                _chatBaseline = false;
                RenderChat();
            }
            if (exchange.Length == 0 || _chatScanning) return;
            _chatScanning = true;
            var known = new HashSet<string>(_chatFiles, StringComparer.OrdinalIgnoreCase);
            var me = AppSettings.UserId;
            var needPeople = _people.Count == 0;
            Task.Run(() =>
            {
                List<SharedPerson>? people = null;
                if (needPeople)
                {
                    try { people = SharedProjects.LoadPeople(exchange); }
                    catch { }
                }
                return (Found: ChatStore.Scan(exchange, me, known), People: people);
            }).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                _chatScanning = false;
                if (t.Exception != null)
                {
                    LogError("Сообщения", t.Exception.GetBaseException());
                    return;
                }
                if (exchange != _chatExchange) return;
                var (found, people) = t.Result;
                if (people is { Count: > 0 } && _people.Count == 0) _people = people;
                var fresh = new List<ChatMessage>();
                foreach (var (file, m) in found)
                {
                    _chatFiles.Add(file);
                    if (!_chatIds.Add(m.Id)) continue;
                    _chatMessages.Add(m);
                    fresh.Add(m);
                }
                var notify = _chatBaseline;
                _chatBaseline = true;
                if (fresh.Count == 0 && notify && people == null) return;
                _chatMessages.Sort((a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
                if (_chatShown && IsActive && ChatPanel.CurrentKey != null) MarkChatRead(ChatPanel.CurrentKey);
                RenderChat();
                if (notify) NotifyChat(fresh.Where(m => m.FromId != me && IsUnread(m)).ToList());
            }));
        }

        private bool IsUnread(ChatMessage m) =>
            m.FromId != AppSettings.UserId && m.CreatedAt > _chatRead.GetValueOrDefault(m.Chat, DateTime.MinValue);

        private void MarkChatRead(string key)
        {
            var last = _chatMessages.Where(m => m.Chat == key).Select(m => m.CreatedAt).DefaultIfEmpty(DateTime.MinValue).Max();
            if (last <= _chatRead.GetValueOrDefault(key, DateTime.MinValue)) return;
            _chatRead[key] = last;
            ChatStore.SaveRead(_chatRead);
        }

        private static string Plural(int n, string one, string few, string many)
        {
            var m10 = n % 10;
            var m100 = n % 100;
            return m100 is > 10 and < 20 ? many : m10 == 1 ? one : m10 is > 1 and < 5 ? few : many;
        }

        private static string Preview(ChatMessage m) =>
            !string.IsNullOrWhiteSpace(m.Text) ? m.Text.Replace("\r", " ").Replace("\n", " ").Trim()
            : m.Links.Count > 0 ? m.Links[0].Title
            : "";

        private List<ChatView.ChatInfo> ChatList()
        {
            var me = AppSettings.UserId;
            var list = new List<ChatView.ChatInfo>();
            ChatView.ChatInfo Info(string key, string title, string subtitle)
            {
                var messages = _chatMessages.Where(m => m.Chat == key).ToList();
                var last = messages.LastOrDefault();
                var preview = last == null ? ""
                    : (last.FromId == me ? "Вы: " : key == ChatStore.General ? last.FromName + ": " : "") + Preview(last);
                return new ChatView.ChatInfo(key, title, subtitle, preview, last?.CreatedAt, messages.Count(IsUnread));
            }
            var colleagues = _people.Where(p => p.Id != me).GroupBy(p => p.Id).Select(g => g.OrderByDescending(p => p.LastSeen).First()).ToList();
            list.Add(Info(ChatStore.General, ChatStore.General,
                colleagues.Count == 0 ? "Все, кто подключён к папке обмена" : $"Вы и ещё {colleagues.Count} {Plural(colleagues.Count, "коллега", "коллеги", "коллег")}"));
            var direct = new List<ChatView.ChatInfo>();
            var keys = new HashSet<string>();
            foreach (var p in colleagues)
            {
                var key = ChatStore.DirectKey(me, p.Id);
                if (!keys.Add(key)) continue;
                direct.Add(Info(key, p.Name.Length > 0 ? p.Name : p.Id, "Личная переписка"));
            }
            foreach (var g in _chatMessages.Where(m => ChatStore.IsDirect(m.Chat) && !keys.Contains(m.Chat)).GroupBy(m => m.Chat))
            {
                keys.Add(g.Key);
                var other = g.LastOrDefault(m => m.FromId != me)?.FromName ?? ChatStore.OtherOf(g.Key, me);
                direct.Add(Info(g.Key, other, "Личная переписка"));
            }
            list.AddRange(direct.OrderByDescending(c => c.LastAt ?? DateTime.MinValue).ThenBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase));
            return list;
        }

        private void RenderChat()
        {
            var unread = _chatMessages.Count(IsUnread);
            _chatNode.Hint = unread > 0 ? $"{unread} {Plural(unread, "новое сообщение", "новых сообщения", "новых сообщений")}" : "";
            if (!_chatShown) return;
            if (ChatExchange.Length == 0)
            {
                ChatPanel.SetAvailable(false,
                    "Сообщения ходят через папку обмена, как передачи и общие проекты. Включите обмен и укажите папку в настройках, тогда здесь появятся коллеги и общий чат.");
                return;
            }
            ChatPanel.SetAvailable(true, "");
            ChatPanel.SetChats(ChatList());
            var key = ChatPanel.CurrentKey;
            var messages = key == null ? new List<ChatMessage>() : _chatMessages.Where(m => m.Chat == key).ToList();
            var empty = key == ChatStore.General
                ? "Здесь пока тихо. Напишите первым: сообщение увидят все, кто подключён к папке обмена."
                : "Напишите коллеге. Статью или заявку из Zammad можно отправить кнопкой «Переслать» в разделе «Заявки».";
            ChatPanel.SetMessages(messages, AppSettings.UserId, empty);
        }

        private async void SendChat(string key, string text)
        {
            var (rest, links) = ChatStore.ExtractLinks(text, HelpdeskUrl);
            if (links.Count > 0)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));
                    using var api = await ZammadApi.ConnectAsync(HelpdeskUrl, new System.Windows.Interop.WindowInteropHelper(this).Handle);
                    foreach (var link in links)
                    {
                        var title = await api.LinkTitleAsync(link, cts.Token);
                        if (title.Length > 0) link.Title = title;
                    }
                }
                catch (Exception ex)
                {
                    LogError("Название ссылки", ex);
                }
            }
            PostChat(key, rest, links, null);
        }

        private void PostChat(string key, string text, List<ChatLink> links, string? doneStatus)
        {
            var exchange = ChatExchange;
            if (exchange.Length == 0)
            {
                Status("Сообщение не отправлено: не указана папка обмена");
                return;
            }
            var message = new ChatMessage
            {
                Chat = key,
                FromId = AppSettings.UserId,
                FromName = _appSettings.DisplayName,
                Text = text,
                Links = links
            };
            Task.Run(() => ChatStore.Send(exchange, message)).ContinueWith(t => Dispatcher.BeginInvoke(() =>
            {
                if (t.Exception != null)
                {
                    var ex = t.Exception.GetBaseException();
                    LogError("Сообщения", ex);
                    Status("Сообщение не отправлено: " + ex.Message);
                    return;
                }
                if (exchange != _chatExchange) return;
                _chatFiles.Add(t.Result);
                if (_chatIds.Add(message.Id)) _chatMessages.Add(message);
                MarkChatRead(key);
                RenderChat();
                if (doneStatus != null) Status(doneStatus);
            }));
        }

        private void NotifyChat(List<ChatMessage> fresh)
        {
            fresh = fresh.Where(m => !(IsActive && _chatShown && ChatPanel.CurrentKey == m.Chat)).ToList();
            if (fresh.Count == 0) return;
            var last = fresh[^1];
            var preview = Preview(last);
            if (preview.Length > 120) preview = preview[..120] + "…";
            var where = last.Chat == ChatStore.General ? " в общем чате" : "";
            var text = fresh.Count == 1
                ? $"{last.FromName}{where}: {preview}"
                : $"{fresh.Count} {Plural(fresh.Count, "новое сообщение", "новых сообщения", "новых сообщений")}. Последнее от {last.FromName}: {preview}";
            Status("Сообщения: " + text);
            try
            {
                _balloonChat = last.Chat;
                _tray?.ShowBalloonTip(8000, ChatBalloonTitle, text, WF.ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                LogError("Не удалось показать уведомление", ex);
            }
        }

        private bool TryOpenBalloonChat()
        {
            if (_balloonChat == null || _tray?.BalloonTipTitle != ChatBalloonTitle) return false;
            var key = _balloonChat;
            _balloonChat = null;
            OpenChat(key);
            return true;
        }

        private void OpenChat(string? key)
        {
            BringToFront();
            Dispatcher.BeginInvoke(() => SelectChat(key), DispatcherPriority.ApplicationIdle);
        }

        private void SelectChat(string? key)
        {
            EnsureChatNode();
            if (!RootNodes.Contains(_chatNode)) return;
            if (!_chatNode.IsSelected) SelectNode(_chatNode);
            if (key == null) return;
            if (_chatShown) ChatPanel.Select(key);
            else Dispatcher.BeginInvoke(() => { if (_chatShown) ChatPanel.Select(key); }, DispatcherPriority.Background);
        }

        private void ShareToChat(ChatLink link)
        {
            if (ChatExchange.Length == 0)
            {
                Status("Чтобы пересылать, включите обмен и укажите папку обмена в настройках");
                return;
            }
            var targets = ChatList().Select(c => new ShareWindow.Target(c.Key, c.Title)).ToList();
            var w = new ShareWindow(link, targets, ChatPanel.CurrentKey) { Owner = this };
            if (w.ShowDialog() != true || w.Selected == null) return;
            PostChat(w.Selected.Key, w.Comment, new List<ChatLink> { link }, $"Отправлено: {w.Selected.Title}");
        }

        private void OpenChatLink(ChatLink link)
        {
            var own = Uri.TryCreate(link.Url, UriKind.Absolute, out var u) && Uri.TryCreate(HelpdeskUrl, UriKind.Absolute, out var b) &&
                      u.Host.Equals(b.Host, StringComparison.OrdinalIgnoreCase);
            if (own)
            {
                EnsureHelpdeskNode();
                if (RootNodes.Contains(_helpdeskNode))
                {
                    ShowInHelpdesk(null, link);
                    return;
                }
            }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(link.Url) { UseShellExecute = true }); }
            catch (Exception ex) { Status("Ссылка не открылась: " + ex.Message); }
        }
    }
}
