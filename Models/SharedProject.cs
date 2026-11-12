using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace YellowBlackEditor.Models
{
    public class SharedMember
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Machine { get; set; } = "";
        public DateTime JoinedAt { get; set; }
        public int Minutes { get; set; }
    }

    public class SharedPerson
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Machine { get; set; } = "";
        public DateTime LastSeen { get; set; }
    }

    public class SharedProject
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Status { get; set; } = "Новый";
        public DateTime? Deadline { get; set; }
        public string Comment { get; set; } = "";
        public ObservableCollection<ProjectTask> Tasks { get; set; } = new();

        public string ClientName { get; set; } = "";
        public string ClientPath { get; set; } = "";
        public string ClientInn { get; set; } = "";

        public string OwnerId { get; set; } = "";
        public string OwnerName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string UpdatedById { get; set; } = "";
        public string UpdatedByName { get; set; } = "";
        public List<SharedMember> Members { get; set; } = new();
        public List<string> Invited { get; set; } = new();

        [JsonIgnore]
        public bool ForEveryone => Invited.Count == 0;

        public bool IsMember(string id) => Members.Any(m => m.Id == id);

        public bool VisibleTo(string id) => ForEveryone || OwnerId == id || Invited.Contains(id) || IsMember(id);

        [JsonIgnore]
        public bool IsClosed => Status == "Готово";

        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Проект без названия" : Name.Trim();

        public string MemberName(string id) =>
            Members.FirstOrDefault(m => m.Id == id)?.Name is { Length: > 0 } n ? n : id == OwnerId ? OwnerName : id;

        public IEnumerable<ProjectTask> AllTasks()
        {
            foreach (var t in Tasks)
            {
                yield return t;
                foreach (var c in t.AllChildren()) yield return c;
            }
        }

        [JsonIgnore]
        public string Progress
        {
            get
            {
                var all = AllTasks().ToList();
                return all.Count == 0 ? "" : $"{all.Count(t => t.Done)} из {all.Count}";
            }
        }
    }
}
