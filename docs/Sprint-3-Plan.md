# Device Event Statistics - Implementation Plan Sprint 3

> **Trạng thái (2026-08-30):** Kế hoạch triển khai kỹ thuật chính thức. Thiết kế nguồn từ `Sprint-3-Official-Design.md`, `Sprint-3-Technical-Flows.md` và schema từ `Sprint-3-Schema.md`.

---

## 1. Mục tiêu và Phạm vi

### 1.1 Mục tiêu Sprint 3
Triển khai hệ thống tổng hợp bất đồng bộ độc lập (`DeviceEventStatistics.Worker`) theo mô hình CQRS/Read-Model. Đọc dữ liệu sự kiện lịch sử bất biến từ MongoDB `device_event_history` và tổng hợp các chỉ số metric/thời lượng trạng thái (Online/Offline/Unknown) theo ngày nghiệp vụ (`StatisticsDate`) ghi vào SQL Server `device_stats.*`.

```text
MongoDB (device_event_history)
        │
        ▼ (Incremental / Bounded Overlap Batch Read)
DeviceEventStatistics.Worker
        │
        ▼ (Atomic SQL Transaction + Idempotency + Fencing)
SQL Server (device_stats.*)
```

### 1.2 Phạm vi chi tiết
- **Bao gồm (In-Scope):**
  - Khởi tạo cấu trúc solution/project mới cho `DeviceEventStatistics` độc lập với ingestion worker.
  - SQL Schema migrations (bảng cơ sở + các thay đổi delta: `DeviceTimeZoneAssignment`, unique index gộp `ReconciliationRequest`).
  - Giao thức chiếu tăng dần (Incremental Projection Engine): Cursor `(LastPersistedAtUtc, LastEventId)`, Overlap Window, Leader Election / Fencing Token (`LeaseEpoch`), phân quyền sở hữu nguồn dữ liệu (`Source Ownership`).
  - Xử lý ngày nghiệp vụ, giải quyết múi giờ động theo lịch sử (`TimeZoneResolver`).
  - Bộ tính toán trạng thái và lát cắt thời lượng liên tục (`StateContinuityDurationCalculator`), chuẩn hóa mốc thời gian về giây nguyên vẹn.
  - Giao dịch SQL Batch nguyên tử: TVP, chèn `ProcessedEvent` (idempotency), upsert `DeviceEventDaily`, `DeviceDailySnapshot`, `DeviceStateCursor`, cập nhật `ProjectionCheckpoint`.
  - Cơ chế đối soát bền vững (`Durable Reconciliation Engine`): bắt sự kiện lệch thứ tự (out-of-order), tính toán lại chính xác (exact-recompute), lan truyền trạng thái xuyên ngày (forward propagation).
  - Khả năng kiểm toán, chất lượng dữ liệu (`IngestionQualityDaily`), nhật ký chạy (`ProjectionRun`), xử lý lỗi cuối cùng (`ProjectionFailure`).
  - Pipeline Rebuild / Backfill theo phiên bản (`ProjectionVersion`).

- **Hoãn lại (Deferred / Out-of-Scope):**
  - API / UI Dashboard báo cáo.
  - Khử trùng lặp mờ liên nguồn (cross-source fuzzy dedupe).
  - Bộ tính điểm sức khỏe thiết bị (Health Score V1 - giữ các cột `Health*` là `NULL`).
  - MongoDB Change Streams.

---

## 2. Nguyên tắc và Bất biến Bất khả Thương lượng

1. **Nguồn chân lý bất biến:** MongoDB `device_event_history` là read-only đối với Statistics Worker.
2. **Giao dịch nguyên tử (Atomic Commit):** Một transaction SQL phải đồng thời commit:
   - `ProcessedEvent`;
   - Fact `DeviceEventDaily` và snapshot `DeviceDailySnapshot`;
   - Con trỏ `DeviceStateCursor`;
   - Lỗi/chất lượng `ProjectionFailure` / `IngestionQualityDaily`;
   - Checkpoint `ProjectionCheckpoint`.
3. **Chống trùng lặp (Idempotency):** Khóa duy nhất trên `ProcessedEvent` đảm bảo không bao giờ tăng số đếm hai lần khi đọc lại sự kiện.
4. **Bảo vệ bằng chốt khóa (Fencing Lease):** Mọi lệnh ghi SQL bắt buộc phải kiểm tra:
   ```sql
   LeaseOwner = @LeaseOwner AND LeaseEpoch = @LeaseEpoch AND LeaseExpiresAtUtc > SYSUTCDATETIME()
   ```
