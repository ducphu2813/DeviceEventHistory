# Device Event History - Implementation Plan Sprint 2

> Trạng thái (2026-08-28): kế hoạch triển khai. Thiết kế nguồn AppHub lấy từ `AppHub-Data-Receiving-and-Recording-Strategy.md`; document contract lấy từ `Sprint-2-Db-Schema.md`.

## 1. Mục tiêu

Sprint 2 bổ sung luồng nhận event realtime từ group `Monitoring` của ERP AppHub và ghi vào cùng MongoDB history/failure đang được raw-log sử dụng:

```text
Raw-log adapter -----------------------------+
                                              +-> canonical history/failure -> MongoDB
ERP AppHub -> callback -> bounded channel ----+
```

Kết quả cuối Sprint:

- Worker kết nối classic SignalR, join group `Monitoring` và nhận đủ 11 callback đã xác định;
- callback được đưa qua bounded channel, canonical mapping và Mongo persistence;
- history AppHub tuân theo schema V2 với `sourceKind`, `category`, `source.eventName`, tenant và time contract rõ ràng;
- raw-log V1 tiếp tục hoạt động, giữ nguyên event identity và checkpoint semantics;
- reconnect/rejoin, saturation, failure và shutdown đều quan sát được;
- cấu hình có thể bật/tắt AppHub độc lập mà không ảnh hưởng raw-log.

Không thuộc Sprint 2:

- sửa source ERP/RFID/Scanner hoặc đổi protocol của ERP;
- broker/durable inbox, active-active nhiều Worker hay cam kết exactly-once/lossless cho SignalR;
- API/UI và các projection current-state/session/statistics;
- tự động request snapshot khi chưa có device catalog và correlation contract;
- deduplicate event AppHub với raw-log khi chưa có cross-source event ID.

## 2. Nguyên tắc triển khai

1. Giữ dependency direction `Domain <- Application <- Infrastructure <- Worker`.
2. Dùng interface + composition; không tạo base class chứa cả file checkpoint và SignalR lifecycle.
3. Chỉ dùng chung canonical model, failure model, mapper dispatch, persistence và telemetry conventions.
4. Raw-log giữ discovery/tail/framing/checkpoint; AppHub giữ connect/callback/channel/reconnect.
5. Callback chỉ capture, serialize, hash và enqueue; không parse business hoặc ghi MongoDB trực tiếp.
6. Một enabled AppHub source có một bounded FIFO channel và một consumer trong Sprint 2.
7. AppHub không tạo `ingestion_checkpoints`.
8. Không đưa `JToken`, `dynamic`, SignalR hoặc MongoDB type vào Domain/Application contract.
9. Không tạo class/collection riêng cho từng callback nếu nhiều callback dùng chung một mapping responsibility.
10. AppHub mặc định disabled cho tới khi config, auth và UAT contract được xác nhận.

Không bắt raw-log implement một `IEventSourceAdapter` mới chỉ để hai flow trông giống nhau. Chỉ trích abstraction khi có hành vi thực sự dùng chung.

## 3. Contract status trước triển khai

Các contract đã khóa từ ERP source:

| ID | Trạng thái đã khóa |
|---|---|
| C1 | SignalR Client 2.4.3 có `netstandard2.0` asset; compile-compatible với `net10.0`. Runtime connect test thuộc UAT. |
| C2 | Endpoint `{HubServer}/signalr`, hub `AppHub`, group `Monitoring`, method `JoinMonitoring()` không argument. |
| C3a | Auth dùng query `token`, fallback `tokenjwt`; `sessionType=0` là Account. AppHub không đọc Authorization header. |
| C6 | Tenant resolution/mismatch theo rule bên dưới. |
| C7a | Typed `UserState` đã có persist/hash/drop policy trong Strategy/Schema. |

Contract tenant:

```text
payload CompanyId hợp lệ                         -> dùng payload
payload thiếu + DedicatedSingleTenant=true       -> dùng configured CompanyId
cả hai có nhưng khác nhau                        -> TENANT_MISMATCH failure
cả hai đều không có                              -> TENANT_UNRESOLVED failure
```

Các gate còn lại trước production UAT:

