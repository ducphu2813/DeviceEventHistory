# Device Event Statistics - Design Sprint 3

## 1. Trạng thái và phạm vi

- Trạng thái: thiết kế trước implementation plan.
- Mục tiêu: tạo statistics read model theo device/day từ MongoDB Device Event History.
- Runtime mới: `.NET 10` `DeviceEventStatistics.Worker`.
- Target database: SQL Server hiện có của hệ thống Report ERP, sử dụng schema mặc định `dbo`.
- SQL contract: `Sprint-3-Schema.md`.
- Source Mongo contract: `Sprint-2-Db-Schema.md`.
- Expected volume ban đầu: khoảng 100.000 history events/ngày, có burst và backlog sau downtime.
- Scheduling: Worker tự quản lý polling, reconciliation và backfill lifecycle; Sprint 3 không dùng Hangfire.

Tài liệu này chốt:

- system boundary và pattern;
- cách đọc MongoDB mà không ảnh hưởng raw-log/AppHub ingestion;
- incremental cursor, batching, idempotency và SQL transaction;
- daily aggregation, state duration, late event và reconciliation;
- failure isolation, recovery, observability, security và testing.

## 2. Mục tiêu Sprint 3

Sprint 3 tạo một pipeline độc lập:

```text
MongoDB device_event_history
    -> incremental read
    -> statistics classification
    -> daily aggregate/state projection
    -> SQL Server dbo
```

Kết quả mong muốn:

- Có số lần từng metric/event theo device/day.
- Có daily opening/closing state và online/offline/unknown duration.
- Có dữ liệu nền để query chart theo ngày/tuần/tháng.
- Có health result versioned khi health rules được khóa.
- Retry/restart không làm cộng trùng.
- Late event cập nhật đúng ngày nghiệp vụ.
- Statistics SQL failure không làm chậm hoặc dừng raw-log/AppHub ingestion.
- Projection có thể reconcile, backfill và rebuild từ MongoDB.

## 3. Ngoài phạm vi

- Thay đổi raw-log reader, parser, checkpoint hoặc AppHub callback pipeline.
- Ghi SQL trực tiếp trong `DeviceEventHistory.Worker`.
- API/dashboard/chart UI.
- Thay MongoDB history bằng SQL Server.
- Cross-source physical-event deduplication khi chưa có producer event ID chung.
- Kafka/RabbitMQ/message broker.
- MongoDB change stream ở Sprint 3 MVP.
- Active-active partitioned statistics workers.
- Machine learning/anomaly model.
- Tự suy health khi chưa có operating schedule/rule evidence.
- Hangfire, Hangfire storage, recurring jobs hoặc Hangfire dashboard.

## 4. Quyết định kiến trúc

### 4.1. Pattern

Sprint 3 dùng:

```text
CQRS read model
    + asynchronous materialized projection
    + incremental micro-batch
    + idempotent consumer / transactional inbox
    + source cursor checkpoint
    + periodic reconciliation
    + versioned rebuild
```

MongoDB là write/history model. SQL Server là analytics read model.

### 4.2. Process boundary

Tạo executable/deployment riêng:

```text
DeviceEventHistory.Worker       // raw-log + AppHub -> MongoDB
DeviceEventStatistics.Worker    // MongoDB -> SQL Server statistics
```

Hai Worker không gọi trực tiếp nhau. Boundary duy nhất là committed MongoDB history contract.

### 4.3. Consistency

- Ingestion history: giữ semantics hiện tại.
- Statistics: eventual consistency.
- Target lag ban đầu: cấu hình theo giây/phút, không yêu cầu sub-second.
- SQL projection: at-least-once delivery + idempotent transactional persistence.
- Không dùng distributed transaction MongoDB/SQL Server.

### 4.4. Scheduling

Không dùng Hangfire.

- Incremental loop dùng `BackgroundService` + `TimeProvider`/cancellable delay.
- Rolling reconciliation dùng hosted service/coordinator riêng với lịch cấu hình.
- Missed scheduled run được phát hiện từ `ProjectionRun`, không phụ thuộc in-memory timer.
- Backfill/rebuild chạy bằng explicit Worker mode/command/configuration, không dùng job dashboard.

## 5. System context

```text
RFID.Antenna raw files -------------------+
                                           |
ERP AppHub Monitoring --------------------+--> DeviceEventHistory.Worker
                                                      |
                                                      v
                                           MongoDB device_event_history
                                                      |
                                            read-only | incremental/reconcile
                                                      v
                                      DeviceEventStatistics.Worker
                                      +-----------------------------+
                                      | history reader              |
                                      | eligibility/ownership       |
                                      | metric mapper               |
                                      | timezone/day bucketing      |
                                      | state transition projector  |
                                      | health evaluator            |
                                      | SQL transaction/checkpoint  |
                                      +--------------+--------------+
                                                     |
                                                     v
                                           SQL Server dbo
                                                     |
                                                     v
                                         future Report API / charts
```

