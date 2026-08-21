---
name: netease-caldav-skill
description: Query and manage NetEase Enterprise Mail calendars over CalDAV. Use for listing calendars, finding events in a bounded date range, and creating, updating, or deleting calendar events through the bundled deterministic CLI.
metadata: {"openclaw":{"emoji":"📅","requires":{"env":["CALDAV_SERVER_URL","CALDAV_USERNAME","CALDAV_PASSWORD","CALDAV_TIMEZONE"]}}}
---

# NetEase CalDAV

Use the bundled `{baseDir}/bin/caldav-cli` executable. It returns exactly one JSON document on standard output. Never expose credentials, authorization headers, raw environment values, or raw server responses.

Read `{baseDir}/references/operations.md` when selecting commands, interpreting errors, or troubleshooting.

## Resolve the request

1. Identify whether the request is read, create, update, or delete.
2. Resolve dates to ISO-8601 values with an explicit offset. Use the configured timezone when interpreting relative dates, and state the resolved dates to the user.
3. Identify the calendar. If several calendars exist and no default is configured, list them and ask the user to choose. Never silently select the first calendar.
4. Keep event queries to 90 days or less.

## Execute read operations

Run read-only commands without confirmation after resolving the date range and calendar:

```bash
{baseDir}/bin/caldav-cli health
{baseDir}/bin/caldav-cli calendars
{baseDir}/bin/caldav-cli events --calendar <id> --from <ISO-8601> --to <ISO-8601>
```

For `events`, show every event field returned by the CLI without dropping or renaming fields: `uid`, `href`, `eTag`, `summary`, `start`, `end`, `allDay`, `timeZone`, `sourceTimeZone`, `location`, and `description`. The CLI converts timed events to the configured `CALDAV_TIMEZONE` while retaining the ICS timezone in `sourceTimeZone`; preserve the ISO-8601 offset in `start` and `end`, and show the complete `warnings` array. Do not replace the full event objects with a shortened summary. The user explicitly requesting full details is permission to include the event `href`; otherwise keep internal URLs out of the reply. On `AUTH_FAILED`, ask the operator to verify the configured secret; do not retry. Retry a read once only when `retryable` is true.

## Create events

Resolve and present the calendar, summary, start, end, and timezone. If the user's current request explicitly and unambiguously contains these values, execute it. Otherwise ask only for the missing or ambiguous value.

```bash
{baseDir}/bin/caldav-cli create --calendar <id> --summary <text> --start <ISO-8601> --end <ISO-8601> [--location <text>] [--description <text>]
```

Never retry a create after an ambiguous connection failure. Query the relevant range first to determine whether it succeeded.

## Update events

Query the event first to obtain its `href`, `etag`, and current values. Show the current and proposed values unless the user already gave an explicit update instruction identifying both the event and new value.

```bash
{baseDir}/bin/caldav-cli update --href <url> --etag <etag> [--summary <text>] [--location <text>] [--description <text>]
```

On `ETAG_CONFLICT`, refresh the event and ask the user to review the latest value. Never overwrite automatically.

## Delete events

Always show the event and obtain explicit confirmation in a separate user turn. Only then pass `--confirm`:

```bash
{baseDir}/bin/caldav-cli delete --href <url> --etag <etag> --confirm
```

Do not treat requests to inspect, clean up, or manage a calendar as deletion authorization. Never retry a delete after an ambiguous connection failure without checking whether the event still exists.

## Handle results

- Treat `ok: true` as success.
- Treat nonzero exit status or `ok: false` as failure.
- Use `error.code`, not message matching, for recovery decisions.
- Keep internal URLs out of user-facing replies unless the user requests full event details or technical details.
- Do not invent success when JSON is missing, malformed, or contradictory.
