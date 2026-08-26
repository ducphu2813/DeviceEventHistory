# Device Event History - Architecture Phase 1 / Sprint 1

## 1. Trạng thái tài liệu

- Trạng thái: kiến trúc đối chiếu với implementation hiện tại ngày 2026-08-26.
- Phạm vi: Worker đọc raw-log do `RFID.Antenna` sinh ra và lưu MongoDB.
- Nền tảng mục tiêu: .NET 10 Worker Service và MongoDB.
- Schema chuẩn: `2026-08-22-Db-Schema.md`.
- Thiết kế tổng thể hai luồng: `Device-Event-History-Design.md`.
- Chiến thuật tail nhiều file: `Logs-Reading-Strategy.md`.
- Luồng AppHub/SignalR là Phase 1 - Sprint 2, không phải dependency của Sprint 1.

Implementation hiện tại đã có vertical slice raw-log hoàn chỉnh trong checkout
`D:\texpo\logging-worker\device-event-worker`: configuration validation,
local/remote HTTP discovery, tail/framing, parser/canonical mapping, MongoDB
history/failure/checkpoint, fair scheduling, recovery và observability cơ bản.
Các tên class/file trong các blueprint cũ bên dưới chỉ còn là khái niệm; tên
thực tế phải tra theo `Device-Event-History-Current-Codebase.md`.

Tài liệu này là source of truth cho:

- boundary và project structure của Sprint 1;
- cách phát hiện, đọc và đóng khung record từ raw file;
- cách parse, persist và commit checkpoint;
- reliability, observability, testing và deployment của File Worker.

Canonical MongoDB field, index và document contract tiếp tục lấy từ schema chuẩn. Khi có khác biệt, schema chuẩn quyết định document shape; tài liệu này quyết định processing flow và source-code organization.

Sprint 1 đã bổ sung `sourceId` vào source context và checkpoint key để phân biệt nhiều
Antenna installation có cùng `FileId` và ngày. Mongo mapper hiện tại lưu
`source.sourceId`, `source.folderDate`, `source.fileId`, `source.relativePath` và
offsets theo schema V1; mọi thay đổi document shape vẫn phải cập nhật schema chuẩn
trước khi sửa mapper.

## 2. Mục tiêu Sprint 1

Sprint 1 phải tạo được một vertical slice chạy end-to-end:

```text
RFID.Antenna
    -> {FolderRawData}/yyyy/MM/dd/File_{FileId}.txt
    -> DeviceEventHistory.Worker
    -> device_event_history hoặc ingestion_failures
    -> ingestion_checkpoints
```

Sau khi hoàn thành:

- Worker phát hiện được file raw-log mới và file đang được append.
- Worker chỉ xử lý record hoàn chỉnh kết thúc bằng `e(0)`.
- Worker giữ raw payload và parse các facts đã được xác nhận.
- Worker tạo `eventId` ổn định để retry không tạo duplicate history.
- Worker ghi event hợp lệ vào `device_event_history`.
- Worker ghi record hoàn chỉnh nhưng không parse được vào `ingestion_failures`.
- Worker chỉ cập nhật checkpoint sau khi MongoDB xác nhận persistence.
- Worker restart và đọc lại an toàn từ checkpoint.
- Có log, metrics và health state cho file access, ingestion lag, parse và persistence.
- Có test chứng minh append, partial record, restart, duplicate và MongoDB failure.

## 3. Ngoài phạm vi Sprint 1

- Kết nối ERP AppHub hoặc bất kỳ SignalR Hub nào.
- `JoinMonitoring`, reconnect/rejoin hoặc callback `receive*`.
- MVC Web, dashboard, event list và device timeline UI.
- `device_current_state`, `tag_current_state`, connection session hoặc production projection.
- Business deduplication theo tag/EPC/device.
- Multi-worker active-active, leader election hoặc distributed lease.
- Message broker hoặc durable inbox riêng.
- Thay đổi source `RFID.Antenna`, `RFID.Analytics` hoặc ERP.
- Thay thế business processing hiện có của `RFID.Analytics`.
- Tự động tải file qua HTTP nếu topology chưa xác nhận cần `RFID.Downloader`.
- Cam kết physical-device end-to-end nếu mới chỉ test bằng fixture/simulator.