Failure isolation:

```text
SQL unavailable
    -> Statistics Worker retry/backoff/lag
    -> Mongo history continues growing
    -> raw-log checkpoint continues
    -> AppHub callback processing continues
```

## 6. Solution structure

Đề xuất thêm bốn projects trong solution hiện tại:

```text
src/
  DeviceEventStatistics.Domain/
  DeviceEventStatistics.Application/
  DeviceEventStatistics.Infrastructure/
  DeviceEventStatistics.Worker/

tests/
  DeviceEventStatistics.UnitTests/
  DeviceEventStatistics.IntegrationTests/
  DeviceEventStatistics.ArchitectureTests/
```

Dependency direction:

```text
DeviceEventStatistics.Domain
            ^
            |
DeviceEventStatistics.Application
            ^
            |
DeviceEventStatistics.Infrastructure
            ^
            |
DeviceEventStatistics.Worker
```

Rules:

- Statistics projects không reference `DeviceEventHistory.Worker`.
- Domain không reference MongoDB Driver, SQL client, hosting hoặc configuration.
- Application không chứa BSON/SQL-specific types.
- MongoDB Driver và `Microsoft.Data.SqlClient` chỉ ở Infrastructure.
- Worker chỉ compose DI, options, hosted services và health checks.
- Không copy toàn bộ Mongo document vào statistics Domain; chỉ dùng minimal projection input contract.
- Shared canonical constants chỉ reuse qua stable contract/project nếu không tạo dependency ngược; không reference ingestion Infrastructure.

## 7. Component design

```text
IncrementalProjectionHostedService
    -> ProjectionLeaseManager
    -> ProjectionCheckpointReader
    -> MongoHistoryEventReader
    -> StatisticsProjectionPipeline
         -> HistoryEventEligibilityPolicy
         -> EventOwnershipPolicy
         -> DeviceMetadataResolver
         -> StatisticsDateResolver
         -> DeviceMetricMapperRegistry
         -> DeviceStateTransitionProjector
         -> DeviceHealthEvaluator
    -> SqlStatisticsBatchWriter
         -> ProcessedEvent gate
         -> daily aggregate/state writes
         -> projection failure writes
         -> checkpoint advance

ReconciliationHostedService
    -> ReconciliationCoordinator
    -> MongoHistoryRangeReader
    -> same mapping/state rules
    -> exact recompute
    -> atomic range replacement
```

### 7.1. `MongoHistoryEventReader`

Trách nhiệm:

- đọc `device_event_history` theo cursor;
- project đúng các field cần thiết;
- sort ổn định;
- không deserialize raw payload khi không cần;
- không mutate Mongo documents;
- giới hạn batch size và cancellation.

Không thực hiện SQL write hoặc health scoring.

### 7.2. `HistoryEventEligibilityPolicy`

Kiểm tra:

- supported `schemaVersion`;
- positive `companyId` và `device.id` khi metric cần device;
- `timelineAtUtc`, `persistedAtUtc`, `eventId` hợp lệ;
- parse status;
- category/source/event contract.

Trả outcome có chủ đích: eligible, ignored, quality-only hoặc projection failure. Không throw cho known data variant.

### 7.3. `EventOwnershipPolicy`

Chọn primary source cho từng event family để tránh cộng chéo raw-log/AppHub.

Policy phải:

- versioned;
- test bằng fixture;
- observable khi ignore secondary source;
- không fuzzy deduplicate bằng time/payload/device;
- có thể giữ source-specific daily row cho shadow analysis nhưng không đưa vào global health khi chưa approved.

### 7.4. `StatisticsDateResolver`

Nhận `timelineAtUtc` và resolved timezone, trả:

```text
StatisticsDate
BucketStartAtUtc
BucketEndAtUtc
TimeZoneId
```

Không fallback sang machine local timezone.

### 7.5. `DeviceMetricMapperRegistry`

Dispatch theo stable mapping key:

```text
sourceKind + category + source.eventName + confirmed facts discriminator
```

Một source event có thể tạo:

- zero metric contribution: ignored/quality-only;
- một metric contribution;
- nhiều metric contributions nếu một canonical event chứa nhiều confirmed facts có semantics độc lập.

Mỗi contribution chứa:

```text
MetricCode
CompanyId
DeviceId
StatisticsDate
SourceKind
CountDelta
TimelineAtUtc
TimeBasis
ParseStatus
```

Không để SQL schema quyết định business mapping bằng dynamic expression. `MetricDefinition` cung cấp registry/display; mapping logic được version trong code và test.

### 7.6. `DeviceStateTransitionProjector`

Xử lý ordered connection/state event:

- load `DeviceStateCursor` trong transaction;
- nhận transition theo `timelineAtUtc + eventId`;
- đóng/mở interval;
- split interval qua business-day boundary;
- update daily online/offline/unknown duration;
- giữ repeated observation count nhưng không tạo duration interval sai;
- đánh dấu reconciliation khi gặp out-of-order event.

### 7.7. `DeviceHealthEvaluator`

Health evaluator là stage sau event count/state facts.

- input chỉ là approved daily facts, device schedule và rule configuration;
- output: status, score, rule version và reason codes;
- không đọc raw payload;
- không trả score nếu required evidence chưa đủ;
- rule change kích hoạt reconciliation/rebuild có audit.

### 7.8. `SqlStatisticsBatchWriter`

Sở hữu một SQL transaction cho contiguous batch:

- processed-event insert/idempotency;
- grouped daily aggregate update;
- state cursor/snapshot update;
- projection failure/quality write;
- projection checkpoint advance.

Không mở transaction trước Mongo read. Transaction chỉ bao quanh SQL work và phải ngắn, bounded.

## 8. Runtime modes

Worker hỗ trợ các mode rõ ràng:

| Mode | Vai trò |
|---|---|
| `incremental` | chạy liên tục từ projection checkpoint |
| `reconciliation` | recompute rolling/recent date range |
| `backfill` | dựng statistics cho explicit historical range |
| `rebuild` | dựng projection version mới từ đầu |

Production service mặc định chạy incremental và internal reconciliation scheduler. Backfill/rebuild phải được operator kích hoạt rõ bằng startup mode/configuration; không tự chạy toàn history khi config sai.

## 9. Startup flow

```text
Host.CreateApplicationBuilder
    -> bind + ValidateOnStart options
    -> register statistics components
    -> build host
    -> log redacted configuration summary
    -> verify Mongo connectivity/read permission
    -> verify SQL connectivity
    -> verify expected dbo schema version
    -> load/validate MetricDefinition registry
    -> acquire projection lease
    -> load/create ProjectionCheckpoint
    -> start incremental loop
    -> start reconciliation scheduler if enabled
```

Fail-fast/not-ready conditions:

- invalid connection/configuration;
- target SQL schema missing/incompatible;
- duplicate metric code/mapping;
- active projection version invalid;
- timezone resolver configuration invalid;
- Mongo query index contract missing theo deployment policy;
- lease already owned bởi healthy active instance.

Statistics disabled phải exit/idle rõ ràng, không busy-loop.

## 10. Incremental source cursor

Cursor:

```text
LastPersistedAtUtc + LastEventId
```

Mongo query:

Để loại trừ rủi ro mất sự kiện do lệch commit giữa các luồng ghi song song (Commit Skew / Clock Skew), Reader áp dụng chiến lược **Bounded Overlap Window**:

```text
fetchStartAtUtc = lastPersistedAtUtc - OverlapWindow (default 5m)

persistedAtUtc >= fetchStartAtUtc
ORDER BY persistedAtUtc ASC, eventId ASC
LIMIT BatchSize
```

Required Mongo index:

```text
persistedAtUtc ASC, eventId ASC
```

Rules:

- `persistedAtUtc` là ingestion insertion time/cursor, không phải statistics day.
- `timelineAtUtc` quyết định business ordering/date.
- Canonical `eventId` phải normalized lowercase hex và compared ordinal.
- Tính toàn vẹn Exactly-Once được bảo đảm tại tầng SQL thông qua bảng `ProcessedEvent` (loại bỏ các event đọc lặp lại trong Overlap Window).
- Cursor chỉ advance qua contiguous batch có terminal SQL outcome.
- V1 historical document thiếu cursor field đi explicit backfill/migration path, không làm normal loop đoán.
- Operator không sửa checkpoint bằng cách tùy tiện; recovery procedure phải ghi audit.

## 11. Incremental loop

Pseudo flow:

```text
while not cancelled
    renew/acquire lease (nhận CurrentEpoch)
    checkpoint = load checkpoint
    
    // Bounded Overlap Read
    fetchStart = checkpoint.LastPersistedAtUtc - OverlapWindow
    batch = read Mongo where persistedAtUtc >= fetchStart, bounded by BatchSize

    if batch empty
        record caught-up/lag
        wait PollInterval
        continue

    projectionBatch = []
    for event in batch source order
        validate minimal contract
        classify eligibility/ownership
        resolve device metadata/timezone
        map metric contributions
        prepare state transition or projection failure
        append terminal event outcome

    begin SQL transaction
        verify LeaseOwner == CurrentOwner AND LeaseEpoch == CurrentEpoch AND unexpired
        idempotency gate by ProcessedEvent (bỏ qua event đã ghi)
        apply only newly inserted event outcomes
        update grouped daily facts
        apply ordered state changes (hoặc enqueue ReconciliationRequest nếu out-of-order)
        update daily snapshots/quality/failures
        advance checkpoint to last event in source batch
    commit

    record metrics
    if batch was full
        immediately read next batch
    else
        short/caught-up delay
```

