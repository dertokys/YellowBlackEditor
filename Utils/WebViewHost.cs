using System.IO;
using Microsoft.Web.WebView2.Core;

namespace YellowBlackEditor.Utils
{
    public static class WebViewHost
    {
        private static Task<CoreWebView2Environment>? _env;

        public static string DataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YellowBlackNotes", "WebView2");

        public static Func<CoreWebView2?>? LiveCore { get; set; }

        public static Task<CoreWebView2Environment> EnvironmentAsync()
        {
            if (_env is { IsFaulted: true } or { IsCanceled: true }) _env = null;
            return _env ??= Create();
        }

        private static async Task<CoreWebView2Environment> Create()
        {
            Directory.CreateDirectory(DataFolder);
            return await CoreWebView2Environment.CreateAsync(null, DataFolder);
        }

        public static async Task<string> CookieHeaderAsync(string url, IntPtr hwnd)
        {
            var live = LiveCore?.Invoke();
            if (live != null) return Join(await live.CookieManager.GetCookiesAsync(url));
            var env = await EnvironmentAsync();
            var controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
            try
            {
                controller.IsVisible = false;
                return Join(await controller.CoreWebView2.CookieManager.GetCookiesAsync(url));
            }
            finally
            {
                controller.Close();
            }
        }

        private static string Join(IEnumerable<CoreWebView2Cookie> cookies) =>
            string.Join("; ", cookies.Select(c => c.Name + "=" + c.Value));
    }
}