| ID | Contract còn mở | Kết quả cần có |
|---|---|---|
| C1-UAT | Kết nối SignalR 2.4.3 tới endpoint thật | Connect/join/reconnect test pass |
| C3b | Service identity, credential issuance/rotation | Secret lifecycle, không hard-code |
| C4 | Exact JSON của tám callback opaque | Fixture redacted, casing/null/type variants |
| C5 | Producer của client-device callbacks | Runtime evidence hoặc xác nhận chưa phát sinh |
| C7b | Privacy/redaction của opaque payload | Approved field classification |
| C8 | Capacity, timeout, max payload và alert threshold | Giá trị UAT có bằng chứng tải |
| C9 | Endpoint là dedicated single-tenant hay multi-tenant | Deployment declaration |

Các gate này không ngăn triển khai Schema V2, common persistence, configuration, transport wrapper, channel và mapper cho typed `UserState`.

## 4. Thứ tự và dependency

```text
P0 Contract/evidence gate
        |
        +------> P3 Configuration + SignalR transport
        |
        v
P1 Canonical Schema V2
        |
        v
P2 Common persistence boundary
        |
        +------> P4 Admission + source runtime
        |                 |
        |                 v
        +-------------> P5 AppHub mappers
                          |
                          v
                    P6 Mongo V2 + indexes
                          |
                          v
                    P7 Reliability + observability
                          |
                          v
                    P8 Integration/UAT + rollout
```

P1 và phần wrapper SignalR của P3 có thể làm song song. P5 chỉ map field đã có evidence; callback chưa khóa contract vẫn đi `unmapped` hoặc failure theo rule, không được suy đoán.

## 5. Cấu trúc source mục tiêu

Ký hiệu: `[N]` file mới, `[M]` file hiện tại cần sửa. Cây dưới đây là target responsibility; tên file có thể tinh chỉnh trong review nhưng không được làm sai layer boundary.

```text
src/
  DeviceEventHistory.Domain/
    Common/
      AppConst.cs                                      [M]
    Events/
      CanonicalDeviceEvent.cs                          [M]
      CanonicalEventFacts.cs                           [N]
    Failures/
      CanonicalIngestionFailure.cs                     [N]

  DeviceEventHistory.Application/
    Ingestion/
      RawSourceEvent.cs                                [N]
      CanonicalIngestionResult.cs                      [N]
      IRawSourceEventMapper.cs                         [N]
      RawSourceEventMapperRegistry.cs                  [N]
      RawSourceEventIdentityFactory.cs                 [N]
    AppHub/Mapping/
      AppHubMappingContext.cs                          [N]
      AppHubTenantResolver.cs                          [N]
      DeviceOnlineEventMapper.cs                       [N]
      DeviceConnectionEventMapper.cs                   [N]
      DeviceControlStateEventMapper.cs                 [N]
      DeviceSensorStateEventMapper.cs                  [N]
      DeviceReadTagEventMapper.cs                      [N]
      ScannerEventMapper.cs                            [N]
      ClientDeviceConnectionEventMapper.cs             [N]
    Persistence/
      ICanonicalIngestionPersistenceService.cs         [N]
      CanonicalIngestionPersistenceService.cs          [N]
      IDeviceEventHistoryWriter.cs                     [M]
      IIngestionFailureWriter.cs                       [M]
      RawRecordPersistenceCoordinator.cs               [M]
    Observability/
      IIngestionTelemetry.cs                           [M]

  DeviceEventHistory.Infrastructure/
    AppHub/
      Configuration/
        AppHubOptions.cs                               [N]
        AppHubSourceOptions.cs                         [N]
      Transport/
        IAppHubMonitoringConnection.cs                 [N]
        IAppHubMonitoringConnectionFactory.cs          [N]
        AppHubMonitoringConnection.cs                  [N]
        AppHubMonitoringConnectionFactory.cs           [N]
        AppHubCallbackRegistrar.cs                     [N]
        AppHubEnvelopeFactory.cs                       [N]
        AppHubConnectionState.cs                       [N]
    MongoDb/
      Mapping/
        CanonicalDeviceEventDocumentMapper.cs          [M]
        IngestionFailureDocumentMapper.cs              [M]
      Indexes/
        MongoIndexInitializer.cs                       [M]
      Stores/
        MongoDeviceEventHistoryWriter.cs               [M]
        MongoIngestionFailureWriter.cs                 [M]
    Observability/
      IngestionHealthState.cs                          [M]
      IngestionMetrics.cs                              [M]

  DeviceEventHistory.Worker/
    Configuration/
      OptionsValidators.cs                             [M]
      ConfigurationRedactor.cs                         [M]
      ServiceCollectionExtensions.cs                   [M]
    HostedServices/
      StartupInitializationHostedService.cs            [M]
      ErpAppHubMonitoringHostedService.cs              [N]
    Orchestration/AppHub/
      AppHubSourceRuntime.cs                           [N]
      AppHubEventAdmission.cs                          [N]
      AppHubEventProcessor.cs                          [N]
      AppHubShutdownCoordinator.cs                     [N]
    HealthChecks/
      AppHubHealthCheck.cs                             [N]
    Program.cs                                         [M]
    appsettings.Example.json                           [M]

tests/
  DeviceEventHistory.UnitTests/
    AppHubConfigurationTests.cs                        [N]
    AppHubEnvelopeFactoryTests.cs                      [N]
    AppHubAdmissionTests.cs                            [N]
    AppHubConnectionLifecycleTests.cs                  [N]
    AppHubMappingTests.cs                              [N]
    CanonicalV2MappingTests.cs                         [N]
    AppHubShutdownTests.cs                             [N]
  DeviceEventHistory.IntegrationTests/
    AppHubMongoPersistenceIntegrationTests.cs          [N]
    MultiSourceWorkerIntegrationTests.cs               [N]
    MongoV2IndexIntegrationTests.cs                    [N]
  fixtures/apphub-monitoring/                          [N]
    *.json

docs/contracts/
  erp-apphub-monitoring-v1.md                          [N]
```