No unbounded in-memory queue. Memory is bounded by `BatchSize`, mapped contribution limit và maximum device/state groups per batch.

## 12. SQL batch protocol

Không thực hiện một SQL round trip cho mỗi event ở production path.

Recommended implementation:

- convert batch thành table-valued parameters hoặc equivalent structured parameters;
- transaction-scoped repository/stored procedure;
- insert new processed IDs và capture inserted IDs;
- chỉ aggregate contributions của IDs vừa insert;
- group deltas theo daily business identity;
- `UPDATE` existing rows, sau đó `INSERT` missing rows dưới transaction-scoped application lock;
- update checkpoint cuối transaction.

Correctness invariant:

```text
ProcessedEvent
Daily aggregates
Daily snapshot/state cursor
ProjectionFailure/Quality
ProjectionCheckpoint

commit hoặc rollback cùng nhau
```

Không dùng SQL checkpoint làm ingestion checkpoint và không update Mongo trong transaction.

## 13. Crash và retry semantics

### Crash trước SQL transaction

- checkpoint cũ;
- batch được đọc lại;
- chưa có SQL effect.

### Crash trong SQL transaction

- SQL rollback;
- checkpoint cũ;
- batch được đọc lại.

### Crash sau SQL commit

- processed-event, facts và checkpoint đã commit cùng nhau;
- restart đọc sau checkpoint.

### Ambiguous client result sau SQL commit

- nếu client không biết commit thành công hay chưa, retry batch;
- unique `ProcessedEvent` biến delivery lại thành idempotent duplicate;
- daily count không tăng lần hai.

### SQL unavailable

- retry capped exponential backoff + jitter;
- không advance checkpoint;
- không consume memory vô hạn;
- projection lag tăng nhưng ingestion không bị ảnh hưởng.

### Mongo unavailable

- giữ SQL checkpoint;
- retry read;
- không tạo empty/fake statistics.

## 14. Data failure và poison event

Phân biệt:

### Terminal data/contract failure

Ví dụ:

- invalid event ID;
- missing tenant/device cho device metric;
- unsupported schema;
- timezone không resolve;
- metric contract không thể map theo approved policy.

Xử lý:

```text
write ProjectionFailure
insert ProcessedEvent outcome=failed_terminal khi eventId hợp lệ
hoặc dùng deterministic ProjectionFailure.FailureId làm idempotency gate khi eventId invalid
advance checkpoint
```

Không để một poison event block toàn stream vô hạn.

### Retryable dependency/infrastructure failure

Ví dụ:

- Mongo/SQL timeout;
- deadlock/connection reset;
- metadata service tạm unavailable nếu metadata là required dependency;
- transaction conflict.

Xử lý:

```text
rollback/no checkpoint
retry/backoff
health degraded/unhealthy theo threshold
```

Programming invariant exception không được silently đổi thành terminal data failure; log error, stop/degrade source và cần điều tra.

## 15. Daily aggregation

Event daily fact là additive cho unique events:

```text
key = ProjectionVersion
    + CompanyId
    + DeviceId
    + StatisticsDate
    + MetricKey
    + SourceKind
```

Batch accumulator tính:

- `EventCount += count of new contributions`;
- warning/time-basis counts;
- `FirstEventAtUtc = min(existing, batch)`;
- `LastEventAtUtc = max(existing, batch)`;
- source freshness max.

Một event có thể tạo nhiều metrics nhưng cùng `EventId` chỉ admission vào projection một lần. Mapping phải deterministic theo `MappingVersion`.

Trong Sprint 3 V1, mỗi occurrence contribution có `CountDelta=1`; `EventCount` luôn là số event occurrence, không lấy business quantity/sensor value cộng vào counter này. Numeric measurements/sums khác event count phải có metric value contract riêng ở schema version sau.

Week/month không có processing loop riêng. Future query/API SUM daily rows theo calendar range.

## 16. Device state và duration

State processing cần hai ordering:

```text
Source cursor order:
    persistedAtUtc, eventId

Business state order:
    timelineAtUtc, eventId per device/state type
```

Vì late event có thể có old timeline nhưng new persisted cursor, incremental state update có thể gặp out-of-order transition.

MVP policy:

1. Count metric vẫn được ghi idempotently.
2. Nếu transition mới không sớm hơn current state cursor, apply incremental duration.
3. Nếu transition out-of-order, không cố sửa duration bằng delta phỏng đoán.
4. Ghi nhận `ReconciliationRequest` vào CSDL SQL Server bền vững cho dải ngày bị ảnh hưởng (từ `timelineAtUtc` đến `CurrentEdgeTimestamp`).
5. Tiến trình Reconciliation chạy ngầm sẽ replay lại ordered timeline và cập nhật chính xác `DeviceDailySnapshot` cùng `DeviceStateCursor`.

