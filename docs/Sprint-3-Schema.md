# Device Event Statistics - SQL Server Schema Sprint 3

## 1. Trạng thái và mục đích

- Trạng thái: thiết kế schema trước implementation.
- Phạm vi: lưu read model thống kê thiết bị theo ngày trên SQL Server.
- Nguồn dữ liệu gốc: MongoDB collection `device_event_history` của Device Event History Worker.
- Nền tảng mục tiêu: SQL Server hiện có của hệ thống Report ERP.
- SQL schema sử dụng: `dbo`.
- Thiết kế luồng xử lý: `Sprint-3-Design.md`.
- Canonical source contract: `Sprint-2-Db-Schema.md`.

Tài liệu này là source of truth của Sprint 3 cho:

- table shape, key, data type và index của statistics read model;
- contract daily aggregate và daily state snapshot;
- idempotency, projection checkpoint và projection failure;
- retention, rebuild, late-event và versioning semantics;
- ranh giới dữ liệu giữa MongoDB history và SQL Server statistics.

MongoDB tiếp tục là source of truth. Dữ liệu trong SQL Server là projection có thể xóa và dựng lại từ MongoDB; không được dùng SQL statistics để sửa ngược raw history.

## 2. Mục tiêu schema

Schema phải trả lời hiệu quả các câu hỏi:

- Trong một ngày, một thiết bị phát sinh từng loại event bao nhiêu lần?
- Event đầu tiên và cuối cùng trong ngày xảy ra khi nào?
- Trong tuần/tháng, event nào tăng hoặc lặp lại bất thường?
- Thiết bị đã online/offline/unknown trong bao lâu?
- Trạng thái mở đầu và kết thúc ngày của thiết bị là gì?
- Daily health status/score được tạo từ rule version nào và vì sao?
- Statistics Worker đã xử lý tới Mongo event nào?
- Một event đã được cộng vào projection hay chưa?
- Event nào không thể đưa vào device statistics và nguyên nhân là gì?

Schema không lưu lại toàn bộ raw payload. Khi cần điều tra chi tiết, API/operator dùng `eventId` để truy ngược MongoDB `device_event_history`.

## 3. Quyết định thiết kế

1. Dùng schema mặc định `dbo` trong database Report ERP; không đặt bảng vào schema `HangFire`.
2. Statistics dùng chung database Report ERP nhưng giữ quyền SQL tối thiểu cho các bảng thuộc projection.
3. Dùng daily grain làm grain bền vững đầu tiên. Week/month được query bằng cách tổng hợp daily rows.
4. Tách event count khỏi state/duration snapshot.
5. Dùng tall fact cho event metric; không tạo một cột mới mỗi khi có event mới.
6. Dùng `CompanyId + DeviceId` làm tenant/device business key.
7. Không tạo cross-database foreign key tới ERP.
8. Dùng `timelineAtUtc` để xếp event vào ngày theo configured business timezone; không dùng `persistedAtUtc` làm ngày nghiệp vụ.
9. Cho phép late event cập nhật ngày cũ. Daily statistics không phải accounting ledger bất biến.
10. Dùng `ProjectionVersion` và `HealthRuleVersion` để rebuild hoặc thay đổi rule có kiểm soát.
11. Dùng rowstore/B-tree ở Sprint 3. Chưa dùng columnstore hoặc table partitioning trước khi benchmark chứng minh cần.
12. SQL write phải idempotent; duplicate projection delivery không làm tăng `EventCount` lần hai.

## 4. Nguồn field từ MongoDB

Statistics projector chỉ đọc các field cần thiết từ canonical history:

| Mongo field | Vai trò trong statistics |
|---|---|
| `_id` | diagnostic/source cursor support khi cần |
| `eventId` | idempotency key và trace về history |
| `schemaVersion` | compatibility routing |
| `companyId` | tenant key |
| `category` | event family |
| `sourceKind` | source ownership và chống cộng chéo sai |
| `timelineAtUtc` | event ordering và daily bucket |
| `timeBasis` | confidence: `occurred` hoặc `received` |
| `receivedAtUtc` | transport/ingestion timing |
| `persistedAtUtc` | incremental projection cursor |
| `source.sourceId` | source diagnostics/quality |
| `source.eventName` | callback/raw event classification |
| `device.id` | device key |
| `device.gateId` | optional device/gate dimension |
| `device.code/name/gateCode/gateName` | optional display metadata candidate |
| `facts.*` | metric discriminator, state và duration input |
| `parse.status` | eligibility và data-quality count |

Không đọc raw text/arguments cho normal aggregation. Raw payload chỉ được mở khi điều tra một mapping case riêng và không được copy sang SQL statistics.

## 5. Naming và data type conventions

### 5.1. Naming

- SQL schema: `[dbo]`.
- Table names: `DES.<BaseTableName>` and must be referenced as
  `[dbo].[DES.<BaseTableName>]` because the dot is part of the table name.
- Column: PascalCase.
- Mỗi physical table có đúng một primary key dạng
  `<TableName>Id INT IDENTITY(1, 1) PRIMARY KEY`, khai báo inline.
- Tất cả cột ngoài primary key đều cho phép `NULL`; application chịu trách nhiệm
  validate và ghi đủ dữ liệu bắt buộc theo contract.
- Không dùng composite primary key, unique index, foreign key, check constraint
  hoặc default constraint.
- Business identity được tra cứu và khóa bằng non-unique index cùng SQL transaction lock.
- Non-unique index: `IX_<Table>_<Columns>`.

File `009_CreateDeviceEventStatisticsSchema.sql` là physical DDL chuẩn để triển khai.
Các đoạn SQL chi tiết phía dưới mô tả logical column contract; PK và nullability vật lý
luôn tuân theo convention tại mục này và script 009.

### 5.2. Data type

