using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Globalization;
using System.Text.Json;
using YellowBlackEditor.Utils;

using YellowBlackEditor.Models;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;


using WF = System.Windows.Forms;

using MediaColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfBrush = System.Windows.Media.Brush;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace YellowBlackEditor
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<NodeViewModel> RootNodes { get; } = new();

        private string _workspacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YellowBlackNotes");
        private NodeViewModel? _currentDocNode;
        private bool _isDirty;
        private bool _isLoadingDocument;
        private bool _isUpdatingSelectionUi;
        private readonly DispatcherTimer _autosaveTimer;
        private readonly Dictionary<string, ContentCacheEntry> _contentCache = new();

        private string HotkeyConfigPath => System.IO.Path.Combine(_appConfigDir, "hotkeys.json");
        private YellowBlackEditor.Models.HotkeySettings _hotkeys = new();

        private readonly string _appConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YellowBlackNotes");
        private string WorkspaceConfigPath => Path.Combine(_appConfigDir, "workspace.txt");

        public static readonly RoutedUICommand CreateSectionCmd = new("Create Section", "CreateSection", typeof(MainWindow));
        public static readonly RoutedUICommand CreateDocumentCmd = new("Create Document", "CreateDocument", typeof(MainWindow));
        public static readonly RoutedUICommand NavPrevCmd = new("Nav Prev", "NavPrev", typeof(MainWindow));
        public static readonly RoutedUICommand NavNextCmd = new("Nav Next", "NavNext", typeof(MainWindow));
        public static readonly RoutedUICommand NavParentCmd = new("Nav Parent", "NavParent", typeof(MainWindow));
        public static readonly RoutedUICommand NavChildCmd = new("Nav Child", "NavChild", typeof(MainWindow));

        private string ThemeConfigPath => System.IO.Path.Combine(_appConfigDir, "theme.json");

        private TextRange? _activeSearchHighlight;
        private bool _searchHighlightActive;

        private static readonly string[] ImageExts = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

        private List<ColorItem> _colors = new()
        {
            new("Белый", Colors.White),
            new("Желтый (акцент)", ColorFromHex("#FFC107")),
            new("Оранжевый", ColorFromHex("#FF9800")),
            new("Красный", ColorFromHex("#F44336")),
            new("Розовый", ColorFromHex("#E91E63")),
            new("Фиолетовый", ColorFromHex("#9C27B0")),
            new("Синий", ColorFromHex("#2196F3")),
            new("Голубой", ColorFromHex("#03A9F4")),
            new("Бирюзовый", ColorFromHex("#00BCD4")),
            new("Зеленый", ColorFromHex("#4CAF50")),
            new("Лаймовый", ColorFromHex("#CDDC39")),
            new("Серебристый", ColorFromHex("#BDBDBD"))
        };

        public MainWindow()
        {
            ThemeManager.LoadAndApply(ThemeConfigPath);
            InitializeComponent();
            Editor.PreviewKeyDown += new System.Windows.Input.KeyEventHandler(Editor_PreviewKeyDown);
            Editor.PreviewMouseDown += new System.Windows.Input.MouseButtonEventHandler(Editor_PreviewMouseDown);
            DataContext = this;

            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save,
                (s, e) => SaveCurrentDocument(),
                (s, e) => e.CanExecute = _currentDocNode != null));

            CommandBindings.Add(new CommandBinding(CreateSectionCmd, (s, e) => AddSection_Click(s, e)));
            CommandBindings.Add(new CommandBinding(CreateDocumentCmd, (s, e) => AddDocument_Click(s, e)));
            CommandBindings.Add(new CommandBinding(NavPrevCmd, (s, e) => NavigatePrev()));
            CommandBindings.Add(new CommandBinding(NavNextCmd, (s, e) => NavigateNext()));
            CommandBindings.Add(new CommandBinding(NavParentCmd, (s, e) => NavigateParent()));
            CommandBindings.Add(new CommandBinding(NavChildCmd, (s, e) => NavigateChild()));

            _hotkeys = HotkeyManager.Load(HotkeyConfigPath);
            HotkeyManager.ApplyToWindow(this, _hotkeys, CreateSectionCmd, CreateDocumentCmd, NavPrevCmd, NavNextCmd, NavParentCmd, NavChildCmd);

            ColorCombo.ItemsSource = _colors;
            ColorCombo.SelectedIndex = 0;

            LoadWorkspaceFromConfig();
            ThemeManager.LoadAndApply(ThemeConfigPath);
            InitializeWorkspace();
            EnsureDefaultStructure();
            LoadTree();
            ExpandAndSelectFirstDocument();

            _autosaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _autosaveTimer.Tick += (s, e) => { if (_currentDocNode != null && _isDirty) SaveCurrentDocument(); };
            _autosaveTimer.Start();

            UpdateUiState();
        }

        private IEnumerable<NodeViewModel> FlattenVisible(IEnumerable<NodeViewModel> nodes)
        {
            foreach (var n in nodes)
            {
                if (n.IsVisible) yield return n;
                foreach (var c in FlattenVisible(n.Children))
                    yield return c;
            }
        }
        private void SelectNode(NodeViewModel n)
        {
            if (n == null) return;
            var p = n.Parent;
            while (p != null) { p.IsExpanded = true; p = p.Parent; }
            n.IsSelected = true;
        }

        private void NavigatePrev()
        {
            var list = FlattenVisible(RootNodes).ToList();
            var cur = Tree.SelectedItem as NodeViewModel;
            if (cur == null && list.Count > 0) { SelectNode(list[0]); return; }
            var idx = list.FindIndex(x => ReferenceEquals(x, cur));
            if (idx > 0) SelectNode(list[idx - 1]);
        }
        private void NavigateNext()
        {
            var list = FlattenVisible(RootNodes).ToList();
            var cur = Tree.SelectedItem as NodeViewModel;
            if (cur == null && list.Count > 0) { SelectNode(list[0]); return; }
            var idx = list.FindIndex(x => ReferenceEquals(x, cur));
            if (idx >= 0 && idx < list.Count - 1) SelectNode(list[idx + 1]);
        }
        private void NavigateParent()
        {
            if (Tree.SelectedItem is NodeViewModel cur && cur.Parent != null)
                SelectNode(cur.Parent);
        }
        private void NavigateChild()
        {
            if (Tree.SelectedItem is NodeViewModel cur && cur.Children.Where(c => c.IsVisible).FirstOrDefault() is NodeViewModel firstChild)
                SelectNode(firstChild);
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new YellowBlackEditor.Views.SettingsWindow(ThemeConfigPath, HotkeyConfigPath) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _hotkeys = HotkeyManager.Load(HotkeyConfigPath);
                HotkeyManager.ApplyToWindow(this, _hotkeys, CreateSectionCmd, CreateDocumentCmd, NavPrevCmd, NavNextCmd, NavParentCmd, NavChildCmd);
                Status("Настройки применены");
            }
        }


        private void LoadWorkspaceFromConfig()
        {
            try
            {
                if (File.Exists(WorkspaceConfigPath))
                {
                    var p = (File.ReadAllText(WorkspaceConfigPath) ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                    {
                        _workspacePath = p;
                    }
                }
            }
            catch {  }
        }

        private void SaveWorkspaceToConfig()
        {
            try
            {
                Directory.CreateDirectory(_appConfigDir);
                File.WriteAllText(WorkspaceConfigPath, _workspacePath ?? "");
            }
            catch {  }
        }

        private sealed class ContentCacheEntry
        {
            public DateTime MTimeUtc;
            public string Text = "";
        }

        private void Editor_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_searchHighlightActive) ClearSearchHighlight();
        }

        private void Editor_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (_searchHighlightActive) ClearSearchHighlight();
        }


        private TextRange? FindFirstInDocument(FlowDocument doc, string query)
        {
            if (string.IsNullOrEmpty(query)) return null;

            var comparer = CultureInfo.CurrentCulture.CompareInfo;
            TextPointer pos = doc.ContentStart;

            while (pos != null && pos.CompareTo(doc.ContentEnd) < 0)
            {
                if (pos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    string runText = pos.GetTextInRun(LogicalDirection.Forward);
                    int idx = comparer.IndexOf(runText, query, CompareOptions.IgnoreCase);
                    if (idx >= 0)
                    {
                        var start = pos.GetPositionAtOffset(idx);
                        var end = start!.GetPositionAtOffset(query.Length);
                        return new TextRange(start, end);
                    }
                    pos = pos.GetPositionAtOffset(runText.Length);
                }
                else
                {
                    pos = pos.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            return null;
        }

        private void HighlightFirstMatchOfActiveSearch()
        {
            var q = (SearchBox.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                ClearSearchHighlight();
                return;
            }

            var match = FindFirstInDocument(Editor.Document, q);
            if (match == null)
            {
                ClearSearchHighlight();
                return;
            }

            ClearSearchHighlight();

            Editor.Selection.Select(match.Start, match.End);
            Editor.Focus();

            var brush = new SolidColorBrush(ColorFromHex("#88FFC107")); 
            match.ApplyPropertyValue(TextElement.BackgroundProperty, brush);

            _activeSearchHighlight = match;
            _searchHighlightActive = true;
        }

        private void ClearSearchHighlight()
        {
            if (_activeSearchHighlight != null)
            {
                try
                {
                    _activeSearchHighlight.ApplyPropertyValue(TextElement.BackgroundProperty, null);
                }
                catch {  }
                _activeSearchHighlight = null;
            }
            _searchHighlightActive = false;
        }


        private string GetDocumentTextForSearch(string docFolder)
        {
            if (_currentDocNode != null &&
                string.Equals(_currentDocNode.FullPath, docFolder, StringComparison.OrdinalIgnoreCase))
            {
                return new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text ?? "";
            }

            string? path = null;
            var xaml = GetXamlPackPath(docFolder);
            var rtf = GetRtfPath(docFolder);
            if (File.Exists(xaml)) path = xaml;
            else if (File.Exists(rtf)) path = rtf;

            if (path == null) return "";

            var mtime = File.GetLastWriteTimeUtc(path);
            if (_contentCache.TryGetValue(docFolder, out var cached) && cached.MTimeUtc == mtime)
                return cached.Text;

            var doc = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var fmt = path.EndsWith(".xamlpack", StringComparison.OrdinalIgnoreCase)
                    ? System.Windows.DataFormats.XamlPackage
                    : System.Windows.DataFormats.Rtf;
                range.Load(fs, fmt);
            }
            var text = new TextRange(doc.ContentStart, doc.ContentEnd).Text ?? "";

            _contentCache[docFolder] = new ContentCacheEntry { MTimeUtc = mtime, Text = text };
            return text;
        }

        private bool DocumentContentContains(NodeViewModel node, string query)
        {
            if (node.Type != NodeType.Document) return false;
            try
            {
                var txt = GetDocumentTextForSearch(node.FullPath);
                return CultureInfo.CurrentCulture.CompareInfo
                    .IndexOf(txt, query, CompareOptions.IgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        

        

        private static MediaColor ColorFromHex(string hex)
            => (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;

        private void InitializeWorkspace()
        {
            try
            {
                if (!Directory.Exists(_workspacePath))
                    Directory.CreateDirectory(_workspacePath);

                WorkspaceText.Text = $"Рабочая папка: {_workspacePath}";
                Status("Рабочая папка готова");
                SaveWorkspaceToConfig();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось подготовить рабочую папку:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnsureDefaultStructure()
        {
            try
            {
                if (!Directory.Exists(_workspacePath)) Directory.CreateDirectory(_workspacePath);
                var hasAny = Directory.EnumerateDirectories(_workspacePath).Any();
                if (!hasAny)
                {
                    var secDir = Path.Combine(_workspacePath, "Раздел 1");
                    Directory.CreateDirectory(secDir);
                    var docDir = Path.Combine(secDir, "Заметка 1");
                    Directory.CreateDirectory(docDir);
                    EnsureContentExists(docDir);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось создать базовую структуру:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadTree()
        {
            RootNodes.Clear();

            if (!Directory.Exists(_workspacePath)) return;

            foreach (var sectionDir in Directory.GetDirectories(_workspacePath).OrderBy(Path.GetFileName))
            {
                var sectionNode = new NodeViewModel
                {
                    Name = Path.GetFileName(sectionDir),
                    FullPath = sectionDir,
                    Type = NodeType.Section
                };

                LoadDocumentsRecursive(sectionNode, sectionDir);
                RootNodes.Add(sectionNode);
            }
        }

        private void LoadDocumentsRecursive(NodeViewModel parent, string directory)
        {
            foreach (var docDir in Directory.GetDirectories(directory).OrderBy(Path.GetFileName))
            {
                var dirName = Path.GetFileName(docDir);
                if (dirName.Equals("_assets", StringComparison.OrdinalIgnoreCase)) continue; 

                var docNode = new NodeViewModel
                {
                    Name = dirName,
                    FullPath = docDir,
                    Type = NodeType.Document,
                    Parent = parent
                };
                parent.Children.Add(docNode);

                LoadAttachmentsForDocument(docNode);

                LoadDocumentsRecursive(docNode, docDir);
            }
        }

        private void LoadAttachmentsForDocument(NodeViewModel docNode)
        {
            var assets = GetAssetsDir(docNode.FullPath);
            if (!Directory.Exists(assets)) return;

            foreach (var file in Directory.GetFiles(assets).OrderBy(Path.GetFileName))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!ImageExts.Contains(ext)) continue;

                var att = new NodeViewModel
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    Type = NodeType.Attachment,
                    Parent = docNode
                };
                docNode.Children.Add(att);
            }
        }

        private static string GetAssetsDir(string docDir) => Path.Combine(docDir, "_assets");

        private void ExpandAndSelectFirstDocument()
        {
            var firstDoc = Flatten(RootNodes).FirstOrDefault(n => n.Type == NodeType.Document);
            if (firstDoc != null)
            {
                ExpandParents(firstDoc);
                firstDoc.IsSelected = true;
            }
        }

        private static IEnumerable<NodeViewModel> Flatten(IEnumerable<NodeViewModel> nodes)
        {
            foreach (var n in nodes)
            {
                yield return n;
                foreach (var c in Flatten(n.Children))
                    yield return c;
            }
        }

        private static void ExpandParents(NodeViewModel node)
        {
            var p = node.Parent;
            while (p != null)
            {
                p.IsExpanded = true;
                p = p.Parent;
            }
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = Tree.SelectedItem as NodeViewModel;
            OnNodeSelected(node);
        }

        private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            if (item != null)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void SaveFlowDocumentAsXamlPackage(FlowDocument doc, string path)
        {
            var tmp = path + ".tmp";
            var bak = path + ".bak";

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            try
            {
                using (var ms = new MemoryStream())
                {
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    range.Save(ms, System.Windows.DataFormats.XamlPackage);

                    File.WriteAllBytes(tmp, ms.ToArray());
                }

                if (File.Exists(bak)) File.Delete(bak);
                if (File.Exists(path)) File.Move(path, bak);

                File.Copy(tmp, path, true);
                File.Delete(tmp);

                if (File.Exists(bak)) File.Delete(bak);
            }
            catch
            {
                try
                {
                    if (File.Exists(tmp)) File.Delete(tmp);
                    if (File.Exists(bak))
                    {
                        if (File.Exists(path)) File.Delete(path);
                        File.Move(bak, path);
                    }
                }
                catch {  }
                throw;
            }
        }

        private FlowDocument LoadFlowDocumentPreferXamlPackage(string docFolder)
        {
            var xamlPath = GetXamlPackPath(docFolder);
            var rtfPath = GetRtfPath(docFolder);
            var bakPath = xamlPath + ".bak";

            static FlowDocument LoadXaml(string p)
            {
                var d = new FlowDocument();
                var r = new TextRange(d.ContentStart, d.ContentEnd);
                using var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
                r.Load(fs, System.Windows.DataFormats.XamlPackage);
                return d;
            }

            if (File.Exists(xamlPath))
            {
                try
                {
                    return LoadXaml(xamlPath);
                }
                catch
                {
                    if (File.Exists(bakPath))
                        return LoadXaml(bakPath);

                    throw; 
                }
            }
            else if (File.Exists(rtfPath))
            {
                var d = new FlowDocument();
                var r = new TextRange(d.ContentStart, d.ContentEnd);
                using var fs = new FileStream(rtfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                r.Load(fs, System.Windows.DataFormats.Rtf);
                return d;
            }
            else
            {
                var d = new FlowDocument(new Paragraph(new Run("Новая заметка")))
                {
                    Foreground = WpfBrushes.White,
                    FontSize = 16
                };
                SaveFlowDocumentAsXamlPackage(d, xamlPath);
                return d;
            }
        }

        private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            if (item != null)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void Tree_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.F2)
            {
                if (Tree.SelectedItem is NodeViewModel node)
                {
                    RenameNode(node);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Delete)
            {
                if (Tree.SelectedItem is NodeViewModel node)
                {
                    if (node.Type == NodeType.Attachment)
                        DeleteAttachment(node);
                    else
                        DeleteNode(node);

                    e.Handled = true;
                }
            }
        }

        private static TreeViewItem? FindTreeViewItem(DependencyObject? source)
        {
            while (source != null && source is not TreeViewItem)
            {
                if (source is Visual v)
                    source = VisualTreeHelper.GetParent(v);
                else if (source is FrameworkContentElement fce)
                    source = fce.Parent;
                else
                    break;
            }
            return source as TreeViewItem;
        }

        private void OnNodeSelected(NodeViewModel? node)
        {
            if (_searchHighlightActive) ClearSearchHighlight();
            if (_isDirty && _currentDocNode != null)
            {
                SaveCurrentDocument();
            }

            if (node?.Type == NodeType.Document)
            {
                LoadDocument(node);
            }
            else
            {
                _currentDocNode = null;
                Editor.Document = CreateEmptyDocument();
                CurrentFileText.Text = "-";
                DirtyText.Text = "";
                _isDirty = false;
            }

            UpdateUiState();
        }

        private static string GetXamlPackPath(string docFolder) => Path.Combine(docFolder, "content.xamlpack");
        private static string GetRtfPath(string docFolder) => Path.Combine(docFolder, "content.rtf");

        private void LoadDocument(NodeViewModel node)
        {
            try
            {
                _isLoadingDocument = true;
                _currentDocNode = node;

                var doc = LoadFlowDocumentPreferXamlPackage(node.FullPath);

                Editor.Document = doc;

                HighlightFirstMatchOfActiveSearch();
                SetDefaultTypingForeground(WpfBrushes.White);

                CurrentFileText.Text = node.Name;
                DirtyText.Text = "Сохранено";
                _isDirty = false;
                Status($"Открыт файл: {node.Name}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка загрузки файла:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isLoadingDocument = false;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e) => SaveCurrentDocument();

        private void SaveCurrentDocument()
        {
            if (_currentDocNode == null) return;

            try
            {
                var xamlPath = GetXamlPackPath(_currentDocNode.FullPath);
                SaveFlowDocumentAsXamlPackage(Editor.Document, xamlPath);

                _isDirty = false;
                DirtyText.Text = "Сохранено";
                Status("Сохранено");
             
                var textNow = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text ?? "";
                _contentCache[_currentDocNode.FullPath] = new ContentCacheEntry
                {
                    MTimeUtc = File.GetLastWriteTimeUtc(xamlPath),
                    Text = textNow
                };
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Ошибка сохранения:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDocNode == null)
            {
                System.Windows.MessageBox.Show("Сначала выберите файл слева.", "Подсказка", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new WpfOpenFileDialog
            {
                Title = "Выберите изображение",
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                foreach (var path in dlg.FileNames)
                    InsertImageAtCaret(path);
            }
        }

        private void InsertImageAtCaret(string path)
        {
            try
            {
                if (!File.Exists(path)) return;

                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; 
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();

                var img = new System.Windows.Controls.Image
                {
                    Source = bi,
                    Stretch = Stretch.Uniform,
                    MaxWidth = Math.Max(100, Editor.ActualWidth - 60),
                    SnapsToDevicePixels = true
                };

                var caret = Editor.CaretPosition;
                var inline = new InlineUIContainer(img, caret);
                Editor.CaretPosition = inline.ElementEnd; 

                _isDirty = true;
                DirtyText.Text = "Есть несохранённые изменения";
                Status($"Картинка вставлена: {System.IO.Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось вставить изображение:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Editor_PreviewDragOver(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                if (files.Any(f => ImageExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())))
                {
                    e.Effects = WpfDragDropEffects.Copy;
                    e.Handled = true;

                    var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor), true);
                    if (pos != null) Editor.CaretPosition = pos;
                }
            }
        }

        private void Editor_Drop(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                foreach (var f in files)
                {
                    var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    if (ImageExts.Contains(ext))
                        InsertImageAtCaret(f);
                }
                e.Handled = true;
            }
        }

        private static string MakeUniqueFileName(string folder, string fileName)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(fileName);
            var ext = System.IO.Path.GetExtension(fileName);
            string candidate = System.IO.Path.Combine(folder, name + ext);
            int i = 1;
            while (File.Exists(candidate))
            {
                candidate = System.IO.Path.Combine(folder, $"{name} ({i++}){ext}");
            }
            return candidate;
        }

        private void Tree_DragOver(object sender, WpfDragEventArgs e)
        {
            e.Effects = WpfDragDropEffects.None;
            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            if (item?.DataContext is NodeViewModel node)
            {
                var target = node.Type == NodeType.Attachment ? node.Parent : node;
                if (target?.Type == NodeType.Document && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                    if (files.Any(f => ImageExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())))
                    {
                        e.Effects = WpfDragDropEffects.Copy;
                        e.Handled = true;
                    }
                }
            }
        }

        private void Tree_Drop(object sender, WpfDragEventArgs e)
        {
            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            if (item?.DataContext is not NodeViewModel node) return;

            var targetDoc = node.Type == NodeType.Attachment ? node.Parent : node;
            if (targetDoc?.Type != NodeType.Document) return;

            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop);
                var assets = GetAssetsDir(targetDoc.FullPath);
                Directory.CreateDirectory(assets);

                foreach (var f in files)
                {
                    var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    if (!ImageExts.Contains(ext)) continue;

                    var dest = MakeUniqueFileName(assets, System.IO.Path.GetFileName(f));
                    File.Copy(f, dest);

                    var att = new NodeViewModel
                    {
                        Name = System.IO.Path.GetFileName(dest),
                        FullPath = dest,
                        Type = NodeType.Attachment,
                        Parent = targetDoc
                    };
                    targetDoc.Children.Add(att);
                    targetDoc.IsExpanded = true;
                }
                Status("Изображение(я) добавлены рядом с файлом");
                e.Handled = true;
            }
        }

        private static string SanitizeName(string name)
        {
            name = name.Trim();
            var invalid = string.Join("", Path.GetInvalidFileNameChars());
            var regex = new Regex($"[{Regex.Escape(invalid)}]+");
            name = regex.Replace(name, " ");
            name = Regex.Replace(name, @"\s{2,}", " ").Trim();
            return string.IsNullOrEmpty(name) ? "Без имени" : name;
        }

        private static string MakeUniqueName(string parentDir, string baseName)
        {
            string candidate = baseName;
            int i = 1;
            while (Directory.Exists(System.IO.Path.Combine(parentDir, candidate)))
            {
                candidate = $"{baseName} ({i++})";
            }
            return candidate;
        }

        private void ChooseWorkspace_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new WF.FolderBrowserDialog
            {
                Description = "Выберите папку рабочей области для заметок",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = _workspacePath
            };
            var result = dlg.ShowDialog();
            if (result == WF.DialogResult.OK)
            {
                _workspacePath = dlg.SelectedPath;
                WorkspaceText.Text = $"Рабочая папка: {_workspacePath}";

                _currentDocNode = null;
                Editor.Document = CreateEmptyDocument();
                CurrentFileText.Text = "-";
                DirtyText.Text = "";
                _isDirty = false;

                LoadTree();
                ExpandAndSelectFirstDocument();

                Status("Рабочая папка изменена");
                SaveWorkspaceToConfig();
            }
        }

        private void Status(string text) => StatusText.Text = text;

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingDocument) return;
            if (_currentDocNode == null) return;
            _isDirty = true;
            DirtyText.Text = "Есть несохранённые изменения";
        }

        private void BoldBtn_Checked(object sender, RoutedEventArgs e) => ApplyToSelection(TextElement.FontWeightProperty, FontWeights.Bold);
        private void BoldBtn_Unchecked(object sender, RoutedEventArgs e) => ApplyToSelection(TextElement.FontWeightProperty, FontWeights.Normal);

        private void ApplyToSelection(DependencyProperty property, object value)
        {
            if (_currentDocNode == null) return;
            Editor.Focus();
            Editor.Selection.ApplyPropertyValue(property, value);
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectionUi) return;
            try
            {
                _isUpdatingSelectionUi = true;

                var weight = Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty);
                BoldBtn.IsChecked = (weight != DependencyProperty.UnsetValue && weight.Equals(FontWeights.Bold));

                var fg = Editor.Selection.GetPropertyValue(TextElement.ForegroundProperty);
                if (fg != DependencyProperty.UnsetValue && fg is SolidColorBrush br)
                {
                    var idx = _colors.FindIndex(c => CloseEnough(c.Brush.Color, br.Color));
                    ColorCombo.SelectedIndex = idx >= 0 ? idx : 0;
                }
                else
                {
                    SetDefaultTypingForeground(WpfBrushes.White);
                }
            }
            finally
            {
                _isUpdatingSelectionUi = false;
            }
        }

        private static bool CloseEnough(MediaColor a, MediaColor b)
        {
            int d(byte x, byte y) => Math.Abs(x - y);
            return d(a.R, b.R) + d(a.G, b.G) + d(a.B, b.B) < 30;
        }

        private void ColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ColorCombo.SelectedItem is ColorItem item)
            {
                var brush = new SolidColorBrush(item.Brush.Color);
                ApplyToSelection(TextElement.ForegroundProperty, brush);
                SetDefaultTypingForeground(brush);
            }
        }

        private void ClearFormatting_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDocNode == null) return;
            var sel = Editor.Selection;
            if (sel == null) return;
            var range = new TextRange(sel.Start, sel.End);
            range.ClearAllProperties();
            SetDefaultTypingForeground(WpfBrushes.White);
        }

        private void UpdateUiState()
        {
           
        }

        private void Node_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var node = (fe.DataContext as NodeViewModel);
            if (node == null) return;

            var cm = new System.Windows.Controls.ContextMenu();

            if (node.Type == NodeType.Section)
            {
                cm.Items.Add(MenuItem("+ Файл", (s, ev) => AddDocumentUnder(node)));
                cm.Items.Add(new System.Windows.Controls.Separator());
                cm.Items.Add(MenuItem("Переименовать", (s, ev) => RenameNode(node)));
                cm.Items.Add(MenuItem("Удалить", (s, ev) => DeleteNode(node)));
            }
            else if (node.Type == NodeType.Document)
            {
                cm.Items.Add(MenuItem("+ Связанный файл", (s, ev) => AddDocumentUnder(node)));
                cm.Items.Add(MenuItem("Вставить картинку в текст...", (s, ev) => InsertImage_Click(s, ev)));
                cm.Items.Add(MenuItem("Добавить изображение рядом...", (s, ev) => AddAttachmentViaDialog(node)));
                cm.Items.Add(MenuItem("Открыть папку файла", (s, ev) => OpenInExplorer(node.FullPath)));
                cm.Items.Add(new System.Windows.Controls.Separator());
                cm.Items.Add(MenuItem("Переименовать", (s, ev) => RenameNode(node)));
                cm.Items.Add(MenuItem("Удалить", (s, ev) => DeleteNode(node)));
            }
            else if (node.Type == NodeType.Attachment)
            {
                cm.Items.Add(MenuItem("Открыть", (s, ev) => OpenFile(node.FullPath)));
                cm.Items.Add(MenuItem("Показать в проводнике", (s, ev) => OpenInExplorer(node.FullPath)));
                cm.Items.Add(new System.Windows.Controls.Separator());
                cm.Items.Add(MenuItem("Переименовать", (s, ev) => RenameAttachment(node)));
                cm.Items.Add(MenuItem("Удалить", (s, ev) => DeleteAttachment(node)));
            }

            fe.ContextMenu = cm;
        }

        private System.Windows.Controls.MenuItem MenuItem(string header, RoutedEventHandler onClick)
        {
            var mi = new System.Windows.Controls.MenuItem { Header = header };
            mi.Click += onClick;
            return mi;
        }

        private void AddAttachmentViaDialog(NodeViewModel docNode)
        {
            var dlg = new WpfOpenFileDialog
            {
                Title = "Выберите изображение",
                Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true)
            {
                var assets = GetAssetsDir(docNode.FullPath);
                Directory.CreateDirectory(assets);

                foreach (var path in dlg.FileNames)
                {
                    var dest = MakeUniqueFileName(assets, System.IO.Path.GetFileName(path));
                    File.Copy(path, dest);

                    var att = new NodeViewModel
                    {
                        Name = System.IO.Path.GetFileName(dest),
                        FullPath = dest,
                        Type = NodeType.Attachment,
                        Parent = docNode
                    };
                    docNode.Children.Add(att);
                }
                docNode.IsExpanded = true;
                Status("Изображение(я) добавлены рядом с файлом");
            }
        }

        private void OpenFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось открыть файл:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenInExplorer(string path)
        {
            try
            {
                var isDir = Directory.Exists(path);
                var arg = isDir ? path : $"/select,\"{path}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось открыть проводник:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RenameNode(NodeViewModel node)
        {
            var prompt = node.Type == NodeType.Section ? "Новое имя раздела:" : "Новое имя файла:";
            var newName = TextPromptWindow.Show(this, "Новое имя:", node.Name, width: 700, height: 220,
                                    getSelection: () => Editor?.Selection?.Text);
            if (string.IsNullOrWhiteSpace(newName)) return;
            newName = SanitizeName(newName);

            if (node.Type == NodeType.Attachment)
            {
                RenameAttachment(node, newName);
                return;
            }

            var parentDir = Directory.GetParent(node.FullPath)!.FullName;
            var newDir = Path.Combine(parentDir, MakeUniqueName(parentDir, newName));

            try
            {
                var oldDir = node.FullPath;
                Directory.Move(oldDir, newDir);
                node.FullPath = newDir;
                if (_contentCache.TryGetValue(oldDir, out var entry))
                {
                    _contentCache.Remove(oldDir);
                    _contentCache[newDir] = entry;
                }
                node.Name = Path.GetFileName(newDir);
                node.IsSelected = true;
                UpdatePathsRecursively(node, oldDir, newDir);
                Status("Переименовано");

                if (_currentDocNode == node)
                    CurrentFileText.Text = node.Name;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось переименовать:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RenameAttachment(NodeViewModel node, string? newName = null)
        {
            if (node.Type != NodeType.Attachment) return;

            var parentDir = Directory.GetParent(node.FullPath)!.FullName;
            var baseName = newName ?? TextPromptWindow.Show(this, "Новое имя файла:", System.IO.Path.GetFileNameWithoutExtension(node.Name), width: 600, height: 180);
            if (string.IsNullOrWhiteSpace(baseName)) return;
            baseName = SanitizeName(baseName);

            var ext = System.IO.Path.GetExtension(node.FullPath);
            var dest = Path.Combine(parentDir, baseName + ext);
            dest = MakeUniqueFileName(parentDir, baseName + ext);

            try
            {
                File.Move(node.FullPath, dest);
                node.FullPath = dest;
                node.Name = System.IO.Path.GetFileName(dest);
                node.IsSelected = true;
                Status("Переименовано");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось переименовать файл:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePathsRecursively(NodeViewModel node, string oldBase, string newBase)
        {
            foreach (var ch in node.Children)
            {
                if (ch.Type == NodeType.Attachment)
                {
                    var relative = Path.GetRelativePath(oldBase, ch.FullPath);
                    ch.FullPath = Path.Combine(newBase, relative);
                }
                else if (ch.Type == NodeType.Document || ch.Type == NodeType.Section)
                {
                    if (ch.FullPath.StartsWith(oldBase, StringComparison.OrdinalIgnoreCase))
                        ch.FullPath = newBase + ch.FullPath.Substring(oldBase.Length);
                    UpdatePathsRecursively(ch, oldBase, newBase);
                }
            }
        }

        private void DeleteNode(NodeViewModel node)
        {
            var what = node.Type == NodeType.Section ? "раздел" : "файл";
            if (System.Windows.MessageBox.Show($"Удалить {what} \"{node.Name}\"?\nДействие необратимо.", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                if (_currentDocNode == node)
                {
                    _currentDocNode = null;
                    Editor.Document = CreateEmptyDocument();
                    CurrentFileText.Text = "-";
                    DirtyText.Text = "";
                    _isDirty = false;
                }

                Directory.Delete(node.FullPath, true);

                if (node.Parent != null)
                    node.Parent.Children.Remove(node);
                else
                    RootNodes.Remove(node);

                ExpandAndSelectFirstDocument();
                Status("Удалено");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось удалить:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteAttachment(NodeViewModel node)
        {
            if (node.Type != NodeType.Attachment) return;

            if (System.Windows.MessageBox.Show($"Удалить файл \"{node.Name}\"?\nДействие необратимо.", "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                if (File.Exists(node.FullPath)) File.Delete(node.FullPath);
                node.Parent?.Children.Remove(node);
                Status("Файл удалён");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Не удалось удалить файл:\n{ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddSection_Click(object sender, RoutedEventArgs e)
        {
            var name = TextPromptWindow.Show(this, "Название раздела:", initial: "", width: 700, height: 220,
                                 getSelection: () => Editor?.Selection?.Text);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = SanitizeName(name);

            var path = Path.Combine(_workspacePath, MakeUniqueName(_workspacePath, name));
            Directory.CreateDirectory(path);

            var node = new NodeViewModel { Name = Path.GetFileName(path), FullPath = path, Type = NodeType.Section };
            RootNodes.Add(node);
            node.IsSelected = true;
            Status($"Раздел создан: {node.Name}");
        }

        private void AddDocument_Click(object sender, RoutedEventArgs e)
        {
            var selection = Tree.SelectedItem as NodeViewModel;
            if (selection == null)
            {
                System.Windows.MessageBox.Show("Выберите раздел или файл слева.", "Подсказка", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            AddDocumentUnder(selection);
            UpdateUiState();
        }

        private void AddDocumentUnder(NodeViewModel parent)
        {
            bool isRelated = parent.Type == NodeType.Document;
            string prompt = isRelated ? "Название связанного файла:" : "Название файла:";

            string? name = TextPromptWindow.Show(this, prompt, initial: "", width: 700, height: 220,
                                     getSelection: () => Editor?.Selection?.Text);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = SanitizeName(name);

            string parentDir = parent.FullPath;
            var dir = Path.Combine(parentDir, MakeUniqueName(parentDir, name));
            Directory.CreateDirectory(dir);
            EnsureContentExists(dir);

            var newNode = new NodeViewModel
            {
                Name = Path.GetFileName(dir),
                FullPath = dir,
                Type = NodeType.Document,
                Parent = parent
            };
            parent.Children.Add(newNode);
            parent.IsExpanded = true;
            newNode.IsSelected = true;

            Status($"Файл создан: {newNode.Name}");

            var q = (SearchBox.Text ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(q))
                ApplySearch(q);
        }

        private void EnsureContentExists(string docDir)
        {
            var xamlPath = GetXamlPackPath(docDir);
            if (!File.Exists(xamlPath))
            {
                var run = new Run("Новая заметка") { Foreground = WpfBrushes.White };
                var doc = new FlowDocument(new Paragraph(run)) { Foreground = WpfBrushes.White, FontSize = 16 };
                SaveFlowDocumentAsXamlPackage(doc, xamlPath);
            }
        }

        private FlowDocument CreateEmptyDocument()
        {
            return new FlowDocument
            {
                Foreground = WpfBrushes.White,
                FontSize = 16,
                PagePadding = new Thickness(12)
            };
        }

        private void SetDefaultTypingForeground(WpfBrush brush)
        {
            try
            {
                Editor.Focus();
                var caret = Editor.CaretPosition;
                Editor.Selection.Select(caret, caret);
                Editor.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
            }
            catch { }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplySearch((SearchBox.Text ?? "").Trim());

            if (_currentDocNode != null)
                HighlightFirstMatchOfActiveSearch();
        }

        private void ApplySearch(string q)
        {
            foreach (var root in RootNodes)
                RecalcVisibility(root, q);

            if (!string.IsNullOrWhiteSpace(q))
            {
                var current = Tree.SelectedItem as NodeViewModel;
                if (current == null || !current.IsVisible)
                {
                    var first = Flatten(RootNodes).FirstOrDefault(n => n.IsVisible && (n.Type == NodeType.Document || n.Type == NodeType.Attachment));
                    if (first != null) first.IsSelected = true;
                }
            }
            else
            {
                foreach (var n in Flatten(RootNodes))
                    n.IsHit = false;
            }
        }

        private bool RecalcVisibility(NodeViewModel node, string q)
        {
            bool hasQuery = !string.IsNullOrWhiteSpace(q);

            bool nameHit = hasQuery &&
                           node.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;

            bool contentHit = false;
            if (hasQuery && node.Type == NodeType.Document)
                contentHit = DocumentContentContains(node, q);

            bool childHit = false;
            foreach (var ch in node.Children)
                childHit |= RecalcVisibility(ch, q);

            if (!hasQuery)
            {
                node.IsVisible = true;
                node.IsHit = false;
                return true;
            }
            else
            {
                bool selfHit = (node.Type == NodeType.Document && (nameHit || contentHit))
                               || (node.Type != NodeType.Document && nameHit);

                node.IsVisible = selfHit || childHit;
                node.IsHit = selfHit;         
                node.IsExpanded = childHit;   
                return node.IsVisible;
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isDirty)
            {
                var res = System.Windows.MessageBox.Show("Есть несохранённые изменения. Сохранить перед выходом?", "Выход", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (res == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
                if (res == MessageBoxResult.Yes)
                {
                    SaveCurrentDocument();
                }
            }
            base.OnClosing(e);
        }

        private class ColorItem
        {
            public string Title { get; }
            public System.Windows.Media.SolidColorBrush Brush { get; }
            public ColorItem(string title, System.Windows.Media.Color color)
            {
                Title = title;
                Brush = new System.Windows.Media.SolidColorBrush(color);
            }
        }
    }
}