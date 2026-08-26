# Device Event History - Implementation Plan Phase 1 / Sprint 1

## 1. Mục tiêu kế hoạch

Sprint 1 triển khai Worker Service đọc raw-log do `RFID.Antenna` sinh ra và lưu MongoDB theo kiến trúc trong `Device-Event-History-Architecture.md`.

Chiến thuật discovery, tail, fairness và multi-file scheduling được chốt chi tiết trong `Logs-Reading-Strategy.md`.

Kết quả cuối Sprint 1:

```text
File_{FileId}.txt
    -> complete record e(0)
    -> parse raw + facts
    -> device_event_history hoặc ingestion_failures
    -> ingestion_checkpoints
```

Dev phải có thể:

- build/test solution bằng .NET 10;
- cấu hình một raw-log source;
- chạy Worker với temporary/UAT folder;
- append một record hoàn chỉnh;
- thấy history MongoDB cùng source offsets;
- restart Worker và tiếp tục đúng checkpoint;
- quan sát lỗi parse/persistence mà không mất âm thầm.

## 1.1. Chiến lược triển khai: Phase 1A MVP trước

Team được phép triển khai trước một vertical slice nhỏ để test đường đi chính. Không cần chờ hoàn tất toàn bộ remote access, parser mở rộng, observability nâng cao hoặc packaging production.

### Contract cố định cho Phase 1A

- `SourceId` và `CompanyId` nằm trong cấu hình cục bộ của Worker.
- Phase 1A đọc local folder trên cùng máy trước.
- `RootPath` được thiết kế đủ tổng quát để sau này nhận Windows UNC share, ví dụ `\\RFID-SERVER\\RawData`.
- Ngày của record lấy từ folder `yyyy/MM/dd`; giờ lấy từ raw record và diễn giải theo timezone Việt Nam (`SE Asia Standard Time`). Không dùng file creation time/last-write time làm thời gian nghiệp vụ.
- File đã tồn tại tại thời điểm Worker khởi động lần đầu dùng `StartupExistingFilePolicy = End` để bỏ qua lịch sử cũ.
- File mới xuất hiện sau khi Worker đã chạy dùng `NewFilePolicy = Beginning`.
- MongoDB connection string lấy từ environment/secret configuration.
- MongoDB schema là source of truth; contract phải bổ sung `source.sourceId` và thông tin tenant `companyId` để không mất ngữ cảnh khi triển khai nhiều source/công ty.

### Không block Phase 1A

Các nội dung sau được triển khai sau khi vertical slice đầu tiên chạy được:

- remote Windows share và preflight kiểm tra server/share/quyền đọc;
- nhiều source hoặc nhiều công ty trong cùng một Worker;
- parser mở rộng cho toàn bộ block và numeric code chưa xác nhận;
- metrics/exporter/health nâng cao;
- Windows Service/container packaging và UAT production.

Phase 1A vẫn bắt buộc giữ các nguyên tắc correctness: byte offset, terminator `e(0)`, partial record không advance, deterministic identity, persistence trước checkpoint và restart không tạo duplicate.

## 2. Ranh giới Sprint

### Phase 1A MVP - triển khai trước

- Một configured source với `SourceId`, `CompanyId` và local `RootPath`.
- Solution foundation và project boundaries.
- Configuration validation cơ bản.
- File discovery/polling cho `yyyy/MM/dd/File_{FileId}.txt`.
- Byte-based tail reader và `e(0)` record framer.
- Minimal parser cho sample tag/business record.
- Canonical history/failure mapping tối thiểu.
- MongoDB history/failure/checkpoint persistence.
- Initial position `End` cho file hiện có và `Beginning` cho file mới.
- Deterministic `eventId`/`failureId` và restart/idempotency test.
- Unit/integration test chứng minh một record đi từ file tới MongoDB.

### Phase 1 hoàn chỉnh - mở rộng sau MVP

- Solution foundation và project boundaries.
- Configuration/validation cho source path, tenant và MongoDB.
- Raw file discovery và polling.
- Byte-based tail reader và `e(0)` record framer.
- Parser cho `@`, `b`, `t`, `te`, `sp`, `u`.
- Canonical history/failure mapping.
- MongoDB collections, indexes và checkpoint.
- Restart/idempotency/graceful shutdown.
- Structured logs, metrics và health state.
- Unit, integration, architecture và vertical-slice tests.
- Deployment/runbook tối thiểu.

Phase 1A không phải production acceptance. Chỉ sau khi các hạng mục Phase 1 hoàn chỉnh và UAT pass mới đánh giá Worker là sẵn sàng triển khai production.

### Chuyển sang Sprint 2

- ERP AppHub/SignalR client.
- `JoinMonitoring`, subscribe callback và reconnect/rejoin.
- Runtime callback catalog.
- Scanner lifecycle/snapshot.
- MVC Web và realtime/polling UI.
- Projection current state/session.

