using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Text.Json;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public static class HotkeyManager
    {
        private static readonly KeyGestureConverter Conv = new();

        public static HotkeySettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var s = JsonSerializer.Deserialize<HotkeySettings>(json);
                    if (s != null) return s;
                }
            }
            catch { }
            return new HotkeySettings();
        }

        public static void Save(string path, HotkeySettings hk)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = JsonSerializer.Serialize(hk, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public static void ApplyToWindow(Window w, HotkeySettings hk,
            RoutedUICommand cmdCreateSection,
            RoutedUICommand cmdCreateDocument,
            RoutedUICommand cmdNavPrev,
            RoutedUICommand cmdNavNext,
            RoutedUICommand cmdNavParent,
            RoutedUICommand cmdNavChild)
        {
            var toRemove = w.InputBindings.OfType<KeyBinding>()
                .Where(kb => kb.Command == cmdCreateSection || kb.Command == cmdCreateDocument ||
                             kb.Command == cmdNavPrev || kb.Command == cmdNavNext ||
                             kb.Command == cmdNavParent || kb.Command == cmdNavChild)
                .ToList();
            foreach (var kb in toRemove) w.InputBindings.Remove(kb);

            void Add(string text, RoutedUICommand cmd)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(text)) return;
                    var gesture = (KeyGesture)Conv.ConvertFromString(text)!;
                    w.InputBindings.Add(new KeyBinding(cmd, gesture));
                }
                catch
                {
                }
            }

            Add(hk.CreateSection, cmdCreateSection);
            Add(hk.CreateDocument, cmdCreateDocument);
            Add(hk.NavPrev, cmdNavPrev);
            Add(hk.NavNext, cmdNavNext);
            Add(hk.NavParent, cmdNavParent);
            Add(hk.NavChild, cmdNavChild);
        }
    }
}