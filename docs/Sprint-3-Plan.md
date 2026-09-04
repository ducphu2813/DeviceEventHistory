# Device Event Statistics - Implementation Plan Sprint 3

## 1. Trạng thái, căn cứ và cách sử dụng

- Ngày lập: 2026-09-04.
- Trạng thái: Implementation Plan chính thức, thay thế toàn bộ bản nháp.
- Căn cứ: `Sprint-3-Design.md`, `Sprint-3-Schema.md`, `Sprint-2-Db-Schema.md`, BSON thực tế của History Worker và fixture UAT.
- Convention: `Coding-Standards.md`, `.editorconfig` và cấu trúc `DeviceEventHistory.*` hiện tại.
- Sprint 2 đã được đội development kiểm thử pass theo xác nhận của người dùng. Fixture/evidence cần thiết vẫn phải được đính kèm khi nghiệm thu Sprint 3.

Plan này xác định thiết kế triển khai, file chịu trách nhiệm, task, phụ thuộc và acceptance. Các thay đổi contract so với Design/Schema cũ được liệt kê ở mục 4; task P0 phải đồng bộ chúng trước khi viết migration hoặc production code. Không dùng DDL cũ nếu chưa áp dụng các thay đổi này.

Các ngưỡng tuning trong Plan là cấu hình khởi điểm cho UAT. Team được điều chỉnh sau benchmark, nhưng không thay đổi semantics về identity, ownership, transaction, thời lượng hoặc coverage chỉ để đạt throughput.

## 2. Phạm vi đã chốt

| Hạng mục | Quyết định |
|---|---|
| Runtime | Thêm `.NET 10` `DeviceEventStatistics.Worker`, executable/deployment riêng |
| SQL | Database riêng, ví dụ `device_event_statistics`; schema `device_stats` |
| Configuration | Mongo và SQL có options/connection string riêng dưới `DatabaseSettings` |
| Context | `MongoHistoryDbContext` và `SqlStatisticsDbContext` riêng trong Infrastructure |
| SQL access | `Microsoft.Data.SqlClient`, parameterized commands/TVP, SQL-local transaction; không thêm EF Core trong Sprint 3 |
| Nguồn gốc | MongoDB `device_event_history`; Statistics Worker có quyền đọc, không sửa history |
| Đầu ra | Event counts, device/scanner state durations, quality, checkpoint và audit |
| Health thiết bị | Hoãn sang **Sprint 4**; các cột health để `NULL` |
| Health vận hành Worker | Vẫn triển khai readiness, dependency errors, lag, lease và retention risk |
| Timezone | Một timezone Việt Nam `UTC+07:00`, không đổi theo device/machine |
| Nguồn metric | Một primary source cho mỗi event family, theo mục 6 |
| Độ trễ | Cập nhật liên tục; cảnh báo ở 12 giờ, vượt 24 giờ là vi phạm SLO |
| Mongo retention | History giữ 7 ngày theo thời điểm persistence, không theo business timeline |
| SQL retention | Facts/snapshots lưu dài hạn; không tự purge inbox của active version |
| Rebuild | Chỉ trong khoảng dữ liệu còn đủ và coverage được xác nhận; không archive dài hạn ở Sprint 3 |
| Scheduler | Worker tự chạy incremental, duration refresh và reconciliation; không Hangfire |
| UAT | AppHub ERP, Antenna/Analytics, Mongo và SQL đã sẵn sàng theo xác nhận của người dùng |

Ngoài phạm vi: API/UI, direct RFID publisher, cross-source fuzzy dedupe, health scoring/rules, operating schedule, historical timezone assignment, active-active partitioning, broker và Mongo change stream.

Không thay raw parser/checkpoint/reliability của History Worker. Có dependency chuẩn bị canonical AppHub facts ở P0.5 nếu checkout triển khai vẫn dùng opaque mapper; thay đổi này được tách riêng khỏi Statistics pipeline. Mongo indexes và history TTL là migration vận hành có kiểm soát, được kiểm thử tương thích và chạy bằng deployment identity riêng.

## 3. Kiến trúc và convention

```text
RFID raw-log + ERP AppHub
    -> DeviceEventHistory.Worker
    -> MongoDB device_event_history
                  |
                  | read-only, bounded pages
                  v
       DeviceEventStatistics.Worker
          -> mapping / SQL idempotency
          -> daily facts / state / reconciliation
                  |
                  v
       SQL database device_event_statistics
          schema device_stats
```

### 3.1. Dependency và trách nhiệm

```text
Domain <- Application <- Infrastructure <- Worker
```

- Domain: value objects, metric/state concepts và các phép tính thuần; không BSON, SqlClient, hosting hoặc I/O.
- Application: use cases, policies, input/output models và interfaces theo nghiệp vụ.
- Infrastructure: Mongo readers, SQL context/store, migrations, configuration metadata, telemetry.
- Worker: options validation, DI, startup, hosted services, runtime scheduling/shutdown.
- Statistics projects không reference History Worker/Infrastructure hoặc legacy ERP/RFID projects. Dùng contract đầu vào riêng, tối thiểu, kiểm thử với BSON fixture từ History Worker.
- Không dùng generic repository hoặc base worker chứa tất cả modes. Tái sử dụng mapping/calculation qua composition.
- `SqlStatisticsDbContext` quản lý tạo connection, ping và mở SQL session; không phải EF `DbContext`. Không giữ một `SqlConnection` dùng chung giữa các tác vụ.
- `SqlProjectionSession` sở hữu connection + transaction + writer lock trong một operation; stores nhận cùng session, không tự mở transaction độc lập.

### 3.2. Quy tắc code

- Theo `.editorconfig`; namespace file-scoped, nullable enabled, async có `CancellationToken`.
- Một public type chính/file; private field `_camelCase` theo `Coding-Standards.md`.
- `AppConst` Statistics chỉ chứa section names, defaults, metric/status/error contracts. `AppConst.Messages.MSG_*` cho operational/validation messages.
- Options chỉ chứa cấu hình/default; validator dùng `ValidateOnStart()`, không ping DB. Startup preflight chịu trách nhiệm external checks.
- `TimeProvider` được inject; không gọi thời gian hệ thống trực tiếp trong logic cần kiểm thử.
- SQL/BSON chỉ trong Infrastructure. SQL identifier cố định/allowlisted; giá trị luôn parameterized.
- Retry có giới hạn, chỉ transient; invariant/programming error không biến thành dữ liệu lỗi để lặng lẽ bỏ qua.
- Log structured, không raw payload/secret. Metrics qua abstraction + `System.Diagnostics.Metrics`; không bắt buộc thêm Serilog/vendor exporter.
- Không tạo thread theo device; memory giới hạn theo page/batch/staging chunk và số group đang xử lý.

## 4. Contract delta phải đồng bộ trước implementation

P0 cập nhật `Sprint-3-Design.md` và `Sprint-3-Schema.md` theo các delta dưới đây. Plan không yêu cầu tự ý chạy DDL ở bước này.

| ID | Delta | Lý do |
|---|---|---|
| D01 | Database SQL riêng; cấu hình nằm dưới `DeviceEventStatistics:DatabaseSettings` | Quyết định đã chốt và convention project |
| D02 | Health evaluator/scoring chuyển Sprint 4; nullable health fields giữ lại | Không triển khai rule chưa chốt |
| D03 | Timezone cố định UTC+7; bỏ yêu cầu `DeviceTimeZoneAssignment` | Không cần lịch sử múi giờ động |
| D04 | `DeviceDailySnapshot` giữ summary device/day; thêm `DeviceStateDaily` theo `StateType`, chuyển duration/connection fields sang bảng mới | Device và scanner connection độc lập |
| D05 | State daily có `CalculatedThroughAtUtc`, coverage và dirty status; duration bằng khoảng đã tính | Không ghi duration tương lai của ngày đang diễn ra |
| D06 | `ProcessedEvent.Outcome` thêm `quality_only`; quality được gate cùng transaction | Phân biệt quality với ignored/failed |
| D07 | Projection registry lưu mapping/ownership/metric-set version bất biến theo projection version | Rebuild tái lập được |
| D08 | Checkpoint có sweep boundary/page cursor tách khỏi high watermark; audit cursor riêng | Overlap có tiến triển, poison document không bị bỏ quên |
| D09 | Reconcile/backfill dùng processed-event membership tại revision đã chụp; publish kiểm tra revision dưới SQL writer gate | Tránh lost update/double count |
| D10 | Lưu coverage/missing-data status và opening-state evidence | Quá retention không recompute từ dữ liệu thiếu |
| D11 | `ReconciliationRequest` có claim/retry/coalescing/recovery; `ProjectionRun` có cursor/revision/range/coverage | Restart không bỏ việc dở |
| D12 | Mongo deployment migrations thêm indexes đọc V1/V2 và TTL history 7 ngày; checkpoint không TTL | Áp dụng retention thật |

### 4.1. Bảng SQL đích

Giữ kiểu SQL theo Schema: IDs/counters/duration `bigint`, thời gian UTC `datetime2(7)`, event SHA-256 `binary(32)`, display text `nvarchar`, concurrency `rowversion`.

| Bảng | Grain/trách nhiệm và thay đổi cần làm |
|---|---|
| `SchemaMigration` | Migration ID/checksum/applied time; Worker chỉ kiểm tra version |
| `ProjectionDefinition` | PK name/version; immutable MappingVersion, OwnershipVersion, MetricSetVersion, CoverageStartAtUtc, TimeZoneId, lifecycle status |
| `DeviceDimension` | PK company/device; current display metadata, timezone Việt Nam; `IsActive` nullable khi thiếu evidence |
| `MetricDefinition` | Unique `(MetricSetVersion, MetricCode)`; không overwrite metric set cũ |
| `DeviceEventDaily` | PK version/company/device/date/metric/source và counters; mỗi metric occurrence CountDelta=1 |
| `DeviceDailySnapshot` | PK version/company/device/date; total unique owned events, warning/error, first/last time, health nullable, calculation/coverage; không chứa state duration |
| `DeviceStateDaily` | PK version/company/device/date/stateType; opening/closing state, durations, observations, reconnect, bucket, calculation boundary, evidence, dirty/finalized/coverage |
| `DeviceStateCursor` | PK version/company/device/stateType; current state, StateSince, LastTimeline/EventId, accounting edge và trusted seed metadata |
| `ProcessedEvent` | Unique projection/version/eventId; outcome gồm quality_only; source identity, date/timeline/mapping metadata để kiểm tra membership |
| `ProjectionCheckpoint` | High watermark, sweep bounds/page cursor, audit scan position, lease/epoch/expiry, DataRevision, last completed sweep |
| `ReconciliationRequest` | Range/device/stateType/reason, retry, NextAttempt, claim owner/epoch/expiry, dirty generation |
| `ProjectionFailure` | Deterministic failure gate và source/error evidence; không raw payload |
| `ProjectionRun` | Mode/range/result, captured revision, source bounds, count/coverage evidence |
| `ProjectionCoverage` | Version/company/device/date/kind; complete/partial/unrecoverable, boundary/reason/run |
| `IngestionQualityDaily` | Source/day/quality grain; unresolved tenant chỉ ở quality |
| `ProjectionStagingEvent/Daily/State` | Staging bền vững theo RunId cho bounded recompute; cleanup sau run |

Indexes report dùng tenant/version/date; staging theo RunId; inbox thêm index version/company/device/date với source/time/outcome INCLUDE.

