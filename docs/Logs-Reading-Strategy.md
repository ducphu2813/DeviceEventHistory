# Device Event History - Chiến thuật đọc raw-log liên tục

> Trạng thái implementation (2026-08-26): chiến thuật dưới đây đã được triển khai trong `D:\texpo\logging-worker\device-event-worker`. Khi tên component trong các mục cũ khác với source, dùng bảng ánh xạ ở mục 19 và `Device-Event-History-Current-Codebase.md` làm chuẩn.

## 1. Mục đích

Tài liệu này chốt chiến thuật để `DeviceEventHistory.Worker` đọc liên tục các raw-log do `RFID.Antenna` append vào nhiều file theo ngày.

Phạm vi Sprint 1:

```text
RFID.Antenna
    -> yyyy/MM/dd/File_{FileId}.txt
    -> DeviceEventHistory.Worker
    -> MongoDB history/failure/checkpoint
```

AppHub/SignalR thuộc Sprint 2 và không ảnh hưởng correctness của chiến thuật đọc file Sprint 1.

## 2. Kết luận ngắn

Worker sẽ **tail nhiều file theo checkpoint riêng**, tương tự nguyên tắc của `RFID.Analytics`, nhưng không copy nguyên cách tổ chức thread/cursor hiện tại.

Chiến thuật được chọn:

1. Poll filesystem để phát hiện file thực tế trong thư mục ngày.
2. Mỗi `(SourceId, FolderDate, FileId)` có một MongoDB checkpoint riêng.
3. Mở file read-only trong khi `RFID.Antenna` vẫn append.
4. Đọc từ byte offset đã checkpoint bằng chunk có giới hạn.
5. Chỉ lấy các record hoàn chỉnh kết thúc bằng `e(0)`.
6. Parse và ghi MongoDB theo đúng thứ tự trong từng file.
7. Chỉ advance checkpoint sau khi history hoặc failure đã được MongoDB xác nhận.
8. Nếu còn backlog, requeue file ngay; nếu đã bắt kịp cuối file, chờ poll tiếp theo.
9. Dùng scheduler công bằng và concurrency có giới hạn; không cần một OS thread riêng cho mỗi file.
10. Theo dõi đồng thời file hôm nay và file hôm qua trong thời gian drain/late flush.

Với 20 file hiện tại, Worker có 20 logical cursors, không nhất thiết có 20 threads.

## 3. Mô hình ghi file hiện tại của RFID.Antenna

### 3.1. Danh sách file

Ảnh khảo sát ngày 2026-08-24 cho thấy bảng:

```text
[dbo].[RFID.RawDataFiles]
```

của company 2 đang có `FileId` từ 1 đến 20. `RFID.Antenna` đọc danh sách này và tạo một writer theo từng `FileId`.

Tên file:

```text
File_1.txt
File_2.txt
...
File_20.txt
```

Nếu DB bổ sung `FileId = 21`, Antenna có thể tạo writer/file mới sau quá trình refresh cấu hình. Worker không được compile hoặc cấu hình cứng số lượng 20.

### 3.2. Folder theo ngày

Mỗi ngày có folder riêng:

```text
{FolderRawData}/yyyy/MM/dd/
```

Ví dụ:

```text
D:/RFID/RawData/2026/08/24/File_1.txt
D:/RFID/RawData/2026/08/24/File_2.txt
...
D:/RFID/RawData/2026/08/24/File_20.txt
```

Qua ngày mới, Antenna đóng stream cũ và mở file cùng `FileId` trong folder ngày mới.

### 3.3. Append record

`ThreadFileWriter` ghi UTF-8 theo mẫu:

```text
{raw record}e(0){Environment.NewLine}
```

Ví dụ:

```text
@(10001,14:30:20,101,5)b(0)t(1,24/08/2026 14:30:19,24/08/2026 14:30:20,3,20,0,0,920,-55)e(0)
```

File được mở với:

```text
FileMode.Append
FileAccess.Write
FileShare.Read
```

Do đó Worker có thể mở file để đọc trong lúc Antenna tiếp tục ghi, miễn Worker không yêu cầu write/delete access và không giữ lock không tương thích.