Không cần tạo project mới. Classic SignalR/Newtonsoft package chỉ được reference trong Infrastructure; MongoDB Driver tiếp tục chỉ nằm trong Infrastructure.

## 6. Phase 0 - Contract và fixture gate

### Task P0.1 - Khóa wire evidence

- capture đã redaction cho tám callback opaque; ba callback Scanner dùng typed `UserState` đã khóa từ source;
- ghi exact argument count/order, JSON property casing/type/nullability của opaque payload;
- ghi nhận timestamp/timezone, payload size và hành vi reconnect;
- phân biệt activity, snapshot response và snapshot candidate;
- lưu fixture theo callback và variant, không chứa token/session/IP chưa duyệt.

### Task P0.2 - Khóa runtime/auth operation

- provision service identity và xác nhận credential issuance/rotation;
- inject query `token` hoặc `tokenjwt` từ secret provider cùng `sessionType=0`;
- chạy connect/join/reconnect UAT với SignalR Client 2.4.3;
- xác nhận endpoint deployment là dedicated single-tenant hay multi-tenant;
- ghi rõ cách secret được inject, refresh và redaction.

### Deliverable và acceptance

- `docs/contracts/erp-apphub-monitoring-v1.md` chứa evidence commit/runtime environment;
- mỗi opaque callback có ít nhất một fixture; known variant có fixture riêng;
- C1-UAT, C3b, C4, C5, C7b, C8 và C9 có owner/status;
- không có credential hoặc dữ liệu cá nhân chưa redaction trong Git.

## 7. Phase 1 - Canonical model Schema V2

### Task P1.1 - Mở rộng canonical event

Cập nhật `CanonicalDeviceEvent` theo V2:

- thêm `receivedAtUtc`, `persistedAtUtc`, `timelineAtUtc`, `timeBasis`;
- source context chung có `producer`, `sourceId`, `transport`, `eventName`, `deliveryKind`, optional `sourceEventId`;
- file context và AppHub generation/sequence là optional, có invariant theo `sourceKind`;
- device context thêm type/code/name/gate display fields;
- raw payload hỗ trợ file text hoặc AppHub arguments JSON, luôn có hash/size;
- facts thêm connection, deviceOnline, deviceControlState, sensorState, scanner và deviceError;
- V2 facts dùng sparse branches; không serialize branch null.

### Task P1.2 - Generalize failure

- chuyển failure khỏi nested type chỉ hiểu `RawRecordContext`;
- failure giữ source context, raw payload, stage, parser version, retryable và tenant nullable;
- giữ adapter chuyển đổi từ raw parser result để raw-log không phải biết AppHub;
- data failure khác infrastructure exception/retry.

