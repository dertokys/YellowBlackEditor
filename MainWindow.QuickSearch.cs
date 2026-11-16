using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using YellowBlackEditor.Models;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;

namespace YellowBlackEditor
{
    public partial class MainWindow
    {
        private const int QuickSearchHotkeyId = 0x5942;
        private QuickSearchWindow? _quickSearch;
        private string _quickSearchHotkey = "";

        private static readonly (uint Mods, uint Key, string Text)[] QuickSearchKeys =
        {
            (0x0001 | 0x0002, 0x20, "Ctrl+Alt+Пробел"),
            (0x0002 | 0x0004, 0x20, "Ctrl+Shift+Пробел"),
            (0x0001 | 0x0002, 0x46, "Ctrl+Alt+F")
        };

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private void InitQuickSearch()
        {
            try
            {
                var handle = new WindowInteropHelper(this).EnsureHandle();
                HwndSource.FromHwnd(handle)?.AddHook(QuickSearchHook);
                foreach (var (mods, key, text) in QuickSearchKeys)
                {
                    if (!RegisterHotKey(handle, QuickSearchHotkeyId, mods | 0x4000, key)) continue;
                    _quickSearchHotkey = text;
                    break;
                }
                if (_quickSearchHotkey.Length == 0)
                    LogError("Быстрый поиск", new InvalidOperationException("Все сочетания для быстрого поиска заняты другими программами"));
                SearchBox.ToolTip = "Ищет сразу по названиям, тексту заметок и карточкам клиентов: ИНН, КПП, ОГРН, телефон, ID. Ctrl+F" +
                                    (_quickSearchHotkey.Length > 0 ? $". Из любой программы — {_quickSearchHotkey}" : "");
                Closed += (_, _) => UnregisterHotKey(handle, QuickSearchHotkeyId);
            }
            catch (Exception ex)
            {
                LogError("Быстрый поиск", ex);
            }
        }

        private IntPtr QuickSearchHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x0312 && wParam.ToInt32() == QuickSearchHotkeyId)
            {
                ShowQuickSearch();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private List<NodeViewModel> QuickFind(string q)
        {
            var nodes = Flatten(RootNodes.Where(n => !n.IsVirtual)).Where(n => n.Type is NodeType.Client or NodeType.Document).ToList();
            var byName = nodes.Where(n => Contains(n.Name, q)).OrderBy(n => n.Type == NodeType.Client ? 0 : 1).ThenBy(n => n.Name.Length);
            var byContent = nodes.Where(n => n.Type == NodeType.Client && !Contains(n.Name, q) && NodeContentContains(n, q));
            return byName.Concat(byContent).Distinct().ToList();
        }

        private void ShowQuickSearch()
        {
            if (_quickSearch != null)
            {
                if (_quickSearch.IsActive) _quickSearch.Close();
                else _quickSearch.BringUp();
                return;
            }
            _quickSearch = new QuickSearchWindow(QuickFind, _quickSearchHotkey, node =>
            {
                BringToFront();
                SelectNode(node);
            });
            _quickSearch.Closed += (_, _) => _quickSearch = null;
            _quickSearch.Show();
        }
    }
}
