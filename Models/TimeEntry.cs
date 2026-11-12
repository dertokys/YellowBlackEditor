using System.Globalization;
using System.Text.RegularExpressions;

namespace YellowBlackEditor.Models
{
    public class TimeEntry
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Date { get; set; } = DateTime.Today;
        public int Minutes { get; set; }
        public string Comment { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? ProjectId { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? ProjectName { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? TaskId { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? TaskTitle { get; set; }
    }

    public static class TimeText
    {
        public static string Human(int minutes)
        {
            if (minutes <= 0) return "0 мин";
            var h = minutes / 60;
            var m = minutes % 60;
            if (h == 0) return $"{m} мин";
            return m == 0 ? $"{h} ч" : $"{h} ч {m} мин";
        }

        public static bool TryParse(string text, out int minutes)
        {
            minutes = 0;
            var t = (text ?? "").Trim().ToLowerInvariant().Replace(" ", "");
            if (t.Length == 0) return false;

            var hm = Regex.Match(t, @"^(\d{1,2})[:.](\d{2})$");
            if (hm.Success && !t.Contains(','))
            {
                var h = int.Parse(hm.Groups[1].Value);
                var m = int.Parse(hm.Groups[2].Value);
                if (m >= 60) return false;
                minutes = h * 60 + m;
                return minutes > 0;
            }

            var parts = Regex.Match(t, @"^(?:(\d+(?:[.,]\d+)?)(?:ч|час|часа|часов|h))?(?:(\d+)(?:м|мин|минут|минуты|m)?)?$");
            if (parts.Success && (parts.Groups[1].Success || parts.Groups[2].Success))
            {
                double total = 0;
                if (parts.Groups[1].Success)
                    total += double.Parse(parts.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * 60;
                if (parts.Groups[2].Success)
                {
                    var n = int.Parse(parts.Groups[2].Value);
                    total += n;
                }
                minutes = (int)Math.Round(total);
                return minutes is > 0 and <= 24 * 60;
            }

            if (Regex.IsMatch(t, @"^\d+[.,]\d+$") &&
                double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
            {
                minutes = (int)Math.Round(hours * 60);
                return minutes is > 0 and <= 24 * 60;
            }
            return false;
        }
    }
}