### 3.4. Ý nghĩa của FileId

`FileId` là khóa định tuyến raw-log. Nó quyết định writer/file nào nhận record, nhưng không phải định danh thiết bị hoặc gate.

Một `File_12.txt` có thể chứa record có nhiều `DeviceId`/`GateId` khác nhau tùy mapping nghiệp vụ. Worker phải parse `DeviceId` và `GateId` từ record, không suy ra từ `FileId`.

## 4. RFID.Analytics đang đọc như thế nào

### 4.1. Khởi tạo danh sách file

`AnalyticCenter.Start.WorkerPlay.cs`:

1. Query `RawDataFile.DataSourceAuto` từ DB.
2. Lấy danh sách `FileId`.
3. Chia danh sách thành các nhóm bằng `ProcessInPage(TotalThread, ...)`.
4. Tạo `Worker.Two` cho mỗi nhóm với `FileFilter` như `File_1.txt`, `File_2.txt`.

Lưu ý: helper `ProcessInPage` dùng tham số như **page size**, nên tên `TotalThread` dễ gây hiểu nhầm. Ví dụ 20 files:

- `TotalThread = 1` có thể tạo 20 groups/workers, mỗi worker một file;
- `TotalThread = 5` có thể tạo 4 groups/workers, mỗi worker năm file.

Worker mới không copy cơ chế này.

### 4.2. Discover file trong folder ngày

`LogFileHostReader.Refresh()`:

1. Tạo folder từ `PathLog + yyyy/MM/dd`.
2. Enumerate `*.txt`.
3. Lọc file có prefix như `File_`.
4. Parse numeric ID từ file name.
5. Lọc theo `FileFilter` của worker group.
6. Tạo một `FileReader` trong RAM cho file mới.

Refresh reader được chạy lúc adapter start và định kỳ qua `PeriodRefreshReader`.

### 4.3. Mỗi file có cursor riêng trong RAM

`FileReader` giữ:

```text
Position      // cursor đã commit
positionWait  // cursor vừa đọc, chưa commit
```

Mỗi lần `Read()`:

1. Gọi `DiskDrive.ReadBlock(FileName, Position, "e(0)", Buffer)`.
2. Đọc một chunk từ `Position`.
3. Tìm terminator `e(0)` cuối cùng trong chunk.
4. Chỉ trả bytes từ Position tới terminator cuối cùng.
5. Đặt `positionWait = data.Index`.
6. Chưa thay đổi `Position`.

Sau khi Adapter xử lý xong:

```text
Reader.Commit()
```

mới copy `positionWait` vào `Position`.

Đây là nguyên tắc đúng cần giữ: **read -> process -> commit**.

### 4.4. Một cycle đọc tất cả file

`LogFileHostReader.DoRead(...)` gọi `Read()` một lần trên từng `FileReader`. Điều này tạo fairness tự nhiên: mỗi cycle, mỗi file được đọc tối đa một block trước khi quay lại cycle tiếp theo.

`Adapter.Run()` thực hiện:

```text
Reader.Read()
    -> parse/process business logic
    -> Reader.Commit()
```

Nếu processing ném exception trước `Commit()`, cursor trong RAM không advance.

### 4.5. Chuyển ngày

Analytics xử lý một `Date` tại một thời điểm. Với online mode, nó chờ khoảng 5 phút sau midnight, vét dữ liệu ngày cũ bằng `TryRunToEnd()`, rồi tạo adapter cho ngày mới.

### 4.6. Hạn chế khi dùng cho Device Event History

| Hạn chế Analytics hiện tại | Ảnh hưởng nếu copy nguyên |
|---|---|
| `Position` chỉ nằm trong RAM | Restart đọc lại từ đầu hoặc phụ thuộc process state khác |
| Position dùng `int` | Không an toàn với file lớn hơn giới hạn int |
| File list/group lấy từ DB lúc startup | File mới/cấu hình mới có thể cần refresh/restart group |
| Grouping gắn với thread model legacy | Khó scale và tên `TotalThread` dễ hiểu sai |
| Một Date active tại một thời điểm | Khó đọc song song ngày mới và late flush ngày cũ |
| Buffer cố định và chỉ tìm terminator trong block | Record lớn hơn buffer có thể làm reader không tiến triển |
| Không có durable eventId/checkpoint Mongo | Không bảo đảm restart/idempotent persistence cho history mới |
| `GC.Collect()` thủ công | Không nên mang sang .NET 10 Worker nếu chưa có profiling evidence |

