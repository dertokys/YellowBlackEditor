using System.IO;
using System.Text.Json;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public static class ThemeManager
    {
        public static ThemeSettings Current { get; private set; } = new();

        public static void LoadAndApply(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var s = System.Text.Json.JsonSerializer.Deserialize<ThemeSettings>(json);
                    if (s != null) Current = s;
                }
            }
            catch {  }

            Apply(Current);
        }

        public static void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = System.Text.Json.JsonSerializer.Serialize(Current, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public static void Apply(ThemeSettings s)
        {
            Current = s;

            SetBrush("BgBrush", s.Bg);
            SetBrush("PanelBrush", s.Panel);
            SetBrush("PanelBrush2", s.Panel2);
            SetBrush("BorderBrush1", s.Border);
            SetBrush("TextBrush", s.Text);
            SetBrush("SubTextBrush", s.SubText);
            SetBrush("AccentBrush", s.Accent);

            var accentLight = string.IsNullOrWhiteSpace(s.AccentLight) ? Lighten(s.Accent, 0.25) : s.AccentLight;
            SetBrush("AccentBrushLight", accentLight);
        }

        private static void SetBrush(string key, string hex)
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;

            if (System.Windows.Application.Current.Resources[key] is System.Windows.Media.SolidColorBrush existing)
            {
                if (existing.IsFrozen)
                {

                    var clone = existing.CloneCurrentValue();
                    clone.Color = color;
                    System.Windows.Application.Current.Resources[key] = clone;
                }
                else
                {

                    existing.Color = color;
                }
            }
            else
            {

                var brush = new System.Windows.Media.SolidColorBrush(color);
                System.Windows.Application.Current.Resources[key] = brush;
            }
        }

        private static string Lighten(string hex, double ratio)
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
            byte L(byte x) => (byte)(x + (255 - x) * ratio);
            var r = System.Windows.Media.Color.FromArgb(c.A, L(c.R), L(c.G), L(c.B));
            return $"#{r.A:X2}{r.R:X2}{r.G:X2}{r.B:X2}";
        }
    }
}