5. **Thứ tự nghiệp vụ:** `timelineAtUtc` quyết định thứ tự trạng thái và ngày nghiệp vụ; `persistedAtUtc` chỉ dùng làm con trỏ đọc MongoDB.
6. **Đối soát chính xác (Exact Recompute):** Xử lý out-of-order bằng cách tái tạo lại chính xác snapshot từ các mốc chuyển đổi thực tế; không cộng dồn delta ước lượng.
7. **Bảo toàn thời lượng:** Tổng thời lượng mỗi ngày snapshot luôn thỏa mãn:
   ```text
   OnlineSeconds + OfflineSeconds + UnknownSeconds = Tổng số giây của ngày nghiệp vụ (86400s)
   ```

---

## 3. Kiến trúc Solution & Phụ thuộc Dự án

### 3.1 Cấu trúc Solution
```text
src/
  DeviceEventStatistics.Domain/
    ├── Common/
    ├── Entities/
    ├── ValueObjects/
    └── Specifications/
  DeviceEventStatistics.Application/
    ├── Common/Interfaces/
    ├── Models/
    ├── Projections/
    ├── Reconciliation/
    └── Services/
  DeviceEventStatistics.Infrastructure/
    ├── Mongo/
    ├── Sql/
    │   ├── Migrations/
    │   ├── Repositories/
    │   └── StoredProcedures/
    ├── TimeZone/
    └── Caching/
  DeviceEventStatistics.Worker/
    ├── HostedServices/
    ├── Options/
    └── Program.cs

tests/
  DeviceEventStatistics.UnitTests/
  DeviceEventStatistics.IntegrationTests/
  DeviceEventStatistics.ArchitectureTests/
```

### 3.2 Chiều phụ thuộc
```text
Domain  <──  Application  <──  Infrastructure  <──  Worker
```
*Lưu ý: Các project Statistics hoàn toàn không tham chiếu trực tiếp đến `DeviceEventHistory.Worker`.*

---

## 4. Đặc Tả Lược Đồ Dữ Liệu SQL Server (`device_stats`)

### Bảng 1: `ProjectionCheckpoint` (Quản lý Cursor & Fencing Lease)

```sql
CREATE TABLE [device_stats].[ProjectionCheckpoint]
(
    [ProjectionName]        varchar(100)  NOT NULL,
    [ProjectionVersion]     int           NOT NULL,
    [PartitionKey]          varchar(100)  NOT NULL,
    [LastPersistedAtUtc]    datetime2(7)  NULL,
    [LastEventId]           varchar(64) COLLATE Latin1_General_100_BIN2 NULL,
    [LastProcessedAtUtc]    datetime2(7)  NULL,
    [LastBatchSize]         int           NOT NULL,
    [LeaseOwner]            varchar(200)  NULL,
    [LeaseExpiresAtUtc]     datetime2(7)  NULL,
    [LeaseEpoch]            bigint        NOT NULL DEFAULT 0,
    [UpdatedAtUtc]          datetime2(7)  NOT NULL,
    [Version]               rowversion    NOT NULL,

    CONSTRAINT [PK_ProjectionCheckpoint] PRIMARY KEY CLUSTERED ([ProjectionName], [ProjectionVersion], [PartitionKey])
);
```

### Bảng 2: `ReconciliationRequest` (Hàng Đợi Reconcile Bền Vững)

```sql
CREATE TABLE [device_stats].[ReconciliationRequest]
(
    [RequestId]             bigint         IDENTITY(1,1) NOT NULL,
    [ProjectionName]        varchar(100)   NOT NULL,
    [ProjectionVersion]     int            NOT NULL,
    [CompanyId]             bigint         NOT NULL,
    [DeviceId]              bigint         NOT NULL,
    [StateType]             varchar(64)    NOT NULL,
    [FromStatisticsDate]    date           NOT NULL,
    [ToStatisticsDate]      date           NOT NULL,
    [ReasonCode]            varchar(64)    NOT NULL,
    [Status]                varchar(32)    NOT NULL DEFAULT 'Pending',
    [AttemptCount]          int            NOT NULL DEFAULT 0,
    [RequestedAtUtc]        datetime2(7)   NOT NULL,
    [StartedAtUtc]          datetime2(7)   NULL,
    [CompletedAtUtc]        datetime2(7)   NULL,
    [ErrorSummary]          nvarchar(1000) NULL,
    [Version]               rowversion     NOT NULL,

    CONSTRAINT [PK_ReconciliationRequest] PRIMARY KEY CLUSTERED ([RequestId]),
    CONSTRAINT [CK_ReconciliationRequest_Status] CHECK ([Status] IN ('Pending', 'Processing', 'Completed', 'Failed', 'Cancelled'))
);
```