## 5. Quyết định: học gì và thay đổi gì

| Chủ đề | Học theo Analytics | Thiết kế Worker mới |
|---|---|---|
| File cursor | Một cursor cho mỗi file | Durable Mongo checkpoint cho mỗi source/date/file |
| Record completeness | Chỉ đọc tới `e(0)` cuối cùng | Byte framer hỗ trợ terminator split qua chunk |
| Commit | Process xong mới commit | Mongo xác nhận history/failure rồi mới checkpoint |
| Discovery | Scan folder và filter `File_*.txt` | Dynamic polling, không phụ thuộc DB list để đọc actual files |
| Fairness | Mỗi cycle đọc một block/file | Time-sliced fair scheduler với byte/record budget |
| Day rollover | Vét ngày cũ trước khi đổi ngày | Đọc hôm nay và hôm qua đồng thời |
| File count | DB list chia worker lúc startup | Runtime discovery, số file thay đổi động |
| Threads | Worker groups legacy | Async I/O + bounded concurrency |
| Restart | In-memory position | At-least-once + deterministic eventId |
| Errors | Exception giữ cursor cũ | Failure collection, health và retry policy rõ ràng |

## 6. Nguồn file nào là authority

### 6.1. Filesystem là authority cho file thực tế

Để đọc data, Worker scan filesystem:

```text
{RootPath}/{yyyy}/{MM}/{dd}/File_*.txt
```

Lý do:

- File có trong DB chưa chắc đã có data.
- File/mirror có thể đến trễ.
- File mới phải được phát hiện mà không restart Worker.
- Worker không nên bắt buộc kết nối ERP DB chỉ để tail file.

### 6.2. DB là expected inventory, không phải cursor authority

`RFID.RawDataFiles` có thể được dùng tùy chọn để:

- biết `FileId` nào được khai báo;
- cảnh báo expected file bị thiếu;
- map company/title metadata;
- đối chiếu dynamic file changes.

Nó không quyết định byte position và không thay thế filesystem discovery.

Sprint 1 có hai lựa chọn:

1. Nếu tương lai cần expected inventory, bổ sung option riêng; model hiện tại không có `ExpectedFileIds` và discovery luôn dựa trên file thực tế.
2. Bổ sung read-only `IRawDataFileCatalog` sau vertical slice nếu thật sự cần DB reconciliation.

Không để DB catalog failure chặn việc đọc một file đang tồn tại nếu `SourceId`/`CompanyId` đã được cấu hình an toàn.

## 7. Identity của một logical file stream

Một checkpoint không được chỉ key theo `FileId` vì cùng `FileId` lặp lại mỗi ngày và có thể lặp giữa nhiều Antenna source.

Logical key:

```text
SourceFileKey = SourceId + FolderDate + FileId + RelativePathDiscriminator
```

Ví dụ:

```text
antenna-site-a|2026-08-24|1
antenna-site-a|2026-08-24|2
...
antenna-site-a|2026-08-24|20
```

Ngày hôm sau tạo 20 key mới:

```text
antenna-site-a|2026-08-25|1
...
antenna-site-a|2026-08-25|20
```

## 8. Per-file state machine

Mỗi logical file có state độc lập:

```text
Discovered
    |
    v
LoadCheckpoint
    |
    v
ReadyToRead <---------------------------+
    |                                   |
    v                                   |
ReadingChunk                            |
    |                                   |
    +--> no new bytes -> CaughtUp -------+
    |
    +--> partial only -> WaitingForMore -+
    |
    +--> complete records
              |
              v
          Persisting
              |
              +--> transient error -> Retry/Degraded
              |
              +--> confirmed
                       |
                       v
               AdvanceCheckpoint
                       |
                       v
                 ReadyToRead

Anomaly: length < checkpoint -> StoppedAndAlerted
```

