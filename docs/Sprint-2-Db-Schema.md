# Device Event History - MongoDB Schema V2 / Final Contract

> Trạng thái (2026-08-28): target schema sau Sprint 2, mở rộng additive từ V1. Runtime hiện tại mới implement raw-log V1. Strategy nhận AppHub được mô tả trong `AppHub-Data-Receiving-and-Recording-Strategy.md`.

## 1. Mục đích và nguyên tắc

Schema V2 là contract chung cho raw-log, ERP AppHub và source adapter tương lai.

Nguyên tắc:

1. `device_event_history` là append-only source of truth.
2. Một source observation tương ứng một history document.
3. Mọi event giữ raw payload và canonical facts đã xác nhận.
4. `sourceKind`, `category`, `source.eventName` là ba discriminator khác nhau.
5. Field không có evidence được omit/null, không điền giả.
6. Normal history phải resolve được `companyId`.
7. Parse/data-contract error vào `ingestion_failures`.
8. Checkpoint chỉ dành cho replayable source.
9. Projection rebuild được từ history.
10. V2 không đổi `eventId` hoặc document V1 đã ghi.

## 2. Collections

| Collection | Vai trò | Source of truth |
|---|---|---|
| `device_event_history` | Multi-source event history | Có |
| `ingestion_failures` | Source event không canonicalize bình thường | Có cho failure evidence |
| `ingestion_checkpoints` | Durable cursor của replayable source | Có cho ingestion position |
| `device_current_state` | Latest device/gate state | Không |
| `tag_current_state` | Latest tag/process state | Không |
| `device_connection_sessions` | Derived connection sessions | Không |
| `production_daily_stats` | Derived daily aggregate | Không |

Không tạo collection riêng theo callback/category.

## 3. Registries

### 3.1. sourceKind

| Value | Source |
|---|---|
| `rfid_antenna_file` | Raw-log Sprint 1 |
| `erp_apphub` | ERP Monitoring callback |
| `rfid_antenna_direct` | Reserved direct publisher |
| `rfid_analytics_direct` | Reserved Analytics publisher |
| `scanner_direct` | Reserved Scanner publisher |
| `application_log` | Reserved application-log adapter |

`source.transport` mô tả technology:

```text
file, http_range, classic_signalr, http, message_broker, application_log
```

### 3.2. category

```text
tag_read
business_process
gate_state
device_online
device_connection
scanner_connection
client_device_connection
device_control_state
device_sensor_state
device_snapshot
device_error
application_error
unknown
```

Worker ingestion error không phải history category; nó thuộc `ingestion_failures`.

### 3.3. source.eventName

Raw file dùng `raw_record`. AppHub giữ exact callback:

```text
receiveDeviceOnline
receiveStateConnected
receiveGreenState
receiveRedState
receiveTimeSensor
receiveDeviceReadTag
receiveDeviceScanConnect
receiveDeviceScanDisconnect
receiveClientDeviceConnected
receiveClientDeviceDisconnected
receiveRequestDeviceScanInfoOnline
```

## 4. device_event_history V2

### 4.1. Canonical envelope

