using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualBasic.FileIO;
using YellowBlackEditor.Models;
using YellowBlackEditor.Utils;
using YellowBlackEditor.ViewModels;
using YellowBlackEditor.Views;
using MediaColor = System.Windows.Media.Color;
using WF = System.Windows.Forms;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfOpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace YellowBlackEditor
{
    public partial class MainWindow : Window
    {
        public ObservableCollection<NodeViewModel> RootNodes { get; } = new();

        private const string AppTitle = "Yellow Black Notes";
        private const string ClientsSectionName = "Клиенты";
        private static readonly string[] ImageExts = { ".png", ".jpg", ".jpeg", ".gif", ".bmp" };

        private readonly string _appConfigDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YellowBlackNotes");
        private string WorkspaceConfigPath => Path.Combine(_appConfigDir, "workspace.txt");
        private string HotkeyConfigPath => Path.Combine(_appConfigDir, "hotkeys.json");
        private string ThemeConfigPath => Path.Combine(_appConfigDir, "theme.json");
        private string WindowConfigPath => Path.Combine(_appConfigDir, "window.json");

        private string _workspacePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "YellowBlackNotes");

        private NodeViewModel? _currentDocNode;
        private NodeViewModel? _currentClientNode;
        private ClientCard? _currentClient;
        private bool _isDirty;
        private bool _clientDirty;
        private bool _isLoadingDocument;
        private bool _isUpdatingSelectionUi;

        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _searchTimer;
        private readonly Dictionary<string, ContentCacheEntry> _contentCache = new(StringComparer.OrdinalIgnoreCase);
        private HotkeySettings _hotkeys = new();

        public static readonly RoutedUICommand CreateSectionCmd = new("Create Section", "CreateSection", typeof(MainWindow));
        public static readonly RoutedUICommand CreateDocumentCmd = new("Create Document", "CreateDocument", typeof(MainWindow));
        public static readonly RoutedUICommand NavPrevCmd = new("Nav Prev", "NavPrev", typeof(MainWindow));
        public static readonly RoutedUICommand NavNextCmd = new("Nav Next", "NavNext", typeof(MainWindow));
        public static readonly RoutedUICommand NavParentCmd = new("Nav Parent", "NavParent", typeof(MainWindow));
        public static readonly RoutedUICommand NavChildCmd = new("Nav Child", "NavChild", typeof(MainWindow));

        private readonly List<ColorItem> _colors = new()
        {
            new("Белый", Colors.White),
            new("Жёлтый", ColorFromHex("#FFC107")),
            new("Оранжевый", ColorFromHex("#FF9800")),
            new("Красный", ColorFromHex("#F44336")),
            new("Розовый", ColorFromHex("#E91E63")),
            new("Фиолетовый", ColorFromHex("#9C27B0")),
            new("Синий", ColorFromHex("#2196F3")),
            new("Голубой", ColorFromHex("#03A9F4")),
            new("Бирюзовый", ColorFromHex("#00BCD4")),
            new("Зелёный", ColorFromHex("#4CAF50")),
            new("Лаймовый", ColorFromHex("#CDDC39")),
            new("Серебристый", ColorFromHex("#BDBDBD"))
        };

        public MainWindow()
        {
            ThemeManager.LoadAndApply(ThemeConfigPath);
            InitializeComponent();
            SourceInitialized += (_, _) => YellowBlackEditor.Utils.DarkTitleBar.Apply(this);
            DataContext = this;

            System.Windows.Application.Current.DispatcherUnhandledException += OnUnhandledException;

            CommandBindings.Add(new CommandBinding(ApplicationCommands.Save, (_, _) => SaveAll(), (_, e) => e.CanExecute = true));
            CommandBindings.Add(new CommandBinding(ApplicationCommands.Find, (_, _) => { SearchBox.Focus(); SearchBox.SelectAll(); }));
            CommandBindings.Add(new CommandBinding(CreateSectionCmd, (s, e) => AddSection_Click(s, e)));
            CommandBindings.Add(new CommandBinding(CreateDocumentCmd, (s, e) => AddDocument_Click(s, e)));
            CommandBindings.Add(new CommandBinding(NavPrevCmd, (_, _) => NavigatePrev()));
            CommandBindings.Add(new CommandBinding(NavNextCmd, (_, _) => NavigateNext()));
            CommandBindings.Add(new CommandBinding(NavParentCmd, (_, _) => NavigateParent()));
            CommandBindings.Add(new CommandBinding(NavChildCmd, (_, _) => NavigateChild()));

            _hotkeys = HotkeyManager.Load(HotkeyConfigPath);
            HotkeyManager.ApplyToWindow(this, _hotkeys, CreateSectionCmd, CreateDocumentCmd, NavPrevCmd, NavNextCmd, NavParentCmd, NavChildCmd);

            ColorCombo.ItemsSource = _colors;
            ApplyFontSizes();

            ClientView.StatusMessage += Status;
            ClientView.AddNoteRequested += () => { if (_currentClientNode != null) AddDocumentUnder(_currentClientNode); };
            ClientView.OpenNoteRequested += n => SelectNode(n);
            InitShared();
            InitToday();
            InitUpdates();

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveAll(); };

            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); RunSearch(); };

            Tree.MouseDoubleClick += Tree_MouseDoubleClick;
            Deactivated += (_, _) => SaveAll(quiet: true);

            RestoreWindowPlacement();
            LoadWorkspaceFromConfig();
            InitializeWorkspace();
            EnsureDefaultStructure();
            LoadTree();
            ExpandAndSelectFirstDocument();
            if (_currentDocNode == null && _currentClientNode == null) ShowEmpty(null);
            InitReminders();
            InitSync();
        }

        private void ApplyFontSizes()
        {
            var t = ThemeManager.Current;
            Editor.FontSize = t.EditorFontSize is >= 10 and <= 40 ? t.EditorFontSize : 16;
            Tree.FontSize = t.TreeFontSize is >= 10 and <= 30 ? t.TreeFontSize : 15;
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogError("Необработанная ошибка", e.Exception);
            e.Handled = true;
            if (_showingCrashDialog) return;
            _showingCrashDialog = true;
            var message = e.Exception.Message;
            Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    var saved = SaveAll(quiet: true);
                    ThemedMessageBox.Show(this, "Внутренняя ошибка программы",
                        (saved ? "Заметки сохранены. " : "Последние правки сохранить не удалось. ") +
                        $"Подробности записаны в журнал {Path.Combine(_appConfigDir, "error.log")}.\n\n{message}",
                        MessageBoxImage.Error);
                }
                finally
                {
                    _showingCrashDialog = false;
                }
            }, DispatcherPriority.Background);
        }

        private bool _showingCrashDialog;

        private sealed class ContentCacheEntry
        {
            public DateTime MTimeUtc;
            public string Text = "";
        }

        private sealed class WindowPlacement
        {
            public double Left { get; set; }
            public double Top { get; set; }
            public double Width { get; set; }
            public double Height { get; set; }
            public bool Maximized { get; set; }
            public double TreeWidth { get; set; }
        }

        private void RestoreWindowPlacement()
        {
            try
            {
                if (!File.Exists(WindowConfigPath)) return;
                var p = JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(WindowConfigPath));
                if (p == null) return;

                var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                      SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                var rect = new Rect(p.Left, p.Top, Math.Max(p.Width, MinWidth), Math.Max(p.Height, MinHeight));
                if (screen.IntersectsWith(rect) && rect.Width <= screen.Width + 50)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = rect.Left;
                    Top = rect.Top;
                    Width = rect.Width;
                    Height = rect.Height;
                }
                if (p.Maximized) WindowState = WindowState.Maximized;
                if (p.TreeWidth >= 200 && p.TreeWidth <= 900) TreeColumn.Width = new GridLength(p.TreeWidth);
            }
            catch { }
        }

        private void SaveWindowPlacement()
        {
            try
            {
                var r = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
                var p = new WindowPlacement
                {
                    Left = r.Left,
                    Top = r.Top,
                    Width = r.Width,
                    Height = r.Height,
                    Maximized = WindowState == WindowState.Maximized,
                    TreeWidth = TreeColumn.ActualWidth
                };
                Directory.CreateDirectory(_appConfigDir);
                File.WriteAllText(WindowConfigPath, JsonSerializer.Serialize(p));
            }
            catch { }
        }

        private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e) => SaveWindowPlacement();

        private IEnumerable<NodeViewModel> FlattenVisible(IEnumerable<NodeViewModel> nodes)
        {
            foreach (var n in nodes)
            {
                if (!n.IsVisible) continue;
                yield return n;
                if (n.IsExpanded)
                    foreach (var c in FlattenVisible(n.Children))
                        yield return c;
            }
        }

        private void SelectNode(NodeViewModel n)
        {
            ExpandParents(n);
            n.IsSelected = true;
        }

        private void NavigatePrev()
        {
            var list = FlattenVisible(RootNodes).ToList();
            var idx = list.FindIndex(x => ReferenceEquals(x, Tree.SelectedItem));
            if (idx > 0) SelectNode(list[idx - 1]);
            else if (idx < 0 && list.Count > 0) SelectNode(list[0]);
        }

        private void NavigateNext()
        {
            var list = FlattenVisible(RootNodes).ToList();
            var idx = list.FindIndex(x => ReferenceEquals(x, Tree.SelectedItem));
            if (idx >= 0 && idx < list.Count - 1) SelectNode(list[idx + 1]);
            else if (idx < 0 && list.Count > 0) SelectNode(list[0]);
        }

        private void NavigateParent()
        {
            if (Tree.SelectedItem is NodeViewModel cur && cur.Parent != null)
                SelectNode(cur.Parent);
        }

        private void NavigateChild()
        {
            if (Tree.SelectedItem is NodeViewModel cur && cur.Children.FirstOrDefault(c => c.IsVisible) is { } first)
                SelectNode(first);
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SettingsWindow(ThemeConfigPath, HotkeyConfigPath) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _hotkeys = HotkeyManager.Load(HotkeyConfigPath);
                HotkeyManager.ApplyToWindow(this, _hotkeys, CreateSectionCmd, CreateDocumentCmd, NavPrevCmd, NavNextCmd, NavParentCmd, NavChildCmd);
                ApplyFontSizes();
                _appSettings = AppSettings.Load();
                ApplySyncSettings();
                Status("Настройки сохранены");
            }
        }

        private void LoadWorkspaceFromConfig()
        {
            try
            {
                if (!File.Exists(WorkspaceConfigPath)) return;
                var p = File.ReadAllText(WorkspaceConfigPath).Trim();
                if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) _workspacePath = p;
            }
            catch { }
        }

        private void SaveWorkspaceToConfig()
        {
            try
            {
                Directory.CreateDirectory(_appConfigDir);
                File.WriteAllText(WorkspaceConfigPath, _workspacePath);
            }
            catch { }
        }

        private static MediaColor ColorFromHex(string hex)
            => (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;

        private void InitializeWorkspace()
        {
            try
            {
                Directory.CreateDirectory(_workspacePath);
                WorkspaceText.Text = $"Рабочая папка: {_workspacePath}";
                SaveWorkspaceToConfig();
            }
            catch (Exception ex)
            {
                ShowError("Не удалось подготовить рабочую папку", ex);
            }
        }

        private void EnsureDefaultStructure()
        {
            try
            {
                if (Directory.EnumerateDirectories(_workspacePath).Any()) return;
                var docDir = Path.Combine(_workspacePath, "Раздел 1", "Заметка 1");
                Directory.CreateDirectory(docDir);
                EnsureContentExists(docDir);
            }
            catch (Exception ex)
            {
                ShowError("Не удалось создать базовую структуру", ex);
            }
        }

        private void LoadTree()
        {
            RootNodes.Clear();
            _projectsGroup.IsSelected = false;
            _sharedGroup.IsSelected = false;
            _todayNode.IsSelected = false;
            _contentCache.Clear();
            if (!Directory.Exists(_workspacePath)) return;
            EnsureTodayNode();

            foreach (var sectionDir in Directory.GetDirectories(_workspacePath).OrderBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase))
            {
                var name = Path.GetFileName(sectionDir);
                if (name.StartsWith('.')) continue;
                var sectionNode = new NodeViewModel { Name = name, FullPath = sectionDir, Type = NodeType.Section };
                LoadChildrenRecursive(sectionNode, sectionDir);
                RootNodes.Add(sectionNode);
            }
            LoadClientProjects();
            LoadClientSoftware();
            RefreshSharedGroup();
            WarmSearchCache();
        }

        private void LoadChildrenRecursive(NodeViewModel parent, string directory)
        {
            var dirs = Directory.GetDirectories(directory)
                .Where(d => !Path.GetFileName(d).Equals("_assets", StringComparison.OrdinalIgnoreCase))
                .Select(d => (Path: d, IsClient: ClientStore.IsClientFolder(d)))
                .ToList();
            var ordered = dirs.Where(d => d.IsClient).OrderBy(d => Path.GetFileName(d.Path), StringComparer.CurrentCultureIgnoreCase)
                .Concat(dirs.Where(d => !d.IsClient).OrderByDescending(d => CreatedAt(d.Path)));

            foreach (var (dir, isClient) in ordered)
            {
                var node = new NodeViewModel
                {
                    Name = Path.GetFileName(dir),
                    FullPath = dir,
                    Type = isClient ? NodeType.Client : NodeType.Document,
                    Parent = parent
                };
                parent.Children.Add(node);
                LoadChildrenRecursive(node, dir);
                LoadAttachments(node);
            }
        }

        private static DateTime CreatedAt(string folder)
        {
            try { return Directory.GetCreationTimeUtc(folder); } catch { return DateTime.MinValue; }
        }

        private static int InsertIndex(NodeViewModel parent, NodeType type, string name)
        {
            var clients = parent.Children.Count(c => c.Type == NodeType.Client);
            if (type == NodeType.Document) return clients;
            if (type == NodeType.Client)
            {
                var i = 0;
                while (i < clients && string.Compare(parent.Children[i].Name, name, StringComparison.CurrentCultureIgnoreCase) < 0) i++;
                return i;
            }
            return parent.Children.Count;
        }

        private void LoadAttachments(NodeViewModel docNode)
        {
            var assets = GetAssetsDir(docNode.FullPath);
            if (!Directory.Exists(assets)) return;

            foreach (var file in Directory.GetFiles(assets).OrderBy(Path.GetFileName))
            {
                if (!ImageExts.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                docNode.Children.Add(new NodeViewModel
                {
                    Name = Path.GetFileName(file),
                    FullPath = file,
                    Type = NodeType.Attachment,
                    Parent = docNode
                });
            }
        }

        private static string GetAssetsDir(string docDir) => Path.Combine(docDir, "_assets");

        private void ExpandAndSelectFirstDocument()
        {
            if (RootNodes.Contains(_todayNode))
            {
                SelectNode(_todayNode);
                return;
            }
            var first = Flatten(RootNodes).FirstOrDefault(n => n.Type is NodeType.Document or NodeType.Client);
            if (first != null) SelectNode(first);
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
            for (var p = node.Parent; p != null; p = p.Parent)
                p.IsExpanded = true;
        }

        private static bool IsSelfOrDescendant(NodeViewModel? node, NodeViewModel ancestor)
        {
            for (var n = node; n != null; n = n.Parent)
                if (ReferenceEquals(n, ancestor)) return true;
            return false;
        }

        private void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var node = Tree.SelectedItem as NodeViewModel;
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(Tree.SelectedItem, node)) OnNodeSelected(node);
            }, DispatcherPriority.Input);
        }

        private void Tree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = FindTreeViewItem(e.OriginalSource as DependencyObject);
            if (item?.DataContext is NodeViewModel { IsVirtual: true })
            {
                e.Handled = true;
                return;
            }
            if (item != null)
            {
                item.IsSelected = true;
                item.Focus();
            }
        }

        private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FindTreeViewItem(e.OriginalSource as DependencyObject)?.DataContext is NodeViewModel { Type: NodeType.Attachment } n)
            {
                OpenFile(n.FullPath);
                e.Handled = true;
            }
        }

        private void Tree_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (Tree.SelectedItem is not NodeViewModel node) return;
            if (node.IsVirtual && e.Key is Key.F2 or Key.Delete)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F2)
            {
                RenameNode(node);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete)
            {
                if (node.Type == NodeType.Attachment) DeleteAttachment(node);
                else DeleteNode(node);
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (node.Type == NodeType.Document) Editor.Focus();
                else if (node.Type == NodeType.Attachment) OpenFile(node.FullPath);
                else node.IsExpanded = !node.IsExpanded;
                e.Handled = true;
            }
        }

        private static TreeViewItem? FindTreeViewItem(DependencyObject? source)
        {
            while (source != null && source is not TreeViewItem)
            {
                source = source switch
                {
                    Visual v => VisualTreeHelper.GetParent(v),
                    FrameworkContentElement fce => fce.Parent,
                    _ => null
                };
            }
            return source as TreeViewItem;
        }

        private void OnNodeSelected(NodeViewModel? node)
        {
            if (TryRebind(node)) return;
            if (node?.Type == NodeType.Project)
            {
                OpenProject(node);
                return;
            }
            var previous = _currentDocNode ?? _currentClientNode;
            if (_revertingSelection)
            {
                _revertingSelection = false;
                if (ReferenceEquals(node, previous)) return;
            }
            if (!SaveAll())
            {
                if (previous != null && !ReferenceEquals(node, previous))
                {
                    _revertingSelection = true;
                    SelectNode(previous);
                }
                return;
            }
            _saveTimer.Stop();

            _currentDocNode = null;
            _currentClientNode = null;
            DetachCurrentClient();
            HideShared();
            HideToday();
            _isDirty = false;
            _clientDirty = false;

            if (node?.Type == NodeType.Document) LoadDocument(node);
            else if (node?.Type == NodeType.Client) LoadClient(node);
            else if (node?.Type == NodeType.SharedProject) ShowShared(node);
            else if (node?.Type == NodeType.Today) ShowToday();
            else ShowEmpty(node);

            UpdateTitle();
            UpdateNoteReminderButton();
            UpdateTimeButton();
        }

        private NodeViewModel? _emptyStateNode;

        private void ShowEmpty(NodeViewModel? node)
        {
            ClientView.Clear();
            ClientView.Visibility = Visibility.Collapsed;
            EditorHost.Visibility = Visibility.Visible;
            FormatBar.IsEnabled = false;

            _isLoadingDocument = true;
            Editor.Document = CreateEmptyDocument();
            _isLoadingDocument = false;
            Editor.IsReadOnly = true;
            Editor.Visibility = Visibility.Hidden;
            EditorHint.Visibility = Visibility.Collapsed;

            _emptyStateNode = node;
            AttachmentPreview.Source = null;
            AttachmentPreview.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            EmptyStateButton.Visibility = Visibility.Collapsed;

            switch (node?.Type)
            {
                case NodeType.Section:
                    var count = node.Children.Count;
                    EmptyStateText.Text = count == 0
                        ? "В разделе пока пусто. Создайте первую заметку."
                        : "Выберите заметку слева или создайте новую.";
                    EmptyStateButton.Content = "Создать заметку";
                    EmptyStateButton.Visibility = Visibility.Visible;
                    break;
                case NodeType.ProjectGroup:
                    EmptyStateText.Text = "Здесь открытые проекты всех клиентов. Нажмите на проект, чтобы открыть его в карточке клиента.";
                    break;
                case NodeType.SharedGroup:
                    var hidden = HiddenSharedCount();
                    EmptyStateText.Text = "Проекты, которыми поделились вы и коллеги. Нажмите на проект, чтобы посмотреть, кто над ним работает и как идут дела." +
                                          (hidden > 0 ? $"\n\nСкрытых проектов: {hidden}." : "");
                    if (hidden > 0)
                    {
                        EmptyStateButton.Content = "Показать скрытые";
                        EmptyStateButton.Visibility = Visibility.Visible;
                    }
                    break;
                case NodeType.Attachment:
                    EmptyState.Visibility = Visibility.Collapsed;
                    ShowAttachmentPreview(node.FullPath);
                    break;
                default:
                    EmptyStateText.Text = RootNodes.All(n => n.IsVirtual)
                        ? "Здесь пока пусто. Создайте раздел или клиента кнопками сверху."
                        : "Выберите заметку или клиента слева.";
                    break;
            }

            CurrentKindText.Text = node?.Type switch
            {
                NodeType.Section => "Раздел",
                NodeType.Attachment => "Картинка",
                _ => ""
            };
            CurrentFileText.Text = node?.Name ?? "";
            RenameBtn.Visibility = node == null || node.IsVirtual ? Visibility.Collapsed : Visibility.Visible;
            DirtyText.Text = "";
        }

        private void ShowAttachmentPreview(string path)
        {
            try
            {
                var bi = new System.Windows.Media.Imaging.BitmapImage();
                bi.BeginInit();
                bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                AttachmentPreview.Source = bi;
                AttachmentPreview.Visibility = Visibility.Visible;
            }
            catch
            {
                EmptyState.Visibility = Visibility.Visible;
                EmptyStateText.Text = "Картинку не удалось показать. Двойной клик в дереве откроет её в просмотрщике.";
            }
        }

        private void EmptyStateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_emptyStateNode?.Type == NodeType.SharedGroup)
            {
                UnhideAllShared();
                ShowEmpty(_emptyStateNode);
                return;
            }
            if (_emptyStateNode != null) AddDocumentUnder(_emptyStateNode);
        }

        private void RenameCurrent_Click(object sender, RoutedEventArgs e)
        {
            var node = _currentDocNode ?? _currentClientNode ?? _emptyStateNode;
            if (node != null) RenameNode(node);
        }

        private void UpdateEditorHint()
        {
            var empty = _currentDocNode != null && Editor.Document.Blocks.Count <= 1 &&
                        Editor.Document.Blocks.FirstBlock is not Table &&
                        new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text.Trim().Length == 0 &&
                        !Editor.Document.Blocks.Any(b => b is BlockUIContainer) &&
                        !(Editor.Document.Blocks.FirstBlock is Paragraph p && p.Inlines.OfType<InlineUIContainer>().Any());
            EditorHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ShowEditor()
        {
            ClientView.Clear();
            ClientView.Visibility = Visibility.Collapsed;
            EditorHost.Visibility = Visibility.Visible;
            EmptyState.Visibility = Visibility.Collapsed;
            AttachmentPreview.Visibility = Visibility.Collapsed;
            AttachmentPreview.Source = null;
            Editor.Visibility = Visibility.Visible;
            Editor.IsReadOnly = false;
            FormatBar.IsEnabled = true;
            RenameBtn.Visibility = Visibility.Visible;
            _emptyStateNode = null;
        }

        private static string GetXamlPackPath(string docFolder) => Path.Combine(docFolder, "content.xamlpack");
        private static string GetRtfPath(string docFolder) => Path.Combine(docFolder, "content.rtf");

        private void LoadDocument(NodeViewModel node)
        {
            ShowEditor();
            try
            {
                _isLoadingDocument = true;
                Editor.Document = LoadFlowDocument(node.FullPath);
                Editor.CaretPosition = Editor.Document.ContentStart;
                _currentDocNode = node;
                CurrentKindText.Text = node.Parent?.Type is NodeType.Client or NodeType.Section or NodeType.Document ? node.Parent.Name : "";
                CurrentFileText.Text = node.Name;
                DirtyText.Text = "";
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть заметку", ex, "Рядом с заметкой может лежать резервная копия content.xamlpack.bak. Откройте папку заметки через меню по правой кнопке.");
                ShowEmpty(null);
            }
            finally
            {
                _isLoadingDocument = false;
            }

            Editor.ScrollToHome();
            UpdateEditorHint();
            HighlightFirstMatchOfActiveSearch();
        }

        private void LoadClient(NodeViewModel node)
        {
            try
            {
                var card = ClientStore.Load(node.FullPath);
                _currentClient = card;
                _currentClientNode = node;
                card.Changed += CurrentClient_Changed;

                EditorHost.Visibility = Visibility.Collapsed;
                ClientView.Visibility = Visibility.Visible;
                FormatBar.IsEnabled = false;
                RenameBtn.Visibility = Visibility.Visible;
                _emptyStateNode = null;
                ClientView.Show(node, card, ClientRemindersFor(node, card));
                ClientView.ShowTime(TimeStore.ScanFolder(_workspacePath, node.FullPath));

                CurrentKindText.Text = "Клиент";
                CurrentFileText.Text = node.Name;
                DirtyText.Text = "";
                SyncSoon();
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть карточку клиента", ex);
                ShowEmpty(null);
            }
        }

        private void CurrentClient_Changed(object? sender, EventArgs e)
        {
            if (!ReferenceEquals(_currentClient, sender)) return;
            _clientDirty = true;
            DirtyText.Text = "Сохраняется…";
            UpdateTitle();
            RestartSaveTimer();
        }

        private void DetachCurrentClient()
        {
            if (_currentClient != null) _currentClient.Changed -= CurrentClient_Changed;
            _currentClient = null;
            DetachClientReminders();
        }

        private void UpdateTitle()
        {
            var name = _currentDocNode?.Name ?? _currentClientNode?.Name;
            var dirty = _isDirty || _clientDirty ? "• " : "";
            Title = name == null ? AppTitle : $"{dirty}{name} — {AppTitle}";
        }

        private void RestartSaveTimer()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveFlowDocumentAsXamlPackage(FlowDocument doc, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite))
            {
                new TextRange(doc.ContentStart, doc.ContentEnd).Save(fs, System.Windows.DataFormats.XamlPackage);
            }
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }

        private static FlowDocument LoadPackage(string path, string format)
        {
            var d = new FlowDocument();
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                new TextRange(d.ContentStart, d.ContentEnd).Load(fs, format);
            StripDefaultForeground(d);
            return d;
        }

        private static void StripDefaultForeground(DependencyObject root)
        {
            var current = ThemeManager.Current.Text;
            bool IsDefault(MediaColor c)
            {
                var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                return hex is "#F5F5F5" or "#1B1B1B" || current.EndsWith(hex.Substring(1), StringComparison.OrdinalIgnoreCase);
            }
            void Walk(DependencyObject o)
            {
                if (o.ReadLocalValue(TextElement.ForegroundProperty) is SolidColorBrush b && IsDefault(b.Color))
                    o.ClearValue(TextElement.ForegroundProperty);
                foreach (var child in LogicalTreeHelper.GetChildren(o).OfType<DependencyObject>())
                    Walk(child);
            }
            Walk(root);
        }

        private FlowDocument LoadFlowDocument(string docFolder)
        {
            var xamlPath = GetXamlPackPath(docFolder);
            var bakPath = xamlPath + ".bak";
            var rtfPath = GetRtfPath(docFolder);

            if (File.Exists(xamlPath))
            {
                try
                {
                    return LoadPackage(xamlPath, System.Windows.DataFormats.XamlPackage);
                }
                catch when (File.Exists(bakPath))
                {
                    Status("Заметка повреждена, открыта предыдущая сохранённая версия");
                    return LoadPackage(bakPath, System.Windows.DataFormats.XamlPackage);
                }
            }
            if (File.Exists(bakPath)) return LoadPackage(bakPath, System.Windows.DataFormats.XamlPackage);
            if (File.Exists(rtfPath)) return LoadPackage(rtfPath, System.Windows.DataFormats.Rtf);

            var d = CreateEmptyDocument();
            SaveFlowDocumentAsXamlPackage(d, xamlPath);
            return d;
        }

        private bool _revertingSelection;

        private bool SaveAll(bool quiet = false)
        {
            var ok = true;
            if (_isDirty && _currentDocNode != null) ok &= SaveCurrentDocument(quiet);
            if (_clientDirty && _currentClientNode != null) ok &= SaveCurrentClient(quiet);
            return ok;
        }

        private void ReportSaveError(string what, Exception ex, string hint, bool quiet)
        {
            if (quiet)
            {
                LogError(what, ex);
                DirtyText.Text = "Не сохранено";
                Status(what + ". Нажмите Ctrl+S, чтобы повторить");
                return;
            }
            ShowError(what, ex, hint);
        }

        private bool SaveCurrentDocument(bool quiet = false)
        {
            if (_currentDocNode == null) return true;
            try
            {
                var xamlPath = GetXamlPackPath(_currentDocNode.FullPath);
                SaveFlowDocumentAsXamlPackage(Editor.Document, xamlPath);
                _isDirty = false;
                MarkSaved();

                _contentCache[_currentDocNode.FullPath] = new ContentCacheEntry
                {
                    MTimeUtc = File.GetLastWriteTimeUtc(xamlPath),
                    Text = new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text ?? ""
                };
                return true;
            }
            catch (Exception ex)
            {
                ReportSaveError("Не удалось сохранить заметку", ex, "Текст остался в окне. Проверьте, что рабочая папка доступна, и нажмите Ctrl+S.", quiet);
                return false;
            }
        }

        private bool SaveCurrentClient(bool quiet = false)
        {
            if (_currentClientNode == null || _currentClient == null) return true;
            try
            {
                ClientStore.Save(_currentClientNode.FullPath, _currentClient);
                _clientDirty = false;
                UpdateClientProjects(_currentClientNode, _currentClient.Projects);
                UpdateClientSoftware(_currentClientNode, _currentClient.Software);
                if (_currentClient.Projects.Any(p => p.SharedId != null && SharedProjects.Hash(p) != p.SharedHash)) SyncSoon();
                _contentCache.Remove(_currentClientNode.FullPath);
                MarkSaved();
                if (_reminderTimer != null) UpdateBell(CollectReminders());
                return true;
            }
            catch (Exception ex)
            {
                ReportSaveError("Не удалось сохранить карточку клиента", ex, "Данные остались в окне. Проверьте, что рабочая папка доступна, и нажмите Ctrl+S.", quiet);
                return false;
            }
        }

        private void MarkSaved()
        {
            DirtyText.Text = "Сохранено в " + DateTime.Now.ToString("HH:mm");
            Status("Сохранено");
            UpdateTitle();
        }

        private void InsertImage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDocNode == null)
            {
                Status("Сначала откройте заметку");
                return;
            }

            var dlg = new WpfOpenFileDialog
            {
                Title = "Выберите картинку",
                Filter = "Картинки|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true)
                foreach (var path in dlg.FileNames)
                    InsertImageAtCaret(path);
        }

        private void InsertImageAtCaret(string path)
        {
            try
            {
                if (!File.Exists(path) || _currentDocNode == null) return;

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

                var inline = new InlineUIContainer(img, Editor.CaretPosition);
                Editor.CaretPosition = inline.ElementEnd;
                Status($"Картинка вставлена: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось вставить картинку", ex);
            }
        }

        private static string[] DroppedImages(System.Windows.IDataObject data)
        {
            if (!data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return Array.Empty<string>();
            var files = data.GetData(System.Windows.DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
            return files.Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant())).ToArray();
        }

        private void Editor_PreviewDragOver(object sender, WpfDragEventArgs e)
        {
            if (_currentDocNode == null || DroppedImages(e.Data).Length == 0) return;
            e.Effects = WpfDragDropEffects.Copy;
            e.Handled = true;
            var pos = Editor.GetPositionFromPoint(e.GetPosition(Editor), true);
            if (pos != null) Editor.CaretPosition = pos;
        }

        private void Editor_Drop(object sender, WpfDragEventArgs e)
        {
            var images = DroppedImages(e.Data);
            if (images.Length == 0) return;
            foreach (var f in images) InsertImageAtCaret(f);
            e.Handled = true;
        }

        private static string MakeUniqueFileName(string folder, string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var candidate = Path.Combine(folder, name + ext);
            for (var i = 1; File.Exists(candidate); i++)
                candidate = Path.Combine(folder, $"{name} ({i}){ext}");
            return candidate;
        }

        private static NodeViewModel? AttachTarget(NodeViewModel? node)
        {
            var target = node?.Type == NodeType.Attachment ? node.Parent : node;
            return target?.Type is NodeType.Document or NodeType.Client ? target : null;
        }

        private void Tree_DragOver(object sender, WpfDragEventArgs e)
        {
            e.Effects = WpfDragDropEffects.None;
            var node = FindTreeViewItem(e.OriginalSource as DependencyObject)?.DataContext as NodeViewModel;
            if (AttachTarget(node) != null && DroppedImages(e.Data).Length > 0)
                e.Effects = WpfDragDropEffects.Copy;
            e.Handled = true;
        }

        private void Tree_Drop(object sender, WpfDragEventArgs e)
        {
            var target = AttachTarget(FindTreeViewItem(e.OriginalSource as DependencyObject)?.DataContext as NodeViewModel);
            var images = DroppedImages(e.Data);
            if (target == null || images.Length == 0) return;
            AddAttachments(target, images);
            e.Handled = true;
        }

        private void AddAttachments(NodeViewModel target, IEnumerable<string> files)
        {
            try
            {
                var assets = GetAssetsDir(target.FullPath);
                Directory.CreateDirectory(assets);
                foreach (var f in files)
                {
                    var dest = MakeUniqueFileName(assets, Path.GetFileName(f));
                    File.Copy(f, dest);
                    target.Children.Add(new NodeViewModel
                    {
                        Name = Path.GetFileName(dest),
                        FullPath = dest,
                        Type = NodeType.Attachment,
                        Parent = target
                    });
                }
                target.IsExpanded = true;
                Status("Картинки добавлены к заметке");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось добавить картинку", ex);
            }
        }

        private static string SanitizeName(string name)
        {
            var invalid = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            name = Regex.Replace(name.Trim(), $"[{invalid}]+", " ");
            name = Regex.Replace(name, @"\s{2,}", " ").Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "Без имени" : name;
        }

        private static string MakeUniqueName(string parentDir, string baseName)
        {
            var candidate = baseName;
            for (var i = 1; Directory.Exists(Path.Combine(parentDir, candidate)) || File.Exists(Path.Combine(parentDir, candidate)); i++)
                candidate = $"{baseName} ({i})";
            return candidate;
        }

        private void ChooseWorkspace_Click(object sender, RoutedEventArgs e)
        {
            SaveAll();
            using var dlg = new WF.FolderBrowserDialog
            {
                Description = "Выберите папку для заметок",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                SelectedPath = _workspacePath
            };
            if (dlg.ShowDialog() != WF.DialogResult.OK) return;

            _workspacePath = dlg.SelectedPath;
            WorkspaceText.Text = $"Рабочая папка: {_workspacePath}";
            SaveWorkspaceToConfig();

            _currentDocNode = null;
            _currentClientNode = null;
            _isDirty = _clientDirty = false;
            ShowEmpty(null);
            LoadTree();
            LoadGeneralReminders();
            ExpandAndSelectFirstDocument();
            RefreshReminders();
            Status("Рабочая папка изменена");
        }

        private void Status(string text) => StatusText.Text = text;

        private void ShowError(string what, Exception ex, string? hint = null)
        {
            LogError(what, ex);
            var message = string.IsNullOrEmpty(hint) ? ex.Message : $"{hint}\n\nПодробности: {ex.Message}";
            ThemedMessageBox.Show(this, what, message, MessageBoxImage.Error);
        }

        private void LogError(string what, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(_appConfigDir);
                File.AppendAllText(Path.Combine(_appConfigDir, "error.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {what}\n{ex}\n\n");
            }
            catch { }
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateEditorHint();
            if (_isLoadingDocument || _currentDocNode == null) return;
            if (!_isDirty)
            {
                _isDirty = true;
                DirtyText.Text = "Сохраняется…";
                UpdateTitle();
            }
            RestartSaveTimer();
        }

        private void BoldBtn_Click(object sender, RoutedEventArgs e)
            => ApplyToSelection(TextElement.FontWeightProperty, BoldBtn.IsChecked == true ? FontWeights.Bold : FontWeights.Normal);

        private void ItalicBtn_Click(object sender, RoutedEventArgs e)
            => ApplyToSelection(TextElement.FontStyleProperty, ItalicBtn.IsChecked == true ? FontStyles.Italic : FontStyles.Normal);

        private void UnderlineBtn_Click(object sender, RoutedEventArgs e)
            => ApplyToSelection(Inline.TextDecorationsProperty, UnderlineBtn.IsChecked == true ? TextDecorations.Underline : new TextDecorationCollection());

        private void ApplyToSelection(DependencyProperty property, object value)
        {
            if (_currentDocNode == null) return;
            Editor.Selection.ApplyPropertyValue(property, value);
            Editor.Focus();
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSelectionUi) return;
            _isUpdatingSelectionUi = true;
            try
            {
                var sel = Editor.Selection;
                BoldBtn.IsChecked = Equals(sel.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold);
                ItalicBtn.IsChecked = Equals(sel.GetPropertyValue(TextElement.FontStyleProperty), FontStyles.Italic);
                UnderlineBtn.IsChecked = sel.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection td && td.Count > 0;

                ColorCombo.SelectedIndex = sel.GetPropertyValue(TextElement.ForegroundProperty) is SolidColorBrush br
                    ? _colors.FindIndex(c => CloseEnough(c.Brush.Color, br.Color))
                    : -1;
            }
            finally
            {
                _isUpdatingSelectionUi = false;
            }
        }

        private static bool CloseEnough(MediaColor a, MediaColor b)
            => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 30;

        private void ColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingSelectionUi || ColorCombo.SelectedItem is not ColorItem item) return;
            ApplyToSelection(TextElement.ForegroundProperty, new SolidColorBrush(item.Brush.Color));
        }

        private void ClearFormatting_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDocNode == null) return;
            new TextRange(Editor.Selection.Start, Editor.Selection.End).ClearAllProperties();
            Editor.Focus();
        }

        private void Node_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not NodeViewModel node) return;
            if (node.IsVirtual)
            {
                e.Handled = true;
                return;
            }

            var cm = new System.Windows.Controls.ContextMenu();
            void Add(string header, Action action) => cm.Items.Add(MenuItem(header, (_, _) => action()));
            void Sep() => cm.Items.Add(new Separator());

            switch (node.Type)
            {
                case NodeType.Section:
                    Add("+ Заметка", () => AddDocumentUnder(node));
                    Add("+ Клиент", () => AddClientUnder(node));
                    Add("Открыть папку", () => OpenInExplorer(node.FullPath));
                    Sep();
                    Add("Переименовать  (F2)", () => RenameNode(node));
                    Add("Удалить в корзину  (Del)", () => DeleteNode(node));
                    break;
                case NodeType.Client:
                    Add("+ Заметка по клиенту", () => AddDocumentUnder(node));
                    Add("Передать коллеге…", () => OpenTransfer(node, false));
                    Add("Добавить картинку…", () => AddAttachmentViaDialog(node));
                    Add("Открыть папку", () => OpenInExplorer(node.FullPath));
                    Sep();
                    Add("Переименовать  (F2)", () => RenameNode(node));
                    Add("Удалить в корзину  (Del)", () => DeleteNode(node));
                    break;
                case NodeType.Document:
                    Add("+ Вложенная заметка", () => AddDocumentUnder(node));
                    Add("Передать коллеге…", () => OpenTransfer(node, false));
                    Add("Вставить картинку в текст…", () => InsertImage_Click(this, new RoutedEventArgs()));
                    Add("Добавить картинку рядом…", () => AddAttachmentViaDialog(node));
                    Add("Открыть папку", () => OpenInExplorer(node.FullPath));
                    Sep();
                    Add("Переименовать  (F2)", () => RenameNode(node));
                    Add("Удалить в корзину  (Del)", () => DeleteNode(node));
                    break;
                case NodeType.Attachment:
                    Add("Открыть", () => OpenFile(node.FullPath));
                    Add("Показать в проводнике", () => OpenInExplorer(node.FullPath));
                    Sep();
                    Add("Переименовать  (F2)", () => RenameAttachment(node));
                    Add("Удалить в корзину  (Del)", () => DeleteAttachment(node));
                    break;
            }

            fe.ContextMenu = cm;
        }

        private static System.Windows.Controls.MenuItem MenuItem(string header, RoutedEventHandler onClick)
        {
            var mi = new System.Windows.Controls.MenuItem { Header = header };
            mi.Click += onClick;
            return mi;
        }

        private void AddAttachmentViaDialog(NodeViewModel docNode)
        {
            var dlg = new WpfOpenFileDialog
            {
                Title = "Выберите картинку",
                Filter = "Картинки|*.png;*.jpg;*.jpeg;*.gif;*.bmp",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) == true) AddAttachments(docNode, dlg.FileNames);
        }

        private void OpenFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть картинку", ex);
            }
        }

        private void OpenInExplorer(string path)
        {
            try
            {
                var arg = Directory.Exists(path) ? $"\"{path}\"" : $"/select,\"{path}\"";
                Process.Start(new ProcessStartInfo("explorer.exe", arg) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowError("Не удалось открыть проводник", ex);
            }
        }

        private void RenameNode(NodeViewModel node)
        {
            if (node.IsVirtual) return;
            if (node.Type == NodeType.Attachment)
            {
                RenameAttachment(node);
                return;
            }

            var prompt = node.Type switch
            {
                NodeType.Section => "Новое имя раздела",
                NodeType.Client => "Новое имя клиента",
                _ => "Новое имя заметки"
            };
            var input = TextPromptWindow.Show(this, prompt, node.Name, width: 640, getSelection: () => Editor?.Selection?.Text, okText: "Переименовать");
            if (string.IsNullOrWhiteSpace(input)) return;
            var newName = SanitizeName(input);
            if (newName == node.Name) return;

            SaveAll();
            var parentDir = Path.GetDirectoryName(node.FullPath)!;
            var caseOnly = string.Equals(newName, node.Name, StringComparison.OrdinalIgnoreCase);
            var newDir = Path.Combine(parentDir, caseOnly ? newName : MakeUniqueName(parentDir, newName));
            var oldDir = node.FullPath;

            try
            {
                if (caseOnly)
                {
                    var tmp = Path.Combine(parentDir, Guid.NewGuid().ToString("N"));
                    Directory.Move(oldDir, tmp);
                    Directory.Move(tmp, newDir);
                }
                else
                {
                    Directory.Move(oldDir, newDir);
                }

                node.FullPath = newDir;
                node.Name = Path.GetFileName(newDir);
                UpdatePathsRecursively(node, oldDir, newDir);
                MoveNoteReminders(oldDir, newDir);
                _contentCache.Clear();
                RefreshProjectsGroup();

                if (ReferenceEquals(_currentDocNode, node) || ReferenceEquals(_currentClientNode, node) || ReferenceEquals(_emptyStateNode, node))
                    CurrentFileText.Text = node.Name;
                UpdateTitle();
                Status("Переименовано");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось переименовать", ex);
            }
        }

        private void RenameAttachment(NodeViewModel node)
        {
            if (node.Type != NodeType.Attachment) return;

            var input = TextPromptWindow.Show(this, "Новое имя картинки", Path.GetFileNameWithoutExtension(node.Name), width: 640, okText: "Переименовать");
            if (string.IsNullOrWhiteSpace(input)) return;

            var dir = Path.GetDirectoryName(node.FullPath)!;
            var ext = Path.GetExtension(node.FullPath);
            var name = SanitizeName(input);
            if (name + ext == node.Name) return;

            try
            {
                var dest = MakeUniqueFileName(dir, name + ext);
                File.Move(node.FullPath, dest);
                node.FullPath = dest;
                node.Name = Path.GetFileName(dest);
                Status("Переименовано");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось переименовать картинку", ex);
            }
        }

        private static void UpdatePathsRecursively(NodeViewModel node, string oldBase, string newBase)
        {
            foreach (var ch in node.Children)
            {
                if (ch.FullPath.StartsWith(oldBase, StringComparison.OrdinalIgnoreCase))
                    ch.FullPath = newBase + ch.FullPath.Substring(oldBase.Length);
                UpdatePathsRecursively(ch, oldBase, newBase);
            }
        }

        private void DeleteNode(NodeViewModel node)
        {
            var what = node.Type switch
            {
                NodeType.Section => "раздел",
                NodeType.Client => "клиента",
                _ => "заметку"
            };
            var inside = node.Children.Any(c => c.Type != NodeType.Attachment) ? "Всё, что внутри, тоже удалится. " : "";
            if (!ThemedMessageBox.Confirm(this, $"Удалить {what} «{node.Name}»?",
                    inside + "Папка попадёт в корзину Windows, оттуда её можно восстановить.", "Удалить", image: MessageBoxImage.Warning))
                return;

            if (IsSelfOrDescendant(_currentDocNode, node) || IsSelfOrDescendant(_currentClientNode, node))
            {
                _saveTimer.Stop();
                _currentDocNode = null;
                _currentClientNode = null;
                DetachCurrentClient();
                _isDirty = _clientDirty = false;
                ShowEmpty(null);
            }
            else
            {
                SaveAll();
            }

            try
            {
                FileSystem.DeleteDirectory(node.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                if (node.Parent != null) node.Parent.Children.Remove(node);
                else RootNodes.Remove(node);
                _contentCache.Clear();
                RefreshProjectsGroup();

                var next = node.Parent ?? RootNodes.FirstOrDefault(n => !n.IsVirtual);
                if (next != null) SelectNode(next);
                Status("Удалено в корзину");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось удалить", ex);
            }
        }

        private void DeleteAttachment(NodeViewModel node)
        {
            if (node.Type != NodeType.Attachment) return;
            if (!ThemedMessageBox.Confirm(this, $"Удалить картинку «{node.Name}»?",
                    "Файл попадёт в корзину Windows, оттуда его можно восстановить.", "Удалить", image: MessageBoxImage.Warning))
                return;

            try
            {
                if (File.Exists(node.FullPath))
                    FileSystem.DeleteFile(node.FullPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                node.Parent?.Children.Remove(node);
                Status("Картинка удалена в корзину");
            }
            catch (Exception ex)
            {
                ShowError("Не удалось удалить картинку", ex);
            }
        }

        private NodeViewModel? CreateFolderNode(NodeViewModel? parent, string prompt, NodeType type)
        {
            var input = TextPromptWindow.Show(this, prompt, "", width: 640, getSelection: () => Editor?.Selection?.Text);
            if (string.IsNullOrWhiteSpace(input)) return null;
            var name = SanitizeName(input);

            try
            {
                var parentDir = parent?.FullPath ?? _workspacePath;
                var dir = Path.Combine(parentDir, MakeUniqueName(parentDir, name));
                Directory.CreateDirectory(dir);
                if (type == NodeType.Document) EnsureContentExists(dir);
                if (type == NodeType.Client) ClientStore.Save(dir, new ClientCard());

                var node = new NodeViewModel { Name = Path.GetFileName(dir), FullPath = dir, Type = type, Parent = parent };
                if (parent == null) RootNodes.Add(node);
                else
                {
                    parent.Children.Insert(InsertIndex(parent, type, node.Name), node);
                    parent.IsExpanded = true;
                }
                SelectNode(node);
                return node;
            }
            catch (Exception ex)
            {
                ShowError("Не удалось создать папку", ex);
                return null;
            }
        }

        private void AddSection_Click(object sender, RoutedEventArgs e)
        {
            var node = CreateFolderNode(null, "Название раздела", NodeType.Section);
            if (node != null) Status($"Раздел создан: {node.Name}");
        }

        private void AddClient_Click(object sender, RoutedEventArgs e)
        {
            var selected = Tree.SelectedItem as NodeViewModel;
            NodeViewModel? section = selected?.Type == NodeType.Section ? selected : null;
            if (section == null)
            {
                section = RootNodes.FirstOrDefault(n => n.Name.Equals(ClientsSectionName, StringComparison.CurrentCultureIgnoreCase));
                if (section == null)
                {
                    try
                    {
                        var dir = Path.Combine(_workspacePath, ClientsSectionName);
                        Directory.CreateDirectory(dir);
                        section = new NodeViewModel { Name = ClientsSectionName, FullPath = dir, Type = NodeType.Section };
                        RootNodes.Insert(0, section);
                    }
                    catch (Exception ex)
                    {
                        ShowError("Не удалось создать раздел «Клиенты»", ex);
                        return;
                    }
                }
            }
            AddClientUnder(section);
        }

        private void AddClientUnder(NodeViewModel section)
        {
            var node = CreateFolderNode(section, "Организация или ИП", NodeType.Client);
            if (node == null) return;
            Status($"Клиент «{node.Name}» создан, заполните карточку");
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(_currentClientNode, node)) ClientView.FocusFirstField();
            }, DispatcherPriority.ContextIdle);
        }

        private void AddDocument_Click(object sender, RoutedEventArgs e)
        {
            var selection = Tree.SelectedItem as NodeViewModel;
            if (selection?.Type == NodeType.Attachment) selection = selection.Parent;
            if (selection == null || selection.IsVirtual)
            {
                Status("Выберите слева раздел, клиента или заметку");
                return;
            }
            AddDocumentUnder(selection);
        }

        private void AddDocumentUnder(NodeViewModel parent)
        {
            var prompt = parent.Type switch
            {
                NodeType.Client => $"Новая заметка по клиенту «{parent.Name}»",
                NodeType.Document => "Название вложенной заметки",
                _ => "Название заметки"
            };
            var node = CreateFolderNode(parent, prompt, NodeType.Document);
            if (node == null) return;

            Status($"Заметка создана: {node.Name}");
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(_currentDocNode, node)) Editor.Focus();
            }, DispatcherPriority.ContextIdle);

            var q = SearchBox.Text.Trim();
            if (q.Length > 0) ApplySearch(q);
        }

        private void EnsureContentExists(string docDir)
        {
            var xamlPath = GetXamlPackPath(docDir);
            if (!File.Exists(xamlPath)) SaveFlowDocumentAsXamlPackage(CreateEmptyDocument(), xamlPath);
        }

        private static FlowDocument CreateEmptyDocument() => new(new Paragraph());

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ClearSearchBtn.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            _searchTimer.Stop();
            _searchTimer.Start();
        }

        private void SearchBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Clear();
                e.Handled = true;
            }
            else if (e.Key is Key.Enter or Key.Down)
            {
                _searchTimer.Stop();
                RunSearch();
                if (Tree.SelectedItem is NodeViewModel n && FindContainer(Tree, n) is { } tvi) tvi.Focus();
                e.Handled = true;
            }
        }

        private static TreeViewItem? FindContainer(ItemsControl parent, NodeViewModel target)
        {
            foreach (var item in parent.Items)
            {
                if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem tvi) continue;
                if (ReferenceEquals(item, target)) return tvi;
                var found = FindContainer(tvi, target);
                if (found != null) return found;
            }
            return null;
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchBox.Clear();
            SearchBox.Focus();
        }

        private void RunSearch()
        {
            ApplySearch(SearchBox.Text.Trim());
            if (_currentDocNode != null) HighlightFirstMatchOfActiveSearch();
        }

        private void ApplySearch(string q)
        {
            foreach (var root in RootNodes)
                RecalcVisibility(root, q);

            if (q.Length == 0)
            {
                _projectsGroup.IsExpanded = true;
                _sharedGroup.IsExpanded = true;
                if (Tree.SelectedItem is NodeViewModel sel) ExpandParents(sel);
                return;
            }

            var current = Tree.SelectedItem as NodeViewModel;
            if (current == null || !current.IsVisible || !current.IsHit)
            {
                var first = Flatten(RootNodes).FirstOrDefault(n => n.IsHit && n.Type is NodeType.Document or NodeType.Client)
                            ?? Flatten(RootNodes).FirstOrDefault(n => n.IsHit && !n.IsVirtual);
                if (first != null) SelectNode(first);
            }
        }

        private bool RecalcVisibility(NodeViewModel node, string q)
        {
            var childHit = false;
            foreach (var ch in node.Children)
                childHit |= RecalcVisibility(ch, q);

            if (q.Length == 0)
            {
                node.IsVisible = true;
                node.IsHit = false;
                return true;
            }

            var selfHit = Contains(node.Name, q) || (node.Type is NodeType.Project or NodeType.SharedProject && Contains(node.Hint, q)) ||
                          (node.Type is NodeType.Document or NodeType.Client && NodeContentContains(node, q));
            node.IsVisible = selfHit || childHit;
            node.IsHit = selfHit;
            node.IsExpanded = childHit;
            return node.IsVisible;
        }

        private static bool Contains(string text, string q)
        {
            if (CultureInfo.CurrentCulture.CompareInfo.IndexOf(text, q, CompareOptions.IgnoreCase) >= 0) return true;
            if (!Regex.IsMatch(q, @"^[\d\s\-+()]+$")) return false;
            var digits = new string(q.Where(char.IsDigit).ToArray());
            return digits.Length >= 3 && text.Contains(digits);
        }

        private bool NodeContentContains(NodeViewModel node, string q)
        {
            try
            {
                return Contains(GetNodeSearchText(node), q);
            }
            catch
            {
                return false;
            }
        }

        private string GetNodeSearchText(NodeViewModel node)
        {
            if (node.Type == NodeType.Client)
            {
                if (ReferenceEquals(node, _currentClientNode) && _currentClient != null) return _currentClient.SearchText();
                return Cached(node.FullPath, ClientStore.CardPath(node.FullPath), () => ClientStore.SearchText(node.FullPath));
            }

            if (ReferenceEquals(node, _currentDocNode))
                return new TextRange(Editor.Document.ContentStart, Editor.Document.ContentEnd).Text ?? "";

            var xaml = GetXamlPackPath(node.FullPath);
            var rtf = GetRtfPath(node.FullPath);
            var path = File.Exists(xaml) ? xaml : File.Exists(rtf) ? rtf : null;
            if (path == null) return "";

            return Cached(node.FullPath, path, () =>
            {
                if (path == rtf) return RtfText.Extract(File.ReadAllText(path));
                var doc = LoadPackage(path, System.Windows.DataFormats.XamlPackage);
                return new TextRange(doc.ContentStart, doc.ContentEnd).Text ?? "";
            });
        }

        private const string ProjectsGroupPath = "::projects";
        private readonly NodeViewModel _projectsGroup = new() { Name = "Проекты", FullPath = ProjectsGroupPath, Type = NodeType.ProjectGroup, IsExpanded = true };
        private readonly Dictionary<NodeViewModel, List<ClientProject>> _clientProjects = new();

        private void LoadClientProjects()
        {
            _clientProjects.Clear();
            foreach (var client in Flatten(RootNodes).Where(n => n.Type == NodeType.Client).ToList())
            {
                var list = ClientStore.ReadProjects(client.FullPath);
                if (list.Count > 0) _clientProjects[client] = list;
            }
            RefreshProjectsGroup();
        }

        private void UpdateClientProjects(NodeViewModel client, IEnumerable<ClientProject> projects)
        {
            var list = projects.ToList();
            if (list.Count > 0) _clientProjects[client] = list;
            else _clientProjects.Remove(client);
            RefreshProjectsGroup();
        }

        private void RefreshProjectsGroup()
        {
            var clients = new HashSet<NodeViewModel>(Flatten(RootNodes.Where(n => !n.IsVirtual)).Where(n => n.Type == NodeType.Client));
            foreach (var gone in _clientProjects.Keys.Where(k => !clients.Contains(k)).ToList())
                _clientProjects.Remove(gone);

            var items = _clientProjects
                .SelectMany(kv => kv.Value.Select((p, i) => (Client: kv.Key, Project: p, Index: i)))
                .Where(x => !x.Project.IsClosed)
                .OrderBy(x => x.Project.Deadline ?? DateTime.MaxValue)
                .ThenBy(x => x.Client.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Index)
                .ToList();

            _projectsGroup.Children.Clear();
            foreach (var x in items)
            {
                _projectsGroup.Children.Add(new NodeViewModel
                {
                    Name = string.IsNullOrWhiteSpace(x.Project.Name) ? "Проект без названия" : x.Project.Name.Trim(),
                    Hint = ProjectHint(x.Client, x.Project),
                    FullPath = ProjectsGroupPath + "|" + x.Client.FullPath + "|" + x.Index,
                    Type = NodeType.Project,
                    Target = x.Client,
                    ProjectIndex = x.Index,
                    Parent = _projectsGroup
                });
            }

            var present = RootNodes.Contains(_projectsGroup);
            if (items.Count > 0 && !present)
            {
                _projectsGroup.IsSelected = false;
                RootNodes.Insert(VirtualIndex(_projectsGroup), _projectsGroup);
            }
            else if (items.Count == 0 && present) RootNodes.Remove(_projectsGroup);

            var q = SearchBox.Text.Trim();
            if (items.Count > 0 && q.Length > 0) RecalcVisibility(_projectsGroup, q);
        }

        private static string ProjectHint(NodeViewModel client, ClientProject p)
        {
            string when;
            if (p.Deadline is { } d)
                when = d.Date < DateTime.Today ? "срок был " + DateText.HumanDate(d) : "до " + DateText.HumanDate(d);
            else
                when = p.Status.ToLower();
            return client.Name + " · " + when + (p.IsShared ? " · общий" : "");
        }

        private void OpenProject(NodeViewModel node)
        {
            if (node.Target is not { } client) return;
            var index = node.ProjectIndex;
            SelectNode(client);
            Dispatcher.BeginInvoke(() =>
            {
                if (ReferenceEquals(_currentClientNode, client)) ClientView.ShowProject(index);
            }, DispatcherPriority.Background);
        }

        private int _warmGeneration;

        private void WarmSearchCache()
        {
            var generation = ++_warmGeneration;
            var items = Flatten(RootNodes)
                .Where(n => n.Type is NodeType.Client or NodeType.Document)
                .Select(n => (n.FullPath, n.Type))
                .ToList();

            Task.Run(() =>
            {
                var result = new List<(string Key, DateTime MTime, string Text)>();
                foreach (var (path, type) in items)
                {
                    try
                    {
                        if (type == NodeType.Client)
                        {
                            var card = ClientStore.CardPath(path);
                            if (File.Exists(card)) result.Add((path, File.GetLastWriteTimeUtc(card), ClientStore.SearchText(path)));
                        }
                        else if (!File.Exists(GetXamlPackPath(path)) && File.Exists(GetRtfPath(path)))
                        {
                            var rtf = GetRtfPath(path);
                            result.Add((path, File.GetLastWriteTimeUtc(rtf), RtfText.Extract(File.ReadAllText(rtf))));
                        }
                    }
                    catch
                    {
                    }
                }
                return result;
            }).ContinueWith(t =>
            {
                if (t.IsFaulted) return;
                Dispatcher.BeginInvoke(() =>
                {
                    if (generation != _warmGeneration) return;
                    foreach (var (key, mtime, text) in t.Result)
                        if (!_contentCache.ContainsKey(key))
                            _contentCache[key] = new ContentCacheEntry { MTimeUtc = mtime, Text = text };
                }, DispatcherPriority.Background);
            }, TaskScheduler.Default);
        }

        private string Cached(string key, string file, Func<string> read)
        {
            if (!File.Exists(file)) return "";
            var mtime = File.GetLastWriteTimeUtc(file);
            if (_contentCache.TryGetValue(key, out var c) && c.MTimeUtc == mtime) return c.Text;
            var text = read();
            _contentCache[key] = new ContentCacheEntry { MTimeUtc = mtime, Text = text };
            return text;
        }

        private void HighlightFirstMatchOfActiveSearch()
        {
            var q = SearchBox.Text.Trim();
            if (q.Length == 0 || _currentDocNode == null) return;

            var match = FindFirstInDocument(Editor.Document, q);
            if (match == null) return;

            Editor.Selection.Select(match.Start, match.End);
            Dispatcher.BeginInvoke(() =>
            {
                var rect = match.Start.GetCharacterRect(LogicalDirection.Forward);
                if (!rect.IsEmpty)
                    Editor.ScrollToVerticalOffset(Math.Max(0, Editor.VerticalOffset + rect.Top - Editor.ActualHeight / 3));
            }, DispatcherPriority.Loaded);
        }

        private static TextRange? FindFirstInDocument(FlowDocument doc, string query)
        {
            var comparer = CultureInfo.CurrentCulture.CompareInfo;
            var pos = doc.ContentStart;
            while (pos != null && pos.CompareTo(doc.ContentEnd) < 0)
            {
                if (pos.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                {
                    var run = pos.GetTextInRun(LogicalDirection.Forward);
                    var idx = comparer.IndexOf(run, query, CompareOptions.IgnoreCase);
                    if (idx >= 0)
                    {
                        var start = pos.GetPositionAtOffset(idx)!;
                        return new TextRange(start, start.GetPositionAtOffset(query.Length)!);
                    }
                    pos = pos.GetPositionAtOffset(run.Length);
                }
                else
                {
                    pos = pos.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            return null;
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (HideToTrayInsteadOfClosing())
            {
                e.Cancel = true;
                return;
            }
            _saveTimer.Stop();
            if (!SaveAll() &&
                !ThemedMessageBox.Confirm(this, "Изменения не сохранены", "Если закрыть программу сейчас, последние правки пропадут.",
                    "Закрыть без сохранения", "Остаться", MessageBoxImage.Warning))
            {
                e.Cancel = true;
                return;
            }
            SaveWindowPlacement();
            DisposeTray();
            base.OnClosing(e);
        }

        private sealed class ColorItem
        {
            public string Title { get; }
            public SolidColorBrush Brush { get; }

            public ColorItem(string title, MediaColor color)
            {
                Title = title;
                Brush = new SolidColorBrush(color);
                Brush.Freeze();
            }
        }
    }
}
