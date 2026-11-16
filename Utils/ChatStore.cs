using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public static class ChatStore
    {
        public const string FolderName = "Сообщения";
        public const string General = "Общий чат";
        private const string DirectPrefix = "Личные ";
        private const string Joiner = " и ";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static string ReadPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YellowBlackNotes", "chat-read.json");

        public static string Root(string exchange) => Path.Combine(exchange, FolderName);

        public static string Safe(string id) => string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

        public static string DirectKey(string a, string b)
        {
            var ids = new[] { Safe(a), Safe(b) }.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            return DirectPrefix + ids[0] + Joiner + ids[1];
        }

        public static bool IsDirect(string key) => key.StartsWith(DirectPrefix, StringComparison.Ordinal);

        public static bool IsMine(string key, string me)
        {
            if (key == General) return true;
            if (!IsDirect(key)) return false;
            var parts = key[DirectPrefix.Length..].Split(Joiner);
            var mine = Safe(me);
            return parts.Length == 2 && parts.Any(p => p.Equals(mine, StringComparison.OrdinalIgnoreCase));
        }

        public static string OtherOf(string key, string me)
        {
            if (!IsDirect(key)) return "";
            var parts = key[DirectPrefix.Length..].Split(Joiner);
            var mine = Safe(me);
            return parts.FirstOrDefault(p => !p.Equals(mine, StringComparison.OrdinalIgnoreCase)) ?? "";
        }

        public static string Send(string exchange, ChatMessage message)
        {
            var now = DateTime.UtcNow;
            message.CreatedAt = now;
            var dir = Path.Combine(Root(exchange), message.Chat, now.ToLocalTime().ToString("yyyy-MM"));
            Directory.CreateDirectory(dir);
            var name = now.ToString("yyyyMMdd-HHmmss-fff") + "_" + message.Id[..8] + ".json";
            var path = Path.Combine(dir, name);
            var tmp = path + "." + Environment.MachineName + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(message, Options), new UTF8Encoding(false));
            File.Move(tmp, path, true);
            return path;
        }

        public static List<(string File, ChatMessage Message)> Scan(string exchange, string me, ISet<string> known)
        {
            var found = new List<(string, ChatMessage)>();
            var root = Root(exchange);
            if (!Directory.Exists(root)) return found;
            var months = new[] { DateTime.Today.ToString("yyyy-MM"), DateTime.Today.AddMonths(-1).ToString("yyyy-MM") };
            foreach (var chatDir in Directory.GetDirectories(root))
            {
                var key = Path.GetFileName(chatDir);
                if (!IsMine(key, me)) continue;
                foreach (var month in months)
                {
                    var dir = Path.Combine(chatDir, month);
                    if (!Directory.Exists(dir)) continue;
                    foreach (var file in Directory.GetFiles(dir, "*.json"))
                    {
                        if (known.Contains(file)) continue;
                        try
                        {
                            var m = JsonSerializer.Deserialize<ChatMessage>(File.ReadAllText(file), Options);
                            if (m == null || m.Id.Length == 0) continue;
                            m.Chat = key;
                            m.Links ??= new();
                            found.Add((file, m));
                        }
                        catch { }
                    }
                }
            }
            return found;
        }

        public static Dictionary<string, DateTime> LoadRead()
        {
            try
            {
                if (File.Exists(ReadPath))
                    return JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(ReadPath), Options) ?? new();
            }
            catch { }
            return new();
        }

        public static void SaveRead(Dictionary<string, DateTime> read)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReadPath)!);
                var tmp = ReadPath + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(read, Options), new UTF8Encoding(false));
                File.Move(tmp, ReadPath, true);
            }
            catch { }
        }

        private static readonly Regex UrlRx = new(@"https?://[^\s<>""«»]+", RegexOptions.IgnoreCase);

        public static (string Text, List<ChatLink> Links) ExtractLinks(string text, string helpdeskBase)
        {
            var links = new List<ChatLink>();
            Uri.TryCreate(helpdeskBase, UriKind.Absolute, out var baseUri);
            var rest = UrlRx.Replace(text, m =>
            {
                var url = m.Value.TrimEnd('.', ',', ')', ';', '!', '?');
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return m.Value;
                var own = baseUri != null && u.Host.Equals(baseUri.Host, StringComparison.OrdinalIgnoreCase);
                if (!own) return m.Value;
                ChatLink link;
                {
                    var hash = Uri.UnescapeDataString(u.Fragment.TrimStart('#'));
                    var ticket = Regex.Match(hash, @"^ticket/zoom/(\d+)");
                    var kb = Regex.Match(hash, @"^knowledge_base/\d+/locale/([^/]+)/(answer|category)/(\d+)");
                    if (ticket.Success) link = new ChatLink { Kind = "ticket", Url = url, Title = "Заявка в Zammad" };
                    else if (kb.Success)
                        link = new ChatLink
                        {
                            Kind = "kb",
                            Url = baseUri!.GetLeftPart(UriPartial.Authority) + "/api/v1/knowledge_bases/preview/" +
                                  (kb.Groups[2].Value == "answer" ? "KnowledgeBaseAnswer" : "KnowledgeBaseCategory") + "/" + kb.Groups[3].Value + "/" + kb.Groups[1].Value,
                            Title = "Статья базы знаний"
                        };
                    else if (u.AbsolutePath.StartsWith("/help", StringComparison.OrdinalIgnoreCase) || u.AbsolutePath.Contains("knowledge_bases/preview"))
                        link = new ChatLink { Kind = "kb", Url = url, Title = "Статья базы знаний" };
                    else link = new ChatLink { Kind = "ticket", Url = url, Title = "Страница Zammad" };
                }
                links.Add(link);
                return "";
            });
            rest = Regex.Replace(rest, @"[ \t]{2,}", " ").Trim();
            return (rest, links);
        }
    }
}
