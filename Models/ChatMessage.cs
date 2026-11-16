namespace YellowBlackEditor.Models
{
    public sealed class ChatLink
    {
        public string Kind { get; set; } = "url";
        public string Url { get; set; } = "";
        public string Title { get; set; } = "";
    }

    public sealed class ChatMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Chat { get; set; } = "";
        public string FromId { get; set; } = "";
        public string FromName { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public List<ChatLink> Links { get; set; } = new();
    }
}
