# Device Event Statistics - Sprint 3 Fix & Completion Plan

## 1. Trạng thái, mục tiêu và cách sử dụng

- Ngày lập: 2026-09-05.
- Trạng thái: kế hoạch sửa lỗi và hoàn thiện Sprint 3 sau kiểm thử runtime thực tế.
- Căn cứ: `Sprint-3-Plan.md`, `Sprint-3-Design.md`, `Sprint-3-Schema.md`, `Sprint-3-Runbook.md`, `Coding-Standards.md`, source code hiện tại và kết quả đối soát MongoDB/SQL Server ngày 2026-09-05.
- Phạm vi: chỉ Device Event Statistics và những contract trực tiếp với MongoDB History/SQL Statistics; không thay đổi pipeline đọc raw log của History Worker nếu không có testcase chứng minh dependency đó cần sửa.
- Convention SQL đã chốt: schema mặc định `dbo`, tên bảng có tiền tố nằm trong tên bảng như `[dbo].[DES.ProcessedEvent]`; mỗi bảng có một khóa chính `INT IDENTITY(1, 1) PRIMARY KEY`; các cột khác nullable; không tạo foreign key hoặc unique index.
- Convention code: theo `Coding-Standards.md`, `.editorconfig` và dependency `Domain <- Application <- Infrastructure <- Worker`.

Tài liệu này không thay thế toàn bộ `Sprint-3-Plan.md`. Đây là plan bổ sung để:

1. Sửa các lỗi đã xuất hiện trên dữ liệu thực tế.
2. Hoàn thiện các contract Sprint 3 đã được thiết kế nhưng chưa được nối vào runtime.
3. Bổ sung migration nâng cấp, integration tests, UAT evidence và điều kiện rollout còn thiếu.
4. Đưa project từ trạng thái “incremental cơ bản chạy đúng” sang trạng thái có thể nghiệm thu Sprint 3.

Mỗi task chỉ được đánh dấu hoàn thành khi code, automated tests và evidence tương ứng đều có. Test bị skip, class đã đăng ký DI nhưng không có consumer, hoặc bảng đã được tạo nhưng không được ghi dữ liệu không được tính là hoàn thành.

## 2. Baseline và bằng chứng thực tế

### 2.1. Snapshot đối soát ngày 2026-09-05

Thời điểm chụp dữ liệu: khoảng `2026-09-05T08:01:57Z`.

| Hạng mục | Kết quả |
|---|---:|
| MongoDB history documents | 9.309 |
| Documents đủ cursor contract | 9.309 |
| SQL checkpoint đã xử lý | 9.077 |
| Documents sau checkpoint | 232 |
| Duplicate Mongo EventId | 0 |
| ProcessedEvent mong đợi đến checkpoint | 9.077 |
| ProcessedEvent thực tế ở SQL | 9.077 |
| Missing/extra/outcome/date mismatch | 0 / 0 / 0 / 0 |
| Metric groups mong đợi/thực tế | 13 / 13 |
| Quality groups mong đợi/thực tế | 7 / 7 |
| Snapshot groups mong đợi/thực tế | 5 / 5 |
| Projection failures | 263, đều là dữ liệu V1 cũ thiếu timeline |
| Reconciliation requests | 2 Pending, `AttemptCount = 0` |
| ProjectionCoverage rows | 0 |
| ProjectionRun rows | 0 |
| ProjectionDefinition rows | 0 |
| DeviceDimension rows | 0 |

Tại thời điểm kiểm tra không có History Worker hoặc Statistics Worker đang chạy. Vì vậy 232 documents sau checkpoint là backlog do tiến trình đã dừng, chưa phải bằng chứng mất dữ liệu.

### 2.2. Những phần đã hoạt động đúng

- Keyset/overlap incremental đã ghi đúng toàn bộ 9.077 events đến checkpoint.
- Event disposition khớp nguồn: `aggregated`, `ignored`, `quality_only`, `failed_terminal` không sai lệch.
- Metric daily, ingestion quality daily và device daily snapshot khớp phép tính độc lập từ MongoDB.
- Ownership hoạt động đúng: AppHub tag-read bị ignored khi raw RFID là primary source, không double count.
- Không có duplicate business identity trong các bảng facts/checkpoint đã kiểm tra.
- State duration không âm, tổng duration bằng khoảng tính toán và không vượt bucket.
- 263 lỗi `STAT_TIMELINE_REQUIRED` thuộc dữ liệu V1 cũ, không có lỗi tương tự ở các event gần nhất.

Các phần trên phải được giữ bằng regression tests. Plan sửa lỗi không được làm thay đổi kết quả đã đúng.

### 2.3. Lỗi và khoảng trống đã xác nhận

| ID | Mức độ | Hiện trạng | Tác động |
|---|---|---|---|
| FIX-01 | P0 | `ReconciliationHostedService` chỉ chạy khi `Mode = Reconciliation` | Hai request do late/out-of-order tạo ra không được xử lý trong vận hành Incremental bình thường; state observation count bị thiếu |
| FIX-02 | P0 | Scope rolling reconciliation trả về ngay khi CompanyIds/DeviceIds rỗng và đang tạo Cartesian product khi cả hai có dữ liệu | Global scope không có rolling requests; explicit scope có thể tạo cặp company/device không tồn tại |
| FIX-03 | P0 | `IHistoryContractAuditReader` đã có implementation nhưng không có hosted service/consumer; `DeepDiscoveryInterval` chưa dùng | Late insert sâu hơn overlap có thể bị bỏ lỡ vĩnh viễn trong thời gian retention |
| FIX-04 | P0 | `ProjectionDefinition` không được tạo/validate ở Incremental; `ResumeFromStoredDefinition` được validator chấp nhận nhưng runtime vẫn bắt `CoverageStartAtUtc` | Projection version/mapping/ownership/metric/timezone không được khóa; resume contract bị hỏng |
| FIX-05 | P1 | `IDeviceMetadataResolver` được đăng ký nhưng không có consumer; `DeviceDimension` luôn rỗng | Metadata/dimension contract Sprint 3 chưa hoạt động |
| FIX-06 | P1 | `ProcessedEvent` và TVP thiếu `CompanyId`, `DeviceId`; membership/rebuild đọc rộng rồi filter trong memory | Mỗi request có thể quét lại toàn range của mọi device; chi phí tăng nhanh theo số device và 100k events/ngày |
| FIX-07 | P1 | 14 metric definitions được seed với `IsEnabled = 0`, nhưng resolver không filter trạng thái | Registry nói metric disabled trong khi runtime vẫn dùng; startup verification không đủ chặt |
| FIX-08 | P1 | Retention headroom dùng `SourceOldestPersistedAtUtc - OldestPendingRequestAtUtc` | Hai timestamp khác ngữ nghĩa; health có thể cảnh báo sai |
| FIX-09 | P1 | Health checks được đăng ký nhưng Generic Host không có endpoint/exporter để hệ thống ngoài probe | Readiness/operational health chỉ tồn tại trong process/log, chưa có contract vận hành ngoài process |
| FIX-10 | P0 | Integration test project không có test thực thi; chưa có `Sprint-3-Testcase.md` | Atomicity, crash recovery, lease takeover, race và retention chưa có bằng chứng nghiệm thu |
| FIX-11 | P2 | Còn hai chỗ dùng `DateTimeOffset.UtcNow` trực tiếp | Logic thời gian không deterministic hoàn toàn theo Coding Standards |
| FIX-12 | P1 | Dependency scan cảnh báo SharpCompress 0.30.1 và Snappier 1.0.0 qua dependency graph | Có cảnh báo bảo mật mức moderate/high cần được xử lý hoặc ghi nhận có căn cứ |
| FIX-13 | Gate | Mongo history chưa bật TTL | Đúng với rollout hiện tại, nhưng chưa được bật trước khi FIX-01 đến FIX-04 và retention tests pass |

