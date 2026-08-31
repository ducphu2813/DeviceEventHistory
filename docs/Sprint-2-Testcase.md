# Sprint 2 - Test Case

## 1. Mục đích

Tài liệu này là bộ test case cho các chức năng đã triển khai của Sprint 2:

```text
ERP AppHub Monitoring
    -> classic SignalR connection
    -> callback admission
    -> bounded FIFO channel
    -> canonical V2 mapping
    -> MongoDB history hoặc ingestion failure
```

Raw-log từ Antenna vẫn chạy song song:

```text
Antenna raw-log
    -> discovery/read/parse
    -> canonical persistence
    -> MongoDB history hoặc ingestion failure
    -> checkpoint sau persistence
```

Mục tiêu của tester là xác nhận:

- Worker kết nối đúng ERP AppHub Training, dùng đúng hub `AppHub` và group `Monitoring`;
- callback được nhận, giữ đúng thứ tự arguments, hash/size và được đưa vào bounded channel;
- event AppHub được ghi theo canonical MongoDB Schema V2;
- event không thể canonicalize được đi vào `ingestion_failures` với đầy đủ evidence;
- raw-log Antenna vẫn hoạt động khi AppHub lỗi và ngược lại;
- reconnect/rejoin, backpressure, retry, shutdown và health/telemetry có tín hiệu quan sát được;
- không lưu credential hoặc các field nhạy cảm của `UserState`.

### 1.1. Nguyên tắc thực hiện manual runtime test

Toàn bộ testcase trong tài liệu này được thực hiện bằng cách khởi chạy Worker thật và quan sát behavior runtime. Tester không cần chạy project Unit Test, Integration Test hoặc Architecture Test để kết luận các testcase dưới đây.

Mỗi testcase chỉ sử dụng các thao tác sau:

- chỉnh `appsettings.Development.json` local;
- khởi động/dừng Worker;
- chạy giả lập của Antenna và Scanner;
- thao tác được ERP Training hoặc network/MongoDB test cho phép;
- kiểm tra log Worker và query MongoDB.

Các chi tiết nội bộ như thứ tự gọi method hoặc implementation class chỉ được đánh giá thông qua kết quả runtime quan sát được. Nếu môi trường Training không hỗ trợ tạo một payload/variant hoặc callback cụ thể, tester ghi `BLOCKED`/`N-A` và lý do, không tự thay bằng unit test.

## 2. Phạm vi và giới hạn cần biết trước khi test

### 2.1. Trong phạm vi

- Configuration, validation và secret injection.
- Classic ASP.NET SignalR client 2.4.3.
- AppHub callback registration trước `Start()` và `JoinMonitoring()` sau khi connect.
- 11 callback thuộc group `Monitoring`.
- Tenant resolution theo `CompanyId`.
- Canonical event/failure V2.
- MongoDB collections, schema validator và indexes.
- Bounded channel, FIFO consumer, saturation và shutdown drain.
- Reconnect/rejoin, source isolation, health state và telemetry.
- Raw-log regression và checkpoint semantics.

### 2.2. Không được coi là lỗi của Worker trong Sprint 2

- Callback không phát sinh từ ERP nếu producer/ERP không tạo ra event.
- Payload opaque của ERP có field/casing khác tài liệu khi chưa có fixture hoặc contract được duyệt.
- Event bị bỏ trong các khoảng best-effort đã được thiết kế: Worker chưa connect/join, admission timeout, process crash hoặc shutdown drain timeout.
- Việc không có event mới trong khi connection vẫn ở trạng thái `Running`.
- Không có HTTP `/health` hoặc exporter metrics: hiện tại Worker đã có health check/telemetry nội bộ nhưng chưa expose HTTP endpoint/vendor exporter.

### 2.3. Hành vi của callback opaque

Tám callback sau đây hiện chưa có exact wire contract đầy đủ từ ERP:

```text
receiveDeviceOnline
receiveStateConnected
receiveGreenState
receiveRedState
receiveTimeSensor
receiveDeviceReadTag
receiveClientDeviceConnected
receiveClientDeviceDisconnected
```

Vì vậy, expected result hiện tại là Worker phải giữ raw arguments sau redaction, map đúng `source.eventName`/`category`, đặt trạng thái `unmapped` kèm warning contract chưa xác nhận nếu chưa có fixture business được duyệt. Tester không được yêu cầu Worker tự suy đoán các field như `DeviceId`, `TagId`, state hoặc timestamp từ payload opaque.

Ba callback dùng payload `UserState` đã có mapping typed:

```text
receiveDeviceScanConnect
receiveDeviceScanDisconnect
receiveRequestDeviceScanInfoOnline
```

Ba callback này được kiểm tra thêm các field device/scanner/user, enum, privacy và phân biệt activity/snapshot.

## 3. Test environment

### 3.1. Thành phần cần chuẩn bị

| Thành phần | Yêu cầu |
|---|---|
| Worker | Worker executable/source version cần test; chạy trực tiếp với .NET runtime đúng theo project |
| ERP AppHub Training | Training endpoint được cấu hình trong `appsettings.Development.json` |
| MongoDB | Instance local/container đang chạy tại `localhost:27017` với database test |
| Antenna | Chạy giả lập của Antenna để tạo raw-log |
| Scanner | Chạy giả lập của Scanner để tạo callback Scanner/AppHub |
| Credential | Token/JWT test do ERP hoặc security team cấp, có quyền kết nối AppHub Monitoring |
| Công cụ kiểm tra | Log Worker, `mongosh` hoặc MongoDB Compass, công cụ xem process/metrics nội bộ nếu có |

Không commit token, connection string production, dữ liệu cá nhân hoặc raw payload chưa redaction vào repository/test evidence.

### 3.2. Cấu hình Development

Tạo hoặc cập nhật file:

```text
src/DeviceEventHistory.Worker/appsettings.Development.json
```

Cấu hình theo đúng cấu trúc `appsettings.Example.json`. Nội dung mẫu cho Training:

```json
{
  "DeviceEventHistory": {
    "Enabled": true,
    "WorkerId": "device-event-history-worker-test-01",
    "RawLog": {
      "PollInterval": "00:00:02",
      "ReadBufferBytes": 524288,
      "MaxRecordBytes": 1048576,
      "LookbackDays": 1,
      "MaxConcurrentFiles": 4,
      "MaxBytesPerTurn": 2097152,
      "MaxRecordsPerTurn": 1000,
      "MaxTurnDuration": "00:00:00.250",
      "StartupExistingFilePolicy": "Beginning",
      "NewFilePolicy": "Beginning",
      "Sources": [
        {
          "SourceId": "antenna-site-ua",
          "Mode": "RemoteHttp",
          "RootPath": "",
          "RemoteBaseUrl": "<antenna-raw-log-url>",
          "CompanyId": 2,
          "TimeZoneId": "SE Asia Standard Time",
          "FilePattern": "File_*.txt",
          "Enabled": true
        }
      ]
    },
    "AppHub": {
      "Enabled": true,
      "Sources": [
        {
          "SourceId": "erp-apphub-ua",
          "Endpoint": "<training-apphub-url>/signalr",
          "HubName": "AppHub",
          "CompanyId": null,
          "DedicatedSingleTenant": false,
          "ChannelCapacity": 5000,
          "EnqueueTimeout": "00:00:00.100",
          "ReconnectMinDelay": "00:00:01",
          "ReconnectMaxDelay": "00:00:30",
          "EnabledEvents": [
            "receiveDeviceOnline",
            "receiveStateConnected",
            "receiveGreenState",
            "receiveRedState",
            "receiveTimeSensor",
            "receiveDeviceReadTag",
            "receiveDeviceScanConnect",
            "receiveDeviceScanDisconnect",
            "receiveClientDeviceConnected",
            "receiveClientDeviceDisconnected",
            "receiveRequestDeviceScanInfoOnline"
          ],
          "AccessToken": "<paste-approved-user-cookie-token>"
        }
      ]
    },
    "DatabaseSettings": {
      "MongoDb": {
        "ConnectionString": "mongodb://admin:admin123@localhost:27017/?authSource=admin",
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
      "ShutdownTimeout": "00:00:30",
      "MaxRawPayloadBytes": 1048576
    },
    "Observability": {
      "MongoFailureUnhealthyThreshold": 3,
      "SourceFailureUnhealthyThreshold": 3,
      "ProgressStaleAfter": "00:05:00"
    }
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "DeviceEventHistory": "Debug",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

Lưu ý:

1. `Endpoint` phải là URL đầy đủ tới SignalR endpoint, tức Training host đã được cấp cộng `/signalr`. Không đặt token trong URL. Giá trị endpoint thật chỉ đặt trong file Development local.
2. `HubName` là tên hub thực tế mà client proxy gọi, không phải tên tùy ý để mô tả cấu hình. Với ERP hiện tại phải là `AppHub`.
3. `CompanyId: null` và `DedicatedSingleTenant: false` dùng cho endpoint multi-tenant, trong đó payload phải có `CompanyId` hợp lệ. Chỉ dùng configured `CompanyId` làm fallback khi endpoint được xác nhận single-tenant và đặt `DedicatedSingleTenant: true`.
4. `AccessToken` là giá trị UserCookie token trực tiếp cho Development local. Giá trị thật phải được ERP/security team cấp và chỉ được nhập vào file Development bị ignore; không ghi vào tài liệu hoặc commit.
5. Nếu dùng JWT thay cho UserCookie token, dùng `TokenJwt` trực tiếp thay cho `AccessToken`. Khi cả hai cùng có giá trị, UserCookie token được ưu tiên.
6. Chỉ bật callback mà ERP Training thực sự route tới group `Monitoring` và payload đã được phép lưu. Có thể bắt đầu với `receiveDeviceOnline`, `receiveStateConnected`, sau đó mở rộng đủ 11 callback khi simulator/ERP sẵn sàng.
7. `RemoteBaseUrl`, MongoDB connection string và `WorkerId` ở trên là cấu hình test mẫu. Tester phải xác nhận lại địa chỉ Antenna/MongoDB trước khi chạy.

### 3.3. Cấu hình credential trực tiếp

Không gửi token qua chat, ghi vào log hoặc commit vào repository. Vì đây là file Development local đã được `.gitignore` loại trừ, tester nhập credential trực tiếp vào `AppHub.Sources[0]` trong `appsettings.Development.json`:

```json
{
  "AccessToken": "<token-test-do-ERP-cap>",
  "TokenJwt": ""
}
```

Hoặc nếu dùng JWT:

```json
{
  "AccessToken": "",
  "TokenJwt": "<jwt-test-do-ERP-cap>"
}
```

Chạy Worker với environment `Development`:

```powershell
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project src/DeviceEventHistory.Worker/DeviceEventHistory.Worker.csproj
```

Environment variable vẫn được hỗ trợ như phương án thay thế cho deployment không muốn lưu secret trong file; khi dùng phương án này, cấu hình tên biến tương ứng ở `AccessTokenEnvironmentVariable` hoặc `TokenJwtEnvironmentVariable`.

### 3.4. Startup checklist

Trước mỗi test run:

- [ ] ERP Training endpoint truy cập được từ máy chạy Worker.
- [ ] Credential còn hạn và được cấp quyền đúng.
- [ ] MongoDB đang chạy, user/password test hợp lệ.
- [ ] Antenna simulator và Scanner simulator đã sẵn sàng.
- [ ] `SourceId` của RawLog và AppHub không trùng nhau.
- [ ] `EnabledEvents` dùng đúng casing và không trùng phần tử.
- [ ] `CompanyId` trong dữ liệu simulator khớp tenant được test.
- [ ] Đã ghi lại thời điểm bắt đầu test và `WorkerId`.
- [ ] Các collection test đã được xác định; không trộn evidence với dữ liệu UAT khác nếu không được phép.

### 3.5. Cách chạy và thu thập evidence

Chạy Worker:

```powershell
dotnet run --project src/DeviceEventHistory.Worker/DeviceEventHistory.Worker.csproj
```

Log cần lưu cho mỗi test:

- `configuration validated`;
- AppHub source connected/disconnected/reconnect scheduled;
- callback admitted/dropped;
- mapping result và persistence result;
- Mongo retry/failure nếu có;
- channel drain/shutdown;
- không lưu token hoặc full raw payload vào evidence.

Các truy vấn MongoDB tham khảo:

```javascript
use device_event_history

db.device_event_history.find({
  sourceKind: "erp_apphub",
  "source.sourceId": "erp-apphub-ua"
}).sort({receivedAtUtc: -1}).limit(20).pretty()

db.ingestion_failures.find({
  sourceKind: "erp_apphub",
  "source.sourceId": "erp-apphub-ua"
}).sort({receivedAtUtc: -1}).limit(20).pretty()

db.device_event_history.aggregate([
  {$match: {sourceKind: "erp_apphub", "source.sourceId": "erp-apphub-ua"}},
  {$group: {_id: "$source.eventName", count: {$sum: 1}}},
  {$sort: {_id: 1}}
])

db.ingestion_checkpoints.countDocuments({sourceKind: "erp_apphub"})