`DeviceDailySnapshot.TotalEventCount` là số event identity hợp lệ thuộc primary metric scope. Một event tạo hai metrics vẫn chỉ tăng total một lần. Unknown/quality-only không ép vào device total. Không suy device error từ lỗi ingestion; Sprint 3 chưa seed error metric nếu chưa có contract xác nhận.

### 4.2. Fields cần khóa trong schema mới

- `DeviceStateDaily`: ngoài PK, cần `BucketStartAtUtc`, `BucketEndAtUtc`, `CalculatedThroughAtUtc`, `OpeningConnectionStatus`, `ClosingConnectionStatus`, `OnlineSeconds`, `OfflineSeconds`, `UnknownSeconds`, `ConnectedEventCount`, `DisconnectedEventCount`, `ReconnectCount`, `OpeningEvidenceKind`, `OpeningEvidenceEventId`, `IsDirty`, `IsFinalized`, `CoverageStatus`, `CalculatedAtUtc`, timestamps và rowversion. Check calculation boundary nằm trong bucket và duration total bằng `DATEDIFF_BIG(SECOND, BucketStartAtUtc, CalculatedThroughAtUtc)`.
- `DeviceStateCursor`: thêm `AccountedThroughAtUtc` để biết duration đã được tính tới đâu. LastTimeline/EventId vẫn biểu diễn observation cuối, không bị refresh thay bằng thời gian hiện tại.
- `ProjectionCheckpoint`: LastPersistedAtUtc/LastEventId là H; `SweepFromAtUtc`, `SweepToAtUtc`, `SweepLastPersistedAtUtc`, `SweepLastEventId` là L/U/P. Completion atomically clear sweep fields, giữ H. Deep discovery/audit có checkpoint partition riêng, nhưng dùng lease/data revision chung của projection; không tự acquire independent data ownership.
- `ProcessedEvent`: thêm `SourceDocumentId`, `CompanyId`, `DeviceId`, `TimelineAtUtc`, `MappingVersion`. SourceKind và StatisticsDate vẫn giữ. Metadata này không đủ tái dựng raw facts, chỉ phục vụ admission/trace/membership.
- `ProjectionCoverage`: phân biệt `activity` và từng stateType; lưu `CoveredFromAtUtc`, `CoveredThroughAtUtc`, reason, run và source retention evidence. `complete` nghĩa đủ dữ liệu theo phạm vi quan sát đã khai báo, không phải chứng minh AppHub không làm mất event ở upstream.
- `DeviceDailySnapshot`: giữ unique-event totals và health nullable; `ErrorEventCount=0` khi không có eligible confirmed error, không diễn giải thành thiết bị chắc chắn không lỗi. Sprint 4 phải đọc coverage và metric availability trước scoring.
- `ProjectionDefinition`: version registry immutable; lifecycle `building`, `ready`, `active`, `retired`, `failed`. Cutover phải xác nhận date coverage; không chỉ đổi một ActiveVersion toàn cục rồi trả thiếu lịch sử cũ.
- Staging không lưu raw payload. Chỉ event references/outcomes và canonical facts tối thiểu cần cho run; xóa staging sau publish hoặc abandoned-run cleanup, không biến staging thành archive.

## 5. Cấu trúc thư mục và các file chính

Thêm vào `DeviceEventHistory.sln`. Có thể tách helper nội bộ khi trách nhiệm rõ; không scaffold class rỗng không dùng.

```text
src/
  DeviceEventStatistics.Domain/
    DeviceEventStatistics.Domain.csproj
    Common/AppConst.cs
    Identity/EventIdentity.cs
    Time/{StatisticsDate,StatisticsBucket}.cs
    Metrics/{MetricContribution,MetricDefinition}.cs
    State/{ConnectionState,StateType,StateTransition,StateCursor}.cs
    State/{StateDurationCalculator,StateDailyResult}.cs
    Coverage/ProjectionCoverage.cs

  DeviceEventStatistics.Application/
    DeviceEventStatistics.Application.csproj
    History/{HistoryEvent,HistoryReadResult,SourceCursor}.cs
    History/{IHistoryEventReader,IHistoryRangeReader,IHistoryContractAuditReader}.cs
    Mapping/{HistoryEventEligibilityPolicy,EventOwnershipPolicy}.cs
    Mapping/{IDeviceMetricMapper,DeviceMetricMapperRegistry}.cs
    Mapping/{RawFileMetricMapper,AppHubConnectionMetricMapper}.cs
    Mapping/{AppHubControlMetricMapper,AppHubSensorMetricMapper,AppHubScannerMetricMapper}.cs
    Mapping/ProjectionEventOutcome.cs
    Metadata/{IDeviceMetadataResolver,DeviceMetadata}.cs
    Time/LocalStatisticsDateResolver.cs
    Projection/{StatisticsProjectionPipeline,IncrementalProjectionHandler}.cs
    Projection/{ProjectionBatch,ProjectionCheckpoint,ProjectionSweep,ProjectionDefinition}.cs
    Projection/{IProjectionLeaseStore,IProjectionCheckpointStore}.cs
    Persistence/{IStatisticsBatchWriter,IProjectionRebuildStore,IDurationRefreshStore}.cs
    Reconciliation/{ReconciliationCoordinator,ReconciliationRequest}.cs
    Reconciliation/{IReconciliationRequestStore,ExactRangeRebuilder}.cs
    Reconciliation/{ProjectionCoveragePolicy,ForwardStatePropagation}.cs
    Recovery/{ProjectionBootstrapHandler,BackfillHandler,RebuildHandler}.cs
    Failures/{ProjectionFailure,ProjectionFailureFactory}.cs
    Observability/{IStatisticsTelemetry,NullStatisticsTelemetry}.cs

  DeviceEventStatistics.Infrastructure/
    DeviceEventStatistics.Infrastructure.csproj
    MongoDb/Configuration/MongoHistoryOptions.cs
    MongoDb/MongoHistoryDbContext.cs
    MongoDb/Reading/{MongoHistoryEventReader,MongoHistoryRangeReader}.cs
    MongoDb/Reading/MongoHistoryContractAuditReader.cs
    MongoDb/Mapping/HistoryDocumentMapper.cs
    MongoDb/Indexes/MongoHistoryIndexVerifier.cs
    SqlServer/Configuration/SqlStatisticsOptions.cs
    SqlServer/{SqlStatisticsDbContext,SqlProjectionSession}.cs
    SqlServer/Execution/SqlRetryPolicy.cs
    SqlServer/Schema/SqlSchemaVerifier.cs
    SqlServer/Mapping/ProjectionTvpMapper.cs
    SqlServer/Stores/{SqlProjectionLeaseStore,SqlProjectionCheckpointStore}.cs
    SqlServer/Stores/{SqlStatisticsBatchWriter,SqlProjectionRebuildStore}.cs
    SqlServer/Stores/{SqlDurationRefreshStore,SqlReconciliationRequestStore}.cs
    SqlServer/Migrations/001_CreateStatisticsSchema.sql
    SqlServer/Migrations/002_CreateProjectionTables.sql
    SqlServer/Migrations/003_CreateIndexesAndTableTypes.sql
    SqlServer/Migrations/004_CreateProjectionProcedures.sql
    SqlServer/Migrations/005_SeedMetricSetV1.sql
    Metadata/ConfigurationDeviceMetadataResolver.cs
    Observability/{StatisticsMetrics,StatisticsHealthState,LoggingScopes}.cs

  DeviceEventStatistics.Worker/
    DeviceEventStatistics.Worker.csproj
    Program.cs
    appsettings.Example.json
    Properties/launchSettings.json
    Configuration/{StatisticsWorkerOptions,ProjectionOptions}.cs
    Configuration/{StateOptions,ReconciliationOptions,RetentionOptions,ObservabilityOptions}.cs
    Configuration/{DeviceMetadataOptions,ServiceCollectionExtensions}.cs
    Configuration/{ConfigurationOptionsRegistration,OptionsValidators,ConfigurationRedactor}.cs
    HostedServices/StartupInitializationHostedService.cs
    Orchestration/{IncrementalProjectionHostedService,ProjectionLeaseCoordinator}.cs
    Orchestration/{LeaseHeartbeatHostedService,ReconciliationHostedService}.cs
    Orchestration/{DurationRefreshHostedService,HistoryContractAuditHostedService}.cs
    Orchestration/{ManualProjectionHostedService,GracefulShutdownCoordinator}.cs
    HealthChecks/{MongoHistoryHealthCheck,SqlStatisticsHealthCheck}.cs
    HealthChecks/{ProjectionProgressHealthCheck,RetentionCoverageHealthCheck}.cs

tests/
  DeviceEventStatistics.UnitTests/
    DeviceEventStatistics.UnitTests.csproj
    ConfigurationValidationTests.cs
    HistoryEligibilityTests.cs
    SourceCursorTests.cs
    ProjectionSweepTests.cs
    MetricOwnershipTests.cs
    MetricMappingTests.cs
    LocalStatisticsDateResolverTests.cs
    StateDurationCalculatorTests.cs
    ForwardStatePropagationTests.cs
    ProjectionCoverageTests.cs
    ProjectionFailureTests.cs
    GracefulShutdownTests.cs
  DeviceEventStatistics.IntegrationTests/
    DeviceEventStatistics.IntegrationTests.csproj
    Fixtures/{StatisticsDatabaseFixture.cs,HistoryV1.json,HistoryV2.json}
    SqlSchemaTests.cs
    MongoHistoryReaderTests.cs
    ProjectionPersistenceTests.cs
    ProjectionLeaseTests.cs
    ReconciliationTests.cs
    RetentionRecoveryTests.cs
    StatisticsWorkerEndToEndTests.cs
  DeviceEventStatistics.ArchitectureTests/
    DeviceEventStatistics.ArchitectureTests.csproj
    DependencyBoundaryTests.cs

deploy/device-event-statistics/
  Apply-SqlMigrations.ps1
  Apply-MongoStatisticsIndexes.ps1
  Enable-HistoryRetention.ps1
  Invoke-StatisticsMode.ps1
  README.md

docs/
  Sprint-3-{Design,Schema,Plan,Testcase,Runbook}.md
```

Test projects dùng xUnit/test SDK theo version repository. Integration tests dùng DB UAT test riêng hoặc container; credentials opt-in, database test có prefix riêng và cleanup không chạm database nghiệp vụ.

Ký hiệu `{A,B}.cs` trong cây thư mục là hai file `A.cs`, `B.cs`, không phải một file chứa nhiều public classes.

### 5.1. Contract giữa các component

| Component | Input -> Output | Boundary cần giữ |
|---|---|---|
| `IHistoryEventReader.ReadPageAsync` | Frozen sweep + page cursor + limit -> bounded HistoryReadResult | Chỉ đọc Mongo; không cập nhật SQL checkpoint |
| `HistoryDocumentMapper` | Minimal BSON -> HistoryEvent hoặc contract diagnostic | Không throw cho known malformed data |
| `StatisticsProjectionPipeline.Map` | HistoryEvent + immutable policy -> ProjectionEventOutcome | Pure mapping; Event/Quality/Failure có trace identity |
| `IStatisticsBatchWriter.PersistAsync` | ProjectionBatch + lease token + expected checkpoint -> committed result | Cùng transaction cho toàn bộ effects; trả actual inserted/duplicate counts |
| `StateDurationCalculator.Calculate` | Current cursor + ordered new observations + as-of -> daily changes/new cursor/dirty ranges | Không I/O; giữ exact order và accounting edge |
| `IReconciliationRequestStore` | Enqueue/claim/renew/complete/retry request generation | Thao tác gắn session hoặc fencing token; không complete request mới bằng run cũ |
| `IProjectionRebuildStore` | Capture revision/membership -> staging -> conditional publish | Prepare/read/publish tách biệt; no Mongo I/O dưới SQL lock |
| `ProjectionCoveragePolicy` | Requested range + membership + retention + opening evidence -> allowed/partial/rejected | Không suy completeness từ query rỗng |

