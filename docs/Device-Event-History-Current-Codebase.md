# Device Event History - Hiện trạng codebase

## 1. Mục đích và trạng thái

Trạng thái cập nhật ngày 2026-08-31:

- Sprint 1 raw-log ingestion: đã hoàn thành implementation.
- Sprint 2 ERP AppHub ingestion: đã hoàn thành implementation.
- Giai đoạn hiện tại: chờ chạy test, runtime verification và UAT theo `Sprint-2-Testcase.md`.

Tài liệu này mô tả codebase thực tế của `DeviceEventHistory.Worker` sau khi hoàn thành implementation Sprint 1 và Sprint 2. Sprint 2 đã hoàn tất phần code cho luồng ERP AppHub/SignalR; phần còn lại là chạy test, runtime verification và UAT trước khi xác nhận sẵn sàng triển khai. Mục tiêu là giúp developer mới hiểu được:

- solution đang có những project và boundary nào;
- một raw-log record hoặc AppHub callback đi qua những thành phần nào;
- checkpoint, retry, duplicate và crash recovery hoạt động ra sao;
- cần sửa ở đâu khi thêm source, block parser hoặc persistence behavior mới.


Các tài liệu contract liên quan:

- `Device-Event-History-Architecture.md`: boundary và processing flow.
- `Device-Event-History-Design.md`: hai luồng raw-log và AppHub/direct transport.
- `Device-Event-History-Plan.md`: work package và acceptance.
- `Logs-Reading-Strategy.md`: chiến thuật tail nhiều file, fairness và checkpoint.
- `2026-08-22-Db-Schema.md`: document shape và index contract.
- `AppHub-Data-Receiving-and-Recording-Strategy.md`: connection, callback admission, mapping và reliability của AppHub.
- `Sprint-2-Db-Schema.md`: canonical document contract V2 dùng chung cho raw-log và AppHub.
- `Sprint-2-Plan.md`: implementation plan và acceptance của Sprint 2.
- `Sprint-2-Testcase.md`: test plan/runtime verification còn phải thực hiện cho Sprint 2.
- `Coding-Standards.md`: coding rule của source.

Khi tài liệu và source code khác nhau, cần kiểm tra contract trước khi sửa; không tự ý thay đổi document shape hoặc processing semantics.

## 2. Phạm vi hiện tại

Worker hiện thực hiện song song hai đường ingestion:

```text
RFID.Antenna
    -> yyyy/MM/dd/File_{FileId}.txt
    -> discover/tail/frame
    -> parse/canonical mapping
    -> MongoDB history hoặc failure
    -> MongoDB checkpoint

ERP AppHub Monitoring
    -> classic SignalR callback
    -> bounded admission channel
    -> canonical mapping
    -> MongoDB history hoặc failure
```

Đã có:

- local filesystem source;
- remote HTTP directory listing và HTTP Range reader;
- đọc nhiều file với bounded concurrency và fair turn scheduling;
- framing theo byte UTF-8 với terminator `e(0)`;
- parser các block `@`, `b`, `t`, `te`, `sp`, `u`;
- canonical event/failure mapping;
- MongoDB history, failure, checkpoint và idempotent identity;
- retry, checkpoint compare-and-set, restart recovery;
- structured logging, metrics abstraction và health state;
- ERP AppHub client dùng classic SignalR 2.4.3;
- callback registration, `JoinMonitoring`, reconnect/rejoin và connection generation;
- bounded FIFO channel riêng cho từng AppHub source;
- canonical mapper cho 11 callback Monitoring đã xác định;
- tenant resolution, payload redaction, payload-size guard và Schema V2 mapping;
- common history/failure persistence dùng chung cho raw-log và AppHub;
- AppHub health, telemetry, shutdown drain và source isolation;
- unit, integration và architecture tests.

Implementation Sprint 2 đã hoàn tất nhưng chưa được xác nhận hoàn thành testing/UAT. Các phần còn phải kiểm chứng gồm:

- chạy đầy đủ automated test suite trên môi trường test phù hợp;
- kết nối endpoint ERP AppHub thực tế bằng service identity;
- xác nhận connect/join/reconnect/rejoin và shutdown drain ở runtime;
- capture/đối chiếu payload thực tế của các callback opaque;
- xác nhận tenant, privacy/redaction, channel capacity và behavior khi MongoDB/network lỗi;
- chạy các test case và lưu evidence theo `Sprint-2-Testcase.md`.

