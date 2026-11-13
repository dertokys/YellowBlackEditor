using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace YellowBlackEditor.Utils
{
    public static class Updates
    {
        public const string FolderName = "Обновления";
        public const string CheckpointsFile = "контрольные точки.txt";
        private const string FormatFile = "формат данных.json";
        public const int DataFormat = 2;

        public sealed record Installer(Version Version, string Path);

        private sealed class FormatInfo
        {
            public int Format { get; set; }
            public string Version { get; set; } = "";
        }

        public static Version Current
        {
            get
            {
                var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static string Root(string exchange) => System.IO.Path.Combine(exchange, FolderName);

        public static List<Installer> Find(string exchange)
        {
            var dir = Root(exchange);
            if (!Directory.Exists(dir)) return new();
            var list = new List<Installer>();
            foreach (var f in Directory.GetFiles(dir, "YellowBlackNotes-Setup-*.exe"))
            {
                var m = Regex.Match(System.IO.Path.GetFileName(f), @"^YellowBlackNotes-Setup-(\d+)\.(\d+)\.(\d+)\.exe$", RegexOptions.IgnoreCase);
                if (m.Success) list.Add(new Installer(new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)), f));
            }
            return list.OrderBy(i => i.Version).ToList();
        }

        public static List<Version> Checkpoints(string exchange)
        {
            var path = System.IO.Path.Combine(Root(exchange), CheckpointsFile);
            var list = new List<Version>();
            if (!File.Exists(path)) return list;
            foreach (Match m in Regex.Matches(File.ReadAllText(path), @"(\d+)\.(\d+)\.(\d+)"))
                list.Add(new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)));
            return list.Distinct().OrderBy(v => v).ToList();
        }

        public static Installer? Target(string exchange)
        {
            var current = Current;
            var newer = Find(exchange).Where(i => i.Version > current).ToList();
            if (newer.Count == 0) return null;
            foreach (var cp in Checkpoints(exchange).Where(v => v > current))
            {
                var stop = newer.FirstOrDefault(i => i.Version == cp);
                if (stop != null) return stop;
            }
            return newer.Last();
        }

        public static (int Format, string Version) RequiredFormat(string exchange)
        {
            try
            {
                var path = System.IO.Path.Combine(Root(exchange), FormatFile);
                if (!File.Exists(path)) return (0, "");
                var f = JsonSerializer.Deserialize<FormatInfo>(File.ReadAllText(path));
                return f == null ? (0, "") : (f.Format, f.Version);
            }
            catch
            {
                return (0, "");
            }
        }

        public static void MarkFormat(string exchange)
        {
            if (RequiredFormat(exchange).Format >= DataFormat) return;
            var dir = Root(exchange);
            Directory.CreateDirectory(dir);
            var path = System.IO.Path.Combine(dir, FormatFile);
            File.WriteAllText(path, JsonSerializer.Serialize(new FormatInfo { Format = DataFormat, Version = Current.ToString(3) }), new UTF8Encoding(false));
        }

        public static bool Launch(Installer target, bool toTray)
        {
            var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "YellowBlackNotes-update");
            Directory.CreateDirectory(tempDir);
            var local = System.IO.Path.Combine(tempDir, System.IO.Path.GetFileName(target.Path));
            File.Copy(target.Path, local, true);
            try
            {
                var args = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS" + (toTray ? " /LAUNCHARGS=--tray" : "");
                Process.Start(new ProcessStartInfo(local, args) { UseShellExecute = true });
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
