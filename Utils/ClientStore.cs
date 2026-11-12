using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public static class ClientStore
    {
        public const string FileName = "client.json";
        public const string ProjectsFileName = "projects.json";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string CardPath(string folder) => Path.Combine(folder, FileName);
        public static string ProjectsPath(string folder) => Path.Combine(folder, ProjectsFileName);

        public static bool IsClientFolder(string folder) => File.Exists(CardPath(folder));

        public static bool NeedsMigration(string folder)
        {
            var raw = File.ReadAllText(CardPath(folder));
            return raw.Contains("\"Projects\"") || raw.Contains("\"Reminders\"") || raw.Contains("\"ProtectedPassword\"") || raw.Contains("\"Phones\"");
        }

        private static ClientCard Read(string folder)
        {
            var path = CardPath(folder);
            string raw;
            ClientCard card;
            try
            {
                raw = File.Exists(path) ? File.ReadAllText(path) : "{}";
                card = JsonSerializer.Deserialize<ClientCard>(raw, Options) ?? new ClientCard();
            }
            catch
            {
                var bak = path + ".bak";
                if (!File.Exists(bak)) throw new InvalidDataException("Карточка клиента повреждена: " + path);
                raw = File.ReadAllText(bak);
                card = JsonSerializer.Deserialize<ClientCard>(raw, Options) ?? new ClientCard();
            }

            var projectsPath = ProjectsPath(folder);
            if (File.Exists(projectsPath))
            {
                try
                {
                    card.Projects = JsonSerializer.Deserialize<ObservableCollection<ClientProject>>(File.ReadAllText(projectsPath), Options) ?? new();
                }
                catch
                {
                    var bak = projectsPath + ".bak";
                    if (File.Exists(bak))
                        card.Projects = JsonSerializer.Deserialize<ObservableCollection<ClientProject>>(File.ReadAllText(bak), Options) ?? new();
                }
            }
            else
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("Projects", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
                    card.Projects = JsonSerializer.Deserialize<ObservableCollection<ClientProject>>(legacy.GetRawText(), Options) ?? new();
            }
            return card;
        }

        public static List<ClientProject> ReadProjects(string folder)
        {
            try
            {
                var projectsPath = ProjectsPath(folder);
                if (File.Exists(projectsPath))
                    return JsonSerializer.Deserialize<List<ClientProject>>(File.ReadAllText(projectsPath), Options) ?? new();
                var path = CardPath(folder);
                if (!File.Exists(path)) return new();
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("Projects", out var legacy) && legacy.ValueKind == JsonValueKind.Array)
                    return JsonSerializer.Deserialize<List<ClientProject>>(legacy.GetRawText(), Options) ?? new();
            }
            catch { }
            return new();
        }

        public static ClientCard Load(string folder)
        {
            var card = Read(folder);

            foreach (var a in card.Secrets())
            {
                if (string.IsNullOrEmpty(a.ProtectedPassword)) continue;
                var plain = Unprotect(a.ProtectedPassword);
                if (plain == null)
                {
                    a.PasswordUnreadable = true;
                    continue;
                }
                if (a.Password.Length == 0) a.Password = plain;
                a.ProtectedPassword = null;
            }

            foreach (var a in card.Accesses)
            {
                if (a.Phones == null) continue;
                if (a.People.Count == 0) foreach (var c in SplitPhones(a.Phones)) a.People.Add(c);
                a.Phones = null;
            }

            card.Track();
            return card;
        }

        private static IEnumerable<ClientContact> SplitPhones(string text)
        {
            foreach (var part in Regex.Split(text, @"[,;\r\n]"))
            {
                var t = part.Trim();
                if (t.Length == 0) continue;
                var m = Regex.Match(t, @"\+?\d[\d\s\-()]{4,}\d");
                if (!m.Success)
                {
                    yield return new ClientContact { Phone = t };
                    continue;
                }
                var name = (t[..m.Index] + " " + t[(m.Index + m.Length)..]).Trim(' ', '-', '–', '—', ':');
                yield return new ClientContact { Phone = m.Value.Trim(), Name = name };
            }
        }

        public static void Save(string folder, ClientCard card)
        {
            var cardPath = CardPath(folder);
            var cardJson = JsonSerializer.Serialize(card, Options);
            if (!File.Exists(cardPath) || File.ReadAllText(cardPath) != cardJson)
                WriteAtomic(cardPath, cardJson);

            var projectsPath = ProjectsPath(folder);
            if (card.Projects.Count == 0 && !File.Exists(projectsPath)) return;
            var projectsJson = JsonSerializer.Serialize(card.Projects, Options);
            if (!File.Exists(projectsPath) || File.ReadAllText(projectsPath) != projectsJson)
                WriteAtomic(projectsPath, projectsJson);
        }

        public static void SaveProjects(string folder, IEnumerable<ClientProject> projects)
        {
            var projectsPath = ProjectsPath(folder);
            var projectsJson = JsonSerializer.Serialize(projects.ToList(), Options);
            if (!File.Exists(projectsPath) || File.ReadAllText(projectsPath) != projectsJson)
                WriteAtomic(projectsPath, projectsJson);
        }

        private static void WriteAtomic(string path, string text)
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }

        public static string SearchText(string folder)
        {
            try
            {
                return File.Exists(CardPath(folder)) ? Read(folder).SearchText() : "";
            }
            catch
            {
                return "";
            }
        }

        private static string? Unprotect(string encoded)
        {
            if (string.IsNullOrEmpty(encoded)) return "";
            try
            {
                var bytes = ProtectedData.Unprotect(Convert.FromBase64String(encoded), null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }
    }
}