| Concept | SQL type |
|---|---|
| Company/device/gate ID | `bigint` |
| Date grain | `date` |
| UTC instant | `datetime2(7)` |
| Counter/duration seconds | `bigint` |
| Enum/code | `varchar(...)` |
| Display text | `nvarchar(...)` |
| SHA-256 identity | `binary(32)` |
| Health score 0..100 | `decimal(5,2)` |
| Optimistic concurrency | `rowversion` |
| Optional structured reason | `nvarchar(max)` chứa JSON |

Tất cả cột có hậu tố `AtUtc` lưu UTC. Không dùng SQL `timestamp` làm thời gian; `rowversion` chỉ dùng concurrency.

`eventId` trong Mongo là lowercase SHA-256 hex. Application phải validate đúng 64 hex characters rồi convert thành `binary(32)` trước khi ghi `ProcessedEvent`. Nếu source document bị corrupt và không có valid event ID, `ProjectionFailure` dùng deterministic failure identity cùng source document identity để vẫn trace/idempotent mà không copy raw payload.

## 6. Tổng quan tables

```text
[dbo].[DES.DeviceDimension]
[dbo].[DES.MetricDefinition]
[dbo].[DES.DeviceEventDaily]
[dbo].[DES.DeviceDailySnapshot]
[dbo].[DES.DeviceStateCursor]
[dbo].[DES.ProcessedEvent]
[dbo].[DES.ProjectionCheckpoint]
[dbo].[DES.ReconciliationRequest]
[dbo].[DES.ProjectionFailure]
[dbo].[DES.ProjectionRun]
[dbo].[DES.IngestionQualityDaily]
```

Quan hệ logic:

```text
DeviceDimension 1 ----- * DeviceEventDaily * ----- 1 MetricDefinition
       |
       +--------------- * DeviceDailySnapshot
       |
       `--------------- * DeviceStateCursor

ProjectionCheckpoint ---- incremental cursor/lease (với LeaseEpoch fencing)
ReconciliationRequest --- hàng đợi bền vững lưu dirty ranges cần reconcile
ProcessedEvent ----------- idempotency inbox
ProjectionFailure -------- terminal/retry diagnostics
ProjectionRun ------------ incremental/reconcile/backfill audit
IngestionQualityDaily ----- source/data-quality aggregate
```

Không tạo foreign key trong `dbo`. Projection correctness không được phụ thuộc vào việc ERP/device catalog đã sync kịp; projector vẫn phải tạo hoặc cập nhật placeholder `DeviceDimension` theo application contract khi cần.

## 7. `DeviceDimension`

Giữ current display metadata phục vụ report. Đây không phải authoritative device master và không thay thế ERP catalog.

```sql
CREATE TABLE [dbo].[DES.DeviceDimension]
(
    [DeviceDimensionId]  INT IDENTITY(1, 1) PRIMARY KEY,
    [CompanyId]          bigint         NULL,
    [DeviceId]           bigint         NULL,
    [DeviceCode]         nvarchar(100)  NULL,
    [DeviceName]         nvarchar(250)  NULL,
    [DeviceType]         varchar(64)    NULL,
    [GateId]             bigint         NULL,
    [GateCode]           nvarchar(100)  NULL,
    [GateName]           nvarchar(250)  NULL,
    [TimeZoneId]         nvarchar(100)  NULL,
    [TimeZoneEffectiveFromUtc] datetime2(7) NULL,
    [IsActive]           bit            NULL,
    [MetadataSource]     varchar(64)    NULL,
    [MetadataUpdatedAtUtc] datetime2(7) NULL,
    [CreatedAtUtc]       datetime2(7)   NULL,
    [UpdatedAtUtc]       datetime2(7)   NULL,
    [Version]            rowversion     NULL
);
```

Rules:

- `TimeZoneId` bắt buộc vì daily bucket phụ thuộc business timezone.
- Metadata ưu tiên từ authoritative catalog/configuration resolver; display field từ event chỉ là candidate.
- Không overwrite non-null authoritative metadata bằng null/opaque event payload.
- `MetadataSource` ví dụ: `erp_catalog`, `source_configuration`, `event_payload`, `placeholder`.
- Một device ID chỉ unique trong tenant; không unique `DeviceId` toàn hệ thống.

Indexes:

```text
IX_DeviceDimension_Company_Active
    (CompanyId, IsActive, DeviceId)

IX_DeviceDimension_Company_Gate
    (CompanyId, GateId, DeviceId)
    WHERE GateId IS NOT NULL