Sprint 1 không thêm package SignalR và không chờ Sprint 2 để hoàn tất đường raw file -> MongoDB.

## 3. Vai trò của các thành phần

| Thành phần | Vai trò | Không chịu trách nhiệm | Kết quả mong đợi |
|---|---|---|---|
| Domain | Canonical event, facts, source context và failure concepts | File I/O, MongoDB, hosting | Model ổn định, framework-independent |
| Application | Điều phối process record, event identity, persistence/checkpoint order | `FileStream`, BSON, Worker loop | Use case kiểm thử độc lập |
| RFID Raw Log Infrastructure | Discover/tail/frame/parse format legacy | Hosting và business projection | Complete `RawFileRecord` + parsed facts |
| MongoDB Infrastructure | Map/write/index/checkpoint | Source file reading | Persistence idempotent và checkpoint bền vững |
| Worker | DI, startup, polling, scheduling, shutdown | Parser/repository implementation | Executable chạy độc lập |
| Observability | Logs, metrics, health state | Thay đổi processing result | Phát hiện lag, lỗi và pipeline stop |
| Unit tests | Kiểm tra pure logic | Mongo/file thật | Fast regression suite |
| Integration tests | Kiểm tra file append/restart/Mongo | Physical RFID | Bằng chứng reliability |
| Architecture tests | Bảo vệ dependency rules | Functional correctness | Ngăn coupling phát sinh |
| End-to-end tests | Chứng minh vertical slice | Production acceptance | Một record quan sát được từ file tới Mongo |

## 4. Thứ tự triển khai tổng thể

```text
WP0 Contract gate + samples
        |
        v
WP1 Solution foundation
        |
        v
WP2 Configuration + source identity
        |
        v
WP3 File discovery + byte framing
        |
        v
WP4 Parser + canonical mapping
        |
        v
WP5 Mongo persistence + checkpoint
        |
        v
WP6 Worker orchestration + recovery
        |
        v
WP7 Observability + health
        |
        v
WP8 Tests + failure scenarios
        |
        v
WP9 Packaging + UAT handoff
```

Không đợi hoàn tất toàn bộ parser mới chứng minh persistence. Sau WP3, dùng một minimal record parser để dựng vertical slice sớm; tiếp tục enrich các block trong WP4.

## 5. Work Package 0 - Contract gate và sample corpus

### Mục tiêu

Khóa input/output tối thiểu trước khi code để tránh parser và schema phát triển theo suy đoán.

### Công việc

1. Chọn `2026-08-22-Db-Schema.md` làm schema source of truth.
2. Đã chốt cấu hình local cho `SourceId` và `CompanyId`. Đồng bộ `sourceId` trong history, failure và checkpoint contract để nhiều Antenna source không va chạm key; lưu `companyId` theo event/failure context để history vẫn giữ tenant ownership.
3. Thu sample đã redaction cho các nhóm:
   - tag read `@ + b + t`;
   - business event `@ + te`;
   - record có `sp`;
   - record có `u`;
   - record chứa nhiều block;
   - malformed complete record;
   - partial record chưa có `e(0)`.
4. Xác nhận file thực tế là UTF-8 và terminator `e(0)`.
5. Đã chốt folder date `yyyy/MM/dd` là nguồn ngày; dùng timezone Việt Nam cho raw time. Không dùng file metadata time làm thời gian nghiệp vụ.
6. Đã chốt Phase 1A đọc local folder; remote path dùng UNC share và preflight sẽ triển khai sau.
7. Đã chốt start policy Phase 1A: file hiện có lúc startup đọc từ `End`; file mới xuất hiện sau đó đọc từ `Beginning`.
8. Liệt kê numeric code chưa xác nhận trong `b(...)` và `te(...)`; parser phải giữ raw value.

### File/deliverable

```text
docs/contracts/rfid-raw-log-v1.md
docs/samples/*.txt
tests/fixtures/rfid-raw-log/*.txt
```

Sample không chứa token, password hoặc dữ liệu nhạy cảm chưa redaction.

### Kết quả mong đợi

- Có sample corpus dùng chung cho unit/integration tests.
- Dev biết field nào được parse, field nào giữ raw/null.
- Không còn mơ hồ về local path, timezone và initial position của Phase 1A.

### Acceptance

- Ít nhất một fixture cho tag read và một fixture cho business event.
- Có fixture partial và malformed.
- Schema/source evidence được link trong contract doc.
- Có config mẫu cho `SourceId`, `CompanyId`, local `RootPath`, `StartupExistingFilePolicy` và `NewFilePolicy`.

## 6. Work Package 1 - Solution foundation

### Mục tiêu

Tạo skeleton build được và khóa dependency boundaries.

### Công việc

1. Tạo solution/project:

```text
src/DeviceEventHistory.Domain
src/DeviceEventHistory.Application
src/DeviceEventHistory.Infrastructure
src/DeviceEventHistory.Worker
tests/DeviceEventHistory.UnitTests
tests/DeviceEventHistory.IntegrationTests
tests/DeviceEventHistory.ArchitectureTests
tests/DeviceEventHistory.EndToEndTests
```

2. Thiết lập:
   - .NET 10;
   - nullable enabled;
   - implicit usings theo convention;
   - warnings/analyzers;
   - central package management;
   - deterministic build;
   - test coverage/reporting cơ bản.
3. Thiết lập project references đúng architecture.
4. Thêm architecture test cho reference rules.
5. Tạo DI extension placeholders:

```csharp
services.AddDeviceEventHistoryApplication();
services.AddRfidRawLogIngestion(configuration);
services.AddDeviceEventHistoryMongoDb(configuration);
```

6. Tạo CI steps: restore, build, test.

### File chính

```text
DeviceEventHistory.sln
Directory.Build.props
Directory.Packages.props
src/*/*.csproj
tests/*/*.csproj
src/DeviceEventHistory.Worker/Program.cs
```

### Kết quả mong đợi

- Solution restore/build/test được bằng .NET 10.
- Dependency direction được architecture test bảo vệ.
- Worker chạy được ở trạng thái disabled/no source.

### Acceptance

- `dotnet build` pass với warnings policy đã chốt.
- Test projects chạy trong CI.
- Không reference source G-ERP/RFID/ERP.
- Không có SignalR package.

## 7. Work Package 2 - Configuration, source identity và options validation

### Mục tiêu

Worker biết đọc source nào, thuộc tenant nào và fail fast khi cấu hình sai.

### Công việc

1. Implement:

```text
RfidRawLogOptions
AntennaSourceOptions
MongoDbOptions
WorkerOptions
IngestionOptions
```

2. Validate:
   - `SourceId` required và unique;
   - `RootPath` absolute;
   - `CompanyId > 0` cho normal history;
   - timezone hợp lệ;
   - file pattern không cho path traversal;
   - buffer/max-record/concurrency > 0;
   - retention hợp lệ;
   - Mongo database/collection names hợp lệ.
3. Load Mongo connection string từ secret/environment.
4. Redact config khi log startup.
5. Implement `SourceDefinition` và `ConfigurationDeviceMetadataResolver` tối thiểu.

### File chính

```text
Infrastructure/RfidRawLog/Configuration/*.cs
Infrastructure/MongoDb/Configuration/*.cs
Application/Metadata/SourceDefinition.cs
Infrastructure/Metadata/ConfigurationDeviceMetadataResolver.cs
Worker/Configuration/*.cs
Worker/appsettings.json
```

### Kết quả mong đợi

- Worker fail fast nếu source/Mongo bắt buộc không hợp lệ.
- Log chỉ thể hiện có/không có secret, không ghi secret value.
- Mỗi event về sau gắn đúng stable `SourceId` và configured `CompanyId`.

### Acceptance

- Unit tests cho valid/invalid options.
- Duplicate SourceId bị từ chối.
- Relative hoặc unsafe root path bị từ chối.
- Missing connection string không bị log ra giá trị nhạy cảm.

## 8. Work Package 3 - File discovery, tail reader và record framer

### Mục tiêu

Đọc an toàn file đang được `RFID.Antenna` append và chỉ trả complete record.

### Thành phần

| Component | Vai trò |
|---|---|
| `RawLogFileDiscovery` | Tìm date folder và file theo poll |
| `RawLogPathParser` | Parse folder date và FileId |
| `RawLogTailReader` | Đọc byte từ checkpoint offset |
| `RawRecordFramer` | Chia byte stream theo `e(0)` |
| `FileReadSession` | Giữ state đọc của một file trong một poll/session |
| `FileRegistry` | Dedupe runtime state và per-file processing owner |
| `FairFileScheduler` | Chia lượt đọc theo byte/record/time budget |

### Công việc

1. Enumerate `yyyy/MM/dd` theo source timezone và lookback.
2. Chỉ nhận `File_{FileId}.txt`.
3. Dùng polling làm correctness mechanism.
4. Mở file read-only, share-compatible với writer.
5. Dùng `long` byte offset.
6. Đọc bounded chunk theo `ReadBufferBytes`.
7. Frame terminator bằng UTF-8 bytes của `e(0)`.
8. Trả:

```text
RawFileRecord
    SourceFileKey
    OffsetStart
    OffsetEnd
    RawBytes
    RawText
    ObservedFileLength
```

9. Hỗ trợ:
   - nhiều record trong một chunk;
   - terminator split qua chunk;
   - trailing newline;
   - partial record;
   - max record bytes;
   - empty file;
   - file tạm unavailable.