### Task P1.3 - V1 compatibility

- không recompute eventId/failureId đã có;
- raw-log mapping tiếp tục tạo đúng V1 trong bước đầu, hoặc chuyển V2 bằng thay đổi additive đã có regression test;
- writer/reader test hỗ trợ V1 null facts và V2 omitted facts;
- `timelineAtUtc = occurredAtUtc ?? receivedAtUtc`, không giả received time thành occurred time.

### Acceptance

- Domain/Application không reference BSON, SignalR hoặc Newtonsoft;
- canonical model biểu diễn được cả raw file và AppHub mà không điền fake file field;
- raw-log unit/integration tests hiện tại vẫn pass;
- tests khóa time contract, sparse facts và required-field invariants.

## 8. Phase 2 - Common mapping/persistence boundary

### Task P2.1 - Source-neutral ingestion result

Tạo `RawSourceEvent` đúng Strategy và `CanonicalIngestionResult` có đúng một outcome:

```text
RawSourceEvent -> mapper registry -> CanonicalDeviceEvent | CanonicalIngestionFailure
```

Registry dispatch bằng exact key `sourceKind + eventName`. Duplicate mapper key phải fail startup/test; unknown key đi mapper fallback có chủ đích, không throw ngẫu nhiên.

### Task P2.2 - Common persistence service

`CanonicalIngestionPersistenceService` chịu trách nhiệm duy nhất:

```text
event   -> history writer -> confirmed
failure -> failure writer -> confirmed
```

- Mongo retry dùng cùng identity của input;
- duplicate deterministic ID là idempotent success;
- service không biết file checkpoint hoặc SignalR connection;
- nhận/ghi processing timing và worker identity theo contract.

### Task P2.3 - Giữ checkpoint wrapper của raw-log

`RawRecordPersistenceCoordinator` gọi common persistence trước, sau đó mới CAS checkpoint. AppHub processor chỉ gọi common persistence và kết thúc envelope, tuyệt đối không gọi checkpoint store.

### Acceptance

- test chứng minh persist trước checkpoint vẫn giữ nguyên;
- AppHub event/failure persist được mà không cần checkpoint model;
- Mongo retry không tạo identity mới;
- không có callback mapper gọi Mongo writer trực tiếp.

## 9. Phase 3 - Configuration, DI và classic SignalR transport

### Task P3.1 - Options và validation

Thêm `AppHubOptions`/`AppHubSourceOptions`, bind tại `DeviceEventHistory:AppHub` và `ValidateOnStart()` khi Worker/AppHub enabled.

Validator bắt buộc:

- `SourceId` non-empty và unique trên toàn bộ raw/AppHub sources;
- endpoint HTTPS/HTTP absolute, không user-info/query token/fragment;
- hub name và enabled-event allowlist hợp lệ;
- `CompanyId` null hoặc positive;
- `DedicatedSingleTenant=true` bắt buộc có positive `CompanyId`;
- capacity, enqueue timeout, reconnect delays và payload limit positive;
- min reconnect delay không lớn hơn max;
- chỉ cho phép 11 callback đã đăng ký;
- source enabled phải có approved service credential theo C3b.

`ConfigurationRedactor` chỉ log endpoint host, source ID, enabled event count và trạng thái credential configured; không log token/full URL query.

### Task P3.2 - Transport wrapper

- thêm `Microsoft.AspNet.SignalR.Client` 2.4.3 vào Infrastructure;
- chỉ thêm explicit `Newtonsoft.Json` reference nếu Infrastructure trực tiếp dùng public types của package;
- bọc `HubConnection`/`IHubProxy` sau `IAppHubMonitoringConnection` để unit test không cần ERP thật;
- đăng ký toàn bộ callback và lifecycle handler trước `Start()`;
- gọi `JoinMonitoring()` sau connect;
- expose trạng thái/connection generation, không expose SignalR types ra ngoài Infrastructure.

### Task P3.3 - DI/composition root

Tách registration theo trách nhiệm để `ServiceCollectionExtensions` không tiếp tục phình thành một method lớn:

```text
AddDeviceEventHistoryConfiguration
AddCanonicalIngestion
AddRfidRawLogIngestion
AddErpAppHubIngestion
AddDeviceEventHistoryMongoDb
AddDeviceEventHistoryObservability
```

