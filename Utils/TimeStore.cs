using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public sealed record TimeRow(string Folder, TimeEntry Entry, string Client, string Note, string Section);

    public static class TimeStore
    {
        public const string FileName = "time.json";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string PathFor(string folder) => Path.Combine(folder, FileName);

        public static List<TimeEntry> Load(string folder)
        {
            var path = PathFor(folder);
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<List<TimeEntry>>(File.ReadAllText(path), Options) ?? new();
            }
            catch
            {
                var bak = path + ".bak";
                try
                {
                    if (File.Exists(bak))
                        return JsonSerializer.Deserialize<List<TimeEntry>>(File.ReadAllText(bak), Options) ?? new();
                }
                catch { }
            }
            return new();
        }

        public static void Save(string folder, List<TimeEntry> items)
        {
            var path = PathFor(folder);
            if (items.Count == 0 && !File.Exists(path)) return;
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(items.OrderBy(e => e.Date).ThenBy(e => e.CreatedAt).ToList(), Options), new UTF8Encoding(false));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }

        public static int Total(string folder) => Load(folder).Sum(e => e.Minutes);

        public static List<TimeRow> ScanAll(string workspace)
        {
            var rows = new List<TimeRow>();
            if (!Directory.Exists(workspace)) return rows;
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(workspace, FileName, SearchOption.AllDirectories).ToList();
            }
            catch
            {
                return rows;
            }
            foreach (var file in files)
            {
                var folder = Path.GetDirectoryName(file)!;
                var rel = Path.GetRelativePath(workspace, folder);
                if (rel.StartsWith('.') || rel.Split(Path.DirectorySeparatorChar).Any(p => p.Equals("_assets", StringComparison.OrdinalIgnoreCase))) continue;
                var (client, note, section) = Describe(workspace, folder);
                foreach (var e in Load(folder))
                    rows.Add(new TimeRow(folder, e, client, note, section));
            }
            return rows;
        }

        public static List<TimeRow> ScanFolder(string workspace, string folder)
        {
            var rows = new List<TimeRow>();
            if (!Directory.Exists(folder)) return rows;
            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, FileName, SearchOption.AllDirectories).ToList();
            }
            catch
            {
                return rows;
            }
            foreach (var file in files)
            {
                var dir = Path.GetDirectoryName(file)!;
                var (client, note, section) = Describe(workspace, dir);
                foreach (var e in Load(dir)) rows.Add(new TimeRow(dir, e, client, note, section));
            }
            return rows;
        }

        public static (string Client, string Note, string Section) Describe(string workspace, string folder)
        {
            var rel = Path.GetRelativePath(workspace, folder);
            var parts = rel.Split(Path.DirectorySeparatorChar);
            var section = parts.Length > 0 ? parts[0] : "";
            string client = "";
            var clientDepth = -1;
            var dir = workspace;
            for (var i = 0; i < parts.Length; i++)
            {
                dir = Path.Combine(dir, parts[i]);
                if (ClientStore.IsClientFolder(dir))
                {
                    client = parts[i];
                    clientDepth = i;
                }
            }
            var note = clientDepth == parts.Length - 1 ? "" : parts.Length > 1 ? parts[^1] : "";
            return (client, note, section);
        }
    }

    public static class SoftwareCatalog
    {
        public static System.Collections.ObjectModel.ObservableCollection<string> Programs { get; } = new();
        public static System.Collections.ObjectModel.ObservableCollection<string> Addons { get; } = new();

        public static void Rebuild(IEnumerable<ClientSoftware> all)
        {
            var list = all.ToList();
            Fill(Programs, list.Select(s => s.Name));
            Fill(Addons, list.SelectMany(s => s.Addons).Select(a => a.Name));
        }

        public static void Add(IEnumerable<ClientSoftware> items)
        {
            foreach (var s in items)
            {
                AddOne(Programs, s.Name);
                foreach (var a in s.Addons) AddOne(Addons, a.Name);
            }
        }

        private static void Fill(System.Collections.ObjectModel.ObservableCollection<string> target, IEnumerable<string> names)
        {
            target.Clear();
            foreach (var n in names.Select(n => (n ?? "").Trim()).Where(n => n.Length > 0)
                         .GroupBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                         .OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                         .Select(g => g.First()))
                target.Add(n);
        }

        private static void AddOne(System.Collections.ObjectModel.ObservableCollection<string> target, string name)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0 || target.Any(x => x.Equals(n, StringComparison.CurrentCultureIgnoreCase))) return;
            target.Add(n);
        }
    }
}
