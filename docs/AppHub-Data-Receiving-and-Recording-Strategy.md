# Device Event History - Chiến thuật nhận và lưu event từ ERP AppHub

> Trạng thái (2026-08-28): thiết kế Sprint 2, chưa triển khai. Document shape chính thức lấy từ `Sprint-2-Db-Schema.md`.

## 1. Mục đích và phạm vi

Sprint 2 bổ sung luồng realtime chạy song song với raw-log:

```text
ERP AppHub Monitoring
    -> callback admission
    -> bounded channel
    -> parse/canonical mapping
    -> MongoDB history hoặc failure
```

Tài liệu này chốt:

- connection, callback, reconnect/rejoin;
- raw event envelope và backpressure;
- mapper, persistence, identity và reliability;
- component boundary, observability, security và tests.

Không thuộc phạm vi:

- sửa ERP/RFID/Scanner;
- thay classic SignalR bằng ASP.NET Core SignalR;
- broker/durable inbox, active-active nhiều Worker;
- projection/API hoặc direct publisher;
- cam kết lossless/exactly-once cho SignalR.

## 2. Kết luận thiết kế

1. Raw-log và AppHub là hai source adapter độc lập.
2. Hai source dùng chung canonical history/failure persistence, nhưng giữ orchestration riêng.
3. Dùng interface + composition, không tạo base class chứa cả file checkpoint và SignalR.
4. Callback chỉ đóng gói raw envelope rồi enqueue; không parse nặng hoặc ghi MongoDB.
5. Dùng bounded FIFO channel, một consumer ban đầu.
6. Mapper dispatch theo `sourceKind + eventName`.
7. AppHub không dùng `ingestion_checkpoints`.
8. Mongo retry giữ nguyên event identity của envelope.
9. Reconnect phải bảo đảm join lại group `Monitoring`.
10. Raw file vẫn là recovery source cho record đã được Antenna ghi file.

## 3. Bằng chứng ERP

Source khảo sát read-only:

```text
Host:    DevUser@192.168.1.38
Root:    D:\phu-td\CORE-ERP
Branch:  ua/erp/training/local-test
Commit:  ab98c8f8431e0d63f16d0fe1407822629cbd52d8
```

Files:

- `AppHub.cs`: Hub và OWIN `/signalr`.
- `AppHub.Rfid.MonitoringDevice.cs`: group/callback Monitoring.
- `AppHub.Connection.cs`: Scanner lifecycle.
- `AppHub.UserState.cs`: Scanner payload.
- `AppHubServer.cs`: process-local connection registry.

ERP dùng classic ASP.NET SignalR 2.4.3. Worker phải dùng `Microsoft.AspNet.SignalR.Client` 2.4.3 và xác nhận compatibility trên `net10.0`; ASP.NET Core SignalR client không phải drop-in replacement.

Contract connection đã xác nhận từ source:

```text
Endpoint = {configured ERP HubServer}/signalr
HubName  = AppHub                 // generated JavaScript proxy: appHub
Join     = JoinMonitoring()       // không có argument
Group    = Monitoring             // global, không partition theo CompanyId
```

NuGet 2.4.3 có asset `netstandard2.0`, tương thích compile với `net10.0`; kết nối thực tế vẫn phải được kiểm tra tại UAT endpoint.

## 4. Monitoring contract

Client đăng ký callback trước `Start()`, sau đó gọi `JoinMonitoring()`.

| Callback | Nguồn ERP | Payload |
|---|---|---|
| `receiveDeviceOnline` | `PushDeviceOnline` | Opaque object; broadcast hoặc targeted snapshot |
| `receiveStateConnected` | `PushStateConnected` | Opaque object |
| `receiveGreenState` | `PushGreenState` | Opaque object |
| `receiveRedState` | `PushRedState` | Opaque object |
| `receiveTimeSensor` | `PushTimeSensor` | Opaque object |
| `receiveDeviceReadTag` | `PushDeviceReadTag` | Opaque object |
| `receiveDeviceScanConnect` | ERP connection lifecycle | `UserState` |
| `receiveDeviceScanDisconnect` | ERP disconnect lifecycle | `UserState` |
| `receiveClientDeviceConnected` | `PushClientDeviceConnected` | Opaque object |
| `receiveClientDeviceDisconnected` | `PushClientDeviceDisconnected` | Opaque object |
| `receiveRequestDeviceScanInfoOnline` | Scanner snapshot request | `UserState` |

`UserState` fields đã xác nhận:

```text
ConnectionId, CompanyId, UserId, UserName, Avatar,
WindowFocus, ModuleName, Browser, Ip, SessionId,
DateConnected, UserId2, SessionType, DeviceType,
WantFollowForViewUserState,
DeviceId, DeviceName, GateId, GateName
```