Tại mọi thời điểm chỉ một processing owner được phép xử lý một `SourceFileKey`.

## 9. Scheduler cho 20+ file

### 9.1. Không tạo một thread cố định cho mỗi file

20 file không phải tải lớn đối với async file I/O. Worker dùng:

- một discovery/poll coordinator;
- một bảng trạng thái file;
- một bounded queue chứa `SourceFileKey` cần đọc;
- một số lượng consumer có giới hạn, ví dụ ban đầu `MaxConcurrentFiles = 4`.

Logical cursors có thể là 20, 40 hoặc 100; concurrency vật lý vẫn được kiểm soát.

### 9.2. Fair scheduling

Mỗi lượt xử lý một file có budget:

```text
MaxBytesPerTurn
MaxRecordsPerTurn
MaxDurationPerTurn
```

Ví dụ initial tuning:

```text
ReadBufferBytes    = 512 KB
MaxBytesPerTurn    = 2 MB
MaxRecordsPerTurn  = 1,000
MaxDurationPerTurn = 250 ms
MaxConcurrentFiles = 4
```

Khi hết budget:

- nếu file vẫn còn complete backlog, requeue ngay cuối hàng;
- nếu đã bắt kịp EOF, đánh dấu `CaughtUp` và chờ poll/wake-up tiếp theo.

Cách này ngăn `File_1.txt` rất nóng chiếm toàn bộ Worker và làm `File_2..20` bị đói.

### 9.3. Hot và cold files

| Loại | Ví dụ | Scheduling |
|---|---|---|
| Hot | File hôm nay đang append | Poll nhanh, requeue ngay khi còn backlog |
| Warm | File hôm qua chưa stable/drained | Poll chậm hơn nhưng vẫn theo dõi |
| Cold complete | File cũ đã bắt kịp và stable | Không poll liên tục; chỉ audit/backfill theo yêu cầu |
| Error | Permission/Mongo/transient error | Retry có backoff |
| Stopped | Truncate/checkpoint conflict | Không tự chạy tiếp; cần operator/policy |

## 10. Vòng polling đề xuất

### 10.1. Hai loại interval

1. `DiscoveryInterval`: tìm file mới/thư mục mới, ví dụ 10-30 giây.
2. `CaughtUpPollInterval`: kiểm tra file hot đã bắt kịp, ví dụ 1 giây.

Không cần enumerate toàn bộ directory mỗi giây. File registry đã discover có thể stat/read nhanh; full discovery chạy thưa hơn.

### 10.2. FileSystemWatcher

`FileSystemWatcher` có thể dùng để đánh thức file sớm khi có change/create, nhưng không được là correctness mechanism duy nhất vì:

- event có thể bị mất khi buffer overflow;
- network share có behavior khác local disk;
- Worker restart không nhận event quá khứ;
- watcher có thể phát duplicate/coalesced events.

Correctness vẫn dựa trên polling + durable checkpoint. Watcher chỉ là optimization sau khi vertical slice ổn định.

## 11. Thuật toán đọc một file

Pseudo-code:

```text
ProcessFile(fileKey):
    acquire per-file lock
    checkpoint = checkpointStore.Load(fileKey)
    position = checkpoint.Position

    fileLength = GetLength(filePath)
    if fileLength < position:
        mark file truncated
        set health unhealthy
        return

    budget = create turn budget

    while budget remains:
        chunk = ReadBytes(filePath, position, ReadBufferBytes)
        if chunk is empty:
            mark caught up
            return

        framed = FrameCompleteRecords(chunk, position, terminator="e(0)")

        if framed has no complete record:
            mark waiting for more bytes
            return

        for record in framed.completeRecords:
            outcome = ProcessAndPersist(record)

            if outcome is not confirmed:
                do not advance checkpoint
                return/retry by policy

            checkpointStore.Advance(record.OffsetEnd, outcome.EventId)
            position = record.OffsetEnd

            if budget exhausted:
                requeue fileKey
                return

    requeue fileKey
```

