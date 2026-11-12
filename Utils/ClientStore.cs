using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public static class ClientStore
    {
        public const string FileName = "client.json";

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string CardPath(string folder) => Path.Combine(folder, FileName);

        public static bool IsClientFolder(string folder) => File.Exists(CardPath(folder));

        public static ClientCard Load(string folder)
        {
            var path = CardPath(folder);
            ClientCard card;
            try
            {
                card = File.Exists(path)
                    ? JsonSerializer.Deserialize<ClientCard>(File.ReadAllText(path), Options) ?? new ClientCard()
                    : new ClientCard();
            }
            catch
            {
                var bak = path + ".bak";
                card = File.Exists(bak)
                    ? JsonSerializer.Deserialize<ClientCard>(File.ReadAllText(bak), Options) ?? new ClientCard()
                    : throw new InvalidDataException("Карточка клиента повреждена: " + path);
            }

            foreach (var a in card.Accesses)
            {
                var plain = Unprotect(a.ProtectedPassword);
                a.PasswordUnreadable = plain == null;
                a.Password = plain ?? "";
            }

            card.Track();
            return card;
        }

        public static void Save(string folder, ClientCard card)
        {
            foreach (var a in card.Accesses)
            {
                if (a.PasswordUnreadable && a.Password.Length == 0) continue;
                a.ProtectedPassword = Protect(a.Password);
                a.PasswordUnreadable = false;
            }

            var path = CardPath(folder);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(card, Options), new UTF8Encoding(false));
            if (File.Exists(path)) File.Copy(path, path + ".bak", true);
            File.Move(tmp, path, true);
        }

        public static string SearchText(string folder)
        {
            try
            {
                var path = CardPath(folder);
                if (!File.Exists(path)) return "";
                var card = JsonSerializer.Deserialize<ClientCard>(File.ReadAllText(path), Options);
                return card?.SearchText() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return "";
            var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(bytes);
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