```

## 8. `MetricDefinition`

Registry cho canonical statistics metric. Mapping logic nằm trong versioned application code; table cung cấp stable key, display metadata, grouping và health eligibility.

```sql
CREATE TABLE [dbo].[DES.MetricDefinition]
(
    [MetricKey]          int            IDENTITY(1,1) NOT NULL,
    [MetricCode]         varchar(100)   NOT NULL,
    [DisplayName]        nvarchar(250)  NOT NULL,
    [MetricGroup]        varchar(64)    NOT NULL,
    [Unit]               varchar(32)    NOT NULL,
    [DefaultCategory]    varchar(64)    NULL,
    [DefaultSourceKind]  varchar(64)    NULL,
    [IsHealthInput]      bit            NOT NULL,
    [IsEnabled]          bit            NOT NULL,
    [MappingVersion]     int            NOT NULL,
    [CreatedAtUtc]       datetime2(7)   NOT NULL,
    [UpdatedAtUtc]       datetime2(7)   NOT NULL,
    [Version]            rowversion     NOT NULL,

    CONSTRAINT [PK_MetricDefinition]
        PRIMARY KEY CLUSTERED ([MetricKey]),
    CONSTRAINT [UX_MetricDefinition_MetricCode]
        UNIQUE ([MetricCode]),
    CONSTRAINT [CK_MetricDefinition_MappingVersion]
        CHECK ([MappingVersion] > 0)
);
```

Initial metric groups:

```text
activity
connection
control
sensor
scanner
business
error
quality
```

Initial metric candidates, chỉ enable sau khi mapping evidence/test đã khóa:

| MetricCode | Ý nghĩa |
|---|---|
| `tag_read` | tag read được chọn theo source ownership policy |
| `business_process` | business/process raw event |
| `device_online_observed` | device online observation |
| `device_connected` | device chuyển/được quan sát connected |
| `device_disconnected` | device chuyển/được quan sát disconnected |
| `scanner_connected` | scanner connect |
| `scanner_disconnected` | scanner disconnect |
| `green_light_on` / `green_light_off` | observed green control state |
| `red_light_on` / `red_light_off` | observed red control state |
| `sensor_state_observed` | sensor state event chưa đủ discriminator sâu hơn |
| `device_error` | confirmed device error |
| `snapshot_observed` | snapshot, không mặc định tính như activity transition |

Không dùng `source.eventName` trực tiếp làm `MetricCode`. Metric code phải ổn định khi transport/callback name thay đổi.

## 9. `DeviceEventDaily`

Fact table đếm metric của từng thiết bị theo ngày.

Grain:

```text
one row per
ProjectionVersion + CompanyId + DeviceId + StatisticsDate + MetricKey + SourceKind
```

Giữ `SourceKind` trong grain để không cộng chéo raw-log/AppHub khi chưa có cross-source identity.

```sql
CREATE TABLE [dbo].[DES.DeviceEventDaily]
(
    [ProjectionVersion]       int            NOT NULL,
    [CompanyId]               bigint         NOT NULL,
    [DeviceId]                bigint         NOT NULL,
    [StatisticsDate]          date           NOT NULL,
    [MetricKey]               int            NOT NULL,
    [SourceKind]              varchar(64)    NOT NULL,
    [EventCount]              bigint         NOT NULL,
    [ParsedWithWarningsCount] bigint         NOT NULL,
    [OccurredTimeBasisCount]  bigint         NOT NULL,
    [ReceivedTimeBasisCount]  bigint         NOT NULL,
    [FirstEventAtUtc]         datetime2(7)   NOT NULL,
    [LastEventAtUtc]          datetime2(7)   NOT NULL,
    [LastSourcePersistedAtUtc] datetime2(7)  NOT NULL,
    [CreatedAtUtc]            datetime2(7)   NOT NULL,
    [UpdatedAtUtc]            datetime2(7)   NOT NULL,
    [Version]                 rowversion     NOT NULL,

    CONSTRAINT [PK_DeviceEventDaily]
        PRIMARY KEY CLUSTERED
        ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate], [MetricKey], [SourceKind]),
    CONSTRAINT [CK_DeviceEventDaily_PositiveProjectionVersion]
        CHECK ([ProjectionVersion] > 0),
    CONSTRAINT [CK_DeviceEventDaily_PositiveCompany]
        CHECK ([CompanyId] > 0),
    CONSTRAINT [CK_DeviceEventDaily_PositiveDevice]
        CHECK ([DeviceId] > 0),
    CONSTRAINT [CK_DeviceEventDaily_NonNegativeCounts]
        CHECK
        (
            [EventCount] >= 0 AND
            [ParsedWithWarningsCount] >= 0 AND
            [OccurredTimeBasisCount] >= 0 AND
            [ReceivedTimeBasisCount] >= 0
        ),
    CONSTRAINT [CK_DeviceEventDaily_TimeBasisTotal]
        CHECK ([OccurredTimeBasisCount] + [ReceivedTimeBasisCount] = [EventCount]),
    CONSTRAINT [CK_DeviceEventDaily_EventRange]
        CHECK ([FirstEventAtUtc] <= [LastEventAtUtc])
);
```

Recommended indexes:

```text
IX_DeviceEventDaily_Company_Date_Metric
    (ProjectionVersion, CompanyId, StatisticsDate, MetricKey)
    INCLUDE (DeviceId, SourceKind, EventCount, FirstEventAtUtc, LastEventAtUtc)

IX_DeviceEventDaily_Company_Metric_Date
    (ProjectionVersion, CompanyId, MetricKey, StatisticsDate)
    INCLUDE (DeviceId, SourceKind, EventCount)

IX_DeviceEventDaily_Date
    (ProjectionVersion, StatisticsDate)
    INCLUDE (CompanyId, DeviceId, MetricKey, SourceKind, EventCount)