Chưa thuộc phạm vi hiện tại:

- dashboard và API query;
- `device_current_state`, `tag_current_state` hoặc production projections;
- multi-worker active-active/leader election;
- message broker hoặc durable inbox/outbox riêng;
- direct publisher từ `RFID.Antenna`/`RFID.Analytics` sang ingress riêng;
- tự động request snapshot khi chưa có device catalog/correlation contract;
- deduplicate chéo raw-log và AppHub khi chưa có shared producer event ID;
- cam kết lossless/exactly-once cho SignalR;
- thay đổi source `RFID.Antenna`, `RFID.Analytics` hoặc ERP legacy;
- backfill engine độc lập cho toàn bộ lịch sử.

## 3. Kiến trúc solution

Dependency direction:

```text
DeviceEventHistory.Domain
            ^
            |
DeviceEventHistory.Application
            ^
            |
DeviceEventHistory.Infrastructure
            ^
            |
DeviceEventHistory.Worker
```

### 3.1. Domain

Path: `src/DeviceEventHistory.Domain`

Chứa các concept ổn định, không biết MongoDB, filesystem, HTTP, SignalR hoặc hosting:

- `Events/CanonicalDeviceEvent.cs`: canonical event và các facts.
- `Failures/CanonicalIngestionFailure.cs`: failure model dùng chung cho mọi source.
- `Common/AppConst.cs`: section name, default kỹ thuật, block name, collection name, message và observability contract không chứa secret.

`CanonicalDeviceEvent` hiện biểu diễn Schema V2: giữ `EventId`, schema/parser version, category, `SourceKind`, company, occurred/received/persisted/timeline time, source identity, device, raw payload, sparse facts, parse result và ingestion metadata. Source context có thể mang file offsets hoặc AppHub connection generation/receive sequence tùy adapter; không điền fake field của source khác. Raw payload đã qua privacy boundary luôn được giữ để trace/reprocess.

### 3.2. Application

Path: `src/DeviceEventHistory.Application`

Định nghĩa model và use-case abstraction, không phụ thuộc MongoDB, file API hay SignalR:

- `Ingestion/`: `RawSourceEvent`, source-neutral mapping result, identity và mapper registry.
- `AppHub/Mapping/`: mapper theo exact `sourceKind + eventName`, tenant resolution và JSON value reader framework-independent.
- `Parsing/`: raw context, parser contract, canonical mapper và `ProcessRawFileRecordHandler`.
- `Persistence/`: common history/failure persistence, checkpoint interfaces/model và raw-log checkpoint wrapper.
- `Metadata/`: source mode và metadata resolver contract.
- `Observability/`: `IIngestionTelemetry`.

`CanonicalIngestionPersistenceService` là boundary dùng chung để persist đúng một history hoặc failure outcome. `ProcessRawFileRecordHandler` chỉ ghép raw parser với mapper. `RawRecordPersistenceCoordinator` bọc common persistence và áp dụng rule riêng của file: persist history/failure trước, chỉ advance checkpoint sau khi persistence được xác nhận. AppHub processor gọi common persistence trực tiếp và không dùng checkpoint.

### 3.3. Infrastructure

Path: `src/DeviceEventHistory.Infrastructure`

Chứa các implementation technology-specific:

```text
RfidRawLog/
  Configuration/  options và source policy
  Discovery/      local/remote file discovery và descriptor
  Reading/        local/remote tail reader
  Framing/        byte-oriented record framer
  Parsing/        tokenizer và RFID raw parser

AppHub/
  Configuration/ options cho endpoint/source/channel/reconnect
  Transport/     classic SignalR connection, proxy và callback registration
  Admission/     redaction, immutable envelope và bounded channel admission

MongoDb/
  Configuration/  MongoDbOptions
  Mapping/        canonical/failure -> BSON
  Stores/         history, failure, checkpoint store
  Indexes/        idempotent index initializer

Metadata/         configuration-based device metadata resolver
Observability/    metrics, health state, logging scopes
```