### Bảng 3: `DeviceDailySnapshot` (Snapshot Trạng Thái & Sức Khỏe Thiết Bị)

```sql
CREATE TABLE [device_stats].[DeviceDailySnapshot]
(
    [ProjectionVersion]        int            NOT NULL,
    [CompanyId]                bigint         NOT NULL,
    [DeviceId]                 bigint         NOT NULL,
    [StatisticsDate]           date           NOT NULL,
    [TimeZoneId]               nvarchar(100)  NOT NULL,
    [BucketStartAtUtc]         datetime2(7)   NOT NULL,
    [BucketEndAtUtc]           datetime2(7)   NOT NULL,
    [OpeningConnectionStatus]  varchar(32)    NOT NULL,
    [ClosingConnectionStatus]  varchar(32)    NOT NULL,
    [OnlineSeconds]            bigint         NOT NULL,
    [OfflineSeconds]           bigint         NOT NULL,
    [UnknownSeconds]           bigint         NOT NULL,
    [ConnectedEventCount]      int            NOT NULL,
    [DisconnectedEventCount]   int            NOT NULL,
    [ReconnectCount]           int            NOT NULL,
    [TotalEventCount]          bigint         NOT NULL,
    [ErrorEventCount]          bigint         NOT NULL,
    [WarningEventCount]        bigint         NOT NULL,
    [HealthStatus]             varchar(32)    NOT NULL,
    [HealthScore]              decimal(5,2)   NULL,
    [HealthRuleVersion]        int            NULL,
    [HealthReasonJson]         nvarchar(max)  NULL,
    [IsFinalized]              bit            NOT NULL,
    [LastEventAtUtc]           datetime2(7)   NULL,
    [LastReconciledAtUtc]      datetime2(7)   NULL,
    [CreatedAtUtc]             datetime2(7)   NOT NULL,
    [UpdatedAtUtc]             datetime2(7)   NOT NULL,
    [Version]                  rowversion     NOT NULL,

    CONSTRAINT [PK_DeviceDailySnapshot] PRIMARY KEY CLUSTERED ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate]),
    CONSTRAINT [CK_DeviceDailySnapshot_DurationTotal] CHECK ([OnlineSeconds] + [OfflineSeconds] + [UnknownSeconds] = DATEDIFF_BIG(SECOND, [BucketStartAtUtc], [BucketEndAtUtc]))
);
```

---

## 5. Các Giai đoạn Thực hiện Chi tiết (Phases & Tasks)

### Phase 1: Khởi tạo Project, Cấu hình & Architecture Tests
- [ ] **P1.1**: Tạo solution folders và các project `.NET 10` (`Domain`, `Application`, `Infrastructure`, `Worker`, `UnitTests`, `IntegrationTests`, `ArchitectureTests`).
- [ ] **P1.2**: Viết Architecture Tests (`NetArchTest.Rules` hoặc reflection rules) kiểm tra chiều phụ thuộc nghiêm ngặt giữa các tầng.
- [ ] **P1.3**: Thiết lập hệ thống `Options` (Database connection strings, Projection runtime options, Lease options, Reconciliation options) và binding trong `appsettings.json`.

### Phase 2: Database Migration & SQL Stored Procedures
- [ ] **P2.1**: Thiết lập DB migration script tạo schema `device_stats` và các bảng cơ sở:
  - `DeviceDimension`, `MetricDefinition`, `DeviceEventDaily`, `DeviceDailySnapshot`, `DeviceStateCursor`, `ProcessedEvent`, `ProjectionCheckpoint`, `ReconciliationRequest`, `ProjectionFailure`, `ProjectionRun`, `IngestionQualityDaily`.
- [ ] **P2.2**: Áp dụng các thay đổi Schema Delta bắt buộc:
  - Tạo bảng `[device_stats].[DeviceTimeZoneAssignment]` và unique filter index `[UX_DeviceTimeZoneAssignment_Current]`.
  - Tạo unique filter index `[UX_ReconciliationRequest_OpenRange]` hỗ trợ gộp request đối soát tự động.