### 2.4. Sai lệch state đã quan sát

| Company | Device | Date | StateType | Mongo expected | SQL actual |
|---:|---:|---|---|---:|---:|
| 2 | 18 | 2026-09-04 | device_connection/disconnected | 306 | 41 |
| 2 | 40 | 2026-09-04 | device_connection/connected | 3 | 1 |
| 2 | 18 | 2026-09-05 | device_connection/disconnected | 30 | 5 |

Daily metric rows cho các event này vẫn đúng. Sai lệch nằm ở state projection chưa được exact reconciliation, phù hợp với hai request Pending có `AttemptCount = 0`.

## 3. Mục tiêu và ngoài phạm vi

### 3.1. Mục tiêu bắt buộc

- Incremental mode phải đồng thời chạy projection, lease heartbeat, duration refresh, request scheduling và reconciliation.
- Mọi late event trong overlap hoặc được deep discovery tìm thấy phải đi qua cùng idempotency gate và được phản ánh đúng vào facts/state/coverage.
- Projection definition phải được tạo hoặc resolve trước khi xử lý event; config runtime không được âm thầm khác stored definition.
- Reconciliation phải query theo đúng `(CompanyId, DeviceId, StateType, range)` ngay từ MongoDB và SQL.
- Metric registry, metadata dimension và health contract phải phản ánh đúng trạng thái runtime.
- Có automated integration tests và UAT testcase/evidence cho các contract rủi ro cao.
- Không regression các counts/quality/snapshot đã đối soát đúng.

### 3.2. Ngoài phạm vi của fix plan

- Device health scoring/rules, API report và UI vẫn thuộc Sprint 4.
- Không thêm EF Core, broker, Mongo change stream hoặc distributed scheduler.
- Không bật active-active partitioning.
- Không thay đổi team SQL convention bằng unique index, foreign key hoặc composite primary key.
- Không tự động chạy DDL từ Worker. Script SQL vẫn được DBA/dev chạy thủ công trên database đang chọn trong SSMS.
- Không bật Mongo TTL trước retention acceptance và rollout gate cuối.

## 4. Nguyên tắc triển khai

### 4.1. Layer và dependency

```text
Domain <- Application <- Infrastructure <- Worker
```

- Domain giữ value objects và calculation thuần.
- Application giữ use case, policy, contracts và interfaces; không chứa BSON/SQL/hosting.
- Infrastructure giữ Mongo queries, SqlClient, TVP, schema verifier, stores và health exporter adapter.
- Worker giữ options, validation, DI, mode orchestration và hosted-service lifecycle.
- Không reference ngược sang `DeviceEventHistory.Infrastructure` hoặc Worker cũ.

### 4.2. Clean code và coding standard

- Một public type chính trong một file; tên type thể hiện trách nhiệm.
- Async I/O nhận và truyền `CancellationToken`.
- Inject `TimeProvider`; không gọi thời gian hệ thống trực tiếp trong Application/Domain.
- Validation/operational message nằm trong `StatisticsContractConstants.Messages` với prefix `MSG_`.
- Reason/status/metric codes là constants ổn định, không hardcode rải rác.
- SQL values luôn parameterized; table/TVP names đi qua `StatisticsSqlObjectNames` hoặc allowlist cố định.
- Log structured và không chứa connection string, credentials hoặc raw payload.
- Không tạo generic repository hoặc “manager/service” ôm nhiều trách nhiệm.
- Không load toàn bộ retention window vào memory; paging/chunking phải bounded.

### 4.3. Idempotency khi database không có unique index

Theo convention của team, database không dùng unique index. Vì vậy code phải bảo vệ business identity bằng:

1. SQL application lock/writer gate theo projection identity.
2. Fencing lease epoch trong mọi write/publish operation.
3. `WHERE NOT EXISTS` hoặc matched update trong cùng transaction.
4. Deterministic EventId/FailureId/QualityIdentity.
5. Integration concurrency tests bắt buộc cho duplicate insert và stale writer.

Không được coi việc “chưa thấy duplicate trên dev” là bằng chứng đủ cho concurrency safety.

## 5. Kiến trúc runtime đích

```text
                         +----------------------+
Mongo history ----------> Incremental reader    |
     |                   | overlap + keyset     |
     |                   +----------+-----------+
     |                              |
     |                   +----------v-----------+
     +-------------------> Deep discovery audit |
                         | durable _id cursor   |
                         +----------+-----------+
                                    |
                         +----------v-----------+
                         | Mapping / ownership  |
                         | metric + metadata    |
                         +----------+-----------+
                                    |
                         +----------v-----------+
                         | SQL atomic writer    |
                         | inbox/facts/state/H  |
                         +----------+-----------+
                                    |
             late/dirty requests    v
                         +----------+-----------+
                         | Reconciliation loop  |
                         | exact scoped rebuild |
                         +----------+-----------+
                                    |
                         +----------v-----------+
                         | Coverage + health    |
                         +----------------------+
```