`HistoryEvent` cần các fields: source document ID, EventId, SchemaVersion, CompanyId, SourceKind, Category, EventName, DeliveryKind, device/gate, TimelineAtUtc, PersistedAtUtc, TimeBasis, ParseStatus và confirmed facts. Không reference CanonicalDeviceEvent implementation của History Worker để tránh coupling schema nội bộ.

`ProjectionEventOutcome` có một disposition `aggregated/ignored/quality_only/failed_terminal`, danh sách metric contributions, zero/one state observation, quality contributions và optional failure. Một failed outcome có thể ghi quality diagnostic nhưng không device metric. Batch writer kiểm tra tính nhất quán trước SQL write.

`CommittedBatchResult` (record nhỏ đi cùng ProjectionBatch nếu phù hợp) cần actual new/duplicate/failure counts, checkpoint sau commit và DataRevision. Logging không được dùng số prepared contributions để báo số đã ghi thành công.

## 6. Metric V1, ownership và metadata

### 6.1. Registry cần seed

| MetricCode | Primary source | Điều kiện mapping |
|---|---|---|
| `tag_read` | `rfid_antenna_file` | Có confirmed tag-read/signal facts; không chỉ dựa vào header có TagId |
| `business_process` | `rfid_antenna_file` | Có confirmed business facts từ `te` |
| `device_connected` | `erp_apphub` | `receiveStateConnected`, state connected rõ |
| `device_disconnected` | `erp_apphub` | `receiveStateConnected`, state disconnected rõ |
| `green_light_on/off` | `erp_apphub` | `receiveGreenState`, control On rõ |
| `red_light_on/off` | `erp_apphub` | `receiveRedState`, control On rõ |
| `sensor_state_observed` | `erp_apphub` | `receiveTimeSensor`, canonical sensor facts đã map |
| `scanner_connected` | `erp_apphub` | `receiveDeviceScanConnect`, confirmed lifecycle activity |
| `scanner_disconnected` | `erp_apphub` | `receiveDeviceScanDisconnect`, confirmed lifecycle activity |

- Dispatch theo sourceKind/category/eventName/facts/deliveryKind. P0 đối chiếu exact field với fixture thật, không parse raw arguments lần nữa.
- Hai nhóm light trong bảng được seed thành bốn mã riêng: `green_light_on`, `green_light_off`, `red_light_on`, `red_light_off`.
- Một raw record có signal và business facts có thể tạo hai metrics; unique `(EventId, MetricCode)` trong batch, total summary chỉ một event.
- AppHub tag-read là secondary: ignored, không cộng `tag_read`. Không fuzzy-dedupe.
- Snapshot, `receiveDeviceOnline`, client-device callbacks và event ngoài bảng không tự tạo transition/metric. Known outside scope là ignored; unmapped/ambiguous là quality-only hoặc terminal failure theo policy.
- Connected count là observation count. Reconnect chỉ tăng cho `disconnected -> connected`; `unknown -> connected` không tính reconnect.
- Quantity/sensor value không cộng EventCount. Không thống kê unique tag/EPC hoặc sản lượng ở Sprint 3.
- `parsed_with_warnings` chỉ thống kê khi core facts hợp lệ; quality count commit cùng transaction.

### 6.2. Metadata

- CompanyId/DeviceId lấy từ canonical event; positive IDs bắt buộc cho device facts. Không gọi ERP trong hot path.
- Configured metadata authoritative; event display metadata chỉ bổ sung chỗ trống.
- Timezone dùng fixed offset UTC+7, label `Asia/Ho_Chi_Minh`; không phụ thuộc Windows/IANA availability hay machine timezone.
- Device chưa có catalog được tạo placeholder trong SQL transaction; `IsActive` null nếu thiếu evidence.
- Không thêm lịch sử timezone hoặc timezone migration trong Sprint 3.

### 6.3. Canonical readiness của checkout hiện tại

Static inspection khi lập Plan cho thấy `DeviceConnectionEventMapper`, `DeviceControlStateEventMapper` và `DeviceSensorStateEventMapper` kế thừa `AppHubOpaqueEventMapper`. Base mapper này hiện tạo facts rỗng, device null và parse.status=unmapped. Scanner mapper đã tạo connection facts. Đây là tình trạng source đang đọc, không suy diễn rằng môi trường UAT của đội khác cũng chạy đúng revision này.

Vì vậy, Sprint 2 test pass không tự chứng minh các metric connection/light/sensor đã có canonical input. Trước khi enable chúng:

1. Đối chiếu revision và BSON thực tế UAT với checkout.
2. Nếu đội khác đã hoàn thiện mapping, tích hợp/review thay đổi đó và chạy regression.
3. Nếu chưa có, tạo task prerequisite P0.5 hoàn thiện các mapper History Application tương ứng từ fixture đã xác nhận; giữ tenant/redaction/identity, không parse raw payload trong Statistics Worker và không rewrite history cũ.
4. Xác nhận DeviceId, exact boolean/state/sensor facts và delivery/time basis. `isConnected=false` không mặc nhiên là disconnected nếu payload thể hiện connecting/unknown; rule phải có fixture rõ.
5. Event cũ vẫn unmapped đi quality-only. Coverage của metric bắt đầu khi canonical input thực sự đủ; không gán zero cho giai đoạn chưa supported.

Các file prerequisite có thể ảnh hưởng: `src/DeviceEventHistory.Application/AppHub/Mapping/DeviceConnectionEventMapper.cs`, `DeviceControlStateEventMapper.cs`, `DeviceSensorStateEventMapper.cs`, cùng mapping tests/BSON tests tương ứng. Không sửa broad `AppHubOpaqueEventMapper` để tự đoán mọi callback. Các project Statistics vẫn độc lập với History implementation.

## 7. Incremental, overlap và source compatibility

### 7.1. High watermark và page cursor

`persistedAtUtc` được History Worker gán trước Mongo insert. Nó không phải server commit timestamp. Overlap giảm commit skew, không bảo đảm lossless vô điều kiện.

```text
H = high watermark đã commit, không bao giờ lùi
L = H.persistedAtUtc - OverlapWindow
U = UtcNow - ReadSafetyDelay, cố định cho cả sweep
P = page cursor cuối đã xử lý trong sweep
```

- Chưa có H: L là explicit `CoverageStartAtUtc`.
- Query `[L,U]`, sort `(persistedAtUtc ASC,eventId ASC)`, keyset strictly after P, LIMIT BatchSize; không Skip.
- P tiến qua mọi page kể cả toàn duplicates. Không tính lại L sau mỗi page.
- Commit page: inbox/outcomes/facts/checkpoint cùng transaction; H lấy max, P là cursor cuối page.
- Hết sweep ghi completion; sweep mới từ H hiện tại. Restart tiếp tục sweep, hoặc replay overlap bằng inbox nếu sweep tạm không còn hợp lệ.
- Index `(persistedAtUtc,eventId)` phục vụ cả V1 và V2.

Mặc định overlap 5 phút, safety delay 30 giây, poll 5 giây. Deep discovery quét toàn history còn giữ mỗi 6 giờ bằng cursor riêng; valid event chưa có inbox đi qua incremental writer. Nó bắt arrival sâu hơn overlap trong giới hạn retention và không đổi H chính.

### 7.2. Compatibility và audit

- Raw-log hiện là schemaVersion=1 nhưng có persisted/timeline/timeBasis; phải hỗ trợ V1 đủ field và V2.
- Main cursor dùng valid persisted timestamp + lowercase SHA-256. Unsupported schema có valid cursor vẫn nhận terminal outcome.
- Document thiếu/sai eventId/timestamp được `HistoryContractAuditHostedService` quét theo Mongo `_id`, ghi deterministic failure + quality với gate riêng.
- Audit dùng upper bound/page cursor và lặp retained window. Source hiện tạo ObjectId; foreign `_id` type phải được phát hiện/báo unsupported, không cast rồi silent skip.
- Statistics Worker không sửa Mongo. V1 cũ thiếu timestamp chỉ đi diagnostic/explicit bootstrap migration; không gán now giả.
- EventIdentity validate đúng 64 lowercase hex rồi đổi `binary(32)`.

## 8. SQL persistence, lease và state

### 8.1. Fencing và writer gate

- Một active owner theo projection/version/partition. Lease dùng SQL server UTC; epoch tăng khi cấp owner mới, không tăng mỗi heartbeat.
- Mọi transaction projection lấy cùng SQL application lock/resource, kiểm tra owner/epoch/unexpired trên checkpoint row; stale owner rollback.
- Heartbeat dùng connection riêng. Mất lease thì cancel work và không commit token cũ.
- Transaction giữ writer gate và checkpoint lock phải ngắn hơn lease còn lại; kiểm tra expiry trước commit. Locking phải bảo đảm owner khác không đổi epoch giữa check và commit. Heartbeat bị chặn bởi short transaction không được tạo deadlock vòng; lock acquisition order thống nhất và có integration test.
- In-process semaphore chỉ tối ưu; SQL gate/fencing là authority cho service/manual run.
- Manual mode cùng version phải pause service để acquire, hoặc gửi request cho owner đang chạy.

### 8.2. Atomic batch

```text
Mongo read/map
BEGIN SQL TRANSACTION
  writer gate + fencing; load revision/state cursors
  insert ProcessedEvent, OUTPUT identities mới
  filter contributions/quality/state theo identities mới
  apply grouped metrics + unique device summary + state
  write requests/failure/coverage
  advance sweep/checkpoint; increment DataRevision
COMMIT
```

- Không pre-aggregate làm mất contribution -> EventId trước inbox gate.
- State input sort `(TimelineAtUtc,EventId)` per device/stateType; late transition enqueue request.
- State math ở Domain/Application; SQL writer load cursor và ghi result trong cùng bounded session. Không Mongo I/O khi SQL transaction mở.
- Chia batch theo contiguous source segments khi touched groups vượt limit; checkpoint chỉ tới segment có terminal outcomes.
- Duplicate inbox là expected; constraint khác không phải duplicate success.
- Retry dùng cùng identities; ambiguous commit dựa inbox/checkpoint để xác minh.

### 8.3. Thời lượng

- StateType V1: `device_connection`, `scanner_connection`; row/cursor riêng. Không tạo cả hai stream nếu chưa có mapping/evidence.
- Bucket `[00:00,00:00 hôm sau)` Việt Nam. `CalculatedThroughAtUtc=min(asOfUtc,BucketEndAtUtc)`.
- `OnlineSeconds + OfflineSeconds + UnknownSeconds` bằng số giây từ bucket start tới calculated-through; ngày đang chạy không ghi thời gian tương lai.
- Không có predecessor: trước observation đầu tiên là unknown. Không event mới: tiếp tục state cuối quan sát, không khẳng định state vật lý chắc chắn đúng.
- Duration refresh mỗi phút, xử lý theo page và split midnight; accounting edge commit cùng daily/cursor.
- Refresh/event dùng cùng gate. Event có timeline trước accounting edge tạo reconciliation.
- Giữ thứ tự timestamp gốc; duration dùng hiệu absolute UTC second endpoints. Không floor từng lát độc lập.
- Repeated state tăng observation count nhưng không reset StateSince. Same timestamp tie-break EventId.
- Future timeline vượt tolerance đi failure/quality; dirty day chưa finalize.

