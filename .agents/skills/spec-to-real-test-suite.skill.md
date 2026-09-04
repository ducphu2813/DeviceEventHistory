---
name: spec-to-real-test-suite
description: "Chuyển đổi tài liệu đặc tả test case (Markdown spec/Test plan) thành bộ kiểm thử toàn diện gồm Unit Test (Domain/Mapping/Validation logic), Integration Test (Database/Index/Pipeline) và Real Live E2E Test (Không mock, kết nối trực tiếp hạ tầng/protocol thật)."
---

# Spec to Real Test Suite Workflow

Kỹ năng tự động hóa quy trình phân tích tài liệu đặc tả kiểm thử và tạo lập bộ test hoàn chỉnh 3 tầng (Unit Test, Integration Test, Real Live E2E Test) mà không dùng Mock giả lập luồng mạng.

---

## 1. Nguyên tắc cốt lõi (Iron Laws)

1. **No-Mock Real Live E2E (Không dùng Mock cho E2E)**:
   - Các test thuộc nhóm `Live E2E` phải kết nối trực tiếp đến endpoint thật (SignalR, WebSocket, REST API, Message Queue) và cơ sở dữ liệu thật (MongoDB, PostgreSQL).
   - Không giả lập callback hoặc fake handler trong tầng E2E.
2. **Deterministic Unit Tests (Kiểm thử logic bất biến)**:
   - Tầng Unit Test kiểm tra triệt để: Parsing, Mapping, Redaction/Hashing bí mật, Validation Rules, Tenant Fallback/Mismatch, Time Contract.
   - 100% độc lập với I/O mạng hoặc external state.
3. **Isolated Integration Environment**:
   - Integration tests sử dụng cơ sở dữ liệu tạm thời (ví dụ: `database_test_{guid}`) và tự động thực thi `dropDatabase` trong khối `finally`.
4. **Graceful Skip on Missing Credentials**:
   - Live E2E tests đọc credential/token từ biến môi trường. Nếu không có biến môi trường, test phải throw `SkipException` (hoặc tương đương) để không làm vỡ pipeline CI/CD mặc định.

---

## 2. Quy trình 4 bước thực hiện

```
[1. Phân tích Spec & Lập Ma trận Test Cases]
                     ↓
[2. Sinh Unit Test Suite (Logic & Contract)]
                     ↓
[3. Sinh Real Integration & Live E2E Tests]
                     ↓
[4. Xác thực, Chạy Test & Báo cáo Evidence]
```

---

### Bước 1: Phân tích Spec & Lập Ma trận Test Cases

1. **Đọc tài liệu đặc tả** (ví dụ: `docs/*-Testcase.md`):
   - Trích xuất toàn bộ danh mục mã kiểm thử (`TC-*`).
   - Phân loại test case vào 3 tầng:
     - **Tầng Unit Test**: Configuration validation, mapper transformation, regex parsing, data contract constraints.
     - **Tầng Integration Test**: Index initialization, idempotent persistence, storage store updates, checkpoint advancing.
     - **Tầng Live E2E Test**: Network handshake, auth token passing, live broadcast reception, real end-to-end ingestion pipeline.
2. **Lập bảng đối chiếu** (Test Case ID vs Test Class & Method Name).

---

### Bước 2: Sinh Unit Test Suite (Mapping, Logic, Validation)

1. **Khởi tạo test class** (ví dụ: `DetailedTestCasesTests.cs`):
   - Đặt tên test method theo format: `TC_{NHOM}_{STT}_{Mo_ta_ngan_gon}`.
   - Nhóm các test method bằng `#region`.
2. **Phủ đầy đủ các kịch bản ngoại lệ**:
   - Boundary values (0, âm, null, khoảng trắng, chuỗi rỗng).
   - Malformed data (Invalid JSON, corrupt string, missing required keys).
   - Security redaction (Hash secrets bằng SHA256, xóa username/ip/token).
   - Tenant resolution (Single-tenant fallback vs Multi-tenant missing/mismatch error).

---

### Bước 3: Sinh Real Integration & Live E2E Tests

1. **Giao thức và Transport thật**:
   - Dùng Client chuẩn của framework (ví dụ `Microsoft.AspNet.SignalR.Client` cho Classic SignalR, `HttpClient` cho REST API).
   - Kết nối endpoint thật và truyền token thật qua query/header.
2. **Thực thi vòng đời khép kín**:
   - `Setup`: Khởi tạo database tạm, tạo index (`InitializeAsync`).
   - `Execution`: Khởi chạy Worker Runtime / Ingestion Service, lắng nghe sự kiện, lưu trữ qua `PersistenceCoordinator`.
   - `Teardown`: Gửi tín hiệu `CancellationToken` kích hoạt Graceful Drain, xác nhận record đã ghi trong DB, sau đó `dropDatabase`.

---

### Bước 4: Chạy kiểm thử & Xác thực

1. **Chạy Unit Test**:
   ```bash
   dotnet test --filter "Category!=LiveE2E"
   ```
2. **Chạy Live E2E Test** (khi có credential thật):
   ```bash
   DEVICE_EVENT_HISTORY_APPHUB_TOKEN="<token>" \
   DEVICE_EVENT_HISTORY_APPHUB_ENDPOINT="<endpoint>" \
   dotnet test --filter "Category=LiveE2E" --logger "console;verbosity=detailed"
   ```
3. **Cập nhật tài liệu Technical Flow** giải thích chi tiết kiến trúc và luồng dữ liệu cho nhóm kiểm thử.