10. Detect `fileLength < checkpoint.position` và trả anomaly; không tự reset.
11. File còn backlog được requeue; file caught-up chờ poll, không chiếm consumer liên tục.
12. Actual file discovery dựa trên filesystem; danh sách DB/`ExpectedFileIds` chỉ dùng để health/reconciliation.

### File chính

```text
Infrastructure/RfidRawLog/Discovery/*.cs
Infrastructure/RfidRawLog/Reading/*.cs
Application/Ingestion/RawFileRecord.cs
Domain/Sources/*.cs
```

### Kết quả mong đợi

- Với file đang append, Worker chỉ trả record đã có `e(0)`.
- Offset phản ánh byte position thật.
- Không giữ lock cản `RFID.Antenna` ghi.
- Partial bytes không bị mất khi poll/restart.

### Acceptance

- Test terminator nằm giữa hai chunk.
- Test hai/mười record trong một chunk.
- Test UTF-8 multi-byte không làm lệch offset.
- Test partial record không tạo output và không advance.
- Test file truncate phát anomaly.
- Test một hot file không làm các file còn lại starvation.
- Test thêm `File_21.txt` ở runtime mà không restart Worker.

## 9. Work Package 4 - Parser và canonical mapping

### Mục tiêu

Chuyển complete raw record thành canonical history hoặc data failure, đồng thời giữ bằng chứng gốc.

### Thành phần

| Component | Vai trò |
|---|---|
| `BlockTokenizer` | Tách block name/raw arguments |
| `HeaderBlockParser` | Parse `@(...)` |
| `GateStateBlockParser` | Parse `b(...)` |
| `SignalBlockParser` | Parse `t(...)` |
| `BusinessEventBlockParser` | Parse `te(...)` |
| `StyleProcessBlockParser` | Parse `sp(...)` |
| `UserBlockParser` | Parse `u(...)` |
| `RfidRawRecordParser` | Điều phối tolerant parsing |
| `ProcessRawFileRecordHandler` | Map canonical event/failure |
| `EventIdentityFactory` | Tạo eventId/failureId deterministic |

### Công việc

1. Tokenizer không split ngây thơ theo dấu phẩy ngoài block boundary.
2. Parse block độc lập; warning một block không làm mất raw của block khác.
3. Map tối thiểu:
   - `@ + b + t` -> `tag_read`;
   - record có `te` -> `business_process`;
   - chưa đủ evidence -> `unknown`.
4. Map source context:
   - producer `RFID.Antenna`;
   - source kind `rfid_antenna_file`;
   - sourceId/fileId/folderDate/path/offsets.
5. Map tenant từ source configuration.
6. Kết hợp header time + folder date + source timezone khi đáng tin cậy.
7. Giữ `occurredAtUtc = null` nếu timestamp không xác định chắc chắn.
8. Giữ raw text và parser warnings/errors.
9. Không tự tạo EPC hoặc business semantics không có trong source.
10. Route complete malformed record sang `IngestionFailure`.

### File chính

```text
Infrastructure/RfidRawLog/Parsing/*.cs
Application/Parsing/*.cs
Application/Ingestion/ProcessRawFileRecordHandler.cs
Application/Ingestion/EventIdentityFactory.cs
Domain/Events/*.cs
Domain/Failures/*.cs
```

### Kết quả mong đợi

- Fixture tag/business tạo canonical event đúng schema.
- Unknown/optional block không crash Worker.
- Malformed complete record có failure code, raw payload và source offsets.
- eventId ổn định qua nhiều lần chạy.

### Acceptance

- Golden tests cho từng fixture.
- Culture test chạy giống nhau trên machine locale khác nhau.
- DeviceId/GateId/FileId không bị dùng thay nhau.
- Hai record cùng payload ở offset khác nhau có eventId khác nhau.
- Cùng record/source/offset có eventId giống nhau.

## 10. Work Package 5 - MongoDB persistence, indexes và checkpoint

### Mục tiêu

Đảm bảo history/failure bền vững và Worker restart tiếp tục đúng vị trí.

### Thành phần

| Component | Vai trò |
|---|---|
| `MongoDeviceEventHistoryWriter` | Ghi append-only history |
| `MongoIngestionFailureWriter` | Ghi failure deterministic |
| `MongoIngestionCheckpointStore` | Load/advance checkpoint |
| `MongoIndexInitializer` | Tạo collection/index idempotent |
| Document mappers | Tách Domain/Application khỏi BSON |

### Công việc

1. Implement document mapping theo schema V1.
2. Tạo collections:
   - `device_event_history`;
   - `ingestion_failures`;
   - `ingestion_checkpoints`.
3. Tạo unique eventId/failureId/checkpoint key.
4. Tạo query/source indexes Sprint 1.
5. Tạo TTL theo `expireAtUtc` nếu retention đã chốt.
6. Implement retry cho transient Mongo errors với giới hạn/backoff.
7. Duplicate key cùng eventId được xem là idempotent retry outcome.
8. Checkpoint advance dùng compare-and-set/version.
9. Thực thi thứ tự:

```text
persist history/failure -> confirm -> advance checkpoint
```

10. Không xóa/sửa history khi checkpoint write fail.

### File chính

```text
Infrastructure/MongoDb/Documents/*.cs
Infrastructure/MongoDb/Mapping/*.cs
Infrastructure/MongoDb/Stores/*.cs
Infrastructure/MongoDb/Indexes/MongoIndexInitializer.cs
Application/Abstractions/Persistence/*.cs
Application/Checkpoints/*.cs
```

### Kết quả mong đợi

- Event hợp lệ và failure được lưu đúng collection.
- Checkpoint cho biết byte offset bền vững của từng source/date/file.
- Crash sau history write/trước checkpoint không nhân đôi history.

### Acceptance

- Integration test với MongoDB thật/test container.
- Index initializer chạy lặp lại không lỗi.
- Duplicate eventId test pass.
- Mongo unavailable không advance checkpoint.
- Checkpoint version conflict không bị ghi đè âm thầm.

## 11. Work Package 6 - Worker orchestration, scheduling và recovery

### Mục tiêu

Ghép các component thành Worker Service hoạt động ổn định và có shutdown/restart semantics rõ ràng.

### Thành phần

| Component | Vai trò |
|---|---|
| `StartupInitializationHostedService` | Validate dependency/index trước ingest |
| `RawLogIngestionHostedService` | Main background loop |
| `SourcePollingCoordinator` | Poll nhiều configured source |
| `FairFileScheduler` | Giới hạn concurrency và chia turn công bằng |
| `FileRegistry` | Giữ logical cursor state và ngăn schedule trùng file |
| `GracefulShutdownCoordinator` | Dừng an toàn trong timeout |

### Công việc

1. Startup:

```text
validate options
 -> verify source roots
 -> ping MongoDB
 -> initialize indexes
 -> start ingestion
```

2. Poll enabled sources theo interval.
3. Không schedule trùng file đang active.
4. Xử lý tuần tự trong một file, song song có giới hạn giữa nhiều file.
5. Truyền cancellation token xuống toàn pipeline.
6. Retry source access/Mongo lỗi theo policy.
7. Dừng file riêng khi phát hiện truncation; không làm mất diagnostic.
8. Graceful shutdown:
   - ngừng poll mới;
   - cho record đang persist hoàn tất;
   - chỉ commit confirmed outcome;
   - log remaining work.
9. Bảo đảm một Worker owner trong Sprint 1 bằng deployment configuration; phát hiện nhiều instance qua operational metric/log nếu có thể.
10. Với 20 files, chỉ giữ 20 logical states/checkpoints; số consumer lấy từ `MaxConcurrentFiles`, không tạo bắt buộc một thread/file.

### File chính

```text
Worker/HostedServices/*.cs
Worker/Orchestration/*.cs
Worker/Program.cs
Infrastructure/DependencyInjection.cs
```

### Kết quả mong đợi

- Worker chạy liên tục và xử lý công bằng nhiều files.
- Restart tiếp tục checkpoint.
- Shutdown không checkpoint quá sớm.
- Source lỗi không làm process crash loop không kiểm soát.

### Acceptance

- Integration test restart.
- Integration test cancellation khi đang chờ poll.
- Test shutdown khi đang persist.
- Test nhiều file và `MaxConcurrentFiles`.
- Test source path tạm unavailable rồi phục hồi.

## 12. Work Package 7 - Observability và health

### Mục tiêu

Vận hành có thể biết Worker đang đọc file nào, đến offset nào và đang lỗi ở đâu mà không cần mở debugger.

### Công việc

1. Structured logging scopes:

```text
WorkerId, SourceId, FolderDate, FileId,
RelativePath, OffsetStart, OffsetEnd,
EventId/FailureId, Attempt, Duration, Result
```

2. Metrics:
   - files discovered/active;
   - bytes/records read;
   - partial records;
   - parse success/warning/failure;
   - history/failure writes;
   - duplicate eventId;
   - checkpoint position/failure;
   - persistence latency/retry;
   - ingestion lag;
   - source unavailable/truncated.
3. Health state:
   - live;
   - ready;
   - degraded;
   - unhealthy.
4. Không log raw payload mặc định.
5. Tạo startup summary đã redaction.
6. Chốt exporter/sink theo hạ tầng UAT; code giữ abstraction.

### File chính

```text
Infrastructure/Observability/*.cs
Worker/HealthChecks/*.cs
Worker/Configuration/ConfigurationRedactor.cs
```

### Kết quả mong đợi

- Operator phân biệt được không có event với không đọc được source.
- Có cảnh báo khi checkpoint không tiến triển, Mongo chậm hoặc file bị truncate.
- Không rò secret/raw payload không cần thiết.