```

Semantics:

- `EventCount` chỉ tăng cho event mới đã qua idempotency gate.
- Mỗi metric occurrence đóng góp đúng `1`; không dùng business quantity hoặc sensor numeric value làm `EventCount`.
- `FirstEventAtUtc` là minimum `timelineAtUtc`.
- `LastEventAtUtc` là maximum `timelineAtUtc`.
- `LastSourcePersistedAtUtc` phục vụ freshness/diagnostic, không phải business timeline.
- `parsed_with_warnings` vẫn có thể được thống kê nếu metric core facts hợp lệ.
- `unmapped` không đi vào normal device metric; nó đi `IngestionQualityDaily`.
- Week/month query bằng `SUM(EventCount)` trên daily rows.

## 10. `DeviceDailySnapshot`

Một row tổng hợp trạng thái, duration và health của một device/day.

```sql
CREATE TABLE [dbo].[DES.DeviceDailySnapshot]
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
    [ConnectedEventCount]      bigint         NOT NULL,
    [DisconnectedEventCount]   bigint         NOT NULL,
    [ReconnectCount]           bigint         NOT NULL,

    [TotalEventCount]          bigint         NOT NULL,
    [ErrorEventCount]          bigint         NOT NULL,
    [WarningEventCount]        bigint         NOT NULL,
    [FirstEventAtUtc]          datetime2(7)   NULL,
    [LastEventAtUtc]           datetime2(7)   NULL,

    [HealthStatus]             varchar(32)    NULL,
    [HealthScore]              decimal(5,2)   NULL,
    [HealthRuleVersion]        int            NULL,
    [HealthReasonJson]         nvarchar(max)  NULL,
    [IsFinalized]              bit            NOT NULL,
    [CalculatedAtUtc]          datetime2(7)   NOT NULL,
    [CreatedAtUtc]             datetime2(7)   NOT NULL,
    [UpdatedAtUtc]             datetime2(7)   NOT NULL,
    [Version]                  rowversion     NOT NULL,

    CONSTRAINT [PK_DeviceDailySnapshot]
        PRIMARY KEY CLUSTERED
        ([ProjectionVersion], [CompanyId], [DeviceId], [StatisticsDate]),
    CONSTRAINT [CK_DeviceDailySnapshot_Bucket]
        CHECK ([BucketStartAtUtc] < [BucketEndAtUtc]),
    CONSTRAINT [CK_DeviceDailySnapshot_Status]
        CHECK
        (
            [OpeningConnectionStatus] IN ('connected', 'disconnected', 'unknown') AND
            [ClosingConnectionStatus] IN ('connected', 'disconnected', 'unknown')
        ),
    CONSTRAINT [CK_DeviceDailySnapshot_Durations]
        CHECK ([OnlineSeconds] >= 0 AND [OfflineSeconds] >= 0 AND [UnknownSeconds] >= 0),
    CONSTRAINT [CK_DeviceDailySnapshot_DurationTotal]
        CHECK
        (
            [OnlineSeconds] + [OfflineSeconds] + [UnknownSeconds]
            = DATEDIFF_BIG(SECOND, [BucketStartAtUtc], [BucketEndAtUtc])
        ),
    CONSTRAINT [CK_DeviceDailySnapshot_Counts]
        CHECK
        (
            [ConnectedEventCount] >= 0 AND
            [DisconnectedEventCount] >= 0 AND
            [ReconnectCount] >= 0 AND
            [TotalEventCount] >= 0 AND
            [ErrorEventCount] >= 0 AND
            [WarningEventCount] >= 0
        ),
    CONSTRAINT [CK_DeviceDailySnapshot_HealthScore]
        CHECK ([HealthScore] IS NULL OR ([HealthScore] >= 0 AND [HealthScore] <= 100)),
    CONSTRAINT [CK_DeviceDailySnapshot_HealthContract]
        CHECK
        (
            ([HealthScore] IS NULL AND [HealthRuleVersion] IS NULL) OR
            ([HealthScore] IS NOT NULL AND [HealthRuleVersion] IS NOT NULL)
        ),
    CONSTRAINT [CK_DeviceDailySnapshot_HealthReasonJson]
        CHECK ([HealthReasonJson] IS NULL OR ISJSON([HealthReasonJson]) = 1)
);
```

`BucketEndAtUtc` là exclusive. Duration invariant mục tiêu:

```text
OnlineSeconds + OfflineSeconds + UnknownSeconds
    = duration(BucketStartAtUtc, BucketEndAtUtc)
```

Do daylight-saving timezone có thể tạo ngày không đúng 86.400 giây, không hard-code tổng duration bằng 86.400.

`IsFinalized=true` chỉ nghĩa là scheduled reconciliation/finalization đã chạy; late event vẫn có thể reopen/recalculate row. Không dùng finalization để bỏ qua late data.

`HealthReasonJson` chỉ lưu reason codes và numeric evidence, ví dụ:

```json
[
  { "code": "HIGH_RECONNECT_RATE", "value": 12, "threshold": 5 },
  { "code": "OFFLINE_RATIO", "value": 0.31, "threshold": 0.20 }
]
```

Không lưu raw payload, username, token, IP, session hoặc connection ID.

Recommended indexes:

```text
IX_DeviceDailySnapshot_Company_Date_Health
    (ProjectionVersion, CompanyId, StatisticsDate, HealthStatus)
    INCLUDE (DeviceId, HealthScore, OnlineSeconds, OfflineSeconds, LastEventAtUtc)

IX_DeviceDailySnapshot_Company_Health_Date
    (ProjectionVersion, CompanyId, HealthStatus, StatisticsDate)
    INCLUDE (DeviceId, HealthScore)
```

## 11. `DeviceStateCursor`

Giữ state cuối cùng để tính duration qua batch và qua midnight. Đây là operational projection state, không phải report fact.

```sql
CREATE TABLE [dbo].[DES.DeviceStateCursor]
(
    [ProjectionVersion]   int            NOT NULL,
    [CompanyId]           bigint         NOT NULL,
    [DeviceId]            bigint         NOT NULL,
    [StateType]           varchar(64)    NOT NULL,
    [CurrentState]        varchar(64)    NOT NULL,
    [StateSinceAtUtc]     datetime2(7)   NOT NULL,
    [LastTimelineAtUtc]   datetime2(7)   NOT NULL,
    [LastEventId]         binary(32)     NOT NULL,
    [UpdatedAtUtc]        datetime2(7)   NOT NULL,
    [Version]             rowversion     NOT NULL,

    CONSTRAINT [PK_DeviceStateCursor]
        PRIMARY KEY CLUSTERED
        ([ProjectionVersion], [CompanyId], [DeviceId], [StateType]),
    CONSTRAINT [CK_DeviceStateCursor_EventOrder]
        CHECK ([StateSinceAtUtc] <= [LastTimelineAtUtc])
);
```

Initial `StateType`:

```text
device_connection
scanner_connection
```

State transition processor phải split interval tại business-day boundary và cập nhật đúng `DeviceDailySnapshot` của từng ngày bị cắt qua.

## 12. `ProcessedEvent`

Transactional inbox chống cộng trùng khi retry, crash hoặc overlap reconciliation/incremental read.

```sql
CREATE TABLE [dbo].[DES.ProcessedEvent]
(
    [ProcessedEventKey]   bigint         IDENTITY(1,1) NOT NULL,
    [ProjectionName]      varchar(100)   NOT NULL,
    [ProjectionVersion]   int            NOT NULL,
    [EventId]             binary(32)     NOT NULL,
    [SourceKind]          varchar(64)    NOT NULL,
    [SourcePersistedAtUtc] datetime2(7)  NOT NULL,
    [StatisticsDate]      date           NULL,
    [Outcome]             varchar(32)    NOT NULL,
    [ProcessedAtUtc]      datetime2(7)   NOT NULL,

    CONSTRAINT [PK_ProcessedEvent]
        PRIMARY KEY CLUSTERED ([ProcessedEventKey]),
    CONSTRAINT [CK_ProcessedEvent_ProjectionVersion]
        CHECK ([ProjectionVersion] > 0),
    CONSTRAINT [CK_ProcessedEvent_Outcome]
        CHECK ([Outcome] IN ('aggregated', 'ignored', 'failed_terminal'))
);

