# Device Event History - Thiết kế hai luồng thu thập dữ liệu

## 1. Trạng thái và phạm vi tài liệu

- Trạng thái: raw-log ingestion đã được triển khai trong Sprint 1; phần AppHub/direct transport vẫn là thiết kế cho giai đoạn sau và chưa thuộc Worker hiện tại.
- Ngày khảo sát source: 2026-08-24.
- Phạm vi source RFID: `D:\texpo\server-phan-tich\G-ERP`.
- Phạm vi source ERP được đọc qua SSH ở chế độ chỉ đọc: `D:\hung-nt\Code\UA\CORE-ERP\Backend`.
- Không sửa source, không commit và không triển khai thay đổi trên ERP remote trong quá trình khảo sát.
- Schema MongoDB tham chiếu: `2026-08-22-Db-Schema.md`.

Tài liệu này trả lời hai câu hỏi:

1. Device Event History sẽ thu thập lịch sử từ raw-log của `RFID.Antenna` như thế nào?
2. Có thể bỏ đường vòng `RFID -> ERP AppHub -> Device Event History` và nhận tín hiệu trực tiếp từ RFID hay không?

## 2. Kết luận thiết kế

Có thể nhận trực tiếp từ RFID đối với các event thực sự được tạo tại `RFID.Antenna` và `RFID.Analytics`, nhưng không thể chỉ thay URL SignalR rồi subscribe trực tiếp.

Lý do:

- `RFID.Antenna` và `RFID.Analytics` hiện là SignalR client của ERP `AppHub`; hai ứng dụng này không host một Hub để Device Event History kết nối vào và subscribe.
- Một SignalR client không thể nghe lén các lời gọi mà một client khác gửi lên Hub. Muốn đi trực tiếp phải bổ sung publisher/adapter ở phía RFID hoặc bổ sung một ingress gateway mới.
- Các callback trạng thái Antenna chủ yếu được ERP chuyển tiếp gần như nguyên payload, nên dữ liệu gốc đã có ở `RFID.Antenna`.
- Các event nghiệp vụ như tag/process/carton được tạo sau khi `RFID.Analytics` xử lý. Chỉ đọc `RFID.Antenna` sẽ không có đầy đủ kết quả nghiệp vụ này.
- Các event Scanner connect/disconnect và snapshot Scanner hiện được ERP tạo hoặc enrich từ connection registry, token/session và metadata ERP. `RFID.Antenna` hoặc `RFID.Analytics` riêng lẻ không có đủ dữ liệu tương đương.

Vì vậy, phương án đề xuất là **hybrid theo event family**:

1. Giữ luồng raw-log làm nguồn bền vững cho tag read và business record đã được ghi file.
2. Giai đoạn đầu dùng ERP AppHub adapter để thu các callback Monitoring hiện có.
3. Thử nghiệm direct publisher từ `RFID.Antenna` và `RFID.Analytics` cho các event mà chúng sở hữu.
4. Không bỏ ERP adapter đối với Scanner lifecycle/snapshot cho tới khi có nguồn thay thế đã được xác nhận.
5. Cutover theo từng event family; không bật đồng thời hai nguồn ghi cùng một event nếu chưa có producer event ID dùng chung.

## 3. Kiến trúc tổng thể

### 3.1. Luồng 1 - raw-log ingestion

Chiến thuật discovery, tail nhiều file, `e(0)` framing, fairness và checkpoint của luồng này đã được hiện thực trong `D:\texpo\logging-worker\device-event-worker`. Tên class/hàm thực tế được mô tả trong `Device-Event-History-Current-Codebase.md`.

```text
RFID reader / emulator
        |
        v
RFID.Antenna
        |
        | append record + e(0)
        v
{FolderRawData}/yyyy/MM/dd/File_{FileId}.txt
        |
        v
DeviceEventHistory.Worker
        |
        +--> SourcePollingCoordinator / FairFileScheduler
        +--> FileTurnProcessor / RawLogTailReader / RawLogRecordFramer
        +--> RfidRawRecordParser / CanonicalDeviceEventMapper
        +--> RawRecordPersistenceCoordinator
        +--> Mongo stores / checkpoint sau persistence
        v
MongoDB
```

