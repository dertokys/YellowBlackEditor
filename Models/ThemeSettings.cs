namespace YellowBlackEditor.Models
{
    public class ThemeSettings
    {
        public string Mode { get; set; } = "dark";
        public string Bg { get; set; } = "#0E0E0E";
        public string Panel { get; set; } = "#151515";
        public string Panel2 { get; set; } = "#111111";
        public string Border { get; set; } = "#232323";
        public string Text { get; set; } = "#F5F5F5";
        public string SubText { get; set; } = "#BDBDBD";
        public string Accent { get; set; } = "#FFC107";
        public string AccentLight { get; set; } = "#FFD54F";
        public double EditorFontSize { get; set; } = 16;
        public double TreeFontSize { get; set; } = 16;

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsLight => Mode == "light";

        public void ApplyPalette(bool light)
        {
            Mode = light ? "light" : "dark";
            if (light)
            {
                Bg = "#EFEEEA";
                Panel = "#FFFFFF";
                Panel2 = "#F6F5F2";
                Border = "#DEDCD6";
                Text = "#1B1B1B";
                SubText = "#5C5C5C";
            }
            else
            {
                Bg = "#0E0E0E";
                Panel = "#151515";
                Panel2 = "#111111";
                Border = "#232323";
                Text = "#F5F5F5";
                SubText = "#BDBDBD";
            }
        }
    }
}
