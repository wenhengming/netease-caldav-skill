using System.Net;
using System.Text;
using NetEaseCalDav;

var tests = new (string Name, Func<Task> Run)[]
{
    ("ICS round-trip fields", TestIcs),
    ("NetEase ICS extensions", TestNetEaseIcsExtensions),
    ("TZID timezone conversion", TestDawsonTimeZone),
    ("NetEase fallback discovery", TestFallbackDiscovery),
    ("Delete requires confirmation", TestDeleteConfirmation),
    ("Cross-origin href rejected", TestCrossOrigin),
    ("ETag conflict classified", TestEtagConflict),
};
var failures = new List<string>();
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures.Add($"FAIL {test.Name}: {ex.Message}"); }
}
foreach (var failure in failures) Console.Error.WriteLine(failure);
return failures.Count == 0 ? 0 : 1;

static Task TestIcs()
{
    var start = new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.FromHours(8));
    var ics = IcsCodec.CreateEvent("uid-1", "Planning, review", start, start.AddHours(1), "Room A", "Line 1\nLine 2");
    var warnings = new List<string>();
    var events = IcsCodec.ParseEvents(ics, "https://calendar.example/event.ics", "\"v1\"", warnings);
    Assert(events.Count == 1, "expected one event");
    Assert(events[0].Summary == "Planning, review", "summary did not round-trip");
    Assert(events[0].Description == "Line 1\nLine 2", "description did not round-trip");
    Assert(warnings.Count == 0, "unexpected parse warning");
    return Task.CompletedTask;
}

static Task TestNetEaseIcsExtensions()
{
    const string ics = "BEGIN:VCALENDAR\r\nPRODID:-//Netease Corporation//EN\r\nVERSION:2.0\r\nBEGIN:VEVENT\r\nUID:netease-1\r\nDTSTART;TZID=America/Los_Angeles:20260819T090000\r\nDTEND;TZID=America/Los_Angeles:20260819T100000\r\nSUMMARY:NetEase meeting\r\nX-HMC-ACTION:UPDATE\r\nBEGIN:VALARM\r\nACTION:DISPLAY\r\nDESCRIPTION:Reminder\r\nTRIGGER:-PT15M\r\nEND:VALARM\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    var warnings = new List<string>();
    var events = IcsCodec.ParseEvents(ics, "https://calendar.example/netease.ics", "\"v1\"", warnings);
    Assert(events.Count == 1, "NetEase extension event was not parsed");
    Assert(events[0].Summary == "NetEase meeting", "NetEase event summary did not parse");
    Assert(events[0].Start == "2026-08-19T09:00:00-07:00", "TZID was not preserved with its offset");
    Assert(events[0].End == "2026-08-19T10:00:00-07:00", "TZID end was not preserved with its offset");
    Assert(warnings.Count == 0, "NetEase extension produced an unexpected warning");
    return Task.CompletedTask;
}

static Task TestDawsonTimeZone()
{
    const string ics = "BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:dawson-1\r\nDTSTART;TZID=America/Dawson:20260822T203000\r\nDTEND;TZID=America/Dawson:20260822T210000\r\nSUMMARY:Timezone test\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";
    var warnings = new List<string>();
    var events = IcsCodec.ParseEvents(ics, "https://calendar.example/dawson.ics", "\"v1\"", warnings);
    Assert(events.Count == 1, "Dawson event was not parsed");
    Assert(events[0].Start == "2026-08-22T20:30:00-07:00", "Dawson start was not preserved with its offset");
    Assert(events[0].End == "2026-08-22T21:00:00-07:00", "Dawson end was not preserved with its offset");
    return Task.CompletedTask;
}

static async Task TestFallbackDiscovery()
{
    var handler = new QueueHandler([
        Xml("<d:multistatus xmlns:d=\"DAV:\"><d:response><d:href>/principals/u/</d:href></d:response></d:multistatus>"),
        Xml("<d:multistatus xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\"><d:response><d:propstat><d:prop><c:calendar-home-set><d:href>/calendars/u/</d:href></c:calendar-home-set></d:prop></d:propstat></d:response></d:multistatus>"),
        Xml("<d:multistatus xmlns:d=\"DAV:\" xmlns:c=\"urn:ietf:params:xml:ns:caldav\"><d:response><d:href>/calendars/u/work/</d:href><d:propstat><d:prop><d:displayname>Work</d:displayname><d:resourcetype><d:collection/><c:calendar/></d:resourcetype><c:calendar-description>Internal</c:calendar-description></d:prop></d:propstat></d:response></d:multistatus>")
    ]);
    using var client = new CalDavClient(Config(), handler);
    var calendars = await client.DiscoverCalendarsAsync(default);
    Assert(calendars.Count == 1 && calendars[0].Id == "work", "fallback calendar was not parsed");
}

static async Task TestDeleteConfirmation()
{
    using var client = new CalDavClient(Config(), new QueueHandler([]));
    var ex = await Throws(() => client.DeleteEventAsync("https://calendar.example/event.ics", "\"v1\"", false, default));
    Assert(ex.Code == "INVALID_ARGUMENT", "delete was not blocked");
}

static async Task TestCrossOrigin()
{
    using var client = new CalDavClient(Config(), new QueueHandler([]));
    var ex = await Throws(() => client.DeleteEventAsync("https://evil.example/event.ics", "\"v1\"", true, default));
    Assert(ex.Code == "INVALID_ARGUMENT", "cross-origin URL was not rejected");
}

static async Task TestEtagConflict()
{
    using var client = new CalDavClient(Config(), new QueueHandler([new HttpResponseMessage(HttpStatusCode.PreconditionFailed)]));
    var ex = await Throws(() => client.DeleteEventAsync("https://calendar.example/event.ics", "\"old\"", true, default));
    Assert(ex.Code == "ETAG_CONFLICT" && ex.ExitCode == 4, "ETag conflict was not classified");
}

static CalDavConfig Config() => new(new Uri("https://calendar.example/"), "user@example.com", "secret", "Asia/Shanghai", null);
static HttpResponseMessage Xml(string xml) => new(HttpStatusCode.MultiStatus) { Content = new StringContent(xml, Encoding.UTF8, "application/xml") };
static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static async Task<CliException> Throws(Func<Task<object>> action)
{
    try { await action(); } catch (CliException ex) { return ex; }
    throw new Exception("expected CliException");
}

sealed class QueueHandler(IEnumerable<HttpResponseMessage> responses) : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new(responses);
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_responses.Count > 0 ? _responses.Dequeue() : throw new Exception("unexpected HTTP request"));
}
