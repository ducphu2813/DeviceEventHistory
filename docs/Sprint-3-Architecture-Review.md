# Đánh Giá Kiến Trúc Sprint 3: Device Event Daily Statistics

Tài liệu này tổng hợp phân tích kiến trúc, phát hiện các điểm nghẽn nghiêm trọng (P0/P1/P2) trong thiết kế (`Sprint-3-Design.md`) và lược đồ cơ sở dữ liệu (`Sprint-3-Schema.md`), đồng thời đưa ra các đề xuất khắc phục cụ thể trước khi triển khai (Implementation Phase).

---

## I. Tổng quan đánh giá (Executive Summary)

Thiết kế Sprint 3 đưa ra cơ chế đọc bất đồng bộ từ MongoDB `device_event_history` sang SQL Server read-model (`device_stats.*`) nhằm giảm tải cho Mongo và phục vụ báo cáo/thống kê hàng ngày.

### Điểm mạnh
1. **Phân tách trách nhiệm (Separation of Concerns):** SQL Server gặp sự cố không gây nghẽn luồng ghi log/AppHub vào MongoDB.
2. **Nguyên tắc Idempotency:** Sử dụng bảng `ProcessedEvent` kết hợp transaction nội bộ tại SQL Server để chống trùng lặp dữ liệu.
3. **Chiến lược Rebuild rõ ràng:** Quản lý theo `ProjectionVersion`, cho phép tính toán song song phiên bản mới mà không ảnh hưởng bảng đang phục vụ.
4. **Bảo toàn dữ liệu:** Không sao chép payload thô hoặc token bảo mật sang SQL Server.

### Các lỗ hổng kiến trúc cần khắc phục
Có **2 lỗi nghiêm trọng mức P0**, **2 lỗi mức P1** và **1 lỗi mức P2** cần được thống nhất phương án xử lý trước khi lập kế hoạch lập trình chi tiết.

---

## II. Phân tích chi tiết các vấn đề kiến trúc

### 1. [P0] Nguy cơ mất sự kiện vĩnh viễn do lệch thời gian commit Mongo (Cursor Skew)

- **Vị trí tài liệu:** `Sprint-3-Design.md` (Mục 11: Cursor & Checkpoint).
- **Cơ chế hiện tại:** Cursor dựa trên điều kiện `persistedAtUtc > LastPersistedAtUtc` (hoặc cùng timestamp và `eventId > LastEventId`).
- **Nguyên nhân gốc rễ (Root Cause):**
  - Hệ thống ingestion có nhiều luồng ghi đồng thời (Raw-log pipeline và AppHub pipeline).
  - MongoDB không có cơ chế gán số thứ tự tuần tự toàn cục (Global Monotonic Sequence) khi chèn dữ liệu độc lập.
  - Giá trị `persistedAtUtc` được sinh ở tầng ứng dụng hoặc trước khi MongoDB ghi đĩa thành công.
- **Kịch bản lỗi (Race Condition):**
  1. Luồng A tạo Sự kiện 1 với `persistedAtUtc = 10:00:00.100`, nhưng bị nghẽn mạng/IO nên chưa hoàn tất ghi vào Mongo.
  2. Luồng B tạo Sự kiện 2 với `persistedAtUtc = 10:00:00.200`, ghi thành công vào Mongo.
  3. Worker đọc Mongo thấy Sự kiện 2, cập nhật checkpoint trong SQL Server lên mốc `10:00:00.200`.
  4. Luồng A hoàn tất ghi Sự kiện 1 vào Mongo.
  5. Ở chu kỳ kế tiếp, Worker truy vấn `persistedAtUtc > 10:00:00.200`. Sự kiện 1 bị bỏ qua vĩnh viễn.
- **Đề xuất giải pháp:**
  - **Phương án khả thi nhất (Bounded Overlap Window):** Worker mỗi chu kỳ luôn đọc lùi lại một khoảng an toàn (ví dụ: `LastPersistedAtUtc - 5 phút`). Tận dụng bảng `device_stats.ProcessedEvent` với khóa chính `(ProjectionName, ProjectionVersion, EventId)` để loại bỏ trùng lặp khi ghi vào SQL.
  - **Phương án nâng cao:** Sử dụng MongoDB Change Stream kết hợp `resumeToken` (yêu cầu cụm Mongo Replica Set).

---

### 2. [P0] Thiếu Fencing Token cho Distributed Lease (Nguy cơ ghi đè dữ liệu cũ)