Không đổi behavior raw-log khi `AppHub:Enabled=false`.

### Acceptance

- invalid config fail fast với stable message từ `AppConst.Messages`;
- AppHub disabled không connect và không busy-loop;
- fake transport chứng minh callback registered before start và join after connect;
- secret không xuất hiện trong startup log/test snapshot.

## 10. Phase 4 - Callback admission và source runtime

### Task P4.1 - Envelope factory

Tại callback boundary:

1. lấy `eventName` và `ReceivedAtUtc` từ `TimeProvider`;
2. serialize toàn bộ arguments thành immutable ordered JSON đúng một lần;
3. tính UTF-8 size và SHA-256;
4. gắn `SourceId`, producer, transport, generation và monotonic receive sequence;
5. tạo deterministic ingestion identity;
6. không giữ `JToken`/dynamic sau boundary.

Identity khi chưa có producer event ID:

```text
SHA-256(SourceId + ConnectionGeneration + ReceiveSequence + EventName + PayloadSha256)
```

### Task P4.2 - Bounded channel

- dùng `Channel<RawSourceEvent>` với capacity theo source và FIFO một reader;
- `TryWrite` fast path, sau đó chờ tối đa `EnqueueTimeout`;
- timeout phải tăng dropped/saturation metric, structured error và health degradation;
- không đổi sang unbounded channel và không log full payload;
- payload quá giới hạn đi data failure có hash/size theo schema, không silently truncate thành normal history.

### Task P4.3 - Source runtime

Mỗi enabled source có một `AppHubSourceRuntime` sở hữu:

- một connection attempt tại một thời điểm;
- connection generation và sequence;
- channel writer/reader;
- một processor consumer;
- reconnect/rejoin lifecycle;
- shutdown/drain state.

`ErpAppHubMonitoringHostedService` chỉ tạo/điều phối runtime; không chứa mapping logic.

### Acceptance

- tests khóa ordered arguments, hash, generation/sequence và stable identity;
- FIFO giữ đúng thứ tự admission với một consumer;
- saturation không mất âm thầm;
- nhiều configured sources không dùng nhầm generation/channel/tenant của nhau.

## 11. Phase 5 - AppHub canonical mappers

### Task P5.1 - Tenant và common context

`AppHubMappingContext` parse JSON bằng API framework-independent, resolve tenant theo C6 và dựng common source/time/raw/parse context. Configured `CompanyId` chỉ fallback khi `DedicatedSingleTenant=true`. Mỗi mapper chỉ map facts của category mình chịu trách nhiệm.

### Task P5.2 - Mapper groups

| Mapper | Callback | Category/facts |
|---|---|---|
| `DeviceOnlineEventMapper` | device online | `device_online` / `deviceOnline` |
| `DeviceConnectionEventMapper` | state connected | `device_connection` / `connection` |
| `DeviceControlStateEventMapper` | green, red | `device_control_state` / `deviceControlState` |
| `DeviceSensorStateEventMapper` | time sensor | `device_sensor_state` / `sensorState` |
| `DeviceReadTagEventMapper` | device read tag | `tag_read` / `tagRead` |
| `ScannerEventMapper` | scan connect/disconnect/info | scanner activity hoặc snapshot |
| `ClientDeviceConnectionEventMapper` | client-device connect/disconnect | `client_device_connection` |

Rules chung:

- chỉ map field đã xác nhận trong fixture/ERP evidence;
- thiếu optional field -> `parsed_with_warnings`;
- trace được nhưng opaque contract chưa khóa -> `unmapped`, `facts: {}` và giữ raw;
- malformed JSON, missing required identity hoặc tenant failure -> `ingestion_failures`;
- source timestamp không chắc -> occurred null, timeline dùng received + warning;
- green/red là observed control state, không tự coi command acknowledgement;
- `receiveDeviceOnline` chỉ là snapshot khi có correlation do Worker tạo; còn lại là realtime/snapshot candidate;
- scanner info response không được ghi như một connect activity mới.

### Task P5.3 - Privacy

