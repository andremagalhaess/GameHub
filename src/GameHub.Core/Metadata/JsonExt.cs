using System.Globalization;
using System.Text.Json;

namespace GameHub.Core.Metadata;

internal static class JsonExt
{
    public static JsonElement? Prop(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined ? v : null;

    public static string? Str(this JsonElement e, string name) => e.Prop(name) is { } v ? v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() is { Length: > 0 } s ? s : null,
        JsonValueKind.Number => v.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null,
    } : null;

    public static long? Long(this JsonElement e, string name) => e.Prop(name) is { } v
        ? v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l
        : v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ls) ? ls : null
        : null;

    public static double? Dbl(this JsonElement e, string name) => e.Prop(name) is { } v
        ? v.ValueKind == JsonValueKind.Number ? v.GetDouble()
        : v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null
        : null;

    public static bool Bool(this JsonElement e, string name) => e.Prop(name) is { ValueKind: JsonValueKind.True };

    public static IEnumerable<JsonElement> Arr(this JsonElement e, string name) =>
        e.Prop(name) is { ValueKind: JsonValueKind.Array } a ? a.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
