namespace YellowBlackEditor.Models
{
    public class HotkeySettings
    {
        public string CreateSection { get; set; } = "Ctrl+Alt+S";
        public string CreateDocument { get; set; } = "Ctrl+Alt+D";
        public string NavPrev { get; set; } = "Alt+Up";
        public string NavNext { get; set; } = "Alt+Down";
        public string NavParent { get; set; } = "Alt+Left";
        public string NavChild { get; set; } = "Alt+Right";
    }
}