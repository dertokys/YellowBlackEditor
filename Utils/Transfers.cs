using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public static class Transfers
    {
        public const string FolderName = "Передачи";
        private const string MetaFile = "transfer.json";
        public const string PayloadFolder = "payload";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string Root(string exchange) => Path.Combine(exchange, FolderName);

        private static string Safe(string id) => string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        public static string InboxDir(string exchange, string personId) => Path.Combine(Root(exchange), Safe(personId));

        public static string PayloadDir(Transfer t) => Path.Combine(t.Folder, PayloadFolder);

        public static Transfer Send(string exchange, Transfer template, string toId, string toName, Action<string> fillPayload)
        {
            var t = JsonSerializer.Deserialize<Transfer>(JsonSerializer.Serialize(template, Options), Options)!;
            t.Id = Guid.NewGuid().ToString("N");
            t.ToId = toId;
            t.ToName = toName;
            t.CreatedAt = DateTime.UtcNow;
            t.State = "new";
            t.Folder = Path.Combine(InboxDir(exchange, toId), DateTime.Now.ToString("yyyy-MM-dd HH-mm") + " " + t.Id[..8]);
            Directory.CreateDirectory(Path.Combine(t.Folder, PayloadFolder));
            fillPayload(Path.Combine(t.Folder, PayloadFolder));
            Write(t);
            return t;
        }

        public static void Write(Transfer t)
        {
            var path = Path.Combine(t.Folder, MetaFile);
            var tmp = path + "." + Environment.MachineName + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(t, Options), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        private static Transfer? Read(string folder)
        {
            try
            {
                var path = Path.Combine(folder, MetaFile);
                if (!File.Exists(path)) return null;
                var t = JsonSerializer.Deserialize<Transfer>(File.ReadAllText(path), Options);
                if (t == null) return null;
                t.Folder = folder;
                return t;
            }
            catch
            {
                return null;
            }
        }

        public static List<Transfer> LoadInbox(string exchange, string me)
        {
            var dir = InboxDir(exchange, me);
            if (!Directory.Exists(dir)) return new();
            return Directory.GetDirectories(dir).Select(Read).OfType<Transfer>()
                .Where(t => t.ToId == me)
                .OrderByDescending(t => t.CreatedAt).ToList();
        }

        public static List<Transfer> LoadSent(string exchange, string me)
        {
            var root = Root(exchange);
            if (!Directory.Exists(root)) return new();
            var list = new List<Transfer>();
            foreach (var person in Directory.GetDirectories(root))
                foreach (var folder in Directory.GetDirectories(person))
                    if (Read(folder) is { } t && t.FromId == me) list.Add(t);
            return list.OrderByDescending(t => t.CreatedAt).Take(100).ToList();
        }

        public static void SetState(Transfer t, string state)
        {
            t.State = state;
            t.StateAt = DateTime.UtcNow;
            Write(t);
        }

        public static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from))
                File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(from))
                CopyDirectory(d, Path.Combine(to, Path.GetFileName(d)));
        }
    }
}
