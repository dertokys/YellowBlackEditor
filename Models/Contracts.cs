using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace YellowBlackEditor.Models
{
    public class ContractTerm : Observable
    {
        private bool _has;
        private string _hours = "";
        private string _note = "";

        public bool Has
        {
            get => _has;
            set
            {
                if (Set(ref _has, value)) Raise(nameof(NoContract));
            }
        }

        public string Hours { get => _hours; set => Set(ref _hours, value); }
        public string Note { get => _note; set => Set(ref _note, value); }

        [JsonIgnore]
        public bool NoContract
        {
            get => !_has;
            set => Has = !value;
        }

        [JsonIgnore]
        public double? HoursValue
        {
            get
            {
                var m = Regex.Match(_hours ?? "", @"\d+(?:[.,]\d+)?");
                return m.Success && double.TryParse(m.Value.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
            }
        }

        [JsonIgnore]
        public bool IsEmpty => !_has && string.IsNullOrWhiteSpace(_hours) && string.IsNullOrWhiteSpace(_note);
    }

    public class ClientContracts
    {
        public ContractTerm Spo { get; set; } = new();
        public ContractTerm Ko { get; set; } = new();

        [JsonIgnore]
        public bool IsEmpty => Spo.IsEmpty && Ko.IsEmpty;
    }
}