Một `ProjectionLeaseCoordinator` vẫn là chủ sở hữu lease của projection. Incremental, duration refresh, audit và reconciliation chia sẻ lease/fencing token; không tạo data ownership độc lập. SQL writer gate bảo đảm publish tuần tự ở boundary cần atomicity.

## 6. Contract và schema cần hoàn thiện

### 6.1. Projection definition lifecycle

Tạo Application service `ProjectionDefinitionResolver` để startup mode resolve một `ResolvedProjectionDefinition` duy nhất gồm:

- ProjectionName/ProjectionVersion.
- MappingVersion.
- OwnershipVersion.
- MetricSetVersion.
- CoverageStartAtUtc.
- TimeZoneId.
- LifecycleStatus.

Quy tắc:

- `ResumeFromStoredDefinition = false`: `CoverageStartAtUtc` bắt buộc; tạo definition nếu chưa có, sau đó validate immutable fields.
- `ResumeFromStoredDefinition = true`: definition phải tồn tại; lấy coverage/mapping/ownership/metric/timezone từ SQL và không yêu cầu config lặp lại coverage.
- Nếu config và stored definition khác nhau, startup fail fast với message constant và structured fields; không tự overwrite.
- Incremental chỉ chạy definition ở trạng thái `ready` hoặc `active` theo contract đã thống nhất. Bootstrap/Rebuild quản lý `building -> ready/failed`.

### 6.2. Durable deep-discovery cursor

Không dùng lại incremental high watermark làm audit cursor. Bổ sung durable fields vào `DES.ProjectionCheckpoint`:

- `AuditLastSourceDocumentId`.
- `AuditStartedAtUtc`.
- `AuditCompletedAtUtc`.
- `AuditCycle` hoặc generation tăng dần.
- `LastCompletedSweepAtUtc` được cập nhật khi incremental sweep hoàn tất.

Audit đọc theo Mongo `_id` tăng dần, page bounded. Khi đến cuối collection, atomically ghi completion rồi quay cursor về đầu ở chu kỳ kế tiếp. Mỗi event audit tìm thấy phải đi qua admission/idempotency giống incremental; không có nhánh ghi facts riêng.

### 6.3. ProcessedEvent scope

Bổ sung vào `ProcessedEventInput`, TVP và `[dbo].[DES.ProcessedEvent]`:

- `CompanyId bigint NULL`.
- `DeviceId bigint NULL`.

Giá trị được ghi cho event có identity hợp lệ, kể cả `ignored` nếu nguồn cung cấp identity. Event không resolve được tenant/device vẫn được ghi nullable theo outcome/quality contract.

SQL membership query phải lọc theo projection/version/company/device/date/timeline trước khi trả EventId. Mongo range reader nhận scope và áp filter từ query, không đọc toàn range rồi mới lọc trong Application.

Với dữ liệu cũ:

- Fresh database: cập nhật script `009` để có đủ hai cột ngay khi tạo bảng.
- Existing database: script upgrade chỉ `ALTER TABLE ... ADD` hai cột nullable và cập nhật seed; không drop bảng/dữ liệu.
- Retained range được enrich/reconcile từ Mongo. Range đã quá retention giữ nullable và coverage phải phản ánh không đủ source; không bịa identity.

### 6.4. Scope semantics

Không biểu diễn target bằng Cartesian product của hai list độc lập trong use case. Thêm value object:

```text
ProjectionDeviceKey(CompanyId, DeviceId)
```

Quy ước:

- Cả hai config list rỗng: global scope.
- Có filter: chỉ event/device thỏa filter; validation ngăn cấu hình nửa vời gây hiểu sai.
- Rolling scheduler lấy các cặp thực tế từ `DeviceStateCursor`, `DeviceStateDaily`, `DeviceDimension` hoặc scoped discovery store.
- Mỗi request được seed theo cặp tồn tại và `StateType`, không cross join CompanyIds × DeviceIds.

### 6.5. Metric registry

- Seed active V1 metrics với `IsEnabled = 1`.
- Các metric chưa được contract xác nhận như health/error/snapshot không được bật chỉ vì đã có row; seed status phải theo bảng mapping chính thức.
- `SqlMetricKeyResolver` chỉ resolve metric đúng `MetricSetVersion`, `MappingVersion`, `OwnershipVersion` và `IsEnabled = 1`.
- Startup verifier phải fail khi thiếu, trùng hoặc disabled metric mà mapper đang yêu cầu.
- Không update semantic của metric set cũ sau rollout; thay đổi semantics phải tạo metric/projection version mới.

### 6.6. Device dimension

`IDeviceMetadataResolver` được gọi tại projection boundary cho event có CompanyId/DeviceId. Thêm `IDeviceDimensionStore` để batch-upsert tối thiểu:

- CompanyId/DeviceId.
- DeviceCode/DeviceName/GateCode/GateName nếu có.
- TimeZoneId/UtcOffsetMinutes.
- MetadataSource và timestamps.

Metadata thiếu không chặn event aggregation. Upsert dimension phải cùng writer gate, bounded theo distinct devices của batch và không overwrite giá trị tốt bằng null nếu nguồn mới không có evidence.

### 6.7. Health retention headroom

Không dùng thời điểm request được tạo để so với persisted lower bound. Snapshot phải trả về earliest source time cần cho request, ví dụ:

- `OldestPendingRequiredFromAtUtc`: bucket start của `FromStatisticsDate` theo timezone thống kê.
- `SourceOldestPersistedAtUtc`: lower bound thực tế Mongo.
- `RetentionBoundaryAtUtc`: `NowUtc - retention` để cảnh báo trước khi source bị TTL xóa.

Headroom là khoảng thời gian còn lại từ earliest required source point tới retention boundary, hoặc đánh giá coverage trực tiếp nếu source oldest đã vượt required point. Công thức cuối phải được khóa bằng unit tests cho healthy, warning và unrecoverable cases.

### 6.8. SQL script strategy

