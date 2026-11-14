using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace YellowBlackEditor.Utils
{
    public sealed class HistoryEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime At { get; set; } = DateTime.UtcNow;
        public string ById { get; set; } = "";
        public string ByName { get; set; } = "";
        public string Path { get; set; } = "";
        public string Old { get; set; } = "";
        public string New { get; set; } = "";
    }

    public static class CardHistory
    {
        public const string FileName = "history.json";
        private const int Limit = 500;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static readonly Dictionary<string, string> Labels = new()
        {
            ["Inn"] = "ИНН", ["Kpp"] = "КПП", ["Ogrn"] = "ОГРН", ["Okpo"] = "ОКПО", ["Address"] = "Адрес", ["Email"] = "Почта",
            ["Comment"] = "Комментарий", ["Contacts"] = "Контакты", ["Accesses"] = "Точки", ["Credentials"] = "Доступы",
            ["People"] = "Телефоны", ["Logins"] = "Логины", ["Name"] = "Имя", ["Phone"] = "Телефон", ["Role"] = "Должность",
            ["Point"] = "Точка", ["Program"] = "Программа", ["AccessId"] = "ID", ["Password"] = "Пароль", ["Title"] = "Название",
            ["Url"] = "Ссылка", ["Login"] = "Логин", ["Spo"] = "Договор СПО", ["Ko"] = "Договор КО", ["Has"] = "Есть",
            ["Hours"] = "Часов", ["Note"] = "Примечание", ["Addons"] = "Подписки", ["Activated"] = "Активировано",
            ["Ends"] = "Окончание", ["TermMonths"] = "Срок, мес."
        };

        private static readonly string[] ItemNameKeys = { "Point", "Name", "Title", "Login", "Phone" };

        public static string PathFor(string folder) => System.IO.Path.Combine(folder, FileName);

        public static List<HistoryEntry> Load(string folder)
        {
            try
            {
                var p = PathFor(folder);
                if (!File.Exists(p)) return new();
                return JsonSerializer.Deserialize<List<HistoryEntry>>(File.ReadAllText(p), Options) ?? new();
            }
            catch
            {
                return new();
            }
        }

        public static void Append(string folder, IEnumerable<HistoryEntry> entries)
        {
            var add = entries.ToList();
            if (add.Count == 0) return;
            var list = Load(folder);
            list.AddRange(add);
            list = list.OrderBy(e => e.At).TakeLast(Limit).ToList();
            var path = PathFor(folder);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, Options), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        public static List<HistoryEntry> Diff(string? oldJson, string? newJson, string prefix)
        {
            var result = new List<HistoryEntry>();
            var a = JsonMerge.Parse(oldJson);
            var b = JsonMerge.Parse(newJson);
            if (a == null && b == null) return result;
            JsonMerge.EnsureIds(a);
            JsonMerge.EnsureIds(b);
            Walk(a, b, prefix, result);
            return result;
        }

        private static string Label(string key) => Labels.TryGetValue(key, out var l) ? l : key;

        private static string ItemName(JsonNode? n)
        {
            if (n is not JsonObject o) return "запись";
            foreach (var k in ItemNameKeys)
                if (o[k] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s)) return s.Trim();
            return "без названия";
        }

        private static string Text(JsonNode? n) => n switch
        {
            null => "",
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            JsonValue v when v.TryGetValue<bool>(out var bo) => bo ? "да" : "нет",
            JsonValue v => v.ToJsonString(),
            JsonObject o => ItemName(o),
            _ => ""
        };

        private static string Join(string a, string b) => a.Length == 0 ? b : a + " › " + b;

        private static void Walk(JsonNode? a, JsonNode? b, string path, List<HistoryEntry> result)
        {
            if (JsonNode.DeepEquals(a, b)) return;
            if (a is JsonObject ao && b is JsonObject bo)
            {
                foreach (var k in ao.Select(kv => kv.Key).Union(bo.Select(kv => kv.Key)))
                {
                    if (k is "Id" or "ProtectedPassword" or "Reminders" or "Projects") continue;
                    Walk(ao[k], bo[k], Join(path, Label(k)), result);
                }
                return;
            }
            if (a is JsonArray aa && b is JsonArray ba)
            {
                var amap = aa.OfType<JsonObject>().Where(x => x["Id"] != null).ToDictionary(x => x["Id"]!.GetValue<string>(), x => (JsonNode)x);
                var bmap = ba.OfType<JsonObject>().Where(x => x["Id"] != null).ToDictionary(x => x["Id"]!.GetValue<string>(), x => (JsonNode)x);
                foreach (var (id, item) in amap)
                    if (!bmap.ContainsKey(id)) result.Add(new HistoryEntry { Path = Join(path, ItemName(item)), Old = "есть", New = "удалено" });
                foreach (var (id, item) in bmap)
                {
                    if (!amap.TryGetValue(id, out var old)) result.Add(new HistoryEntry { Path = Join(path, ItemName(item)), Old = "", New = "добавлено" });
                    else Walk(old, item, Join(path, ItemName(item)), result);
                }
                return;
            }
            var oldText = Text(a);
            var newText = Text(b);
            if (oldText == newText) return;
            result.Add(new HistoryEntry { Path = path, Old = oldText, New = newText });
        }
    }
}
