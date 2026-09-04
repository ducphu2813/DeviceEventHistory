# Sprint 3 Official Design — Thống kê sự kiện thiết bị (Device Event Statistics)

## 1. Quyết định

**Trạng thái (Status):** đề xuất thiết kế chính thức; cần phê duyệt triển khai.
**Lịch sử nguồn (Source history):** `Sprint-3-Design.md`, `Sprint-3-Schema.md`.
**Nguyên tắc ưu tiên khi xung đột (Supersedes on conflict):** tài liệu này có quyền ưu tiên cao nhất.
**Nguồn chân lý (Source of truth):** MongoDB `device_event_history`. SQL Server `device_stats` là read model có thể tái tạo (rebuildable).

Sprint 3 tạo `DeviceEventStatistics.Worker`, tiến trình độc lập:

```text
DeviceEventHistory.Worker
    -> MongoDB device_event_history
    -> DeviceEventStatistics.Worker
    -> SQL Server device_stats
```

Không ghi SQL đồng bộ (synchronous) trong luồng tiếp nhận dữ liệu (ingestion). Không dùng Hangfire. Không khử trùng lặp mờ liên nguồn (cross-source fuzzy dedupe).

## 2. Phạm vi (Scope)

### Bao gồm (In)

- Đếm metric theo ngày dựa trên `CompanyId + DeviceId + StatisticsDate`.
- Trạng thái kết nối / thời lượng (duration) theo múi giờ nghiệp vụ (business timezone).
- Incremental projection, tính lũy thừa (idempotency), cơ chế thử lại (retry), phục hồi sau sự cố (crash recovery).
- Đối soát (reconciliation) sự kiện trạng thái đến muộn / lệch thứ tự (late / out-of-order).
- Backfill / rebuild theo phiên bản (versioned).
- Tổng hợp chất lượng nguồn dữ liệu (source-quality aggregate), sự cố projection, nhật ký kiểm tra quá trình chạy (run audit).

### Hoãn lại (Deferred)

- API / dashboard.
- Khử trùng lặp sự kiện vật lý liên nguồn (cross-source physical-event dedupe).
- Bộ chiếu chủ động - chủ động (active-active projector).
- Mongo change stream.
- Điểm sức khỏe thiết bị (health score). Sprint 3 chỉ ghi các fact phục vụ tính sức khỏe; `HealthStatus`, `HealthScore`, `HealthRuleVersion`, `HealthReasonJson` giữ `NULL`.

Health V1 là giai đoạn phụ (sub-phase) riêng. Không suy đoán sức khỏe khi chưa có lịch vận hành (operating schedule) và bộ quy tắc cơ sở (baseline) được duyệt.

## 3. Các bất biến bất khả thương lượng (Non-negotiable invariants)

1. Lịch sử Mongo là nguồn dữ liệu bất biến (immutable source). SQL không được sửa ngược vào Mongo.
2. Giao dịch SQL (SQL transaction) phải commit / rollback đồng thời các mục sau:
   - `ProcessedEvent`;
   - Các thay đổi fact / snapshot / state hàng ngày;
   - Kết quả chất lượng / lỗi (quality / failure outcome);
   - `ProjectionCheckpoint`.
3. Nhận trùng lặp sự kiện (duplicate delivery) không được tăng số đếm hai lần.
4. Checkpoint chỉ tiến lên qua batch nguồn liên tục (contiguous) có kết quả xử lý cuối cùng (terminal outcome).
5. Mọi thao tác ghi SQL của incremental / reconciliation bắt buộc phải vượt qua chốt kiểm tra khóa (fencing):

```sql
LeaseOwner = @LeaseOwner
AND LeaseEpoch = @LeaseEpoch
AND LeaseExpiresAtUtc > SYSUTCDATETIME()
```

6. `timelineAtUtc` quyết định thứ tự trạng thái và ngày nghiệp vụ. `persistedAtUtc` chỉ dùng làm con trỏ đọc Mongo (Mongo cursor).
7. Sự kiện đến muộn có thể điều chỉnh lại dữ liệu ngày đã chốt (finalized).
8. Đối soát (reconciliation) phải tính toán lại chính xác (exact-recompute). Không cộng dồn độ lệch ước lượng (delta “đoán”).
9. Thời lượng snapshot luôn thỏa mãn:

```text
OnlineSeconds + OfflineSeconds + UnknownSeconds
= DATEDIFF_BIG(SECOND, BucketStartAtUtc, BucketEndAtUtc)
```