```json
{
  "_id": "ObjectId(...)",
  "eventId": "sha256(...)",
  "schemaVersion": 2,
  "category": "scanner_connection",
  "sourceKind": "erp_apphub",
  "companyId": 2,

  "occurredAtUtc": "2026-08-28T08:30:00.123Z",
  "occurredAtLocal": "2026-08-28T15:30:00.123+07:00",
  "receivedAtUtc": "2026-08-28T08:30:00.123Z",
  "persistedAtUtc": "2026-08-28T08:30:00.150Z",
  "timelineAtUtc": "2026-08-28T08:30:00.123Z",
  "timeBasis": "received",

  "source": {
    "producer": "ERP.AppHub",
    "sourceId": "erp-apphub-ua",
    "transport": "classic_signalr",
    "eventName": "receiveDeviceScanConnect",
    "sourceEventId": null,
    "deliveryKind": "realtime",
    "connectionGeneration": "01J6...",
    "receiveSequence": 42,
    "fileId": null,
    "fileName": null,
    "relativePath": null,
    "folderDate": null,
    "offsetStart": null,
    "offsetEnd": null
  },

  "device": {
    "id": 101,
    "gateId": 5,
    "type": "scanner",
    "code": null,
    "name": "Scanner A",
    "gateCode": null,
    "gateName": "Gate 5"
  },

  "rawPayload": {
    "format": "signalr-arguments-json-v1",
    "text": null,
    "arguments": [
      {
        "DeviceId": 101,
        "GateId": 5
      }
    ],
    "sha256": "...",
    "sizeBytes": 420
  },

  "facts": {
    "connection": {
      "status": "connected",
      "connectedAtLocal": "2026-08-28T15:29:58+07:00"
    },
    "scanner": {
      "sessionType": 1,
      "deviceType": 2
    }
  },

  "parse": {
    "status": "parsed",
    "parserVersion": "erp-apphub-v1",
    "warnings": [],
    "errors": []
  },

  "ingestion": {
    "workerId": "device-event-history-worker-01",
    "attempt": 1,
    "processingDurationMs": 8
  }
}
```

### 4.2. Required fields

```text
eventId, schemaVersion, category, sourceKind, companyId
receivedAtUtc, persistedAtUtc, timelineAtUtc, timeBasis
source.producer, source.sourceId, source.transport
source.eventName, source.deliveryKind
rawPayload.format, rawPayload.sha256, rawPayload.sizeBytes
facts, parse.status, parse.parserVersion, ingestion.workerId
```

`device`, occurred times và facts branches có thể vắng nếu category cho phép. Tenant không resolve được phải đi failure `TENANT_UNRESOLVED`.

### 4.3. Sparse rule

V2 luôn có `facts` object nhưng chỉ ghi branch có dữ liệu; event chưa map sâu dùng `facts: {}`. V1 có thể chứa branch `null`; mixed-version reader phải hỗ trợ cả hai.

## 5. Time contract

| Field | Meaning |
|---|---|
| `occurredAtUtc` | Producer/source event time nếu đáng tin cậy; với AppHub không có source timestamp thì là thời điểm Worker nhận callback |
| `receivedAtUtc` | Worker receive time |
| `persistedAtUtc` | Mongo persistence time |
| `timelineAtUtc` | Effective indexed timeline time |

```text
timelineAtUtc = occurredAtUtc ?? receivedAtUtc
timeBasis     = occurred | received
```

Raw-log không ghi received time giả làm occurred time. Với AppHub Monitoring, ERP không gửi source timestamp nên Worker dùng `ReceivedAtUtc` làm observed event time để bảo đảm document có đủ hai field `occurredAt*`; `timeBasis` vẫn là `received` để phân biệt rõ đây không phải timestamp do ERP phát sinh. `occurredAtLocal` được chuyển theo `TimeZoneId` của AppHub source; UTC fields dùng BSON Date.

Timestamp/timezone không chắc chắn:

- Raw-log: `occurredAtUtc = null`, timeline dùng received time, giữ raw timestamp và thêm parse warning.
- AppHub: `occurredAtUtc = receivedAtUtc`, `occurredAtLocal` là cùng mốc được chuyển timezone, `timeBasis = received`.

## 6. Source, device và raw payload

### 6.1. Common source

```json
{
  "producer": "RFID.Antenna",
  "sourceId": "antenna-site-a",
  "transport": "file",
  "eventName": "raw_record",
  "sourceEventId": null,
  "deliveryKind": "activity"
}
```

`deliveryKind`:

```text
activity, realtime, snapshot, snapshot_candidate,
reconnect_snapshot, heartbeat, unknown
```

### 6.2. Source-specific context

Raw file:

```text
fileId, fileName, relativePath, folderDate, offsetStart, offsetEnd
```

AppHub:

```text
connectionGeneration, receiveSequence
```

File context giữ tên field V1. AppHub sequence chỉ có nghĩa trong một generation và không phải global device ordering.