## 9. Reconciliation, coverage và retention

### 9.1. Exact replacement không race

1. Claim request; trong short SQL transaction chụp DataRevision R, inbox identities/outcomes target range, seed và coverage vào staging.
2. Đọc Mongo bounded pages cho đúng staged identities; event mới ngoài snapshot không thuộc recompute này.
3. Nếu thiếu staged identity do TTL/deletion thì dừng coverage failure, không replace tập thiếu.
4. Tính metrics/summary/state đúng mapping version và stream staging, không giữ cả range trong RAM.
5. Publish dưới gate/fencing; DataRevision/seed/generation/coverage phải còn bằng snapshot. Stale thì bỏ staging và retry.
6. Replace đúng scope, update cursor khi range chạm current edge an toàn, complete đúng generation; không xóa inbox/đổi incremental checkpoint.

Để reconciliation có thể tiến khi incremental/refresh liên tục đổi revision, coordinator cho data writers dừng sau batch trong bounded maintenance slice. Chunk theo device/day; vượt thời gian thì hủy publish/retry, ưu tiên SLO.

Deep discovery admission chạy trước. Reconcile sửa tập đã admission, nên event mới không vừa nằm trong replacement vừa bị delta cộng lại.

### 9.2. Request và propagation

- Enqueue cùng transaction nhận late event. Coalesce range theo projection/company/device/stateType dưới SQL lock; không giả định unique index tự gộp overlap.
- Request Processing không bị mở rộng âm thầm: tăng dirty generation hoặc tạo successor. Publish chỉ complete generation đã chụp.
- Claim có expiry/epoch; startup reclaim claim hết hạn; retry có NextAttempt/attempt cap.
- Predecessor từ Mongo; nếu đã TTL thì dùng SQL closing-state anchor ngày trước khi coverage complete/not dirty đúng version.
- Replay tới transition chặn ảnh hưởng/current edge; range lớn chia continuation, không bỏ phần còn lại vì MaxRangeDays.
- Late event sửa trước trusted seed nhưng source mất thì báo unrecoverable, không đoán.

### 9.3. Retention 7 ngày

- TTL theo `persistedAtUtc`, không theo timelineAtUtc.
- Rolling reconcile 3 ngày mỗi giờ; deep retained discovery 6 giờ. Không bắt đầu recompute sát expiry, mặc định cần 24 giờ headroom.
- Membership check lại trước publish để phát hiện TTL trong lúc đọc. Không có bootstrap manifest/inbox coverage thì không tuyên bố complete chỉ vì query trả hết dữ liệu còn lại.
- Coverage ghi partial first day, missing window và failed repair; SQL result không tự thành complete sau TTL.
- Source mất: giữ SQL hiện có, chặn replacement/backfill thiếu source và ghi `STAT_SOURCE_RETENTION_GAP`/`STAT_REBUILD_RANGE_UNAVAILABLE`.
- Mất dữ liệu trong downtime vượt retention được ghi explicit coverage gap dựa trên last completed sweep/manifest và retention boundary. Không biết có bao nhiêu event đã mất thì để unknown, không tự ghi số lượng zero. Caught-up sau gap không xóa gap lịch sử.
- Event mới có business date cũ vẫn cộng count; state không sửa được thì giữ prior result và đánh dấu unrecoverable.
- Không purge ProcessedEvent active version. Raw-log replay sau TTL vẫn không tăng count lần hai.
- Facts/state/coverage/seed giữ dài hạn. Run audit có thể giữ 180 ngày nhưng không xóa evidence correctness chưa giải quyết.
- Không đổi retention ingestion_failures/checkpoints.

### 9.4. Bootstrap/backfill/rebuild

- Version mới bắt buộc explicit CoverageStartAtUtc và verify source available.
- Bootstrap dùng cùng admission/transaction. First partial day được ghi partial; predecessor không biến missing count thành complete.
- Backfill cùng version admission event thiếu rồi exact reconcile; không insert aggregate trực tiếp.
- Rebuild N+1 có registry/coverage riêng, retained bootstrap, tail catch-up, verify và cutover. Không copy old aggregates rồi gán mapping version mới.
- Full-history rebuild quá 7 ngày không hỗ trợ nếu source đã mất. Có thể giữ version cũ cho old days hoặc cutover version mới trong scope có coverage.
- Không thêm history archive ở Sprint 3.

## 10. Configuration và startup

```json
{
  "DeviceEventStatistics": {
    "Enabled": false,
    "WorkerId": "device-event-statistics-worker-01",
    "DatabaseSettings": {
      "MongoDb": { "ConnectionString": "", "DatabaseName": "device_event_history", "HistoryCollection": "device_event_history" },
      "SqlServer": { "ConnectionString": "", "DatabaseName": "device_event_statistics", "SchemaName": "device_stats", "CommandTimeout": "00:00:30" }
    },
    "Projection": {
      "Name": "device_event_daily", "Version": 1, "Mode": "Incremental", "CoverageStartAtUtc": null,
      "BatchSize": 1000, "PollInterval": "00:00:05", "OverlapWindow": "00:05:00",
      "ReadSafetyDelay": "00:00:30", "DeepDiscoveryInterval": "06:00:00",
      "LeaseDuration": "00:02:00", "LeaseRenewInterval": "00:00:20",
      "PersistenceRetryCount": 5, "RetryMinDelay": "00:00:01", "RetryMaxDelay": "00:00:30",
      "FutureEventTolerance": "00:05:00", "ShutdownTimeout": "00:00:30"
    },
    "State": { "DurationRefreshInterval": "00:01:00" },
    "Reconciliation": {
      "Enabled": true, "Interval": "01:00:00", "LookbackDays": 3, "MaximumRangeDays": 3,
      "MaximumPublishPause": "00:02:00", "FinalizePreviousDayLocalTime": "02:00:00"
    },
    "Retention": { "HistoryDays": 7, "MinimumRebuildHeadroom": "1.00:00:00", "PurgeActiveProcessedEvents": false },
    "Observability": { "LagWarningAfter": "12:00:00", "LagUnhealthyAfter": "1.00:00:00" }
  }
}
```

Environment overrides:

```text
DEVICE_EVENT_STATISTICS_MONGODB_CONNECTION_STRING
DEVICE_EVENT_STATISTICS_SQLSERVER_CONNECTION_STRING
```

- Override/redaction giống History Worker. State options phải có consumer/validator.
- Validate SQL connection database khớp DatabaseName, không ghi nhầm master/ERP DB.
- CoverageStart bắt buộc khi tạo version, không yêu cầu lại khi resume. Manual mode yêu cầu range/scope/version explicit.
- Mapping/ownership versions phải khớp ProjectionDefinition; thay policy tạo version mới hoặc corrective run có audit.
- Tenant/source scope là cấu hình bất biến theo projection version. P1 bổ sung `ScopeOptions` nếu UAT triển khai một phần; không đổi filter trên cùng global checkpoint rồi bỏ mất tenants đã đi qua. Mở rộng scope phải explicit backfill coverage tương ứng hoặc tạo version mới.
- Startup: validate -> ping Mongo/SQL -> verify migrations/indexes/registry -> acquire lease -> checkpoint/coverage -> processors. Hosted services chờ startup barrier.
- BackgroundService không tự migrate production. Deploy identity chạy script; Worker verify/fail not-ready.
- Disabled thì idle/exit rõ. Lease đang được owner khỏe giữ thì standby có backoff.

## 11. Các phase triển khai chi tiết

Mỗi phase dưới đây có mục tiêu, đầu vào, file chịu trách nhiệm, các task cụ thể và deliverable/acceptance. Task ID P0.1-P9.7 được giữ để team phân công và theo dõi. Các file liệt kê dưới từng phase nằm trong cây mục 5; chỉ các file History được ghi rõ mới là file hiện tại cần sửa.

### Phase 0 - Đồng bộ contract và chuẩn bị dữ liệu đầu vào

**Mục tiêu:** chuyển các quyết định đã chốt thành contract thống nhất để team không tự chọn hai cách triển khai khác nhau.

**Đầu vào:** Design/Schema hiện tại, quyết định ở mục 2, delta D01-D12 và kết quả Sprint 2 UAT.

**File/tài liệu chính:** `docs/Sprint-3-Design.md`, `docs/Sprint-3-Schema.md`, fixtures V1/V2 trong test project; các History AppHub mappers nêu ở mục 6.3 nếu cần hoàn thiện.

#### Task P0.1 - Đồng bộ Design và Schema theo phạm vi đã chốt

- Áp dụng lần lượt D01-D12; ghi rõ SQL database riêng, context riêng, UTC+7 và Health chuyển Sprint 4.
- Thay mô tả snapshot cũ bằng device summary và state daily riêng theo `StateType`.
- Sửa duration invariant: ngày đang chạy chỉ tính tới `CalculatedThroughAtUtc`; ngày đã hết mới tính đủ ngày.
- Cập nhật overlap, revision-guarded reconciliation và retention 7 ngày; loại bỏ yêu cầu timezone history/Hangfire không còn thuộc phạm vi.
- Kiểm tra tên bảng, field, status và class giữa ba tài liệu; không giữ hai phiên bản contract trái nhau.

#### Task P0.2 - Hoàn thiện database và component contracts

- Viết DDL đầy đủ cho các bảng mới/đổi ở mục 4: column type, nullability, PK/unique/check/default và indexes.
- Khóa `ProcessedEvent` dispositions, coverage statuses, request lifecycle và error codes vào registry chung.
- Xác định shape TVP cho từng nhóm input; contribution phải giữ EventId để lọc sau idempotency gate.
- Xác định input/output của reader, batch writer, state calculator và recompute store theo mục 5.1.
- Ghi rõ component nào sở hữu connection/transaction/lease và component nào chỉ tính toán; không để hai store tự commit một batch.

#### Task P0.3 - Chuẩn bị fixtures và bảng ánh xạ metric

- Lấy ít nhất một canonical BSON V1 raw-log và V2 AppHub cho từng nhóm metric được enable; lưu fixture đã redaction.
- Với mỗi fixture, ghi sourceKind, category, eventName, deliveryKind, device identity, facts, parse status và time basis.
- Lập bảng fixture -> metric contributions -> state observation -> expected SQL outcome.
- Thêm variants: raw record có nhiều facts, warning, missing required field, unknown state, snapshot và AppHub tag-read secondary.
- Ghi revision/môi trường tạo fixture và tham chiếu bằng chứng Sprint 2 pass; không commit secrets/raw payload nhạy cảm.

#### Task P0.4 - Định nghĩa coverage và dữ liệu lịch sử ban đầu

- Ghi rõ CoverageStartAtUtc cho version mới phải là tham số rollout, không hard-code ngày hoặc lấy mặc định từ máy.
- Định nghĩa ngày đầu thiếu dữ liệu là partial; có predecessor chỉ giúp state opening, không chứng minh event count đầy đủ.
- Mô tả hành vi khi source hết retention: giữ SQL hiện tại, đánh dấu gap, từ chối rebuild không đủ evidence.
- Khóa acceptance cho unknown opening, hai state types, metric chưa đủ canonical input và Health fields null.
- Các quyết định đã được người dùng chốt không cần xin lại; giá trị theo môi trường được ghi thành deployment parameters.

#### Task P0.5 - Bảo đảm AppHub có canonical facts để thống kê