- `009_CreateDeviceEventStatisticsSchema.sql` tiếp tục là script tạo mới đầy đủ, idempotent ở mức object existence, chạy trên database đang được chọn trong SSMS, không có `USE <database>`, không drop object.
- Thêm `010_UpgradeDeviceEventStatisticsSprint3Fixes.sql` cho database đã chạy 009. Script chỉ thực hiện thay đổi additive/seed correction cần thiết và cũng chạy trên database đang chọn.
- Cả hai script giữ schema `[dbo]`, tên table/type dạng `[DES.*]`, không tạo schema mới.
- Worker không tự chạy 009/010. Startup verifier chỉ báo thiếu/sai schema và dừng an toàn.
- Nếu TVP cần thay đổi shape, 010 phải tạo type version mới (ví dụ `[DES.ProcessedEventInputV2]`) vì SQL Server không `ALTER TYPE`; code cutover sang type mới trước khi type cũ được loại bỏ ở một release sau.

## 7. Cấu trúc file dự kiến

Ký hiệu: `[M]` sửa file hiện có, `[N]` thêm file mới. Tên file có thể điều chỉnh nhẹ khi implementation chứng minh có responsibility boundary tốt hơn, nhưng không gom nhiều trách nhiệm vào một file.

```text
src/DeviceEventStatistics/
  DeviceEventStatistics.Domain/
    Common/
      StatisticsContractConstants.cs                         [M]
    Projection/
      ProjectionDeviceKey.cs                                 [N]

  DeviceEventStatistics.Application/
    History/
      IHistoryContractAuditReader.cs                         [M]
      HistoryAuditCheckpoint.cs                              [N]
    Mapping/
      ProjectionEventOutcomeMapper.cs                        [M]
    Metadata/
      DeviceMetadata.cs                                      [M]
      IDeviceDimensionStore.cs                               [N]
    Persistence/
      ProjectionBatchContracts.cs                            [M]
    Projection/
      ProjectionContracts.cs                                 [M]
      ProjectionDefinitionResolver.cs                        [N]
      ProjectionSweep.cs                                     [M]
      HistoryContractAuditHandler.cs                         [N]
      IProjectionDefinitionStore.cs                          [N]
      IProjectionAuditCheckpointStore.cs                     [N]
      IProjectionScopeReader.cs                              [N]
    Reconciliation/
      ReconciliationContracts.cs                             [M]
      ReconciliationCoordinator.cs                           [M]
      ExactRangeRebuilder.cs                                 [M]

  DeviceEventStatistics.Infrastructure/
    Metadata/
      ConfigurationDeviceMetadataResolver.cs                 [M]
    MongoDb/Reading/
      MongoHistoryContractAuditReader.cs                     [M]
      MongoHistoryRangeReader.cs                             [M]
      MongoHistoryQuery.cs                                   [M]
    SqlServer/Mapping/
      ProjectionTvpMapper.cs                                 [M]
    SqlServer/Schema/
      SqlSchemaVerifier.cs                                   [M]
    SqlServer/Stores/
      SqlProjectionDefinitionStore.cs                        [N]
      SqlProjectionAuditCheckpointStore.cs                   [N]
      SqlProjectionScopeReader.cs                            [N]
      SqlDeviceDimensionStore.cs                             [N]
      SqlStatisticsBatchWriter.cs                            [M]
      SqlProjectionBatchOperations.cs                        [M]
      SqlProjectionCheckpointStore.cs                        [M]
      SqlProjectionRebuildStore.cs                           [M]
      SqlMetricKeyResolver.cs                                [M]
    SqlServer/Migrations/
      009_CreateDeviceEventStatisticsSchema.sql              [M]
      010_UpgradeDeviceEventStatisticsSprint3Fixes.sql       [N]
    Observability/
      SqlProjectionOperationalSnapshotReader.cs              [M]

  DeviceEventStatistics.Worker/
    Configuration/
      StatisticsOptions.cs                                   [M]
      OptionsValidators.cs                                   [M]
      ServiceCollectionExtensions.cs                         [M]
    Orchestration/
      IncrementalProjectionHostedService.cs                  [M]
      LeaseHeartbeatHostedService.cs                         [M]
      ReconciliationHostedService.cs                         [M]
      HistoryContractAuditHostedService.cs                   [N]
      OperationalHealthHostedService.cs                      [M]
    HealthChecks/
      OperationalHealthState.cs                              [M]
    Program.cs                                                [M]
    appsettings.Example.json                                  [M]

tests/DeviceEventStatistics/
  DeviceEventStatistics.UnitTests/
    ReconciliationSchedulingTests.cs                         [N]
    ProjectionScopeTests.cs                                  [N]
    HistoryContractAuditTests.cs                             [N]
    ProjectionDefinitionResolverTests.cs                     [N]
    MetricRegistryTests.cs                                   [N]
    DeviceDimensionTests.cs                                  [N]
    StatisticsHealthEvaluatorTests.cs                        [N]
    TimeProviderTests.cs                                     [N]

  DeviceEventStatistics.IntegrationTests/
    Fixtures/
      StatisticsDatabaseFixture.cs                           [N]
      MongoHistoryFixture.cs                                 [N]
      HistoryV1.json                                         [N]
      HistoryV2.json                                         [N]
    SqlSchemaTests.cs                                        [N]
    ProjectionDefinitionTests.cs                             [N]
    ProjectionPersistenceTests.cs                            [N]
    ProjectionLeaseTests.cs                                  [N]
    DeepDiscoveryTests.cs                                    [N]
    ReconciliationTests.cs                                   [N]
    RetentionRecoveryTests.cs                                [N]
    StatisticsWorkerEndToEndTests.cs                         [N]

  DeviceEventStatistics.ArchitectureTests/
    DependencyBoundaryTests.cs                               [M]

deploy/device-event-statistics/
  Apply-SqlMigrations.ps1                                    [M]
  Enable-HistoryRetention.ps1                                [M]
  README.md                                                   [M]

docs/
  Sprint-3-Design.md                                          [M]
  Sprint-3-Schema.md                                          [M]
  Sprint-3-Runbook.md                                         [M]
  Sprint-3-Testcase.md                                        [N]
  2026-09-05-Sprint-3-Fix-Plan.md                             [N]
```

Không bắt buộc tạo file chỉ để khớp cây trên nếu type nhỏ gắn chặt với public type hiện có. Ngược lại, implementation không được bỏ một abstraction cần thiết bằng cách đưa SQL/Mongo logic vào Worker.

## 8. Các phase triển khai chi tiết

### Phase F0 - Khóa baseline, contract và migration strategy