CREATE UNIQUE INDEX [UX_ProcessedEvent_Projection_Event]
    ON [dbo].[DES.ProcessedEvent]
       ([ProjectionName], [ProjectionVersion], [EventId]);

CREATE INDEX [IX_ProcessedEvent_ProcessedAtUtc]
    ON [dbo].[DES.ProcessedEvent] ([ProcessedAtUtc]);
```

Rules:

- Insert unique `EventId` và aggregate update nằm trong cùng SQL transaction.
- Unique conflict nghĩa là idempotent duplicate: không cộng lại daily rows.
- `ignored` dùng cho event hợp lệ nhưng ngoài ownership/metric scope.
- `failed_terminal` chỉ dùng sau khi đã ghi `ProjectionFailure` terminal trong cùng transaction.
- Không xóa processed rows thuộc active projection version nếu vẫn có khả năng replay vào cùng aggregates.
- Khi rebuild toàn bộ, dùng projection version mới hoặc truncate cả facts + processed-event của version mục tiêu trước khi chạy.

## 13. `ProjectionCheckpoint`

Cursor và optional lease của incremental projector. Nó hoàn toàn độc lập với Mongo `ingestion_checkpoints` của raw-log.

```sql
CREATE TABLE [dbo].[DES.ProjectionCheckpoint]
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

    CONSTRAINT [PK_ProjectionCheckpoint]
        PRIMARY KEY CLUSTERED
        ([ProjectionName], [ProjectionVersion], [PartitionKey]),
    CONSTRAINT [CK_ProjectionCheckpoint_CursorPair]
        CHECK
        (
            ([LastPersistedAtUtc] IS NULL AND [LastEventId] IS NULL) OR
            ([LastPersistedAtUtc] IS NOT NULL AND [LastEventId] IS NOT NULL)
        ),
    CONSTRAINT [CK_ProjectionCheckpoint_BatchSize]
        CHECK ([LastBatchSize] >= 0)
);
```

Default row:

```text
ProjectionName    = device_event_daily
ProjectionVersion = 1
PartitionKey      = device_event_history
```

Cursor order:

```text
persistedAtUtc ASC, eventId ASC
```

Mongo query phải dùng cùng binary/ordinal semantics của canonical lowercase hex `eventId`. Không đổi normalization/collation giữa các lần chạy.

Checkpoint chỉ advance trong cùng SQL transaction đã xác nhận toàn bộ outcomes của contiguous batch. SQL/Mongo transient failure không advance checkpoint.

Sprint 3 chạy một active incremental projector. Lease bảo vệ deployment overlap; nó không biến design thành active-active partitioned projector. Cột `LeaseEpoch` đóng vai trò Fencing Token: mọi transaction ghi dữ liệu (Incremental hoặc Reconcile) bắt buộc kiểm tra `LeaseOwner`, `LeaseExpiresAtUtc` và `LeaseEpoch` để loại bỏ rủi ro zombie/split-brain worker ghi đè dữ liệu.

## 14. `ReconciliationRequest`

Hàng đợi bền vững lưu trữ các yêu cầu tính toán lại (Reconciliation) khi phát sinh sự kiện chuyển trạng thái đến muộn (out-of-order) hoặc có thay đổi cấu hình múi giờ/metadata.

```sql
CREATE TABLE [dbo].[DES.ReconciliationRequest]
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

    CONSTRAINT [PK_ReconciliationRequest]
        PRIMARY KEY CLUSTERED ([RequestId]),
    CONSTRAINT [CK_ReconciliationRequest_Dates]
        CHECK ([FromStatisticsDate] <= [ToStatisticsDate]),
    CONSTRAINT [CK_ReconciliationRequest_Status]
        CHECK ([Status] IN ('Pending', 'Processing', 'Completed', 'Failed', 'Cancelled')),
    CONSTRAINT [CK_ReconciliationRequest_Attempts]
        CHECK ([AttemptCount] >= 0)
);
```

Recommended indexes:

```text
IX_ReconciliationRequest_Status_Requested
    (ProjectionName, ProjectionVersion, Status, RequestedAtUtc)
    INCLUDE (CompanyId, DeviceId, StateType, FromStatisticsDate, ToStatisticsDate, AttemptCount)
