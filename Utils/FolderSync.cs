using System.IO;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;

namespace YellowBlackEditor.Utils
{
    public sealed class SyncResult
    {
        public List<string> LocalChanged { get; } = new();
        public bool StructureChanged { get; set; }
        public int Uploaded { get; set; }
        public int Downloaded { get; set; }
        public int Conflicts { get; set; }
    }

    public sealed class FolderSync
    {
        public const string TrashFolderName = "_Удалённые";
        private const string RemoteSubfolder = "Клиенты";
        private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);

        private sealed class Entry
        {
            public long L { get; set; }
            public long R { get; set; }
        }

        private readonly string _localRoot;
        private readonly string _remoteRoot;
        private readonly string _exchangeRoot;
        private readonly string _statePath;
        private readonly string _conflictsRoot;

        public FolderSync(string localRoot, string exchangeFolder, string configDir)
        {
            _localRoot = Path.GetFullPath(localRoot).TrimEnd('\\');
            _exchangeRoot = Path.GetFullPath(exchangeFolder).TrimEnd('\\');
            _remoteRoot = Path.Combine(_exchangeRoot, RemoteSubfolder);
            var key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(
                System.Text.Encoding.UTF8.GetBytes((_localRoot + "|" + _exchangeRoot).ToLowerInvariant())))[..12];
            _statePath = Path.Combine(configDir, $"sync-{key}.json");
            _conflictsRoot = Path.Combine(configDir, "conflicts");
        }

        public string RemoteRoot => _remoteRoot;

        public SyncResult Run()
        {
            if (!Directory.Exists(_exchangeRoot))
                throw new DirectoryNotFoundException($"Папка обмена недоступна: {_exchangeRoot}");
            Directory.CreateDirectory(_localRoot);
            Directory.CreateDirectory(_remoteRoot);

            var state = LoadState();
            var local = Scan(_localRoot);
            var remote = Scan(_remoteRoot);
            var result = new SyncResult();
            var newState = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

            foreach (var rel in local.Keys.Union(remote.Keys, StringComparer.OrdinalIgnoreCase))
            {
                local.TryGetValue(rel, out var l);
                remote.TryGetValue(rel, out var r);
                state.TryGetValue(rel, out var s);
                var lPath = Safe(_localRoot, rel);
                var rPath = Safe(_remoteRoot, rel);

                if (l != null && r != null)
                {
                    var lChanged = s == null || !Same(l.LastWriteTimeUtc.Ticks, s.L);
                    var rChanged = s == null || !Same(r.LastWriteTimeUtc.Ticks, s.R);
                    if (lChanged && rChanged && !SameContent(l, r))
                    {
                        result.Conflicts++;
                        if (l.LastWriteTimeUtc >= r.LastWriteTimeUtc)
                        {
                            BackupConflict(rPath, rel, "из папки обмена");
                            Copy(lPath, rPath, _remoteRoot);
                            result.Uploaded++;
                        }
                        else
                        {
                            BackupConflict(lPath, rel, "локальная");
                            Copy(rPath, lPath, _localRoot);
                            result.Downloaded++;
                            result.LocalChanged.Add(rel);
                        }
                    }
                    else if (lChanged && !rChanged)
                    {
                        Copy(lPath, rPath, _remoteRoot);
                        result.Uploaded++;
                    }
                    else if (rChanged && !lChanged)
                    {
                        Copy(rPath, lPath, _localRoot);
                        result.Downloaded++;
                        result.LocalChanged.Add(rel);
                    }
                    newState[rel] = new Entry { L = new FileInfo(lPath).LastWriteTimeUtc.Ticks, R = new FileInfo(rPath).LastWriteTimeUtc.Ticks };
                }
                else if (l != null)
                {
                    if (s != null && Same(l.LastWriteTimeUtc.Ticks, s.L))
                    {
                        FileSystem.DeleteFile(lPath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        var clientDir = Path.GetDirectoryName(lPath)!;
                        if (Directory.Exists(clientDir) && !Directory.EnumerateDirectories(clientDir).Any())
                            FileSystem.DeleteDirectory(clientDir, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                        result.LocalChanged.Add(rel);
                        result.StructureChanged = true;
                        continue;
                    }
                    Copy(lPath, rPath, _remoteRoot);
                    result.Uploaded++;
                    newState[rel] = new Entry { L = l.LastWriteTimeUtc.Ticks, R = new FileInfo(rPath).LastWriteTimeUtc.Ticks };
                }
                else if (r != null)
                {
                    if (s != null && Same(r.LastWriteTimeUtc.Ticks, s.R))
                    {
                        MoveToRemoteTrash(rPath, rel);
                        RemoveEmptyParents(Path.GetDirectoryName(rPath)!, _remoteRoot);
                        continue;
                    }
                    Copy(rPath, lPath, _localRoot);
                    result.Downloaded++;
                    result.LocalChanged.Add(rel);
                    result.StructureChanged = true;
                    newState[rel] = new Entry { L = new FileInfo(lPath).LastWriteTimeUtc.Ticks, R = r.LastWriteTimeUtc.Ticks };
                }
            }

            SaveState(newState);
            return result;
        }

        private static bool Same(long a, long b) => Math.Abs(a - b) <= Tolerance.Ticks;

        private static bool Shared(string rel)
        {
            var parts = rel.Split(Path.DirectorySeparatorChar);
            return parts.Length == 2 &&
                   parts[1].Equals("client.json", StringComparison.OrdinalIgnoreCase) &&
                   !parts[0].Equals(TrashFolderName, StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, FileInfo> Scan(string root)
        {
            var map = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(root)) return map;
            foreach (var f in new DirectoryInfo(root).EnumerateFiles("*", System.IO.SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, f.FullName);
                if (!Shared(rel)) continue;
                map[rel] = f;
            }
            return map;
        }

        private static string Safe(string root, string rel)
        {
            var full = Path.GetFullPath(Path.Combine(root, rel));
            if (!full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Путь выходит за пределы папки: {rel}");
            return full;
        }

        private static bool SameContent(FileInfo a, FileInfo b)
        {
            if (a.Length != b.Length) return false;
            using var fa = a.OpenRead();
            using var fb = b.OpenRead();
            var ba = new byte[81920];
            var bb = new byte[81920];
            while (true)
            {
                var na = fa.Read(ba, 0, ba.Length);
                var nb = fb.Read(bb, 0, bb.Length);
                if (na != nb) return false;
                if (na == 0) return true;
                if (!ba.AsSpan(0, na).SequenceEqual(bb.AsSpan(0, nb))) return false;
            }
        }

        private static void Copy(string from, string to, string targetRoot)
        {
            Safe(targetRoot, Path.GetRelativePath(targetRoot, to));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            var tmp = to + ".synctmp";
            File.Copy(from, tmp, true);
            File.SetLastWriteTimeUtc(tmp, File.GetLastWriteTimeUtc(from));
            File.Move(tmp, to, true);
        }

        private void MoveToRemoteTrash(string rPath, string rel)
        {
            var target = Safe(_exchangeRoot, Path.Combine(TrashFolderName, DateTime.Now.ToString("yyyy-MM-dd"), rel));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(rPath, target, true);
        }

        private void BackupConflict(string path, string rel, string label)
        {
            if (!File.Exists(path)) return;
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss");
            var dir = Path.Combine(_conflictsRoot, stamp + " " + label, Path.GetDirectoryName(rel) ?? "");
            Directory.CreateDirectory(dir);
            File.Copy(path, Path.Combine(dir, Path.GetFileName(rel)), true);
        }

        private static void RemoveEmptyParents(string dir, string root)
        {
            while (dir.Length > root.Length && dir.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) return;
                Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir)!;
            }
        }

        private Dictionary<string, Entry> LoadState()
        {
            try
            {
                if (File.Exists(_statePath))
                    return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_statePath))?
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return new(StringComparer.OrdinalIgnoreCase);
        }

        private void SaveState(Dictionary<string, Entry> state)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
            var tmp = _statePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state));
            File.Move(tmp, _statePath, true);
        }
    }
}
