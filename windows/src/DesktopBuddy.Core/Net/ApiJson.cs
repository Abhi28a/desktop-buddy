using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace DesktopBuddy.Core;

// Loose JSON reading, the C# counterpart of Swift's `json["x"] as? String` chains:
// a wrong type or a missing key gives null instead of throwing.

internal static class ApiJson
{
    public static JsonObject? Obj(this JsonNode? n, string key) => n is JsonObject o && o[key] is JsonObject r ? r : null;

    public static string? Str(this JsonNode? n, string key) =>
        n is JsonObject o && o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static bool? Bool(this JsonNode? n, string key) =>
        n is JsonObject o && o[key] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    /// A number, or a numeric string, as a double (Swift: `as? Double ?? Double(string)`).
    public static double? Num(this JsonNode? n, string key)
    {
        if (n is not JsonObject o || o[key] is not JsonValue v) return null;
        if (v.TryGetValue<double>(out var d)) return d;
        if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
        return null;
    }

    /// The objects of an array (non-objects are skipped); empty when missing.
    public static List<JsonObject> Objs(this JsonNode? n, string key)
    {
        var list = new List<JsonObject>();
        if (n is JsonObject o && o[key] is JsonArray a)
            foreach (var item in a)
                if (item is JsonObject io) list.Add(io);
        return list;
    }

    /// The strings of an array (non-strings are skipped); empty when missing.
    public static List<string> Strs(this JsonNode? n, string key)
    {
        var list = new List<string>();
        if (n is JsonObject o && o[key] is JsonArray a)
            foreach (var item in a)
                if (item is JsonValue v && v.TryGetValue<string>(out var s)) list.Add(s);
        return list;
    }

    /// A string that is empty counts as missing (Swift: `.flatMap { $0.isEmpty ? nil : $0 }`).
    public static string? NonEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;

    /// A web address when the text is a valid absolute URL (Swift: `URL(string:)`), else null.
    public static string? Url(string? s) =>
        s is not null && Uri.TryCreate(s, UriKind.Absolute, out _) ? s : null;
}

internal static class ApiText
{
    /// Trims spaces and tabs (Foundation's CharacterSet.whitespaces), not line breaks.
    public static string TrimSpaces(string s)
    {
        static bool Space(char c) => c == '\t' || char.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;
        var start = 0;
        var end = s.Length;
        while (start < end && Space(s[start])) start++;
        while (end > start && Space(s[end - 1])) end--;
        return s[start..end];
    }

    private static readonly Regex Tags = new("<[^>]+>", RegexOptions.Compiled);

    /// Removes HTML tags and decodes the few entities Teams uses (port of stripHTML in MicrosoftSources.swift).
    public static string StripHtml(string s) =>
        Tags.Replace(s, "").Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Trim();

    /// Gmail's snippet escapes a handful of characters.
    public static string DecodeEntities(string s) =>
        s.Replace("&#39;", "'").Replace("&quot;", "\"").Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">");

    /// Sorts like Finder (Foundation's localizedStandardCompare): case-insensitive, numbers by value.
    public static int LocalizedStandardCompare(string a, string b)
    {
        var ca = CultureInfo.CurrentCulture.CompareInfo;
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var si = i; var sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length < nb.Length ? -1 : 1;
                var c = string.CompareOrdinal(na, nb);
                if (c != 0) return c < 0 ? -1 : 1;
            }
            else
            {
                var si = i; var sj = j;
                while (i < a.Length && !char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && !char.IsAsciiDigit(b[j])) j++;
                var c = ca.Compare(a, si, i - si, b, sj, j - sj, CompareOptions.IgnoreCase);
                if (c != 0) return c < 0 ? -1 : 1;
            }
        }
        var left = a.Length - i;
        var right = b.Length - j;
        return left == right ? 0 : (left < right ? -1 : 1);
    }
}