### Acceptance

- Test log redaction.
- Test state transitions ready -> degraded -> unhealthy.
- Metrics có source/file labels với cardinality được kiểm soát.
- Không dùng full raw path/eventId làm metric label cardinality cao; để chúng trong structured log.

## 13. Work Package 8 - Test suite và failure scenarios

### Mục tiêu

Chứng minh correctness và recovery trước UAT.

### Test matrix bắt buộc

| Scenario | Expected result |
|---|---|
| Complete record | History + checkpoint advance |
| Partial record | Không history/failure, checkpoint giữ nguyên |
| Partial sau đó complete | Một history, checkpoint đúng end offset |
| Multiple records | Xử lý đúng thứ tự, checkpoint tới contiguous end |
| Malformed complete record | Failure + checkpoint advance |
| Unknown block | Raw giữ nguyên, warning; không crash |
| Duplicate retry | Không duplicate history |
| Crash sau history/trước checkpoint | Restart duplicate-safe rồi advance |
| Mongo unavailable | Retry/degraded, checkpoint không advance |
| Checkpoint conflict | Không overwrite, dừng/reload theo policy |
| File truncated | Stop file + unhealthy/alert |
| Date rollover | File ngày mới và leftover ngày cũ đều được đọc |
| UTF-8 split boundary | Raw text/offset không hỏng |
| Oversized record | Bounded failure/alert, không tăng RAM vô hạn |
| Graceful shutdown | Không mất confirmed checkpoint semantics |
| 20 files cùng append | Cursor độc lập, concurrency đúng giới hạn |
| Một hot file + 19 files nhỏ | Không starvation; scheduler requeue công bằng |
| File_21 xuất hiện runtime | Discover/checkpoint/ingest không restart |

### Công việc

1. Unit tests cho pure functions/parser/options/identity.
2. Integration fixture writer mô phỏng append giống `RFID.Antenna`:

```text
UTF8.GetBytes($"{record}e(0){Environment.NewLine}")
```

3. Mongo integration bằng isolated database/container.
4. Architecture tests chạy trong CI.
5. End-to-end test tạo temp folder, append file và query Mongo.
6. Failure injection cho Mongo và shutdown.
7. Báo cáo test tách rõ static/fixture/UAT/physical evidence.

### Kết quả mong đợi

- Regression suite lặp lại được.
- Có bằng chứng Worker không checkpoint quá sớm.
- Có bằng chứng restart không tạo duplicate.
- Known gaps được ghi rõ thay vì bị che bởi mock.

### Acceptance

- Toàn bộ mandatory test matrix pass.
- Test không phụ thuộc thứ tự hoặc shared database state.
- CI fail khi architecture boundary bị phá.

## 14. Work Package 9 - Packaging, UAT và operations handoff

### Mục tiêu

Đưa Worker vào môi trường có raw folder thật một cách kiểm soát và rollback được.

### Công việc

1. Chuẩn bị:
   - Windows Service package hoặc container image;
   - environment config template;
   - read-only mount/share instruction;
   - secret injection;
   - Mongo initialization role/runtime role.
2. Runbooks:

```text
docs/operations/configure-source.md
docs/operations/checkpoint-recovery.md
docs/operations/file-truncation.md
docs/operations/mongodb-incident.md
docs/operations/backfill-and-replay.md
docs/operations/rollback.md
```

3. UAT rollout:
   - deploy `Enabled=false`;
   - validate options/path/Mongo;
   - choose initial position/backfill window;
   - enable đúng một Worker;
   - observe one complete record;
   - compare raw bytes, offsets, eventId và Mongo document;
   - restart Worker;
   - append record mới và xác nhận continuation.
4. Chạy failure drill Mongo unavailable ngắn có kiểm soát.
5. Ghi UAT acceptance report.

### Kết quả mong đợi

- Worker deploy/restart/rollback được.
- Operator biết xử lý source unavailable, checkpoint và Mongo incident.
- Có evidence một record thật hoặc simulator record đi từ raw file tới MongoDB.

### Acceptance

- Source folder chỉ được mount/cấp quyền read-only cho Worker.
- Không thay đổi file RFID.Antenna.
- Checkpoint đối chiếu đúng byte offset.
- UAT report phân biệt simulator và physical reader.

## 15. Milestone theo vertical slice

### Milestone 0 - Phase 1A MVP kickoff

Mục tiêu là tạo bản Worker nhỏ nhất nhưng có đường dữ liệu thật từ raw file tới MongoDB.

Phạm vi:

- một source local;
- `SourceId`/`CompanyId` từ local configuration;
- file hiện có bắt đầu ở cuối file;
- file mới bắt đầu từ đầu file;
- frame record bằng `e(0)`;
- minimal parser cho fixture tag/business;
- history/failure/checkpoint persistence;
- restart và duplicate-safe test.