```

## 15. `ProjectionFailure`

Lưu event không thể aggregate do statistics contract, không copy raw payload.

```sql
CREATE TABLE [dbo].[DES.ProjectionFailure]
(
    [ProjectionFailureKey] bigint         IDENTITY(1,1) NOT NULL,
    [FailureId]            binary(32)     NOT NULL,
    [ProjectionName]       varchar(100)   NOT NULL,
    [ProjectionVersion]    int            NOT NULL,
    [EventId]              binary(32)     NULL,
    [SourceEventIdentity]  varchar(256)   NOT NULL,
    [CompanyId]            bigint         NULL,
    [DeviceId]             bigint         NULL,
    [SourceKind]           varchar(64)    NULL,
    [Category]             varchar(64)    NULL,
    [SourceEventName]      varchar(128)   NULL,
    [SourcePersistedAtUtc] datetime2(7)   NULL,
    [ErrorCode]            varchar(100)   NOT NULL,
    [ErrorStage]           varchar(64)    NOT NULL,
    [ErrorMessage]         nvarchar(1000) NOT NULL,
    [Retryable]            bit            NOT NULL,
    [RetryCount]           int            NOT NULL,
    [FirstFailedAtUtc]     datetime2(7)   NOT NULL,
    [LastFailedAtUtc]      datetime2(7)   NOT NULL,
    [ResolvedAtUtc]        datetime2(7)   NULL,
    [Resolution]           nvarchar(500)  NULL,
    [Version]              rowversion     NOT NULL,

    CONSTRAINT [PK_ProjectionFailure]
        PRIMARY KEY CLUSTERED ([ProjectionFailureKey]),
    CONSTRAINT [UX_ProjectionFailure_FailureId]
        UNIQUE ([ProjectionName], [ProjectionVersion], [FailureId]),
    CONSTRAINT [CK_ProjectionFailure_RetryCount]
        CHECK ([RetryCount] >= 0),
    CONSTRAINT [CK_ProjectionFailure_Time]
        CHECK ([FirstFailedAtUtc] <= [LastFailedAtUtc])
);
```

Suggested error codes:

```text
STAT_TENANT_REQUIRED
STAT_DEVICE_REQUIRED
STAT_TIMELINE_REQUIRED
STAT_TIMEZONE_UNRESOLVED
STAT_METRIC_UNMAPPED
STAT_EVENT_ID_INVALID
STAT_OUT_OF_ORDER_STATE_EVENT
STAT_HEALTH_RULE_FAILED
STAT_SCHEMA_UNSUPPORTED
```

Data/contract failure có thể ghi terminal failure rồi advance projection checkpoint. Infrastructure failure như SQL unavailable, Mongo timeout hoặc transaction conflict không ghi thành data failure và không advance checkpoint.

`FailureId` được tạo deterministic từ projection/version, Mongo source document identity, available event identity và error code. `EventId` có thể null cho corrupted/unsupported source document không có valid SHA-256 event identity; `SourceEventIdentity` vẫn phải đủ để trace về MongoDB mà không lưu raw payload.

## 15. `ProjectionRun`

Audit một incremental, reconciliation hoặc backfill run.

```sql
CREATE TABLE [dbo].[DES.ProjectionRun]
(
    [ProjectionRunId]      INT IDENTITY(1, 1) PRIMARY KEY,
    [RunId]                uniqueidentifier NULL,
    [ProjectionName]       varchar(100)     NULL,
    [ProjectionVersion]    int              NULL,
    [RunType]              varchar(32)      NULL,
    [RequestedFromDate]    date             NULL,
    [RequestedToDate]      date             NULL,
    [RequestedCompanyId]   bigint           NULL,
    [StartedAtUtc]         datetime2(7)     NULL,
    [CompletedAtUtc]       datetime2(7)     NULL,
    [Status]               varchar(32)      NULL,
    [ReadEventCount]       bigint           NULL,
    [AggregatedEventCount] bigint           NULL,
    [DuplicateEventCount]  bigint           NULL,
    [IgnoredEventCount]    bigint           NULL,
    [FailureEventCount]    bigint           NULL,
    [AffectedRowCount]     bigint           NULL,
    [ErrorSummary]         nvarchar(2000)   NULL
);
```

Index:

```text
IX_ProjectionRun_Name_StartedAtUtc
    (ProjectionName, ProjectionVersion, StartedAtUtc DESC)
```

## 16. `IngestionQualityDaily`

Thống kê chất lượng source/history độc lập với device health. Một parse failure không có `DeviceId` không được ép vào device snapshot.

```sql
CREATE TABLE [dbo].[DES.IngestionQualityDaily]
(
    [ProjectionVersion] int            NOT NULL,
    [StatisticsDate]    date           NOT NULL,
    [CompanyId]         bigint         NOT NULL,
    [SourceKind]        varchar(64)    NOT NULL,
    [SourceId]          varchar(200)   NOT NULL,
    [QualityCode]       varchar(100)   NOT NULL,
    [EventCount]        bigint         NOT NULL,
    [FirstSeenAtUtc]    datetime2(7)   NOT NULL,
    [LastSeenAtUtc]     datetime2(7)   NOT NULL,
    [UpdatedAtUtc]      datetime2(7)   NOT NULL,

    CONSTRAINT [PK_IngestionQualityDaily]
        PRIMARY KEY CLUSTERED
        ([ProjectionVersion], [StatisticsDate], [CompanyId], [SourceKind], [SourceId], [QualityCode]),
    CONSTRAINT [CK_IngestionQualityDaily_Count]
        CHECK ([EventCount] >= 0)
);
```

`CompanyId=0` chỉ được phép trong bảng quality để biểu diễn unresolved/global source quality. Normal device fact/snapshot luôn yêu cầu positive company ID.

Quality `StatisticsDate` dùng business statistics date khi resolve được; nếu source event thiếu trusted timeline/timezone thì dùng UTC date của `persistedAtUtc` và quality code phải thể hiện degraded time basis. Quy tắc fallback này chỉ áp dụng quality table, không áp dụng normal device fact/snapshot.

Initial `QualityCode`:

```text
parsed_with_warnings
unmapped
received_time_basis
unsupported_schema
projection_failure
```

Mongo `ingestion_failures` projection có thể được bổ sung sau; Sprint 3 MVP ưu tiên history quality có thể xác định từ `device_event_history`.

## 17. Event eligibility và aggregation contract

Một history event được đưa vào normal device statistics khi:

```text
companyId > 0
device.id > 0
timelineAtUtc hợp lệ
sourceKind/category hợp lệ
parse.status in (parsed, parsed_with_warnings)
metric mapping tồn tại và enabled
event family thuộc source ownership policy
```

Outcomes:

| Condition | Outcome |
|---|---|
| Eligible + chưa processed | `aggregated` |
| Eligible + processed-event đã tồn tại | idempotent duplicate, không cộng |
| Hợp lệ nhưng ngoài statistics scope/ownership | `ignored` |
| `unmapped` hoặc chỉ có quality meaning | quality aggregate, không device metric |
| Thiếu tenant/device/timezone hoặc schema không hỗ trợ | projection failure theo policy |
| SQL/Mongo transient error | retry, không checkpoint |

Snapshot/reconnect snapshot không mặc định tạo connection transition. Chỉ confirmed activity/state event mới thay đổi duration state machine.

## 18. Source ownership và double-count contract

Do raw-log và AppHub chưa có shared producer event ID, schema không tự deduplicate physical event giữa hai source.

Rules:

1. `SourceKind` luôn nằm trong daily event grain.
2. Mapping code phải có ownership registry theo event family.
3. Một metric chỉ có một primary source khi dùng cho global health/count.
4. Secondary/shadow source có thể lưu metric riêng nhưng API không tự SUM chéo.
5. Không deduplicate bằng `DeviceId + category + timelineAtUtc` hoặc payload similarity.

Initial ownership proposal:

| Event family | Primary statistics source |
|---|---|
| raw tag/business facts | `rfid_antenna_file` |
| device connection/control/sensor | `erp_apphub` |
| scanner lifecycle/snapshot | `erp_apphub` |
| unknown/unmapped | quality only |

Ownership table trên phải được xác nhận bằng Sprint 2 UAT payload trước khi production-enable metric tương ứng.

## 19. Timezone, daily bucket và late event

Daily bucket algorithm:

```text
event.timelineAtUtc
    -> resolve DeviceDimension.TimeZoneId
    -> convert UTC to local business time
    -> StatisticsDate = local calendar date
    -> calculate BucketStartAtUtc/BucketEndAtUtc from timezone rules
