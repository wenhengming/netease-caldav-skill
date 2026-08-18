using System.Text.Json;
using System.Text.Json.Serialization;
using NetEaseCalDav;

var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "help";
var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = false };
try
{
    if (command == "help") throw new CliException("INVALID_ARGUMENT", "Commands: health, calendars, events, create, update, delete", 2);
    var options = ParseOptions(args.Skip(1).ToArray());
    var config = CalDavConfig.FromEnvironment();
    using var client = new CalDavClient(config);
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    object data;
    IReadOnlyList<string> warnings = [];
    switch (command)
    {
        case "health": data = await client.HealthAsync(cts.Token); break;
        case "calendars": data = await client.DiscoverCalendarsAsync(cts.Token); break;
        case "events":
            var eventResult = await client.GetEventsAsync(Calendar(options, config), Date(options, "from"), Date(options, "to"), cts.Token);
            data = eventResult.Events; warnings = eventResult.Warnings; break;
        case "create":
            data = await client.CreateEventAsync(Calendar(options, config), Required(options, "summary"), Date(options, "start"), Date(options, "end"), Optional(options, "location"), Optional(options, "description"), cts.Token); break;
        case "update":
            var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in new[] { "summary", "location", "description" }) if (options.TryGetValue(key, out var value)) changes[key.ToUpperInvariant()] = value;
            data = await client.UpdateEventAsync(Required(options, "href"), Required(options, "etag"), changes, cts.Token); break;
        case "delete":
            data = await client.DeleteEventAsync(Required(options, "href"), Required(options, "etag"), options.ContainsKey("confirm"), cts.Token); break;
        default: throw new CliException("INVALID_ARGUMENT", $"Unknown command: {command}", 2);
    }
    Console.WriteLine(JsonSerializer.Serialize(CliResult.Success(command, data, warnings), jsonOptions));
    return 0;
}
catch (CliException ex)
{
    Console.WriteLine(JsonSerializer.Serialize(CliResult.Failure(command, ex), jsonOptions));
    return ex.ExitCode;
}
catch
{
    var ex = new CliException("SERVER_ERROR", "Unexpected internal error", 5);
    Console.WriteLine(JsonSerializer.Serialize(CliResult.Failure(command, ex), jsonOptions));
    return 5;
}

static Dictionary<string, string> ParseOptions(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < values.Length; i++)
    {
        if (!values[i].StartsWith("--", StringComparison.Ordinal)) throw new CliException("INVALID_ARGUMENT", $"Unexpected argument: {values[i]}", 2);
        var key = values[i][2..];
        if (key == "confirm") { result[key] = "true"; continue; }
        if (++i >= values.Length || values[i].StartsWith("--", StringComparison.Ordinal)) throw new CliException("INVALID_ARGUMENT", $"Missing value for --{key}", 2);
        result[key] = values[i];
    }
    return result;
}
static string Required(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new CliException("INVALID_ARGUMENT", $"--{key} is required", 2);
static string? Optional(Dictionary<string, string> values, string key) => values.TryGetValue(key, out var value) ? value : null;
static string Calendar(Dictionary<string, string> values, CalDavConfig config) => Optional(values, "calendar") ?? config.DefaultCalendar ?? throw new CliException("INVALID_ARGUMENT", "--calendar is required when CALDAV_DEFAULT_CALENDAR is not configured", 2);
static DateTimeOffset Date(Dictionary<string, string> values, string key) => DateTimeOffset.TryParse(Required(values, key), out var value) ? value : throw new CliException("INVALID_ARGUMENT", $"--{key} must be ISO-8601 with an offset", 2);