- **Vị trí tài liệu:** `Sprint-3-Design.md` (Mục 13) và `Sprint-3-Schema.md` (Bảng `ProjectionCheckpoint`).
- **Cơ chế hiện tại:** Bảng `ProjectionCheckpoint` chỉ sử dụng `LeaseOwner` và `LeaseExpiresAtUtc`.
- **Nguyên nhân gốc rễ:**
  - Môi trường phân tán gặp hiện tượng Garbage Collection (GC) pause hoặc nghẽn mạng tạm thời khiến Worker mất quyền mà không tự biết.
  - Không có mã định danh thế hệ (Fencing Token / Epoch) đi kèm mỗi giao dịch ghi.
- **Kịch bản lỗi:**
  1. Worker 1 lấy lease, chuẩn bị ghi một batch lớn nhưng bị tạm dừng (Process freeze/GC).
  2. Lease của Worker 1 hết hạn (`LeaseExpiresAtUtc`).
  3. Worker 2 lấy lease mới, xử lý và ghi thành công dữ liệu cùng checkpoint mới vào SQL.
  4. Worker 1 phục hồi, tiếp tục thực hiện câu lệnh ghi đã chuẩn bị từ trước với dữ liệu cũ, đè lên kết quả đúng của Worker 2.
- **Đề xuất giải pháp:**
  - Bổ sung cột `LeaseEpoch bigint NOT NULL DEFAULT 0` vào bảng `device_stats.ProjectionCheckpoint`.
  - Mỗi lần gia hạn hoặc chiếm Lease thành công, tăng `LeaseEpoch = LeaseEpoch + 1`.
  - Mọi Transaction ghi dữ liệu thống kê, cập nhật snapshot hoặc checkpoint đều phải kiểm tra điều kiện `WHERE LeaseOwner = @CurrentOwner AND LeaseEpoch = @CurrentEpoch AND LeaseExpiresAtUtc > SYSUTCDATETIME()`. Nếu không khớp, hủy bỏ toàn bộ Transaction.

---

### 3. [P1] Lan truyền trạng thái sang các ngày kế tiếp khi có sự kiện đến muộn (State Propagation)

- **Vị trí tài liệu:** `Sprint-3-Design.md` (Mục 12) và `Sprint-3-Schema.md` (Bảng `DeviceDailySnapshot`, `DeviceStateCursor`).
- **Cơ chế hiện tại:** Khi có sự kiện thay đổi trạng thái bị trễ, tài liệu chỉ định nghĩa tính toán lại (Reconciliation) cục bộ cho `StatisticsDate` của sự kiện đó.
- **Nguyên nhân gốc rễ:**
  - Trạng thái thiết bị là một chuỗi liên tục (Continuous Timeline). Trạng thái kết thúc (Closing State) của ngày $D$ chính là trạng thái mở đầu (Opening State) của ngày $D+1$.
- **Kịch bản lỗi:**
  1. Ngày $D$ lúc 23:00 có sự kiện `Connected` đến muộn. Trước đó hệ thống đang ghi nhận thiết bị `Disconnected`.
  2. Ngày $D+1$ và $D+2$ thiết bị không phát sinh sự kiện nào mới.
  3. Nếu chỉ tính toán lại ngày $D$, ngày $D+1$ và $D+2$ vẫn giữ opening state là `Disconnected`, dẫn đến `OnlineSeconds`, `OfflineSeconds` và điểm sức khỏe (Health Score) của các ngày sau hoàn toàn sai lệch.
- **Đề xuất giải pháp:**
  - Mở rộng phạm vi tính toán lại (Dirty Range): Xác định khoảng thời gian từ ngày phát sinh sự kiện muộn đến thời điểm có sự kiện chuyển đổi trạng thái tiếp theo hoặc đến trạng thái hiện tại (`Current State Edge`).
  - Cập nhật lại toàn bộ `DeviceDailySnapshot` trong dải ngày bị ảnh hưởng và điều chỉnh `DeviceStateCursor` tương ứng.

---

### 4. [P1] Yêu cầu Reconcile chưa được lưu trữ bền vững (Non-durable Dirty Markers)