Nguyên tắc:

- Worker có checkpoint riêng, không dùng chung `FileReader.Position` của `RFID.Analytics`.
- Chỉ đọc đến record hoàn chỉnh có marker `e(0)`.
- Giữ nguyên raw text và lưu facts đã parse.
- `FileId` là khóa định tuyến raw-log, không phải `DeviceId`.
- Chỉ advance checkpoint sau khi `device_event_history` hoặc `ingestion_failures` đã ghi thành công.
- Raw-log là nguồn có thể đọc lại; phù hợp hơn SignalR cho audit và recovery.

### 3.2. Luồng 2 hiện tại - nhận lại từ ERP AppHub

```text
RFID.Antenna --------------------+
                                |
                                | PushDevice*, PushState*, ...
                                v
                         ERP AppHub 2.4.3
                                |
RFID.Analytics -----------------+--> route/group/in-memory state
                                |
                                | receive* callbacks
                                v
                    Device Event History AppHub Adapter
                                |
                                v
                       Canonical ingestion pipeline
                                |
                                v
                             MongoDB
```

AppHub adapter phải:

- Dùng `Microsoft.AspNet.SignalR.Client` 2.4.3, không dùng ASP.NET Core SignalR client cho endpoint ERP hiện tại.
- Đăng ký callback trước khi gọi `Start()`.
- Gọi `JoinMonitoring()` sau khi start thành công.
- Gọi lại `JoinMonitoring()` sau reconnect.
- Chỉ tạo raw technical envelope trong callback rồi đưa vào bounded buffer.
- Không parse nặng hoặc ghi MongoDB trực tiếp trong callback.
- Phân biệt realtime, snapshot, reconnect snapshot và unknown khi có đủ bằng chứng.
- Không coi `Start()` hoặc `JoinMonitoring()` thành công là bằng chứng đã nhận event thật.

### 3.3. Luồng 2 mục tiêu - direct RFID event publishing

```text
RFID.Antenna ---- Antenna Event Publisher -----+
                                                |
RFID.Analytics -- Analytics Event Publisher ----+--> Device Event Ingress
                                                |        |
Optional Scanner Publisher --------------------+        v
                                                Canonical ingestion pipeline
                                                         |
                                                         v
                                                      MongoDB
```

Đây là publisher chủ động từ RFID sang ingress, không phải Device Event History tự subscribe vào `RFID.Antenna` hoặc `RFID.Analytics` hiện tại.

Publisher nên tạo một source envelope tối thiểu ngay tại nơi phát:

```json
{
  "sourceEventId": "uuid-created-on-producer",
  "sourceApplication": "RFID.Antenna",
  "sourceMethod": "PushStateConnected",
  "occurredAtUtc": "2026-08-24T01:00:00Z",
  "deviceId": 101,
  "gateId": 5,
  "arguments": [
    {
      "isStart": true,
      "isConnecting": false,
      "isConnected": true,
      "deviceId": 101
    }
  ]
}
```

`sourceEventId` phải được tạo một lần tại producer và giữ nguyên qua retry. Nếu cùng một event được gửi cả ERP và direct ingress mà hai đường không dùng chung ID, hệ thống không thể deduplicate an toàn bằng payload vì các lần đọc thẻ giống nhau vẫn có thể là event hợp lệ khác nhau.

## 4. Điều source RFID đang thực sự phát

### 4.1. RFID.Antenna

`RFID.Antenna` chạy .NET Framework 4.8 và dùng `Microsoft.AspNet.SignalR.Client` 2.4.3. Nó kết nối tới `{ServerDataApi}/signalr`, tạo proxy `AppHub`, rồi gọi `JoinAnten()` và `JoinEmulator()`.

Các event có sẵn ngay tại Antenna:

| Source method | Payload thấy trong source | Ý nghĩa |
|---|---|---|
| `PushDeviceOnline` | `DeviceOnline` + optional target `connectionId` | Snapshot/trạng thái đầy đủ của Antenna device |
| `PushStateConnected` | `IsStart`, `IsConnecting`, `IsConnected`, `DeviceId` | Trạng thái reader connection |
| `PushGreenState` | `DeviceId`, `On` | Trạng thái đèn xanh |
| `PushRedState` | `DeviceId`, `On` | Trạng thái đèn đỏ |
| `PushTimeSensor` | `DeviceId`, `Timeout` | Trạng thái/thời gian sensor |
| `PushDeviceReadTag` | `DeviceId`, `TagId` hoặc `DeviceId`, `Epc` | Tag đọc trực tiếp/realtime |

`DeviceOnline` đã chứa các field như device/gate code/name, trạng thái kết nối, active và light state. Vì vậy nhóm callback Antenna có thể mirror trực tiếp sang Device Event Ingress mà không cần ERP enrich payload trong đường broadcast thông thường.

Bằng chứng source local:

- `Texpo.Stw/Texpo.Stw.RFID.Antenna/AntennaCenter.Start.SignalRClientPlay.cs`: tạo `AppHub` connection, join group và nhận request snapshot.
- `Texpo.Stw/Texpo.Stw.RFID.Antenna/Reader.cs:225`: tạo `DeviceOnline`.
- `Texpo.Stw/Texpo.Stw.RFID.Antenna/Reader.cs:246-249`: phát connection/light/sensor state.
- `Texpo.Stw/Texpo.Stw.RFID.Antenna/AntennaCenter.Start.SignalRQueuePlay.cs:35`: phát tag read realtime.

### 4.2. RFID.Analytics

`RFID.Analytics` cũng chạy .NET Framework 4.8 và dùng classic SignalR client 2.4.3. Nó kết nối ERP `AppHub`, gọi `AnalyticJoin()` và phát các kết quả sau khi đã chạy logic process:

| Source method | Dữ liệu | Ý nghĩa |
|---|---|---|
| `PushTag` | `ReportTagOnline` | Kết quả tag/process đã enrich |
| `PushTagChanged` | `tagId` | Tag/process đã thay đổi |
| `RemoveTagFromGate` | tag/object/process/gate fields | Tag bị loại khỏi gate/process state |
| `PushCartonToGate` | `gateId`, carton info | Carton và trạng thái đóng thùng |
| `PushNotifyGateCleared` | `gateId` | Gate đã được clear |
| `PushSecondsInGatePacking` | `gateId`, `seconds` | Countdown packing |

`ReportTagOnline` có dữ liệu nghiệp vụ mà Antenna chưa thể biết tại thời điểm đọc vật lý, gồm `CompanyId`, object/style/color/size, process, quantity, trạng thái flow, scanning date, user và kết quả process.

Điểm quan trọng: các event `PushTag`, `PushTagChanged`, carton và gate process không nằm trong danh sách callback `Monitoring` nêu ở yêu cầu hiện tại. ERP route chúng vào các group như `GateProcess_*`, `Tag_*` hoặc `Anten`. Nếu chỉ subscribe `JoinMonitoring()`, Device Event History sẽ không thu được đầy đủ business event từ Analytics.

Bằng chứng source local:

- `Texpo.Stw/Texpo.Stw.RFID.Analytics/AnalyticCenter.Start.SignalRClientPlay.cs:29-65`.
- `Texpo.Stw/Texpo.Stw.Business/Entities/RFID/ReportTagOnline.cs`.

## 5. ERP AppHub đang làm gì

### 5.1. Những phần chỉ route/broadcast

Trong `Core.Sites.Hubs/AppHub.Rfid.MonitoringDevice.cs`, ERP nhận các Hub invocation từ Antenna rồi chuyển sang group `Monitoring`:

```text
PushDeviceOnline  -> receiveDeviceOnline
PushStateConnected -> receiveStateConnected
PushGreenState -> receiveGreenState
PushRedState -> receiveRedState
PushTimeSensor -> receiveTimeSensor
PushDeviceReadTag -> receiveDeviceReadTag
```

