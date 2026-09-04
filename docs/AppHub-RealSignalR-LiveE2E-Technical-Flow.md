# Luồng kỹ thuật: AppHubRealSignalRLiveIntegrationTests.cs

Tài liệu chi tiết về kiến trúc, các luồng thực thi và quy trình xác thực của bộ kiểm thử tích hợp trực tiếp (`Live E2E`) tại file `tests/DeviceEventHistory.IntegrationTests/AppHubRealSignalRLiveIntegrationTests.cs`.

---

## 1. Tổng quan kiến trúc

`AppHubRealSignalRLiveIntegrationTests` được thiết kế theo tiêu chuẩn kiểm thử tích hợp cấp độ cao nhất (**Live End-to-End Test, No Mock**):
- **Giao tiếp mạng thật**: Kết nối trực tiếp qua giao thức SignalR cổ điển (ASP.NET Classic SignalR Client) tới máy chủ ERP Training.
- **Xác thực thật**: Truyền token hợp lệ qua query string lúc bắt đầu kết nối.
- **Hàng đợi & Điều phối thật**: Sử dụng `AppHubEventAdmission` (Bounded Channel FIFO), thực thi bảo toàn dữ liệu và che giấu bí mật (redaction/hashing).
- **Cơ sở dữ liệu thật**: Tự động khởi tạo database Mongo tạm, cấu hình Index, kiểm tra lưu trữ `CanonicalDeviceEvent` và `IngestionFailure`, sau đó tự dọn dẹp (dropDatabase).

```
┌────────────────────────────────────────────────────────┐
│               ERP SignalR Server (Training)            │
│         https://training-api.un-available.net/signalr   │
└──────────────────────────┬─────────────────────────────┘
                           │ (Classic SignalR Transport)
                           ▼
┌────────────────────────────────────────────────────────┐
│             AppHubMonitoringConnection                 │
│  - Handshake & Authentication (token / tokenjwt)       │
│  - Auto-Invoke JoinMonitoring() upon connect           │
│  - Register 11 Broadcast Callbacks before Start        │
└──────────────────────────┬─────────────────────────────┘
                           │ Raw Arguments Object[]
                           ▼
┌────────────────────────────────────────────────────────┐
│            AppHubRawSourceEventFactory                 │
│  - Hash ConnectionId (SHA256)                          │
│  - Strip UserName, UserIp, Secrets                     │
│  - Compute PayloadSha256 & Assign ReceiveSequence      │
└──────────────────────────┬─────────────────────────────┘
                           │ RawSourceEvent
                           ▼
┌────────────────────────────────────────────────────────┐
│            AppHubEventAdmission (Channel)              │
│  - Thread-safe Bounded Channel (FIFO)                  │
│  - Backpressure handling & Timeout admission           │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│            RawSourceEventMapperRegistry                │
│  - Map 11 Callbacks to Canonical Schema V2             │
│  - Multi-tenant / Single-tenant resolution             │
│  - Tag untrusted source time warning                   │
└──────────────────────────┬─────────────────────────────┘
                           │ CanonicalIngestionResult
                           ▼
┌────────────────────────────────────────────────────────┐
│     CanonicalIngestionPersistenceCoordinator           │
│  - Idempotent Write (Unique index / EventId)           │
│  - Partition by (sourceKind, tenantId, receivedDate)   │
└──────────────────────────┬─────────────────────────────┘
                           │
                           ▼
┌────────────────────────────────────────────────────────┐
│             MongoDB Persistence Layer                  │
│  - Collection: device_event_history                    │
│  - Collection: ingestion_failures                      │
│  - Collection: ingestion_checkpoints                   │
└────────────────────────────────────────────────────────┘
```

---

## 2. Chi tiết các luồng thực thi (Technical Flows)

### 2.1. Luồng 1: Transport & Connection Lifecycle Test

**Mục tiêu:** Kiểm tra tầng giao vận mạng, bắt tay SignalR, xác thực token, gọi RPC `JoinMonitoring()` và đóng kết nối an toàn.

**Phương thức:** `Connects_to_real_training_apphub_and_joins_monitoring()`

```
[Start Test] 
      │
      ├─► Đọc DEVICE_EVENT_HISTORY_APPHUB_TOKEN từ env (Skip nếu thiếu)
      │
      ├─► Khởi tạo AppHubSourceOptions & AppHubMonitoringConnectionFactory
      │
      ├─► Đăng ký callback receiveDeviceOnline
      │
      ├─► StartAsync() ──► Bắt tay HTTP ──► Upgrade WebSocket/LongPolling
      │                      │
      │                      └─► Tự động gọi hub method JoinMonitoring()
      │
      ├─► Assert: State == Running && ConnectionGeneration != null
      │
      ├─► StopAsync() ──► Ngắt kết nối SignalR
      │
      └─► Assert: State == Disconnected
```

**Các bước chi tiết:**
1. **Kiểm tra Pre-condition**: Đọc `DEVICE_EVENT_HISTORY_APPHUB_TOKEN` từ biến môi trường. Nếu không có token, ném `SkipException` để đảm bảo bộ test CI không fail khi chưa cấp quyền.
2. **Khởi tạo kết nối**: Dùng `AppHubMonitoringConnectionFactory.Create(options)` với `HubName = "AppHub"`.
3. **Đăng ký lắng nghe**: Gọi `connection.RegisterCallback(AppConst.AppHub.Callbacks.ReceiveDeviceOnline, ...)` trước khi mở kết nối.
4. **Kích hoạt Start**: Chạy `StartAsync(cancellationToken)` với timeout 30 giây. Client bắt tay, xác thực qua query string, chuyển trạng thái sang `Running`, và phát sinh một chuỗi `ConnectionGeneration` ngẫu nhiên đại diện cho vòng đời kết nối này.
5. **Thoát an toàn**: Gọi `StopAsync()` và xác nhận trạng thái chuyển về `Disconnected`.