- typed `UserState` giữ CompanyId/UserId/DateConnected, enum và device/gate fields;
- SHA-256 `ConnectionId` thành `facts.scanner.connectionIdHash`, không lưu raw value;
- drop UserName, Avatar, WindowFocus, ModuleName, Browser, IP, SessionId, UserId2 và WantFollowForViewUserState trước persistence;
- opaque payload chỉ production-enable sau khi C7b khóa redaction;
- raw arguments access/logging phải theo policy, không đưa full payload vào operational log.

### Acceptance

- golden test cho từng callback và known variant;
- category, source kind, exact event name, delivery kind và facts đúng Schema V2;
- tenant mismatch/unresolved tạo đúng failure code;
- không mapper nào suy diễn EPC/device/gate/time unit không có evidence.

## 12. Phase 6 - MongoDB Schema V2 và indexes

### Task P6.1 - Document mappers

Mở rộng hai mapper Mongo theo `Sprint-2-Db-Schema.md`:

- write required V2 envelope/time/source/raw/parse/ingestion fields;
- AppHub `rawPayload.arguments` là BSON array theo đúng order;
- sparse facts: omit branch không có dữ liệu;
- source-specific fields chỉ ghi khi có;
- `persistedAtUtc` lấy tại persistence boundary;
- normal history phải có positive `companyId`.

### Task P6.2 - Index initializer

Giữ unique `eventId`/`failureId` và file indexes hiện tại; bổ sung có chọn lọc:

```text
companyId + timelineAtUtc DESC
companyId + category + timelineAtUtc DESC
sourceKind + receivedAtUtc DESC
source.sourceId + receivedAtUtc DESC
source.eventName + receivedAtUtc DESC
device.id/device.gateId/tagId + timelineAtUtc DESC
failure sourceKind/sourceId/eventName/error.code/error.stage + receivedAtUtc
```

Dùng partial filter cho field source/facts sparse. Không tạo mọi index nếu query/volume chưa chứng minh cần; migration index phải idempotent và không drop index production tự động.

### Task P6.3 - Validation/compatibility

- Mongo validation chặt ở V2 envelope, linh hoạt ở facts/arguments;
- V1/V2 cùng tồn tại, không rewrite V1 identity;
- không TTL history/checkpoint mặc định;
- không tạo collection riêng theo callback;
- chưa triển khai projection collections trong Sprint này.

### Acceptance

- integration test xác nhận exact BSON types và sparse shape;
- duplicate event/failure ID là idempotent success;
- index initializer chạy lặp lại an toàn trên database có V1 documents;
- AppHub write không tạo hoặc sửa checkpoint.

## 13. Phase 7 - Reconnect, shutdown, health và telemetry

### Task P7.1 - Reconnect/rejoin

State machine:

```text
Disconnected -> Connecting -> Connected -> JoiningMonitoring -> Running
                                      ^                          |
                                      +------ Reconnecting <-----+
```

- capped exponential backoff + jitter;
- chỉ ready sau `JoinMonitoring` thành công;
- rebuild connection tạo generation mới;
- dispose subscription/connection cũ trước khi thay;
- callback không bị đăng ký trùng sau reconnect;
- AppHub lỗi không cancel raw-log hosted service.

### Task P7.2 - Graceful shutdown

```text
stop reconnect/connect
-> stop receive
-> complete channel writer
-> drain consumer trong ShutdownTimeout
-> log remaining count nếu timeout
-> cancel/dispose
```

Không tuyên bố event còn trong memory là durable. Shutdown timeout phải hữu hạn và dùng host cancellation token.

### Task P7.3 - Observability

Mở rộng telemetry/health với label cardinality thấp:

- connection attempt/state/reconnect/join;
- callbacks received/admitted/dropped theo `SourceId` + allowlisted `eventName`;
- channel depth/saturation;
- mapping status và persistence result/latency;
- last callback age và last successful join;
- source readiness riêng cho raw-log/AppHub.

Không dùng event ID, device ID, connection ID hoặc raw payload làm metric label. `AppHubHealthCheck` phân biệt disabled, connecting, running, degraded và unhealthy theo threshold; không có event mới nhưng connection healthy không tự động là lỗi.

### Acceptance

- disconnect test chứng minh reconnect + rejoin đúng một lần;
- Mongo outage gây bounded backpressure/health signal, không làm memory tăng vô hạn;
- shutdown drain thành công và timeout path đều có test;
- raw-log tiếp tục advance checkpoint khi AppHub disconnect hoặc mapper failure.