### 11.1. Đọc lại partial bytes

Phương án correctness đơn giản nhất:

- không checkpoint partial bytes;
- poll sau đọc lại từ durable position cũ;
- giới hạn `MaxRecordBytes` để không reread vô hạn.

Có thể giữ trailing buffer trong RAM để tối ưu, nhưng restart vẫn phải đúng khi buffer mất. In-memory buffer không được trở thành authority.

## 12. Record framing

### 12.1. Terminator là boundary

Worker tìm UTF-8 bytes của:

```text
e(0)
```

Record được coi là complete ngay khi thấy terminator. Newline chỉ là separator sau record.

### 12.2. Nhiều record trong chunk

Ví dụ chunk:

```text
record-Ae(0)\r\nrecord-Be(0)\r\npartial-C
```

Framer trả:

- record A;
- record B;
- không trả partial C;
- checkpoint tối đa sau record B/newline đã consume;
- poll sau đọc lại partial C từ durable offset.

### 12.3. Terminator chia qua chunk

Ví dụ:

```text
chunk 1 ends with: ...e(
chunk 2 starts with: 0)\r\n
```

Framer phải giữ overlap/in-memory remainder trong cùng turn hoặc reread từ checkpoint với buffer mở rộng. Không được kết luận malformed chỉ vì terminator nằm qua boundary.

### 12.4. Record lớn hơn buffer

Nếu không có terminator trong `ReadBufferBytes`:

1. Tiếp tục tích lũy/đọc thêm tới `MaxRecordBytes`.
2. Nếu tìm thấy terminator, xử lý bình thường.
3. Nếu vượt `MaxRecordBytes`, dừng file hoặc route operational failure theo policy; không tăng RAM vô hạn và không skip tùy tiện.

## 13. Persistence và checkpoint

### 13.1. Checkpoint per file

Ví dụ với 20 files, MongoDB có thể có:

| File key | Position | Last event | State |
|---|---:|---|---|
| `site-a|2026-08-24|1` | 1,024,800 | `evt-...` | caught_up |
| `site-a|2026-08-24|2` | 980,120 | `evt-...` | reading |
| ... | ... | ... | ... |
| `site-a|2026-08-24|20` | 2,450,090 | `evt-...` | caught_up |

Một file lỗi không làm các file còn lại mất cursor.

### 13.2. Thứ tự bắt buộc

```text
complete record
    -> parse/map
    -> persist device_event_history
       hoặc ingestion_failures
    -> MongoDB confirmed
    -> advance checkpoint
```

Không checkpoint khi:

- record chưa có `e(0)`;
- Mongo write timeout/chưa rõ outcome;
- checkpoint CAS/version conflict;
- process bị cancel trước confirmed persistence.

### 13.3. Crash recovery

Nếu crash sau history write nhưng trước checkpoint:

1. Worker restart từ checkpoint cũ.
2. Đọc lại record.
3. Tạo cùng deterministic `eventId` từ source/path/offset/raw hash.
4. Unique eventId biến lần ghi lại thành idempotent success.
5. Checkpoint được advance.

Đây là at-least-once reading với effectively-once history persistence.

### 13.4. Commit granularity

Sprint 1 nên bắt đầu commit per record hoặc per small contiguous batch.

Nếu batch:

- mọi record trước checkpoint mới phải có confirmed outcome;
- không advance qua một record lỗi chưa được lưu failure;
- batch writer phải trả outcome theo từng event;
- ưu tiên correctness trước throughput.

## 14. Ngày mới và late flush

Worker mới không cần đợi xử lý xong ngày cũ rồi mới nhìn ngày mới.

Tại midnight:

```text
Active set trước 00:00:
    2026-08-24/File_1..20

Active set sau 00:00:
    2026-08-25/File_1..20    (hot)
    2026-08-24/File_1..20    (warm/draining)
```

Chiến thuật:

1. Discover folder ngày mới ngay khi xuất hiện.
2. Tạo checkpoint key mới theo folder date.
3. Tiếp tục poll ngày cũ trong `LookbackDays`.
4. Khi file ngày cũ caught-up và size/last-write stable qua `OldDayStablePeriod`, chuyển cold.
5. Không xóa checkpoint ngày cũ.