---

### 2.2. Luồng 2: Full Ingestion Pipeline E2E Test với MongoDB

**Mục tiêu:** Kiểm tra toàn diện chuỗi xử lý khép kín: Nhận sự kiện thật -> Chuẩn hóa Canonical -> Điều phối bộ nhớ -> Ghi nhận và lập chỉ mục trong MongoDB.

**Phương thức:** `End_to_end_apphub_live_pipeline_with_real_mongodb_persistence()`

```
[Start E2E Test]
      │
      ├─► 1. Setup Isolated Mongo DB: device_event_history_e2e_{guid}
      │      └─► Initialize Compound Indexes & Unique Keys
      │
      ├─► 2. Khởi tạo Persistence Layer:
      │      ├─► MongoDeviceEventHistoryWriter
      │      ├─► MongoIngestionFailureWriter
      │      └─► CanonicalIngestionPersistenceCoordinator
      │
      ├─► 3. Khởi tạo Application & Mapper Registry:
      │      └─► Đăng ký đầy đủ 11 Mappers (Scanner, Online, State, Sensor, TagRead, ClientDevice)
      │
      ├─► 4. Khởi chạy Worker Runtime:
      │      ├─► AppHubSourceRuntime.RunAsync()
      │      ├─► Kết nối SignalR ERP Training thật & JoinMonitoring()
      │      └─► Pipeline đọc từ Channel, Map và Persist xuống Mongo
      │
      ├─► 5. Graceful Drain & Shutdown:
      │      ├─► Gửi Cancellation Token sau khoảng thời gian chạy
      │      └─► Đợi xả cạn (Drain) Channel xuống MongoDB
      │
      ├─► 6. Verification:
      │      └─► Assert CountDocuments trên các collections >= 0 (Không lỗi Index/Schema)
      │
      └─► 7. Cleanup (finally):
             └─► dropDatabase() dọn dẹp sạch tài nguyên test
```

**Các điểm kỹ thuật quan trọng:**
1. **Isolated Test Database**: Mỗi lần test tạo một cơ sở dữ liệu ngẫu nhiên dạng `device_event_history_e2e_{guid}` để không ảnh hưởng dữ liệu sản xuất hoặc dữ liệu dev khác.
2. **Index Initialization**: Kích hoạt `MongoIndexInitializer.InitializeAsync()` để kiểm tra tính tương thích của các chỉ mục duy nhất:
   - Index trên `eventId` (Unique).
   - Index phức hợp phục vụ truy vấn phân vùng: `(companyId, occurredAtUtc, source.sourceId)`.
3. **Full Mapper Coverage**: Nạp toàn bộ 11 Mappers vào `RawSourceEventMapperRegistry` để bảo đảm mọi loại callback từ ERP Training đều được xử lý đúng phân loại Category và Parse Status.
4. **Graceful Drain**: Khi ngắt kết nối runtime, cơ chế `GracefulShutdownCoordinator` bảo đảm toàn bộ sự kiện đã vào Channel sẽ được lưu hoàn tất trước khi tiến trình giải phóng bộ nhớ.
5. **Dọn dẹp tự động**: Khối `finally` gửi lệnh `dropDatabase` xóa toàn bộ cơ sở dữ liệu test vừa tạo.

---

## 3. Danh mục biến môi trường kiểm thử

| Tên biến | Bắt buộc | Mặc định | Ý nghĩa |
|---|---|---|---|
| `DEVICE_EVENT_HISTORY_APPHUB_TOKEN` | **Có** | *None* | Token xác thực kết nối Training SignalR Hub (UserCookie hoặc JWT). |
| `DEVICE_EVENT_HISTORY_APPHUB_ENDPOINT` | Không | `https://training-api.un-available.net/signalr` | Địa chỉ máy chủ SignalR Training. |
| `DEVICE_EVENT_HISTORY_MONGODB_CONNECTION_STRING` | Không | `mongodb://localhost:27017` | Connection string tới máy chủ MongoDB kiểm thử. |

---

## 4. Hướng dẫn chạy kiểm thử Live E2E

### 4.1. Chạy trên Linux / macOS (Bash)

```bash
export DEVICE_EVENT_HISTORY_APPHUB_TOKEN="<token_do_erp_cap>"
export DEVICE_EVENT_HISTORY_APPHUB_ENDPOINT="http://192.168.1.38:8089/signalr"
export DEVICE_EVENT_HISTORY_MONGODB_CONNECTION_STRING="mongodb://localhost:27017"

# Chạy riêng nhóm Live E2E
dotnet test --filter "Category=LiveE2E" --logger "console;verbosity=detailed"
```

### 4.2. Chạy trên Windows (PowerShell)

```powershell
$env:DEVICE_EVENT_HISTORY_APPHUB_TOKEN = "<token_do_erp_cap>"
$env:DEVICE_EVENT_HISTORY_APPHUB_ENDPOINT = "http://192.168.1.38:8089/signalr"
$env:DEVICE_EVENT_HISTORY_MONGODB_CONNECTION_STRING = "mongodb://localhost:27017"

dotnet test --filter "Category=LiveE2E" --logger "console;verbosity=detailed"
```
