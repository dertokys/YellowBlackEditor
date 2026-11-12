using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public static class SharedProjects
    {
        public const string FolderName = "Общие проекты";
        private const string TrashName = "_Удалённые";
        private const string PeopleName = "_люди";

        private static string PersonFile(string exchange, string id)
        {
            var safe = string.Concat(id.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            return Path.Combine(Root(exchange), PeopleName, safe + ".json");
        }

        public static List<SharedPerson> LoadPeople(string exchange)
        {
            var dir = Path.Combine(Root(exchange), PeopleName);
            var list = new List<SharedPerson>();
            if (!Directory.Exists(dir)) return list;
            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var p = JsonSerializer.Deserialize<SharedPerson>(File.ReadAllText(file), Options);
                    if (p != null && p.Id.Length > 0) list.Add(p);
                }
                catch { }
            }
            return list;
        }

        public static void TouchPerson(string exchange, string name)
        {
            var path = PersonFile(exchange, AppSettings.UserId);
            try
            {
                if (File.Exists(path))
                {
                    var old = JsonSerializer.Deserialize<SharedPerson>(File.ReadAllText(path), Options);
                    if (old != null && old.Name == name && DateTime.UtcNow - old.LastSeen < TimeSpan.FromHours(12)) return;
                }
            }
            catch { }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var me = new SharedPerson { Id = AppSettings.UserId, Name = name, Machine = AppSettings.MachineName, LastSeen = DateTime.UtcNow };
            var tmp = path + "." + Environment.MachineName + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(me, Options), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        public static ClientProject CloneAsSeparate(ClientProject p, string suffix)
        {
            ProjectTask Fresh(ProjectTask t)
            {
                var c = new ProjectTask { Title = t.Title, Done = t.Done };
                foreach (var ch in t.Children) c.Children.Add(Fresh(ch));
                return c;
            }
            var copy = new ClientProject
            {
                Name = (string.IsNullOrWhiteSpace(p.Name) ? "Проект" : p.Name.Trim()) + suffix,
                Status = p.Status,
                Deadline = p.Deadline,
                Comment = p.Comment
            };
            foreach (var t in p.Tasks) copy.Tasks.Add(Fresh(t));
            return copy;
        }

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string Root(string exchange) => Path.Combine(exchange, FolderName);

        private static string FilePath(string exchange, string id) => Path.Combine(Root(exchange), id + ".json");

        public static List<SharedProject> LoadAll(string exchange)
        {
            if (!Directory.Exists(exchange))
                throw new DirectoryNotFoundException($"Папка обмена недоступна: {exchange}");
            var root = Root(exchange);
            var list = new List<SharedProject>();
            if (!Directory.Exists(root)) return list;
            foreach (var file in Directory.GetFiles(root, "*.json"))
            {
                try
                {
                    var p = JsonSerializer.Deserialize<SharedProject>(File.ReadAllText(file), Options);
                    if (p != null && p.Id.Length > 0) list.Add(p);
                }
                catch { }
            }
            return list;
        }

        public static void Save(string exchange, SharedProject p)
        {
            var root = Root(exchange);
            Directory.CreateDirectory(root);
            var path = FilePath(exchange, p.Id);
            var tmp = path + "." + Environment.MachineName + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(p, Options), new UTF8Encoding(false));
            File.Move(tmp, path, true);
        }

        public static void Remove(string exchange, string id)
        {
            var path = FilePath(exchange, id);
            if (!File.Exists(path)) return;
            var trash = Path.Combine(Root(exchange), TrashName);
            Directory.CreateDirectory(trash);
            File.Move(path, Path.Combine(trash, id + ".json"), true);
        }

        public static string Hash(string name, string status, DateTime? deadline, string comment, IEnumerable<ProjectTask> tasks)
        {
            var sb = new StringBuilder();
            sb.Append(name).Append('\u0001').Append(status).Append('\u0001')
              .Append(deadline?.ToString("yyyy-MM-dd") ?? "").Append('\u0001').Append(comment).Append('\u0001');
            void Walk(IEnumerable<ProjectTask> list, int depth)
            {
                foreach (var t in list)
                {
                    sb.Append(depth).Append(t.Done ? '+' : '-').Append(t.Title).Append('\u0002');
                    Walk(t.Children, depth + 1);
                }
            }
            Walk(tasks, 0);
            return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public static string Hash(ClientProject p) => Hash(p.Name, p.Status, p.Deadline, p.Comment, p.Tasks);
        public static string Hash(SharedProject p) => Hash(p.Name, p.Status, p.Deadline, p.Comment, p.Tasks);

        private static System.Collections.ObjectModel.ObservableCollection<ProjectTask> CopyTasks(IEnumerable<ProjectTask> tasks) => new(tasks.Select(CopyTask));

        private static ProjectTask CopyTask(ProjectTask t)
        {
            var c = new ProjectTask { Id = t.Id, Title = t.Title, Done = t.Done };
            foreach (var ch in t.Children) c.Children.Add(CopyTask(ch));
            return c;
        }

        public static void CopyContent(ClientProject from, SharedProject to)
        {
            to.Name = from.Name;
            to.Status = from.Status;
            to.Deadline = from.Deadline;
            to.Comment = from.Comment;
            to.Tasks = CopyTasks(from.Tasks);
        }

        public static void CopyContent(SharedProject from, ClientProject to)
        {
            to.Name = from.Name;
            to.Status = from.Status;
            to.Deadline = from.Deadline;
            to.Comment = from.Comment;
            to.Tasks.Clear();
            foreach (var t in from.Tasks) to.Tasks.Add(CopyTask(t));
        }

        public static void Touch(SharedProject p)
        {
            p.UpdatedAt = DateTime.UtcNow;
            p.UpdatedById = AppSettings.UserId;
        }

        public static bool EnsureMember(SharedProject p, AppSettings me)
        {
            var id = AppSettings.UserId;
            var name = me.DisplayName;
            var changed = false;
            var m = p.Members.FirstOrDefault(x => x.Id == id);
            if (m == null)
            {
                p.Members.Add(new SharedMember { Id = id, Name = name, Machine = AppSettings.MachineName, JoinedAt = DateTime.UtcNow });
                changed = true;
            }
            else if (m.Name != name)
            {
                m.Name = name;
                changed = true;
            }
            if (p.OwnerId == id && p.OwnerName != name)
            {
                p.OwnerName = name;
                changed = true;
            }
            if (p.UpdatedById == id && p.UpdatedByName != name)
            {
                p.UpdatedByName = name;
                changed = true;
            }
            return changed;
        }

        public static void ApplyInfo(SharedProject sp, ClientProject p)
        {
            p.SharedOwnerId = sp.OwnerId;
            p.SharedOwnerName = sp.OwnerName;
            var others = sp.Members.Where(m => m.Id != sp.OwnerId)
                .OrderBy(m => m.Id == AppSettings.UserId ? 0 : 1)
                .Select(m => m.Id == AppSettings.UserId ? "вы" : m.Name).ToList();
            p.SharedMembers = others.Count == 0 ? null : string.Join(", ", others);
            p.SharedNote = null;
        }
    }
}