Acceptance:

- append một record mới vào `File_1.txt`;
- Worker đọc được đúng một complete record;
- MongoDB có document history hoặc failure tương ứng;
- checkpoint tiến tới `offsetEnd` sau persistence;
- restart Worker không tạo duplicate;
- record chưa có `e(0)` không bị checkpoint;
- raw payload, `SourceId`, `CompanyId`, `FileId`, `DeviceId` và `GateId` được giữ đúng ngữ nghĩa.

### Milestone 1 - Buildable skeleton

Bao gồm WP0-WP2.

Kết quả:

- solution build/test;
- options validated;
- sample corpus sẵn sàng;
- architecture rules được khóa.

### Milestone 2 - One record end-to-end

Bao gồm minimal WP3-WP5.

Kết quả:

- append một record fixture;
- frame đến `e(0)`;
- parse header/basic facts;
- ghi history;
- ghi checkpoint.

Đây là milestone ưu tiên cao nhất vì chứng minh đường observable raw file -> MongoDB.

Milestone 0 và Milestone 2 có thể được thực hiện liên tiếp trong một nhánh MVP; không cần chờ hoàn tất parser đầy đủ hoặc remote deployment.

### Milestone 3 - Complete Sprint 1 parser and recovery

Bao gồm phần còn lại WP4-WP6.

Kết quả:

- tất cả block V1;
- malformed -> failure;
- restart/duplicate/retry/truncation behavior.

### Milestone 4 - Operational readiness

Bao gồm WP7-WP9.

Kết quả:

- observability;
- mandatory failure tests;
- package/runbook/UAT acceptance.

## 16. Backlog theo project

### DeviceEventHistory.Domain

- [ ] Event/source/facts/parse models.
- [ ] Failure model và failure codes.
- [ ] Source file key và offset range.
- [ ] Domain invariants/tests.

### DeviceEventHistory.Application

- [ ] RawFileRecord/context.
- [ ] Parser/file/persistence/checkpoint abstractions.
- [ ] ProcessRawFileRecordHandler.
- [ ] EventIdentityFactory.
- [ ] Checkpoint decision/CAS contract.
- [ ] Source/tenant resolution contract.
- [ ] Application unit tests.

### DeviceEventHistory.Infrastructure

- [ ] Options + validators.
- [ ] File discovery/path parser.
- [ ] Tail reader/framer.
- [ ] Block tokenizer/parsers.
- [ ] Mongo document mappings.
- [ ] History/failure/checkpoint stores.
- [ ] Index initializer.
- [ ] Telemetry/health implementation.
- [ ] Infrastructure integration tests.

### DeviceEventHistory.Worker

- [ ] Program/DI/configuration.
- [ ] Startup initialization.
- [ ] RawLogIngestionHostedService.
- [ ] Polling/scheduler/shutdown.
- [ ] Health checks.
- [ ] Service/container packaging.

### Tests and docs

- [ ] Fixture corpus.
- [ ] Unit test suite.
- [ ] Mongo/file integration suite.
- [ ] Architecture tests.
- [ ] End-to-end vertical slice.
- [ ] Operations runbooks.
- [ ] UAT report template.

## 17. Dependency và khả năng làm song song

Có thể làm song song sau WP1:

- Team A: options/source discovery/framing.
- Team B: Domain/Application canonical models và event identity.
- Team C: Mongo document/store/index với fixed fixture model.
- Team D: test harness, temporary writer và Mongo environment.

Điểm đồng bộ bắt buộc:

1. WP0 khóa sample/schema trước parser implementation lớn.
2. `RawFileRecord` contract phải chốt trước khi nối WP3-WP4.
3. Canonical event/failure contract phải chốt trước Mongo mapper.
4. Checkpoint write order phải có integration test trước performance batching.
5. Không bắt đầu UAT trước mandatory recovery matrix.

## 18. Rủi ro và biện pháp

| Rủi ro | Ảnh hưởng | Biện pháp Sprint 1 |
|---|---|---|
| Worker không nhìn thấy raw folder production | Không ingest | Xác nhận topology/path/permission ở WP0 và preflight |
| Share/mirror replace file thay vì append | Offset sai/truncation | Detect length regression, stop and alert |
| Terminator split qua chunk | Mất/cắt record | Byte framer + split-boundary tests |
| UTF-8 byte/char offset lệch | Duplicate/mất record | Dùng long byte offset, không dùng string length |
| Crash giữa history và checkpoint | Đọc lại | Unique deterministic eventId + history-first protocol |
| MongoDB chậm/unavailable | Backlog/lag | Bounded retry, health, checkpoint giữ nguyên |
| Numeric semantics chưa rõ | Facts sai | Giữ raw value/null, parser warning, không suy đoán |
| Initial position sai | Backfill quá lớn hoặc bỏ lịch sử | Explicit start policy và UAT approval |
| Nhiều Worker cùng đọc | Duplicate load/checkpoint conflict | Một owner Sprint 1 + CAS/version detection |
| FileId bị hiểu là device | Query sai | Source model tách FileId/DeviceId/GateId và tests |
| Tối ưu batch quá sớm | Nhảy checkpoint/lỗi recovery | Vertical slice tuần tự trước, benchmark sau |

