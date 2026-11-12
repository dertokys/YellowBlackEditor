using System.Text.Json.Serialization;

namespace YellowBlackEditor.Models
{
    public class Transfer
    {
        public const string KindNote = "note";
        public const string KindClient = "client";
        public const string KindText = "text";
        public const string KindFile = "file";

        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string FromId { get; set; } = "";
        public string FromName { get; set; } = "";
        public string ToId { get; set; } = "";
        public string ToName { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Kind { get; set; } = KindText;
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public string Text { get; set; } = "";
        public string FileName { get; set; } = "";
        public string ClientName { get; set; } = "";
        public string State { get; set; } = "new";
        public DateTime? StateAt { get; set; }

        [JsonIgnore]
        public string Folder { get; set; } = "";

        [JsonIgnore]
        public bool IsNew => State == "new";

        [JsonIgnore]
        public string KindTitle => Kind switch
        {
            KindNote => "Заметка",
            KindClient => "Клиент",
            KindFile => "Файл",
            _ => "Текст"
        };

        [JsonIgnore]
        public string Glyph => Kind switch
        {
            KindNote => "",
            KindClient => "",
            KindFile => "",
            _ => ""
        };

        [JsonIgnore]
        public string StateText => State switch
        {
            "accepted" => "получено",
            "declined" => "удалено получателем",
            _ => "ещё не открыли"
        };
    }
}