10. Các sự kiện trạng thái cùng `(CompanyId, DeviceId, StateType)` phải được xử lý tuần tự theo:

```text
timelineAtUtc ASC, eventId ASC
```

## 4. Kiến trúc mục tiêu (Target architecture)

```text
IncrementalProjectionHostedService
  -> ProjectionLeaseManager
  -> MongoHistoryEventReader
  -> Eligibility + Ownership + Metadata + Date resolver + Metric mapper
  -> SqlStatisticsBatchWriter

ReconciliationHostedService
  -> Durable ReconciliationRequest queue
  -> MongoHistoryRangeReader
  -> ExactStateRangeRebuilder
  -> SqlStatisticsRangeWriter
```

Các project:

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

Chiều phụ thuộc (Dependency direction):

```text
Domain <- Application <- Infrastructure <- Worker
```

Các project thống kê (Statistics) không tham chiếu đến `DeviceEventHistory.Worker`.

## 5. Hợp đồng xử lý sự kiện (Event processing contract)

### 5.1 Kết quả hợp lệ (Eligibility outcomes)

| Kết quả (Outcome) | Tác động lên SQL (SQL effect) |
|---|---|
| `aggregated` | Chèn `ProcessedEvent`; ghi nhận đóng góp metric / state. |
| `ignored` | Chèn `ProcessedEvent`; tùy chọn ghi nhận tổng hợp chất lượng. |
| `quality_only` | Chèn `ProcessedEvent`; ghi `IngestionQualityDaily`; không ghi fact thiết bị. |
| `failed_terminal` | Chèn `ProjectionFailure` + `ProcessedEvent`; checkpoint vẫn tiến lên. |
| transient failure | Rollback; checkpoint không tiến lên. |

### 5.2 Quyền sở hữu nguồn ban đầu (Initial ownership)

| Nhóm dữ liệu (Family) | Nguồn được tính (Source) |
|---|---|
| tag / business raw fact | `rfid_antenna_file` |
| device connection / control / sensor / scanner | `erp_apphub` |
| không xác định / chưa ánh xạ (opaque / unmapped) | chỉ ghi chất lượng (quality-only) |

Không khử trùng lặp bằng khoảng cách thời gian lân cận (proximity), mã băm payload (payload hash), hay cặp tag / device. Nếu producer có chung `sourceEventId`, sẽ lập sprint tương quan (correlation) riêng.

### 5.3 Metric MVP

Kích hoạt sau khi chạy fixture test:

```text
tag_read
business_process
device_connected
device_disconnected
device_online_observed
scanner_connected
scanner_disconnected
green_light_on
green_light_off
red_light_on
red_light_off
sensor_state_observed
device_error
snapshot_observed
```

`MetricDefinition` lưu cấu hình hiển thị / bật tắt / nhóm. Logic ánh xạ được quản lý theo phiên bản trong mã nguồn (code), không dùng SQL động (dynamic SQL).

## 6. Giao thức chiếu tăng dần (Incremental protocol)

Con trỏ (Cursor):

```text
LastPersistedAtUtc + LastEventId
```

Bộ đọc (Reader):

```text
fetchStartAtUtc = LastPersistedAtUtc - OverlapWindow
persistedAtUtc >= fetchStartAtUtc
ORDER BY persistedAtUtc ASC, eventId ASC
LIMIT BatchSize
```

Cấu hình mặc định:

```text
OverlapWindow = 00:05:00
BatchSize = 1000
PollInterval = 00:00:05
LeaseDuration = 00:02:00
```

Index Mongo bắt buộc:

```text
persistedAtUtc ASC, eventId ASC
```

Quy trình ghi theo batch (Batch writer):

1. Bắt đầu giao dịch SQL (SQL transaction).
2. Xác thực chủ sở hữu lease, epoch và thời hạn hiệu lực.
3. Chèn `ProcessedEvent` bằng TVP / stored procedure.
4. Chỉ giữ lại các sự kiện vừa được chèn thành công.
5. Cập nhật / chèn (upsert) nhóm `DeviceEventDaily`, `IngestionQualityDaily`.
6. Áp dụng các chuyển đổi trạng thái đúng thứ tự (in-order). Chuyển đổi lệch thứ tự (out-of-order) chỉ đưa vào hàng đợi đối soát bền vững (durable reconciliation).
7. Upsert các snapshot / cursor bị ảnh hưởng.
8. Tiến checkpoint đến sự kiện nguồn cuối cùng.
9. Commit giao dịch.