Các boundary trên giúp Sprint 1 tập trung chứng minh một đường bền vững và quan sát được: raw file -> MongoDB.

## 4. Bằng chứng source raw-log hiện tại

Source `RFID.Antenna` hiện thực hiện:

1. `Reader.Receive(...)` tạo raw record có `DeviceId`, `GateId`, gate state và signal facts.
2. `WriterPlay.Queue(fileIndex, log)` route record theo `FileId`.
3. `ThreadFileWriter` append UTF-8 vào:

```text
{FolderRawData}/yyyy/MM/dd/File_{FileId}.txt
```

4. Mỗi lần ghi thêm marker và newline:

```text
{raw record}e(0){Environment.NewLine}
```

5. File writer dùng `FileMode.Append`, `FileAccess.Write`, `FileShare.Read` và flush sau mỗi record.
6. Ngày mới tạo thư mục/file mới; file cũ không phải nguồn đang append chính.

Vị trí source dùng để kiểm chứng:

- `Texpo.Stw/Texpo.Stw.RFID.Antenna/Reader.cs:166-182`: tạo tag raw record và queue theo file index.
- `Texpo.Stw/Texpo.Stw.RFID.Antenna/AntennaCenter.Start.WriterPlay.cs`: chọn `Writer` theo `RawDataFile.FileId`.
- `Texpo.Stw/Texpo.Stw.RFID.Antenna/Writer.cs`: map `FolderRawData` và `File_{FileId}` vào file writer.
- `Texpo.Stw/Texpo.Stw.Core/Utility/ThreadFileWriter.cs:72-101`: append UTF-8, thêm `e(0)`/newline, flush và mở file share-read.
- `Texpo.Stw/Texpo.Stw.RFID.Analytics/Common/LogFileByHost/FileReader.cs`: Analytics hiện đọc tới terminator rồi chỉ commit position sau processing.
- `Texpo.Stw/Texpo.Stw.Core/Utility/IO/DiskDrive.cs:28-49`: implementation hiện tại đọc block theo byte position và terminator.

Đây là bằng chứng static source. Architecture mới không copy nguyên implementation cũ: Sprint 1 nâng offset lên `long`, bổ sung durable Mongo checkpoint và failure/restart semantics.

Các raw block đã thấy:

| Block | Vai trò |
|---|---|
| `@(...)` | header: tag/time/device/gate |
| `b(...)` | gate/user state |
| `t(...)` | antenna và signal |
| `te(...)` | event/process/quantity |
| `sp(...)` | custom process |
| `u(...)` | user |
| `e(0)` | record terminator, không phải business block |

Một record không bắt buộc có tất cả block. Parser phải tolerant và không suy đoán field không tồn tại.

## 5. System context

```text
                         read-only file access
RFID.Antenna -----------------------------------------+
                                                      |
                                                      v
                                      +-----------------------------+
                                      | DeviceEventHistory.Worker   |
                                      |                             |
                                      | discovery -> tail -> frame  |
                                      | -> parse -> persist         |
                                      +--------------+--------------+
                                                     |
                                                     v
                                      +-----------------------------+
                                      | MongoDB                     |
                                      |                             |
                                      | device_event_history        |
                                      | ingestion_failures          |
                                      | ingestion_checkpoints       |
                                      +-----------------------------+
```

### 5.1. Deployment boundary

Worker phải nhìn thấy cùng raw files theo một trong hai topology:

1. **Same filesystem/shared folder**: Worker đọc trực tiếp `FolderRawData` hoặc read-only share.
2. **Mirrored folder**: một thành phần hạ tầng bên ngoài mirror file về local path; Worker chỉ đọc mirror.

Sprint 1 không tự quyết định `RFID.Downloader` có bắt buộc hay không. Deployment phải xác nhận:

- absolute source root;
- account chạy Worker có read permission;
- độ trễ và semantics của share/mirror;
- file có được append tại chỗ hay replace nguyên file;
- timezone dùng để tạo thư mục ngày.

## 6. Kiến trúc solution