Interval qua midnight được split theo timezone. Opening state của ngày lấy từ last known predecessor state trước bucket start (`timelineAtUtc < BucketStartAtUtc`); nếu không có evidence thì `unknown`.

## 17. Late event

Late event là event được persist hôm nay nhưng `timelineAtUtc` thuộc ngày trước.

Rules:

- normal cursor vẫn advance theo persisted time;
- daily count của ngày cũ được update;
- `IsFinalized` của affected snapshot có thể trở lại false;
- state transition out-of-order kích hoạt enqueue `ReconciliationRequest` bền vững;
- forward propagation đảm bảo cập nhật lan truyền từ ngày xảy ra late transition cho đến next boundary transition hoặc current state edge;
- rolling reconciliation đảm bảo convergence;
- không reset source checkpoint về ngày cũ.

Không có hard “day closed forever” trong device analytics. Nếu business cần cutoff, thêm reporting cutoff policy riêng, không làm mất late evidence.

## 18. Reconciliation without Hangfire

`ReconciliationHostedService` chạy trong Statistics Worker và dùng `TimeProvider` cùng persisted run history.

Scheduling flow:

```text
startup
    -> read pending requests from [dbo].[DES.ReconciliationRequest]
    -> read last successful rolling reconciliation run
    -> determine missed/required date windows
    -> wait until next configured schedule with cancellable delay
    -> request reconciliation from coordinator
```

Không dựa hoàn toàn vào in-memory timer: sau restart, Worker quét bảng `ReconciliationRequest` và kiểm tra `ProjectionRun` để chạy các dải ngày chưa hoàn tất.

Default proposal:

- scheduler drain liên tục các `ReconciliationRequest` pending trong CSDL;
- rolling reconciliation recent 3 ngày mỗi giờ hoặc theo configured interval;
- finalize/reconcile ngày hôm qua sau local business-day boundary;
- maximum một reconciliation run active;
- target date/company ranges bounded;
- off-peak window cho full backfill/rebuild.

Reconciliation algorithm:

```text
acquire reconciliation lease/range lock (với LeaseEpoch)
truy vấn Predecessor State: transition gần nhất trước First BucketStartAtUtc
read all eligible Mongo events for target business date/range [FirstBucketStartAtUtc, LastBucketEndAtUtc)
group/order from scratch using same mapping version
calculate exact DeviceEventDaily + DeviceDailySnapshot (bao gồm opening/closing states)
write staging/in-memory expected result
acquire SQL writer gate briefly
BEGIN TRANSACTION
    verify LeaseOwner + LeaseEpoch
    replace affected daily rows/snapshots
    update state cursor only when range reaches current edge safely
    resolve matching ReconciliationRequest (Status = 'Completed')
    resolve matching projection failures when applicable
    record ProjectionRun outcome
COMMIT
release lease
```

Reconciliation không replay vào incremental delta writer vì `ProcessedEvent` sẽ skip và không sửa incorrect aggregate. Nó dùng exact recompute + atomic replacement.

## 19. Backfill và rebuild without Hangfire

### Backfill

Explicit run parameters:

```text
Mode=Backfill
FromDate
ToDate
CompanyId? / DeviceId?
ProjectionVersion
```

Backfill dùng range reader và exact recompute. Nó không thay normal incremental checkpoint trừ khi operator khởi tạo một projection hoàn toàn mới theo approved procedure.

### Rebuild

Production strategy:

```text
create ProjectionVersion N+1
    -> seed MetricDefinition/mapping version
    -> build historical ranges
    -> catch up incremental tail
    -> validate counts/health
    -> switch future query configuration to N+1
    -> retain N for rollback window
```

Không reset checkpoint rồi cộng vào version cũ.

### Manual operation

Sprint 3 chưa cần dashboard/job scheduler. Operator chạy dedicated one-shot Worker invocation hoặc deployment job với explicit config/arguments và audit `ProjectionRun`.

## 20. Source ownership và metric mapping

Initial ownership:

| Family | Primary statistics source | Ghi chú |
|---|---|---|
| tag/business raw facts | `rfid_antenna_file` | replayable source ưu tiên |
| device connection | `erp_apphub` | chỉ confirmed activity/state callback |
| device control/sensor | `erp_apphub` | observed state, không suy command acknowledgement |
| scanner lifecycle | `erp_apphub` | activity khác snapshot |
| snapshot | source-specific | không tính như connection transition mặc định |
| unknown/unmapped | quality-only | không health metric |

Mapping registry phải phân biệt:

- activity vs snapshot/reconnect snapshot;
- connected/disconnected vs unknown;
- control state on/off;
- raw tag/business facts;
- parsed vs parsed-with-warnings;
- confirmed callback fixture vs opaque/unmapped payload.

Không dedupe bằng time proximity, tag/device pair hoặc payload hash. Nếu future producer cung cấp shared `sourceEventId`, cross-source correlation được thiết kế ở sprint riêng.