**Mục tiêu:** biến kết quả audit thành baseline có thể lặp lại và khóa các quyết định trước khi sửa runtime.

#### Task F0.1 - Lưu query/evidence đối soát

- Đưa các query Mongo/SQL đã dùng vào `Sprint-3-Testcase.md` theo dạng sanitized, không chứa credentials.
- Ghi rõ checkpoint boundary `(LastPersistedAtUtc, LastEventId)` của mỗi lần đối soát.
- Tách expected calculation cho event, metrics, quality, snapshot và state.
- Baseline bắt buộc giữ: 9.077/9.077 ProcessedEvent và zero mismatch cho metric/quality/snapshot ở snapshot đã ghi nhận.

#### Task F0.2 - Đồng bộ Design và Schema

- Cập nhật mode matrix: Incremental bao gồm projection + heartbeat + refresh + audit + reconciliation.
- Bổ sung audit cursor, paired scope, ProcessedEvent company/device và health headroom semantics.
- Ghi rõ SQL convention của team và strategy 009/010.
- Ghi rõ TTL là rollout gate, không phải startup behavior.

#### Task F0.3 - Viết testcase trước cho lỗi thực tế

- Test service activation trong Incremental.
- Test request Pending được claim và Completed.
- Test global/explicit paired scope.
- Test late event sâu hơn overlap được audit bắt.
- Test resume stored definition không cần config coverage.
- Test metric disabled không được resolve.
- Test retention headroom theo source requirement.

#### Deliverable và acceptance Phase F0

- Design/Schema/Testcase không còn mâu thuẫn với plan này.
- Có testcase ID cho mọi FIX-01 đến FIX-13.
- Chưa chạy DDL production ở phase này.

### Phase F1 - Khôi phục reconciliation trong Incremental mode

**Mục tiêu:** sửa ngay lỗi state thực tế và bảo đảm durable requests luôn được xử lý.

#### Task F1.1 - Sửa mode activation

- Cho `ReconciliationHostedService` chạy ở cả `Incremental` và `Reconciliation` khi feature enabled.
- Dedicated Reconciliation mode vẫn chạy được để vận hành thủ công, nhưng không trở thành điều kiện duy nhất để xử lý request.
- Heartbeat phải renew lease trong mọi mode giữ lease đủ lâu để write; không để dedicated/manual reconciliation mất lease giữa range.
- Mọi cycle dùng `GracefulShutdownCoordinator` và lease-lost cancellation.

#### Task F1.2 - Thiết kế shared lease an toàn

- Incremental mode dùng lease hiện tại của coordinator; reconciliation không release lease mà nó không tự acquire.
- Khi service tự acquire lease, heartbeat/renew phải tồn tại suốt operation và release trong `finally`.
- SQL publish tiếp tục kiểm tra lease owner/epoch/expiry và DataRevision.
- Không cho old epoch ghi sau takeover.

#### Task F1.3 - Sửa rolling scope

- Thêm `IProjectionScopeReader` trả distinct `ProjectionDeviceKey` thực tế.
- Global scope discover cặp đã quan sát; explicit scope filter cặp, không cross-product.
- Seed rolling requests cho từng supported StateType trong `RollingDays`.
- Coalesce request cùng stream/range/reason dưới writer gate để tránh tăng request vô hạn dù database không có unique index.

#### Task F1.4 - Xử lý dữ liệu state đang sai

- Sau khi deploy code, xử lý lại hai Pending requests hiện có.
- Đối chiếu device 18/40 cho 2026-09-04 và 2026-09-05 với Mongo source.
- Chỉ mark Completed sau publish thành công và DataRevision được tăng.
- Không sửa tay state count trong SQL.

#### Tests Phase F1

- Incremental startup kích hoạt reconciliation.
- Pending -> Processing -> Completed.
- Crash khi Processing -> claim expiry -> reclaim.
- Lease takeover giữa stage/publish -> stale publish bị từ chối.
- Empty scope global tạo request đúng cặp; explicit scope không tạo cặp giả.
- Sau exact rebuild, các state mismatch đã nêu về 0.

#### Deliverable và acceptance Phase F1

- Không còn request Pending lâu hơn schedule/claim threshold khi dependencies healthy.
- State rows device 18/40 khớp Mongo trong retained range.
- Incremental event/metric/quality/snapshot vẫn zero mismatch.

### Phase F2 - Durable deep discovery và source contract audit

**Mục tiêu:** bắt late insert sâu hơn overlap và audit toàn bộ source trong retention một cách bounded.

#### Task F2.1 - Hoàn thiện audit contracts

- Tạo audit checkpoint model độc lập với high watermark.
- `HistoryContractAuditHandler` đọc page, map contract, đưa valid events qua `StatisticsProjectionPipeline` hoặc admission handler dùng chung.
- Invalid contract tạo deterministic failure/quality theo policy; không silent skip.
- Audit duplicate là idempotent success và vẫn advance audit cursor.

#### Task F2.2 - Implement audit hosted service

- Thêm `HistoryContractAuditHostedService` chạy theo `DeepDiscoveryInterval` trong Incremental mode.
- Audit chỉ chạy khi có active lease và readiness pass.
- Mỗi turn giới hạn pages/events/duration để không làm đói incremental loop.
- Shutdown/cancel lưu durable page cursor ở transaction boundary đã commit.

#### Task F2.3 - Persist audit progress

- Cập nhật 009/010 và checkpoint store cho audit fields.
- Advance audit cursor cùng SQL commit của admission batch hoặc bằng CAS/fencing operation không thể vượt event chưa persist.
- Khi hoàn thành full sweep, cập nhật completion time/generation và bắt đầu chu kỳ mới sau interval.

#### Task F2.4 - Query/index verification

- Xác nhận Mongo query `_id > cursor` dùng index `_id_` và projection fields tối thiểu.
- Đo read load khi vừa incremental vừa audit với 100k events/ngày.
- Không tạo TTL trước khi audit/reconciliation recovery tests pass.

#### Tests Phase F2

- Event đến trễ trong overlap được incremental bắt.
- Event insert có `persistedAtUtc` sâu hơn overlap được audit bắt.
- All-duplicate audit page vẫn tiến cursor.
- Crash sau SQL commit/trước client acknowledgement không double count.
- Full audit completion được lưu và restart tiếp tục đúng.
- Malformed/unsupported document tạo failure/quality đúng contract.

