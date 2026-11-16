using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using YellowBlackEditor.Utils;
using Button = System.Windows.Controls.Button;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using UserControl = System.Windows.Controls.UserControl;

namespace YellowBlackEditor.Views
{
    public partial class HelpdeskView : UserControl
    {
        private const string RuntimeUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        private WebView2? _web;
        private Task? _init;
        private string _baseUrl = "";
        private string? _pending;
        private Action? _messageAction;
        private string? _scriptId;
        private int _scriptVersion;
        private static string? _script;
        private CoreWebView2Environment? _env;
        private readonly List<WebTab> _tabs = new();
        private WebTab? _current;

        private sealed class WebTab
        {
            public required WebView2 Web { get; init; }
            public required Border Header { get; init; }
            public required TextBlock Title { get; init; }
            public Button? Close { get; init; }
            public bool Main => Close == null;
        }

        private WebView2? Active => _current?.Web ?? _web;

        public event Action<Exception>? Failed;

        public HelpdeskView()
        {
            InitializeComponent();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        public static string Normalize(string url)
        {
            url = (url ?? "").Trim().TrimEnd('/');
            if (url.Length == 0) return "";
            if (!url.Contains("://")) url = "https://" + url;
            var hash = url.IndexOf('#');
            if (hash >= 0) url = url[..hash].TrimEnd('/');
            return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp) ? url : "";
        }

        public static string SearchRoute(string query) => "search/" + Uri.EscapeDataString(query.Trim());

        public async void Open(string baseUrl, string? route = null)
        {
            baseUrl = Normalize(baseUrl);
            if (baseUrl.Length == 0)
            {
                ShowMessage("Адрес системы заявок не указан. Его можно задать в настройках.", null, null);
                return;
            }
            var changed = !string.Equals(baseUrl, _baseUrl, StringComparison.OrdinalIgnoreCase);
            _baseUrl = baseUrl;
            var target = route == null ? (changed || _web?.CoreWebView2 == null ? baseUrl + "/" : null) : baseUrl + "/#" + route;
            if (_web?.CoreWebView2 != null)
            {
                if (changed)
                {
                    try { await InstallScriptAsync(_web.CoreWebView2); }
                    catch (Exception ex) { Failed?.Invoke(ex); }
                }
                if (target != null) Navigate(target);
                return;
            }
            _pending = target ?? baseUrl + "/";
            try
            {
                _init ??= InitAsync();
                await _init;
                if (_web?.CoreWebView2 == null) return;
                var go = _pending;
                _pending = null;
                if (go != null) Navigate(go);
            }
            catch (WebView2RuntimeNotFoundException)
            {
                _init = null;
                ResetWeb();
                ShowMessage("Для вкладки заявок нужен компонент Microsoft Edge WebView2. Установите его и откройте вкладку ещё раз.",
                    "Скачать WebView2", () => OpenExternal(RuntimeUrl));
            }
            catch (Exception ex)
            {
                _init = null;
                ResetWeb();
                Failed?.Invoke(ex);
                ShowMessage("Не удалось открыть заявки: " + ex.Message, "Открыть в браузере", () => OpenExternal(_baseUrl + "/"));
            }
        }