## 21. Health evaluation

Health score chỉ được tính sau khi daily facts đáng tin cậy.

Pipeline:

```text
daily event facts + state durations
    -> expected device schedule/context
    -> versioned health rules
    -> score/status/reason codes
    -> DeviceDailySnapshot
```

Potential signals:

- offline ratio;
- unknown ratio;
- reconnect/disconnect frequency;
- confirmed error/sensor anomaly;
- activity freshness;
- volume deviation so với approved baseline;
- time-basis/warning quality.

Guardrails:

- no event không tự động bằng unhealthy;
- no schedule/evidence -> health `unknown`;
- health rule không query raw payload;
- mọi result có rule version;
- threshold change cần recompute affected dates;
- health failure không được rollback correct event counts nếu có thể ghi terminal health failure/reconcile marker riêng.

## 22. Concurrency model

Sprint 3 MVP:

- một active incremental projector cho một `ProjectionName + ProjectionVersion + PartitionKey`;
- một Mongo reader loop với Bounded Overlap Replay;
- một SQL batch writer được bảo vệ bằng Fencing Token (`LeaseEpoch`);
- reconciliation/backfill không commit cùng target range với incremental writer;
- in-process writer gate + SQL lease fencing token (`LeaseEpoch`) bảo vệ tuyệt đối chống split-brain / zombie worker;
- cancellation token qua Mongo read, mapping boundary khi async, SQL command và delay.

Không tạo một task/thread cho mỗi device.

Metric mapping có thể parallelize CPU-bound trong batch sau benchmark, nhưng state transition của cùng device/state type phải tuần tự theo business order.

Mọi transaction ghi dữ liệu (Incremental & Reconciliation) đều bắt buộc thực thi với điều kiện Fencing:
```sql
WHERE LeaseOwner = @CurrentOwner 
  AND LeaseEpoch = @CurrentEpoch 
  AND LeaseExpiresAtUtc > SYSUTCDATETIME()
```
Nếu kiểm tra thất bại, rollback toàn bộ transaction ngay lập tức để tránh ghi đè dữ liệu.

## 23. Backpressure và sizing

100.000 events/ngày tương đương khoảng 1,16 event/giây trung bình, nhưng design phải chịu burst và downtime backlog.

Initial tuning proposal:

| Option | Initial value |
|---|---|
| `BatchSize` | 1.000 |
| `PollInterval` | 5 giây khi caught up |
| `MaxBatchProcessingDuration` | 30 giây cảnh báo |
| `PersistenceRetryCount` | 5 |
| `RetryMinDelay` | 1 giây |
| `RetryMaxDelay` | 30 giây |
| `ReconciliationLookbackDays` | 3 |
| `LeaseDuration` | lớn hơn expected batch duration, có renew |

Behavior:

- batch full -> đọc batch tiếp ngay, không chờ PollInterval;
- caught up -> cancellable delay;
- SQL slow -> stop reading thêm batch, không buffer vô hạn;
- process memory bounded bởi batch;
- retry giữ cùng event identities/contributions;
- health degraded khi lag hoặc batch duration vượt threshold.

Capacity acceptance phải đo:

- sustained 100.000/day;
- peak burst tối thiểu cao hơn average nhiều lần;
- catch-up 1–3 ngày backlog;
- rolling reconciliation song song với report query;
- SQL transaction log/locking và Mongo query latency.

## 24. Configuration contract

Ví dụ không chứa secret:

```json
{
  "DeviceEventStatistics": {
    "Enabled": true,
    "WorkerId": "device-event-statistics-worker-01",
    "Projection": {
      "Name": "device_event_daily",
      "Version": 1,
      "Mode": "Incremental",
      "BatchSize": 1000,
      "PollInterval": "00:00:05",
      "LeaseDuration": "00:02:00",
      "PersistenceRetryCount": 5,
      "RetryMinDelay": "00:00:01",
      "RetryMaxDelay": "00:00:30"
    },
    "MongoDb": {
      "DatabaseName": "device_event_history",
      "HistoryCollection": "device_event_history"
    },
    "SqlServer": {
      "DatabaseName": "UA-REPORTING-DB",
      "SchemaName": "dbo",
      "CommandTimeout": "00:00:30"
    },
    "Reconciliation": {
      "Enabled": true,
      "Interval": "01:00:00",
      "LookbackDays": 3,
      "FinalizePreviousDayLocalTime": "02:00:00",
      "MaximumRangeDays": 31
    },
    "Observability": {
      "LagDegradedAfter": "00:05:00",
      "LagUnhealthyAfter": "00:30:00",
      "FailureUnhealthyThreshold": 5
    }
  }
}
```

Connection strings lấy từ environment/secret provider, ví dụ:

```text
DEVICE_EVENT_STATISTICS_MONGODB_CONNECTION_STRING
DEVICE_EVENT_STATISTICS_SQLSERVER_CONNECTION_STRING
```

Không log connection string, credential hoặc full source document.

Validation:

- positive batch/retry/lease/delay values;
- retry min <= max;
- projection version > 0;
- mode/collection/schema identifiers allowlisted;
- reconciliation lookback/range bounded;
- SQL schema không phải `HangFire`, `dbo` chỉ được dùng khi explicitly approved;
- timezone mapping available cho enabled device/source scope;
- no secret in endpoint/config summary.

## 25. SQL access strategy

Technology:

- `Microsoft.Data.SqlClient` cho SQL Server connection/transaction/commands;
- parameterized SQL, table-valued parameters hoặc stored procedures cho batch persistence;
- versioned SQL migration scripts;
- không cần ORM change tracking trong hot projection path.

Rules:

- connection mở theo bounded operation, dùng pool;
- explicit transaction isolation theo tested workload;
- command timeout cấu hình;
- retry chỉ transient errors và giữ batch identity;
- deadlock retry không tạo duplicate;
- report read/write blocking được benchmark; có thể cân nhắc row-versioning ở DBA/deployment decision, không tự bật từ Worker.

## 26. MongoDB access strategy

- read-only credential;
- field projection tối thiểu;
- cursor index `persistedAtUtc + eventId`;
- batch query có cancellation;
- không dùng `$lookup` nặng trong incremental loop;
- không aggregate toàn history trong normal loop;
- range/reconciliation query giới hạn ngày/company/device;
- source document immutable assumption được giám sát; update/delete history không nằm trong normal contract.

Mongo change stream có thể là future optimization nếu deployment là replica set/sharded cluster và low-latency requirement xuất hiện. Nó không thay idempotency/reconciliation và không thuộc Sprint 3 MVP.

## 27. Graceful shutdown

```text
application stopping
    -> stop scheduling reconciliation mới
    -> stop acquiring/renewing new work after current batch
    -> allow active Mongo read/map/SQL transaction complete trong timeout
    -> if transaction confirmed, checkpoint included
    -> otherwise rollback/cancel and retain old checkpoint
    -> mark ProjectionRun cancelled/failed as applicable
    -> release lease
    -> dispose connections/resources
```

Không advance checkpoint chỉ vì shutdown timeout. Uncommitted batch được đọc lại khi restart.

## 28. Observability

### 28.1. Structured logs

Context:

```text
WorkerId
ProjectionName
ProjectionVersion
RunId / RunType
Batch first/last persisted cursor
Batch size
CompanyId/DeviceId only in diagnostic scope, not high-cardinality metrics
StatisticsDate/range
Result/duration/retry attempt
```

Không log full event/raw payload/connection strings.

Important logs:

| Log | Ý nghĩa |
|---|---|
| configuration validated | redacted options hợp lệ |
| projection lease acquired/lost | active owner status |
| batch read | Mongo source range/size |
| batch committed | SQL outcomes + checkpoint |
| idempotent duplicates | replay được skip |
| projection failure | terminal data contract issue |
| projection retry | transient dependency issue |
| reconciliation started/completed | exact recompute run |
| out-of-order state event | device/day cần reconcile |
| statistics lag threshold exceeded | projection không theo kịp |

### 28.2. Metrics

- source events read;
- eligible/ignored/quality/failure events;
- processed-event duplicates;
- daily rows inserted/updated;
- state transitions and reconciliation markers;
- batch size/duration;
- Mongo read latency/failure;
- SQL transaction latency/retry/failure/deadlock;
- projection checkpoint and lag;
- reconciliation duration/range/result;
- health calculation result count;
- lease state.

Không dùng eventId/deviceId làm metric label.

### 28.3. Health

| State | Meaning |
|---|---|
| Live | process/event loop chạy |
| Ready | config/schema/dependencies/metric registry hợp lệ và lease acquired |
| Degraded | retry, lag, reconciliation pending hoặc partial metadata issue |
| Unhealthy | Mongo/SQL unavailable quá ngưỡng, lease lost, checkpoint không tiến triển, schema mismatch |

Không có source event mới nhưng checkpoint caught-up không phải unhealthy.

## 29. Security và deployment isolation

- Mongo account read-only `device_event_history`.
- SQL Worker account chỉ SELECT/INSERT/UPDATE/DELETE cần thiết trong `dbo`; migration identity riêng nếu có thể.
- Report account read-only.
- Không reuse Hangfire schema/account/queue.
- Không tạo trigger/cross-database write vào ERP.
- Connection strings từ secret provider.
- Logs không có PII/raw payload.
- Reconciliation/backfill range phải validate để tránh accidental full-database workload.
- Statistics Worker deploy độc lập, có CPU/memory/connection-pool limits.
- Nếu dùng chung Report database, full rebuild chỉ chạy trong approved maintenance/load window.