Initial values đề xuất:

```text
LookbackDays       = 1
OldDayStablePeriod = 10 minutes
```

Các giá trị phải benchmark/điều chỉnh theo cách Antenna rotate và topology mirror thực tế.

## 15. File mới và thay đổi số lượng FileId

### Trường hợp thêm FileId 21 trong DB

```text
DB RawDataFiles adds 21
    -> RFID.Antenna refreshes writers
    -> creates/appends File_21.txt
    -> Worker discovery sees File_21.txt
    -> creates SourceFileKey/checkpoint
    -> starts from configured initial-position policy
```

Worker không cần restart nếu file discovery động.

### Trường hợp xóa/deactivate FileId

- Worker không xóa history/checkpoint.
- File không còn thay đổi sẽ chuyển cold.
- Expected inventory reconciliation có thể cảnh báo config drift.
- Không xóa source file.

### Initial position cho file mới

Chính sách cần explicit:

| Policy | Behavior | Use case |
|---|---|---|
| `Beginning` | Position 0 | Muốn ingest toàn bộ file/ngày |
| `End` | Position = current length | Chỉ lấy event mới sau deploy |
| `ConfiguredBackfill` | Scan các ngày được duyệt | Migration/UAT backfill |

Không tự chọn `End` vì có thể bỏ data; không tự chọn `Beginning` ở production vì có thể tạo backlog lớn. UAT/operator phải chốt.

## 16. Failure và anomaly handling

### 16.1. Partial record

Trạng thái bình thường khi Antenna đang ghi. Không log error mỗi poll. Chỉ metric/gauge partial bytes/age.

Nếu file ngày cũ đã stable lâu nhưng vẫn còn partial tail, chuyển degraded và ghi diagnostic để operator xem xét.

### 16.2. Malformed complete record

Record có `e(0)` nhưng parser không xử lý được:

1. Tạo deterministic `failureId`.
2. Lưu raw bytes/text, offsets và parser error vào `ingestion_failures`.
3. Mongo confirmed.
4. Advance checkpoint qua record đó.

Không để một malformed record chặn vĩnh viễn toàn bộ file.

### 16.3. File truncated/replaced

Nếu:

```text
current file length < checkpoint position
```

Sprint 1:

- dừng processing file đó;
- health unhealthy/degraded theo scope;
- log source/date/file/checkpoint/current length;
- không tự reset 0;
- operator chọn replay/reset policy.

### 16.4. File tạm unavailable

- Giữ checkpoint.
- Retry với exponential/capped backoff.
- File khác tiếp tục chạy.
- Health phản ánh source access issue.

### 16.5. MongoDB unavailable

- Không checkpoint.
- Retry có giới hạn/backoff.
- Scheduler có backpressure; không đọc vô hạn vào RAM.
- Khi Mongo phục hồi, đọc lại từ durable checkpoint.

## 17. Backpressure

File là durable upstream buffer tự nhiên. Worker không cần đọc trước toàn bộ data vào memory.

Khi Mongo chậm:

1. Giảm/đóng processing turn tại record chưa confirmed.
2. Giữ checkpoint.
3. Không enqueue raw records vô hạn.
4. Để bytes còn lại nằm trong source file.
5. Metrics báo ingestion lag/backlog tăng.

Nếu dùng bounded channel, channel nên chứa **file work item** hoặc batch nhỏ, không chứa toàn bộ nội dung của mọi file.

## 18. Configuration hiện tại

