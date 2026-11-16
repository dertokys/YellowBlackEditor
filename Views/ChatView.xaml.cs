using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using YellowBlackEditor.Models;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace YellowBlackEditor.Views
{
    public partial class ChatView : UserControl
    {
        public sealed record ChatInfo(string Key, string Title, string Subtitle, string Preview, DateTime? LastAt, int Unread);

        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
        private static readonly Regex UrlRx = new(@"https?://[^\s<>""«»]+", RegexOptions.IgnoreCase);

        public event Action<string, string>? SendRequested;
        public event Action<ChatLink>? LinkClicked;
        public event Action<string>? ChatSelected;
        public event Action? SettingsRequested;

        public string? CurrentKey { get; private set; }
        private List<ChatInfo> _chats = new();
        private string _shownSignature = "";

        public ChatView()
        {
            InitializeComponent();
            SendBtn.IsEnabled = false;
        }

        public void SetAvailable(bool available, string message)
        {
            Body.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
            StatePanel.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
            StateText.Text = message;
        }

        public void SetChats(List<ChatInfo> chats)
        {
            _chats = chats;
            if (CurrentKey == null || chats.All(c => c.Key != CurrentKey)) CurrentKey = chats.FirstOrDefault()?.Key;
            ChatList.Children.Clear();
            foreach (var c in chats) ChatList.Children.Add(ChatItem(c));
            var current = chats.FirstOrDefault(c => c.Key == CurrentKey);
            ChatTitle.Text = current?.Title ?? "";
            ChatSubtitle.Text = current?.Subtitle ?? "";
        }

        public void Select(string key)
        {
            if (_chats.All(c => c.Key != key)) return;
            CurrentKey = key;
            _shownSignature = "";
            SetChats(_chats);
            ChatSelected?.Invoke(key);
        }

        public void FocusInput() => Dispatcher.BeginInvoke(() => InputBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);

        private Border ChatItem(ChatInfo c)
        {
            var on = c.Key == CurrentKey;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var avatar = Avatar(c.Key == Utils.ChatStore.General ? "#" : c.Title, 36, c.Key == Utils.ChatStore.General);
            grid.Children.Add(avatar);
            var text = new StackPanel { Margin = new Thickness(10, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            var title = new TextBlock { Text = c.Title, FontWeight = c.Unread > 0 || on ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
            var preview = new TextBlock { Text = c.Preview.Length > 0 ? c.Preview : c.Subtitle, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            preview.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
            text.Children.Add(title);
            text.Children.Add(preview);
            grid.Children.Add(text);
            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(right, 2);
            if (c.LastAt is { } at)
            {
                var time = new TextBlock { Text = ShortTime(at), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right };
                time.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
                right.Children.Add(time);
            }
            if (c.Unread > 0)
            {
                var badge = new Border { CornerRadius = new CornerRadius(9), MinWidth = 18, Height = 18, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
                badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                badge.Child = new TextBlock { Text = c.Unread > 99 ? "99+" : c.Unread.ToString(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Dark, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                right.Children.Add(badge);
            }
            grid.Children.Add(right);
            var item = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 2), Cursor = Cursors.Hand, Child = grid };
            item.SetResourceReference(Border.BackgroundProperty, on ? "AccentSoftBrush" : "BgBrush");
            if (!on)
            {
                item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "SurfaceHoverBrush");
                item.MouseLeave += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "BgBrush");
            }
            item.MouseLeftButtonUp += (_, _) => Select(c.Key);
            return item;
        }

        private static SolidColorBrush Dark { get; } = new(System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11));

        private static Border Avatar(string name, double size, bool general)
        {
            var letters = general ? "#" : string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpper(w[0])));
            var b = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size / 2), VerticalAlignment = VerticalAlignment.Top };
            b.SetResourceReference(Border.BackgroundProperty, general ? "AccentBrush" : "AccentSoftBrush");
            var t = new TextBlock { Text = letters.Length > 0 ? letters : "?", FontSize = size * 0.36, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (general) t.Foreground = Dark;
            else t.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
            b.Child = t;
            return b;
        }

        private static string ShortTime(DateTime utc)
        {
            var d = utc.ToLocalTime();
            if (d.Date == DateTime.Today) return d.ToString("HH:mm");
            if (d.Date == DateTime.Today.AddDays(-1)) return "вчера";
            return d.ToString(d.Year == DateTime.Today.Year ? "d MMM" : "dd.MM.yy", Ru);
        }

        private static string DayTitle(DateTime local)
        {
            if (local.Date == DateTime.Today) return "Сегодня";
            if (local.Date == DateTime.Today.AddDays(-1)) return "Вчера";
            var s = local.ToString(local.Year == DateTime.Today.Year ? "d MMMM, dddd" : "d MMMM yyyy", Ru);
            return char.ToUpper(s[0]) + s[1..];
        }

        public void SetMessages(List<ChatMessage> messages, string myId, string emptyText)
        {
            var signature = CurrentKey + "|" + string.Join(",", messages.Select(m => m.Id));
            if (signature == _shownSignature) return;
            var atBottom = MessagesScroll.VerticalOffset >= MessagesScroll.ScrollableHeight - 40 || _shownSignature.Length == 0 ||
                           !_shownSignature.StartsWith(CurrentKey + "|", StringComparison.Ordinal);
            _shownSignature = signature;
            MessagesPanel.Children.Clear();
            EmptyText.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = emptyText;
            DateTime? day = null;
            ChatMessage? previous = null;
            foreach (var m in messages)
            {
                var local = m.CreatedAt.ToLocalTime();
                if (day != local.Date)
                {
                    day = local.Date;
                    previous = null;
                    var sep = new TextBlock { Text = DayTitle(local), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 8) };
                    sep.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
                    MessagesPanel.Children.Add(sep);
                }
                var grouped = previous != null && previous.FromId == m.FromId && (m.CreatedAt - previous.CreatedAt).TotalMinutes < 5;
                MessagesPanel.Children.Add(MessageItem(m, m.FromId == myId, grouped));
                previous = m;
            }
            if (atBottom) Dispatcher.BeginInvoke(() => MessagesScroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private FrameworkElement MessageItem(ChatMessage m, bool mine, bool grouped)
        {
            var bubble = new Border
            {
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 8, 12, 8),
                MaxWidth = 560,
                HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                BorderThickness = new Thickness(1)
            };
            bubble.SetResourceReference(Border.BackgroundProperty, mine ? "AccentSoftBrush" : "PanelBrush");
            bubble.SetResourceReference(Border.BorderBrushProperty, mine ? "AccentSoftBrush" : "BorderBrush1");
            var content = new StackPanel();
            if (!mine && !grouped && CurrentKey == Utils.ChatStore.General)
            {
                var who = new TextBlock { Text = m.FromName, FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) };
                who.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextBrush");
                content.Children.Add(who);
            }
            if (!string.IsNullOrWhiteSpace(m.Text)) content.Children.Add(TextWithLinks(m.Text));
            foreach (var link in m.Links) content.Children.Add(LinkCard(link));
            var time = new TextBlock { Text = m.CreatedAt.ToLocalTime().ToString("HH:mm"), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 3, 0, 0) };
            time.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
            content.Children.Add(time);
            bubble.Child = content;

            var row = new Grid { Margin = new Thickness(0, grouped ? 2 : 8, 0, 0) };
            if (mine)
            {
                row.Children.Add(bubble);
                return row;
            }
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (!grouped) row.Children.Add(Avatar(m.FromName, 32, false));
            Grid.SetColumn(bubble, 1);
            row.Children.Add(bubble);
            return row;
        }

        private static FrameworkElement TextWithLinks(string text)
        {
            var box = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 20 };
            var last = 0;
            foreach (Match match in UrlRx.Matches(text))
            {
                if (match.Index > last) box.Inlines.Add(new Run(text[last..match.Index]));
                var url = match.Value;
                var link = new Hyperlink(new Run(url)) { Cursor = Cursors.Hand };
                link.SetResourceReference(TextElement.ForegroundProperty, "AccentTextBrush");
                link.Click += (_, _) =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
                    catch { }
                };
                box.Inlines.Add(link);
                last = match.Index + match.Length;
            }
            if (last < text.Length) box.Inlines.Add(new Run(text[last..]));
            return box;
        }

        private FrameworkElement LinkCard(ChatLink link)
        {
            var (glyph, kind) = link.Kind switch
            {
                "kb" => ("", "Статья базы знаний"),
                "ticket" => ("", "Zammad"),
                _ => ("", "Ссылка")
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            icon.Child = new TextBlock { Text = glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = Dark, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(icon);
            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(text, 1);
            var title = new TextBlock { Text = link.Title.Length > 0 ? link.Title : link.Url, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 60, TextTrimming = TextTrimming.CharacterEllipsis };
            var sub = new TextBlock { Text = kind + " · открыть", FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            sub.SetResourceReference(TextBlock.ForegroundProperty, "SubTextBrush");
            text.Children.Add(title);
            text.Children.Add(sub);
            grid.Children.Add(text);
            var card = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 2), BorderThickness = new Thickness(1), Cursor = Cursors.Hand, Child = grid, MinWidth = 260, ToolTip = link.Url };
            card.SetResourceReference(Border.BackgroundProperty, "BgBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush1");
            card.MouseEnter += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            card.MouseLeave += (_, _) => card.SetResourceReference(Border.BorderBrushProperty, "BorderBrush1");
            card.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                LinkClicked?.Invoke(link);
            };
            return card;
        }

        private void Send()
        {
            var text = InputBox.Text.Trim();
            if (text.Length == 0 || CurrentKey == null) return;
            SendRequested?.Invoke(CurrentKey, text);
            InputBox.Clear();
            InputBox.Focus();
        }

        private void Send_Click(object sender, RoutedEventArgs e) => Send();

        private void Input_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
            e.Handled = true;
            Send();
        }

        private void Input_TextChanged(object sender, TextChangedEventArgs e)
        {
            InputHint.Visibility = InputBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SendBtn.IsEnabled = InputBox.Text.Trim().Length > 0;
        }

        private void StateButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
    }
}