### 6.3. Device

```text
id, gateId, type, code, name, gateCode, gateName
```

Không suy `device.id` từ `FileId`. ID/gate lấy từ payload hoặc authoritative metadata resolver. Display fields có thể null.

### 6.4. Raw payload

Raw file:

```json
{
  "format": "rfid-raw-v1",
  "text": "@(...)t(...)e(0)",
  "arguments": null,
  "sha256": "...",
  "sizeBytes": 180
}
```

AppHub:

```json
{
  "format": "signalr-arguments-json-v1",
  "text": null,
  "arguments": [
    {
      "DeviceId": 101
    }
  ],
  "sha256": "...",
  "sizeBytes": 80
}
```

AppHub arguments giữ đầy đủ và đúng thứ tự. Không silently truncate payload quá giới hạn. Token/JWT/connection string không được lưu.

"Giữ đầy đủ" nghĩa là giữ đầy đủ representation đã qua approved redaction tại source boundary. Với typed `UserState`, Worker phải drop/hash field nhạy cảm trước khi tạo immutable `RawSourceEvent`; MongoDB không nhận raw `ConnectionId`, IP, session hoặc avatar. Opaque callback chỉ được production-enable sau khi payload fields đã được phân loại.

### 6.5. Tenant resolution

```text
payload CompanyId > 0
    -> authoritative tenant

payload thiếu CompanyId + source DedicatedSingleTenant=true
    -> dùng configured CompanyId

payload/config mismatch
    -> TENANT_MISMATCH failure

không có tenant hợp lệ
    -> TENANT_UNRESOLVED failure
```

ERP group `Monitoring` là global. `CompanyId` cấu hình không được fallback trên source multi-tenant.

## 7. Facts branches

| Branch | Fields chính | Source |
|---|---|---|
| `tagRead` | tagId, epcRaw, routingFileId, readTimeText | Raw/AppHub/direct |
| `gateState` | stateCode, rawValue | Raw |
| `signal` | antennaPort, timestamps, count, power, phase, channel, RSSI | Raw |
| `businessEvent` | eventType, processId, quantity, processIds, second | Raw/Analytics |
| `styleProcess` | processCustomRaw, processCustom | Raw |
| `user` | userId, userName | Raw/AppHub |
| `connection` | status, reason, source booleans, connectedAtLocal | AppHub/direct |
| `deviceOnline` | online, active, snapshot, sourceState | AppHub/direct |
| `deviceControlState` | control, state, rawState | AppHub/direct |
| `sensorState` | sensor, state, timeout, timeoutUnit | AppHub/direct |
| `scanner` | sessionType, deviceType, connectionIdHash | AppHub |
| `deviceError` | code, message, severity, retryable | Source error event |

Canonical values:

```text
connection.status: connecting | connected | disconnected | unknown
deviceControlState.control: green_light | red_light | unknown
```

Rules:

- không tự tạo EPC nếu source chỉ có TagId;
- không dedupe hai tag read giống nhau;
- source numeric code chưa hiểu vẫn giữ raw;
- sensor timeout unit chỉ map khi đã xác nhận;
- state callback không tự coi là command acknowledgement;
- user/session/connection fields tuân thủ privacy policy.

Scanner `UserState` wire enums:

```text
SessionType: Account=0, Partner=1, Unknown=2
DeviceType:  Browser=0, Android=1, IOS=2, Device=3
```

Scanner canonical mapping giữ `CompanyId`, `UserId`, `DateConnected`, enum values, device/gate identity/display fields và SHA-256 `connectionIdHash`. `DateConnected` được ERP tạo bằng server-local `DateTime.Now`; không map thành trusted UTC khi chưa có timezone evidence.

## 8. Parse và ingestion

```json
{
  "parse": {
    "status": "parsed_with_warnings",
    "parserVersion": "erp-apphub-v1",
    "warnings": [
      {
        "code": "SOURCE_TIME_MISSING",
        "message": "Timeline uses receivedAtUtc"
      }
    ],
    "errors": []
  },
  "ingestion": {
    "workerId": "device-event-history-worker-01",
    "attempt": 1,
    "processingDurationMs": 8
  }
}
```