MongoDB Driver, classic SignalR client và Newtonsoft.Json chỉ xuất hiện trong project này; các technology-specific type không rò sang Domain/Application.

### 3.4. Worker

Path: `src/DeviceEventHistory.Worker`

Đây là composition root và runtime orchestration:

- `Program.cs`: build host, đăng ký DI, log redacted startup summary.
- `Configuration/`: bind và validate options.
- `HostedServices/`: startup Mongo initialization.
- `Orchestration/`: raw-log polling/registry/scheduler/turn processor; AppHub hosted service/source runtime/event processor; graceful shutdown.
- `HealthChecks/`: Mongo, raw-log source, ingestion progress và AppHub checks.

Worker không chứa parser hoặc BSON mapping trực tiếp trong `BackgroundService`.

## 4. Startup và dependency injection

Luồng startup thực tế:

```text
Program.cs
  -> Host.CreateApplicationBuilder(args)
  -> AddDeviceEventHistoryConfiguration(configuration)
  -> bind options + ValidateOnStart
  -> register adapters, stores, scheduler và hosted services
  -> build host
  -> log configuration summary đã redaction
  -> host.Run()
```

`StartupInitializationHostedService` chạy khi Worker enabled:

1. cấu hình source identity cho health state;
2. ping MongoDB;
3. tạo/kiểm tra ba collection và indexes;
4. đánh dấu startup ready.

Sau initialization, hai hosted service có thể chạy độc lập theo configuration:

- `RawLogIngestionHostedService` khởi chạy file orchestration. `GracefulShutdownCoordinator` tạo scheduling loop trước polling loop để bounded queue có consumer trước khi polling bắt đầu enqueue.
- `ErpAppHubMonitoringHostedService` tạo một `AppHubSourceRuntime` cho mỗi source, sau đó runtime khởi động FIFO processor trước connection loop, đăng ký callback trước `Start()`, join `Monitoring` và quản lý reconnect/rejoin.

Options đều được bind từ configuration và validate bằng `ValidateOnStart()`:

- `WorkerOptions`: `DeviceEventHistory`.
- `RfidRawLogOptions`: `DeviceEventHistory:RawLog`.
- `MongoDbOptions`: `DeviceEventHistory:DatabaseSettings:MongoDb`.
- `IngestionOptions`: `DeviceEventHistory:Ingestion`.
- `ObservabilityOptions`: `DeviceEventHistory:Observability`.
- `AppHubOptions`: `DeviceEventHistory:AppHub`.

Connection string có thể lấy từ environment variable `DEVICE_EVENT_HISTORY_MONGODB_CONNECTION_STRING`. Không log connection string, password hoặc token.

## 5. Configuration và source identity

Các file `appsettings*.json` bị ignore để phục vụ cấu hình local/development; `launchSettings.json` vẫn được giữ để chọn environment. Cấu hình môi trường thật phải đến từ environment/secret provider.

Các option quan trọng:

| Option | Ý nghĩa |
|---|---|
| `Enabled` | bật/tắt toàn bộ ingestion |
| `WorkerId` | identity của process worker, không dùng làm event identity |
| `PollInterval` | khoảng thời gian giữa các lần discovery |
| `LookbackDays` | số ngày lùi thêm ngoài ngày hiện tại |
| `MaxConcurrentFiles` | số file được xử lý đồng thời |
| `MaxBytesPerTurn` | giới hạn bytes mỗi lượt của một file |
| `MaxRecordsPerTurn` | giới hạn records mỗi lượt |
| `MaxTurnDuration` | giới hạn thời gian mỗi lượt |
| `StartupExistingFilePolicy` | offset ban đầu cho file có từ lúc startup nhưng chưa có checkpoint |
| `NewFilePolicy` | offset ban đầu cho file phát hiện sau startup |
| `Sources` | danh sách Antenna source |

Một source có:

- `SourceId`: identity ổn định của installation/stream;
- `Mode`: `Local` hoặc `RemoteHttp`;
- `RootPath` cho local, `RemoteBaseUrl` cho remote;
- `CompanyId`, `TimeZoneId`, `FilePattern`, `Enabled`.

`FileId` chỉ là routing key của file, không phải `DeviceId`, `GateId` hoặc `SourceId`.

Checkpoint key thực tế là:

```text
SourceId + FolderDate + FileId + RelativePath
```

Điều này cho phép `File_1.txt` của hai ngày hoặc hai source có checkpoint độc lập.

AppHub được cấu hình độc lập tại `DeviceEventHistory:AppHub`. Mỗi enabled source có:

- `SourceId`, absolute `Endpoint`, `HubName` và danh sách `EnabledEvents`;
- optional `CompanyId` và cờ `DedicatedSingleTenant` cho tenant fallback có kiểm soát;
- `ChannelCapacity`, `EnqueueTimeout`, reconnect min/max delay;
- credential lấy từ secret/environment, không đặt trong endpoint hoặc file cấu hình commit vào Git.

`SourceId` phải unique trên cả raw-log và AppHub source. AppHub validator chỉ cho phép callback trong allowlist đã khóa và không log token, full query string hoặc credential.

## 6. Runtime pipeline

### 6.1. Discovery

`RawLogFileDiscovery` tính ngày hiện tại theo `source.TimeZoneId`, sau đó gọi adapter cho từng ngày từ `LookbackDays` về ngày hiện tại. Với `LookbackDays=1`, phạm vi là hôm nay và hôm qua.

Adapter local duyệt thư mục vật lý. Adapter remote:

1. GET directory URL `yyyy/MM/dd/`;
2. parse HTML link listing;
3. chỉ nhận file khớp `File_{FileId}.txt`;
4. tạo `RawLogFileDescriptor` với source/date/file/path/URL.

Discovery có thể chạy lặp lại; identity của descriptor giúp registry không tạo state trùng.

### 6.2. Registry và initial offset

`FileRegistry` giữ một `FileIngestionState` cho mỗi logical file.

Khi tạo state:

1. load checkpoint từ `ingestion_checkpoints`;
2. nếu có checkpoint, dùng `checkpoint.Position`;
3. nếu chưa có checkpoint, áp dụng startup/new-file policy;
4. tạo framer riêng cho file;
5. đưa state vào scheduler.

`Beginning` nghĩa là bắt đầu từ byte `0`; `End` nghĩa là bỏ qua phần file hiện tại và bắt đầu từ EOF. Policy chỉ áp dụng khi chưa có checkpoint. Checkpoint luôn được ưu tiên.

Registry là in-memory. Vì vậy sau restart, chỉ các file được discovery lại trong lookback window mới được dựng lại state và load checkpoint. Checkpoint của file cũ hơn vẫn nằm trong Mongo nhưng không tự làm file đó được discovery.

Trong cùng một process, registry còn giữ state cũ và orchestration có thể tiếp tục schedule state chưa stopped; đây là lý do cleanup/eviction state là một việc cần xem xét khi số ngày và số file tăng lâu dài.

### 6.3. Tail reader

`IRawLogTailReader` chọn implementation theo `RawLogSourceMode`:

- `LocalRawLogTailReader`: mở read-only với share mode tương thích writer đang append.
- `RemoteHttpRawLogTailReader`: gửi HTTP `Range: bytes={offset}-{offset+maxBytes-1}`.

Offset luôn là byte offset `long`. Remote reader xử lý `206 PartialContent`, phát hiện server bỏ qua Range, và coi `416` tại đúng EOF là caught up thay vì truncation.

Reader không sửa, rename, lock hoặc truncate source file.

### 6.4. Framing

`RawLogRecordFramer` làm việc trên bytes UTF-8 và tìm marker:

```text
e(0)
```

Framer:

- tách nhiều record trong một chunk;
- giữ partial bytes qua nhiều chunk;
- tính `StartOffset`/`EndOffsetExclusive` tuyệt đối;
- hỗ trợ terminator bị chia qua boundary;
- không phát record chưa có terminator;
- giới hạn `MaxRecordBytes`.

Record phía sau chỉ được xử lý sau khi record phía trước đã có persistence outcome rõ ràng.

### 6.5. Turn processor

`FileTurnProcessor` thực hiện bounded turn:

1. đọc chunk từ `state.ReadOffset`;
2. cập nhật observed file length;
3. frame các record hoàn chỉnh;
4. parse/map từng record;
5. persist event hoặc failure;
6. commit checkpoint sau confirmation;
7. dừng khi đạt byte/record/time budget;
8. trả `CaughtUp`, `WaitingForMoreData`, `Requeue`, `Truncated`, `PersistenceFailed` hoặc conflict tương ứng.

