using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GameHub.Core.Data;

internal static class DbConvert
{
    private const char ListSeparator = '|';

    public static object ToDb(this string? s) => string.IsNullOrWhiteSpace(s) ? DBNull.Value : s.Trim();
    public static object ToDb(this double? d) => d.HasValue ? d.Value : DBNull.Value;
    public static object ToDb(this long? l) => l.HasValue ? l.Value : DBNull.Value;
    public static object ToDb(this DateTimeOffset? d) => d.HasValue ? d.Value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : DBNull.Value;
    public static string ToDb(this DateTimeOffset d) => d.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static object ListToDb(IEnumerable<string> items)
    {
        var clean = items.Select(i => i.Replace(ListSeparator, '/').Trim())
                         .Where(i => i.Length > 0)
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .ToList();
        return clean.Count == 0 ? DBNull.Value : string.Join(ListSeparator, clean);
    }

    public static List<string> ListFromDb(string? s) =>
        string.IsNullOrEmpty(s) ? new() : s.Split(ListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static string? Str(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    public static long? Long(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetInt64(i);
    }

    public static double? Dbl(this SqliteDataReader r, string col)
    {
        var i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetDouble(i);
    }

    public static DateTimeOffset? Date(this SqliteDataReader r, string col)
    {
        var s = r.Str(col);
        return s is null ? null : DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    public static void Add(this SqliteCommand cmd, string name, object? value) =>
        cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