## 19. Definition of Done Phase 1A MVP

Phase 1A được xem là hoàn thành để team bắt đầu test khi đạt các điều kiện sau:

### Contract và configuration

- [ ] Một source local được cấu hình bằng `SourceId`, `CompanyId`, `RootPath` và timezone Việt Nam.
- [ ] MongoDB connection string không nằm trong source hoặc log.
- [ ] Schema/document contract có `SourceId` và `CompanyId` ở vị trí thống nhất.
- [ ] Existing-file start policy là `End`; new-file policy là `Beginning`.

### File và persistence

- [ ] Worker discover đúng `yyyy/MM/dd/File_{FileId}.txt`.
- [ ] File được mở read-only bằng byte offset `long`.
- [ ] Chỉ record có `e(0)` mới được xử lý.
- [ ] Partial record không advance checkpoint.
- [ ] History/failure được ghi trước checkpoint.
- [ ] Restart không tạo duplicate nhờ deterministic identity.

### MVP evidence

- [ ] Có fixture tag/business đã redaction.
- [ ] Có test append một record mới và thấy record trong MongoDB.
- [ ] Có test malformed/partial record.
- [ ] Có test MongoDB persistence failure không advance checkpoint.
- [ ] Có báo cáo rõ đây là fixture/test evidence, chưa phải UAT hoặc physical-device acceptance.

## 20. Definition of Done Sprint 1 đầy đủ

Sprint 1 hoàn thành khi tất cả điều kiện sau đạt:

### Code và architecture

- [ ] .NET 10 solution build/test pass.
- [ ] Project dependencies đúng architecture.
- [ ] Không có AppHub/SignalR dependency.
- [ ] Configuration validate và secret không nằm trong source/log.

### File ingestion

- [ ] Discover đúng `yyyy/MM/dd/File_{FileId}.txt`.
- [ ] Đọc read-only bằng long byte offset.
- [ ] Frame đúng complete record `e(0)`.
- [ ] Partial record không advance checkpoint.
- [ ] UTF-8/chunk boundary tests pass.

### Parsing và schema

- [ ] Parser hỗ trợ `@`, `b`, `t`, `te`, `sp`, `u` theo evidence.
- [ ] Raw payload được giữ nguyên.
- [ ] Facts không có bằng chứng để null/omit.
- [ ] FileId, DeviceId và GateId tách đúng semantics.
- [ ] Deterministic eventId/failureId.

### Persistence và recovery

- [ ] Ba collections Sprint 1 được initialize.
- [ ] Unique/query/TTL indexes theo contract.
- [ ] History/failure persist trước checkpoint.
- [ ] Restart tiếp tục đúng offset.
- [ ] Crash-after-history test không nhân đôi event.
- [ ] Mongo failure không advance checkpoint.
- [ ] File truncation không reset âm thầm.

### Operations

- [ ] Structured logs/metrics/health hoạt động.
- [ ] Graceful shutdown semantics đã test.
- [ ] Một ingestion owner được cấu hình.
- [ ] Runbooks và UAT report có sẵn.
- [ ] Có vertical-slice evidence raw file -> MongoDB.
- [ ] Evidence ghi rõ fixture, simulator, UAT hay physical source.

## 21. Sản phẩm bàn giao Sprint 1

1. Repository/solution .NET 10.
2. `DeviceEventHistory.Worker` executable.
3. RFID raw-log file adapter.
4. Tolerant raw record parser.
5. MongoDB history/failure/checkpoint persistence.
6. Index initializer và retention configuration.
7. Unit/integration/architecture/end-to-end test suites.
8. Docker/Windows Service packaging theo môi trường chọn.
9. Configuration templates không chứa secret.
10. Metrics, health và log convention.
11. Fixture/sample contract đã redaction.
12. Operations runbooks.
13. UAT acceptance report.

## 22. Handoff sang Sprint 2

Sprint 2 bổ sung AppHub adapter nhưng tái sử dụng:

- canonical history/failure writers;
- event identity abstraction;
- tenant/device resolution;
- MongoDB index/retention setup;
- telemetry conventions;
- application processing boundary.

Sprint 2 không dùng lại file checkpoint cho SignalR. Nó phải có connection/reconnect/rejoin state riêng và raw SignalR envelope riêng.

Sprint 2 chỉ bắt đầu sau khi Sprint 1 đã chứng minh raw file -> MongoDB, vì đường file là nền tảng bền vững để đối chiếu các callback realtime về sau.
