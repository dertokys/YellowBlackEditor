using System.Text;

namespace YellowBlackEditor.Utils
{
    public static class RtfText
    {
        private static readonly HashSet<string> SkipDestinations = new()
        {
            "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "header", "footer", "listtable", "listoverridetable", "themedata", "datastore"
        };

        static RtfText()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public static string Extract(string rtf)
        {
            var sb = new StringBuilder(rtf.Length / 2);
            var bytes = new List<byte>();
            var encoding = Encoding.GetEncoding(1251);
            var skip = new Stack<bool>();
            var skipping = false;
            var ucSkip = 1;
            var pendingSkip = 0;
            var groupStart = false;

            void FlushBytes()
            {
                if (bytes.Count == 0) return;
                if (!skipping) sb.Append(encoding.GetString(bytes.ToArray()));
                bytes.Clear();
            }

            void Emit(string s)
            {
                FlushBytes();
                if (!skipping) sb.Append(s);
            }

            var i = 0;
            while (i < rtf.Length)
            {
                var ch = rtf[i];
                if (ch == '{')
                {
                    FlushBytes();
                    skip.Push(skipping);
                    groupStart = true;
                    i++;
                    continue;
                }
                if (ch == '}')
                {
                    FlushBytes();
                    skipping = skip.Count > 0 && skip.Pop();
                    groupStart = false;
                    i++;
                    continue;
                }
                if (ch == '\\' && i + 1 < rtf.Length)
                {
                    var next = rtf[i + 1];
                    if (char.IsLetter(next))
                    {
                        var j = i + 1;
                        while (j < rtf.Length && char.IsLetter(rtf[j])) j++;
                        var word = rtf[(i + 1)..j];
                        var k = j;
                        if (k < rtf.Length && (rtf[k] == '-' || char.IsDigit(rtf[k])))
                        {
                            k++;
                            while (k < rtf.Length && char.IsDigit(rtf[k])) k++;
                        }
                        int.TryParse(rtf[j..k], out var param);
                        if (k < rtf.Length && rtf[k] == ' ') k++;
                        i = k;

                        if (groupStart && SkipDestinations.Contains(word)) skipping = true;
                        groupStart = false;

                        switch (word)
                        {
                            case "par":
                            case "line":
                                Emit("\n");
                                break;
                            case "tab":
                                Emit("\t");
                                break;
                            case "uc":
                                ucSkip = param;
                                break;
                            case "u":
                                Emit(((char)(param < 0 ? param + 65536 : param)).ToString());
                                pendingSkip = ucSkip;
                                break;
                        }
                        continue;
                    }

                    groupStart = false;
                    if (next == '\'' && i + 3 < rtf.Length)
                    {
                        if (pendingSkip > 0) pendingSkip--;
                        else if (byte.TryParse(rtf.AsSpan(i + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) bytes.Add(b);
                        i += 4;
                        continue;
                    }
                    if (next == '*') skipping = true;
                    else if (next is '\\' or '{' or '}') Emit(next.ToString());
                    else if (next == '~') Emit(" ");
                    i += 2;
                    continue;
                }

                groupStart = false;
                if (ch is '\r' or '\n')
                {
                    i++;
                    continue;
                }
                if (pendingSkip > 0)
                {
                    pendingSkip--;
                    i++;
                    continue;
                }
                Emit(ch.ToString());
                i++;
            }
            FlushBytes();
            return sb.ToString();
        }
    }
}