```text
DeviceEventHistory/
|-- DeviceEventHistory.sln
|-- Directory.Build.props
|-- Directory.Packages.props
|-- .editorconfig
|-- .gitignore
|-- README.md
|
|-- src/
|   |-- DeviceEventHistory.Domain/
|   |-- DeviceEventHistory.Application/
|   |-- DeviceEventHistory.Infrastructure/
|   `-- DeviceEventHistory.Worker/
|
|-- tests/
|   |-- DeviceEventHistory.UnitTests/
|   |-- DeviceEventHistory.IntegrationTests/
|   |-- DeviceEventHistory.ArchitectureTests/
|   `-- DeviceEventHistory.EndToEndTests/
|
|-- deploy/
|   |-- docker/
|   |-- compose/
|   |-- windows-service/
|   `-- environments/
|
`-- docs/
    |-- architecture/
    |-- contracts/
    |-- operations/
    `-- samples/
```

### 6.1. Project dependency

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

Quy tắc bắt buộc:

- Domain không reference project khác trong solution.
- Application chỉ reference Domain.
- Infrastructure reference Application và Domain.
- Worker reference Application và Infrastructure để compose DI.
- MongoDB Driver chỉ xuất hiện trong Infrastructure.
- File-system implementation chỉ xuất hiện trong Infrastructure.
- `BackgroundService`, hosting và configuration root chỉ nằm trong Worker.
- Không project nào reference source project của G-ERP, RFID hoặc ERP remote.
- Không copy legacy entity trực tiếp vào Domain; chỉ tạo canonical model cần cho history.

### 6.2. Vì sao chưa tách nhiều Infrastructure project

Sprint 1 giữ một project Infrastructure, nhưng chia namespace/folder rõ ràng giữa `RfidRawLog`, `MongoDb`, `Metadata` và `Observability`. Việc tách thành package/project riêng chỉ thực hiện khi Sprint 2 bổ sung AppHub adapter hoặc khi deployment/package coupling chứng minh cần thiết.

## 7. Cấu trúc solution và file thực tế

### 7.1. Domain

```text
src/DeviceEventHistory.Domain/
|-- Events/CanonicalDeviceEvent.cs
`-- Common/AppConst.cs
```

Trách nhiệm:

- Chứa canonical concepts ổn định.
- Không biết file API, MongoDB, BSON, Worker hosting hoặc configuration.
- Không tự parse chuỗi legacy.
- Cho phép `facts` thiếu field khi source không có dữ liệu.

### 7.2. Application