db.device_event_history.getIndexes()
db.ingestion_failures.getIndexes()
db.ingestion_checkpoints.getIndexes()
```

## 4. Quy ước kết quả

| Ký hiệu | Ý nghĩa |
|---|---|
| PASS | Kết quả thực tế khớp toàn bộ expected result |
| FAIL | Có ít nhất một expected result không đạt |
| BLOCKED | Không thể thực hiện do ERP/credential/Mongo/simulator chưa sẵn sàng |
| N/A | Case được loại khỏi scope với lý do được tester ghi rõ |

Với mỗi case, tester ghi tối thiểu: `Test case ID`, thời điểm, environment, `WorkerId`, dữ liệu/callback đã dùng, actual result, log/query evidence, status và defect ID nếu FAIL.

## 5. Test cases - Configuration và startup

### TC-CFG-001 - Startup với cấu hình Training hợp lệ

**Mục đích:** Xác nhận Worker bind và validate cấu hình đầy đủ.

**Tiền điều kiện:** MongoDB, credential, AppHub Training và RawLog endpoint sẵn sàng; cấu hình theo mục 3.2.

**Các bước:**

1. Điền trực tiếp `AccessToken` hoặc `TokenJwt` trong `appsettings.Development.json`.
2. Chạy Worker với `DOTNET_ENVIRONMENT=Development`.
3. Theo dõi startup log và thời điểm Worker bắt đầu polling/connect.

**Kết quả mong đợi:**

- Worker khởi động thành công, không có `OptionsValidationException`.
- Có log `configuration validated`.
- Log chỉ thể hiện host, source ID, số callback và `credential=true/false`; không có token/full query string.
- Mongo collections/indexes được kiểm tra hoặc khởi tạo thành công.
- RawLog và AppHub được khởi động độc lập.

### TC-CFG-002 - Thiếu credential

**Mục đích:** Xác nhận Worker không connect anonymous và không chạy giả trạng thái healthy.

**Các bước:**

1. Xóa `DEVICE_EVENT_HISTORY_APPHUB_TOKEN` khỏi process environment.
2. Giữ AppHub enabled và chạy Worker.

**Kết quả mong đợi:**

- Configuration summary thể hiện credential chưa configured.
- AppHub không tạo connection thành công/không vào trạng thái Running.
- Log ghi lỗi credential theo message ổn định, không ghi giá trị secret.
- RawLog vẫn có thể tiếp tục hoạt động nếu Mongo và Antenna endpoint hợp lệ.
- Nếu Worker fail fast theo validation/runtime policy, failure phải được ghi rõ là AppHub credential; không được báo kết nối thành công.

### TC-CFG-003 - Token và JWT fallback

**Mục đích:** Xác nhận đúng query credential được chọn.

**Các bước:**

1. Chạy lần 1 chỉ với UserCookie token qua `AccessToken`.
2. Chạy lần 2 chỉ với JWT qua `TokenJwt`.
3. Chạy lần 3 với cả `AccessToken` và `TokenJwt` có giá trị.

**Kết quả mong đợi:**

- Lần 1 dùng query key `token`.
- Lần 2 dùng query key `tokenjwt`.
- Lần 3 ưu tiên UserCookie token theo contract.
- Không có credential trong log, raw event, Mongo document hoặc exception message.
- Credential hợp lệ cho phép connect/join; credential hết hạn/bị thu hồi phải được xem là lỗi connection/auth và được retry theo policy.

### TC-CFG-004 - Endpoint/hub/callback không hợp lệ

**Mục đích:** Xác nhận validation fail fast và message có thể chẩn đoán.

**Các bước:** Thực hiện riêng từng biến thể rồi restart Worker:

1. Endpoint thiếu scheme hoặc không phải absolute URL.
2. Endpoint có query token, fragment hoặc user-info.
3. `HubName` rỗng hoặc chứa ký tự không hợp lệ.
4. `EnabledEvents` chứa tên không nằm trong allowlist.
5. `EnabledEvents` chứa callback trùng.
6. `ReconnectMinDelay` lớn hơn `ReconnectMaxDelay`.
7. `ChannelCapacity`, `MaxRawPayloadBytes` hoặc timeout không dương.

**Kết quả mong đợi:**

- Worker không start với cấu hình invalid.
- Log/exception chỉ rõ property/source bị lỗi.
- Không có connection attempt tới ERP khi validation chưa pass.
- Các source khác không bị chạy với cấu hình nửa hợp lệ.

### TC-CFG-005 - Source ID trùng giữa RawLog và AppHub

**Các bước:** Đặt `RawLog.Sources[0].SourceId` bằng `AppHub.Sources[0].SourceId`, sau đó chạy Worker.

**Kết quả mong đợi:** Worker fail validation; không tạo channel/connection có identity mơ hồ; lỗi chỉ rõ source ID bị duplicate.

### TC-CFG-006 - Redaction startup log

**Các bước:** Chạy Worker với token hợp lệ, endpoint có path `/signalr`, bật log level `Debug`, thu thập toàn bộ startup log.

**Kết quả mong đợi:**

- Có host/source ID/event count/credential configured.
- Không có token, JWT, full auth query, Mongo password hoặc full raw payload.
- Nếu có lỗi endpoint, log không biến thành full URL chứa credential.

## 6. Test cases - Connection, join và callback registration

### TC-CONN-001 - Connect tới Training AppHub

**Mục đích:** Xác nhận đúng classic SignalR endpoint/hub.

**Các bước:**

1. Chạy Worker với credential hợp lệ.
2. Quan sát connection state và log.
3. Dùng Scanner simulator tạo một callback được bật.

**Kết quả mong đợi:**

- Client connect tới endpoint Training đã cấu hình với suffix `/signalr`.
- Proxy dùng hub `AppHub`.
- Callback được nhận chỉ sau khi connection đã start và join thành công.
- Event có `sourceKind=erp_apphub`, `source.sourceId=erp-apphub-ua`, `source.transport=classic_signalr`.

### TC-CONN-002 - Join group Monitoring

**Các bước:**

1. Restart Worker.
2. Quan sát lifecycle log/telemetry.
3. Tạo event từ Antenna/Scanner simulator sau khi Worker báo connected.

**Kết quả mong đợi:**

- Worker gọi `JoinMonitoring()` không argument sau connect.
- Worker chỉ coi source sẵn sàng sau join thành công.
- Event thuộc group Monitoring được nhận và lưu; event tạo trước khi join không được dùng để kết luận Worker phải nhận được.
- Không có join lặp vô hạn khi connection ổn định.

### TC-CONN-003 - Callback registration trước `Start()`

**Các bước:**

1. Chạy Worker thật với cấu hình callback đã bật.
2. Theo dõi log lifecycle khi Worker khởi động.
3. Đối chiếu thời điểm callback được phát từ ERP/simulator với thời điểm Worker connect/join.

**Kết quả mong đợi:** Worker start ổn định, join thành công và nhận được các callback đã bật ngay sau khi ERP/simulator phát event. Không xuất hiện lỗi callback registration; không có callback bị mất do Worker đăng ký handler sau khi connection đã start. Việc kiểm tra thứ tự method nội bộ không thuộc manual acceptance nếu không có log/trace runtime tương ứng.

### TC-CONN-004 - Sai hub name hoặc sai endpoint

**Các bước:**

1. Đổi `HubName` thành tên không tồn tại rồi chạy.
2. Khôi phục `HubName=AppHub`, đổi endpoint sang path sai rồi chạy.

**Kết quả mong đợi:** Connection/join thất bại có log chẩn đoán; không lưu event giả; Worker thực hiện reconnect theo backoff nếu process vẫn đang chạy; không busy-loop.

### TC-CONN-005 - AppHub disabled không ảnh hưởng RawLog

**Các bước:**

1. Đặt `DeviceEventHistory:AppHub:Enabled=false`.
2. Giữ RawLog enabled.
3. Chạy giả lập của Antenna và Scanner.

**Kết quả mong đợi:**

- Worker không tạo AppHub connection/connection attempt.
- Không có AppHub reconnect loop.
- Raw-log từ Antenna vẫn được đọc, parse, persist và advance checkpoint.
- Scanner callback không được kỳ vọng lưu khi AppHub disabled.

### TC-CONN-006 - Nhiều source độc lập

**Điều kiện:** Có từ hai AppHub source hợp lệ được khai báo trong `appsettings.Development.json`.

**Các bước:** Chạy hai source với SourceId/generation/channel/tenant khác nhau, tạo callback trên từng source.

**Kết quả mong đợi:** Event không bị trộn `SourceId`, connection generation, sequence, channel hoặc CompanyId; lỗi/reconnect một source không dừng source còn lại.

## 7. Test cases - Reconnect và rejoin

### TC-RECON-001 - Reconnect sau mất mạng

**Các bước:**

1. Để Worker connected/running.
2. Tạm ngắt đường tới Training endpoint hoặc dùng cách được tester/network team phê duyệt để làm connection disconnect.
3. Khôi phục kết nối.
4. Tạo event mới từ simulator.

**Kết quả mong đợi:**

- Worker ghi nhận disconnected/reconnect.
- Reconnect delay nằm trong configured min/max, có exponential backoff và jitter.
- Connection mới có generation mới.
- Callback được đăng ký lại đúng một lần trên connection mới.
- `JoinMonitoring()` được gọi lại sau reconnect.
- Event sau khi rejoin được nhận/lưu.
- Event trong khoảng disconnected có thể mất theo best-effort; không được đánh dấu là đã persist nếu không có evidence.

### TC-RECON-002 - Nhiều lifecycle signal liên tiếp

**Các bước:** Tạo disconnect/reconnect nhanh bằng thao tác trên endpoint Training hoặc theo quy trình network được tester phê duyệt.

**Kết quả mong đợi:** Các lệnh join được serialize; không có duplicate concurrent `JoinMonitoring()`; không có callback registration trùng; source cuối cùng ở trạng thái ổn định sau khi connection hồi phục.

### TC-RECON-003 - Credential hết hạn khi reconnect

**Các bước:** Làm credential bị hết hạn/thu hồi theo quy trình của ERP, gây reconnect rồi cấp credential mới và restart/refresh theo quy trình được hỗ trợ.

**Kết quả mong đợi:**

- Reconnect với credential invalid không bị báo Running.
- Log có lỗi auth/connection đã redaction.
- Khi credential hợp lệ lại, Worker connect/join và nhận event bình thường.
- Không có token cũ trong Mongo hoặc log.

### TC-RECON-004 - Event identity sau reconnect

**Các bước:** Ghi nhận event trước disconnect và event tương đương sau reconnect; lưu generation/sequence/eventId.

**Kết quả mong đợi:**

- `source.connectionGeneration` mới sau reconnect.
- `receiveSequence` được hiểu trong từng generation, không coi là global ordering.
- Worker không deduplicate hai event chỉ vì payload/device/tag giống nhau nếu không có producer event ID ổn định.

## 8. Test cases - Callback và canonical mapping

### 8.1. Bảng expected chung

| Callback | Category | Delivery kind mặc định | Expected hiện tại |
|---|---|---|---|
| `receiveDeviceOnline` | `device_online` | `snapshot_candidate` | Opaque raw + warning/unmapped nếu chưa có contract |
| `receiveStateConnected` | `device_connection` | `realtime` | Opaque raw + warning/unmapped nếu chưa có contract |
| `receiveGreenState` | `device_control_state` | `realtime` | Opaque raw + warning/unmapped nếu chưa có contract |
| `receiveRedState` | `device_control_state` | `realtime` | Opaque raw + warning/unmapped nếu chưa có contract |
| `receiveTimeSensor` | `device_sensor_state` | `realtime` | Opaque raw + warning/unmapped nếu chưa có contract |
| `receiveDeviceReadTag` | `tag_read` | `realtime` | Opaque raw + warning/unmapped nếu chưa có contract |
| `receiveDeviceScanConnect` | `scanner_connection` | `realtime` | Typed scanner mapping, status `connected` |
| `receiveDeviceScanDisconnect` | `scanner_connection` | `realtime` | Typed scanner mapping, status `disconnected` |
| `receiveClientDeviceConnected` | `client_device_connection` | `realtime` | Opaque raw + warning/unmapped nếu chưa có contract |
| `receiveClientDeviceDisconnected` | `client_device_connection` | `realtime` | Opaque raw + warning/unmapped nếu chưa có contract |
| `receiveRequestDeviceScanInfoOnline` | `device_snapshot` | `snapshot` | Typed scanner snapshot mapping |

Mỗi callback bên dưới phải được thực hiện riêng ít nhất một lần với payload hợp lệ. Tester phải lưu lại timestamp phát event, callback name, CompanyId, device/gate ID nếu có và query Mongo tương ứng.

### TC-MAP-001 - `receiveDeviceOnline`

**Các bước:** Bật callback, chạy giả lập phù hợp tạo event online, chờ consumer persist, query history theo event name.

**Kết quả mong đợi:** Có đúng event `source.eventName=receiveDeviceOnline`, `category=device_online`, `sourceKind=erp_apphub`; `deliveryKind=snapshot_candidate`; `receivedAtUtc`, `timelineAtUtc`, `persistedAtUtc` hợp lệ; raw arguments giữ đúng representation sau redaction/hash/size; không tự gắn `snapshot=true` hoặc tự suy đoán device facts khi chưa có correlation/contract.

### TC-MAP-002 - `receiveStateConnected`

**Các bước:** Tạo state-connected event từ ERP/simulator.

**Kết quả mong đợi:** Có history `category=device_connection` và event name chính xác; opaque payload được giữ nguyên thứ tự sau redaction; không tự coi callback là command acknowledgement; nếu chưa có contract, parse status là `unmapped` với warning phù hợp.

### TC-MAP-003 - `receiveGreenState` và `receiveRedState`

**Các bước:** Tạo lần lượt green và red event.

**Kết quả mong đợi:**

- Green có `category=device_control_state`, event name `receiveGreenState`.
- Red có `category=device_control_state`, event name `receiveRedState`.
- Không đổi chéo hai event.
- Không ghi `green_light`/`red_light` vào facts nếu opaque contract chưa xác nhận field tương ứng.
- Raw payload và warning/unmapped status vẫn có.

### TC-MAP-004 - `receiveTimeSensor`

**Các bước:** Tạo event sensor từ simulator/ERP.

**Kết quả mong đợi:** Event có `category=device_sensor_state`, đúng event name, raw payload/hash/size; không tự suy luận đơn vị timeout hoặc timestamp business khi chưa được ERP xác nhận; variant không map được phải đi failure hoặc unmapped theo loại lỗi.

### TC-MAP-005 - `receiveDeviceReadTag`

**Các bước:** Tạo event read-tag bằng simulator, dùng payload có CompanyId hợp lệ.

**Kết quả mong đợi:** Event có `category=tag_read`, đúng event name, tenant đúng payload; không tự tạo EPC khi payload chỉ có TagId; không deduplicate các tag read chỉ vì DeviceId/TagId/payload giống nhau; raw evidence được lưu.

### TC-MAP-006 - `receiveDeviceScanConnect`

**Các bước:** Chạy giả lập của Scanner để tạo scanner connect với `UserState` có các field hợp lệ: `CompanyId`, `UserId`, `DeviceId`, `DeviceName`, `GateId`, `GateName`, `SessionType`, `DeviceType`, `ConnectionId`, `DateConnected`.

**Kết quả mong đợi:**

- History V2 có `category=scanner_connection` và `source.eventName=receiveDeviceScanConnect`.
- `facts.connection.status=connected` và `isSourceConnected=true`.
- `facts.scanner.sessionType`, `deviceType`, `facts.user.userId` đúng wire value.
- Device/gate identity/display fields được map nếu có.
- `ConnectionId` không xuất hiện raw; `facts.scanner.connectionIdHash` là SHA-256 UTF-8.
- UserName, Avatar, WindowFocus, ModuleName, Browser, Ip, SessionId, UserId2 và `WantFollowForViewUserState` bị drop khỏi stored raw representation.
- `DateConnected` được giữ theo policy source time chưa trusted UTC; có warning source time phù hợp.

### TC-MAP-007 - `receiveDeviceScanDisconnect`

**Các bước:** Sau TC-MAP-006, chạy giả lập của Scanner để tạo disconnect cùng device/user.

**Kết quả mong đợi:**

- History có `category=scanner_connection`, event name disconnect.
- `facts.connection.status=disconnected` và `isSourceConnected=false`.
- Scanner/user/device/privacy mapping giống policy của connect.
- Không update/ghi đè một activity connect trước đó; đây là một history observation mới.

### TC-MAP-008 - `receiveRequestDeviceScanInfoOnline`

**Các bước:** Dùng cơ chế được ERP/simulator hỗ trợ để tạo response snapshot scanner info.

**Kết quả mong đợi:**

- History có `category=device_snapshot`, `deliveryKind=snapshot`.
- `facts.connection.status=unknown`; không map thành connected activity.
- Scanner/device fields hợp lệ được map; privacy policy vẫn áp dụng.
- Snapshot không được coi là request correlation của Worker nếu Worker không phải requester.
- Snapshot cũ không được dùng để suy luận hay ghi đè current state trong Sprint 2 vì projection chưa thuộc phạm vi.

### TC-MAP-009 - Client-device callbacks

**Các bước:** Khi ERP/simulator có thể phát, test lần lượt `receiveClientDeviceConnected` và `receiveClientDeviceDisconnected`.

**Kết quả mong đợi:**

- Hai event có category `client_device_connection` và đúng event name.
- Với payload opaque chưa được duyệt, lưu raw + warning/unmapped, không bịa facts.
- Nếu ERP không phát callback, ghi `BLOCKED/N-A` kèm evidence producer chưa sẵn sàng; không đánh dấu lỗi Worker chỉ vì không có event.

### TC-MAP-010 - Callback arguments giữ đúng thứ tự

**Các bước:** Dùng payload/runtime action của ERP hoặc simulator có thể nhận diện vị trí arguments, phát event qua Worker thật và đọc `rawPayload.arguments` trong MongoDB.

**Kết quả mong đợi:** `rawPayload.arguments` giữ đúng số lượng và thứ tự arguments sau approved redaction; không serialize lại nhiều lần làm thay đổi nội dung ngoài formatting được quy định; `sizeBytes` và `sha256` tương ứng với representation đã lưu.

### TC-MAP-011 - CompanyId từ payload

**Các bước:** Gửi cùng callback với `CompanyId=2`, sau đó `CompanyId` của tenant khác đã được cấp phép.

**Kết quả mong đợi:** Mỗi history document dùng đúng CompanyId từ payload; không dùng một tenant cố định cho mọi event; index/query tenant trả đúng partition logic.

## 9. Test cases - Tenant, time và malformed payload

### TC-DATA-001 - Dedicated single-tenant fallback

**Các bước:**

1. Dùng source có `CompanyId=2`, `DedicatedSingleTenant=true`.
2. Gửi payload hợp lệ nhưng thiếu CompanyId.

**Kết quả mong đợi:** Event có `companyId=2`; fallback chỉ hoạt động vì source được khai báo dedicated; source context vẫn giữ đúng SourceId.

### TC-DATA-002 - Multi-tenant không được fallback CompanyId

**Các bước:** Dùng cấu hình `CompanyId=null`, `DedicatedSingleTenant=false`, gửi payload thiếu CompanyId.

**Kết quả mong đợi:** Không tạo normal history; tạo `ingestion_failures` với `error.code=TENANT_UNRESOLVED`, `companyId=null`, source/raw payload đầy đủ, `retryable=false`.

### TC-DATA-003 - Tenant mismatch

**Các bước:** Cấu hình dedicated `CompanyId=2`, gửi payload `CompanyId=3`.

**Kết quả mong đợi:** Không ghi history normal; ghi failure với `error.code=TENANT_MISMATCH`; giữ payload và source trace; không âm thầm chọn config hoặc payload để tiếp tục.

### TC-DATA-004 - CompanyId không hợp lệ

**Các bước:** Gửi lần lượt `CompanyId=0`, số âm, string không parse được, boolean/object thay cho số.

**Kết quả mong đợi:** Event đi `ingestion_failures` với lỗi validation/tenant unresolved phù hợp; không có history document với tenant <= 0.

### TC-DATA-005 - Malformed JSON

**Các bước:** Dùng ERP/simulator phát variant payload JSON hỏng hoặc payload không deserialize được nếu môi trường Training hỗ trợ.

**Kết quả mong đợi:**

- Không làm crash consumer hoặc connection loop.
- Ghi failure với stage `deserialization` và mã invalid record format tương ứng.
- Failure giữ `source.eventName`, generation/sequence, raw hash/size và received time.
- Không ghi malformed payload thành history bình thường.

### TC-DATA-006 - Thiếu object payload

**Các bước:** Gửi arguments rỗng, array rỗng hoặc argument đầu tiên không phải object.

**Kết quả mong đợi:** Event không được map thành normal AppHub facts; ghi failure validation với source/raw evidence; consumer tiếp tục nhận event kế tiếp.

### TC-DATA-007 - Scanner thiếu required DeviceId

**Các bước:** Gửi `receiveDeviceScanConnect`, disconnect hoặc snapshot với UserState thiếu `DeviceId`, `DeviceId=0` hoặc type không hợp lệ.

**Kết quả mong đợi:** Ghi `ingestion_failures` với validation error; không tạo scanner history thiếu required device identity; event tiếp theo vẫn được xử lý.

### TC-DATA-008 - Scanner optional fields và warning

**Các bước:** Gửi Scanner `UserState` có DeviceId/CompanyId nhưng thiếu từng nhóm optional: SessionType, DeviceType, UserId, ConnectionId, DateConnected, tên/gate.

**Kết quả mong đợi:**

- Nếu core identity hợp lệ, history vẫn được ghi.
- `parse.status=parsed_with_warnings`.
- Warnings phản ánh field/time thiếu; không tạo giá trị giả.
- ConnectionId thiếu không làm lưu raw ConnectionId; chỉ có warning và không có hash nếu không có source value.

### TC-DATA-009 - Time contract

**Các bước:** Test event có source occurred time hợp lệ, event không có occurred time, và Scanner có `DateConnected` local.

**Kết quả mong đợi:**

- Có occurred time đáng tin cậy: `timelineAtUtc=occurredAtUtc`, `timeBasis=occurred`.
- Không có occurred time: `occurredAtUtc=null`, `timelineAtUtc=receivedAtUtc`, `timeBasis=received`.
- `receivedAtUtc` là thời điểm Worker nhận callback; `persistedAtUtc` là thời điểm persistence.
- Không chuyển source local time thành trusted UTC nếu chưa có timezone evidence.

### TC-DATA-010 - Payload vượt MaxRawPayloadBytes

**Các bước:** Tạm đặt `MaxRawPayloadBytes` nhỏ trong môi trường test hoặc gửi payload lớn hơn giới hạn.

**Kết quả mong đợi:**

- Không silently truncate rồi ghi như history bình thường.
- Có failure oversize với hash/size/source trace; raw content xử lý theo policy bảo mật, không ghi full payload vượt giới hạn nếu policy không cho phép.
- Connection/consumer không crash; callback/event sau vẫn có thể xử lý.
- Telemetry/log có oversized signal, không có full payload.

## 10. Test cases - Persistence và MongoDB Schema V2

### TC-MONGO-001 - History document V2

**Các bước:** Tạo ít nhất một history AppHub hợp lệ và query document đầy đủ.

**Kết quả mong đợi:** Document có tối thiểu:

```text
eventId
schemaVersion=2
category
sourceKind=erp_apphub
companyId > 0
receivedAtUtc
persistedAtUtc
timelineAtUtc
timeBasis
source
rawPayload
facts
parse
ingestion
```

`source.eventName`, `source.sourceId`, `source.transport=classic_signalr`, `source.deliveryKind`, generation/sequence và raw hash/size phải đúng event đã test. Không tạo file context giả hoặc collection riêng theo callback.

### TC-MONGO-002 - Sparse facts

**Các bước:** Query một event opaque unmapped và một Scanner event typed.

**Kết quả mong đợi:**

- History opaque có `facts` object nhưng không có branch business giả.
- History Scanner chỉ có các branch facts có dữ liệu.
- Không biến branch null thành dữ liệu fake; reader vẫn đọc được các document V1/V2 nếu cùng collection.

### TC-MONGO-003 - Failure document V2

**Các bước:** Thực hiện TC-DATA-002, TC-DATA-003 hoặc TC-DATA-005 rồi query failure collection.

**Kết quả mong đợi:** Failure có `failureId`, `schemaVersion=2`, `sourceKind`, nullable `companyId`, source, rawPayload, error, received/persisted time, `retryable`, `retryCount`, `ingestion`; không tạo history normal cho cùng source event.

### TC-MONGO-004 - Failure không bị xóa khi resolution

**Các bước:** Nếu quy trình vận hành của môi trường test có thao tác resolve failure, thực hiện resolve một failure đã tạo ở các case trước.

**Kết quả mong đợi:** Document vẫn tồn tại; chỉ bổ sung/cập nhật `resolvedAtUtc` và resolution metadata theo policy; raw evidence không bị xóa.

### TC-MONGO-005 - Duplicate identity/idempotent retry

**Các bước:**

1. Gửi một event và ghi lại `eventId`.
2. Gây transient MongoDB failure trong lúc Worker đang xử lý event để Worker tự retry.
3. Query theo eventId.

**Kết quả mong đợi:**

- Retry dùng cùng event identity.
- Duplicate key được coi là idempotent success.
- Chỉ có một normal history document cho eventId đó.
- Không phát sinh eventId mới chỉ vì Mongo retry.

### TC-MONGO-006 - AppHub không tạo checkpoint

**Các bước:** Persist nhiều AppHub events, sau đó query `ingestion_checkpoints`.

**Kết quả mong đợi:** Không có checkpoint có `sourceKind=erp_apphub` hoặc source AppHub. AppHub dùng generation/sequence, không dùng file checkpoint giả.

### TC-MONGO-007 - Raw-log checkpoint sau persistence

**Các bước:** Chạy giả lập của Antenna tạo raw records; theo dõi history/failure và checkpoint.

**Kết quả mong đợi:** Raw record được persist confirmed trước khi checkpoint advance; nếu persistence fail, checkpoint giữ vị trí cũ để replay; event ID V1 của raw-log không bị recompute do Sprint 2.

### TC-MONGO-008 - Schema validator reject document V2 không hợp lệ

**Các bước:** Dùng `mongosh` hoặc MongoDB Compass insert document V2 thiếu required field hoặc có `companyId=0` vào database test.

**Kết quả mong đợi:** MongoDB reject document; Worker không coi write thất bại là ingestion success; health/retry signal được phân biệt với data failure.

### TC-MONGO-009 - V1/V2 coexistence

**Điều kiện:** Database có document raw-log V1 hợp lệ từ Sprint 1.

**Các bước:** Query đồng thời document V1 và AppHub V2; thực hiện query không phá reader hiện tại.

**Kết quả mong đợi:** V1 vẫn đọc được, không đổi `_id`/eventId; AppHub chỉ ghi V2; V2 có effective timeline/sparse facts theo contract.

### TC-MONGO-010 - Collections và indexes startup

**Các bước:** Xóa database test hoặc dùng database test mới, khởi động Worker, sau đó chạy `getIndexes()`.

**Kết quả mong đợi:**

- Ba collection history/failure/checkpoint được tạo/cập nhật validator.
- Có unique `eventId` trên history.
- Có unique `failureId` trên failure.
- Có query indexes cho company/timeline, category/timeline, source, event name, device/gate/tag nếu field tồn tại.
- Có failure code/time, resolved time và checkpoint source identity/updated time indexes.
- Chạy restart lần hai không tạo duplicate index hoặc fail do index đã tồn tại.

## 11. Test cases - Admission, FIFO, backpressure và persistence failure

### TC-QUEUE-001 - FIFO với một consumer

**Các bước:** Gửi liên tiếp một chuỗi event có sequence dễ nhận biết từ cùng generation; để consumer xử lý bình thường.

**Kết quả mong đợi:** Các event đã admission được xử lý theo thứ tự FIFO; `receiveSequence` tăng theo callback boundary; không dùng nhiều consumer gây đảo thứ tự trong một source.

### TC-QUEUE-002 - Channel bounded

**Các bước:** Dùng cấu hình test `ChannelCapacity` nhỏ, làm Mongo chậm hoặc tạm không sẵn sàng, sau đó phát event nhanh bằng simulator.

**Kết quả mong đợi:**

- Pending count không tăng vô hạn.
- Khi full, Worker chờ tối đa `EnqueueTimeout` rồi drop/saturation theo policy.
- Log/telemetry có source ID và event name, không có full payload.
- Event bị drop không được báo là persisted.
- Không có memory growth vô hạn do đổi sang unbounded queue.

### TC-QUEUE-003 - Admission fast path và timeout

**Các bước:**

1. Gửi event khi channel còn chỗ.
2. Gửi event khi channel full trong thời gian lâu hơn `EnqueueTimeout`.

**Kết quả mong đợi:**

- Trường hợp 1 được admission nhanh.
- Trường hợp 2 tăng dropped/saturation telemetry, ghi structured warning và làm health degraded theo policy.
- Worker vẫn nhận event mới sau khi channel có chỗ.

### TC-QUEUE-004 - Mongo outage và recovery

**Các bước:**

1. Để Worker nhận được event.
2. Tạm dừng MongoDB hoặc chặn network tới Mongo theo quy trình được duyệt.
3. Tạo event AppHub và raw-log.
4. Khôi phục MongoDB.
5. Tạo event mới và kiểm tra retry/backlog.

**Kết quả mong đợi:**

- Mongo failure là infrastructure/health signal, không bị ghi nhầm thành data failure nếu chưa có source mapping outcome.
- Có retry bounded theo `PersistenceRetryCount`.
- Channel vẫn bounded; saturation nếu có phải quan sát được.
- Khi Mongo hồi phục, event được nhận mới ghi bình thường; retry cùng identity không tạo duplicate.
- Raw/AppHub failure ở một flow không làm flow còn lại tự dừng ngoài giới hạn chung của Mongo.

### TC-QUEUE-005 - Mapping failure không làm dừng consumer

**Các bước:** Gửi một malformed/tenant-invalid event xen giữa hai event hợp lệ.

**Kết quả mong đợi:** Event lỗi vào failure; event trước và sau vẫn được xử lý; consumer không bị terminate bởi một data-contract error.

### TC-QUEUE-006 - Unknown callback event

**Các bước:** Đặt một callback không nằm trong allowlist vào `EnabledEvents`, sau đó restart Worker.

**Kết quả mong đợi:** Worker fail validation trước khi connect; callback không hợp lệ không được map vào category sai, không tạo history/failure giả và không làm Worker chạy với cấu hình không rõ ràng.

## 12. Test cases - Shutdown và lifecycle

### TC-SHUT-001 - Graceful shutdown khi queue rỗng

**Các bước:** Chạy Worker ổn định, không tạo backlog, gửi Ctrl+C/SIGTERM theo cách deployment sử dụng.

**Kết quả mong đợi:**

- Worker dừng reconnect/receive.
- Connection được stop/dispose.
- Channel writer complete.
- Có log shutdown/stop rõ ràng.
- Process thoát trong `ShutdownTimeout`.

### TC-SHUT-002 - Drain event còn trong channel

**Các bước:** Làm persistence chậm có kiểm soát, tạo một số event trong channel, gửi shutdown.

**Kết quả mong đợi:** Worker dừng nhận event mới, drain các event còn lại trong thời gian cho phép; event drain thành công xuất hiện trong Mongo; log số event đã xử lý/drained.

### TC-SHUT-003 - Drain timeout

**Các bước:** Làm persistence block lâu hơn `ShutdownTimeout`, tạo backlog rồi shutdown.

**Kết quả mong đợi:**

- Worker không chờ vô hạn.
- Có log drain timeout và số event còn lại.
- Processor được cancel sau timeout.
- Event còn trong memory có thể mất theo best-effort; không tạo checkpoint/record giả để che mất.
- Process vẫn thoát sạch, không treo background task.

### TC-SHUT-004 - Shutdown trong lúc reconnect backoff

**Các bước:** Làm AppHub disconnected, chờ Worker vào reconnect delay, gửi shutdown.

**Kết quả mong đợi:** Cancellation kết thúc delay sớm; không tạo thêm connection attempt sau shutdown; process thoát trong timeout.

## 13. Test cases - Health và telemetry

### TC-OBS-001 - Health khi AppHub disabled

**Các bước:** Đặt AppHub disabled và chạy Worker.

**Kết quả mong đợi:** AppHub health check trả healthy/disabled; không coi việc không có connection là unhealthy; RawLog health được đánh giá độc lập.

### TC-OBS-002 - Health khi đang connecting

**Các bước:** Dùng endpoint/credential khiến connection đang retry hoặc chưa join được.

**Kết quả mong đợi:** AppHub health state là `connecting` hoặc `degraded` theo thời điểm/threshold; không báo Running; không có event mới không tự động làm source unhealthy nếu connection vẫn Running.

### TC-OBS-003 - Health running

**Các bước:** Connect/join thành công và giữ Worker ổn định.

**Kết quả mong đợi:** AppHub source state là `running`; health check tương ứng healthy; snapshot có last successful join/connection state; không yêu cầu event phải liên tục xuất hiện.

### TC-OBS-004 - Health degraded/unhealthy sau lỗi lặp

**Các bước:** Làm connection/join fail liên tiếp ít nhất tới threshold `SourceFailureUnhealthyThreshold`.

**Kết quả mong đợi:** Health chuyển degraded rồi unhealthy theo threshold; reason nêu được AppHub unavailable/source failure; khi source hồi phục, state trở lại running/healthy theo policy.

### TC-OBS-005 - Telemetry connection lifecycle

**Các bước:** Chạy startup, disconnect/reconnect và join thành công/thất bại.

**Kết quả mong đợi:** Có counters/states cho connection attempt, connection state, reconnect và join; labels có cardinality thấp như source/event/status; không dùng event ID, device ID, connection ID hoặc raw payload làm metric label.

### TC-OBS-006 - Telemetry callback/channel/mapping

**Các bước:** Gửi event hợp lệ, event malformed và tạo saturation.

**Kết quả mong đợi:** Có signal cho callbacks received/admitted/dropped, channel depth/saturation và mapping result; mapping failure/drop không bị đếm là normal history success.

### TC-OBS-007 - Last callback age

**Các bước:** Gửi callback, sau đó dừng simulator nhưng giữ connection Running.

**Kết quả mong đợi:** Last callback age tăng theo thời gian; source vẫn healthy nếu connection không lỗi; dashboard/consumer không được tự động kết luận mất kết nối chỉ vì không có dữ liệu.

### TC-OBS-008 - Health/telemetry không làm lộ dữ liệu nhạy cảm

**Các bước:** Gửi UserState có ConnectionId/IP/SessionId/UserName và thu thập log/metrics.

**Kết quả mong đợi:** Không có raw ConnectionId, IP, token, JWT, session hoặc full payload trong log/metric label; Mongo chỉ có ConnectionId hash và field được phép persist.

> Hiện tại Worker chưa có HTTP health endpoint hoặc vendor metrics exporter. Nếu môi trường test cần gọi `/health`, case này chỉ được thực hiện sau khi deployment bổ sung endpoint; không đánh dấu bản Sprint 2 hiện tại FAIL chỉ vì endpoint chưa tồn tại.

## 14. Test cases - Raw-log regression và source isolation

### TC-RAW-001 - Antenna raw-log vẫn ingest khi AppHub đang chạy

**Các bước:** Bật cả RawLog và AppHub, chạy giả lập của Antenna và Scanner, tạo dữ liệu đồng thời.

**Kết quả mong đợi:** Raw records và AppHub events cùng vào common persistence nhưng giữ `sourceKind`, category, event name và identity riêng; AppHub không tạo checkpoint; RawLog vẫn advance checkpoint sau persistence.

### TC-RAW-002 - RawLog vẫn ingest khi AppHub connect fail

**Các bước:** Dùng AppHub endpoint/credential invalid, giữ Antenna endpoint/Mongo hợp lệ, chạy giả lập của Antenna.

**Kết quả mong đợi:** AppHub có retry/health failure; RawLog vẫn đọc/parse/persist/advance checkpoint; không có exception AppHub làm dừng raw-log scheduler.

### TC-RAW-003 - AppHub vẫn process khi RawLog source lỗi

**Các bước:** Làm RawLog endpoint unavailable, giữ AppHub Training và Mongo/credential hợp lệ, chạy giả lập của Scanner.

**Kết quả mong đợi:** RawLog có source health failure; AppHub vẫn connect/join/admit/map/persist event; không trộn failure source.

### TC-RAW-004 - Không dedupe chéo RawLog/AppHub

**Các bước:** Tạo raw event và AppHub event có cùng DeviceId/TagId/payload business nếu simulator cho phép.

**Kết quả mong đợi:** Hai observation được lưu bằng identity/source context riêng; không deduplicate chỉ bằng DeviceId, TagId, EPC, payload hash hoặc category/time.

## 15. Test cases - Security và data privacy

### TC-SEC-001 - Credential không xuất hiện trong Mongo

**Các bước:** Connect bằng token/JWT hợp lệ, query toàn bộ document mới ở history/failure/checkpoint.

**Kết quả mong đợi:** Không có token, JWT, auth query, Authorization value hoặc connection string/password trong bất kỳ field nào.

### TC-SEC-002 - UserState redaction

**Các bước:** Gửi Scanner `UserState` có đầy đủ field nhạy cảm và không nhạy cảm.

**Kết quả mong đợi:**

- Persist: CompanyId, UserId, DateConnected, SessionType, DeviceType, DeviceId/Name, GateId/Name.
- Hash: ConnectionId thành `facts.scanner.connectionIdHash`.
- Drop: UserName, Avatar, WindowFocus, ModuleName, Browser, Ip, SessionId, UserId2, WantFollowForViewUserState, raw ConnectionId.
- Stored `rawPayload.arguments` là representation sau redaction; hash/size tính trên representation đó.

### TC-SEC-003 - Opaque payload privacy gate

**Các bước:** Với mỗi opaque callback, so sánh payload thực tế với privacy classification/fixture được ERP và security team phê duyệt.

**Kết quả mong đợi:** Chỉ payload/field đã được approve mới được production-enable và persist; field chưa phân loại phải được redaction/disable hoặc ghi nhận BLOCKED, không tự động đưa vào Mongo.

## 16. Test data và evidence matrix

Tester nên duy trì bảng evidence sau trong test report:

| Nhóm | Evidence bắt buộc |
|---|---|
| Startup | Log configuration validated đã redaction |
| Connection | Endpoint host, source ID, connect/join state, thời điểm |
| Callback | Callback name, simulator/action, received/admitted sequence |
| Mapping | Mongo history/failure document ID, category, parse status, error code nếu có |
| Identity | eventId/failureId, generation, sequence, payload hash/size |
| Tenant | Payload CompanyId, configured tenant mode, actual stored companyId |
| Mongo | Collections, validator/index list, query result |
| Queue | Capacity/timeout, admitted/dropped count, saturation evidence |
| Reconnect | Generation trước/sau, join evidence, event sau recovery |
| Shutdown | Stop time, drained count, remaining count/timeout nếu có |
| Privacy | Redacted document/log/metric sample |
| Regression | Raw-log history và checkpoint trước/sau |

Không đưa full raw payload, token, JWT, IP hoặc dữ liệu cá nhân chưa duyệt vào test report. Có thể dùng hash, eventId, source sequence và screenshot/query đã che dữ liệu.

## 17. Tiêu chí pass Sprint 2

Sprint 2 được xem là đạt khi:

- TC-CFG-001, TC-CONN-001, TC-CONN-002 và ít nhất một callback thực tế từ Training pass.
- Đủ 11 callback được test bằng runtime evidence; callback chưa có producer phải có trạng thái `BLOCKED/N-A` và owner/action rõ ràng.
- Ba callback Scanner typed pass về category, tenant, status, privacy và snapshot/activity.
- Tenant unresolved/mismatch/malformed/oversized đi failure đúng schema, không crash consumer.
- History AppHub có Schema V2 và không có file context giả/checkpoint giả.
- Mongo retry duplicate giữ nguyên identity; index/schema validator được tạo idempotent.
- Channel bounded, FIFO và saturation có evidence.
- Reconnect tạo generation mới, rejoin Monitoring và nhận được event sau recovery.
- Shutdown drain bounded, không treo; event còn trong memory được phản ánh đúng nếu timeout.
- AppHub failure không dừng raw-log; RawLog failure không dừng AppHub khi Mongo vẫn hoạt động.
- Health/telemetry phản ánh connection, callback, queue, mapping và lỗi; không lộ secret/privacy.
- Worker khởi động và chạy trực tiếp thành công với cấu hình Training; toàn bộ evidence manual runtime đã được lưu trước khi chốt UAT.

## 18. Known open items cần ghi trong test report

- Exact payload/casing/type của tám callback opaque.
- Producer/runtime evidence của `receiveClientDeviceConnected` và `receiveClientDeviceDisconnected`.
- DeviceOnline là activity hay snapshot trong từng use case.
- Sensor timeout unit và semantics của các field business.
- ERP service identity, token issuance/rotation và yêu cầu `sessionType=0` trên Training.
- Endpoint Training là multi-tenant hay dedicated single-tenant trong deployment test.
- Capacity, timeout, payload limit và alert threshold sau benchmark thực tế.
- Việc expose HTTP health endpoint hoặc metrics exporter nếu tester cần kiểm tra bằng hệ thống monitoring bên ngoài.