Xung đột khóa duy nhất (unique conflict) trên `ProcessedEvent` đồng nghĩa với sự kiện trùng lặp. Không ghi tổng hợp lần hai.

## 7. Trạng thái, thời lượng, sự kiện đến muộn (State, duration, late event)

### 7.1 Độ chính xác của thời lượng (Duration precision)

Snapshot lưu số nguyên giây. Bộ chiếu chuẩn hóa mọi thời điểm chuyển đổi trạng thái:

```text
stateTimelineAtUtc = floor(timelineAtUtc, 1 second)
```

`DeviceEventDaily.FirstEventAtUtc` và `LastEventAtUtc` giữ nguyên `timelineAtUtc` gốc. Thời lượng trạng thái chỉ sử dụng các thời điểm đã chuẩn hóa. Điều này đảm bảo phép toán khoảng thời gian cộng dồn mang tính tiền định (deterministic).

### 7.2 Sự kiện đúng thứ tự thông thường (Normal in-order event)

- Đóng khoảng thời gian cũ tại thời điểm chuyển đổi đã chuẩn hóa.
- Tách khoảng thời gian tại mỗi ranh giới ngày nghiệp vụ.
- Ghi các lát cắt thời lượng (duration slices).
- Cập nhật con trỏ trạng thái mới (state cursor).

Sự kiện quan sát lặp lại với cùng trạng thái:

- Tính vào metric quan sát nếu đã được ánh xạ;
- Không đóng / mở khoảng thời lượng mới;
- Chỉ cập nhật thông tin chẩn đoán (diagnostics).

### 7.3 Sự kiện trạng thái đến muộn hoặc lệch thứ tự (Out-of-order or late state event)

Bộ ghi tăng dần (Incremental writer) không được làm biến đổi `DeviceStateCursor` từ sự kiện trạng thái cũ (stale).

Bộ ghi phải thực hiện:

1. Lưu số đếm metric thông thường nếu sự kiện hợp lệ.
2. Chèn / gộp `ReconciliationRequest` trong cùng giao dịch.
3. Đánh dấu sự kiện nguồn là `aggregated`.
4. Để bộ đối soát (reconciler) tính toán lại chính xác dải snapshot trạng thái.

Dải dữ liệu bẩn (Dirty range):

```text
FromStatisticsDate = date chứa late transition
ToStatisticsDate   = min(
  date chứa transition kế tiếp đã biết sau late event,
  current business date
)
```

Bộ đối soát tiếp tục mở rộng về phía trước nếu trạng thái đóng của ngày vừa tái tạo khác với trạng thái mở của ngày kế tiếp hiện có. Chỉ dừng lại khi đạt điểm cố định hoặc vượt quá dải tối đa được cấu hình. Nếu dải vượt giới hạn, request chuyển thành `Failed` để người vận hành xử lý; không âm thầm cắt bớt dữ liệu.

### 7.4 Ranh giới tính toán lại chính xác (Exact recompute boundary)

Đối với dải thiết bị / trạng thái `[from, to]`, đọc:

- Chuyển đổi trạng thái mới nhất trước `BucketStart(from)`;
- Mọi chuyển đổi trạng thái trong khoảng `[BucketStart(from), BucketEnd(to))`;
- Chuyển đổi trạng thái đầu tiên sau dải khi cần xác định ranh giới lan truyền.

Bộ tái tạo (rebuilder) thay thế nguyên tử (atomically) các cột sinh từ trạng thái cho các dòng mục tiêu. Không dùng `ProcessedEvent` làm bộ lọc và không chèn lại các sự kiện đã xử lý. Metric sự kiện vẫn là fact tăng dần; giá trị trạng thái là giá trị thay thế lấy từ nguồn.

Incremental và reconciliation không được commit đồng thời trên cùng một dải thiết bị / trạng thái. Một lease / epoch duy nhất sẽ làm chốt khóa phân giải (fencing) cho cả hai.

## 8. Mô hình SQL (SQL model)

Giữ nguyên các bảng từ `Sprint-3-Schema.md`:

```text
DeviceDimension
MetricDefinition
DeviceEventDaily
DeviceDailySnapshot
DeviceStateCursor
ProcessedEvent
ProjectionCheckpoint
ReconciliationRequest
ProjectionFailure
ProjectionRun
IngestionQualityDaily
```