Partial record chỉ nằm trong framer memory; checkpoint vẫn trỏ tới contiguous prefix đã persistence.

### 6.6. AppHub realtime runtime

Luồng AppHub hiện tại:

```text
ErpAppHubMonitoringHostedService
    -> AppHubSourceRuntime per source
    -> AppHubMonitoringConnection / AppHubCallbackRegistrar
    -> AppHubEventAdmission
    -> Channel<RawSourceEvent>
    -> AppHubEventProcessor
    -> RawSourceEventMapperRegistry
    -> CanonicalIngestionPersistenceService
    -> MongoDB history hoặc failure
```

Mỗi `AppHubSourceRuntime` sở hữu connection, connection generation, monotonic receive sequence, bounded channel và một FIFO consumer riêng. Callback chỉ redaction/serialize arguments, tính hash/size, tạo immutable envelope rồi admission; callback không parse business data hoặc ghi MongoDB trực tiếp.

Admission dùng `TryWrite` fast path, sau đó chờ tối đa `EnqueueTimeout`. Khi channel vẫn full, callback được ghi nhận là dropped, tăng saturation metric và làm health degradation; không đổi sang unbounded channel và không giả vờ event đã persistence.

Connection lifecycle đăng ký callback trước `Start()`, join `Monitoring` sau khi connect, rebuild connection với generation mới khi lỗi và reconnect bằng capped exponential backoff có jitter. Runtime join lại group sau reconnect và dispose subscription/connection cũ để tránh duplicate callback registration.

AppHub không có checkpoint và không replay mặc định. Identity của một admitted envelope ổn định trong Mongo retry, nhưng không deduplicate qua reconnect/restart hoặc với raw-log khi chưa có producer `sourceEventId` dùng chung.

## 7. Parser và canonical mapping

### 7.1. Raw-log parser

Luồng parser:

```text
FramedRawLogRecord
  -> RawRecordContext
  -> BlockTokenizer
  -> RfidRawRecordParser
  -> CanonicalDeviceEventMapper
  -> RawRecordProcessingResult
```

`BlockTokenizer` nhận diện boundary block trước khi parse arguments. Không split toàn raw record bằng dấu phẩy vì một số field có thể chứa cấu trúc riêng.

Các block hiện hỗ trợ:

| Block | Mapping chính |
|---|---|
| `@(...)` | `TagId`, thời gian đọc, `DeviceId`, `GateId` |
| `b(...)` | gate state |
| `t(...)` | antenna/signal/time/RSSI |
| `te(...)` | event type, process, quantity, process list |
| `sp(...)` | custom process |
| `u(...)` | user id |
| `e(0)` | record terminator, do framer xử lý |

Parser tolerant với block thiếu. Header `@(...)` hợp lệ là điều kiện chính cho normal history V1. Block unknown tạo warning và raw value vẫn được giữ; block malformed tạo parse error, nhưng raw payload không bị loại bỏ.

Category hiện được suy ra ở mapper:

- có `te`: `business_process`;
- không có `te` nhưng có `t`: `tag_read`;
- còn lại: `unknown`.

`CompanyId` lấy từ source configuration. `OccurredAtUtc`/`OccurredAtLocal` được chuyển theo `TimeZoneId` của source. Mọi numeric/date parsing dùng invariant culture.

### 7.2. AppHub mapper registry

AppHub callback được dispatch bằng exact key `sourceKind + eventName`. Các mapper hiện có:

| Mapper | Callback/category chính |
|---|---|
| `DeviceOnlineEventMapper` | `receiveDeviceOnline` / `device_online` |
| `DeviceConnectionEventMapper` | `receiveStateConnected` / `device_connection` |
| `DeviceControlStateEventMapper` | green/red / `device_control_state` |
| `DeviceSensorStateEventMapper` | `receiveTimeSensor` / `device_sensor_state` |
| `DeviceReadTagEventMapper` | `receiveDeviceReadTag` / `tag_read` |
| `ScannerEventMapper` | scanner connect/disconnect/info / activity hoặc snapshot |
| `ClientDeviceConnectionEventMapper` | client-device connect/disconnect |