Với nhóm này, ERP chủ yếu làm router. Direct publisher tại Antenna có khả năng cung cấp payload tương đương, với điều kiện giữ đúng raw arguments và timestamp tại producer.

### 5.2. Những phần ERP tạo hoặc enrich

ERP AppHub giữ `AppHub.Server` dạng process-local in-memory connection registry. Khi một Scanner/device client kết nối, ERP:

- đọc token hoặc JWT/session query;
- xác định `CompanyId`, `UserId`, session type và client device type;
- đọc `DeviceId`, `GateId` từ connection query;
- lookup `DeviceName` và `GateName` từ ERP;
- lưu `UserState` theo Hub connection ID;
- phát `receiveDeviceScanConnect` và `receiveDeviceScanDisconnect` từ lifecycle của connection.

`RequestDeviceScanInfoOnline` cũng đọc registry này để trả `receiveRequestDeviceScanInfoOnline` cho đúng requester. Những dữ liệu/semantics này không tồn tại đầy đủ trong `RFID.Antenna` hoặc `RFID.Analytics`.

ERP còn giữ `MemTag` và `MemCartons`, sau đó route event Analytics theo gate, user, tag và process group. Đây là behavior stateful/routing của ERP, không chỉ là đổi tên method.

Bằng chứng source remote:

- `Core.Sites.Hubs/AppHub.Connection.cs:13-112`.
- `Core.Sites.Hubs/AppHub.Rfid.MonitoringDevice.cs:10-62`.
- `Core.Sites.Hubs/AppHub.Rfid.cs`.
- `Core.Sites.Hubs/AppHub.UserState.cs`.

Các kết luận trên là static source inspection. Chưa thực hiện runtime callback capture hoặc physical-device validation trên ERP remote.

## 6. Phân loại danh sách method/callback Monitoring

Không nên coi toàn bộ danh sách là “các hàm ERP bắn xuống”. Bốn method đầu là command do monitoring client gọi lên Hub; các tên `receive*` là callback server gửi xuống client.

| Tên | Loại | Nguồn dữ liệu thực | Có thể bỏ ERP ngay? | Ghi chú |
|---|---|---|---|---|
| `JoinMonitoring` | Client -> Hub command | ERP group membership | Không áp dụng | Direct ingress sẽ dùng auth/subscription riêng, không cần giữ nguyên command này |
| `LeaveMonitoring` | Client -> Hub command | ERP group membership | Không áp dụng | Chỉ là quản lý subscription |
| `RequestDeviceAntenInfoOnline` | Client -> Hub command | ERP forward request đến Antenna | Có điều kiện | Direct design cần request/snapshot protocol hoặc Antenna heartbeat định kỳ |
| `RequestDeviceScanInfoOnline` | Client -> Hub command | ERP connection registry | Chưa | Cần Scanner source hoặc registry thay thế |
| `receiveDeviceOnline` | Hub -> client callback | `RFID.Antenna.DeviceOnline` | Có | Broadcast hoặc targeted snapshot; phải giữ classification |
| `receiveStateConnected` | Hub -> client callback | `RFID.Antenna.Reader` | Có | Payload có sẵn tại Antenna |
| `receiveGreenState` | Hub -> client callback | `RFID.Antenna.Reader` | Có | Payload có sẵn tại Antenna |
| `receiveRedState` | Hub -> client callback | `RFID.Antenna.Reader` | Có | Payload có sẵn tại Antenna |
| `receiveTimeSensor` | Hub -> client callback | `RFID.Antenna.Reader` | Có | Payload có sẵn tại Antenna |
| `receiveDeviceReadTag` | Hub -> client callback | `RFID.Antenna` | Có | Có cả biến thể `TagId` và `Epc`; parser phải tolerant |
| `receiveDeviceScanConnect` | Hub -> client callback | ERP `OnConnected` + metadata | Chưa | Không phải event do Antenna/Analytics phát |
| `receiveDeviceScanDisconnect` | Hub -> client callback | ERP `OnDisconnected` + registry | Chưa | Không phải event do Antenna/Analytics phát |
| `receiveClientDeviceConnected` | Hub -> client callback | `PushClientDeviceConnected(object)` | Chưa xác nhận | Chỉ tìm thấy Hub method/route; chưa tìm thấy producer trong source đã khảo sát |
| `receiveClientDeviceDisconnected` | Hub -> client callback | `PushClientDeviceDisconnected(object)` | Chưa xác nhận | Cần runtime capture và tìm source client gọi Hub method |
| `receiveRequestDeviceScanInfoOnline` | Hub -> client callback | ERP connection registry snapshot | Chưa | ERP trả targeted callback cho requester |

