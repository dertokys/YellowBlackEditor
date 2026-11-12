using System.Diagnostics;
using System.IO;

namespace YellowBlackEditor.Utils
{
    public static class RemoteLauncher
    {
        private static readonly Dictionary<string, string[]> Candidates = new(StringComparer.OrdinalIgnoreCase)
        {
            ["AnyDesk"] = new[]
            {
                @"%ProgramFiles(x86)%\AnyDesk\AnyDesk.exe",
                @"%ProgramFiles%\AnyDesk\AnyDesk.exe",
                @"%LocalAppData%\anydesk\anydesk.exe"
            },
            ["RuDesktop"] = new[]
            {
                @"%ProgramFiles%\RuDesktop\rudesktop.exe",
                @"%ProgramFiles(x86)%\RuDesktop\rudesktop.exe",
                @"%LocalAppData%\RuDesktop\rudesktop.exe"
            },
            ["Ассистент"] = new[]
            {
                @"%ProgramFiles(x86)%\Ассистент\assistant.exe",
                @"%ProgramFiles%\Ассистент\assistant.exe"
            },
            ["Helper"] = new[]
            {
                @"%ProgramFiles%\Helper\Helper.exe",
                @"%ProgramFiles(x86)%\Helper\Helper.exe",
                @"%LocalAppData%\Helper\Helper.exe"
            }
        };

        public static string? FindExe(string program)
        {
            if (!Candidates.TryGetValue(program, out var list)) return null;
            foreach (var raw in list)
            {
                var p = Environment.ExpandEnvironmentVariables(raw);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static string Launch(string program, string accessId)
        {
            var exe = FindExe(program) ?? throw new FileNotFoundException($"{program} не установлен на этом компьютере.");
            var id = new string(accessId.Where(c => !char.IsWhiteSpace(c)).ToArray());

            var psi = new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
            if (program.Equals("AnyDesk", StringComparison.OrdinalIgnoreCase) && id.Length > 0)
                psi.Arguments = id;

            Process.Start(psi);
            return psi.Arguments.Length > 0
                ? $"{program}: подключение к {id}"
                : $"{program} открыт, ID скопирован — вставьте его (Ctrl+V)";
        }
    }
}
