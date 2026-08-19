using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace NetEaseCalDav;

public sealed class CalDavClient : IDisposable
{
    private static readonly XNamespace D = "DAV:";
    private static readonly XNamespace C = "urn:ietf:params:xml:ns:caldav";
    private readonly CalDavConfig _config;
    private readonly HttpClient _http;

    public CalDavClient(CalDavConfig config, HttpMessageHandler? handler = null)
    {
        _config = config;
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.Username}:{config.Password}")));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenClaw-NetEase-CalDAV/1.0");
    }

    public async Task<object> HealthAsync(CancellationToken ct)
    {
        using var request = XmlRequest("PROPFIND", _config.ServerUrl, "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>", "0");
        using var response = await SendAsync(request, ct);
        return new { status = "healthy", server = _config.ServerUrl.Host, httpStatus = (int)response.StatusCode };
    }

    public async Task<List<CalendarInfo>> DiscoverCalendarsAsync(CancellationToken ct)
    {
        const string principalBody = "<d:propfind xmlns:d=\"DAV:\"><d:prop><d:current-user-principal/></d:prop></d:propfind>";
        var principalDoc = await PropfindAsync(_config.ServerUrl, principalBody, "0", ct);
        // Some NetEase servers return current-user-principal with a 404 propstat.
        // Only consume href values from successful propstat blocks; otherwise the
        // failed property can be mistaken for a valid principal.
        var principal = SuccessfulPropertyHref(principalDoc, D + "current-user-principal")
            ?? principalDoc.Descendants(D + "response").Elements(D + "href").Select(x => x.Value.Trim()).FirstOrDefault(x => x.Length > 0);
        if (string.IsNullOrWhiteSpace(principal)) throw new CliException("DISCOVERY_FAILED", "CalDAV principal was not found", 5);

        const string homeBody = "<d:propfind xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\"><d:prop><c:calendar-home-set/></d:prop></d:propfind>";
        var homeDoc = await PropfindAsync(ToSafeUri(principal), homeBody, "0", ct);
        var home = SuccessfulPropertyHref(homeDoc, C + "calendar-home-set");
        if (string.IsNullOrWhiteSpace(home)) throw new CliException("DISCOVERY_FAILED", "CalDAV calendar home was not found", 5);

        const string calendarsBody = "<d:propfind xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\"><d:prop><d:displayname/><d:resourcetype/><c:calendar-description/></d:prop></d:propfind>";
        var doc = await PropfindAsync(ToSafeUri(home), calendarsBody, "1", ct);
        var calendars = new List<CalendarInfo>();
        foreach (var response in doc.Descendants(D + "response").Where(x => x.Descendants(C + "calendar").Any()))
        {
            var href = response.Element(D + "href")?.Value.Trim();
            if (string.IsNullOrEmpty(href)) continue;
            var url = ToSafeUri(href).ToString();
            var id = href.Trim('/').Split('/').LastOrDefault() ?? url;
            calendars.Add(new CalendarInfo(id, url,
                response.Descendants(D + "displayname").Select(x => x.Value).FirstOrDefault() ?? string.Empty,
                response.Descendants(C + "calendar-description").Select(x => x.Value).FirstOrDefault() ?? string.Empty));
        }
        return calendars;
    }

    public async Task<(List<EventInfo> Events, List<string> Warnings)> GetEventsAsync(string calendar, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to <= from || to - from > TimeSpan.FromDays(90)) throw new CliException("INVALID_ARGUMENT", "Event range must be positive and no longer than 90 days", 2);
        var calendarUri = await ResolveCalendarAsync(calendar, ct);
        var start = from.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var end = to.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        var body = $"<c:calendar-query xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\"><d:prop><d:getetag/><c:calendar-data/></d:prop><c:filter><c:comp-filter name=\"VCALENDAR\"><c:comp-filter name=\"VEVENT\"><c:time-range start=\"{start}\" end=\"{end}\"/></c:comp-filter></c:comp-filter></c:filter></c:calendar-query>";
        using var request = XmlRequest("REPORT", calendarUri, body, "1");
        using var response = await SendAsync(request, ct);
        var doc = ParseXml(await response.Content.ReadAsStringAsync(ct));
        var events = new List<EventInfo>();
        var warnings = new List<string>();
        foreach (var item in doc.Descendants(D + "response"))
        {
            var href = item.Element(D + "href")?.Value.Trim() ?? string.Empty;
            var etag = item.Descendants(D + "getetag").Select(x => x.Value.Trim()).FirstOrDefault() ?? string.Empty;
            var data = item.Descendants(C + "calendar-data").Select(x => x.Value).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(data)) continue;
            try { events.AddRange(IcsCodec.ParseEvents(data, ToSafeUri(href).ToString(), etag, warnings)); }
            catch { warnings.Add($"Skipped malformed event at {href}"); }
        }
        return (events, warnings);
    }

    public async Task<object> CreateEventAsync(string calendar, string summary, DateTimeOffset start, DateTimeOffset end, string? location, string? description, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(summary)) throw new CliException("INVALID_ARGUMENT", "summary is required", 2);
        if (end <= start) throw new CliException("INVALID_ARGUMENT", "end must be later than start", 2);
        var uid = Guid.NewGuid().ToString("D");
        var calendarUri = await ResolveCalendarAsync(calendar, ct);
        var target = new Uri(calendarUri.ToString().TrimEnd('/') + "/" + uid + ".ics");
        using var request = new HttpRequestMessage(HttpMethod.Put, target) { Content = CalendarContent(IcsCodec.CreateEvent(uid, summary, start, end, location, description)) };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");
        using var response = await SendAsync(request, ct);
        return new { uid, href = target.ToString(), etag = response.Headers.ETag?.Tag ?? string.Empty };
    }

    public async Task<object> UpdateEventAsync(string href, string etag, IReadOnlyDictionary<string, string> changes, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(etag)) throw new CliException("INVALID_ARGUMENT", "etag is required", 2);
        if (changes.Count == 0) throw new CliException("INVALID_ARGUMENT", "At least one update field is required", 2);
        var target = ToSafeUri(href);
        using var get = new HttpRequestMessage(HttpMethod.Get, target);
        using var current = await SendAsync(get, ct);
        var ics = await current.Content.ReadAsStringAsync(ct);
        using var put = new HttpRequestMessage(HttpMethod.Put, target) { Content = CalendarContent(IcsCodec.UpdateEvent(ics, changes)) };
        put.Headers.TryAddWithoutValidation("If-Match", etag);
        using var response = await SendAsync(put, ct);
        return new { href = target.ToString(), etag = response.Headers.ETag?.Tag ?? etag };
    }

    public async Task<object> DeleteEventAsync(string href, string etag, bool confirmed, CancellationToken ct)
    {
        if (!confirmed) throw new CliException("INVALID_ARGUMENT", "delete requires --confirm", 2);
        if (string.IsNullOrWhiteSpace(etag)) throw new CliException("INVALID_ARGUMENT", "etag is required", 2);
        var target = ToSafeUri(href);
        using var request = new HttpRequestMessage(HttpMethod.Delete, target);
        request.Headers.TryAddWithoutValidation("If-Match", etag);
        using var response = await SendAsync(request, ct);
        return new { href = target.ToString(), deleted = true };
    }

    private async Task<Uri> ResolveCalendarAsync(string calendar, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(calendar)) throw new CliException("INVALID_ARGUMENT", "calendar is required", 2);
        if (Uri.TryCreate(calendar, UriKind.Absolute, out var absolute)) return ToSafeUri(absolute.ToString());
        var found = (await DiscoverCalendarsAsync(ct)).FirstOrDefault(x => x.Id.Equals(calendar, StringComparison.OrdinalIgnoreCase));
        return found is null ? throw new CliException("CALENDAR_NOT_FOUND", "Calendar was not found", 4) : new Uri(found.Url);
    }

    private async Task<XDocument> PropfindAsync(Uri uri, string body, string depth, CancellationToken ct)
    {
        using var request = XmlRequest("PROPFIND", uri, body, depth);
        using var response = await SendAsync(request, ct);
        return ParseXml(await response.Content.ReadAsStringAsync(ct));
    }

    private static HttpRequestMessage XmlRequest(string method, Uri uri, string body, string depth)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), uri) { Content = new StringContent(body, Encoding.UTF8, "application/xml") };
        request.Headers.TryAddWithoutValidation("Depth", depth);
        return request;
    }
    private static StringContent CalendarContent(string ics) => new(ics, Encoding.UTF8, "text/calendar");

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            var response = await _http.SendAsync(request, ct);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            { response.Dispose(); throw new CliException("AUTH_FAILED", "CalDAV authentication or authorization failed", 3); }
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            { response.Dispose(); throw new CliException("ETAG_CONFLICT", "The event changed on the server; refresh before retrying", 4); }
            if (response.StatusCode == HttpStatusCode.NotFound)
            { response.Dispose(); throw new CliException("EVENT_NOT_FOUND", "The requested CalDAV resource was not found", 4); }
            if (!response.IsSuccessStatusCode)
            { var status = (int)response.StatusCode; response.Dispose(); throw new CliException("SERVER_ERROR", $"CalDAV server returned HTTP {status}", 5, status >= 500); }
            return response;
        }
        catch (CliException) { throw; }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new CliException("CONNECTION_FAILED", "CalDAV request timed out", 5, true, ex); }
        catch (HttpRequestException ex) { throw new CliException("CONNECTION_FAILED", "Unable to connect to the CalDAV server", 5, true, ex); }
    }

    private XDocument ParseXml(string xml)
    {
        try { return XDocument.Parse(xml); }
        catch (Exception ex) { throw new CliException("PARSE_ERROR", "CalDAV server returned malformed XML", 5, false, ex); }
    }

    private Uri ToSafeUri(string href)
    {
        var uri = Uri.TryCreate(href, UriKind.Absolute, out var absolute) ? absolute : new Uri(_config.ServerUrl, href);
        var trustedHost = uri.Host.Equals(_config.ServerUrl.Host, StringComparison.OrdinalIgnoreCase) || _config.AllowedHosts.Contains(uri.Host);
        if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !trustedHost || uri.Port != _config.ServerUrl.Port)
            throw new CliException("INVALID_ARGUMENT", $"CalDAV resource URL is outside the configured trusted HTTPS origins: {uri.Host}", 2);
        return uri;
    }

    private static string? SuccessfulPropertyHref(XDocument document, XName property)
        => document.Descendants(D + "propstat")
            .Where(PropstatSucceeded)
            .SelectMany(propstat => propstat.Descendants(property).Elements(D + "href"))
            .Select(x => x.Value.Trim())
            .FirstOrDefault(x => x.Length > 0);

    private static bool PropstatSucceeded(XElement propstat)
    {
        var status = propstat.Element(D + "status")?.Value.Trim();
        var parts = status?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is { Length: >= 2 } && int.TryParse(parts[1], out var code) && code is >= 200 and < 300;
    }

    public void Dispose() => _http.Dispose();
}