Kết luận coverage:

- Direct từ `RFID.Antenna`: đủ cho nhóm Antenna device state và raw tag realtime.
- Direct từ `RFID.Analytics`: có thể cung cấp business/process event phong phú hơn `Monitoring` hiện tại.
- Direct chỉ từ một trong hai ứng dụng: không đủ cho toàn bộ Device Event History.
- Direct từ cả Antenna và Analytics: vẫn thiếu Scanner/client lifecycle nếu không bổ sung Scanner publisher hoặc registry riêng.

## 7. Thiết kế direct transport

### 7.1. Ràng buộc giao thức

`RFID.Antenna` và `RFID.Analytics` đang dùng classic ASP.NET SignalR 2.4.3 trên .NET Framework 4.8. ASP.NET Core SignalR của hệ thống .NET 10 là giao thức khác và không phải drop-in replacement.

Không được thiết kế theo giả định sau:

```text
RFID classic SignalR client -> ASP.NET Core SignalR Hub .NET 10
```

mà không có PoC tương thích cụ thể.

### 7.2. Các lựa chọn

#### Lựa chọn A - Dedicated classic SignalR ingress gateway

```text
RFID net48 classic SignalR client
        |
        v
DeviceEventHistory.IngressGateway
classic ASP.NET SignalR 2.4.3 / OWIN
        |
        v
durable handoff hoặc internal ingestion API
        |
        v
.NET 10 Worker -> MongoDB
```

Ưu điểm:

- Tương thích với SignalR client hiện có.
- Thay đổi ở RFID có thể giới hạn ở việc thêm connection/publisher thứ hai.
- Có thể giữ trải nghiệm realtime.

Nhược điểm:

- Phải vận hành thêm một gateway legacy.
- SignalR vẫn best effort; muốn đảm bảo không mất event cần local outbox/durable inbox.
- Cần thiết kế auth riêng, không copy token ERP sang hệ thống mới.

#### Lựa chọn B - HTTP ingestion API từ RFID sang .NET 10

```text
RFID producer -> POST /api/ingestion/events -> .NET 10 ingress -> Worker/MongoDB
```

Ưu điểm:

- Tương thích đơn giản với ứng dụng net48.
- Có response/ack rõ ràng, dễ retry và idempotency.
- Không cần gateway classic SignalR.

Nhược điểm:

- Không phải SignalR.
- Nếu gọi đồng bộ tại reader callback có thể ảnh hưởng luồng đọc; bắt buộc phải enqueue/outbox trước khi gửi.

Đây là lựa chọn phù hợp hơn cho PoC direct đầu tiên nếu mục tiêu chính là thu history đáng tin cậy, không phải giữ nguyên giao thức SignalR.

#### Lựa chọn C - Nâng RFID publisher lên ASP.NET Core SignalR client

Chỉ chọn sau khi kiểm chứng target framework/package compatibility và có kế hoạch regression cho ứng dụng net48. Phạm vi thay đổi và rủi ro cao hơn hai lựa chọn trên.

### 7.3. Khuyến nghị

- Ngắn hạn: dùng ERP AppHub adapter cho realtime Monitoring và raw-file Worker cho history bền vững.
- PoC direct: ưu tiên HTTP ingestion + local bounded queue/outbox tại RFID.
- Nếu SignalR là yêu cầu bắt buộc: dùng dedicated classic SignalR ingress gateway, không nối classic client trực tiếp vào ASP.NET Core SignalR Hub.
- Sau PoC: cutover từng event family từ ERP adapter sang direct source.

