using System.Text;

namespace GameHub.Core.Text;

/// <summary>CSV (RFC 4180) com detecção de separador: vírgula, ponto e vírgula ou tabulação.</summary>
public static class Csv
{
    /// <summary>O GameHub exporta com ";" e BOM UTF-8, que o Excel em português abre direto em colunas.</summary>
    public const char DefaultSeparator = ';';

    public static string Write(IEnumerable<IReadOnlyList<string?>> rows, char separator = DefaultSeparator)
    {
        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            for (var i = 0; i < row.Count; i++)
            {
                if (i > 0) sb.Append(separator);
                sb.Append(Escape(row[i], separator));
            }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    public static void WriteFile(string path, IEnumerable<IReadOnlyList<string?>> rows, char separator = DefaultSeparator)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, Write(rows, separator), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.Move(tmp, path, overwrite: true);
    }

    private static string Escape(string? value, char separator)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // Evita que planilhas interpretem o texto como fórmula.
        if (value[0] is '=' or '+' or '-' or '@' && !double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        var needs = value.IndexOfAny(new[] { separator, '"', '\r', '\n' }) >= 0 || value != value.Trim();
        return needs ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    public static char DetectSeparator(string text)
    {
        var firstLine = text;
        var nl = text.IndexOfAny(new[] { '\r', '\n' });
        if (nl >= 0) firstLine = text[..nl];
        var candidates = new[] { ';', ',', '\t' };
        return candidates.OrderByDescending(c => CountOutsideQuotes(firstLine, c)).First();
    }

    private static int CountOutsideQuotes(string line, char c)
    {
        var n = 0; var q = false;
        foreach (var ch in line)
        {
            if (ch == '"') q = !q;
            else if (ch == c && !q) n++;
        }
        return n;
    }

    public static List<string[]> Parse(string text, char? separator = null)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var sep = separator ?? DetectSeparator(text);
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var fieldStarted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }
            if (ch == '"' && field.Length == 0) { inQuotes = true; fieldStarted = true; }
            else if (ch == sep) { row.Add(Unprotect(field.ToString())); field.Clear(); fieldStarted = true; }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(Unprotect(field.ToString())); field.Clear();
                if (row.Count > 1 || row[0].Length > 0 || fieldStarted) rows.Add(row.ToArray());
                row.Clear(); fieldStarted = false;
            }
            else { field.Append(ch); fieldStarted = true; }
        }
        if (field.Length > 0 || row.Count > 0 || fieldStarted)
        {
            row.Add(Unprotect(field.ToString()));
            if (row.Count > 1 || row[0].Length > 0) rows.Add(row.ToArray());
        }
        return rows;
    }

    private static string Unprotect(string v) =>
        v.Length > 1 && v[0] == '\'' && v[1] is '=' or '+' or '-' or '@' ? v[1..] : v;
}

/// <summary>Tabela lida de CSV com acesso por nomes alternativos de coluna (pt e en, sem acento, sem caixa).</summary>
public sealed class CsvTable
{
    private readonly Dictionary<string, int> _index = new();
    public IReadOnlyList<string> Headers { get; }
    public IReadOnlyList<string[]> Rows { get; }

    public CsvTable(string text)
    {
        var all = Csv.Parse(text);
        if (all.Count == 0) throw new InvalidDataException("O arquivo CSV está vazio.");
        Headers = all[0];
        for (var i = 0; i < Headers.Count; i++)
            _index.TryAdd(Key(Headers[i]), i);
        Rows = all.Skip(1).ToList();
    }

    public static string Key(string header) => TitleNormalizer.StripAccents(header).ToLowerInvariant()
        .Replace("_", " ").Replace("-", " ").Replace("(", " ").Replace(")", " ").Trim()
        .Replace("  ", " ");

    public bool Has(params string[] names) => names.Any(n => _index.ContainsKey(Key(n)));

    public string? Get(string[] row, params string[] names)
    {
        foreach (var n in names)
            if (_index.TryGetValue(Key(n), out var i) && i < row.Length)
            {
                var v = row[i].Trim();
                return v.Length == 0 ? null : v;
            }
        return null;
    }
}