- Đối chiếu mapper trong checkout với revision/BSON UAT vì connection/control/sensor hiện còn kế thừa opaque mapper.
- Nếu đội Sprint 2 đã hoàn thiện mapping, tích hợp và review thay đổi; nếu chưa, hoàn thiện đúng các mapper nêu ở mục 6.3 bằng fixture xác nhận.
- Map positive DeviceId, confirmed state/control/sensor facts, deliveryKind và time basis; không đoán disconnected từ boolean thiếu ngữ cảnh.
- Giữ tenant/privacy/identity behavior và fallback unknown/unmapped cho variant chưa xác nhận; không sửa broad opaque base để đoán mọi callback.
- Chạy mapping/BSON/raw-log regression, ghi revision và thời điểm metric bắt đầu có đủ dữ liệu. History cũ unmapped vẫn là quality-only.

#### Deliverable và acceptance của Phase 0

- Design/Schema thống nhất với Plan, DDL delta đủ để viết migrations.
- Mỗi metric enable có fixture và expected outcome; scope chưa đủ input được ghi rõ.
- P1/P2 có thể bắt đầu sau contract review; P3 mapping và UAT của metric liên quan phải có P0.5.
- Không coi toàn bộ metric scope hoàn thành chỉ vì transport Sprint 2 đã pass.

### Phase 1 - Khởi tạo solution, configuration và database contexts

**Mục tiêu:** có bộ khung Statistics Worker đúng layer, cấu hình được và kiểm tra dependencies trước khi chạy projection.

**Phụ thuộc:** contract P0.1/P0.2. Phase này chưa xử lý history thành statistics.

**File chính:** các `.csproj`, `Program.cs`, `Configuration/*`, `StartupInitializationHostedService.cs`, `MongoHistoryDbContext.cs`, `SqlStatisticsDbContext.cs`, `SqlProjectionSession.cs`, `DependencyBoundaryTests.cs`.

#### Task P1.1 - Tạo projects và references

- Tạo Domain/Application/Infrastructure/Worker và ba test projects, thêm vào `DeviceEventHistory.sln`.
- Dùng net10.0, nullable, implicit usings và test framework theo convention hiện có.
- Application reference Domain; Infrastructure reference Application/Domain; Worker compose Application/Infrastructure.
- Package MongoDB/SqlClient chỉ ở Infrastructure; không reference History Worker/Infrastructure hoặc ERP legacy.
- Xóa template Class1/UnitTest1 mới nếu không dùng; không sửa projects History chỉ để đổi cấu trúc chung.

#### Task P1.2 - Bind options, validate và đăng ký DI

- Tạo Worker/Projection/State/Reconciliation/Retention/Observability/Metadata options; bind Mongo/SQL dưới DatabaseSettings.
- Implement environment override cho hai connection strings, giữ sample không có secret.
- Validator kiểm tra database/schema/mode, batch/retry/delay, lease/renew interval, retention headroom và manual range.
- Validate CoverageStart cho version mới; resume dùng definition đã lưu. Nếu triển khai limited scope, bind scope immutable theo projection version.
- Tách DI registrations theo configuration, Mongo, SQL, projection, state, reconciliation và observability; không dồn tất cả vào Program.cs.
- Redactor chỉ báo connection configured và thông tin không nhạy cảm; viết validation/redaction tests.

#### Task P1.3 - Implement Mongo/SQL contexts và transaction session

- MongoHistoryDbContext cung cấp collection access và ping; reusable Mongo client, read-only credential.
- SqlStatisticsDbContext tạo connection theo operation, verify target database và mở SqlProjectionSession.
- Session sở hữu connection/transaction; commit/rollback/dispose/cancellation rõ ràng, không chia sẻ một SqlConnection giữa hosted services.
- Stores tham gia session do caller cung cấp; context không chứa metric mapping hoặc state calculation.
- Test resource disposal và cancellation; SQL schema-specific preflight được nối hoàn chỉnh ở P2.

#### Task P1.4 - Startup preflight và readiness barrier

- Startup lần lượt validate options, ping Mongo/SQL, verify expected schema/index/registry rồi mới mở startup barrier.
- Data hosted services chờ barrier; không đọc history trong lúc migration/schema đang không hợp lệ.
- Worker disabled phải exit/idle rõ và không mở processing loop.
- Preflight thất bại có stable error và not-ready; không tự chạy production migration hoặc đổi database.
- Chuẩn bị điểm acquire lease/load checkpoint sau barrier để P4 nối runtime.

#### Task P1.5 - Khóa architecture bằng tests

- Kiểm tra project dependency direction và package/type không rò vào Domain/Application.
- Kiểm tra không reference History implementation hoặc legacy ERP/RFID projects.
- Dùng configuration tests để chứng minh options không thực hiện network I/O.
- Build cả solution; xử lý compile/reference issues trước khi chuyển feature code sang phase sau.

#### Deliverable và acceptance của Phase 1

- Solution có projects/config sample/context/DI hoạt động và architecture tests pass.
- Invalid config fail fast, secret không lộ; disabled không busy-loop.
- Context/session phân tách rõ; chưa có SQL write trong History ingestion pipeline.

### Phase 2 - SQL schema, batch contracts và lease infrastructure

**Mục tiêu:** tạo nền persistence có migration, transaction và fencing đủ để phase sau ghi dữ liệu an toàn.

**Phụ thuộc:** P0 schema contract và P1 contexts. Logic metric/state được tích hợp ở P4-P6.

**File chính:** `SqlServer/Migrations/001-005`, `SqlProjectionSession.cs`, `SqlProjectionLeaseStore.cs`, `SqlProjectionCheckpointStore.cs`, `ProjectionTvpMapper.cs`, `SqlRetryPolicy.cs`, `SqlSchemaVerifier.cs`, `Apply-SqlMigrations.ps1`.

#### Task P2.1 - Viết migrations cho toàn bộ statistics schema

- Tạo schema/version history trước, sau đó registry/dimension, facts/state, inbox/checkpoint và recovery/staging tables.
- Áp dụng đúng bigint/datetime2/binary identity, nullable health và duration/coverage constraints ở P0.
- Tạo PK/unique/indexes cho device/day queries, inbox lookup, pending requests và RunId staging.
- Migration chạy trên database mới và chạy lại an toàn; không tự drop data/index để che schema mismatch.
- Viết SQL integration tests cho duplicate keys, invalid counters/dates/statuses và snapshot calculation bounds.

#### Task P2.2 - Seed metric set và quản lý projection version

- Seed từng MetricCode V1, group/unit/primary source/mapping version theo mục 6; tách bốn light metrics thành mã riêng.
- Seed theo MetricSetVersion + MetricCode, chạy lại không nhân bản hoặc đổi definition đã dùng.
- ProjectionDefinition được tạo qua bootstrap với explicit coverage/timezone/ownership versions, không tự gán active cho mọi deployment.
- Từ chối resume bằng mapping version khác registry; giữ hai version cùng tồn tại phục vụ rebuild/rollback.

#### Task P2.3 - Tạo TVP và batch parameter mapping

- Định nghĩa structured parameters cho event outcomes, metric contributions, device summary, state inputs/results, quality, failures và checkpoint.
- Mỗi contribution giữ EventId và target key; chưa group bỏ EventId trước SQL new-ID gate.
- Map SHA-256 sang binary(32), UTC/date đúng type, null rõ; không serialize toàn object/raw payload vào SQL.
- TVP có unique input identity/key cần thiết để phát hiện duplicate contribution trong cùng batch.
- Test round-trip values, nulls và size limits; mapper không tự resolve business state.

#### Task P2.4 - Tạo SQL operations cho lease và batch persistence

- Implement acquire/renew/release bằng SQL server time; epoch tăng khi cấp ownership mới.
- Tạo operations insert inbox OUTPUT new IDs, update/insert facts, advance checkpoint và conditional DataRevision.
- Tạo operations claim/coalesce/retry request và prepare/publish staging theo contract P0; business orchestration nối ở P6.
- Tất cả data mutations dùng cùng session/gate/fencing; procedure không tự commit tách khỏi transaction caller.
- Viết tests owner conflict, expired lease, stale epoch và rollback; actual projection effects được kiểm chứng thêm ở P4/P6.

#### Task P2.5 - SQL retry và lock ordering

- Phân loại transient SQL timeout/deadlock/connection errors; retry capped exponential backoff có jitter và cancellation.
- Quy định lock order thống nhất: projection writer gate -> checkpoint/fencing -> touched target rows.
- Heartbeat có connection riêng; data transaction bounded, kiểm tra remaining lease/expiry trước commit.
- Không retry constraint/data/invariant error như transient; không coi mọi unique conflict là inbox duplicate.
- Test hai connections tranh owner, transaction giữ lock và lease renewal để tránh deadlock vòng.

#### Task P2.6 - Công cụ migration và schema verifier

- Apply-SqlMigrations nhận target database/schema explicit, kiểm tra resolved target trước execution.
- Lưu migration ID/checksum/time; fail nếu script đã apply bị đổi checksum.
- Dùng migration identity riêng; runtime chỉ verify expected schema/version và permissions.
- Ghi cách chạy/retry/kiểm tra migration trong deploy README; không đặt credential vào command log.

#### Deliverable và acceptance của Phase 2

- SQL database test khởi tạo được từ migrations, seed lặp không đổi data cũ.
- PK/check/index/TVP đúng contract; lease fencing và rollback tests pass.
- Phase sau có session/store operations để ghép atomic projection, không phải tự tạo transaction protocol khác.

### Phase 3 - Mongo readers, compatibility và metric mapping

**Mục tiêu:** biến history V1/V2 thành projection outcomes đúng nguồn, đúng ngày và có cursor đọc tiếp rõ ràng.

**Phụ thuộc:** P1; contract fixtures P0.3/P0.5. Integration với SQL cần P2.

**File chính:** `MongoDb/Reading/*`, `HistoryDocumentMapper.cs`, `History/*`, `ProjectionSweep.cs`, `Mapping/*`, `LocalStatisticsDateResolver.cs`, metadata resolver và Mongo index deployment script.

#### Task P3.1 - Minimal history input và BSON mapper

- Query chỉ fields ở HistoryEvent contract: identities, timestamps, discriminators, device và confirmed facts; không fetch raw payload trong hot path.
- Map V1 raw-log có cursor fields và V2 AppHub; handle Int32/Int64/null/missing mà không phụ thuộc CLR entity của History Worker.
- Giữ timeline/timeBasis của source; không giả thời điểm mới cho legacy document thiếu timestamp.
- Chuyển known malformed fields thành contract diagnostic có source identity; programming exceptions vẫn được surface.
- Golden tests so fixture BSON với HistoryEvent và expected diagnostic.

#### Task P3.2 - Incremental reader có overlap và keyset paging

- Implement H/L/U/P theo mục 7; giữ U/L cố định trong một sweep.
- Query theo persisted time rồi ordinal EventId; trang sau strictly after P và vẫn trong sweep bounds.
- Trả page cuối/caught-up rõ; all-duplicate page vẫn có next cursor và không restart từ L.
- Persist/reload sweep state qua checkpoint abstraction; H không lùi khi replay overlap.
- Test nhiều event cùng timestamp, >BatchSize trong overlap, empty page, restart giữa sweep và cancellation.

#### Task P3.3 - Deep discovery và source contract audit

- Deep discovery quét retained window mỗi 6 giờ bằng page cursor riêng; valid unseen event gửi qua same admission writer.
- Audit tìm invalid/missing eventId/timestamp và foreign source ID type; diagnostic ghi qua deterministic failure gate.
- Lưu tiến độ sweep phụ, lặp retained range để bắt document xuất hiện trễ; không dùng cursor phụ để sửa H chính.
- Bound page/query time, có cancellation và metrics; không load một tuần raw documents vào RAM.
- Test arrival cũ hơn overlap, malformed documents và restart sweep; persistence thực tế nối với P4.