## 8. Ranh giới reliability và duplicate

Raw-log và realtime event có reliability khác nhau:

| Nguồn | Có thể đọc lại | Khoảng mất dữ liệu chính | Vai trò đề xuất |
|---|---|---|---|
| Raw file | Có, theo checkpoint | File bị xóa/rotate sai hoặc topology không nhìn thấy file | Source of truth cho record đã ghi file |
| ERP AppHub | Không mặc định | Worker hoặc ERP mất kết nối | Operational realtime history |
| Direct SignalR | Không mặc định | Producer/ingress mất kết nối | Operational realtime history |
| Direct HTTP + outbox | Có điều kiện | Outbox/policy chưa bền vững | Direct history tốt hơn khi có ack/retry |

Không deduplicate bằng `DeviceId + TagId`, EPC hoặc payload hash đơn thuần. Một tag có thể được đọc hợp lệ nhiều lần.

Chiến lược migration:

1. Gán `sourceEventId` tại producer cho đường direct.
2. Trong shadow mode, lưu direct event vào collection/database kiểm chứng riêng hoặc đánh dấu rõ source; không trộn với history production.
3. So sánh count, payload, ordering và latency giữa ERP path và direct path.
4. Chọn một ingestion owner cho từng event family khi cutover.
5. Chỉ hợp nhất hai nguồn khi có correlation/idempotency contract đáng tin cậy.

## 9. Source adapter và event family đề xuất

```text
IEventSourceAdapter
    +-- AntennaRawFileAdapter
    +-- ErpAppHubMonitoringAdapter
    +-- DirectAntennaEventAdapter       (sau PoC)
    +-- DirectAnalyticsEventAdapter     (sau PoC)
    `-- ScannerLifecycleAdapter         (chưa chốt nguồn)
```

Mọi adapter phải tạo cùng một `RawSourceEvent` trung lập:

```text
RawSourceEvent
    eventId/sourceEventId
    sourceApplication
    sourceTransport
    sourceMethod/eventName
    receivedAtUtc
    occurredAtUtc?
    rawArguments
    connectionContext?
    fileContext?
```

Sau boundary này mới thực hiện:

```text
validate
  -> classify event family
  -> parse confirmed facts
  -> resolve company/device/gate
  -> persist history or failure
  -> update projection after history success