```

Timezone resolution priority:

```text
authoritative device/site mapping
    -> configured source timezone
    -> no global machine-local fallback
```

Nếu timezone không resolve được, tạo `STAT_TIMEZONE_UNRESOLVED`; không dùng timezone của SQL Server hoặc Worker machine.

Late event:

- được cộng vào `StatisticsDate` quá khứ;
- cập nhật first/last time và snapshot liên quan;
- có thể làm `IsFinalized` trở lại false cho tới reconciliation kế tiếp;
- không thay projection checkpoint về quá khứ vì cursor theo `persistedAtUtc`, không theo event timeline.

## 20. State duration contract

Connection duration không thể suy ra chỉ từ event count. State processor dùng ordered transitions:

```text
(CompanyId, DeviceId, StateType)
    order by TimelineAtUtc ASC, EventId ASC
```

Rules:

- Duplicate cùng `EventId` bị loại trước state transition.
- Repeated `connected -> connected` tăng event count nhưng không mở interval mới.
- `connected -> disconnected` đóng online interval.
- `disconnected -> connected` đóng offline interval và tăng reconnect khi rule thỏa.
- Không biết opening state thì thời gian trước observation đầu tiên là `unknown`.
- Interval qua midnight được split theo business timezone.
- Out-of-order event trong incremental path phải đánh dấu day/device cần reconciliation; không âm thầm tạo negative duration.
- Reconciliation recompute toàn bộ ordered events của affected device/day và replace snapshot atomically.

## 21. Health contract

Health là derived output, không phải raw fact.

Initial health inputs có thể gồm:

- online/offline/unknown ratio;
- connect/disconnect/reconnect count;
- confirmed device error count;
- sensor/control anomaly count;
- last-event freshness;
- parsed-with-warning/received-time-basis ratio;
- expected operating schedule nếu có authoritative configuration.

Không coi “không có event” là unhealthy nếu chưa biết device có được kỳ vọng hoạt động hay không.

`HealthStatus` registry đề xuất:

```text
healthy
degraded
unhealthy
unknown
```

Mọi non-null health result phải lưu `HealthRuleVersion` và reason codes. Thay đổi rule không rewrite âm thầm kết quả cũ; dùng recompute/rebuild có run audit.

Sprint 3 có thể triển khai event daily + state snapshot trước, health evaluator sau trong cùng sprint plan. Schema cho health được chuẩn bị ngay nhưng không bắt buộc giả score khi rule chưa khóa.

## 22. Transaction và upsert protocol

Cho một contiguous incremental batch:

```text
BEGIN TRANSACTION
    for each event in source order
        try insert ProcessedEvent khi eventId hợp lệ
        if duplicate
            mark duplicate outcome; do not aggregate
        else
            map event -> zero/one/many metric contributions
            aggregate DeviceEventDaily
            apply ordered state transition when eligible
            write terminal ProjectionFailure when required

    update DeviceDailySnapshot summaries
    advance ProjectionCheckpoint to last source event in batch
COMMIT
```

Nếu event identity không hợp lệ, deterministic `ProjectionFailure.FailureId` là idempotency gate thay cho `ProcessedEvent.EventId`. Nếu transaction rollback, processed-event/failure, aggregate, state cursor và checkpoint cùng rollback. Retry đọc lại batch và không mất event.

Không dùng distributed transaction giữa MongoDB và SQL Server. Correctness đến từ Mongo source replay + SQL idempotency + SQL-local transaction.

## 23. Reconciliation và rebuild semantics

### 23.1. Incremental

- cộng contribution mới;
- dùng `ProcessedEvent` chống duplicate;
- advance checkpoint theo persisted cursor.

### 23.2. Rolling reconciliation

- đọc lại toàn bộ source events của target company/device/date range;
- tính expected daily facts/snapshot từ đầu;
- ghi vào staging/in-memory aggregate;
- replace target rows trong một SQL transaction;
- không cộng lại qua normal incremental path;
- ghi `ProjectionRun` với `RunType=reconciliation`.

### 23.3. Full rebuild

Hai cách hợp lệ:

1. Tạo `ProjectionVersion` mới, build song song rồi chuyển read configuration sang version mới.
2. Với môi trường non-production/maintenance window, truncate toàn bộ rows của version mục tiêu rồi build lại.

Production ưu tiên version mới để rollback được. Không rebuild bằng cách reset checkpoint rồi cộng vào aggregate cũ.

## 24. Query examples

### Một device trong một ngày

```sql
SELECT
    d.StatisticsDate,
    m.MetricCode,
    d.SourceKind,
    d.EventCount,
    d.FirstEventAtUtc,
    d.LastEventAtUtc
