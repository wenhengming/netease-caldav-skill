using System.Text.Json.Serialization;

namespace NetEaseCalDav;

public sealed record CalDavConfig(Uri ServerUrl, string Username, string Password, string TimeZone, string? DefaultCalendar)
{
    public static CalDavConfig FromEnvironment()
    {
        var server = Environment.GetEnvironmentVariable("CALDAV_SERVER_URL");
        var username = Environment.GetEnvironmentVariable("CALDAV_USERNAME");
        var password = Environment.GetEnvironmentVariable("CALDAV_PASSWORD");
        var timezone = Environment.GetEnvironmentVariable("CALDAV_TIMEZONE");
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(server)) missing.Add("CALDAV_SERVER_URL");
        if (string.IsNullOrWhiteSpace(username)) missing.Add("CALDAV_USERNAME");
        if (string.IsNullOrWhiteSpace(password)) missing.Add("CALDAV_PASSWORD");
        if (string.IsNullOrWhiteSpace(timezone)) missing.Add("CALDAV_TIMEZONE");
        if (missing.Count > 0) throw new CliException("CONFIG_MISSING", $"Missing configuration: {string.Join(", ", missing)}", 2);
        if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new CliException("INVALID_ARGUMENT", "CALDAV_SERVER_URL must be an absolute HTTPS URL", 2);
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(timezone!); }
        catch { throw new CliException("INVALID_ARGUMENT", "CALDAV_TIMEZONE must be a valid IANA timezone", 2); }
        return new CalDavConfig(uri, username!, password!, timezone!, Environment.GetEnvironmentVariable("CALDAV_DEFAULT_CALENDAR"));
    }
}

public sealed record CalendarInfo(string Id, string Url, string DisplayName, string Description);
public sealed record EventInfo(string Uid, string Href, string ETag, string Summary, string? Start, string? End, bool AllDay, string? TimeZone, string Location, string Description);

public sealed class CliException : Exception
{
    public string Code { get; }
    public int ExitCode { get; }
    public bool Retryable { get; }
    public CliException(string code, string message, int exitCode, bool retryable = false, Exception? inner = null) : base(message, inner)
        => (Code, ExitCode, Retryable) = (code, exitCode, retryable);
}

public sealed record ErrorBody(string Code, string Message, bool Retryable);
public sealed record CliResult(bool Ok, string Command, object? Data, IReadOnlyList<string> Warnings, ErrorBody? Error)
{
    public static CliResult Success(string command, object? data, IReadOnlyList<string>? warnings = null) => new(true, command, data, warnings ?? [], null);
    public static CliResult Failure(string command, CliException ex) => new(false, command, null, [], new(ex.Code, ex.Message, ex.Retryable));
}