```

## 10. Phân chia event giữa các nguồn

| Event family | Nguồn ưu tiên V1 | Nguồn direct mục tiêu | Ghi chú |
|---|---|---|---|
| Tag raw read | Raw file | Antenna publisher | Raw file vẫn là nguồn recovery |
| Reader connection | ERP Monitoring | Antenna publisher | Direct khả thi |
| Green/red light | ERP Monitoring | Antenna publisher | Direct khả thi |
| Sensor state | ERP Monitoring | Antenna publisher | Direct khả thi |
| Device online snapshot | ERP Monitoring | Antenna snapshot/heartbeat | Cần phân biệt snapshot và activity |
| Tag/process result | Analytics/AppHub group hoặc raw business block | Analytics publisher | Không chỉ dựa vào `receiveDeviceReadTag` |
| Carton/gate progress | Analytics/AppHub group | Analytics publisher | Không nằm trong Monitoring callback list |
| Scanner connect/disconnect | ERP Monitoring | Scanner publisher hoặc registry mới | Chưa thể bỏ ERP |
| Scanner online snapshot | ERP registry | Scanner registry mới | Chưa thể bỏ ERP |
| Client device lifecycle | ERP Monitoring | Chưa xác định | Cần tìm producer và capture payload |

## 11. Kế hoạch PoC đề xuất

### Bước 1 - AppHub callback catalog

- Kết nối AppHub UAT bằng service identity.
- Subscribe allowlist trước `Start()` và gọi `JoinMonitoring()`.
- Capture raw arguments đã redaction cho từng callback.
- Xác nhận argument count, casing, nullability, timestamp và payload variants.
- Đặc biệt kiểm chứng `receiveClientDeviceConnected/Disconnected` vì static source chưa cho biết producer.

### Bước 2 - Direct Antenna publisher

- Chọn một event ít rủi ro: `PushStateConnected` hoặc `PushGreenState`.
- Mirror event sang direct ingress bằng queue, không gửi đồng bộ trong hardware callback.
- Gắn `sourceEventId`, producer timestamp, device ID và raw arguments.
- So sánh payload/latency với callback ERP tương ứng.
- Sau đó thử `PushDeviceReadTag`, nhưng không dùng kết quả này thay cho kiểm chứng raw-file/Analytics.

### Bước 3 - Direct Analytics publisher

- Mirror `PushTag` và một event gate/carton.
- Kiểm tra các field business như company, process, quantity, scanning date và user.
- So sánh với raw-log để xác định dữ liệu nào chỉ xuất hiện sau Analytics.

### Bước 4 - Scanner gap analysis

- Xác định ứng dụng nào gọi `PushClientDeviceConnected/Disconnected`.
- Capture payload Scanner connect/disconnect và snapshot ở UAT.
- Quyết định giữ ERP adapter hay xây Scanner Registry/Publisher riêng.

### Bước 5 - Shadow run và cutover

- Chạy direct path ở shadow mode.
- So sánh event count, missing rate, duplicate rate, ordering và latency.
- Test disconnect/reconnect, producer restart và ingress unavailable.
- Cutover theo event family sau khi có acceptance evidence.

## 12. Tiêu chí chấp nhận direct path

Một event family chỉ được coi là có thể bỏ ERP khi:

- Xác định được producer thực sự và exact payload contract.
- Direct source có đủ `CompanyId` hoặc có cách resolve tenant đáng tin cậy.
- Có `DeviceId`/`GateId` canonical và không dùng `FileId` thay thế.
- Có producer event ID hoặc idempotency contract ổn định.
- Không block reader/Analytics processing khi ingress chậm hoặc unavailable.
- Có retry/backpressure/health signal và không silently drop.
- Snapshot và realtime được phân loại đúng.
- Kết quả shadow run khớp event count/payload trong ngưỡng đã thống nhất.
- Đã test reconnect và restart.
- MongoDB history/failure và projection giữ đúng ordering.
- Có bằng chứng UAT; physical-device acceptance được ghi nhận riêng.

## 13. Các quyết định còn mở

- Direct ingress bắt buộc dùng SignalR hay có thể dùng HTTP + outbox?
- Có chấp nhận vận hành classic SignalR ingress gateway hay không?
- Event family nào được chọn cho direct PoC đầu tiên?
- Có được phép thay đổi `RFID.Antenna` và `RFID.Analytics` để thêm publisher thứ hai không?
- Nguồn chính thức để resolve `DeviceId/GateId -> CompanyId` là gì?
- Ứng dụng nào đang gọi `PushClientDeviceConnected/Disconnected`?
- Scanner lifecycle sẽ tiếp tục đi qua ERP hay có publisher/registry mới?
- Có cần lưu cả raw-file event và Analytics business event khi chúng liên quan cùng một tag/process không? Nếu có, correlation key là gì?
- Direct event có cần local durable outbox hay chỉ best effort có quan sát?

## 14. Quyết định kiến trúc đề xuất cho Phase 1

```text
Phase 1
    Raw file Worker
        -> tag/business raw history có checkpoint

    ERP AppHub Monitoring Adapter
        -> Antenna state + Scanner lifecycle/snapshot

    Runtime discovery
        -> xác nhận payload thực tế

    Direct PoC
        -> một Antenna state event
        -> một Analytics business event

    Chưa bỏ ERP adapter
        -> cho tới khi Scanner gap và duplicate/correlation được giải quyết
```

Thiết kế này cho phép Device Event History triển khai sớm mà không phụ thuộc vào việc sửa ERP, đồng thời tạo đường tiến hóa để lấy dữ liệu trực tiếp từ đúng producer. Direct path được xem là một adapter mới theo từng event family, không phải thay thế toàn bộ AppHub bằng một lần chuyển đổi.