- **Vị trí tài liệu:** `Sprint-3-Design.md` (Mục 12) và `Sprint-3-Schema.md`.
- **Cơ chế hiện tại:** Nhắc đến việc "đánh dấu các bản ghi cần reconciliation", nhưng thiếu bảng lưu trữ vật lý trong SQL DDL.
- **Nguyên nhân gốc rễ:**
  - Đánh dấu trạng thái cần tính lại trên bộ nhớ tạm (In-memory) sẽ bị mất hoàn toàn nếu tiến trình Worker khởi động lại hoặc crash.
  - Cơ chế quét lùi định kỳ (Rolling Lookback, ví dụ 3 ngày) sẽ bỏ sót các sự kiện trễ hơn 3 ngày.
- **Đề xuất giải pháp:**
  - Bổ sung bảng vật lý `device_stats.ReconciliationRequest`:
    - `RequestId` (bigint / uniqueidentifier)
    - `ProjectionName`, `ProjectionVersion`
    - `CompanyId`, `DeviceId`, `StateType`
    - `FromStatisticsDate`, `ToStatisticsDate`
    - `Status` (Pending, Processing, Completed, Failed)
    - `AttemptCount`, `RequestedAtUtc`, `CompletedAtUtc`
  - Ghi nhận yêu cầu Reconcile ngay trong Transaction ghi sự kiện muộn. Tiến trình Scheduler sẽ quét bảng này để xử lý tuần tự, đảm bảo không bị thất lạc dữ liệu.

---

### 5. [P2] Thiếu kiểm soát phiên bản khi thay đổi Múi giờ thiết bị (Timezone Revision Contract)

- **Vị trí tài liệu:** `Sprint-3-Schema.md` (Bảng `DeviceDimension`, `DeviceDailySnapshot`).
- **Nguyên nhân gốc rễ:**
  - Phân đoạn ngày (`StatisticsDate`, `BucketStartAtUtc`, `BucketEndAtUtc`) phụ thuộc hoàn toàn vào múi giờ của thiết bị/địa điểm.
  - Khi thiết bị đổi cấu hình múi giờ (hoặc cập nhật địa điểm), bảng `DeviceDimension` bị cập nhật đè mà không lưu lại mốc thời gian áp dụng (`EffectiveFromUtc`).
- **Đề xuất giải pháp:**
  - Quản lý phiên bản múi giờ hoặc lưu `EffectiveDate` cho cấu hình múi giờ trong `DeviceDimension`.
  - Khi có sự thay đổi múi giờ, tự động kích hoạt tiến trình tạo `ReconciliationRequest` cho các ngày bị ảnh hưởng thay vì chỉ ghi đè thuộc tính tĩnh.

---

## III. Bảng tổng hợp hành động (Action Item Matrix)

| Mức độ | Vấn đề | Rủi ro | Phương án giải quyết bắt buộc |
| :--- | :--- | :--- | :--- |
| **P0** | Skew cursor Mongo | Mất sự kiện vĩnh viễn | Áp dụng Bounded Overlap Window khi đọc Mongo + Dedup bằng `ProcessedEvent`. |
| **P0** | Thiếu Fencing Token | Worker cũ ghi đè dữ liệu mới | Thêm `LeaseEpoch` vào `ProjectionCheckpoint` và kiểm tra trong mọi Transaction ghi SQL. |
| **P1** | State Propagation | Sai lệch thời lượng online/offline nhiều ngày | Reconcile dải ngày từ mốc sự kiện muộn đến mốc transition kế tiếp hoặc current edge. |
| **P1** | Marker Reconcile chưa bền vững | Mất vết cần tính lại khi restart | Bổ sung bảng `device_stats.ReconciliationRequest` vào CSDL SQL Server. |
| **P2** | Quản lý đổi Múi giờ | Lệch khung thời gian ngày (Bucket) | Bổ sung phiên bản/mốc hiệu lực cho múi giờ và kích hoạt reconcile khi có thay đổi. |

---

## IV. Kết luận và Khuyến nghị

Kiến trúc tổng thể của Sprint 3 có cấu trúc module và phân tách tầng lưu trữ hợp lý. Tuy nhiên, để đảm bảo tính nhất quán dữ liệu (Data Consistency) và độ tin cậy trong môi trường phân tán:

1. **Không tiến hành lập trình ngay** khi các vấn đề P0 và P1 chưa có giải pháp kỹ thuật chính thức.
2. Các bên liên quan cần rà soát và thống nhất phương án xử lý 5 điểm nêu trên để cập nhật vào `Sprint-3-Design.md` và `Sprint-3-Schema.md`.