- [ ] **P2.3**: Viết Stored Procedures / User-Defined Table Types (TVP) phục vụ ghi batch hiệu năng cao:
  - TVP `ProcessedEventTableType`, `DeviceEventDailyTableType`, `DeviceDailySnapshotTableType`.
  - SP `sp_AcquireOrRenewLease` (xử lý atomic epoch increment và heartbeat).
  - SP `sp_PersistIncrementalBatch` (thực hiện batch insert `ProcessedEvent`, upsert fact/snapshot/cursor, và advance checkpoint dưới sự bảo vệ của Fencing Token).
  - SP `sp_MergeReconciliationRequest` (gộp mở rộng dải ngày bẩn khi phát hiện out-of-order transition).

### Phase 3: Domain Model & TimeZone Assignment Resolver
- [ ] **P3.1**: Định nghĩa Domain Entities / Value Objects:
  - `StatisticsDate`, `TimeRangeUtc`, `EventIdentity`, `LeaseFencingToken`, `StateTransition`, `DurationSlice`.
- [ ] **P3.2**: Triển khai `TimeZoneAssignmentResolver`:
  - Tìm múi giờ có hiệu lực của thiết bị tại thời điểm `timelineAtUtc` dựa trên cấu hình lịch sử `DeviceTimeZoneAssignment`.
  - Tính toán chuyển đổi chính xác `timelineAtUtc` sang ngày nghiệp vụ `StatisticsDate` (YYYY-MM-DD) và ranh giới `[BucketStartAtUtc, BucketEndAtUtc]`.

### Phase 4: Mongo History Reader & Source Eligibility Mapping
- [ ] **P4.1**: Tạo `MongoHistoryReader` với Cursor `(LastPersistedAtUtc, LastEventId)` và cơ chế `OverlapWindow` (mặc định 5 phút).
- [ ] **P4.2**: Triển khai bộ phân loại điều kiện và quyền sở hữu nguồn (`SourceOwnershipEvaluator`):
  - Nhóm `rfid_antenna_file`: Nhận tag read & business process facts.
  - Nhóm `erp_apphub`: Nhận connection, control, scanner, sensor facts.
  - Phân loại kết quả: `aggregated`, `ignored`, `quality_only`, `failed_terminal`.
- [ ] **P4.3**: Triển khai `MetricMapperRegistry`:
  - Ánh xạ mã sự kiện chuẩn hóa sang các metric MVP (`tag_read`, `device_connected`, `device_disconnected`, `green_light_on`, v.v.).

### Phase 5: Xử lý Trạng thái, Lát cắt Thời lượng & Chuẩn hóa Giây
- [ ] **P5.1**: Triển khai bộ chuẩn hóa thời gian: `stateTimelineAtUtc = floor(timelineAtUtc, 1 second)`.
- [ ] **P5.2**: Xây dựng `StateContinuityDurationCalculator`:
  - Xử lý sự kiện in-order: Đóng khoảng thời gian cũ, cắt lát theo ranh giới nửa đêm (00:00:00) cho từng ngày nghiệp vụ liên quan, cộng dồn `OnlineSeconds`/`OfflineSeconds`/`UnknownSeconds`.
  - Xử lý sự kiện lặp trạng thái: Cập nhật chẩn đoán/metric, không tạo lát cắt mới.
- [ ] **P5.3**: Bắt sự kiện out-of-order / late state transition:
  - Nhận diện `timelineAtUtc < LastTransitionAtUtc` của cursor hiện tại.
  - Đóng gói dải ngày bẩn `[FromStatisticsDate, ToStatisticsDate]` và tạo/gộp `ReconciliationRequest`.

### Phase 6: Incremental Batch Writer & Fencing Execution
- [ ] **P6.1**: Triển khai `ProjectionLeaseManager`:
  - Xin lease, quản lý chu kỳ heartbeat nền, theo dõi `LeaseEpoch` và kích hoạt tự hủy/dừng worker khi mất lease.
- [ ] **P6.2**: Triển khai `SqlStatisticsBatchWriter`:
  - Mở SQL transaction có fencing check.
  - Chèn TVP `ProcessedEvent`, lọc ra danh sách sự kiện mới.
  - Tổng hợp nhóm và upsert `DeviceEventDaily`, `IngestionQualityDaily`, `DeviceDailySnapshot`, `DeviceStateCursor`.
  - Commit transaction và advance checkpoint.

### Phase 7: Durable Reconciliation Engine & Multi-Day Forward Propagation
- [ ] **P7.1**: Xây dựng `ReconciliationHostedService`:
  - Polling hàng đợi `ReconciliationRequest` (các request ở trạng thái `Pending`).
  - Khóa request sang trạng thái `Processing`.