Mapper chỉ tạo facts đã có evidence. Known optional variant tạo warning; traceable callback chưa khóa contract đi fallback `unknown/unmapped` và giữ raw arguments; malformed JSON, required identity hoặc tenant không hợp lệ tạo `ingestion_failures`.

Tenant resolution ưu tiên positive `CompanyId` trong payload. Configured `CompanyId` chỉ được fallback khi source khai báo `DedicatedSingleTenant=true`; payload/config mismatch hoặc không resolve được tenant tạo failure. Scanner `ConnectionId` được hash trước persistence và các field nhạy cảm theo privacy policy bị drop tại admission boundary.

## 8. MongoDB persistence và checkpoint

Worker dùng ba collection chính:

```text
device_event_history
ingestion_failures
ingestion_checkpoints
```

### 8.1. History và failure

`CanonicalIngestionPersistenceService` nhận source-neutral outcome và gọi đúng writer. `MongoDeviceEventHistoryWriter` map canonical event V1/V2 sang BSON và insert append-only. `eventId` có unique index.

`MongoIngestionFailureWriter` lưu data failure của raw-log hoặc AppHub, gồm source kind/context, failure identity, raw payload đã qua privacy boundary, error code/message/stage, parser version và ingestion metadata. File context/offset hoặc AppHub event/generation/sequence chỉ xuất hiện khi source tương ứng có dữ liệu. `failureId` có unique index.

Duplicate deterministic identity được coi là idempotent success, không tạo thêm document.

### 8.2. Checkpoint

Checkpoint chỉ áp dụng cho raw-log. `MongoIngestionCheckpointStore` lưu position, version, last identity/hash, observed file length, worker id và updated time. Advance dùng compare-and-set theo `_id` và `version`.

Trình tự bắt buộc:

```text
frame complete record
    -> persist history hoặc failure
    -> Mongo confirmation
    -> advance checkpoint
```

Nếu persistence hoặc checkpoint CAS thất bại, checkpoint không được tiến tiếp. Khi retry, identity ổn định từ source/path/offset/raw hash giúp tránh duplicate.

AppHub history/failure dùng common persistence nhưng tuyệt đối không tạo hoặc advance file checkpoint.

Index được khởi tạo idempotent bởi `MongoIndexInitializer`, gồm unique identity, company/timeline/category, source kind/source ID/event name, device/gate/tag/parse và raw-log source offset indexes. Sparse source/facts fields dùng partial indexes theo Schema V2 khi phù hợp.

## 9. Scheduling, backpressure và recovery

`FairFileScheduler` dùng bounded `Channel<FileIngestionState>` với số consumer bằng `MaxConcurrentFiles`. Một file không giữ OS thread riêng; mỗi file được xử lý theo turn budget rồi đưa về cuối hàng khi còn backlog.

Queue capacity hiện được tính từ `MaxConcurrentFiles * SchedulerQueueMultiplier`; nó không phải giới hạn tổng số file. Nếu số file lớn hơn capacity, polling producer sẽ enqueue dần khi consumer giải phóng chỗ.

Một deadlock trước đây đã được sửa: consumer không còn block bằng `WriteAsync` khi tự requeue vào queue đã đầy. Requeue nội bộ dùng non-blocking `TryWrite`; nếu queue đầy, state được giải phóng schedule flag và polling cycle sau sẽ schedule lại.

Điều này cho phép các trường hợp như:

```text
20 files, 4 consumers, queue capacity 16
```

tiếp tục xử lý mà không làm toàn bộ consumer tự khóa.

Khi Worker restart hoặc crash:

- queue và framer memory bị mất;
- source raw file vẫn là dữ liệu gốc;
- checkpoint chỉ trỏ tới record đã persistence xác nhận;
- Worker discovery lại file, load checkpoint và đọc tiếp;
- record đã ghi nhưng checkpoint chưa advance có thể được xử lý lại, nhưng idempotency ngăn duplicate.

Raw-log là at-least-once processing với idempotent persistence, không phải durable queue semantics.

AppHub có reliability boundary khác: bounded channel giữ FIFO của các event đã admission trong một process, nhưng channel mất khi process crash và ERP SignalR không replay mặc định. Mongo retry giữ identity của cùng envelope; event có thể mất khi chưa connect/join, network disconnect, admission timeout, process crash hoặc shutdown drain timeout. AppHub failure không dừng raw-log và raw-log failure không dừng AppHub.

