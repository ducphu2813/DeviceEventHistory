# Device Event History - Hiện trạng codebase

## 1. Mục đích và trạng thái

Tài liệu này mô tả codebase thực tế của `DeviceEventHistory.Worker` sau khi hoàn thành baseline Phase 1A / Sprint 1. Mục tiêu là giúp developer mới hiểu được:

- solution đang có những project và boundary nào;
- một raw record đi qua những thành phần nào;
- checkpoint, retry, duplicate và crash recovery hoạt động ra sao;
- cần sửa ở đâu khi thêm source, block parser hoặc persistence behavior mới.


Các tài liệu contract liên quan:

- `Device-Event-History-Architecture.md`: boundary và processing flow.
- `Device-Event-History-Design.md`: hai luồng raw-log và AppHub/direct transport.
- `Device-Event-History-Plan.md`: work package và acceptance.
- `Logs-Reading-Strategy.md`: chiến thuật tail nhiều file, fairness và checkpoint.
- `2026-08-22-Db-Schema.md`: document shape và index contract.
- `Coding-Standards.md`: coding rule của source.

Khi tài liệu và source code khác nhau, cần kiểm tra contract trước khi sửa; không tự ý thay đổi document shape hoặc processing semantics.

## 2. Phạm vi hiện tại

Worker hiện thực hiện đường đi:

```text
RFID.Antenna
    -> yyyy/MM/dd/File_{FileId}.txt
    -> discover/tail/frame
    -> parse/canonical mapping
    -> MongoDB history hoặc failure
    -> MongoDB checkpoint
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
- unit, integration và architecture tests.

Chưa thuộc phạm vi hiện tại:

- AppHub/SignalR, dashboard và API query;
- `device_current_state`, `tag_current_state` hoặc production projections;
- multi-worker active-active/leader election;
- message broker hoặc durable queue riêng;
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

Chứa các concept ổn định, không biết MongoDB, filesystem, HTTP hoặc hosting:

- `Events/CanonicalDeviceEvent.cs`: canonical event và các facts.
- `Common/AppConst.cs`: section name, default kỹ thuật, block name, collection name, message và observability contract không chứa secret.

`CanonicalDeviceEvent` giữ `EventId`, schema/parser version, category, company, thời gian, source identity, device, raw payload, facts và parse result. Raw payload luôn được giữ để trace/reprocess.

### 3.2. Application

Path: `src/DeviceEventHistory.Application`

Định nghĩa model và use-case abstraction, không phụ thuộc MongoDB hay file API:

- `Parsing/`: raw context, parser contract, canonical mapper và `ProcessRawFileRecordHandler`.
- `Persistence/`: history/failure/checkpoint interfaces, checkpoint model và `RawRecordPersistenceCoordinator`.
- `Metadata/`: source mode và metadata resolver contract.
- `Observability/`: `IIngestionTelemetry`.

`ProcessRawFileRecordHandler` chỉ ghép parser với mapper. `RawRecordPersistenceCoordinator` áp dụng rule quan trọng: persist history/failure trước, chỉ advance checkpoint sau khi persistence được xác nhận.

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

MongoDb/
  Configuration/  MongoDbOptions
  Mapping/        canonical/failure -> BSON
  Stores/         history, failure, checkpoint store
  Indexes/        idempotent index initializer

Metadata/         configuration-based device metadata resolver
Observability/    metrics, health state, logging scopes
```

MongoDB Driver chỉ xuất hiện trong project này.

### 3.4. Worker

Path: `src/DeviceEventHistory.Worker`

Đây là composition root và runtime orchestration:

- `Program.cs`: build host, đăng ký DI, log redacted startup summary.
- `Configuration/`: bind và validate options.
- `HostedServices/`: startup Mongo initialization và raw-log hosted service.
- `Orchestration/`: polling, registry, scheduler, turn processor và shutdown.
- `HealthChecks/`: Mongo, source path và ingestion progress checks.

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

Sau đó `RawLogIngestionHostedService` khởi chạy orchestration. `GracefulShutdownCoordinator` tạo scheduling loop trước polling loop để bounded queue có consumer trước khi polling bắt đầu enqueue.

Options đều được bind từ configuration và validate bằng `ValidateOnStart()`:

- `WorkerOptions`: `DeviceEventHistory`.
- `RfidRawLogOptions`: `DeviceEventHistory:RawLog`.
- `MongoDbOptions`: `DeviceEventHistory:DatabaseSettings:MongoDb`.
- `IngestionOptions`: `DeviceEventHistory:Ingestion`.
- `ObservabilityOptions`: `DeviceEventHistory:Observability`.

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

## 7. Parser và canonical mapping

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

## 8. MongoDB persistence và checkpoint