#### Task P3.4 - Eligibility, ownership và metric registry

- Implement dispositions aggregated/ignored/quality_only/failed_terminal với thứ tự kiểm tra identity, schema, source scope, core facts và tenant/device/time.
- Tạo registry dispatch theo sourceKind/category/eventName/facts/deliveryKind; duplicate mapper key fail startup/test.
- RawFileMetricMapper xử lý tag/business; AppHub mappers xử lý confirmed connection/control/sensor/scanner.
- AppHub tag-read secondary đi ignored; snapshots không tạo transition; unmapped chỉ quality.
- Một event có nhiều metrics nhưng mỗi MetricCode xuất hiện tối đa một lần; unique-device summary vẫn một event.
- Golden tests theo fixtures, đặc biệt warning không làm core facts mất tin cậy và connecting khác disconnected.

#### Task P3.5 - Ngày thống kê và metadata

- LocalStatisticsDateResolver dùng fixed UTC+7 để trả StatisticsDate/bucket start/end từ timeline UTC.
- Metadata resolver ưu tiên cấu hình; event display fields chỉ bổ sung giá trị chưa có.
- Device/company không hợp lệ đi failure/quality theo contract; không suy từ FileId hoặc source filename.
- Placeholder dimension không tự gán active/operating schedule; creation do SQL writer thực hiện cùng fact transaction.
- Test UTC đổi ngày Việt Nam, missing metadata, null display và quality-only time fallback.

#### Task P3.6 - Index deployment và query verification

- Tạo/verify cursor index phục vụ cả V1/V2; tránh partial index chỉ nhận schemaVersion=2.
- Thử range index theo company/device/timeline/eventId cho reconcile/predecessor queries trên UAT.
- Dùng explain/query evidence để chọn index bổ sung cần thiết; không tạo mọi permutation.
- Deployment identity tạo index, runtime verifier báo missing/incompatible index theo policy.
- Lưu index names/query shapes vào runbook và integration test.

#### Deliverable và acceptance của Phase 3

- Reader phân trang/restart được, raw V1 không bị loại; source lỗi có diagnostic route.
- Fixtures cho ra đúng metrics/state inputs/quality outcomes theo source ownership.
- Query memory bounded và UAT query plan có evidence; chưa tự ghi facts ngoài writer của P4.

### Phase 4 - Incremental projection và atomic SQL persistence

**Mục tiêu:** hoàn thành vertical slice history -> SQL counts/quality/checkpoint, chứng minh retry không cộng trùng trước khi thêm duration.

**Phụ thuộc:** P2 SQL operations và P3 readers/mappers.

**File chính:** `StatisticsProjectionPipeline.cs`, `IncrementalProjectionHandler.cs`, `ProjectionBatch.cs`, `SqlStatisticsBatchWriter.cs`, checkpoint stores, `IncrementalProjectionHostedService.cs`, `ProjectionLeaseCoordinator.cs`, `LeaseHeartbeatHostedService.cs`.

#### Task P4.1 - Ghép reader và mapper thành projection batch

- IncrementalProjectionHandler load sweep/checkpoint, gọi reader một page rồi map từng event thành terminal outcome.
- Giữ source order và EventId của mọi contribution; validation bảo đảm không có outcome vừa failed vừa tạo device metric.
- Giới hạn batch size/touched groups; nếu chia nhỏ thì commit theo contiguous source segments.
- Known data error tạo failure outcome; transient dependency error giữ checkpoint để retry; programming invariant lỗi thì stop/degrade và surface.
- Batch model mang expected checkpoint, lease token, page bounds và prepared outcomes, không mang SqlConnection/BSON.

#### Task P4.2 - Ghi inbox, facts, quality và failures trong một transaction

`SqlStatisticsBatchWriter` thực hiện theo thứ tự:

1. Mở session, lấy writer gate và kiểm tra fencing.
2. Insert inbox cho valid identities; lấy danh sách IDs thực sự mới.
3. Chỉ tính deltas từ IDs mới: metric counts, unique-device summary, warnings/time-basis/quality.
4. Tạo/bổ sung dimension metadata rồi upsert daily rows theo target keys.
5. Ghi terminal failures và invalid-identity failure gate nếu có.
6. Cập nhật checkpoint/sweep cùng transaction; commit rồi mới trả CommittedBatchResult.

- Group updates theo daily key, không một SQL round trip cho mỗi event ở production path.
- Duplicate inbox không cộng lại bất cứ metric/quality/summary nào; failure gate cũng idempotent.
- Counts báo ra telemetry lấy từ committed result, không dùng số prepared events.
- Tests gồm một event nhiều metrics, secondary ignored, quality-only, terminal failure và duplicate trong cùng batch.

#### Task P4.3 - Hoàn thiện checkpoint, revision và crash recovery

- Page cursor tiến tới event cuối có terminal SQL outcome; H lấy max để replay không kéo cursor lùi.
- Ignored/quality-only events vẫn có inbox; invalid identity dùng FailureId gate và audit cursor phù hợp.
- DataRevision tăng khi event admission hoặc projection/state data thay đổi; heartbeat và read-only poll không làm đổi data revision.
- Crash trước commit giữ old checkpoint; crash sau commit hoặc response không rõ được xử lý bằng retry cùng IDs và durable inbox.
- Viết integration tests fault injection trước write, trong transaction và sau commit; không suy thành công từ việc method đã được gọi.

#### Task P4.4 - Runtime loop, heartbeat và backpressure

- Sau startup barrier, acquire lease, load definition/checkpoint rồi start incremental loop.
- Full page tiếp tục xử lý; caught-up dùng cancellable delay; SQL chậm thì ngừng đọc thêm, không tăng queue vô hạn.
- Heartbeat renew owner/epoch theo cấu hình; lease lost hủy work và ngăn commit token cũ.
- Nối deep discovery/audit với cùng writer/session protocol, không cho sweep phụ tự ghi ngoài fencing.
- Shutdown ngừng schedule page mới, drain batch hiện tại trong timeout, commit/rollback xong mới release lease.

#### Deliverable và acceptance của Phase 4

- Fixture raw V1/AppHub V2 tạo đúng SQL facts/quality và checkpoint.
- Duplicate, retry, crash và ambiguous commit không tăng count lần hai hoặc làm checkpoint vượt dữ liệu chưa ghi.
- SQL outage giữ memory bounded và không làm History Worker phụ thuộc SQL.
- Đây là gate bắt buộc trước P5; chưa đạt idempotency thì chưa nối state/reconciliation.

### Phase 5 - State transitions, thời lượng và refresh ngày đang chạy

**Mục tiêu:** lưu device/scanner connection riêng và tính thời lượng đúng theo observed state, kể cả không có event mới.

**Phụ thuộc:** P4 atomic persistence; state inputs đã có fixture từ P3.

**File chính:** Domain `State/*`, `SqlStatisticsBatchWriter.cs`, `SqlDurationRefreshStore.cs`, `DurationRefreshHostedService.cs`, state/coverage models và tests.

#### Task P5.1 - Implement bộ tính state thuần

- StateDurationCalculator nhận cursor, ordered new observations, business bucket và as-of; trả duration changes, cursor mới và dirty ranges.
- Connected -> connected chỉ tăng observation count; không reset StateSince hoặc mở interval mới.
- Connected -> disconnected đóng online interval; disconnected -> connected đóng offline và tăng reconnect.
- Unknown -> connected không tính reconnect; thiếu predecessor thì khoảng trước observation đầu tiên là unknown.
- Giữ original timestamp order, EventId tie-break; duration dùng chung endpoint-second boundaries để tổng các lát bảo toàn.
- Unit tests độc lập DB cho repeated state, same timestamp, sub-second intervals và midnight.

#### Task P5.2 - Nối state calculation vào atomic batch

- Load state cursors của touched device/stateType trong cùng SQL session sau fencing.
- Chỉ đưa observations của IDs mới qua calculator; duplicate không tác động cursor hoặc duration.
- Persist DeviceStateDaily và DeviceStateCursor cùng event counts/inbox/checkpoint.
- Dùng grain riêng `device_connection` và `scanner_connection`; không ghép hai loại vào một trạng thái cuối.
- `LastTimelineAtUtc/LastEventId` phản ánh event observation; `AccountedThroughAtUtc` phản ánh phần duration đã tính.

#### Task P5.3 - Refresh duration khi chưa có transition mới

- DurationRefreshHostedService mỗi phút chọn known streams theo bounded pages.
- Tính từ accounting edge tới min(as-of, bucket end), nối tiếp sang ngày mới nếu cần.
- Cập nhật daily result và edge trong một fenced transaction; restart/retry không tính lại đoạn cũ.
- Không tạo stream giả cho device/type chưa có evidence; không coi không có event mới là disconnected.
- Test nhiều lần refresh, restart giữa pages, idle qua midnight và stopped worker catch-up.

#### Task P5.4 - Nhận diện late/out-of-order và đánh dấu dirty

- So observation với business cursor bằng timestamp + EventId, đồng thời kiểm tra có nằm trước accounting edge đã tính hay không.
- Event count vẫn admission một lần; duration không sửa bằng estimated delta khi thứ tự không còn an toàn.
- Tạo/gộp ReconciliationRequest từ ngày event đến affected edge trong cùng transaction; giữ evidence event gây dirty.
- Các row bị ảnh hưởng có IsDirty, chưa finalize. Request store basic đã có ở P2; lifecycle/recompute hoàn chỉnh ở P6.
- Test event đến sau refresh nhưng timeline cũ, late qua nhiều ngày và repeated delivery của cùng late event.

#### Task P5.5 - Snapshot, coverage và partial-day semantics

- DeviceDailySnapshot giữ unique-event summary; DeviceStateDaily giữ state durations/observations.
- CalculatedThroughAtUtc không vượt now/bucket end; total duration chỉ bằng đoạn đã tính.
- Lưu opening evidence và coverage; unknown observed state khác thiếu coverage vì Mongo đã xóa source.
- Giữ tất cả Health* null ở Sprint 3; không thêm scorer vào phase này.
- Test SQL constraint và các row dirty/finalized/partial; không finalize chỉ vì đến ngày mới khi còn pending repair.

#### Deliverable và acceptance của Phase 5

- 08:00 connected, 10:00 connected, 11:00 disconnected cho 2 connected observations và 3 giờ online.
- Device/scanner độc lập; refresh idempotent, midnight và phần ngày chưa kết thúc đúng.
- Late transition tạo request durable; không negative duration hoặc tự ghi thời gian tương lai.

### Phase 6 - Reconciliation chính xác và quản lý coverage

**Mục tiêu:** sửa ngày bị ảnh hưởng bởi late event mà không mất kết quả incremental mới và không tính lại từ source thiếu.

**Phụ thuộc:** P5 state/dirty requests, P2 staging/SQL operations, P3 range reader.

**File chính:** `ReconciliationCoordinator.cs`, `ExactRangeRebuilder.cs`, `ProjectionCoveragePolicy.cs`, `ForwardStatePropagation.cs`, `SqlProjectionRebuildStore.cs`, `SqlReconciliationRequestStore.cs`, `ReconciliationHostedService.cs`.

#### Task P6.1 - Hoàn thiện durable request lifecycle