## 14. Phase 8 - Test, UAT và rollout

### Task P8.1 - Unit tests

- options/validation/redaction;
- connection lifecycle với fake transport;
- envelope JSON order/hash/size/identity;
- bounded channel FIFO/full/timeout/cancellation;
- mapper golden tests cho 11 callbacks;
- tenant/time/privacy/unknown/malformed/oversized cases;
- V1 regression và common persistence/checkpoint order.

### Task P8.2 - Integration tests

- fixture callback -> channel -> mapper -> Mongo history/failure;
- duplicate retry và exact BSON V2;
- index initialization trên mixed V1/V2 database;
- AppHub và raw-log chạy đồng thời;
- AppHub disabled, unavailable hoặc reconnecting không block raw-log;
- shutdown khi channel còn backlog.

Real ERP transport thuộc UAT; unit/integration test trong repo dùng wrapper/fake để không phụ thuộc LAN server.

### Task P8.3 - UAT

Với một source/tenant UAT:

1. connect, join Monitoring và xác nhận readiness;
2. phát/capture đủ callback có producer;
3. đối chiếu event count, source/category/eventName và payload hash;
4. ngắt network/restart ERP để test reconnect/rejoin;
5. tạo burst để đo channel depth, latency và capacity;
6. restart Worker để ghi nhận best-effort gap semantics;
7. chạy raw-log song song và kiểm tra checkpoint vẫn tiến;
8. kiểm tra log/metric không lộ secret/full payload.

### Task P8.4 - Rollout/rollback

- deploy code với `AppHub:Enabled=false` trước;
- initialize additive indexes/validator và chạy V1 regression;
- bật một UAT source, sau đó mới bật production source;
- theo dõi connection, dropped callbacks, mapping status, Mongo latency và raw-log progress;
- rollback bằng cách disable AppHub; không xóa V2 history đã ghi và không đổi raw-log checkpoint.

### Acceptance cuối Sprint

- `dotnet build` và toàn bộ test suite pass;
- đủ 11 callback được register; callback chưa có producer được ghi rõ evidence gap;
- confirmed fixture map đúng V2, unknown variant giữ raw evidence;
- reconnect/rejoin, saturation và shutdown có test/evidence;
- AppHub không dùng checkpoint và không làm raw-log dừng;
- configuration/secret/privacy review pass;
- không có silent loss claim: tài liệu/runbook ghi rõ best-effort boundary.

## 15. Configuration target

`appsettings.Example.json` chỉ chứa placeholder an toàn:

```json
{
  "DeviceEventHistory": {
    "AppHub": {
      "Enabled": false,
      "Sources": [
        {
          "SourceId": "erp-apphub-ua",
          "Endpoint": "https://erp.example.com/signalr",
          "HubName": "AppHub",
          "CompanyId": null,
          "DedicatedSingleTenant": false,
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

Credential không nằm trong sample. Auth query contract đã khóa là `token` hoặc `tokenjwt` cùng `sessionType=0`; tên secret key và rotation procedure được bổ sung sau C3b. Không hard-code token trong endpoint.

## 16. Definition of Done cho mỗi task

Một task chỉ được coi là done khi:

- code nằm đúng layer và tuân theo `Coding-Standards.md`;
- public responsibility có unit test, behavior qua I/O có integration test phù hợp;
- cancellation, retry, failure và structured logging path đã được xử lý;
- không thêm secret, environment path hoặc full payload vào source/log;
- không làm thay đổi event identity/checkpoint semantics của raw-log ngoài contract đã duyệt;
- docs/config sample được cập nhật cùng code;
- build/test pass và không để placeholder `Class1.cs`/`UnitTest1.cs` mới.

## 17. Tài liệu source of truth

- `AppHub-Data-Receiving-and-Recording-Strategy.md`: connection, admission, mapping và reliability strategy.
- `Sprint-2-Db-Schema.md`: MongoDB V2/final document contract.
- `Device-Event-History-Current-Codebase.md`: hiện trạng implementation trước Sprint 2.
- `Device-Event-History-Architecture.md`: project boundary và dependency direction.
- `Coding-Standards.md`: coding/config/security/test conventions.

Khi plan khác Strategy hoặc Schema, Strategy/Schema được ưu tiên và plan phải được cập nhật trong cùng change set.