Worker dùng ba collection chính:

```text
device_event_history
ingestion_failures
ingestion_checkpoints
```

### 8.1. History và failure

`MongoDeviceEventHistoryWriter` map canonical event sang BSON và insert append-only. `eventId` có unique index.

`MongoIngestionFailureWriter` lưu record hoàn chỉnh nhưng parse không thành công, gồm failure identity, raw payload, error code/message, parser version và source offsets. `failureId` có unique index.

Duplicate deterministic identity được coi là idempotent success, không tạo thêm document.

### 8.2. Checkpoint

`MongoIngestionCheckpointStore` lưu position, version, last identity/hash, observed file length, worker id và updated time. Advance dùng compare-and-set theo `_id` và `version`.

Trình tự bắt buộc:

```text
frame complete record
    -> persist history hoặc failure
    -> Mongo confirmation
    -> advance checkpoint
```

Nếu persistence hoặc checkpoint CAS thất bại, checkpoint không được tiến tiếp. Khi retry, identity ổn định từ source/path/offset/raw hash giúp tránh duplicate.

Index được khởi tạo idempotent bởi `MongoIndexInitializer`, gồm unique identity, thời gian, device/gate/tag/category/parse và source offset indexes.

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

Đây là at-least-once processing với idempotent persistence, không phải durable queue semantics.

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
- persistence latency và ingestion lag.
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

Health check classes đã đăng ký cho Mongo, raw-log source, ingestion progress và AppHub. AppHub có health state riêng theo source, phân biệt connecting/running/degraded/unhealthy; không có callback mới nhưng connection vẫn healthy không tự động bị coi là lỗi. Worker hiện chưa expose HTTP health endpoint; muốn đưa health ra ngoài cần bổ sung host/endpoint riêng.

AppHub reconnect dùng connection generation mới cho mỗi lần rebuild, backoff exponential có jitter và join lại `Monitoring` sau khi SignalR báo reconnected. Callback chỉ được register một lần cho mỗi connection; transition join được serialize để không phát sinh duplicate join khi transport phát nhiều lifecycle event liên tiếp. Khi shutdown, runtime dừng receive/reconnect, complete bounded channel, drain consumer trong `ShutdownTimeout` và cancel processor nếu drain timeout; event còn trong memory không được coi là durable.

## 11. Testing hiện tại

Các test project:

- `tests/DeviceEventHistory.UnitTests`: options, parser, framer, orchestration, persistence và observability.
- `tests/DeviceEventHistory.IntegrationTests`: Mongo persistence và raw-log worker flow.
- `tests/DeviceEventHistory.ArchitectureTests`: dependency direction/boundary.

Lần kiểm tra local gần nhất:

```text
Build:         succeeded
Unit tests:    71 passed
Integration:    4 passed
Architecture:   1 passed
```

Integration test cần MongoDB local/container tương ứng. Việc build/test pass không thay thế kiểm tra remote server, sample log thực tế và physical-device UAT.

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

### 12.3. Thêm database hoặc persistence behavior

1. thêm subsection dưới `DatabaseSettings`;
2. tạo options và validator riêng;
3. tạo abstraction ở Application;
4. implement store/mapper/index ở Infrastructure;
5. đăng ký DI ở Worker;
6. bổ sung retry, idempotency và integration test.

Không reference MongoDB Driver từ Domain/Application.

### 12.4. Thay đổi orchestration

Phải giữ các invariant:

- không mất queue item do bounded capacity;
- không block consumer khi tự requeue;
- không để một hot file chiếm toàn bộ consumer;
- không advance checkpoint trước persistence confirmation;
- truyền `CancellationToken` qua toàn bộ async path;
- thay đổi quan trọng phải có regression test.

## 13. Các giới hạn và việc nên làm tiếp

- `FileRegistry` hiện giữ state in-memory và chưa có eviction cho file/ngày rất cũ.
- `LookbackDays` là discovery window, không phải cơ chế backfill toàn bộ archive.
- Remote discovery phụ thuộc directory listing và Range support của server.
- Chưa có durable queue; raw file và Mongo checkpoint là nguồn recovery.
- Chưa có health HTTP endpoint hoặc exporter/vendor-specific metrics.
- Chưa có multi-worker coordination.
- Cần tiếp tục kiểm chứng đầy đủ numeric/business meaning của `b(...)`, `te(...)`, `sp(...)`, cùng mapping chính thức giữa `DeviceId`, `GateId`, `CompanyId` và tenant.

Mọi thay đổi tiếp theo nên bắt đầu bằng việc xác định invariant liên quan trong các phần trên, sau đó thêm test chứng minh behavior trước khi tối ưu hoặc mở rộng.