- Implement enqueue/coalesce/claim/renew/complete/fail/retry theo state machine đã khóa.
- Coalesce theo projection/company/device/stateType dưới SQL lock; không dùng unique index thay range merging.
- Processing request có owner/epoch/expiry và generation; request mới làm range lớn hơn phải tạo successor hoặc tăng generation.
- Startup reclaim expired claims; transient retry có NextAttemptAtUtc/attempt limit; permanent coverage failure cần action rõ.
- Test crash sau claim, hai claimers, update range trong Processing và complete token/generation cũ.

#### Task P6.2 - Chụp membership, revision và kiểm tra source

- Dưới short writer gate, chụp DataRevision R, inbox identities/outcomes đúng target range, opening seed và coverage vào staging theo RunId.
- Thả SQL gate rồi đọc Mongo bounded pages; chỉ facts thuộc staged membership được dùng cho run này.
- Đối chiếu staged eligible identities với source đã fetch; thiếu do TTL/deletion không được coi là event count zero.
- Validate requested range/retention headroom và opening evidence; insufficient coverage trả explicit failure trước publish.
- Test event vừa tới sau snapshot, source bị xóa giữa pages và resume/cleanup abandoned staging.

#### Task P6.3 - Recompute metrics và state từ đầu

- Dùng đúng mapping/ownership version của ProjectionDefinition và cùng pure calculators với incremental.
- Lấy predecessor từ retained Mongo hoặc trusted SQL closing anchor ngày trước, đúng version/not dirty/coverage.
- Replay ordered events per device/stateType; dựng expected daily metrics, unique summary, quality và durations.
- Stream kết quả vào staging theo bounded groups; range rỗng thật khác range thiếu source.
- Không replay qua incremental delta writer vì inbox sẽ skip; output là expected replacement cho đúng scope.

#### Task P6.4 - Publish atomically và kiểm soát concurrent writers

- Lấy lại writer gate/fencing; so R, request generation, seed và coverage với snapshot đã chụp.
- Nếu stale, không replace; ghi retry reason, bỏ/rebuild staging bằng run mới.
- Nếu hợp lệ, replace đúng affected rows, update cursor khi range chạm edge an toàn, complete đúng request và ghi run/revision cùng transaction.
- Không xóa inbox hoặc reset H normal; event ngoài staged set sẽ được admission ở incremental/deep discovery sau đó.
- Coordinator có bounded pause data writers sau current batch để tránh recompute retry mãi; heartbeat vẫn tiếp tục.
- Integration test incremental/refresh xen giữa prepare và publish; kết quả không lost update/double count.

#### Task P6.5 - Forward propagation và chia range

- So closing state sau recompute với opening state ngày kế tiếp; mở rộng khi late transition còn ảnh hưởng.
- Dừng tại transition chặn ảnh hưởng/fixed point hoặc current edge có evidence.
- Nếu vượt MaximumRangeDays, tạo continuation durable cho phần còn lại; không complete toàn bộ yêu cầu khi mới xử lý một chunk.
- Khi propagation vượt retained source/trusted anchor, giữ previous SQL result, đánh dấu unrecoverable và yêu cầu vận hành.
- Test late nhiều ngày, ngày không có transition và continuation qua restart.

#### Task P6.6 - Scheduler đối soát và finalization

- Drain Pending requests theo lịch retry; chạy rolling 3 ngày mỗi giờ và finalize ngày hôm qua lúc 02:00 Việt Nam.
- Đọc ProjectionRun/request history khi startup để phát hiện missed windows; không chỉ dựa timer memory.
- Một active reconciliation run cho projection; phối hợp incremental/refresh bằng writer coordinator.
- Finalize chỉ khi range đủ coverage và không còn dirty generation cần xử lý; late event có thể reopen.
- Log run/range/result và cancellation; test missed schedule/restart/time provider.

#### Deliverable và acceptance của Phase 6

- Exact replacement theo cùng rules với incremental; concurrent changes làm stale publish bị từ chối.
- TTL/missing seed dẫn tới coverage failure, không làm mất thống kê SQL đã có.
- Request/continuation không mất khi restart; stale owner/generation không complete nhầm.
- Reconciliation đạt tiến triển trong bounded pause và không kéo lag quá SLO.

### Phase 7 - Bootstrap, retention và các chế độ phục hồi

**Mục tiêu:** triển khai khởi tạo/backfill/rebuild có audit và vận hành được với Mongo chỉ giữ history 7 ngày.

**Phụ thuộc:** P6 exact recompute/coverage. Script retention được viết/test ở phase này; activation môi trường thật đi cùng rollout P9.

**File chính:** `Recovery/*`, `ManualProjectionHostedService.cs`, `ProjectionCoveragePolicy.cs`, `Enable-HistoryRetention.ps1`, `Invoke-StatisticsMode.ps1`, `docs/Sprint-3-Runbook.md`.

#### Task P7.1 - Implement bootstrap, backfill và rebuild handlers

- Bootstrap version mới từ explicit CoverageStartAtUtc, tạo definition/inbox/facts qua cùng atomic writer.
- Validate date/company/device/source scope và retained coverage trước khi bắt đầu; ghi ProjectionRun và partial first day nếu cần.
- Same-version backfill admission event còn thiếu rồi exact reconcile; không ghi count trực tiếp và không đẩy H normal.
- Rebuild N+1 dùng registry/coverage riêng, retained history bootstrap rồi tail catch-up; không sửa mapping/version của SQL data cũ.
- ManualProjectionHostedService chạy đúng mode đã chọn và kết thúc với exit/result rõ; không vô tình start incremental/background scheduler của mode khác.
- Viết tests invalid range, duplicate invocation, cancelled run và resume/retry theo durable state.

#### Task P7.2 - Implement history retention script

- Kiểm tra target database/collection, current indexes và timestamp coverage trước khi tạo TTL.
- Tạo/verify retention 604800 giây theo persistedAtUtc; tuyệt đối không dùng timelineAtUtc.
- Script có chế độ inspection/preview để operator thấy cutoff và dữ liệu bị ảnh hưởng trước activation.
- Document thiếu valid persisted timestamp được báo riêng để migration/cleanup có audit, không coi đã được TTL xử lý.
- Không áp dụng TTL lên ingestion_checkpoints; không thay retention ingestion_failures trong task này.
- Test trên database dùng riêng, bao gồm late business date nhưng persisted mới và timestamp missing.

#### Task P7.3 - Xác nhận cơ chế retention phù hợp deployment

- Kiểm tra target Mongo/Mongo-compatible hỗ trợ TTL/index contract bằng UAT evidence.
- Nếu hỗ trợ đúng, dùng TTL script P7.2; nếu không, triển khai deployment-owned scheduled cleanup theo cùng persisted cutoff.
- Cleanup fallback phải bounded batches, observable và không chạy bằng Statistics runtime credential.
- Ghi rõ deletion timing thực tế, permission và cách tắt cleanup; không hứa dữ liệu bị xóa đúng từng giây.
- Không implement đồng thời hai cơ chế xóa không phối hợp trên cùng collection.

#### Task P7.4 - Retention-gap detection và durable coverage

- Khi startup/resume, đối chiếu last completed sweeps/manifests với retention boundary và requested range.
- Nếu downtime có thể vượt source còn giữ, ghi explicit gap/partial/unrecoverable; không tự reset checkpoint hoặc đánh dấu zero missing events.
- Giữ daily state anchors đã materialize trong SQL để nối opening state khi đủ evidence; không dùng anchor dirty/sai version.
- Chặn recompute range thiếu source membership/predecessor; giữ kết quả SQL trước đó và ghi recovery reason.
- Test downtime 1/3/>7 ngày, TTL mid-read và caught-up sau gap vẫn giữ diagnostic lịch sử.

#### Task P7.5 - Cleanup dữ liệu vận hành mà không mất idempotency

- Active ProcessedEvent không purge trong Sprint 3; duplicate EventId phải vẫn bị chặn sau khi Mongo đã TTL và raw file bị replay.
- Cleanup staging chỉ với run completed hoặc abandoned đã xác nhận không còn owner hợp lệ.
- ProjectionRun retention có cấu hình, nhưng coverage manifests, anchors và unresolved recovery evidence phải còn.
- Facts/snapshots không tự purge; không truncate version cũ để tiết kiệm dung lượng khi chưa có operator procedure.
- Test cleanup lặp lại không xóa active request/staging và không làm raw replay tăng statistics.

#### Task P7.6 - Recovery, cutover và rollback runbook

- Viết từng procedure: kiểm tra lag/coverage, restart, resume request, backfill range còn source, rebuild version mới.
- Cho command/config mẫu dùng placeholders, expected logs/SQL checks và điều kiện dừng khi thấy coverage gap.
- Mô tả cutover chỉ cho date range version mới đủ coverage; giữ version cũ cho lịch sử đã mất source nếu cần.
- Rollback bằng disable Statistics hoặc chuyển read version phù hợp; không sửa raw ingestion checkpoint.
- Ghi giới hạn không full-history rebuild ngoài retained source và không archive dài hạn trong Sprint 3.

#### Deliverable và acceptance của Phase 7

- Các mode chạy có range/version/audit rõ và không double count cùng incremental.
- Retention có script/evidence, gap detection không làm mất SQL result đã có.
- Raw replay sau Mongo TTL vẫn idempotent; limited rebuild không giả coverage lịch sử.
- Team vận hành có procedure cụ thể cho sự cố trong và ngoài 7 ngày.

### Phase 8 - Observability, health vận hành và graceful shutdown

**Mục tiêu:** operator biết Worker đang làm gì, còn bao nhiêu việc chưa xử lý và khi nào dữ liệu có nguy cơ mất khỏi retention.

**Phụ thuộc:** các feature P4-P7. Telemetry hooks được viết cùng từng feature; phase này hoàn thiện integration và acceptance vận hành.

**File chính:** `Observability/*`, `HealthChecks/*`, `GracefulShutdownCoordinator.cs`, `ConfigurationRedactor.cs`, deployment README/runbook.

#### Task P8.1 - Structured logs cho từng processing boundary

- Log configuration validated, lease acquired/lost, sweep started/completed, batch committed, retry, dirty request và recompute outcome.
- Scope có projection/version/run/epoch, cursor/range, result/duration/attempt; device ID chỉ ở diagnostic scope cần thiết.
- Phân biệt prepared/committed counts và dropped/stale staging; không log như đã persistence trước commit.
- Không connection string/raw document/PII; stable messages từ AppConst, viết redaction assertions.

#### Task P8.2 - Metrics qua abstraction chung

- Implement IStatisticsTelemetry/StatisticsMetrics với counters read/new/duplicate/ignored/quality/failed.
- Đo Mongo read, SQL transaction, retry/deadlock, batch duration và touched rows.
- Theo dõi sweep completion, duration accounting edge, pending request age, stale publish retries, lease và retention headroom.
- Labels chỉ nhóm nhỏ như mode/status/source kind; không EventId/DeviceId/RunId làm metric labels.
- Dùng committed result cập nhật metrics; exporter/sink là adapter môi trường, không nhúng vendor dependency vào core.

#### Task P8.3 - Tính lag đúng theo từng loại công việc

- Incremental lag dựa known outstanding history và sweep progress, không chỉ lấy now trừ timestamp event cuối.
- Theo dõi riêng oldest dirty request, duration refresh age và thời gian deep discovery chưa hoàn tất.
- Source không có event mới nhưng sweep caught-up thì không degraded chỉ vì last-event timestamp cũ.
- Coverage gap lịch sử không bị xóa khỏi health/evidence khi H hiện tại đã caught-up.
- Test idle source, live backlog, reconnect/retry và old dirty request bằng TimeProvider.