### 8.1 Thay đổi schema bắt buộc (Required schema deltas)

#### A. Lịch sử múi giờ (Timezone history)

Chỉ riêng `DeviceDimension.TimeZoneEffectiveFromUtc` không thể lưu giữ các thay đổi múi giờ trong lịch sử. Bổ sung:

```sql
CREATE TABLE [device_stats].[DeviceTimeZoneAssignment]
(
    [CompanyId] bigint NOT NULL,
    [DeviceId] bigint NOT NULL,
    [TimeZoneId] nvarchar(100) NOT NULL,
    [EffectiveFromUtc] datetime2(7) NOT NULL,
    [EffectiveToUtc] datetime2(7) NULL,
    [Source] varchar(64) NOT NULL,
    [CreatedAtUtc] datetime2(7) NOT NULL,
    [UpdatedAtUtc] datetime2(7) NOT NULL,
    [Version] rowversion NOT NULL,

    CONSTRAINT [PK_DeviceTimeZoneAssignment]
        PRIMARY KEY CLUSTERED ([CompanyId], [DeviceId], [EffectiveFromUtc]),
    CONSTRAINT [CK_DeviceTimeZoneAssignment_Range]
        CHECK ([EffectiveToUtc] IS NULL OR [EffectiveFromUtc] < [EffectiveToUtc])
);

CREATE UNIQUE INDEX [UX_DeviceTimeZoneAssignment_Current]
    ON [device_stats].[DeviceTimeZoneAssignment] ([CompanyId], [DeviceId])
    WHERE [EffectiveToUtc] IS NULL;
```

Bộ giải quyết múi giờ (timezone resolver) sử dụng cấu hình có hiệu lực tại thời điểm `timelineAtUtc`. Thay đổi múi giờ sẽ đóng cấu hình cũ, mở cấu hình mới, tạo yêu cầu đối soát cho dải lịch sử bị ảnh hưởng.

`DeviceDimension.TimeZoneId` vẫn là giá trị hiển thị / cache hiện tại.

#### B. Gộp yêu cầu đối soát (Reconciliation request merge)

Hàng đợi bộ điều phối (scheduler queue) cần cơ chế gộp mang tính tiền định. Bổ sung:

```sql
CREATE UNIQUE INDEX [UX_ReconciliationRequest_OpenRange]
    ON [device_stats].[ReconciliationRequest]
    (
        [ProjectionName],
        [ProjectionVersion],
        [CompanyId],
        [DeviceId],
        [StateType]
    )
    WHERE [Status] IN ('Pending', 'Processing');
```

Quy tắc gộp của bộ ghi (Writer merge rule):

```text
FromStatisticsDate = MIN(existing, incoming)
ToStatisticsDate   = MAX(existing, incoming)
RequestedAtUtc     = current UTC
```

Nếu trạng thái là `Processing`, worker khóa request và dải xử lý. Sự kiện đến muộn mới sẽ mở rộng request sau lượt chạy hiện tại hoặc tạo request `Pending` thay thế chỉ sau khi request hiện tại đạt trạng thái kết thúc. Không được âm thầm thay đổi dải đang hoạt động.

#### C. Chốt bảo vệ truy vấn (Query guard)

Truy vấn báo cáo trong tương lai bắt buộc phải chỉ định `ProjectionVersion` đang hoạt động được chọn. Không dùng `MAX(ProjectionVersion)` ngầm định.

## 9. Khóa thuê và tính đồng thời (Lease and concurrency)

Một writer hoạt động duy nhất cho:

```text
ProjectionName + ProjectionVersion + PartitionKey
```

Việc cấp mới / gia hạn lease sẽ tăng `LeaseEpoch` khi quyền sở hữu thay đổi. Mọi stored procedure ghi đều xác thực lease bên trong transaction. Worker dừng công việc mới ngay lập tức sau khi gia hạn thất bại. Transaction đang hoạt động chỉ được hoàn tất nếu kiểm tra chốt khóa (fencing) vẫn hợp lệ.

Không chia task / thread theo từng thiết bị. Xử lý song song việc ánh xạ metric chỉ được phép sau khi đo kiểm hiệu năng (benchmark); cùng một thiết bị / trạng thái vẫn phải đảm bảo tuần tự.

## 10. Xử lý sự cố (Failure handling)