Parse status:

```text
parsed
parsed_with_warnings
unmapped
```

Payload không đủ tạo normal history đi `ingestion_failures`, không ghi history status `failed`.

`schemaVersion` mô tả document shape; `parserVersion` mô tả mapper/parser logic. `workerId` không tham gia event identity.

## 9. Category mapping AppHub

| Callback | Category | Facts |
|---|---|---|
| `receiveDeviceOnline` | `device_online` | `deviceOnline` |
| `receiveStateConnected` | `device_connection` | `connection` |
| green/red | `device_control_state` | `deviceControlState` |
| `receiveTimeSensor` | `device_sensor_state` | `sensorState` |
| `receiveDeviceReadTag` | `tag_read` | `tagRead` |
| Scanner connect/disconnect | `scanner_connection` | `connection`, `scanner`, `user` |
| client-device connect/disconnect | `client_device_connection` | partial/unmapped |
| Scanner info response | `device_snapshot` | `connection`, `scanner` |

Opaque contract chưa xác nhận được lưu raw + warning/unmapped; không suy đoán fields.

## 10. ingestion_failures V2

Failure lưu source event hoàn chỉnh nhưng không tạo được normal history:

```json
{
  "_id": "ObjectId(...)",
  "failureId": "sha256(...)",
  "schemaVersion": 2,
  "sourceKind": "erp_apphub",
  "companyId": null,
  "source": {
    "producer": "ERP.AppHub",
    "sourceId": "erp-apphub-ua",
    "transport": "classic_signalr",
    "eventName": "receiveDeviceReadTag",
    "connectionGeneration": "01J6...",
    "receiveSequence": 125
  },
  "rawPayload": {
    "format": "signalr-arguments-json-v1",
    "arguments": [],
    "sha256": "...",
    "sizeBytes": 2
  },
  "error": {
    "code": "TENANT_UNRESOLVED",
    "message": "CompanyId cannot be resolved",
    "stage": "metadata_resolution",
    "parserVersion": "erp-apphub-v1",
    "details": []
  },
  "receivedAtUtc": "2026-08-28T09:15:00Z",
  "persistedAtUtc": "2026-08-28T09:15:00Z",
  "retryable": false,
  "retryCount": 0,
  "resolvedAtUtc": null,
  "resolution": null,
  "ingestion": {
    "workerId": "device-event-history-worker-01"
  }
}
```

Failure stages:

```text
admission, framing, deserialization, validation,
metadata_resolution, mapping, persistence_contract
```

Mongo/network outage khi chưa có source data outcome thuộc health/retry, không phải data failure.

Resolution chỉ update `resolvedAtUtc` và resolution metadata; không xóa raw failure evidence.

## 11. ingestion_checkpoints

Chỉ replayable source dùng checkpoint. Raw file giữ contract:

```json
{
  "_id": "source|date|fileId|relativePath",
  "schemaVersion": 2,
  "sourceKind": "rfid_antenna_file",
  "sourceId": "antenna-site-a",
  "folderDate": "2026-08-28",
  "fileId": 12,
  "relativePath": "2026/08/28/File_12.txt",
  "position": 10480,
  "lastEventId": "...",
  "lastRecordHash": "...",
  "observedFileLength": 10720,
  "workerId": "device-event-history-worker-01",
  "updatedAtUtc": "2026-08-28T09:20:00Z",
  "version": 7
}
```

Rules:

- key: `SourceId + FolderDate + FileId + RelativePath`;
- byte position là `long`;
- CAS theo `version`;
- history/failure confirmed trước advance;
- không reset khi truncate;
- không TTL.

AppHub không tạo checkpoint giả.

## 12. Event identity

Raw file:

```text
SHA-256(SourceId + RelativePath + OffsetStart + OffsetEnd + PayloadSha256)
```

AppHub chưa có producer ID:

```text
SHA-256(SourceId + ConnectionGeneration + ReceiveSequence + EventName + PayloadSha256)
```

Producer có stable `sourceEventId`:

```text
SHA-256(SourceId + SourceEventId)
```

Không unique/dedupe bằng `DeviceId + TagId`, EPC, payload hash hoặc category/time. Không recompute V1 event IDs. AppHub identity chỉ bảo đảm idempotent retry trong cùng admitted envelope, không dedupe reconnect generation.

## 13. Rebuildable projections

### device_current_state

```json
{
  "_id": "company-2-device-101-gate-5",
  "companyId": 2,
  "deviceId": 101,
  "gateId": 5,
  "connectionStatus": "connected",
  "online": true,
  "greenLightOn": false,
  "redLightOn": false,
  "lastReadAtUtc": "2026-08-28T09:20:00Z",
  "lastTagId": "TAG001",
  "lastEventId": "...",
  "lastTimelineAtUtc": "2026-08-28T09:20:00Z",
  "updatedAtUtc": "2026-08-28T09:20:01Z",
  "projectionVersion": 1
}
```

### Other projection keys

```text
tag_current_state:
  companyId + tagId

device_connection_sessions:
  companyId + deviceId + session identity

production_daily_stats:
  companyId + business day + deviceId + gateId
```

Snapshot cũ không được ghi đè activity mới. Tag read không đồng nghĩa process complete. Business day dùng source/business timezone.

## 14. Indexes

### History

```text
unique eventId
companyId + timelineAtUtc DESC
companyId + category + timelineAtUtc DESC
sourceKind + receivedAtUtc DESC
source.sourceId + receivedAtUtc DESC
source.eventName + receivedAtUtc DESC
device.id + timelineAtUtc DESC
device.gateId + timelineAtUtc DESC
facts.tagRead.tagId + timelineAtUtc DESC
parse.status + receivedAtUtc DESC
```

File trace:

```text
source.sourceId + source.folderDate + source.fileId + source.offsetStart
```

### Failures/checkpoints

```text
failures:
  unique failureId
  sourceKind/sourceId/eventName/error.code/error.stage + receivedAtUtc
  resolvedAtUtc

checkpoints:
  unique sourceId + folderDate + fileId + relativePath
  updatedAtUtc DESC
```

Facts/file/AppHub-specific indexes nên dùng partial filters. Không tạo mọi index nếu query/volume chưa chứng minh cần.

## 15. Validation, retention và privacy

Mongo validator chặt ở envelope, linh hoạt ở `facts` và opaque `rawPayload.arguments`.

Required BSON types:

```text
eventId string; schemaVersion int; category/sourceKind string
companyId positive integer
receivedAtUtc/persistedAtUtc/timelineAtUtc date
source/rawPayload/facts/parse/ingestion object
```

Raw payload phải có representation phù hợp format và luôn có hash/size.

Retention:

- history không TTL mặc định;
- failure có thể expire sau resolution theo policy;
- checkpoint không TTL;
- archive dùng explicit `expireAtUtc` nếu được duyệt.

Không persist token/JWT hoặc full auth query.

Typed `UserState` policy:

```text
persist:
  CompanyId, UserId, DateConnected, SessionType, DeviceType,
  DeviceId, DeviceName, GateId, GateName

hash:
  ConnectionId -> SHA-256 UTF-8 -> facts.scanner.connectionIdHash

drop trước persistence:
  UserName, Avatar, WindowFocus, ModuleName, Browser, Ip,
  SessionId, UserId2, WantFollowForViewUserState, raw ConnectionId
```

`rawPayload.arguments` của typed `UserState` là representation sau redaction: raw `ConnectionId` được thay bằng `ConnectionIdHash`, các field thuộc drop-list bị omit. `rawPayload.sha256` và `sizeBytes` được tính trên stored representation này. Raw payload access vẫn cần role phù hợp. Opaque payload privacy fields còn phụ thuộc fixture contract.

## 16. V1/V2 compatibility