#### Task P8.4 - Readiness và alert thresholds

- Ready khi config/schema/registry/dependencies hợp lệ, startup xong và instance có quyền xử lý theo mode.
- Known backlog >=12 giờ phát warning; >24 giờ là SLO breach/unhealthy; thể hiện cả retention headroom.
- Mongo/SQL unavailable, lease lost hoặc schema mismatch báo theo threshold riêng sớm hơn, không chờ 12 giờ.
- Unrecoverable coverage có actionable reason; không báo khỏe chỉ vì không còn event để đọc.
- Ghi rõ đây là health của pipeline, không phải device health/scoring Sprint 4.

#### Task P8.5 - Graceful shutdown trên toàn bộ loops

- Stop scheduling sweep/refresh/reconcile mới; chờ current SQL transaction trong ShutdownTimeout.
- Giữ heartbeat cần thiết trong drain; chỉ release lease sau khi commit/rollback đã kết thúc.
- Timeout/cancellation rollback uncommitted work, giữ checkpoint cũ; không complete request vì process sắp dừng.
- Dispose readers/connections/session, để claim/run chưa hoàn tất có thể recovery sau restart.
- Test shutdown khi đọc Mongo, đang SQL transaction, đang build staging và đang publish.

#### Task P8.6 - Deployment và vận hành cơ bản

- Hoàn thiện config sample, environment keys, permissions, schema/index preflight và cách chạy từng mode.
- Nối log/metrics/readiness vào hệ thống giám sát hiện có; HTTP endpoint/exporter nếu cần nằm ở deployment adapter.
- Runbook có query kiểm tra active owner, checkpoint, pending requests, coverage gaps và latest successful run.
- Log/run evidence đủ để operator xác định retry được hay cần dữ liệu/correction; không yêu cầu debug source mới biết trạng thái.

#### Deliverable và acceptance của Phase 8

- Idle, backlog, dependency outage, lease loss, retention gap và drain có tín hiệu khác nhau.
- Threshold 12/24 giờ được test; metrics không high-cardinality hoặc lộ secret.
- Shutdown/restart giữ atomicity và request recovery; monitoring không bị hiểu thành device health Sprint 4.

### Phase 9 - Integration verification, UAT và rollout

**Mục tiêu:** có bằng chứng end-to-end và quy trình triển khai trước khi bật retention/production scope.

**Phụ thuộc:** P0-P8. Unit/integration tests đã được bổ sung trong các phase, phase này chạy và tổng hợp acceptance toàn hệ thống.

**File/tài liệu chính:** test projects, `docs/Sprint-3-Testcase.md`, `docs/Sprint-3-Runbook.md`, deployment scripts/README và UAT evidence.

#### Task P9.1 - Viết testcase có thể thực thi

- Chuyển ma trận mục 12 thành testcase có ID, prerequisites, fixture/config, bước thao tác, expected result và query đối chiếu.
- Gắn testcase với task/contract cần chứng minh: ownership, cursor, transaction, state, reconciliation, retention.
- Mỗi case có evidence/pass-fail/blocked và reason; test chưa chạy không được ghi pass.
- Phân biệt automated fixture, integration DB và runtime ERP/UAT; không dùng loại evidence này để thay thế loại khác.

#### Task P9.2 - Chạy automated và end-to-end suite

- Build solution và chạy Statistics unit/architecture/integration tests trên DB test riêng.
- End-to-end: raw/AppHub fixture -> History Worker -> Mongo -> Statistics Worker -> SQL; xác nhận identity, facts, state và checkpoints.
- Chạy mixed V1/V2, invalid contract, rollback, ambiguous commit và resume cases.
- Lưu commit SHA, schema/mapping versions, sanitized config, commands và test results; opt-in skipped là chưa kiểm chứng.

#### Task P9.3 - UAT bằng raw-log và AppHub thực tế

- Chọn scope UAT có fixture canonical đủ, tạo tag/business và connection/control/sensor/scanner observations tương ứng.
- Đối chiếu Mongo EventId/source/facts với SQL inbox và metric rows; chỉ primary source được đếm.
- Kiểm tra timeline/day UTC+7, warning/quality routing và snapshot không đổi connection state.
- Đối chiếu unique summary và nhiều-metric contribution; không so tổng metric count một-một với raw history count.
- Lưu logs/queries và event examples đã redaction; metric thiếu canonical input phải ghi rõ prerequisite thay vì pass giả.

#### Task P9.4 - Capacity, backlog và tác động database

- Chạy fixture tối thiểu 100.000 events/ngày equivalent, có burst và live writes trong lúc catch-up.
- Tạo backlog 300.000 events/3 ngày; đo thời gian bắt kịp, throughput, peak memory, p95 batch/DB latency và lock/log growth.
- Target recovery ban đầu <=12 giờ; normal known lag <=24 giờ. Backlog test bắt đầu với lag >24 giờ phải báo breach cho tới khi bắt kịp.
- Chạy rolling reconciliation cùng report queries; ghi baseline và mức ảnh hưởng thực tế để tuning batch/pause/index.
- Không sửa identity/checkpoint/coverage rules để đạt benchmark; điều chỉnh cấu hình trong bounds và chạy lại case liên quan.

#### Task P9.5 - Fault và concurrency acceptance

- Mô phỏng crash trước/trong/sau SQL commit, timeout/unknown response, deadlock và lease takeover.
- Chèn event vào lúc recompute build staging/publish; xác nhận không lost update/double count.
- Tạo overlap nhiều hơn batch, deep-skew arrival, TTL xóa source giữa pages và downtime vượt retention.
- Test repeated state, late-before-refresh-edge, multi-day propagation và shutdown request Processing.
- Đối chiếu expected SQL values sau recovery và lưu evidence từng case, không chỉ báo process vẫn chạy.

#### Task P9.6 - Rollout và rollback có thứ tự

1. Apply SQL migrations/seed và Mongo indexes bằng deployment identity.
2. Deploy Statistics disabled, verify config/permissions/schema/indexes và readiness preflight.
3. Bootstrap version/scope từ explicit retained coverage; kiểm tra counts/state/quality.
4. Enable limited scope, chạy late/retry/reconcile acceptance rồi mới mở rộng theo scope/version contract.
5. Xác nhận coverage/lag alerts/retention headroom; preview dữ liệu hết hạn trước khi enable TTL 7 ngày.
6. Theo dõi processing/read load và pending repair; nếu lỗi thì disable Statistics/rollback read version theo runbook, giữ SQL evidence.

Không reset History ingestion checkpoint hoặc xóa SQL data để làm rollout trông sạch. Việc mở rộng scope phải có backfill/coverage, không chỉ đổi filter sau global cursor.

#### Task P9.7 - Regression và bàn giao

- Chạy regression raw-log framing/parser/persistence/checkpoint và AppHub mapping bị ảnh hưởng bởi prerequisite.
- Xác nhận SQL outage không dừng History Worker; Mongo queries/index/TTL load không gây regression ingestion ngoài ngưỡng đã đo.
- Hoàn thiện Runbook: start/stop, preflight, incident recovery, coverage gap, limited rebuild, cutover và rollback.
- Tổng hợp phase acceptance, testcase evidence, known limits và cấu hình UAT được chọn.
- Ghi Health Sprint 4 và archive ngoài scope; bàn giao rõ phần nào đã kiểm chứng và phần nào còn blocked.

#### Deliverable và acceptance cuối Sprint

- Build/test suites pass với các integration gates thực sự được chạy.
- UAT metric ownership/canonical input, state duration, atomicity và recovery có evidence.
- Capacity/backlog/retention/SLO đạt mục tiêu; missing historical coverage không bị che giấu.
- Deploy/rollback vận hành được theo tài liệu, History Worker không phụ thuộc synchronous SQL.

### Thứ tự thực hiện và bàn giao giữa các phase

```text
P0 -> P1 -> P2
          -> P3
P2 + P3 -> P4 -> P5 -> P6 -> P7 -> P8 -> P9
```

P2/P3 có thể song song sau P0. Telemetry/test đi cùng feature. P4 idempotency là gate trước state/reconciliation.

## 12. Ma trận kiểm chứng bắt buộc

| Nhóm | Kết quả cần chứng minh |
|---|---|
| Compatibility | Raw V1 + AppHub V2, BSON numeric variants, unsupported schema terminal |
| Cursor | Equal timestamp; overlap >batch; all-duplicate pages vẫn tiến; H không lùi |
| Commit skew | Late insert trong overlap; sâu hơn overlap được deep scan bắt trong retention |
| Malformed | Invalid identity/time có audit/failure; không block hoặc silent skip |
| Ownership | Raw/AppHub tag overlap chỉ raw tăng; snapshot không đổi state |
| Counts | Một event hai metrics: mỗi metric +1, total +1; outcome retry không tăng lại |
| Atomicity | Crash trước/trong/sau commit, unknown result, deadlock; inbox/facts/checkpoint đồng nhất |
| Fencing | Old epoch/expired lease/overlap/manual writer không ghi trái quyền |
| State | Unknown, repeated, same timestamp, second boundary, midnight, idle day, two state types |
| Refresh | Edge một lần; late-before-edge dirty/reconcile, không negative duration |
| Recompute race | DataRevision đổi thì stale publish bị từ chối và sau đó tiến triển |
| Requests | Crash reclaim; generation/continuation không mất range |
| Coverage | Trusted predecessor hoặc partial/unrecoverable; không kết quả giả |
| Retention | TTL mid-read; >7d no reset; active inbox chống replay sau TTL |
| Recovery modes | Backfill không double count; rebuild scope/coverage/rollback rõ |
| Observability | Idle healthy; backlog 12h warning/24h breach; DB/lease báo sớm |
| Capacity | 100k/day, 300k backlog, reconcile; memory/p95 DB/report latency |
| Security | Layer boundaries, parameterization, redaction, Mongo read-only, deploy identity riêng |

Test opt-in bị skip không phải pass. Automated fixture không thay ERP/UAT evidence; Sprint 2 pass không thay SQL projection acceptance.

## 13. Definition of Done và bàn giao

- [ ] Design/Schema/Plan thống nhất grain, duration, timezone, retention và Health Sprint 4.
- [ ] 4 Statistics projects, 3 tests, migrations/deploy scripts/example/runbook đúng convention.
- [ ] SQL database/context riêng, runtime chỉ đọc Mongo, dependency đúng.
- [ ] Metric ownership đúng, V1/V2 hỗ trợ, không raw payload trong SQL.
- [ ] Counts/state/quality/inbox/checkpoint nguyên tử; stale lease không ghi được.
- [ ] Overlap tiến triển, deep discovery/audit bền vững trong retention.
- [ ] Device/scanner state riêng; partial-day/unknown/observed-state semantics đúng.
- [ ] Reconciliation exact, revision guarded, request recovery/propagation đầy đủ.
- [ ] Mongo retention 7 ngày được kiểm chứng; SQL giữ facts/inbox/coverage, không giả rebuild ngoài source.
- [ ] SLO/load tests đạt; runtime health/logs/metrics/shutdown có evidence.
- [ ] UAT/rollout/rollback vận hành được, không sửa checkpoint thủ công.

Sprint 4 tiếp nhận health rules/score, operating schedule, reason/version semantics và API/read needs. Health dựa trên facts/duration/quality/coverage đã lưu; rule cần chi tiết không còn trong Mongo không được tự áp dụng ngược cho lịch sử thiếu evidence.
