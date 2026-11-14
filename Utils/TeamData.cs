using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace YellowBlackEditor.Utils
{
    public sealed class TeamMonth
    {
        public string UserId { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime UpdatedAt { get; set; }
        public Dictionary<string, int> Clients { get; set; } = new();
    }

    public sealed class PresenceInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Client { get; set; } = "";
        public DateTime Since { get; set; }
        public DateTime Beat { get; set; }

        public bool IsFresh => Client.Length > 0 && Math.Abs((DateTime.UtcNow - Beat).TotalMinutes) < 5;
    }

    public static class TeamData
    {
        private const string TimeFolder = "Время";
        private const string PresenceFolder = "Присутствие";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private static string Safe(string id) => string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        private static void WriteIfChanged(string path, string json)
        {
            if (File.Exists(path) && File.ReadAllText(path) == json) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        public static string MonthKey(DateTime d) => d.ToString("yyyy-MM");

        public static void PublishMonth(string exchange, string name, DateTime month, Dictionary<string, int> clients)
        {
            var path = Path.Combine(exchange, TimeFolder, MonthKey(month), Safe(AppSettings.UserId) + ".json");
            var data = new TeamMonth { UserId = AppSettings.UserId, Name = name, Clients = clients.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value) };
            var old = File.Exists(path) ? Read<TeamMonth>(path) : null;
            if (old != null && old.Name == name && old.Clients.Count == data.Clients.Count && !old.Clients.Except(data.Clients).Any()) return;
            data.UpdatedAt = DateTime.UtcNow;
            WriteIfChanged(path, JsonSerializer.Serialize(data, Options));
        }

        public static List<TeamMonth> LoadMonth(string exchange, DateTime month)
        {
            var dir = Path.Combine(exchange, TimeFolder, MonthKey(month));
            if (!Directory.Exists(dir)) return new();
            return Directory.GetFiles(dir, "*.json").Select(Read<TeamMonth>).OfType<TeamMonth>().Where(t => t.UserId.Length > 0).ToList();
        }

        public static void PublishPresence(string exchange, string name, string client, DateTime since)
        {
            var path = Path.Combine(exchange, PresenceFolder, Safe(AppSettings.UserId) + ".json");
            var data = new PresenceInfo { Id = AppSettings.UserId, Name = name, Client = client, Since = since, Beat = DateTime.UtcNow };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, Options), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        public static List<PresenceInfo> LoadPresence(string exchange)
        {
            var dir = Path.Combine(exchange, PresenceFolder);
            if (!Directory.Exists(dir)) return new();
            return Directory.GetFiles(dir, "*.json").Select(Read<PresenceInfo>).OfType<PresenceInfo>()
                .Where(p => p.Id.Length > 0 && p.Id != AppSettings.UserId).ToList();
        }

        private static T? Read<T>(string path) where T : class
        {
            try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options); }
            catch { return null; }
        }
    }
}