#### Deliverable và acceptance Phase F2

- `LastCompletedSweepAtUtc` và audit completion không còn luôn null sau một cycle đầy đủ.
- Deep-skew fixture được đưa vào SQL đúng một lần.
- Memory/read batch bị giới hạn theo config.

### Phase F3 - Projection registry, resume và device dimension

**Mục tiêu:** nối các bảng/abstractions đã có vào lifecycle thật của Incremental.

#### Task F3.1 - Projection definition resolver

- Tách read/create/validate definition khỏi `SqlProjectionRecoveryStore` nếu store hiện tại ôm cả manual run lifecycle.
- Resolve definition trước khi acquire/read data hoặc ngay sau readiness nhưng trước processing loop.
- So sánh immutable mapping/ownership/metric/timezone/coverage contract.
- Trả `ResolvedProjectionDefinition` cho Incremental, audit, reconciliation và manual modes dùng chung.

#### Task F3.2 - Sửa ResumeFromStoredDefinition

- Xóa nhánh runtime luôn throw khi `CoverageStartAtUtc` null nếu resume hợp lệ.
- Không dùng options trực tiếp sau khi definition đã resolve; use cases nhận resolved contract.
- Validator phân biệt create-new và resume-existing rõ ràng.
- Example config và Runbook có ví dụ cho cả hai trường hợp.

#### Task F3.3 - Device dimension persistence

- Resolve metadata một lần trên distinct device keys của batch.
- Upsert dimension trong SQL session phù hợp, giữ last known non-null metadata.
- Metadata lỗi transient không làm commit facts nửa vời; policy retry/fallback phải rõ.
- Metadata không có vẫn cho phép fact processing và để fields nullable.

#### Tests Phase F3

- New definition được tạo đúng một lần dưới concurrent startup.
- Existing definition resume thành công không cần CoverageStartAtUtc trong config.
- Mismatch mapping/metric/timezone fail fast trước read source.
- Dimension insert/update/null-preservation đúng.
- Incremental, reconciliation và rebuild dùng cùng resolved definition.

#### Deliverable và acceptance Phase F3

- `DES.ProjectionDefinition` có row phù hợp active projection.
- `DES.DeviceDimension` được populate cho device mới quan sát.
- Config/stored definition mismatch không thể chạy âm thầm.

### Phase F4 - Scoped reconciliation và SQL contract V2

**Mục tiêu:** loại bỏ full-range scan lặp lại và bảo đảm exact rebuild scale được.

#### Task F4.1 - Mở rộng ProcessedEvent contract

- Thêm CompanyId/DeviceId vào Application input, TVP mapper, SQL batch operations và table definition.
- Event mapper điền identity khi source có đủ dữ liệu.
- Schema verifier kiểm tra columns/type mới và TVP version đúng.
- 010 sử dụng TVP V2 thay vì drop type đang có thể được process khác sử dụng.

#### Task F4.2 - Push filter xuống MongoDB

- `IHistoryRangeReader` nhận company/device/range rõ ràng.
- Mongo query filter ngay trên CompanyId/DeviceId/timeline/persisted range theo contract V1/V2.
- Keyset paging giữ deterministic tie-break.
- Explain plan phải dùng index phù hợp; nếu cần index mới, cập nhật deployment script riêng và benchmark write impact.

#### Task F4.3 - Scope SQL membership

- `ReadMembershipAsync` filter projection/version/company/device/date/timeline/outcome cần thiết.
- Không materialize membership toàn tenant/range khi request chỉ cho một device stream.
- Staging/publish/delete chỉ tác động grain request và coverage tương ứng.

#### Task F4.4 - Backward data handling

- Retained rows có thể enrich qua controlled reconciliation/audit.
- Rows không còn source giữ CompanyId/DeviceId null và coverage partial/unrecoverable nếu cần exact history.
- Nếu production đã có report consumer, cân nhắc projection version mới thay vì destructive in-place rebuild.
- Dev/UAT có thể recreate bằng 009 sau backup nếu team chủ động chọn; script plan không tự drop.

#### Tests và benchmark Phase F4

- Query chỉ đọc event của requested device.
- Hai device cùng range không lẫn membership/state/quality.
- Existing nullable legacy row không làm request crash.
- 100 requests không lặp 100 lần full retained-range scan.
- Benchmark 100k/day và backlog 300k theo Phase 9 target gốc.

#### Deliverable và acceptance Phase F4

- Read volume tỷ lệ với scope request, không tỷ lệ với toàn tenant cho mỗi request.
- Exact publish giữ zero lost update/double count dưới concurrent incremental writes.

### Phase F5 - Metric registry consistency và startup verification

**Mục tiêu:** làm cho database registry và runtime mapper có cùng sự thật.

#### Task F5.1 - Chốt active metric set V1

- Đối chiếu từng mapper với metric seed.
- Chỉ active metrics được đặt `IsEnabled = 1`.
- Ghi mapping/ownership version rõ trong Design/Schema/Testcase.

#### Task F5.2 - Sửa resolver/verifier

- Filter enabled + đúng versions.
- Detect duplicate logical metric rows do database không có unique index và fail startup với diagnostic an toàn.
- Detect missing required metric code trước khi Worker đọc Mongo.
- Cache immutable resolved map theo projection definition, không query mỗi event.

#### Task F5.3 - Cập nhật scripts

- 009 seed trạng thái đúng cho fresh database.
- 010 update đúng rows theo MetricKey/MetricSetVersion/MetricCode với predicate chặt.
- Không xóa metric history và không đổi meaning của version đã production hóa.

#### Tests Phase F5

- Enabled metric resolve thành công.
- Disabled/missing/wrong version bị reject.
- Duplicate logical row fail preflight.
- Mapping regression giữ 13 metric groups khớp baseline.

#### Deliverable và acceptance Phase F5

- Không còn trạng thái “all metrics disabled nhưng runtime vẫn aggregate”.
- Startup summary ghi version/count, không log data tenant hoặc secrets.

### Phase F6 - Health, graceful runtime và TimeProvider cleanup

**Mục tiêu:** health có ngữ nghĩa đúng và có thể được hệ thống vận hành bên ngoài sử dụng.

#### Task F6.1 - Sửa operational snapshot/headroom