## 30. Testing strategy

### 30.1. Unit tests

- minimal Mongo document mapping V1/V2;
- cursor comparison/order;
- event eligibility;
- source ownership;
- metric registry duplicate/mapping;
- timezone/day bucket;
- late event;
- state repeated/out-of-order/midnight transitions;
- health rules/version/reason;
- error classification;
- options validation/redaction.

### 30.2. SQL integration tests

- migrations/schema verification;
- TVP/batch persistence;
- processed-event unique identity;
- duplicate retry;
- commit/rollback/checkpoint atomicity;
- projection failure terminal outcome;
- lease acquire/renew/loss;
- state cursor concurrency;
- reconciliation exact replacement;
- projection versions coexist;
- report indexes/query shape.

### 30.3. Mongo integration tests

- compound cursor index/query;
- equal `persistedAtUtc` tie-break by `eventId`;
- batch boundaries không skip/duplicate;
- cancellation/timeout/restart;
- mixed V1/V2 compatibility;
- late timeline with current persisted cursor.

### 30.4. End-to-end

```text
fixture raw-log/AppHub
    -> DeviceEventHistory.Worker
    -> Mongo history
    -> DeviceEventStatistics.Worker
    -> SQL daily facts/snapshot/checkpoint
```

Scenarios:

- one event end-to-end;
- 100.000-event day;
- retry after ambiguous SQL result;
- crash before/inside/after commit;
- SQL outage while ingestion continues;
- Mongo outage/recovery;
- duplicate history delivery;
- late event;
- out-of-order connection event + reconciliation;
- raw-log/AppHub overlapping family không cộng chéo;
- restart and catch-up backlog;
- graceful shutdown;
- rebuild new projection version.

### 30.5. Performance acceptance

- sustain 100.000 events/day với safety margin;
- process peak burst không unbounded memory;
- catch up minimum agreed backlog window;
- statistics lag trong SLO;
- SQL batch transaction bounded;
- reconciliation không làm report latency vượt threshold;
- no measurable regression trên raw-log/AppHub ingestion vì chỉ shared Mongo read load.

## 31. Rollout

```text
1. Apply dbo schema/migrations.
2. Deploy Statistics Worker với Enabled=false.
3. Verify Mongo index và SQL/report impact.
4. Seed/validate metric registry, timezone và ownership.
5. Run limited company/date backfill in UAT.
6. Compare SQL counts với Mongo queries.
7. Enable incremental projection for one tenant/source scope.
8. Run late/retry/restart/reconciliation tests.
9. Enable broader scope.
10. Keep API/report reads disabled until acceptance evidence passes.
```

Rollback:

- disable Statistics Worker;
- raw-log/AppHub continue normally;
- giữ Mongo history và SQL diagnostic data;
- không reset ingestion checkpoint;
- repair/rebuild SQL projection rồi resume từ projection checkpoint hoặc version mới.

## 32. Definition of Done Design Sprint 3

- Statistics Worker là process độc lập.
- Không có synchronous SQL write trong ingestion pipeline.
- Không có Hangfire dependency.
- Mongo source cursor và SQL checkpoint contract rõ.
- Batch bounded, không unbounded queue.
- Processed-event + SQL-local transaction chống double count.
- Terminal data failure khác transient dependency failure.
- Poison event không block stream vô hạn.
- Daily aggregate/state snapshot tách trách nhiệm.
- Timezone, late event, out-of-order và midnight semantics rõ.
- Source ownership ngăn cộng chéo mặc định.
- Reconciliation dùng exact recompute/replace.
- Backfill/rebuild versioned và audit được.
- Health result không được suy khi thiếu evidence.
- Observability, security, shutdown và rollout không ảnh hưởng ingestion.
- Unit/integration/end-to-end/capacity acceptance được định nghĩa.

## 33. Các quyết định cần khóa trước implementation plan

- SQL database/schema deployment thực tế và migration owner (`dbo` schema).
- Cấu hình MongoDB Bounded Overlap Window (mặc định 5 phút) kết hợp `ProcessedEvent` để loại bỏ commit skew.
- Quy tắc kiểm tra Fencing Token (`LeaseEpoch`) trên `ProjectionCheckpoint` trong mọi transaction ghi SQL.
- Lược đồ bảng `[dbo].[DES.ReconciliationRequest]` lưu trữ bền vững và quy tắc forward propagation cho multi-day state transition.
- Authoritative timezone/device metadata source và cơ chế audit/trigger reconcile khi timezone thay đổi.
- Initial metrics + source ownership sau Sprint 2 UAT.
- Health Rule V1 hoặc quyết định defer score sang sub-phase sau daily facts.
- Reconciliation interval/lookback và approved report maintenance window.
- Active projection version selection cho future API.
- Processed-event retention policy.
- Exact deployment mode cho manual backfill/rebuild invocation.
