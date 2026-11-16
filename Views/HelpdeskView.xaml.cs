using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
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
            _web = new WebView2 { Visibility = Visibility.Hidden };
            Host.Children.Add(_web);
            await _web.EnsureCoreWebView2Async(env);
            var core = _web.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (IsOwn(e.Uri)) core.Navigate(e.Uri);
                else OpenExternal(e.Uri);
            };
            core.DocumentTitleChanged += (_, _) => TitleText.Text = core.DocumentTitle;
            core.HistoryChanged += (_, _) =>
            {
                BackBtn.IsEnabled = core.CanGoBack;
                ForwardBtn.IsEnabled = core.CanGoForward;
            };
            core.NavigationCompleted += (_, e) =>
            {
                if (e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    MessagePanel.Visibility = Visibility.Collapsed;
                    _web.Visibility = Visibility.Visible;
                    return;
                }
                _web.Visibility = Visibility.Hidden;
                ShowMessage("Сайт заявок не открылся. Проверьте интернет и адрес в настройках.", "Повторить", () => core.Reload());
            };
        }

        private void ResetWeb()
        {
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
            if (_web?.CoreWebView2?.CanGoBack == true) _web.CoreWebView2.GoBack();
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_web?.CoreWebView2?.CanGoForward == true) _web.CoreWebView2.GoForward();
        }

        private void Reload_Click(object sender, RoutedEventArgs e)
        {
            if (_web?.CoreWebView2 != null) _web.CoreWebView2.Reload();
            else Open(_baseUrl);
        }

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            if (_web?.CoreWebView2 != null) Navigate(_baseUrl + "/#dashboard");
            else Open(_baseUrl);
        }

        private void External_Click(object sender, RoutedEventArgs e)
        {
            var url = _web?.CoreWebView2?.Source;
            OpenExternal(string.IsNullOrEmpty(url) || url == "about:blank" ? _baseUrl + "/" : url);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.F5) return;
            Reload_Click(sender, e);
            e.Handled = true;
        }
    }
}