```json
{
  "DeviceEventHistory": {
    "Enabled": true,
    "WorkerId": "device-event-history-worker-01",
    "RawLog": {
    "PollInterval": "00:00:02",
    "ReadBufferBytes": 524288,
    "MaxRecordBytes": 1048576,
    "MaxBytesPerTurn": 2097152,
    "MaxRecordsPerTurn": 1000,
    "MaxTurnDuration": "00:00:00.250",
    "MaxConcurrentFiles": 4,
    "LookbackDays": 1,
    "StartupExistingFilePolicy": "End",
    "NewFilePolicy": "Beginning",
    "Sources": [
      {
        "SourceId": "antenna-site-a",
        "RootPath": "D:/RFID/RawData",
        "CompanyId": 2,
        "TimeZoneId": "SE Asia Standard Time",
        "FilePattern": "File_*.txt",
        "Mode": "Local",
        "RemoteBaseUrl": "",
        "Enabled": true
      }
    ]
  }
  }
}
```

Các giá trị trên phản ánh shape runtime hiện tại; connection string nằm ở `DatabaseSettings.MongoDb` và không đưa vào tài liệu.

Model hiện tại chưa có `ExpectedFileIds`; actual discovery luôn dựa trên files tồn tại. Expected inventory là extension point cho health/reconciliation sau này.

## 19. Component design

```text
RawLogFileDiscovery
    -> discover qua LocalRawLogFileDiscovery hoặc RemoteHttpRawLogFileDiscovery

FileRegistry
    -> giữ runtime state và per-file lock

FairFileScheduler
    -> bounded Channel + consumer + turn budget

RawLogTailReader
    -> đọc bytes từ checkpoint position

FileTurnProcessor
    -> gọi IRawLogTailReader + IRawLogRecordFramer

ProcessRawFileRecordHandler
    -> parser.Parse + mapper.Map

RawRecordPersistenceCoordinator
    -> history/failure write rồi checkpoint AdvanceAsync

IngestionMetrics / IngestionHealthState / LoggingScopes
    -> metrics, health và structured logs
```

### File structure liên quan

```text
src/DeviceEventHistory.Infrastructure/RfidRawLog/
|-- Configuration/{RfidRawLogOptions,AntennaSourceOptions}.cs
|-- Discovery/{RawLogFileDiscovery,LocalRawLogFileDiscovery,RemoteHttpRawLogFileDiscovery}.cs
|-- Reading/{RawLogTailReader,LocalRawLogTailReader,RemoteHttpRawLogTailReader}.cs
|-- Framing/{RawLogRecordFramer,FramedRawLogRecord}.cs
`-- Parsing/{BlockTokenizer,RfidRawRecordParser}.cs

src/DeviceEventHistory.Worker/Orchestration/
|-- SourcePollingCoordinator.cs
|-- FileRegistry.cs
|-- FairFileScheduler.cs
|-- FileTurnProcessor.cs
`-- GracefulShutdownCoordinator.cs
```

## 20. Metrics và vận hành

### Per source

- discovered file count;
- expected/missing file count;
- active/hot/warm/cold/error files;
- total bytes lag;
- oldest unprocessed source time;
- source access failures.

### Per file trong logs/diagnostics

- checkpoint position;
- current length;
- remaining bytes;
- last read time;
- last checkpoint time;
- last event/failure ID;
- partial tail bytes/age;
- retry count;
- truncation state.

Không dùng full path hoặc eventId làm metric label cardinality cao; đưa chúng vào structured logs/diagnostic state.

### Health rules ví dụ

| Condition | Health |
|---|---|
| Không có record mới nhưng source đọc được | Healthy |
| Một file tạm unavailable, files khác chạy | Degraded |
| Mongo retry ngắn | Degraded |
| Checkpoint không tiến triển dù file tăng liên tục | Degraded/Unhealthy theo threshold |
| File length nhỏ hơn checkpoint | Unhealthy cho source/file |
| Mongo unavailable quá threshold | Unhealthy |

## 21. Test matrix bắt buộc

### Framing

- terminator trong một chunk;
- terminator split qua hai chunk;
- nhiều records/chunk;
- partial tail;
- CRLF/LF;
- UTF-8 multi-byte boundary;
- record lớn hơn read buffer nhưng nhỏ hơn max;
- oversized record.

### Multi-file scheduling

- 20 files cùng append;
- một hot file và 19 low-volume files;
- thêm File_21 lúc runtime;
- một file lỗi không chặn 19 file còn lại;
- concurrency không vượt configured maximum;
- hot file không gây starvation.