- Snapshot đọc earliest required date/range của Pending/Processing request.
- Evaluator phân biệt incremental lag, pending age, source-retention risk và unrecoverable coverage.
- Idle source nhưng dependency đọc được không bị coi là lỗi.
- Không yêu cầu lease held ở mode disabled/manual đã hoàn thành; mode-aware health phải rõ.

#### Task F6.2 - Chọn health exposure tối thiểu

- Ưu tiên endpoint HTTP nhỏ trong Worker nếu deployment có thể expose port; dùng ASP.NET Core health checks và map `/health/live`, `/health/ready`.
- Nếu deployment không cho HTTP, implement exporter/adapter đã thống nhất với platform; không để `AddHealthChecks()` không có consumer.
- Liveness chỉ chứng minh process loop còn sống; readiness gồm config/schema/Mongo/SQL/projection health.
- Không trả exception detail/connection metadata ra endpoint.

#### Task F6.3 - TimeProvider compliance

- Bỏ fallback `DateTimeOffset.UtcNow` khỏi `ProjectionSweep` và `ProjectionEventOutcomeMapper`.
- Caller truyền thời điểm từ injected `TimeProvider` hoặc dùng source timestamps deterministic.
- Search toàn Statistics source để không còn direct system clock trong logic kiểm thử.

#### Task F6.4 - Telemetry và shutdown regression

- Bổ sung audit cursor age, pending request count, reconciliation duration/result, coverage gaps và retention headroom metrics.
- Labels không dùng EventId/DeviceId/raw path.
- Shutdown dừng nhận cycle mới, chờ operation hiện tại theo timeout, không release lease trước transaction boundary.

#### Tests Phase F6

- Health truth table cho healthy/degraded/unhealthy.
- Pending request gần/vượt retention boundary.
- Dependencies unavailable và graceful draining.
- Health endpoints/exporter trả status đúng và redacted.
- FakeTimeProvider điều khiển toàn bộ clock-sensitive tests.

#### Deliverable và acceptance Phase F6

- Orchestrator/monitor bên ngoài probe được liveness/readiness.
- Không còn health false warning do so hai timestamp khác nghĩa.
- Không còn direct UtcNow trong Statistics Application logic.

### Phase F7 - Dependency security và schema upgrade verification

**Mục tiêu:** xử lý dependency warnings và chứng minh scripts an toàn cho cả fresh/existing database.

#### Task F7.1 - Dependency graph audit

- Chạy `dotnet list package --include-transitive --vulnerable` hoặc command tương đương trên solution.
- Xác định SharpCompress/Snappier đến từ package nào và phiên bản MongoDB.Driver nào đã fix.
- Nâng package ở mức nhỏ nhất an toàn, đọc breaking changes và chạy toàn bộ Mongo compatibility tests.
- Nếu chưa thể nâng, ghi risk owner, mitigation và deadline; không bỏ qua warning âm thầm.

#### Task F7.2 - Fresh schema test

- Tạo database test rỗng, chạy 009 trong context database đó.
- Xác nhận toàn bộ `[dbo].[DES.*]` tables/types/indexes/seed đúng và không có object ở schema khác.
- Chạy 009 lần hai: không xóa data/object và không tạo duplicate metric seed.

#### Task F7.3 - Existing schema upgrade test

- Tạo fixture schema theo 009 phiên bản cũ, chèn dữ liệu mẫu, chạy 010.
- Xác nhận dữ liệu cũ còn nguyên, columns/type V2 tồn tại, metric seed được sửa đúng.
- Chạy 010 lần hai để chứng minh idempotency cần thiết.
- Worker bản mới phải fail rõ khi 010 chưa chạy và start thành công khi schema đủ.

#### Deliverable và acceptance Phase F7

- Không còn vulnerability warning chưa có disposition.
- 009/010 chạy được trực tiếp trong SSMS trên database đang chọn.
- Không có DROP table/data trong upgrade path.

### Phase F8 - Integration tests, UAT, capacity và rollout

**Mục tiêu:** hoàn tất Phase 9 còn thiếu của Sprint 3 và thu evidence nghiệm thu.

#### Task F8.1 - Dựng integration fixtures

- Integration project dùng database/container test riêng, opt-in credentials và tên database có prefix test.
- Fixture cleanup chỉ xóa object/database do test tạo, không chạm `UA-REPORTING-DB` hoặc Mongo dev nghiệp vụ.
- Có Mongo V1/V2, duplicate, malformed, late, deep-skew, repeated state và multi-day fixtures.

#### Task F8.2 - SQL atomicity và lease suite

- Crash trước transaction, trong transaction, sau commit và ambiguous client result.
- Deadlock/transient retry có giới hạn.
- Duplicate event/concurrent writer dưới convention không unique index.
- Lease takeover và stale epoch bị fenced.
- DataRevision thay đổi làm stale reconciliation publish bị reject rồi retry tiến triển.

#### Task F8.3 - End-to-end functional suite

- History fixtures -> Mongo -> Statistics Worker -> SQL.
- Đối chiếu ProcessedEvent, metric, quality, snapshot, state, definition, dimension và coverage.
- Chạy incremental + audit + refresh + reconciliation đồng thời.
- Restart giữ checkpoint/audit cursor/request lifecycle đúng.

#### Task F8.4 - Runtime UAT thực tế

- Chạy ERP AppHub, rawlogs server, History Worker và Statistics Worker.
- Chọn một số Company/Device có tín hiệu connection/tag/control/sensor gần thời điểm test.
- Chụp Mongo source boundary trước khi query SQL để tránh so dữ liệu đang tiếp tục thay đổi.
- Đối chiếu exact EventId/outcome, metric counts, state observations/duration và coverage.
- Ghi pass/fail/evidence trong `Sprint-3-Testcase.md`; dữ liệu upstream thiếu không ghi pass giả.

#### Task F8.5 - Capacity và retention acceptance

- Chạy tối thiểu 100.000 events/ngày equivalent và backlog 300.000 events/3 ngày.
- Đo throughput, catch-up time, peak memory, p95 Mongo/SQL batch latency, reconciliation duration và report query impact.
- Chạy deep audit đồng thời, bảo đảm không làm incremental vượt SLO.
- Mô phỏng TTL xóa source giữa pages và downtime vượt retention; coverage phải partial/unrecoverable, không tạo số liệu “complete” giả.

#### Task F8.6 - Rollout có kiểm soát