```text
schemaVersion 1: raw-log runtime hiện tại
schemaVersion 2: unified multi-source contract
```

Rules:

- V1/V2 cùng tồn tại trong collection;
- không đổi V1 `_id`/`eventId`;
- giữ V1 file fields;
- AppHub chỉ ghi V2;
- V2 raw-log có thể dùng envelope mới;
- API/reader hiểu cả V1 null branches và V2 omitted branches;
- V1 timeline fallback `occurredAtUtc ?? receivedAtUtc`;
- warning V1 string và V2 object được đọc theo version.

Backfill V1 nếu cần chỉ bổ sung derived fields:

```text
timelineAtUtc, timeBasis, source.transport,
source.eventName, rawPayload.sizeBytes
```

Không reparse facts hoặc đổi identity trong migration schema.

## 17. Query và Worker rules

History filters:

```text
companyId, from/to timelineAtUtc
deviceId, gateId, tagId
category, sourceKind, sourceId, eventName
deliveryKind, parse.status
```

Sort:

```text
timelineAtUtc DESC, _id DESC
```

API phải expose occurred/received/timeline/timeBasis; list endpoint không trả full raw payload mặc định.

Persistence:

```text
source adapter
    -> raw envelope
    -> tenant/source validation
    -> source mapper
    -> history hoặc failure confirmed
    -> source checkpoint nếu replayable
```

- raw-log checkpoint sau persistence;
- AppHub không checkpoint;
- mapper/callback không ghi Mongo trực tiếp;
- projection sau history confirmed;
- data failure khác infrastructure failure.

## 18. Tests và Definition of Done

Tests:

- V1 regression và V2 mapping;
- time/timeline/timeBasis;
- sparse facts và mixed V1/V2;
- raw arguments/hash/size;
- tenant resolution/mismatch;
- each AppHub category + unknown/malformed;
- identity/duplicate semantics;
- failure/checkpoint/index initializer;
- validator and tenant-isolated queries;
- projection keys nếu projection bật.

Schema đạt yêu cầu khi:

- raw-log và AppHub cùng dùng một envelope không có field bắt buộc giả;
- source/category/eventName rõ;
- tenant và time contract rõ;
- raw evidence được giữ;
- facts không trùng semantics;
- failure/checkpoint boundary đúng;
- identity riêng theo source;
- indexes phục vụ timeline/source/device/tag;
- V1 compatibility không đổi ID;
- projection rebuild được;
- secret/privacy được kiểm soát;
- mapper/index/validation/tests khớp tài liệu.

## 19. Evidence còn cần xác nhận

- exact payload/casing của tám opaque AppHub callbacks;
- producer client-device callbacks;
- sensor timeout unit và DeviceOnline semantics;
- authoritative company/device/gate mapping;
- source timestamps/timezones;
- privacy của opaque payload, volume, payload size, retention và query patterns;
- service credential issuance/rotation và tenant scope của deployment endpoint;
- projection precedence giữa activity/snapshot;
- correlation raw-log/AppHub/direct.

Khi chưa đủ evidence: giữ raw, map tối thiểu, warning/unmapped, không tạo giá trị giả.

## 20. Tóm tắt

```text
device_event_history
    = immutable multi-source history
    = sourceKind + category + source.eventName
    = raw evidence + canonical facts

ingestion_failures
    = source evidence không canonicalize bình thường

ingestion_checkpoints
    = cursor của replayable source, hiện tại là raw file

projections
    = derived current state/session/stats
```

V2 mở rộng additive từ V1: giữ file identity/offset, bổ sung multi-source context, effective timeline, AppHub receive identity và device-event facts. Source adapter mới phải map vào contract này thay vì tạo document shape riêng.

## 21. Tài liệu liên quan

- `2026-08-22-Db-Schema.md`.
- `AppHub-Data-Receiving-and-Recording-Strategy.md`.
- `Device-Event-History-Current-Codebase.md`.
- `Device-Event-History-Architecture.md`.
- `Logs-Reading-Strategy.md`.
- `Coding-Standards.md`.