Enum wire value đã xác nhận:

```text
SessionType: Account=0, Partner=1, Unknown=2
DeviceType:  Browser=0, Android=1, IOS=2, Device=3
```

Ba callback Scanner dùng một argument `UserState`: connect, disconnect và request-info response. Tám callback còn lại dùng một argument `object`; ERP chỉ chuyển tiếp object từ producer.

### Runtime evidence còn thiếu

Tám callback dùng `object`; ERP chỉ route, không validate hoặc enrich. Exact casing, nullability và variants phải lấy từ source producer hoặc UAT fixture. Không tìm thấy producer của cả tám method `Push...` trong checkout ERP, bao gồm hai callback client-device.

`receiveDeviceOnline` có thể là activity hoặc targeted snapshot nhưng payload không mang discriminator. Chỉ gắn `snapshot` khi Worker correlation được request do chính nó gửi.

`receiveRequestDeviceScanInfoOnline` là targeted response, chỉ phát khi client gọi `RequestDeviceScanInfoOnline(deviceId)`. Lookup hiện tìm `DeviceId` qua mọi company mà không filter tenant; Sprint 2 không tự động request snapshot cho tới khi có device catalog/correlation an toàn.

## 5. Kiến trúc component

```text
ErpAppHubMonitoringHostedService
    -> AppHubConnectionManager
    -> AppHubCallbackRegistrar
    -> AppHubEventAdmission
    -> Channel<RawSourceEvent>
    -> AppHubEventProcessor
    -> IRawSourceEventMapper registry
    -> CanonicalEventPersistenceService
    -> MongoDB
```

Boundary dùng chung:

```text
IEventSourceAdapter
RawSourceEvent
CanonicalDeviceEvent / IngestionFailure
History/failure writers
Mongo retry + telemetry conventions
```

Boundary riêng:

| Raw-log | AppHub |
|---|---|
| discovery/tail/framing | connect/callback/rejoin |
| file state/fair scheduler | bounded event channel |
| byte checkpoint | connection generation/sequence |
| replay từ file | best-effort realtime |

Không tạo hosted service/collection/inheritance type riêng cho từng callback.

## 6. RawSourceEvent

Contract Application-level không phụ thuộc SignalR/Newtonsoft:

```csharp
public sealed record RawSourceEvent
{
    public required string IngestionEventId { get; init; }
    public required string SourceKind { get; init; }
    public required string SourceId { get; init; }
    public required string SourceApplication { get; init; }
    public required string SourceTransport { get; init; }
    public required string EventName { get; init; }
    public required DateTimeOffset ReceivedAtUtc { get; init; }
    public DateTimeOffset? OccurredAtUtc { get; init; }
    public required string RawArgumentsJson { get; init; }
    public required string PayloadSha256 { get; init; }
    public required string ConnectionGeneration { get; init; }
    public required long ReceiveSequence { get; init; }
    public required string DeliveryKind { get; init; }
}
```

Rules:

- sanitize field đã bị privacy policy loại bỏ, sau đó serialize immutable JSON một lần ở Infrastructure boundary;
- giữ đúng thứ tự và toàn bộ arguments/fields đã được phép persist;
- không truyền `JToken`/dynamic object vào Application;
- gắn `ReceivedAtUtc` ngay khi callback đến;
- không đưa credential/query string vào envelope;
- `ReceiveSequence` chỉ có nghĩa trong một connection generation.

## 7. Configuration

```json
{
  "DeviceEventHistory": {
    "AppHub": {
      "Enabled": true,
      "Sources": [
        {
          "SourceId": "erp-apphub-ua",
          "Endpoint": "https://erp.example.com/signalr",
          "HubName": "AppHub",
          "CompanyId": null,
          "DedicatedSingleTenant": false,
          "TimeZoneId": "SE Asia Standard Time",
          "ChannelCapacity": 5000,
          "EnqueueTimeout": "00:00:00.100",
          "ReconnectMinDelay": "00:00:01",
          "ReconnectMaxDelay": "00:00:30",
          "EnabledEvents": [
            "receiveDeviceOnline",
            "receiveStateConnected",
            "receiveGreenState",
            "receiveRedState",
            "receiveTimeSensor",
            "receiveDeviceReadTag",
            "receiveDeviceScanConnect",
            "receiveDeviceScanDisconnect",
            "receiveClientDeviceConnected",
            "receiveClientDeviceDisconnected",
            "receiveRequestDeviceScanInfoOnline"
          ]
        }
      ]
    }
  }
}
```

Token/JWT lấy từ secret provider/environment. Validator phải bảo đảm source ID unique, endpoint tuyệt đối, callback allowlist/capacity/timeouts hợp lệ và không log secret.

Auth wire contract đã xác nhận:

```text
query token     -> ERP UserCookie.Decrypt(...)
nếu thiếu token:
query tokenjwt  -> đọc claim sub, sau đó load User từ database
query sessionType=0 cho Account
```

AppHub code không đọc `Authorization` header và không có token refresh flow. `JoinMonitoring()` hiện không kiểm tra `UserState`/`[Authorize]`, nhưng Worker không dựa vào anonymous access làm production policy. Service identity, cách phát hành và rotation credential vẫn phải được ERP/security team cung cấp.

## 8. Connection lifecycle

```text
Disconnected
    -> Connecting
    -> Connected
    -> JoiningMonitoring
    -> Running
    -> Reconnecting
    -> JoiningMonitoring
```

Startup invariant:

1. Tạo connection bằng configured endpoint và approved auth query.
2. Tạo proxy `AppHub`.
3. Đăng ký callback và lifecycle handlers.
4. Start.
5. Invoke `JoinMonitoring()`.
6. Chỉ ready sau join thành công.

Reconnect:

- capped exponential backoff + jitter;
- một connect attempt/source;
- tạo connection generation mới khi rebuild connection;
- bảo đảm rejoin idempotently;
- không đăng ký callback trùng;
- dispose subscription/connection cũ;
- connected nhưng chưa join không được coi ready.

Snapshot request chỉ bật khi có device catalog đáng tin cậy. Snapshot response phải được correlation và không được ghi như activity connect mới.

## 9. Callback admission, ordering và backpressure

Callback chỉ:

```text
capture eventName/time
    -> serialize arguments
    -> hash payload
    -> assign generation/sequence/event ID
    -> enqueue
    -> return
```

Không làm Mongo write, metadata lookup, business parse, projection hoặc retry trong callback.

Admission:

1. `TryWrite` fast path.
2. Khi full, chờ tối đa `EnqueueTimeout`.
3. Nếu vẫn full, record dropped/saturation metric, structured error và health degradation.
4. Không giả vờ event đã lưu.

Channel phải bounded. Không đổi sang unbounded memory để che saturation.

Một consumer giữ FIFO của events đã admission trong process. Đây không phải physical-device ordering tuyệt đối. Chỉ tăng concurrency/partition sau benchmark; partition key tương lai có thể là `CompanyId + DeviceId`.

## 10. Mapping contract

Mapper registry dispatch theo:

```text
sourceKind = erp_apphub
eventName  = exact callback name
```

| Callback | Category |
|---|---|
| `receiveDeviceOnline` | `device_online` |
| `receiveStateConnected` | `device_connection` |
| green/red | `device_control_state` |
| `receiveTimeSensor` | `device_sensor_state` |
| `receiveDeviceReadTag` | `tag_read` |
| Scanner connect/disconnect | `scanner_connection` |
| client-device connect/disconnect | `client_device_connection` |
| Scanner info response | `device_snapshot` |

Outcome:

| Condition | Result |
|---|---|
| Core facts hợp lệ | history `parsed` |
| Thiếu optional/known variant | history `parsed_with_warnings` |
| Envelope trace được nhưng contract chưa khóa | history `unknown/unmapped` |
| JSON/tenant/required identity không hợp lệ | `ingestion_failures` |
| Infrastructure transient failure | retry/health, không phải data failure |

Tenant resolution:

```text
payload CompanyId > 0
    -> dùng payload

payload thiếu CompanyId + configured CompanyId > 0
    -> chỉ dùng config khi source được khai báo DedicatedSingleTenant=true

payload và configured CompanyId cùng có nhưng khác nhau
    -> TENANT_MISMATCH

không resolve được tenant
    -> TENANT_UNRESOLVED
```

Group `Monitoring` là global. Không được dùng configured tenant làm fallback cho endpoint multi-tenant.

Không suy đoán business facts. Unknown optional field không được làm mất raw evidence.

## 11. Persistence và identity

Common persistence boundary:

```text
CanonicalEventPersistenceService
    -> history hoặc failure confirmed

RawRecordPersistenceCoordinator
    -> common persistence
    -> file checkpoint

AppHubEventProcessor
    -> common persistence
    -> complete envelope, không checkpoint
```

AppHub identity khi producer chưa có event ID:

```text
SHA-256(
  SourceId + ConnectionGeneration + ReceiveSequence
  + EventName + PayloadSha256
)
```

Identity này chống duplicate Mongo retry của cùng admitted envelope, không deduplicate reconnect/restart. Nếu producer có `sourceEventId` ổn định, ưu tiên `SourceId + sourceEventId`.

Không dedupe bằng tag/device/payload hash và không dedupe AppHub với raw-log khi chưa có correlation contract.

## 12. Reliability và shutdown

