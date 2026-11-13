using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace YellowBlackEditor.Utils
{
    public static class JsonMerge
    {
        public sealed class Result
        {
            public JsonNode? Merged { get; set; }
            public List<string> Conflicts { get; } = new();
        }

        public static JsonNode? Parse(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try { return JsonNode.Parse(text); }
            catch { return null; }
        }

        public static void EnsureIds(JsonNode? node, string path = "")
        {
            switch (node)
            {
                case JsonObject o:
                    foreach (var kv in o.ToList())
                        EnsureIds(kv.Value, path.Length == 0 ? kv.Key : path + "." + kv.Key);
                    break;
                case JsonArray a:
                    var seen = new Dictionary<string, int>();
                    var used = new HashSet<string>();
                    foreach (var item in a)
                    {
                        if (item is not JsonObject io) continue;
                        var id = io["Id"]?.GetValue<string>();
                        if (string.IsNullOrEmpty(id) || used.Contains(id))
                        {
                            var probe = (JsonObject)io.DeepClone();
                            probe.Remove("Id");
                            var basis = path + "|" + probe.ToJsonString();
                            seen[basis] = seen.GetValueOrDefault(basis) + 1;
                            id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(basis + "|" + seen[basis])))[..16].ToLowerInvariant();
                            io["Id"] = id;
                        }
                        used.Add(id);
                    }
                    foreach (var item in a) EnsureIds(item, path);
                    break;
            }
        }

        public static string WithIds(string json)
        {
            var node = Parse(json);
            if (node == null) return json;
            EnsureIds(node);
            return node.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        public static Result Merge(JsonNode? b, JsonNode? l, JsonNode? r, bool preferLocal)
        {
            var res = new Result();
            res.Merged = M(b, l, r, "", res, preferLocal);
            return res;
        }

        private static bool Eq(JsonNode? a, JsonNode? b) => JsonNode.DeepEquals(a, b);

        private static JsonNode? Clone(JsonNode? n) => n?.DeepClone();

        private static bool HasIds(JsonArray a) => a.All(x => x is JsonObject o && o["Id"] is JsonValue);

        private static string IdOf(JsonNode? n) => n!["Id"]!.GetValue<string>();

        private static JsonNode? M(JsonNode? b, JsonNode? l, JsonNode? r, string path, Result res, bool preferLocal)
        {
            if (Eq(l, r)) return Clone(l);
            if (Eq(b, l)) return Clone(r);
            if (Eq(b, r)) return Clone(l);

            if (l is JsonObject lo && r is JsonObject ro)
            {
                var bo = b as JsonObject;
                var o = new JsonObject();
                var keys = lo.Select(kv => kv.Key).Concat(ro.Select(kv => kv.Key).Where(k => !lo.ContainsKey(k))).ToList();
                foreach (var k in keys)
                {
                    var inL = lo.ContainsKey(k);
                    var inR = ro.ContainsKey(k);
                    var inB = bo != null && bo.ContainsKey(k);
                    if (!inL && inB && Eq(bo![k], ro[k])) continue;
                    if (!inR && inB && Eq(bo![k], lo[k])) continue;
                    o[k] = M(bo?[k], inL ? lo[k] : null, inR ? ro[k] : null, path.Length == 0 ? k : path + "." + k, res, preferLocal);
                }
                return o;
            }

            if (l is JsonArray la && r is JsonArray ra && HasIds(la) && HasIds(ra) && (b is not JsonArray bx || HasIds(bx)))
            {
                var bmap = (b as JsonArray)?.ToDictionary(IdOf, x => x!) ?? new Dictionary<string, JsonNode>();
                var lmap = la.ToDictionary(IdOf, x => x!);
                var rmap = ra.ToDictionary(IdOf, x => x!);
                var order = la.Select((x, i) => (Id: IdOf(x), Pos: i, Side: 0))
                    .Concat(ra.Select((x, i) => (Id: IdOf(x), Pos: i, Side: 1)).Where(x => !lmap.ContainsKey(x.Id)))
                    .OrderBy(x => x.Pos).ThenBy(x => x.Side)
                    .Select(x => x.Id)
                    .ToList();
                var arr = new JsonArray();
                foreach (var id in order)
                {
                    bmap.TryGetValue(id, out var bi);
                    lmap.TryGetValue(id, out var li);
                    rmap.TryGetValue(id, out var ri);
                    if (li != null && ri != null) arr.Add(M(bi, li, ri, path + "[" + id + "]", res, preferLocal));
                    else if (li != null)
                    {
                        if (bi != null && Eq(bi, li)) continue;
                        arr.Add(Clone(li));
                    }
                    else if (ri != null)
                    {
                        if (bi != null && Eq(bi, ri)) continue;
                        arr.Add(Clone(ri));
                    }
                }
                return arr;
            }

            res.Conflicts.Add(path.Length == 0 ? "(весь файл)" : path);
            return Clone(preferLocal ? l : r);
        }
    }
}