1. Backup/snapshot evidence và dừng Statistics Worker, không reset History checkpoint.
2. Apply 010 trên existing database hoặc 009 trên fresh database bằng SSMS/deployment identity.
3. Deploy Worker disabled; chạy schema/dependency/index preflight.
4. Chạy limited scope hoặc projection version UAT; exact reconcile retained dirty ranges.
5. Enable Incremental; theo dõi lag, requests, audit completion, coverage và SQL load.
6. Chỉ sau khi retention tests pass mới preview rồi enable Mongo TTL 7 ngày.
7. Nếu lỗi, disable Statistics Worker; không xóa SQL evidence và không sửa checkpoint thủ công.

#### Deliverable và acceptance cuối Phase F8

- Unit, architecture và integration tests pass thực sự; không có “No test is available”.
- UAT gần thời điểm chạy có zero mismatch cho event/metric/quality/snapshot/state trong captured boundary.
- Pending request được xử lý trong threshold và audit sweep hoàn tất.
- Capacity/SLO/retention evidence được lưu.
- Runbook đủ start/stop/preflight/upgrade/reconcile/rollback/TTL.

## 9. Thứ tự phụ thuộc và chiến lược commit

```text
F0
 |
 +--> F1 reconciliation runtime --------+
 |                                      |
 +--> F2 deep discovery ----------------+--> F8 integration/UAT/rollout
 |                                      |
 +--> F3 registry + metadata -----------+
 |                                      |
 +--> F4 scoped SQL/Mongo contract -----+
 |                                      |
 +--> F5 metric registry ---------------+
 |                                      |
 +--> F6 health/time -------------------+
 |                                      |
 +--> F7 dependencies/schema scripts ---+
```

Thứ tự release khuyến nghị:

1. F0 test/contracts.
2. F1 + F2 là P0 runtime correctness.
3. F3 + F5 khóa registry/startup.
4. F4 thực hiện schema/TVP V2 và performance scoping.
5. F6 + F7 hardening.
6. F8 acceptance và rollout.

Mỗi commit/PR nên có một responsibility rõ và test đi kèm. Không trộn package upgrade, schema V2 và thay đổi reconciliation lớn trong một commit khó rollback.

## 10. Ma trận kiểm chứng bắt buộc

| Contract | Test tối thiểu | Evidence |
|---|---|---|
| Incremental reconciliation | Pending -> Completed trong Incremental mode | SQL request/run/state rows + logs |
| Paired scope | Không tạo company/device Cartesian product | Unit + SQL seeded requests |
| Deep discovery | Late sâu hơn overlap được ghi đúng một lần | Mongo fixture + inbox/facts/checkpoint |
| Registry resume | Stored definition resolve không cần config coverage | Unit + integration startup |
| Immutable definition | Version mismatch fail fast | Startup failure redacted |
| Metric enablement | Disabled/missing/duplicate metric bị reject | Unit + schema integration |
| Device dimension | Metadata upsert và preserve non-null | SQL integration |
| Scoped reconciliation | Query không đọc device ngoài request | Mongo command/query evidence + result |
| Recompute race | Revision đổi làm stale publish fail | Concurrency integration |
| Atomicity | Crash/ambiguous commit không double count | SQL integration |
| Fencing | Old epoch không write/publish | Lease integration |
| State | repeated/same-time/midnight/late/multi-day | Unit + E2E exact values |
| Coverage | retention gap không bị ghi complete | Integration + SQL coverage rows |
| Health | lag/request/headroom đúng semantics | Fake time + endpoint probe |
| Schema | 009 fresh và 010 upgrade không mất data | Disposable SQL database |
| Security | no secret/raw payload; dependency warnings xử lý | scan/log review/package report |
| Capacity | 100k/day, 300k backlog, concurrent audit/reconcile | benchmark report |

## 11. Rollback và dữ liệu hiện có

- Source of truth trong retained window vẫn là MongoDB; SQL facts không được sửa tay để “khớp số”.
- Nếu code mới lỗi, dừng Statistics Worker. History Worker tiếp tục ghi Mongo độc lập.
- Giữ nguyên ProcessedEvent/checkpoint/request/run/staging để điều tra; không reset high watermark.
- Rollback binary chỉ được thực hiện nếu schema change additive và version cũ vẫn đọc được. TVP V2 giúp giữ compatibility trong một release window.
- Với correction làm thay đổi semantics, dùng projection version mới và cutover sau coverage validation thay vì overwrite version production đã công bố.
- TTL không được bật hoặc phải được tạm dừng rollout nếu audit/reconciliation chưa theo kịp retention.

## 12. Definition of Done

- [ ] FIX-01 đến FIX-12 có code, tests và evidence; FIX-13 chỉ được đóng sau retention rollout gate.
- [ ] Reconciliation chạy trong Incremental và sửa được state mismatch thực tế.
- [ ] Global/explicit scope dùng cặp CompanyId/DeviceId đúng, không cross-product.
- [ ] Deep audit có durable cursor, full sweep completion và bắt deep-skew event.
- [ ] ProjectionDefinition được tạo/resolve/validate cho mọi mode; resume contract hoạt động.
- [ ] DeviceDimension được populate mà không chặn facts khi metadata thiếu.
- [ ] ProcessedEvent/TVP V2 có CompanyId/DeviceId; range query được scope tại source.
- [ ] Metric enabled/version contract nhất quán giữa SQL và mapper.
- [ ] Health headroom đúng ngữ nghĩa và có external probe/exporter.
- [ ] Statistics Application không dùng direct system clock.
- [ ] Dependency vulnerabilities được nâng cấp hoặc có risk disposition được phê duyệt.
- [ ] 009 fresh-create và 010 existing-upgrade được test trên database disposable.
- [ ] Integration suite có tests thật và pass; UAT evidence được ghi trong `Sprint-3-Testcase.md`.
- [ ] Mongo/SQL captured-boundary comparison không còn mismatch cho event, metrics, quality, snapshot và state.
- [ ] Capacity/retention/fault/concurrency acceptance đạt ngưỡng Sprint 3.
- [ ] Runbook phản ánh đúng deployment, schema scripts, modes, recovery và TTL gate.

Chỉ khi toàn bộ checklist trên hoàn thành mới coi Sprint 3 đã được đóng về implementation và vận hành. Các bảng đã tồn tại hoặc worker chạy không crash chỉ chứng minh deployment cơ bản, không thay thế correctness, recovery và retention acceptance.
