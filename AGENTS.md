# Codex 專案工作規範

本專案是网易企業郵箱 CalDAV skill。開始處理本專案的需求前，先閱讀 `SKILL.md`；涉及 CLI 命令、錯誤處理或疑難排解時，再閱讀 `references/operations.md`。

## 一般要求

- 使用繁體中文回覆使用者，除非使用者另有要求。
- 優先使用內建的 `bin/caldav-cli`，不要自行重寫 CalDAV 請求或繞過既有 CLI 行為。
- 不要公開憑證、授權標頭、原始環境變數、密碼或原始伺服器回應。
- 不要把密碼放進命令列參數、原始碼、提交內容或文件；使用環境變數／secret 注入。
- 修改程式後，依變更範圍執行相關測試；不要捏造測試或 CLI 成功結果。

## 日曆與時間

- 需求分為查詢、建立、更新、刪除；先判斷操作類型。
- 日期必須解析為帶明確偏移量的 ISO-8601 值，並使用 `CALDAV_TIMEZONE` 解讀相對日期；回覆中說明解析後的範圍。
- 查詢範圍必須為正數且不得超過 90 天。
- 有多個日曆且沒有 `CALDAV_DEFAULT_CALENDAR` 時，列出日曆並請使用者選擇，不得默默選第一個。

## 操作安全

- `health`、`calendars`、`events` 是唯讀操作，可直接執行；只有 `retryable: true` 時，唯讀操作才可重試一次。
- 建立日程前確認日曆、標題、開始時間、結束時間和時區；資訊已明確時才直接執行。
- 更新日程前先取得目前的 `href`、`etag` 和目前值；若 `ETAG_CONFLICT`，重新取得日程並請使用者確認，不得自動覆蓋。
- 刪除日程必須先展示日程，並在後續獨立回覆取得明確確認，才可使用 `--confirm`。
- 寫入操作遇到連線結果不明確時，不得直接重試；先查詢確認結果。
- `AUTH_FAILED` 不要重試，請使用者確認密碼設定。
- 使用錯誤碼判斷處理方式，不要依賴錯誤訊息文字比對。

## 回覆格式

- `events` 預設回覆使用繁體中文 Markdown，不要輸出原始 JSON，除非使用者明確要求 JSON。
- 先提供包含編號、標題、開始、結束、顯示時區和地點的摘要表格，再逐一提供每個日程的完整欄位。
- 不要刪除或重新命名 CLI 回傳的 `uid`、`href`、`eTag`、`summary`、`start`、`end`、`allDay`、`timeZone`、`sourceTimeZone`、`location`、`description` 和 `warnings`。
- 保留 `start`、`end` 的 ISO-8601 偏移量；有時間的日程使用 `CALDAV_TIMEZONE` 顯示，並保留 `sourceTimeZone`。
- 除非使用者要求完整詳情或技術細節，否則不要顯示內部 URL、授權資訊或其他敏感內部資料。
- 除非使用者明確要求，不要自行判斷日程異常，也不要主動建議刪除日程。

## 參考命令

```bash
bin/caldav-cli health
bin/caldav-cli calendars
bin/caldav-cli events --calendar <id> --from <ISO-8601> --to <ISO-8601>
bin/caldav-cli create --calendar <id> --summary <text> --start <ISO-8601> --end <ISO-8601>
bin/caldav-cli update --href <url> --etag <etag> [--summary <text>]
bin/caldav-cli delete --href <url> --etag <etag> --confirm
```
