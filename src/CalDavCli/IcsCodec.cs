using System.Globalization;
using System.Text;

namespace NetEaseCalDav;

public static class IcsCodec
{
    public static IReadOnlyList<EventInfo> ParseEvents(string ics, string href, string etag, List<string> warnings)
    {
        var lines = Unfold(ics);
        var result = new List<EventInfo>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase)) continue;
            var fields = new Dictionary<string, (string RawKey, string Value)>(StringComparer.OrdinalIgnoreCase);
            var nestedComponentDepth = 0;
            for (i++; i < lines.Count && !lines[i].Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase); i++)
            {
                if (lines[i].StartsWith("BEGIN:", StringComparison.OrdinalIgnoreCase))
                {
                    nestedComponentDepth++;
                    continue;
                }
                if (lines[i].StartsWith("END:", StringComparison.OrdinalIgnoreCase))
                {
                    if (nestedComponentDepth > 0) nestedComponentDepth--;
                    continue;
                }
                if (nestedComponentDepth > 0) continue;

                var colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                var rawKey = lines[i][..colon];
                var key = rawKey.Split(';')[0];
                if (!fields.ContainsKey(key)) fields[key] = (rawKey, lines[i][(colon + 1)..]);
            }
            if (!fields.TryGetValue("UID", out var uid) || string.IsNullOrWhiteSpace(uid.Value))
            {
                warnings.Add($"Skipped event without UID at {href}");
                continue;
            }
            fields.TryGetValue("DTSTART", out var start);
            fields.TryGetValue("DTEND", out var end);
            var allDay = start.RawKey?.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) == true || (start.Value?.Length == 8 && !start.Value.Contains('T'));
            result.Add(new EventInfo(
                Unescape(uid.Value), href, etag,
                Get(fields, "SUMMARY"), NormalizeDate(start.Value), NormalizeDate(end.Value), allDay,
                GetParameter(start.RawKey, "TZID"), Get(fields, "LOCATION"), Get(fields, "DESCRIPTION")));
        }
        return result;
    }

    public static string CreateEvent(string uid, string summary, DateTimeOffset start, DateTimeOffset end, string? location, string? description)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//OpenClaw//NetEase CalDAV Skill//EN\r\nCALSCALE:GREGORIAN\r\nBEGIN:VEVENT\r\n");
        sb.Append("UID:").Append(Escape(uid)).Append("\r\nDTSTAMP:").Append(stamp).Append("\r\n");
        sb.Append("DTSTART:").Append(start.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("DTEND:").Append(end.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture)).Append("\r\n");
        sb.Append("SUMMARY:").Append(Escape(summary)).Append("\r\n");
        if (!string.IsNullOrEmpty(location)) sb.Append("LOCATION:").Append(Escape(location)).Append("\r\n");
        if (!string.IsNullOrEmpty(description)) sb.Append("DESCRIPTION:").Append(Escape(description)).Append("\r\n");
        sb.Append("END:VEVENT\r\nEND:VCALENDAR\r\n");
        return sb.ToString();
    }

    public static string UpdateEvent(string ics, IReadOnlyDictionary<string, string> changes)
    {
        var lines = Unfold(ics).ToList();
        var begin = lines.FindIndex(x => x.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase));
        var end = lines.FindIndex(begin + 1, x => x.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase));
        if (begin < 0 || end < 0) throw new CliException("PARSE_ERROR", "Server event does not contain VEVENT", 5);
        foreach (var (key, value) in changes)
        {
            var index = lines.FindIndex(begin + 1, end - begin - 1, x => x.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase) || x.StartsWith(key + ";", StringComparison.OrdinalIgnoreCase));
            var replacement = $"{key}:{Escape(value)}";
            if (index >= 0) lines[index] = replacement;
            else { lines.Insert(end, replacement); end++; }
        }
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static List<string> Unfold(string text)
    {
        var output = new List<string>();
        foreach (var line in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if ((line.StartsWith(' ') || line.StartsWith('\t')) && output.Count > 0) output[^1] += line[1..];
            else if (line.Length > 0) output.Add(line);
        }
        return output;
    }

    private static string Get(Dictionary<string, (string RawKey, string Value)> fields, string key) => fields.TryGetValue(key, out var v) ? Unescape(v.Value) : string.Empty;
    private static string? GetParameter(string? rawKey, string name) => rawKey?.Split(';').Skip(1).Select(x => x.Split('=', 2)).FirstOrDefault(x => x.Length == 2 && x[0].Equals(name, StringComparison.OrdinalIgnoreCase))?.ElementAt(1);
    private static string? NormalizeDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string[] formats = ["yyyyMMdd'T'HHmmss'Z'", "yyyyMMdd'T'HHmmss", "yyyyMMdd"];
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
            return parsed.ToString(value.Length == 8 ? "yyyy-MM-dd" : "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        return value;
    }
    private static string Escape(string value) => value.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r\n", "\\n").Replace("\n", "\\n");
    private static string Unescape(string value) => value.Replace("\\n", "\n", StringComparison.OrdinalIgnoreCase).Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
}
