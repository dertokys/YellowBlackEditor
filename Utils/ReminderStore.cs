using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public static class ReminderStore
    {
        public const string FileName = "_reminders.json";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static ObservableCollection<Reminder> Load(string workspace)
        {
            var path = Path.Combine(workspace, FileName);
            try
            {
                if (File.Exists(path))
                    return JsonSerializer.Deserialize<ObservableCollection<Reminder>>(File.ReadAllText(path), Options) ?? new();
            }
            catch
            {
                var bak = path + ".bak";
                if (File.Exists(bak))
                    return JsonSerializer.Deserialize<ObservableCollection<Reminder>>(File.ReadAllText(bak), Options) ?? new();
            }
            return new();
        }

        public static void Save(string workspace, IEnumerable<Reminder> items)
        {
            var path = Path.Combine(workspace, FileName);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(items, Options), new UTF8Encoding(false));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }
    }
}