| Sự cố (Failure) | Cách xử lý (Handling) |
|---|---|
| Sai hợp đồng sự kiện (Bad event contract) | Ghi `ProjectionFailure` vĩnh viễn (terminal); checkpoint tiến lên. |
| Mongo không khả dụng | Thử lại kèm giãn cách thời gian (retry / backoff); không biến đổi SQL; checkpoint giữ nguyên. |
| SQL lỗi tạm thời / deadlock | Rollback / thử lại cùng batch; checkpoint giữ nguyên. |
| Kết quả commit không rõ ràng | Thử lại cùng batch; `ProcessedEvent` sẽ khử trùng lặp. |
| Mất lease | Rollback / đánh dấu ghi thất bại; dừng worker. |
| Lỗi đối soát (Reconciliation failure) | Giữ durable request; tăng số lần thử; gửi cảnh báo. |

Không sao chép payload thô, secret, token, IP, session, connection ID sang SQL / logs.

## 11. Các chế độ chạy (Runtime modes)

| Chế độ (Mode) | Hành vi (Behavior) |
|---|---|
| `incremental` | Bộ đọc liên tục có cửa sổ gối đầu (overlap) và bộ ghi batch nguyên tử. |
| `reconciliation` | Xử lý hết các durable request cùng với cửa sổ nhìn lại (rolling lookback). |
| `backfill` | Xử lý chính xác dải ngày chỉ định; không dịch chuyển checkpoint của incremental. |
| `rebuild` | Tạo phiên bản projection mới, xây dựng lịch sử, đuổi kịp đuôi dữ liệu mới (tail catch-up), xác thực, chuyển đổi (cutover). |

Quy trình Rebuild:

```text
Tạo phiên bản N+1
Xây dựng lịch sử
Đuổi kịp dữ liệu mới nhất (tail)
Xác thực
Chuyển cấu hình báo cáo sang N+1
Giữ phiên bản N qua cửa sổ rollback
```

Không bao giờ reset và tái sử dụng phiên bản đang hoạt động.

## 12. Cổng nghiệm thu (Acceptance gates)

Triển khai chỉ bắt đầu sau khi:

1. Xác nhận chủ sở hữu cơ sở dữ liệu / schema SQL.
2. Xác nhận compound cursor index trong Mongo.
3. Xác nhận nguồn metadata / múi giờ thiết bị chuẩn có thẩm quyền.
4. Phê duyệt bộ dữ liệu mẫu (fixture) metric ban đầu và ma trận sở hữu.
5. Xác nhận hoãn tính năng Health.
6. Phê duyệt dải đối soát tối đa và khung giờ bảo trì.
7. Chọn chính sách lưu giữ cho `ProcessedEvent`: giữ lại toàn bộ các dòng của projection đang hoạt động. Không có job xóa dữ liệu trong Sprint 3.

Bằng chứng kiểm thử bắt buộc:

- Kiểm thử trùng lặp / thử lại / sự cố đột ngột (duplicate / retry / crash);
- Kiểm thử lệch commit con trỏ (cursor commit-skew);
- Kiểm thử chốt khóa tiến trình treo (fencing zombie-worker);
- Kiểm thử qua nửa đêm, đổi giờ mùa hè (DST), trạng thái lặp lại, trạng thái lệch thứ tự;
- Kiểm thử lan truyền tiến về phía trước qua nhiều ngày (multi-day forward-propagation);
- Kiểm thử thay thế dữ liệu đối soát chính xác;
- Benchmark 100.000 sự kiện/ngày, lưu lượng đột biến (burst), bù dữ liệu trễ 1–3 ngày;
- UAT đối soát khớp số đếm giữa Mongo và SQL.

## 13. Thứ tự triển khai (Implementation order)

1. Tạo solution / projects, options, architecture tests.
2. SQL migrations: base schema kèm các thay đổi delta bắt buộc.
3. Hợp đồng đọc Mongo, cursor gối đầu (overlap), mapper đầu vào projection.
4. Transaction xử lý batch sự kiện / checkpoint / fencing.
5. Projection metric hàng ngày và chất lượng dữ liệu.
6. Con trỏ trạng thái / bộ chiếu thời lượng (duration projector).
7. Hàng đợi đối soát bền vững và bộ tái tạo dải chính xác.
8. Backfill / rebuild / kiểm toán quá trình chạy (run audit).
9. Khả năng quan sát (observability), health checks, cơ chế tắt an toàn (graceful shutdown).
10. UAT backfill, so sánh đối soát, triển khai thử nghiệm trên nhóm tenant giới hạn.