| Boundary | Guarantee |
|---|---|
| Raw file | Replayable; checkpoint + idempotent persistence |
| ERP AppHub | Best-effort; không replay mặc định |
| In-memory channel | Bounded; mất khi process crash |
| Mongo retry | Idempotent cho event đã admission |

Khoảng có thể mất event:

- chưa connect/join;
- network/ERP disconnect;
- admission timeout;
- process crash khi event còn trong channel;
- shutdown drain timeout.

Shutdown:

```text
stop connect/reconnect
    -> stop AppHub receive
    -> complete channel writer
    -> drain trong ShutdownTimeout
    -> log remaining count
    -> cancel
```

Nếu yêu cầu lossless trở thành bắt buộc, cần durable inbox/outbox hoặc broker ở thiết kế riêng.

## 13. Failure, health và security

Failure rules:

- connect/join failure: retry backoff, degraded/unhealthy theo threshold;
- unknown variant: giữ raw + warning;
- malformed/tenant unresolved: ingestion failure;
- oversized payload: failure metadata/hash, không silently truncate;
- Mongo unavailable: retry + bounded backpressure;
- programming/config exception: error + unhealthy, không nuốt như success.

Metrics tối thiểu:

```text
connection state/attempt/reconnect/join
callbacks received/admitted/dropped by eventName
channel depth/saturation
mapping status
Mongo result/retry/latency
last callback age
```

Không dùng event/device/connection ID làm metric label.

Không log token, full query string hoặc full raw payload.

Policy cho `UserState` đã khóa:

| Xử lý | Fields |
|---|---|
| Persist | CompanyId, UserId, DateConnected, SessionType, DeviceType, DeviceId/Name, GateId/Name |
| Hash | ConnectionId -> `facts.scanner.connectionIdHash` |
| Drop trước persistence | UserName, Avatar, WindowFocus, ModuleName, Browser, Ip, SessionId, UserId2, WantFollowForViewUserState, raw ConnectionId |

Hash dùng SHA-256 trên UTF-8 `ConnectionId`; không lưu raw value. Privacy classification của tám opaque payload vẫn phải khóa sau khi có fixture.
Tại redaction boundary, `ConnectionId` được thay bằng `ConnectionIdHash` trong stored arguments để mapper dùng mà không giữ raw identifier. Hash/size của `rawPayload` được tính trên representation sau redaction.

## 14. Tests và acceptance

Tests bắt buộc:

- options/redaction/callback allowlist;
- callback registered trước start;
- connect/join/reconnect/rejoin;
- envelope, arguments order, generation/sequence/identity;
- each confirmed callback fixture và payload variants;
- Scanner activity khác snapshot;
- unknown/malformed/oversized/tenant-unresolved;
- bounded channel saturation/FIFO/shutdown;
- Mongo retry/duplicate/failure;
- raw-log vẫn tiến khi AppHub lỗi;
- UAT capture đủ 11 callback: casing, types, timestamps, payload size và reconnect behavior.

Acceptance:

- callback không parse nặng/ghi Mongo trực tiếp;
- channel bounded và saturation không silent;
- mapper dispatch đúng source/event;
- `sourceKind`, `category`, `source.eventName` lưu đúng schema;
- AppHub không dùng file checkpoint;
- Mongo retry giữ event ID;
- AppHub failure không làm raw-log dừng;
- secret/full payload không xuất hiện trong log;
- không tuyên bố lossless/exactly-once.

## 15. Các quyết định còn mở

- service identity, credential issuance và rotation;
- exact payload của tám opaque callbacks;
- producer client-device callbacks;
- endpoint deployment là dedicated single-tenant hay multi-tenant;
- privacy/redaction fields của tám opaque payload;
- snapshot có được request tự động hay không;
- capacity, timeout, payload limit và alert threshold;
- yêu cầu best-effort hay durable ingestion trong tương lai.

## 16. Tóm tắt

```text
Raw-log adapter -----------------+
                                 +-> canonical history/failure -> MongoDB
ERP AppHub adapter -> channel ---+
```

Thiết kế generic ở canonical contract/persistence, nhưng transport-specific ở file checkpoint và SignalR connection. Một bounded channel, một consumer, mapper registry và component rõ trách nhiệm là đủ; broker, partitioning và projection chỉ thêm khi requirement/benchmark chứng minh cần.

## 17. Tài liệu liên quan

- `Sprint-2-Db-Schema.md`.
- `Device-Event-History-Current-Codebase.md`.
- `Device-Event-History-Architecture.md`.
- `Device-Event-History-Design.md`.
- `Logs-Reading-Strategy.md`.
- `Coding-Standards.md`.

Kết luận ERP là static source inspection tại commit đã nêu. Exact wire contract vẫn phải được khóa bằng runtime capture/UAT.