```text
src/DeviceEventHistory.Application/
|-- Metadata/{SourceDefinition,RawLogSourceMode,IDeviceMetadataResolver}.cs
|-- Observability/{IIngestionTelemetry,NullIngestionTelemetry}.cs
|-- Parsing/*.cs
`-- Persistence/*.cs
```

Trách nhiệm:

- Điều phối một record hoàn chỉnh từ raw source tới history/failure.
- Tạo deterministic `eventId`.
- Áp dụng rule: persist trước, checkpoint sau.
- Không biết `FileStream`, `BsonDocument` hoặc `IMongoCollection`.
- Persistence abstraction phải theo use case, không dùng generic repository.

### 7.3. Infrastructure - RFID raw-log adapter

```text
src/DeviceEventHistory.Infrastructure/
|-- Metadata/ConfigurationDeviceMetadataResolver.cs
|-- MongoDb/{Configuration,Execution,Indexes,Mapping,Stores}/*.cs
|-- Observability/{IngestionHealthState,IngestionMetrics,LoggingScopes}.cs
`-- RfidRawLog/{Configuration,Discovery,Framing,Parsing,Reading}/*.cs
```

### 7.4. Worker composition root

```text
src/DeviceEventHistory.Worker/
|-- Configuration/{WorkerOptions,IngestionOptions,ObservabilityOptions,OptionsValidators,ServiceCollectionExtensions,ConfigurationRedactor}.cs
|-- HostedServices/{StartupInitializationHostedService}.cs
|-- HealthChecks/*.cs
|-- Orchestration/{RawLogIngestionHostedService,SourcePollingCoordinator,FairFileScheduler,FileTurnProcessor,FileRegistry,GracefulShutdownCoordinator}.cs
`-- Program.cs
```

Worker chỉ làm composition/orchestration. Logic framing, parsing và persistence không viết trực tiếp trong `BackgroundService.ExecuteAsync()`.

## 8. Configuration contract

Ví dụ cấu hình không chứa secret:

```json
{
  "DeviceEventHistory": {
    "Enabled": true,
    "WorkerId": "device-event-history-worker-01",
    "RawLog": {
      "PollInterval": "00:00:02",
      "ReadBufferBytes": 524288,
      "MaxRecordBytes": 1048576,
      "LookbackDays": 1,
      "MaxConcurrentFiles": 4,
      "MaxBytesPerTurn": 2097152,
      "MaxRecordsPerTurn": 1000,
      "MaxTurnDuration": "00:00:00.250",
          "StartupExistingFilePolicy": "End",
          "NewFilePolicy": "Beginning",
      "OnFileTruncated": "StopAndAlert",
      "Sources": [
        {
          "SourceId": "antenna-site-a",
          "RootPath": "D:/RFID/RawData",
          "CompanyId": 2,
          "TimeZoneId": "SE Asia Standard Time",
          "FilePattern": "File_*.txt",
          "Enabled": true
        }
      ]
    },
    "DatabaseSettings": {
      "MongoDb": {
        "DatabaseName": "device_event_history",
        "HistoryCollection": "device_event_history",
        "FailureCollection": "ingestion_failures",
        "CheckpointCollection": "ingestion_checkpoints"
      }
    },
    "Ingestion": {
      "DefaultRetentionDays": 90,
      "FailureRetentionDays": 30,
      "PersistenceRetryCount": 5,
      "ShutdownTimeout": "00:00:30"
    }
  }
}
```

MongoDB connection string phải đến từ environment/secret store, không commit vào `appsettings.json`.

### 8.1. Source identity

`SourceId` là định danh ổn định của một Antenna installation hoặc một raw-log stream. Nó không phải `FileId`, `DeviceId` hoặc machine name tự động thay đổi.

Checkpoint key phải gồm tối thiểu:

```text
SourceId + FolderDate + FileId
```

Nếu nhiều file cùng `FileId` có thể tồn tại trong một source/date, thêm normalized relative path hoặc stream discriminator.

### 8.2. Tenant resolution

Raw record không luôn chứa `CompanyId`. Sprint 1 dùng `CompanyId` cấu hình theo `SourceId` làm tenant boundary ban đầu.

- Không nhận CompanyId từ tên file.
- Không suy đoán CompanyId từ `FileId`.
- Nếu source chưa cấu hình tenant, Worker không ghi normal history; nó báo configuration error hoặc route record vào failure theo policy đã chốt.
- Metadata resolver được đặt sau abstraction để tương lai thay bằng catalog/database lookup.

## 9. Runtime pipeline

```text
poll sources
    -> discover date folders and File_{FileId}.txt
    -> load checkpoint
    -> open file read-only from byte offset
    -> read bounded byte chunk
    -> frame complete records by UTF-8 bytes of e(0)
    -> for each contiguous complete record
         create RawFileRecord
         create deterministic eventId
         parse tolerant blocks
         map canonical event/failure
         persist history/failure
         advance checkpoint to record offsetEnd
    -> leave incomplete trailing bytes uncommitted
    -> repeat
```

### 9.1. Discovery

Discovery phải:

- enumerate `yyyy/MM/dd` folders theo source timezone;
- scan ngày hiện tại và `LookbackDays` để bắt late flush/restart;
- chỉ nhận file khớp `File_{FileId}.txt`;
- parse `FileId` bằng regex chặt chẽ;
- không dựa riêng vào `FileSystemWatcher`, vì watcher có thể mất event trên network share/restart;
- polling idempotent: cùng file được discover nhiều lần không tạo nhiều processing owner;
- giới hạn concurrency để một source nhiều file không làm cạn thread/file handle.

`FileSystemWatcher` có thể bổ sung như wake-up hint trong tương lai, nhưng polling/checkpoint vẫn là correctness mechanism.

### 9.2. File access

Reader mở file:

- `FileAccess.Read`;
- share mode tương thích với writer đang append;
- không lock, rename, copy, truncate hoặc sửa source file;
- offset dùng `long`, không dùng `int`;
- offset là byte offset, không phải character index.

UTF-8 multi-byte boundary phải được xử lý bằng byte framing hoặc stateful decoder. Không decode tùy ý từng chunk rồi cộng độ dài string vào checkpoint.

### 9.3. Record framing

Terminator canonical:

```text
e(0)
```

Framer phải:

- tìm terminator trên byte stream;
- trả từng record kèm `offsetStart` và `offsetEnd` tuyệt đối;
- hỗ trợ nhiều record trong một chunk;
- giữ trailing partial bytes cho poll tiếp theo;
- không checkpoint partial record;
- bỏ newline sau terminator khỏi business payload nhưng vẫn tính đúng byte offset đã consume;
- không chờ newline để công nhận record hoàn chỉnh; nếu CR/LF đã có ngay sau terminator thì consume cùng record, nếu chưa có thì poll sau phải bỏ leading CR/LF trước record kế tiếp;
- giới hạn `MaxRecordBytes`;
- nếu record vượt giới hạn, tạo operational failure/alert theo policy, không giữ RAM vô hạn.

Một file chỉ được advance theo prefix liên tục. Không xử lý record phía sau nếu record trước chưa có persistence outcome rõ ràng.

### 9.4. Parser

Parser thực hiện hai tầng:

1. Tokenize các block và giữ raw value.
2. Parse từng block độc lập thành facts đã xác nhận.

Quy tắc:

- `@(...)` phải tồn tại cho normal history V1; nếu thiếu hoặc malformed, route failure.
- `e(0)` được framer xử lý, không đưa thành facts.
- Block không có thì để null/omit.
- Block chưa biết được ghi warning và giữ raw payload; không làm Worker crash.
- Numeric/date parsing dùng culture xác định, không phụ thuộc machine locale.
- Raw timestamp không có date phải kết hợp với `folderDate` theo source timezone, sau đó chuyển UTC; nếu không đáng tin cậy thì `occurredAtUtc = null` và giữ text gốc.
- `DeviceId = 0` hoặc `GateId = 0` có thể xuất hiện ở business record; không tự động coi là malformed nếu category cho phép.
- Không tự tạo EPC nếu raw source chỉ có TagId.

### 9.5. Category mapping

Mapping Sprint 1 tối thiểu:

| Điều kiện record | Category |
|---|---|
| Có `t(...)` | `tag_read` |
| Có `te(...)` | `business_process` |
| Không đủ bằng chứng | `unknown` |

Nếu một record có nhiều block, facts cùng tồn tại trong một history document. Không tách sáu block thành sáu event độc lập.

## 10. Event identity và idempotency

`eventId` đề xuất:

```text
SHA-256(
  SourceId + "|" +
  RelativeFilePath + "|" +
  OffsetStart + "|" +
  OffsetEnd + "|" +
  SHA-256(RawRecordBytes)
)
```

Yêu cầu:

- deterministic khi Worker restart;
- khác nhau giữa các source cùng tên file;
- không dựa vào current time hoặc WorkerId;
- unique index trong `device_event_history`;
- duplicate key với cùng eventId trong retry được xem là idempotent success sau khi xác nhận semantics.

Không dùng `DeviceId + TagId`, EPC hoặc payload hash đơn lẻ làm unique key. Hai lần đọc giống nhau tại hai thời điểm/offset khác nhau đều phải được giữ.

`failureId` cũng phải deterministic theo source file key, offset và raw hash để crash/retry không nhân đôi failure.

## 11. Persistence và checkpoint protocol

### 11.1. Collections Sprint 1

| Collection | Vai trò |
|---|---|
| `device_event_history` | Append-only history cho record hợp lệ |
| `ingestion_failures` | Complete record không thể map/persist bình thường vì data contract |
| `ingestion_checkpoints` | Byte position đã xử lý bền vững theo source/date/file |

Projection collections không thuộc Sprint 1.

### 11.2. Checkpoint document

Checkpoint tối thiểu:

```json
{
  "_id": "antenna-site-a|2026-08-24|12",
  "sourceId": "antenna-site-a",
  "folderDate": "2026-08-24",
  "fileId": 12,
  "relativePath": "2026/08/24/File_12.txt",
  "position": 10480,
  "lastEventId": "...",
  "lastRecordHash": "...",
  "observedFileLength": 10720,
  "workerId": "device-event-history-worker-01",
  "updatedAtUtc": "2026-08-24T01:00:00Z",
  "version": 7
}
```

`position` là byte offset ngay sau phần terminator/newline đã consume theo framing contract.

### 11.3. Write order

Cho từng record theo thứ tự file:

```text
1. Persist device_event_history
   hoặc persist ingestion_failures
2. MongoDB xác nhận outcome
3. Advance checkpoint bằng compare-and-set/version
4. Mới xử lý record kế tiếp trong contiguous sequence
```

Không advance checkpoint khi:

- record chưa có terminator;
- MongoDB timeout/unavailable;
- history/failure write chưa được xác nhận;
- checkpoint compare-and-set phát hiện owner/version conflict.

### 11.4. At-least-once recovery

Không cần transaction MongoDB nhiều collection để đạt correctness Sprint 1:

- Nếu crash trước history write: checkpoint cũ, record được đọc lại.
- Nếu crash sau history write nhưng trước checkpoint: record được đọc lại, unique eventId biến retry thành idempotent success, sau đó checkpoint advance.
- Nếu crash sau checkpoint: record không đọc lại.

Đây là at-least-once processing với effectively-once persistence theo eventId.

### 11.5. Batch policy

Đường correctness đầu tiên nên persist tuần tự hoặc theo batch nhỏ nhưng checkpoint chỉ advance đến contiguous prefix đã xác nhận.

Nếu dùng unordered bulk write:

- phải map rõ outcome theo eventId;
- duplicate cùng eventId là idempotent success;
- không được nhảy checkpoint qua một record chưa xác nhận;
- transient failure phải retry với cùng eventId.

Ưu tiên correctness trước throughput; chỉ tối ưu batch sau benchmark bằng volume thật.

## 12. File anomalies

### 12.1. Partial trailing record

Đây là trạng thái bình thường khi Worker đọc đúng lúc Antenna chưa ghi xong. Worker giữ checkpoint cũ và poll lại; không tạo failure ngay.

Nếu partial record tồn tại quá ngưỡng sau khi ngày/file không còn active, chuyển health sang degraded và ghi diagnostic. Việc tạo `ingestion_failures` cho partial bytes phải theo policy rõ ràng vì chưa có complete source record.

### 12.2. File truncated hoặc replaced

Nếu `currentLength < checkpoint.position`:

- Sprint 1 mặc định `StopAndAlert` cho file đó;
- không tự reset position về 0;
- không silently bỏ qua;
- operator phải xác định file bị truncate, replace hay mirror reset.

Policy replay từ đầu chỉ dùng khi eventId deterministic và operator chủ động cho phép.

### 12.3. File missing hoặc source unavailable

- Readiness degraded/unhealthy theo thời gian cấu hình.
- Giữ checkpoint.
- Retry discovery với backoff có giới hạn.
- Không xóa checkpoint khi file tạm mất.

### 12.4. Date rollover

- Source timezone quyết định folder date.
- Worker luôn tiếp tục scan hôm qua trong `LookbackDays` để bắt leftover flush.
- Checkpoint tách theo folder date.
- Không dùng local timezone của container nếu khác source timezone.

## 13. Concurrency model

Sprint 1 có đúng một ingestion Worker owner.

Trong process:

- một file có tối đa một active processing session;
- nhiều file có thể chạy song song tới `MaxConcurrentFiles`;
- record trong cùng file xử lý tuần tự;
- scheduler đảm bảo fairness giữa các file bằng budget theo bytes/records/duration cho mỗi lượt;
- file còn backlog được requeue cuối hàng thay vì giữ worker vô hạn;
- file đã caught-up chờ poll tiếp theo, không busy-loop;
- polling không tạo duplicate task cho file đang xử lý;
- cancellation token truyền qua discovery, read và MongoDB write.

Với 20 `RFID.RawDataFiles`, Worker giữ 20 logical cursors/checkpoints nhưng không tạo cố định 20 OS threads. Initial tuning có thể bắt đầu với bốn file consumers rồi điều chỉnh bằng load test.

Không dùng một global unbounded queue. Nếu có channel nội bộ:

- channel phải bounded;
- item giữ source file key và offset;
- saturation phải có metric/health;
- không drop silently;
- checkpoint vẫn là authority, không phải queue memory.

## 14. Graceful shutdown

```text
application stopping
    -> stop scheduling poll mới
    -> stop opening file session mới
    -> allow active record persistence hoàn tất trong timeout
    -> commit checkpoint cho outcome đã xác nhận
    -> cancel phần còn lại
    -> log file/offset chưa xử lý
    -> dispose file/Mongo resources
```

Không checkpoint item chỉ vì shutdown timeout. Record chưa hoàn tất sẽ được đọc lại khi restart.

## 15. MongoDB indexes Sprint 1

Initializer phải idempotent và tối thiểu tạo:

### device_event_history

```text
unique eventId
occurredAtUtc DESC
device.id + occurredAtUtc DESC
device.gateId + occurredAtUtc DESC
facts.tagRead.tagId + occurredAtUtc DESC
category + occurredAtUtc DESC
parse.status + receivedAtUtc DESC
source.sourceId + source.folderDate + source.fileId + source.offsetStart
```

### ingestion_failures

```text
unique failureId
source.sourceId + source.folderDate + source.fileId + source.offsetStart
error.code + receivedAtUtc DESC
resolvedAtUtc
```

### ingestion_checkpoints

```text
unique sourceId + folderDate + fileId (+ relativePath khi cần)
updatedAtUtc DESC
```

TTL chỉ tạo khi schema/retention đã chốt `expireAtUtc`. Không dùng TTL cho checkpoint.

## 16. Observability

### 16.1. Structured logs

Log context tối thiểu:

- `WorkerId`;
- `SourceId`;
- `FolderDate`;
- `FileId`;
- relative path;
- `OffsetStart`/`OffsetEnd`;
- `EventId` hoặc `FailureId`;
- parser version;
- retry attempt;
- duration/result.

Không log toàn bộ raw payload ở mức mặc định. Sample payload chỉ bật có kiểm soát, giới hạn kích thước và redaction nếu cần.

### 16.2. Metrics

- discovered files;
- active files;
- source access failures;
- bytes read;
- complete records framed;
- partial trailing bytes;
- records parsed/warned/failed;
- history/failure write success;
- MongoDB retry/failure/latency;
- duplicate eventId count;
- checkpoint position và advance failures;
- ingestion lag theo file/source;
- oversized records;
- file truncation/replacement detection;
- graceful shutdown unprocessed count nếu xác định được.

### 16.3. Health

| State | Ý nghĩa Sprint 1 |
|---|---|
| Live | Process/event loop còn chạy |
| Ready | Options hợp lệ, MongoDB truy cập được, ít nhất một enabled source root hợp lệ |
| Degraded | Source chậm/tạm mất, partial stale, MongoDB retry, ingestion lag cao |
| Unhealthy | MongoDB unavailable quá ngưỡng, file truncated, pipeline không tiến triển |

Source không có event mới không tự động là unhealthy. Health phải phân biệt “không có dữ liệu” và “không thể đọc dữ liệu”.

## 17. Security

- Worker account chỉ cần read permission trên raw-log root.
- Không cho Worker write/delete/rename source file.
- Mongo credential có quyền tối thiểu cho ba collections và index initialization theo deployment role.
- Connection string lấy từ secret store/environment.
- Log không chứa password/token/connection string.
- `RootPath` phải validate thành absolute path và nằm trong allowlisted source roots.
- Không chấp nhận path lấy từ raw record.
- Company mapping do server configuration quyết định, không do file name hoặc client input.

## 18. Testing architecture

### 18.1. Unit tests

- path/date/FileId parsing;
- UTF-8 byte framing;
- nhiều record trong một chunk;
- terminator chia qua hai chunk;
- partial record không advance offset;
- maximum record size;
- từng raw block parser;
- missing/unknown block;
- culture/timezone parsing;
- deterministic eventId/failureId;
- category mapping;
- checkpoint decision và contiguous ordering;
- options validation.

### 18.2. Integration tests

- append file trong khi Worker đang đọc;
- writer chỉ ghi nửa record rồi ghi phần còn lại;
- restart từ Mongo checkpoint;
- crash simulation sau history write/trước checkpoint;
- duplicate eventId;
- malformed complete record -> failure -> checkpoint advance;
- MongoDB unavailable/recovery;
- file truncation detection;
- date rollover/lookback;
- nhiều file và concurrency fairness;
- index initializer idempotent.

Integration test dùng temporary source directory và MongoDB test database/container. Không cần source RFID thật cho test pipeline.

### 18.3. Architecture tests

- Domain không reference Application/Infrastructure/Worker.
- Application không reference MongoDB/FileSystem/Hosting.
- MongoDB types không rò sang Domain/Application.
- Worker không chứa parser hoặc repository implementation.
- Không reference G-ERP/RFID/ERP source projects.
- Không có SignalR package trong Sprint 1.

### 18.4. End-to-end acceptance

Thứ tự bằng chứng:

1. Fixture raw record -> Worker -> MongoDB.
2. Fixture append/partial/restart -> MongoDB + checkpoint.
3. Sample raw file đã redaction từ môi trường mục tiêu.
4. Worker đọc folder UAT thực tế ở read-only mode.
5. RFID.Antenna/simulator tạo record mới -> history xuất hiện và offset đối chiếu đúng.
6. Physical reader acceptance riêng nếu có thiết bị.

Không dùng test fixture hoặc simulator để tuyên bố physical-device validation.

## 19. Deployment

Worker hỗ trợ:

- Windows Service khi chạy gần RFID.Antenna/raw folder;
- container khi raw folder/share được mount read-only và timezone/path đã cấu hình đúng.

Một environment chỉ bật một Worker ingestion owner trong Sprint 1.

Startup order:

```text
bind + validate options
    -> verify source roots
    -> connect/ping MongoDB
    -> initialize collections/indexes
    -> load checkpoints
    -> start polling
```

Nếu options, MongoDB hoặc source permission bắt buộc không hợp lệ, Worker fail fast hoặc báo not-ready; không chạy ở trạng thái tưởng là healthy nhưng không ingest.

## 20. Extension points cho Sprint 2 và tương lai

Sprint 1 chuẩn bị các boundary sau nhưng không implement AppHub:

```text
IRawEventSourceAdapter
    +-- RfidAntennaFileAdapter       (Sprint 1)
    `-- ErpAppHubAdapter             (Sprint 2)

ProcessRawSourceEvent
    -> canonical mapping
    -> history/failure writers
```

Điểm tái sử dụng cho Sprint 2:

- canonical event/failure model;
- history/failure Mongo writers;
- event identity abstraction;
- tenant/device resolution;
- telemetry conventions;
- parser version/schema version;
- projection trigger sau history success trong sprint sau.

Điểm không được ép dùng chung:

- file checkpoint không áp dụng cho SignalR connection;
- file byte offset không đưa vào generic event model bắt buộc;
- AppHub reconnect/rejoin không đặt vào File Worker;
- rawArguments SignalR khác raw text file và phải có source-specific envelope.

## 21. Definition of Done Architecture Sprint 1

- Solution/project boundaries đúng mục 6.
- Options validate và không chứa secret trong source.
- Worker discover được đúng date folder và `File_{FileId}.txt`.
- Reader dùng `long` byte offset và không khóa source writer.
- Framer chỉ trả complete record đến `e(0)`.
- Parser tolerant, giữ raw và facts đã xác nhận.
- `eventId` deterministic và unique.
- History/failure persist trước checkpoint.
- Restart/crash retry không nhân đôi history.
- Partial record, malformed record, file truncation và Mongo failure có behavior/test rõ ràng.
- Ba collection và index Sprint 1 được initialize idempotent.
- Structured logs, metrics và health phản ánh pipeline thật.
- Graceful shutdown không checkpoint record chưa xác nhận.
- Chỉ một Worker owner hoạt động.
- Unit, integration, architecture và vertical-slice acceptance pass trên .NET 10.
- Không có SignalR/AppHub dependency trong Sprint 1.
- Có runbook cấu hình source path, recovery checkpoint, file truncation và Mongo incident.

## 22. Các quyết định cần chốt trước UAT

- Raw-log root thực tế của từng environment.
- Worker đọc trực tiếp folder hay đọc mirror do Downloader cung cấp.
- Stable `SourceId` và `CompanyId` cho từng source.
- Source timezone.
- Start policy: đọc từ đầu, từ thời điểm deploy hay backfill N ngày.
- Retention history/failure.
- Max record size, read buffer, poll interval và concurrency.
- Expected event volume và acceptable ingestion lag.
- MongoDB topology, write concern, backup/restore.
- Operator policy khi file truncate/replace.
- Sample log đã redaction để khóa parser tests trước UAT.