## 10. Observability và debug flow

`IIngestionTelemetry` là abstraction cho metrics. `IngestionMetrics` dùng `System.Diagnostics.Metrics` và cập nhật health state của raw-log cùng AppHub.

Metrics hiện có cho:

- files discovered/active;
- source access failure;
- bytes read, records framed, partial records;
- parsed/warning/failed records;
- history/failure writes và duplicate identity;
- checkpoint advance/failure;
- Mongo retry/failure;
- oversized/truncated files;
- persistence latency và ingestion lag;
- AppHub connection attempts/states, reconnect/join, callback admission và mapping result;
- AppHub channel depth/saturation, last callback age và source readiness.

Structured logging dùng `AppConst.Logging` và `LoggingScopes`. Log scope chứa `WorkerId`, `SourceId`, `FolderDate`, `FileId`, `RelativePath`, offsets và result; không chứa connection string hoặc raw payload đầy đủ.

Các log quan trọng khi debug:

| Log | Ý nghĩa |
|---|---|
| `configuration validated` | options đã bind/validate, secret chỉ hiện dưới dạng configured true/false |
| `MongoDB collections and indexes initialized` | startup Mongo thành công |
| `scheduler started` | consumer đã sẵn sàng |
| `file state created` | registry đã load checkpoint hoặc chọn initial policy |
| `file turn started` | scheduler thực sự lấy file ra xử lý |
| `turn read` | đã đọc bytes và biết file length/offset |
| `record processed` | history/failure persistence và checkpoint flow đã hoàn tất |
| `file truncated`, `checkpoint conflict`, `turn failed` | cần điều tra reliability |
| `AppHub ingestion started`, `source connected/disconnected` | trạng thái hosted service và connection từng source |
| `callback dropped`, `connection failed` | saturation, admission hoặc transport cần điều tra |
| `channel drained`, `drain timeout` | kết quả graceful shutdown của AppHub |

Health check classes đã đăng ký cho Mongo, raw-log source, ingestion progress và AppHub. AppHub có health state riêng theo source, phân biệt connecting/running/degraded/unhealthy; không có callback mới nhưng connection vẫn healthy không tự động bị coi là lỗi. Worker hiện chưa expose HTTP health endpoint; muốn đưa health ra ngoài cần bổ sung host/endpoint riêng.

AppHub reconnect dùng connection generation mới cho mỗi lần rebuild, backoff exponential có jitter và join lại `Monitoring` sau khi SignalR báo reconnected. Callback chỉ được register một lần cho mỗi connection; transition join được serialize để không phát sinh duplicate join khi transport phát nhiều lifecycle event liên tiếp. Khi shutdown, runtime dừng receive/reconnect, complete bounded channel, drain consumer trong `ShutdownTimeout` và cancel processor nếu drain timeout; event còn trong memory không được coi là durable.

## 11. Testing hiện tại

Sprint 2 đã hoàn thành implementation và đã bổ sung test code; trạng thái hiện tại là chờ thực thi đầy đủ test plan, runtime verification và UAT để chốt acceptance.

Các test project:

- `tests/DeviceEventHistory.UnitTests`: raw-log options/parser/framer/orchestration/persistence/observability cùng AppHub configuration, transport, admission/runtime, mapping, health và shutdown.
- `tests/DeviceEventHistory.IntegrationTests`: Mongo persistence, raw-log worker flow và Mongo Schema V2 compatibility.
- `tests/DeviceEventHistory.ArchitectureTests`: dependency direction/boundary.

Các con số `71 unit / 4 integration / 1 architecture` trước đây chỉ là baseline Sprint 1, không còn đại diện cho acceptance Sprint 2 sau khi test suite đã được mở rộng. Kết quả Sprint 2 phải được cập nhật từ lần chạy mới, bao gồm build, unit, integration, architecture và các evidence manual trong `Sprint-2-Testcase.md`.

Integration test cần MongoDB local/container tương ứng. Real ERP transport, authentication, opaque callback payload và reconnect behavior phải được xác nhận ở UAT; fake transport/unit test không thay thế runtime evidence.

