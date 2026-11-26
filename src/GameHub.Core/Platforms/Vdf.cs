using System.Text;

namespace GameHub.Core.Platforms;

/// <summary>Leitor mínimo do formato KeyValues/VDF da Valve (libraryfolders.vdf, appmanifest_*.acf).</summary>
public sealed class VdfNode
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? this[string key] => Values.TryGetValue(key, out var v) ? v : null;
    public VdfNode? Child(string key) => Children.TryGetValue(key, out var c) ? c : null;

    public static VdfNode Parse(string text)
    {
        var tokens = Tokenize(text).GetEnumerator();
        var root = new VdfNode();
        ParseInto(root, tokens);
        return root;
    }

    private static void ParseInto(VdfNode node, IEnumerator<string?> tokens)
    {
        while (tokens.MoveNext())
        {
            var key = tokens.Current;
            if (key is null) return; // "}"
            if (!tokens.MoveNext()) return;
            var next = tokens.Current;
            if (next == "{")
            {
                var child = new VdfNode();
                ParseInto(child, tokens);
                node.Children[key] = child;
            }
            else if (next is not null)
            {
                node.Values[key] = next;
            }
            else return;
        }
    }

    // Retorna strings (chaves/valores), "{" para abertura e null para "}".
    private static IEnumerable<string?> Tokenize(string text)
    {
        var i = 0;
        while (i < text.Length)
        {
            var ch = text[i];
            if (char.IsWhiteSpace(ch)) { i++; continue; }
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/') { while (i < text.Length && text[i] != '\n') i++; continue; }
            if (ch == '{') { i++; yield return "{"; continue; }
            if (ch == '}') { i++; yield return null; continue; }
            if (ch == '"')
            {
                i++;
                var sb = new StringBuilder();
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        var n = text[i + 1];
                        sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
                        i += 2;
                        continue;
                    }
                    sb.Append(text[i++]);
                }
                i++;
                yield return sb.ToString();
                continue;
            }
            // token sem aspas
            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not '{' and not '}' and not '"') i++;
            yield return text[start..i];
        }
    }
}