FROM [dbo].[DES.DeviceEventDaily] d
JOIN [dbo].[DES.MetricDefinition] m
  ON m.MetricKey = d.MetricKey
WHERE d.ProjectionVersion = @ProjectionVersion
  AND d.CompanyId = @CompanyId
  AND d.DeviceId = @DeviceId
  AND d.StatisticsDate = @StatisticsDate;
```

### Trend theo tuần/tháng

```sql
SELECT
    d.StatisticsDate,
    m.MetricCode,
    SUM(d.EventCount) AS EventCount
FROM [dbo].[DES.DeviceEventDaily] d
JOIN [dbo].[DES.MetricDefinition] m
  ON m.MetricKey = d.MetricKey
WHERE d.ProjectionVersion = @ProjectionVersion
  AND d.CompanyId = @CompanyId
  AND d.DeviceId = @DeviceId
  AND d.StatisticsDate >= @FromDate
  AND d.StatisticsDate < @ToDate
GROUP BY d.StatisticsDate, m.MetricCode
ORDER BY d.StatisticsDate, m.MetricCode;
```

API/query layer phải dùng ownership-aware metric selection; không `SUM` cùng metric qua mọi `SourceKind` nếu metric chưa được xác nhận cross-source-safe.

## 25. Retention và capacity

Với 100.000 history event/ngày:

- raw events không được duplicate sang SQL;
- `DeviceEventDaily` nhỏ hơn raw history nhiều lần vì được group theo device/day/metric/source;
- `ProcessedEvent` có thể tăng khoảng 36,5 triệu rows/năm nếu mọi event đều giữ;
- `ProjectionFailure` và `ProjectionRun` nhỏ hơn đáng kể trong normal operation.

Initial retention proposal:

| Table | Retention |
|---|---|
| `DeviceEventDaily` | theo reporting requirement, mặc định nhiều năm |
| `DeviceDailySnapshot` | theo reporting requirement, mặc định nhiều năm |
| `DeviceStateCursor` | giữ current active projection state |
| `ProcessedEvent` | không purge active version trước khi replay horizon được khóa |
| `ProjectionCheckpoint` | không TTL |
| `ProjectionFailure` | tối thiểu bằng investigation/rebuild window |
| `ProjectionRun` | 90–180 ngày hoặc theo operations policy |
| `IngestionQualityDaily` | theo reporting requirement |

Chỉ bổ sung monthly partitioning/columnstore sau khi có volume, query plan và retention benchmark thực tế.

## 26. Security và privacy

- SQL credential lấy từ secret/environment provider.
- Statistics Worker có Mongo read-only trên history và SQL read/write chỉ trong `dbo`.
- Report/API account chỉ có SELECT trên approved views/tables.
- Không lưu raw payload, token, JWT, connection string, session, IP, avatar hoặc raw connection ID.
- Không log SQL connection string hoặc full Mongo event.
- `HealthReasonJson` chỉ chứa rule code và numeric evidence đã duyệt.
- Không tạo cross-database FK hoặc trigger viết ngược ERP tables.

## 27. Migration và deployment rules

- Mọi DDL được quản lý bằng versioned migration trong repository.
- Production migration không tự drop table/index.
- Seed `MetricDefinition` idempotently bằng `MetricCode`.
- Schema migration chạy trước khi Statistics Worker bắt đầu projection.
- Worker fail fast/not-ready khi schema version không tương thích.
- SQL Server objects của Hangfire hiện có không được sửa hoặc tái sử dụng cho Sprint 3.
- Nếu dùng chung database Report, load test phải xác nhận index/build/reconciliation không ảnh hưởng report workload.

## 28. Tests bắt buộc cho schema

### Contract/unit

- eventId hex -> `binary(32)` round-trip;
- daily bucket theo timezone;
- DST/non-86.400-second day;
- metric mapping và source ownership;
- eligibility parsed/warning/unmapped;
- health reason/version invariant;
- state transition và midnight splitting.

### SQL integration

- migrations idempotent;
- unique processed event;
- duplicate retry không tăng count;
- aggregate + processed-event + checkpoint commit atomically;
- transaction rollback không advance checkpoint;
- concurrent writer/lease conflict;
- late event cập nhật past day;
- reconciliation replace không double count;
- projection version coexistence;
- exact SQL types/check constraints/indexes;
- representative week/month query plan.

### Capacity

- ingest ít nhất 100.000 events/day equivalent fixture;
- burst test cao hơn average rate;
- catch-up 1–3 ngày backlog;
- reconciliation một ngày dữ liệu;
- đo SQL transaction duration, lock/deadlock, log growth và report query latency.

## 29. Definition of Done Schema Sprint 3

- Statistics objects dùng schema mặc định `dbo` trong database Report ERP.
- Daily event fact có grain/key rõ và không dùng wide event columns.
- Daily snapshot tách khỏi event counts.
- UTC/date/timezone contract rõ.
- Late-event/reconciliation/rebuild semantics rõ.
- Raw-log/AppHub ownership không gây cộng chéo mặc định.
- Processed-event và SQL transaction chứng minh idempotency.
- Projection checkpoint độc lập ingestion checkpoint.
- Projection failure không chứa raw/secret.
- State duration xử lý repeated/out-of-order/midnight events.
- Health result có rule version và evidence.
- Index phục vụ device/day và tenant/date charts.
- Migration, security, retention và capacity tests được khóa.
- MongoDB vẫn là source of truth; SQL projection rebuild được.

## 30. Các quyết định phải xác nhận trước implementation

- Database riêng hay schema `dbo` trong ERP Report database.
- Authoritative source cho device/site timezone và display metadata.
- Initial metric registry và event ownership sau Sprint 2 UAT.
- Health rule V1, expected operating schedule và thresholds.
- Active projection version selection mechanism của future API.
- `ProcessedEvent` retention theo Mongo history/replay policy.
- Backlog recovery target và acceptable statistics lag.
- Report workload window dành cho reconciliation/rebuild.
