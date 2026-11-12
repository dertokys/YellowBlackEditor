using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace YellowBlackEditor.Utils
{
    public class AppSettings
    {
        public bool MinimizeToTray { get; set; } = true;
        public bool TrayHintShown { get; set; }
        public bool SyncEnabled { get; set; }
        public string SyncFolder { get; set; } = "";
        public string UserName { get; set; } = "";
        public List<string> HiddenShared { get; set; } = new();
        public List<string> SeenInvites { get; set; } = new();
        public List<string> SeenTransfers { get; set; } = new();

        public static string UserId => Environment.MachineName + "\\" + Environment.UserName;
        public static string MachineName => Environment.MachineName;
        public static string DefaultUserName => Environment.UserName;
        [System.Text.Json.Serialization.JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(UserName) ? DefaultUserName : UserName.Trim();

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "YellowBlackNotes";

        private static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YellowBlackNotes", "app.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        public static bool StartsWithWindows
        {
            get
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(RunValue) is string;
            }
            set
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                if (value) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --tray");
                else key.DeleteValue(RunValue, false);
            }
        }
    }
}
