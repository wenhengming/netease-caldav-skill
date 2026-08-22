---
name: netease-caldav-skill
description: 透過 CalDAV 查詢與管理网易企業郵箱日曆，支援列出日曆、在指定日期範圍查詢日程，以及透過內建 CLI 建立、更新或刪除日程。
metadata: {"openclaw":{"emoji":"📅","requires":{"env":["CALDAV_SERVER_URL","CALDAV_USERNAME","CALDAV_PASSWORD","CALDAV_TIMEZONE"]}}}
---

# 网易企業郵箱 CalDAV

使用內建的 `{baseDir}/bin/caldav-cli` 執行檔。它會在標準輸出中返回一份 JSON 文件。絕對不要公開憑證、授權標頭、原始環境變數或原始伺服器回應。

選擇命令、解讀錯誤或進行疑難排解時，請閱讀 `{baseDir}/references/operations.md`。

## 解析使用者需求

1. 判斷需求屬於查詢、建立、更新或刪除。
2. 將日期解析為帶有明確偏移量的 ISO-8601 值。解讀相對日期時使用已設定的時區，並向使用者說明解析後的日期範圍。
3. 確定要使用的日曆。如果存在多個日曆且未設定預設日曆，列出日曆並請使用者選擇，不得默默選取第一個日曆。
4. 日程查詢範圍不得超過 90 天。

## 執行查詢操作

解析日期範圍和日曆後，可直接執行唯讀命令，無需額外確認：

```bash
{baseDir}/bin/caldav-cli health
{baseDir}/bin/caldav-cli calendars
{baseDir}/bin/caldav-cli events --calendar <id> --from <ISO-8601> --to <ISO-8601>
```

對 `events` 而言，預設的使用者回覆必須是易讀的 Markdown，而不是原始 JSON。不得刪除或重新命名 CLI 返回的任何日程欄位，包括：`uid`、`href`、`eTag`、`summary`、`start`、`end`、`allDay`、`timeZone`、`sourceTimeZone`、`location` 和 `description`。CLI 會將有時間的日程轉換為已設定的 `CALDAV_TIMEZONE`，並在 `sourceTimeZone` 中保留 ICS 原始時區；保留 `start` 和 `end` 中的 ISO-8601 偏移量，並完整顯示 `warnings` 陣列。先顯示包含 `#`、`summary`、`start`、`end`、`timeZone` 和 `location` 的精簡 Markdown 摘要表格，再為每個日程顯示包含全部欄位的完整詳情區塊，絕不能只顯示摘要表格。除非使用者明確要求 JSON，否則不要輸出原始 JSON。除非使用者明確要求，否則不要自行判斷日程異常，也不要主動建議刪除日程。使用者明確要求完整詳情時，可以包含日程 `href`；否則不要在回覆中顯示內部 URL。遇到 `AUTH_FAILED` 時，請使用者確認設定的密碼，不要重試。只有 `retryable` 為 true 時，唯讀操作才可重試一次。

完整回覆結構範例（使用實際 CLI 值；以下內容為虛構資料）：

```json
{
  "ok": true,
  "command": "events",
  "data": [
    {
      "uid": "example-event-1",
      "href": "https://calendar.example/events/example-event-1.ics",
      "eTag": "\"v3\"",
      "summary": "團隊會議",
      "start": "2026-08-22T10:00:00-07:00",
      "end": "2026-08-22T11:00:00-07:00",
      "allDay": false,
      "timeZone": "America/Los_Angeles",
      "sourceTimeZone": "America/Denver",
      "location": "會議室 A",
      "description": "專案檢視"
    }
  ],
  "warnings": []
}
```

使用者可見的 Markdown 回覆範例（使用實際 CLI 值；以下內容為虛構資料）：

### 日程（1 個）

| # | 標題 | 開始時間 | 結束時間 | 顯示時區 | 地點 |
|---|---|---|---|---|---|
| 1 | 團隊會議 | 2026-08-22T10:00:00-07:00 | 2026-08-22T11:00:00-07:00 | America/Los_Angeles | 會議室 A |

#### 1. 團隊會議

- **UID：** `example-event-1`
- **URL：** `https://calendar.example/events/example-event-1.ics`
- **eTag：** `"v3"`
- **開始時間：** `2026-08-22T10:00:00-07:00`
- **結束時間：** `2026-08-22T11:00:00-07:00`
- **顯示時區：** `America/Los_Angeles`
- **原始時區：** `America/Denver`
- **全天日程：** `false`
- **地點：** 會議室 A
- **描述：** 專案檢視

警告：無。

## 建立日程

解析並向使用者展示日曆、標題、開始時間、結束時間和時區。如果使用者當前需求已明確包含這些值，直接執行；否則只詢問缺少或不明確的值。

```bash
{baseDir}/bin/caldav-cli create --calendar <id> --summary <text> --start <ISO-8601> --end <ISO-8601> [--location <text>] [--description <text>]
```

連線結果不明確時，建立操作不得重試。先查詢相關日期範圍，確認建立操作是否已成功。

## 更新日程

先查詢日程，取得其 `href`、`etag` 和目前值。除非使用者已明確指定日程和新值，否則應同時展示目前值與預計更新值。

```bash
{baseDir}/bin/caldav-cli update --href <url> --etag <etag> [--summary <text>] [--location <text>] [--description <text>]
```

遇到 `ETAG_CONFLICT` 時，重新取得日程並請使用者確認最新值，不得自動覆蓋。

## 刪除日程

始終先展示日程，並在單獨的後續使用者回覆中取得明確確認，之後才可傳入 `--confirm`：

```bash
{baseDir}/bin/caldav-cli delete --href <url> --etag <etag> --confirm
```

不要將查看、整理或管理日曆的要求視為刪除授權。連線結果不明確時，必須先確認日程是否仍存在，不能直接重試刪除。

## 處理結果

- 將 `ok: true` 視為成功。
- 將非零退出狀態或 `ok: false` 視為失敗。
- 使用 `error.code`，不要依賴錯誤訊息文字匹配來決定恢復方式。
- 除非使用者要求完整日程詳情或技術細節，否則不要在回覆中顯示內部 URL。
- 當 JSON 缺失、格式錯誤或內容矛盾時，不得捏造成功結果。