## 12. Hướng dẫn mở rộng

### 12.1. Thêm source mode mới

Thực hiện theo thứ tự:

1. thêm enum mode trong `Application/Metadata`;
2. thêm options/validation nếu mode có configuration riêng;
3. implement `IRawLogSourceFileDiscovery`;
4. implement `IRawLogSourceTailReader`;
5. đăng ký adapter trong `ServiceCollectionExtensions`;
6. thêm unit test cho discovery, range/offset và failure behavior.

Không đưa HTTP/filesystem logic vào `SourcePollingCoordinator` hoặc `FileTurnProcessor`.

### 12.2. Thêm raw block hoặc mapping

1. xác nhận format từ source evidence;
2. thêm block constant/field count vào `AppConst.RawLog`;
3. thêm parsed model;
4. cập nhật `BlockTokenizer`/`RfidRawRecordParser`;
5. cập nhật canonical facts/mapper nếu contract yêu cầu;
6. giữ unknown/malformed behavior và raw payload;
7. thêm parser, mapping và Mongo document test.

Không suy đoán business meaning chỉ từ tên block hoặc một sample duy nhất.

### 12.3. Thêm hoặc thay đổi AppHub callback mapping

1. khóa exact callback name, argument order, payload casing/type/nullability và privacy policy từ source/UAT evidence;
2. thêm callback vào allowlist/constants và options validation;
3. thêm hoặc cập nhật `IRawSourceEventMapper` theo exact `sourceKind + eventName`;
4. giữ optional/unknown/malformed behavior, tenant rules và raw arguments sau redaction;
5. đăng ký mapper trong `AddErpAppHubIngestion`;
6. thêm golden mapping test, privacy test, Mongo BSON test và runtime testcase tương ứng.

Không ghi MongoDB trực tiếp trong SignalR callback và không đưa SignalR/Newtonsoft type vào Domain/Application.

### 12.4. Thêm database hoặc persistence behavior

1. thêm subsection dưới `DatabaseSettings`;
2. tạo options và validator riêng;
3. tạo abstraction ở Application;
4. implement store/mapper/index ở Infrastructure;
5. đăng ký DI ở Worker;
6. bổ sung retry, idempotency và integration test.

Không reference MongoDB Driver từ Domain/Application.

### 12.5. Thay đổi orchestration

Phải giữ các invariant:

- không mất queue item do bounded capacity;
- không block consumer khi tự requeue;
- không để một hot file chiếm toàn bộ consumer;
- không advance checkpoint trước persistence confirmation;
- không dùng checkpoint cho AppHub;
- không parse hoặc persist trực tiếp trong SignalR callback;
- không đổi bounded AppHub channel thành unbounded channel;
- không coi event còn trong AppHub memory là durable;
- truyền `CancellationToken` qua toàn bộ async path;
- thay đổi quan trọng phải có regression test.

## 13. Các giới hạn và việc nên làm tiếp

- `FileRegistry` hiện giữ state in-memory và chưa có eviction cho file/ngày rất cũ.
- `LookbackDays` là discovery window, không phải cơ chế backfill toàn bộ archive.
- Remote discovery phụ thuộc directory listing và Range support của server.
- Chưa có durable inbox/queue cho AppHub; raw file và Mongo checkpoint chỉ là recovery source của raw-log.
- Chưa có health HTTP endpoint hoặc exporter/vendor-specific metrics.
- Chưa có multi-worker coordination.
- Cần tiếp tục kiểm chứng đầy đủ numeric/business meaning của `b(...)`, `te(...)`, `sp(...)`, cùng mapping chính thức giữa `DeviceId`, `GateId`, `CompanyId` và tenant.
- Sprint 2 còn chờ automated test run đầy đủ và UAT evidence cho ERP endpoint, service credential, opaque callback payload, reconnect/rejoin, saturation, Mongo outage và privacy.
- AppHub vẫn là best-effort; chưa có producer event ID/correlation contract để deduplicate qua reconnect/restart hoặc với raw-log.

Mọi thay đổi tiếp theo nên bắt đầu bằng việc xác định invariant liên quan trong các phần trên, sau đó thêm test chứng minh behavior trước khi tối ưu hoặc mở rộng.
