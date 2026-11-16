using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace YellowBlackEditor.Utils
{
    public static class BaseTransfer
    {
        public const int Format = 1;
        public const string ManifestName = "_выгрузка Yellow Black Notes.json";

        public sealed class Manifest
        {
            public string App { get; set; } = "Yellow Black Notes";
            public int Format { get; set; } = BaseTransfer.Format;
            public string Version { get; set; } = "";
            public DateTime Exported { get; set; }
            public string By { get; set; } = "";
            public string Machine { get; set; } = "";
            public int Files { get; set; }
        }

        public sealed class ImportResult
        {
            public int Added { get; set; }
            public int Updated { get; set; }
            public int Merged { get; set; }
            public int KeptLocal { get; set; }
            public int Same { get; set; }
            public int Conflicts { get; set; }
            public string Backup { get; set; } = "";
            public string ConflictDir { get; set; } = "";
        }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static string StateRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YellowBlackNotes", "Перенос базы");

        private static string StateDir(string workspace)
        {
            var key = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(workspace).TrimEnd('\\').ToLowerInvariant())))[..12];
            return Path.Combine(StateRoot, key);
        }

        private static string HashesPath(string workspace) => Path.Combine(StateDir(workspace), "hashes.json");
        private static string BaseFilesDir(string workspace) => Path.Combine(StateDir(workspace), "files");

        private static bool Skip(string rel)
        {
            var name = Path.GetFileName(rel);
            return name.Length == 0 ||
                   name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".synctmp", StringComparison.OrdinalIgnoreCase) ||
                   name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsJson(string rel) => rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        private static string Hash(byte[] data) => Convert.ToHexString(SHA1.HashData(data));

        private static string HashFile(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return Convert.ToHexString(SHA1.HashData(fs));
        }

        private static string Rel(string root, string file) => Path.GetRelativePath(root, file).Replace('\\', '/');

        private static IEnumerable<string> Files(string workspace) =>
            Directory.EnumerateFiles(workspace, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => !Skip(Rel(workspace, f)));

        public static string SafeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        public static (string Path, int Files, int Skipped) Export(string workspace, string folder, string by, string version)
        {
            Directory.CreateDirectory(folder);
            var stem = SafeFileName($"Yellow Black Notes — база {by} {DateTime.Now:yyyy-MM-dd HH-mm}");
            var target = Path.Combine(folder, stem + ".zip");
            for (var i = 2; File.Exists(target); i++) target = Path.Combine(folder, $"{stem} ({i}).zip");
            var tmp = target + ".tmp";
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var jsonCopies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var skipped = 0;
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8))
                {
                    foreach (var file in Files(workspace))
                    {
                        var rel = Rel(workspace, file);
                        try
                        {
                            var hash = HashFile(file);
                            zip.CreateEntryFromFile(file, rel, CompressionLevel.Fastest);
                            hashes[rel] = hash;
                            if (IsJson(rel)) jsonCopies[rel] = File.ReadAllBytes(file);
                        }
                        catch
                        {
                            skipped++;
                        }
                    }
                    var manifest = new Manifest
                    {
                        Version = version,
                        Exported = DateTime.Now,
                        By = by,
                        Machine = Environment.MachineName,
                        Files = hashes.Count
                    };
                    var entry = zip.CreateEntry(ManifestName, CompressionLevel.Fastest);
                    using var es = entry.Open();
                    es.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
                }
                File.Move(tmp, target, true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
            SaveBase(workspace, hashes, jsonCopies);
            return (target, hashes.Count, skipped);
        }

        public static Manifest? ReadManifest(string zipPath)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            var entry = zip.GetEntry(ManifestName);
            if (entry == null) return null;
            using var s = entry.Open();
            return JsonSerializer.Deserialize<Manifest>(s);
        }

        public static int CountFiles(string zipPath)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            return zip.Entries.Count(e => !Skip(e.FullName.Replace('\\', '/')));
        }

        public static string Backup(string workspace, string backupDir)
        {
            Directory.CreateDirectory(backupDir);
            var target = Path.Combine(backupDir, $"{DateTime.Now:yyyy-MM-dd HH-mm} перед загрузкой базы.zip");
            var tmp = target + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8))
                    foreach (var file in Files(workspace))
                        zip.CreateEntryFromFile(file, Rel(workspace, file), CompressionLevel.Fastest);
                File.Move(tmp, target, true);
            }
            catch
            {
                try { File.Delete(tmp); } catch { }
                throw;
            }
            return target;
        }

        public static ImportResult Import(string zipPath, string workspace, string backupDir)
        {
            var result = new ImportResult { Backup = Backup(workspace, backupDir) };
            var baseHashes = LoadHashes(workspace);
            var baseFiles = BaseFilesDir(workspace);
            var root = Path.GetFullPath(workspace).TrimEnd('\\') + "\\";
            var newHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var newJson = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var conflictDir = Path.Combine(backupDir, $"{DateTime.Now:yyyy-MM-dd HH-mm} конфликты загрузки базы");

            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                var rel = entry.FullName.Replace('\\', '/');
                if (rel.EndsWith('/') || Skip(rel)) continue;
                var local = Path.GetFullPath(Path.Combine(workspace, rel.Replace('/', '\\')));
                if (!local.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;

                byte[] data;
                using (var s = entry.Open())
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    data = ms.ToArray();
                }
                var remoteHash = Hash(data);
                var remoteTime = entry.LastWriteTime.UtcDateTime;
                newHashes[rel] = remoteHash;
                if (IsJson(rel)) newJson[rel] = data;

                if (!File.Exists(local))
                {
                    Write(local, data, remoteTime);
                    result.Added++;
                    continue;
                }
                var localHash = HashFile(local);
                if (localHash == remoteHash)
                {
                    result.Same++;
                    continue;
                }
                baseHashes.TryGetValue(rel, out var baseHash);
                if (baseHash == localHash)
                {
                    Write(local, data, remoteTime);
                    result.Updated++;
                    continue;
                }
                if (baseHash == remoteHash)
                {
                    result.KeptLocal++;
                    continue;
                }

                var localTime = File.GetLastWriteTimeUtc(local);
                var remoteNewer = remoteTime > localTime;
                if (IsJson(rel))
                {
                    var merged = TryMerge(Path.Combine(baseFiles, rel.Replace('/', '\\')), local, data, !remoteNewer, rel);
                    if (merged != null)
                    {
                        Keep(conflictDir, rel, File.ReadAllBytes(local));
                        Write(local, merged, null);
                        result.Merged++;
                        continue;
                    }
                }
                if (remoteNewer)
                {
                    Keep(conflictDir, rel, File.ReadAllBytes(local));
                    Write(local, data, remoteTime);
                }
                else
                {
                    Keep(conflictDir, rel, data);
                }
                result.Conflicts++;
            }
            if (Directory.Exists(conflictDir)) result.ConflictDir = conflictDir;
            SaveBase(workspace, newHashes, newJson);
            return result;
        }

        private static byte[]? TryMerge(string basePath, string localPath, byte[] remote, bool preferLocal, string rel)
        {
            try
            {
                var isHistory = Path.GetFileName(rel).Equals(CardHistory.FileName, StringComparison.OrdinalIgnoreCase);
                if (!File.Exists(basePath) && !isHistory) return null;
                var b = File.Exists(basePath) ? JsonMerge.Parse(File.ReadAllText(basePath)) : new System.Text.Json.Nodes.JsonArray();
                var l = JsonMerge.Parse(File.ReadAllText(localPath));
                var r = JsonMerge.Parse(Encoding.UTF8.GetString(remote).TrimStart('﻿'));
                if (b == null || l == null || r == null) return null;
                JsonMerge.EnsureIds(b);
                JsonMerge.EnsureIds(l);
                JsonMerge.EnsureIds(r);
                var res = JsonMerge.Merge(b, l, r, preferLocal);
                return res.Merged == null ? null : new UTF8Encoding(false).GetBytes(res.Merged.ToJsonString(JsonOptions));
            }
            catch
            {
                return null;
            }
        }

        private static void Write(string path, byte[] data, DateTime? utcTime)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, path, true);
            if (utcTime is { } t && t.Year > 1980) File.SetLastWriteTimeUtc(path, t);
        }

        private static void Keep(string dir, string rel, byte[] data)
        {
            var path = Path.Combine(dir, rel.Replace('/', '\\'));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
        }

        private static Dictionary<string, string> LoadHashes(string workspace)
        {
            try
            {
                var path = HashesPath(workspace);
                if (File.Exists(path))
                    return new Dictionary<string, string>(
                        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new(), StringComparer.OrdinalIgnoreCase);
            }
            catch { }
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        private static void SaveBase(string workspace, Dictionary<string, string> hashes, Dictionary<string, byte[]> json)
        {
            try
            {
                var dir = StateDir(workspace);
                var files = BaseFilesDir(workspace);
                if (Directory.Exists(files)) Directory.Delete(files, true);
                Directory.CreateDirectory(files);
                foreach (var (rel, data) in json)
                {
                    var path = Path.Combine(files, rel.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, data);
                }
                File.WriteAllText(HashesPath(workspace), JsonSerializer.Serialize(hashes, JsonOptions));
            }
            catch { }
        }
    }
}
