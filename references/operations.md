# CLI 操作說明

## 環境設定

必須設定 `CALDAV_SERVER_URL`、`CALDAV_USERNAME`、`CALDAV_PASSWORD` 和 `CALDAV_TIMEZONE`。伺服器網址必須是絕對 HTTPS URL。`CALDAV_DEFAULT_CALENDAR` 為選填。請透過 OpenClaw skill 環境設定或 Docker secret／環境變數注入方式設定密碼，絕對不要將密碼放在命令列參數中。

如果伺服器返回的 CalDAV 資源 URL 使用了與 `CALDAV_SERVER_URL` 不同、但可信任的主機名稱，可以選填 `CALDAV_ALLOWED_HOSTS`，填入以逗號分隔的主機白名單，例如 `caldav.qiye.163.com,caldavhz.qiye.163.com`。只接受主機名稱，不接受 scheme、路徑或連接埠。

## 命令

| 操作 | 命令 | 是否需要確認 |
|---|---|---|
| 健康檢查 | `caldav-cli health` | 否 |
| 日曆列表 | `caldav-cli calendars` | 否 |
| 查詢日程 | `caldav-cli events --calendar ID --from TIME --to TIME` | 否 |
| 建立日程 | `caldav-cli create --calendar ID --summary TEXT --start TIME --end TIME` | 解析所有欄位 |
| 更新日程 | `caldav-cli update --href URL --etag ETAG --summary TEXT` | 未明確指定時展示變更 |
| 刪除日程 | `caldav-cli delete --href URL --etag ETAG --confirm` | 必須單獨確認 |

時間必須使用帶偏移量的 ISO-8601 格式，例如 `2026-08-19T09:00:00+08:00`。日程查詢範圍必須為正數，且最多 90 天。

## 結果格式

成功：`{"ok":true,"command":"calendars","data":[],"warnings":[]}`

失敗：`{"ok":false,"command":"events","warnings":[],"error":{"code":"AUTH_FAILED","message":"CalDAV authentication or authorization failed","retryable":false}}`

`events` 的使用者可見回覆應使用繁體中文 Markdown：先提供摘要表格，再逐一列出完整欄位。`start` 和 `end` 會依 `CALDAV_TIMEZONE` 顯示，`sourceTimeZone` 保留 ICS 原始時區。

## 錯誤處理

| 錯誤碼 | 處理方式 |
|---|---|
| `CONFIG_MISSING` | 請操作人員設定容器 |
| `INVALID_ARGUMENT` | 修正請求，不要原樣重試 |
| `AUTH_FAILED` | 請操作人員確認密碼，不要重試 |
| `CONNECTION_FAILED` | retryable 時只重試一次唯讀操作；寫入操作先確認結果 |
| `DISCOVERY_FAILED` | 確認端點和伺服器相容性 |
| `CALENDAR_NOT_FOUND` | 重新取得日曆列表 |
| `EVENT_NOT_FOUND` | 重新查詢日期範圍 |
| `ETAG_CONFLICT` | 重新取得日程，請使用者確認 |
| `SERVER_ERROR` | 只有標記為 retryable 時才重試 |
| `PARSE_ERROR` | 顯示警告並停止不安全的寫入操作 |

退出碼：`0` 表示成功；`2` 表示輸入或設定錯誤；`3` 表示驗證或授權錯誤；`4` 表示找不到資源或版本衝突；`5` 表示網路、伺服器、探索或解析錯誤。

## Docker 掛載

將已安裝的 skill 以唯讀方式掛載到目前使用的 OpenClaw workspace：

```yaml
volumes:
  - ./netease-caldav-skill:/workspace/skills/netease-caldav-skill:ro
```

請依 OpenClaw 實際設定的 workspace 路徑調整 `/workspace`。