        private async Task InitAsync()
        {
            ShowMessage("Загружаю заявки…", null, null);
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YellowBlackNotes", "WebView2");
            Directory.CreateDirectory(data);
            var env = await CoreWebView2Environment.CreateAsync(null, data);
            _env = env;
            _web = new WebView2 { Visibility = Visibility.Hidden };
            Host.Children.Add(_web);
            await _web.EnsureCoreWebView2Async(env);
            _current = AddTab(_web, false);
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            await InstallScriptAsync(core);
            ThemeManager.Changed += OnThemeChanged;
            core.NewWindowRequested += OnNewWindow;
            core.DocumentTitleChanged += (_, _) => UpdateTitles();
            core.HistoryChanged += (_, _) => UpdateNav();
            core.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    MessagePanel.Visibility = Visibility.Collapsed;
                    if (_current == null || _current.Main) _web.Visibility = Visibility.Visible;
                    return;
                }
                _web.Visibility = Visibility.Hidden;
                ShowMessage("Сайт заявок не открылся. Проверьте интернет и адрес в настройках.", "Повторить", () => core.Reload());
            };
        }

        private static string Script => _script ??= LoadScript();

        private static string LoadScript()
        {
            using var stream = typeof(HelpdeskView).Assembly.GetManifestResourceStream("YellowBlackEditor.Assets.helpdesk.js");
            if (stream == null) return "";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string ThemeJson()
        {
            static string C(string key) =>
                System.Windows.Application.Current.Resources[key] is System.Windows.Media.SolidColorBrush b ? $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}" : "#808080";
            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["light"] = ThemeManager.Current.IsLight,
                ["bg"] = C("BgBrush"),
                ["panel"] = C("PanelBrush"),
                ["panel2"] = C("PanelBrush2"),
                ["border"] = C("BorderBrush1"),
                ["text"] = C("TextBrush"),
                ["sub"] = C("SubTextBrush"),
                ["accent"] = C("AccentBrush"),
                ["accentLight"] = C("AccentBrushLight"),
                ["accentText"] = C("AccentTextBrush"),
                ["accentSoft"] = C("AccentSoftBrush"),
                ["hover"] = C("SurfaceHoverBrush"),
                ["field"] = C("FieldBrush"),
                ["fieldBorder"] = C("FieldBorderBrush"),
                ["danger"] = C("DangerBrush"),
            });
        }

        private async Task InstallScriptAsync(CoreWebView2 core)
        {
            if (Script.Length == 0) return;
            var version = ++_scriptVersion;
            var old = _scriptId;
            _scriptId = null;
            if (old != null) core.RemoveScriptToExecuteOnDocumentCreated(old);
            var host = Uri.TryCreate(_baseUrl, UriKind.Absolute, out var u) ? u.Authority : "";
            var id = await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__ybnHost = " + JsonSerializer.Serialize(host) + ";\nwindow.__ybnTheme = " + ThemeJson() + ";\n" + Script);
            if (version != _scriptVersion)
            {
                core.RemoveScriptToExecuteOnDocumentCreated(id);
                return;
            }
            _scriptId = id;
        }

        private async void OnThemeChanged()
        {
            try
            {
                var core = _web?.CoreWebView2;
                if (core == null) return;
                await InstallScriptAsync(core);
                await core.ExecuteScriptAsync("window.__ybnApplyTheme && window.__ybnApplyTheme(" + ThemeJson() + ")");
            }
            catch { }
        }

        private void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            var uri = e.Uri;
            if (!IsOwn(uri)) OpenExternal(uri);
            else if (IsApp(uri)) ShowInMain(uri);
            else _ = OpenTabAsync(uri);
        }

        private void ShowInMain(string uri)
        {
            if (_tabs.Count > 0) Activate(_tabs[0]);
            Navigate(uri);
        }

        private static bool IsApp(string uri) =>
            Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.AbsolutePath == "/" || u.AbsolutePath.Length == 0);

        private async Task OpenTabAsync(string url)
        {
            if (_env == null) return;
            WebView2? web = null;
            try
            {
                web = new WebView2 { Visibility = Visibility.Hidden };
                Host.Children.Add(web);
                await web.EnsureCoreWebView2Async(_env);
                var core = web.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                var tab = AddTab(web, true);
                core.NewWindowRequested += OnNewWindow;
                core.NavigationStarting += (_, e) =>
                {
                    if (IsOwn(e.Uri) && IsApp(e.Uri))
                    {
                        e.Cancel = true;
                        ShowInMain(e.Uri);
                    }
                    else if (!IsOwn(e.Uri) && e.IsUserInitiated)
                    {
                        e.Cancel = true;
                        OpenExternal(e.Uri);
                    }
                };
                core.DocumentTitleChanged += (_, _) => UpdateTitles();
                core.HistoryChanged += (_, _) => UpdateNav();
                Activate(tab);
                core.Navigate(url);
            }
            catch (Exception ex)
            {
                if (web != null && _tabs.All(t => t.Web != web))
                {
                    Host.Children.Remove(web);
                    web.Dispose();
                }
                Failed?.Invoke(ex);
            }
        }

        private WebTab AddTab(WebView2 web, bool closable)
        {
            var title = new TextBlock
            {
                Text = closable ? "База знаний" : "Заявки",
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 240,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(title);
            Button? close = null;
            if (closable)
            {
                close = new Button
                {
                    Content = "\uE711",
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 10,
                    Width = 22,
                    Height = 22,
                    Margin = new Thickness(8, 0, 0, 0),
                    Padding = new Thickness(0),
                    Background = System.Windows.Media.Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    ToolTip = "Закрыть вкладку (Ctrl+W)",
                    Focusable = false,
                };
                row.Children.Add(close);
            }
            var header = new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 5, closable ? 6 : 12, 5),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                Child = row,
            };
            var tab = new WebTab { Web = web, Header = header, Title = title, Close = close };
            if (close != null) close.Click += (_, _) => CloseTab(tab);
            header.MouseLeftButtonUp += (_, _) => Activate(tab);
            header.MouseUp += (_, e) =>
            {
                if (e.ChangedButton == MouseButton.Middle) CloseTab(tab);
            };
            _tabs.Add(tab);
            TabsPanel.Children.Add(header);
            TabsScroll.Visibility = _tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            Paint();
            return tab;
        }

        private void Activate(WebTab tab)
        {
            _current = tab;
            foreach (var t in _tabs)
            {
                var on = t == tab;
                t.Web.Visibility = on && (!t.Main || MessagePanel.Visibility != Visibility.Visible) ? Visibility.Visible : Visibility.Hidden;
            }
            Paint();
            UpdateTitles();
            UpdateNav();
            tab.Header.BringIntoView();
        }

        private void Paint()
        {
            var dark = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x11, 0x11));
            foreach (var t in _tabs)
            {
                var on = t == _current;
                t.Header.SetResourceReference(Border.BackgroundProperty, on ? "AccentBrush" : "PanelBrush");
                t.Header.SetResourceReference(Border.BorderBrushProperty, on ? "AccentBrush" : "BorderBrush1");
                if (on) t.Title.Foreground = dark;
                else t.Title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                t.Title.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                if (t.Close == null) continue;
                if (on) t.Close.Foreground = dark;
                else t.Close.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "SubTextBrush");
            }
        }

        private void CloseTab(WebTab tab)
        {
            if (tab.Main) return;
            var index = _tabs.IndexOf(tab);
            if (index < 0) return;
            _tabs.RemoveAt(index);
            TabsPanel.Children.Remove(tab.Header);
            Host.Children.Remove(tab.Web);
            tab.Web.Dispose();
            TabsScroll.Visibility = _tabs.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (_current == tab) Activate(_tabs[Math.Max(0, index - 1)]);
        }

        private void UpdateTitles()
        {
            foreach (var t in _tabs)
            {
                if (t.Main) continue;
                var full = t.Web.CoreWebView2?.DocumentTitle ?? "";
                var dash = full.LastIndexOf(" - ", StringComparison.Ordinal);
                t.Title.Text = string.IsNullOrWhiteSpace(full) ? "База знаний" : dash > 0 ? full[(dash + 3)..] : full;
                t.Header.ToolTip = string.IsNullOrWhiteSpace(full) ? null : full;
            }
            TitleText.Text = Active?.CoreWebView2?.DocumentTitle ?? "";
        }

        private void UpdateNav()
        {
            var core = Active?.CoreWebView2;
            BackBtn.IsEnabled = core?.CanGoBack == true;
            ForwardBtn.IsEnabled = core?.CanGoForward == true;
        }

        private void KnowledgeTab_Click(object sender, RoutedEventArgs e)
        {
            if (_env == null || _web?.CoreWebView2 == null)
            {
                Open(_baseUrl);
                return;
            }
            _ = OpenTabAsync(_baseUrl + "/help");
        }

        private void ResetWeb()
        {
            ThemeManager.Changed -= OnThemeChanged;
            _scriptId = null;
            foreach (var t in _tabs.Where(t => !t.Main).ToList()) CloseTab(t);
            _tabs.Clear();
            TabsPanel.Children.Clear();
            TabsScroll.Visibility = Visibility.Collapsed;
            _current = null;
            _env = null;
            if (_web == null) return;
            Host.Children.Remove(_web);
            _web.Dispose();
            _web = null;
        }

        private bool IsOwn(string uri) =>
            Uri.TryCreate(uri, UriKind.Absolute, out var u) && Uri.TryCreate(_baseUrl, UriKind.Absolute, out var b) &&
            string.Equals(u.Host, b.Host, StringComparison.OrdinalIgnoreCase);

        private void Navigate(string url)
        {
            try
            {
                _web?.CoreWebView2?.Navigate(url);
            }
            catch (Exception ex)
            {
                Failed?.Invoke(ex);
            }
        }

        private void ShowMessage(string text, string? button, Action? action)
        {
            if (_web != null) _web.Visibility = Visibility.Hidden;
            MessagePanel.Visibility = Visibility.Visible;
            MessageText.Text = text;
            _messageAction = action;
            MessageButton.Content = button ?? "";
            MessageButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;
        }

        private static void OpenExternal(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        private void MessageButton_Click(object sender, RoutedEventArgs e) => _messageAction?.Invoke();

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            var core = Active?.CoreWebView2;
            if (core?.CanGoBack == true) core.GoBack();
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            var core = Active?.CoreWebView2;
            if (core?.CanGoForward == true) core.GoForward();
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            var core = Active?.CoreWebView2;
            if (core != null) core.Reload();
            else Open(_baseUrl);
        }

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            if (_web?.CoreWebView2 != null) ShowInMain(_baseUrl + "/#ticket/view");
            else Open(_baseUrl);
        }

        private void External_Click(object sender, RoutedEventArgs e)
        {
            var url = Active?.CoreWebView2?.Source;
            OpenExternal(string.IsNullOrEmpty(url) || url == "about:blank" ? _baseUrl + "/" : url);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F5)
            {
                Reload_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control && _current is { Main: false } tab)
            {
                CloseTab(tab);
                e.Handled = true;
            }
            else if (e.Key == Key.Tab && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && _tabs.Count > 1 && _current != null)
            {
                var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                var i = (_tabs.IndexOf(_current) + step + _tabs.Count) % _tabs.Count;
                Activate(_tabs[i]);
                e.Handled = true;
            }
        }
    }
}
