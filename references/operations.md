# CLI operations

## Environment

Require `CALDAV_SERVER_URL`, `CALDAV_USERNAME`, `CALDAV_PASSWORD`, and `CALDAV_TIMEZONE`. The server must be an absolute HTTPS URL. `CALDAV_DEFAULT_CALENDAR` is optional. Configure secrets through OpenClaw skill environment settings or Docker secret/environment injection, never command arguments.

If the server returns CalDAV resource URLs on a trusted hostname different from `CALDAV_SERVER_URL`, optionally set `CALDAV_ALLOWED_HOSTS` to a comma-separated host allowlist, for example `caldav.qiye.163.com,caldavhz.qiye.163.com`. Host names only are accepted; schemes, paths, and ports are rejected.

## Commands

| Operation | Command | Confirmation |
|---|---|---|
| Health | `caldav-cli health` | No |
| Calendars | `caldav-cli calendars` | No |
| Events | `caldav-cli events --calendar ID --from TIME --to TIME` | No |
| Create | `caldav-cli create --calendar ID --summary TEXT --start TIME --end TIME` | Resolve all fields |
| Update | `caldav-cli update --href URL --etag ETAG --summary TEXT` | Show change unless explicit |
| Delete | `caldav-cli delete --href URL --etag ETAG --confirm` | Always confirm separately |

Times must use ISO-8601 with an offset, such as `2026-08-19T09:00:00+08:00`. Event ranges must be positive and at most 90 days.

## Result schema

Success: `{"ok":true,"command":"calendars","data":[],"warnings":[]}`

Failure: `{"ok":false,"command":"events","warnings":[],"error":{"code":"AUTH_FAILED","message":"CalDAV authentication or authorization failed","retryable":false}}`

## Error handling

| Code | Action |
|---|---|
| `CONFIG_MISSING` | Ask the operator to configure the container |
| `INVALID_ARGUMENT` | Correct the request; do not retry unchanged |
| `AUTH_FAILED` | Ask the operator to verify secrets; do not retry |
| `CONNECTION_FAILED` | Retry one read when retryable; verify writes first |
| `DISCOVERY_FAILED` | Verify endpoint and server compatibility |
| `CALENDAR_NOT_FOUND` | Refresh calendars |
| `EVENT_NOT_FOUND` | Refresh the date range |
| `ETAG_CONFLICT` | Refresh and ask the user to review |
| `SERVER_ERROR` | Retry only when marked retryable |
| `PARSE_ERROR` | Surface warnings and stop unsafe writes |

Exit codes are `0` success, `2` input/configuration, `3` authentication/authorization, `4` missing/conflict, and `5` network/server/discovery/parsing.

## Docker mount

Mount the installed skill read-only under the active OpenClaw workspace:

```yaml
volumes:
  - ./netease-caldav-skill:/workspace/skills/netease-caldav-skill:ro
```

Adapt `/workspace` to the configured OpenClaw workspace path.
