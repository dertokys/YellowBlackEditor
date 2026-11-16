using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using YellowBlackEditor.Models;

namespace YellowBlackEditor.Utils
{
    public sealed class ZammadAuthException : Exception
    {
        public ZammadAuthException() : base("Нет входа в Zammad") { }
    }

    public sealed record ZammadTime(int TicketId, string Number, string Headline, string Client, DateTime At, double Minutes);

    public sealed class ZammadApi : IDisposable
    {
        public const string FolderPrefix = "zammad:";

        private readonly string _base;
        private readonly HttpClient _http;
        private readonly Dictionary<int, string> _headlines = new();

        private ZammadApi(string baseUrl, string cookie)
        {
            _base = baseUrl.TrimEnd('/');
            _http = new HttpClient(new HttpClientHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _http.DefaultRequestHeaders.Add("Cookie", cookie);
            _http.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
        }

        public static async Task<ZammadApi> ConnectAsync(string baseUrl, IntPtr hwnd)
        {
            var cookie = await WebViewHost.CookieHeaderAsync(baseUrl.TrimEnd('/') + "/", hwnd);
            if (cookie.Length == 0) throw new ZammadAuthException();
            return new ZammadApi(baseUrl, cookie);
        }

        public static bool IsZammadFolder(string folder) => folder.StartsWith(FolderPrefix, StringComparison.Ordinal);

        public static int TicketOf(string folder) =>
            IsZammadFolder(folder) && int.TryParse(folder[FolderPrefix.Length..], out var id) ? id : 0;

        private async Task<JsonElement> GetAsync(string path, CancellationToken ct)
        {
            using var response = await _http.GetAsync(_base + "/api/v1/" + path, ct);
            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                (response.StatusCode == HttpStatusCode.Forbidden && path.StartsWith("users/me", StringComparison.Ordinal)))
                throw new ZammadAuthException();
            response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.Clone();
        }

        private static string Str(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

        private static int Int(JsonElement e, string name) =>
            e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

        private static double Num(JsonElement e, string name)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            return v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        }