### Checkpoint/recovery

- restart với 20 checkpoints;
- crash sau history write/trước checkpoint;
- Mongo unavailable;
- checkpoint CAS conflict;
- malformed complete record;
- file truncate/replace.

### Date rollover

- tạo 20 files ngày mới;
- ngày cũ còn late record;
- Worker đọc đồng thời hai ngày;
- old-day stable chuyển cold;
- restart sau midnight.

## 22. PoC nên thực hiện trước

PoC đầu tiên không cần 20 files ngay. Thứ tự:

1. Một file, một complete record.
2. Một file, partial rồi complete.
3. Một file, restart/checkpoint.
4. Hai files append xen kẽ để chứng minh cursor độc lập.
5. Hai mươi files bằng fixture writer.
6. Một hot file + mười chín files nhỏ để test fairness.
7. UAT folder thật read-only.
8. Simulator/Antenna tạo record và đối chiếu Mongo.

Chỉ sau bước 5-6 mới tune concurrency/buffer. Không suy ra production throughput từ unit test.

## 23. Acceptance criteria cho chiến thuật đọc file

- Worker tự phát hiện toàn bộ file thực tế trong folder ngày, không hard-code 20.
- Mỗi file có checkpoint độc lập theo source/date/FileId.
- Worker đọc trong khi Antenna vẫn append mà không cản writer.
- Chỉ complete record tới `e(0)` được xử lý.
- Partial record không mất và không bị đánh lỗi sớm.
- Một file nóng không làm các file còn lại starvation.
- Thêm FileId/file mới không cần restart Worker.
- Midnight không tạo khoảng mù giữa ngày cũ và ngày mới.
- History/failure confirmed trước checkpoint.
- Restart/crash không tạo duplicate history.
- Mongo/source lỗi không làm RAM tăng vô hạn.
- File truncate không tự reset âm thầm.
- Có metrics để biết file count, lag, cursor và failure.
- Test 20 files, partial, restart, Mongo failure và date rollover pass.

## 24. Quyết định cuối cùng

Worker mới **học theo thuật toán cursor/terminator/commit của RFID.Analytics**, nhưng dùng một runtime model mới:

```text
Analytics legacy
    in-memory FileReader.Position
    + one block per file per cycle
    + commit after business processing

Device Event History Sprint 1
    durable Mongo checkpoint per logical file
    + fair async scheduler
    + bounded read turn
    + complete record framing
    + history/failure persistence
    + checkpoint after confirmed persistence
    + simultaneous current/previous-day monitoring
```

Đây là phương án phù hợp cho 20 file hiện tại và vẫn mở rộng được khi số `RFID.RawDataFiles` tăng trong tương lai.

## 25. Bằng chứng source đã dùng

- `Texpo.Stw/Texpo.Stw.RFID.Antenna/AntennaCenter.Start.WriterPlay.cs`.
- `Texpo.Stw/Texpo.Stw.RFID.Antenna/Writer.cs`.
- `Texpo.Stw/Texpo.Stw.Core/Utility/ThreadFileWriter.cs`.
- `Texpo.Stw/Texpo.Stw.RFID.Analytics/AnalyticCenter.Start.WorkerPlay.cs`.
- `Texpo.Stw/Texpo.Stw.RFID.Analytics/Common/LogFileByHost/LogFileHostReader.cs`.
- `Texpo.Stw/Texpo.Stw.RFID.Analytics/Common/LogFileByHost/FileReader.cs`.
- `Texpo.Stw/Texpo.Stw.Core/Utility/IO/DiskDrive.cs`.
- `Texpo.Stw/Texpo.Stw.RFID.Analytics/Concretes/Adapter.cs`.
- `Texpo.Stw/Texpo.Stw.RFID.Analytics/AnalyticCenter.Worker.Two.Reader.cs`.
- `Texpo.Stw/Texpo.Stw.RFID.Analytics/AnalyticCenter.Worker.One.RefreshReader.cs`.

Kết luận là static source inspection. Chưa benchmark throughput trên 20 file production, chưa chạy UAT folder và chưa xác nhận physical reader trong tài liệu này.