- [ ] **P7.2**: Triển khai `ExactStateRangeRebuilder`:
  - Đọc chuyển đổi trạng thái cuối cùng trước `BucketStart(FromDate)` từ MongoDB.
  - Đọc toàn bộ chuyển đổi trong dải `[BucketStart(FromDate), BucketEnd(ToDate))`.
  - Tái tạo lại chính xác từng lát cắt thời lượng cho các ngày trong dải.
- [ ] **P7.3**: Lan truyền tiến về phía trước (Forward State Propagation):
  - So sánh trạng thái kết thúc ngày `ToDate` với trạng thái mở đầu ngày `ToDate + 1`.
  - Nếu khác biệt, tự động mở rộng dải đối soát sang ngày tiếp theo cho đến khi đạt điểm cố định (Fixed Point) hoặc chạm ranh giới ngày hiện tại / trần cấu hình tối đa.
- [ ] **P7.4**: Cập nhật kết quả đối soát nguyên tử vào SQL Server, hoàn tất request (`Completed` hoặc `Failed`).

### Phase 8: Backfill, Versioned Rebuild & Cutover
- [ ] **P8.1**: Xây dựng công cụ CLI / Hosted Service cho chế độ `Backfill`:
  - Nhận tham số dải ngày `FromDate` -> `ToDate`, chạy chiếu độc lập không làm xê dịch checkpoint của incremental.
- [ ] **P8.2**: Xây dựng quy trình `Rebuild Pipeline`:
  - Tạo `ProjectionVersion = N + 1`.
  - Xây dựng dữ liệu lịch sử song song.
  - Đuổi kịp đuôi dữ liệu mới (Tail catch-up).
  - Đối soát xác thực số liệu giữa hai phiên bản và chuyển đổi cutover sang phiên bản mới.

### Phase 9: Worker Hosted Services, Observability & Health Checks
- [ ] **P9.1**: Tích hợp các Hosted Services vào `DeviceEventStatistics.Worker`:
  - `IncrementalProjectionHostedService`.
  - `ReconciliationHostedService`.
  - `LeaseHeartbeatHostedService`.
- [ ] **P9.2**: Cấu hình Logging có cấu trúc (Serilog), OpenTelemetry Metrics & Health Checks endpoint.
- [ ] **P9.3**: Đảm bảo cơ chế Graceful Shutdown an toàn, giải phóng lease sạch sẽ khi tiến trình dừng.

### Phase 10: Kiểm thử Toàn diện & Cổng Nghiệm thu (Verification & UAT)
- [ ] **P10.1**: Unit Tests:
  - Logic tính lát cắt thời lượng qua nửa đêm, đổi múi giờ (DST), chuẩn hóa giây, mapper metric.
- [ ] **P10.2**: Integration Tests (Testcontainers Mongo + SQL Server):
  - Duplicate / Retry idempotency test (gửi lại batch cũ không tăng đếm).
  - Out-of-order reconciliation & forward propagation test.
  - Fencing Token test (chặn zombie worker có epoch cũ).
  - Overlap window catch-up test.
- [ ] **P10.3**: Performance Benchmark & UAT:
  - Kiểm thử tải 100.000 sự kiện/ngày, kiểm tra khớp số liệu 100% giữa MongoDB và SQL Server.

---

## 6. Ma trận Rủi ro & Giải pháp Kiểm soát

| Rủi ro kỹ thuật | Mức độ | Biện pháp kiểm soát trong Plan |
|---|---|---|
| **Zombie Worker ghi đè dữ liệu cũ** | Cao | Fencing token `LeaseEpoch` bắt buộc trong mọi Stored Procedure ghi SQL. |
| **Trùng lặp số đếm khi retry batch** | Cao | Bảng `ProcessedEvent` làm bộ lọc chặn tại transaction SQL. |
| **Lệch thời lượng do sai số mili-giây** | Trung bình | Chuẩn hóa mốc thời gian chuyển đổi `stateTimelineAtUtc` về giây nguyên vẹn (`floor`). |
| **Lan truyền trạng thái vô tận khi đối soát** | Trung bình | Đặt ngưỡng tối đa (`MaxReconciliationDays`), chuyển sang `Failed` kèm cảnh báo nếu vượt ngưỡng. |
| **Tắc nghẽn đọc MongoDB** | Thấp | Compound index bắt buộc `(persistedAtUtc ASC, eventId ASC)` và bounded limit. |