        private static DateTime Time(JsonElement e, string name) =>
            DateTime.TryParse(Str(e, name), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
                ? DateTime.SpecifyKind(d, DateTimeKind.Utc) : DateTime.MinValue;

        private static JsonElement Asset(JsonElement assets, string kind, int id) =>
            id != 0 && assets.ValueKind == JsonValueKind.Object && assets.TryGetProperty(kind, out var map) &&
            map.TryGetProperty(id.ToString(), out var item) ? item : default;

        private static string Clean(string s) => Regex.Replace(s.Replace('ㅤ', ' ').Replace(' ', ' '), @"\s+", " ").Trim();

        public async Task<List<ZammadTime>> PersonalTimeAsync(DateTime from, DateTime to, CancellationToken ct)
        {
            await GetAsync("users/me", ct);
            var overviews = await GetAsync("ticket_overviews", ct);
            var link = "my_assigned";
            if (overviews.ValueKind == JsonValueKind.Array)
                foreach (var o in overviews.EnumerateArray())
                    if (Str(o, "name").Contains("Личн", StringComparison.OrdinalIgnoreCase))
                    {
                        link = Str(o, "link");
                        break;
                    }
            var view = await GetAsync("ticket_overviews?view=" + Uri.EscapeDataString(link), ct);
            var assets = view.TryGetProperty("assets", out var a) ? a : default;
            var fromUtc = from.ToUniversalTime();
            var toUtc = to.ToUniversalTime();
            var ids = new List<int>();
            if (view.TryGetProperty("index", out var index) && index.TryGetProperty("tickets", out var tickets) && tickets.ValueKind == JsonValueKind.Array)
                foreach (var t in tickets.EnumerateArray())
                    if (Time(t, "updated_at") >= fromUtc) ids.Add(Int(t, "id"));

            var result = new List<ZammadTime>();
            using var gate = new SemaphoreSlim(4);
            var jobs = ids.Where(id => id != 0).Select(async id =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    var list = await GetAsync($"tickets/{id}/time_accountings", ct);
                    if (list.ValueKind != JsonValueKind.Array) return new List<ZammadTime>();
                    var hits = list.EnumerateArray()
                        .Select(x => (At: Time(x, "created_at"), Minutes: Num(x, "time_unit")))
                        .Where(x => x.Minutes > 0 && x.At >= fromUtc && x.At < toUtc).ToList();
                    if (hits.Count == 0) return new List<ZammadTime>();
                    var ticket = Asset(assets, "Ticket", id);
                    var org = Asset(assets, "Organization", Int(ticket, "organization_id"));
                    var customer = Asset(assets, "User", Int(ticket, "customer_id"));
                    var client = Clean(Str(org, "name"));
                    if (client.Length == 0) client = Clean(Str(customer, "firstname") + " " + Str(customer, "lastname"));
                    var headline = await HeadlineAsync(id, Str(ticket, "title"), ct);
                    var number = Str(ticket, "number");
                    return hits.Select(h => new ZammadTime(id, number, headline, client, h.At.ToLocalTime(), h.Minutes)).ToList();
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();
            foreach (var part in await Task.WhenAll(jobs)) result.AddRange(part);
            return result.OrderBy(r => r.At).ToList();
        }

        private async Task<string> HeadlineAsync(int id, string title, CancellationToken ct)
        {
            lock (_headlines)
                if (_headlines.TryGetValue(id, out var known)) return known;
            var headline = Clean(title);
            try
            {
                var articles = await GetAsync($"ticket_articles/by_ticket/{id}", ct);
                if (articles.ValueKind == JsonValueKind.Array)
                {
                    var first = articles.EnumerateArray().OrderBy(x => Str(x, "created_at")).FirstOrDefault();
                    var body = Str(first, "body");
                    if (Str(first, "content_type").Contains("html", StringComparison.OrdinalIgnoreCase))
                    {
                        body = Regex.Replace(body, @"<br\s*/?>|</(p|div|li|tr|h\d)>", "\n", RegexOptions.IgnoreCase);
                        body = WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]+>", ""));
                    }
                    var request = RequestText(body);
                    if (request.Length > 0) headline = request;
                    else if (headline.StartsWith("ITF", StringComparison.OrdinalIgnoreCase))
                    {
                        var line = body.Split('\n').Select(Clean).FirstOrDefault(l => l.Length > 0) ?? "";
                        if (line.Length > 0) headline = line.Length > 160 ? line[..160] + "…" : line;
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
            lock (_headlines) _headlines[id] = headline;
            return headline;
        }

        public static string RequestText(string body)
        {
            var lines = body.Replace("\r", "").Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (!Regex.IsMatch(lines[i], @"^\s*Заявка\s*:\s*$", RegexOptions.IgnoreCase)) continue;
                var value = new List<string>();
                for (var j = i + 1; j < lines.Length; j++)
                {
                    if (Regex.IsMatch(lines[j], @"^\s*[^:]{2,40}:\s*$")) break;
                    if (lines[j].Trim().Length == 0 && value.Count > 0) break;
                    if (lines[j].Trim().Length > 0) value.Add(lines[j].Trim());
                }
                var text = Clean(string.Join(" ", value));
                return text.Length > 200 ? text[..200] + "…" : text;
            }
            return "";
        }

        public async Task<string> LinkTitleAsync(ChatLink link, CancellationToken ct)
        {
            var kb = Regex.Match(link.Url, @"KnowledgeBaseAnswer/(\d+)/");
            if (link.Kind == "kb" && kb.Success)
            {
                var id = int.Parse(kb.Groups[1].Value);
                var data = await GetAsync("knowledge_bases/1/answers/" + id, ct);
                if (data.TryGetProperty("assets", out var assets) && assets.TryGetProperty("KnowledgeBaseAnswerTranslation", out var tr))
                    foreach (var t in tr.EnumerateObject())
                        if (Int(t.Value, "answer_id") == id && Str(t.Value, "title").Length > 0) return Clean(Str(t.Value, "title"));
                return "";
            }
            var ticket = Regex.Match(link.Url, @"#ticket/zoom/(\d+)");
            if (link.Kind == "ticket" && ticket.Success)
            {
                var id = int.Parse(ticket.Groups[1].Value);
                var data = await GetAsync("tickets/" + id, ct);
                var headline = await HeadlineAsync(id, Str(data, "title"), ct);
                var number = Str(data, "number");
                return (number.Length > 0 ? "Заявка №" + number + ": " : "Заявка: ") + headline;
            }
            return "";
        }

        public static TimeRow ToRow(ZammadTime t) =>
            new(FolderPrefix + t.TicketId,
                new TimeEntry
                {
                    Id = "zammad-" + t.TicketId + "-" + t.At.Ticks,
                    Date = t.At.Date,
                    Minutes = (int)Math.Round(t.Minutes),
                    Comment = "",
                    CreatedAt = t.At
                },
                t.Client,
                t.Headline.Length > 0 ? t.Headline : "Заявка №" + t.Number,
                "Zammad");

        public void Dispose() => _http.Dispose();
    }
}
